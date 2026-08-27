# Digital Save IQ Full Export Progress Plan

## 结论

这个需求可以在当前 Digital 代码基线上直接落地，不需要把系统拉回旧的 handover / streaming 自动切换路径。

推荐实现方式是：

1. 保持当前播放链路不变，继续让 `DigitalModulator` 的常态缓存服务于“预览/播放/trim 下载”。
2. 把 `Save IQ` 明确建模为一个独立的“全量导出任务”，不要复用当前下载缓存，也不要覆盖 `m_complex`。
3. 使用 Qt 标准 `QProgressDialog` 做窗口模态假进度。
4. 后台任务使用仓库已普遍采用的 `QtConcurrent + QFutureWatcher`，而不是再单独维护一条新的常驻线程。
5. 假进度按 `estimatedSize / 8 MB/s` 估时，并在 95% 附近进入明显减速区，完成后直接关闭窗口，不弹成功提示。

## 范围

本次方案只覆盖 Digital Modulation 的 `Save IQ`。

不包含：

- 不改当前 playback trim 交互。
- 不恢复任何自动切换到 Streaming 的路径。
- 不把完整导出结果写回当前 playback cache。
- 不新增自定义复杂进度控件，优先用 Qt 标准控件。

## 本地判断

### Local Hypothesis

当前需求的控制点已经足够局部，最小落点就是：

- `DigitalPanel::onSaveClicked()` 负责拿路径
- `DigitalModulation::saveData()` 负责决定“直接保存”还是“全量导出任务”
- `DigitalModulator::computeWaveformSync()` 已经能按当前参数走一次全量同步生成

因此，一个独立的后台导出任务就可以满足需求，无需改动当前 Digital 的异步预览/播放生成主链路。

### Cheap Check

已经确认两点：

1. `DigitalModulator::computeWaveformSync()` 是现成的全量同步生成入口。
2. 它和当前 `workerLoop()` 一样都会拿 `Analog::waveformGuard`，因此与常态异步生成并发时会在算法调用处串行化，不会把三方生成接口同时打爆。

这意味着 Save IQ 可以单独起后台任务，但不需要再造一套算法互斥机制。

## 当前状态

### 1. Save IQ 入口

当前 UI 已经收敛为：

- `DigitalPanel` 只负责弹保存文件对话框并发出 `requestSaveData(filePath)`
- `DigitalModulation::saveData()` 直接取 `digitalModulator->iqData()` 并写 WAV

### 2. 当前问题

这条链路默认假设“当前缓存就是想导出的完整波形”。

但 Digital 当前已经不是这个语义：

- 常态播放缓存可能是 trimmed 结果
- `Save IQ` 的业务语义却是“导出当前参数对应的完整波形，用于流模式或外部分析”

因此当前直接 dump `iqData()` 会导出截断文件，语义不正确。

### 3. 现有能力

已有能力已经足够：

- `DigitalModulator::hasTrimmedCurrentData()` 可判断当前缓存是否 trimmed
- `DigitalModulator::computeWaveformSync()` 可重新生成完整波形
- `ThemeManager` 可提供亮/暗主题颜色
- 仓库里已有 `QProgressDialog` 使用先例

## 推荐设计

## 1. Save IQ 改成独立导出任务

推荐把 Save IQ 明确拆成两步：

1. UI 线程在文件对话框返回后整理导出请求
2. 后台任务用该请求生成完整波形并写文件

当前请求至少包含：

- `QJsonObject settingsSnapshot`
- `QString filePath`
- `qint64 estimatedSize`

这样可以保证：

- 文件元数据、导出路径和假进度口径在任务启动前就已确定
- 导出结果不需要写回 `DigitalModulator::m_complex`
- 在当前 `QFileDialog::exec()` 同步模态调用路径下，不需要为此再额外公开一套基于 profile 快照的生成接口

## 2. 不复用当前常态 worker 线程

不建议把完整导出塞进 `DigitalModulator::workerLoop()`，原因有三点：

1. 那条线程的职责是“当前参数的常态预览/播放缓存”，不是一次性导出任务。
2. 导出结果不应该写回当前 cache，否则会把 trimmed / preview / playback 语义重新搅混。
3. Save IQ 需要额外承载文件写出、进度对话框、失败收尾，这些和常态生成生命周期不同。

推荐使用：

- `QtConcurrent::run(...)` 执行完整生成 + 写文件
- `QFutureWatcher` 收尾回到 UI 线程
- `QTimer` 在 UI 线程驱动假进度

这比手写 `QObject + QThread` 更轻，且符合当前 analog modulator 普遍使用的后台任务风格。

## 3. 导出结果不要写回 playback cache

这条边界必须明确保住：

- 当前 `digitalModulator->m_complex` 继续只代表当前业务缓存
- Save IQ 的全量 `QVector<qint16>` 只在导出任务内部短暂存在
- 文件写完后立即释放，不长期驻留 UI / Business 层

否则会重新引入 Digital 大波形内存峰值问题。

## 4. 假进度条策略

### 估时口径

按最终确认，直接使用当前标准估算口径：

- `estimatedSize = Digital_EstimatedSize(params)`
- 假设速度为 `8 MB/s`

即：

$$
expectedSeconds = \max(1.0, \frac{estimatedSize}{8 \times 1024 \times 1024})
$$

这里保留 `1.0s` 最小值，避免小文件一闪而过。

### 进度曲线

建议不要简单线性跑到 95%，而是分两段：

1. 在预计时长内，用 ease-out 曲线从 `0 -> 95`
2. 超过预计时长后，缓慢 creep 到 `99`
3. 任务真实完成时，直接置 `100` 并关闭对话框

可用的一个简单模型：

$$
progress_1 = 95 \times \frac{1 - e^{-3 \times x}}{1 - e^{-3}}, \quad x = \min(1, \frac{elapsed}{expectedSeconds})
$$

超时后：

$$
progress_2 = 95 + 4 \times (1 - e^{-\frac{overtime}{\max(1, expectedSeconds \times 0.5)}})
$$

最终：

- 预计时间内逐渐减速逼近 95
- 超时后最多慢慢爬到 99
- 永远不在真实完成前跑满 100

### 为什么这样更稳

因为当前后台任务不只包含“算法生成”，还包含：

- 等待 `waveformGuard`
- 生成完成后的内存拷贝
- WAV 文件写盘

若只做线性 95%，很容易在大文件或磁盘慢时提前跑完，看起来像卡死。

## 5. 进度对话框建议

推荐直接用标准 `QProgressDialog`，但把行为收紧：

- `ApplicationModal`
- 无取消按钮
- 去掉关闭按钮
- 文案强调“正在生成完整波形并导出”
- 完成后直接关闭，不弹成功提示

推荐默认文案：

- 标题：`Save IQ Data`
- 标签：`Generating full waveform for export...`

可选地在标签追加：

- `Expected size: xxx`
- `This may take a while.`

### 为什么不建议提供 Cancel

当前三方生成 API 没有取消能力。

如果给用户一个 `Cancel`：

- UI 取消了，但后台生成仍会继续
- 用户会误以为任务已停，语义是假的

所以除非后续愿意接受“只能取消结果落盘，不能取消底层生成”这种半取消语义，否则当前版本建议完全不提供取消按钮。

## 6. 主题适配建议
主题可以从thememanager中拿到，根据当前是暗色还是亮色，调整 `QProgressDialog` 的样式表，保证它在两种主题下都能有合适的背景和边框颜色。
针对暗色：
 QProgressDialog {
    qproperty-windowOpacity: 0.98;
    background-color: #1D2228;
}
 QProgressDialog QLabel{
    background-color: transparent;
}
 QProgressDialog QProgressBar {
    border: 1px solid #797E87;
    text-align: right;
}
 QProgressDialog QProgressBar::chunk {
    background-color: #00ce00;
    width: 10px;
    margin: 0.5px;
}
针对亮色：
 QProgressDialog {
    qproperty-windowOpacity: 0.98;
    background-color: #ffffff;
}
 QProgressDialog QLabel{
    background-color: transparent;
}
 QProgressDialog QProgressBar {
    border: 1px solid #868178;
    text-align: right;
}
 QProgressDialog QProgressBar::chunk {
    background-color: #00ce00;
    width: 10px;
    margin: 0.5px;
}


## 7. 建议代码形态

为了把改动面控制在最小范围，建议优先局部实现，不额外新建通用框架。

### `digitalmodulation.h` 侧

建议新增：

- 导出任务状态成员
- 进度对话框指针
- 假进度 `QTimer`
- `QFutureWatcher<DigitalFullExportResult>`
- 若需要快路判断，可增加一个“当前缓存是否完整”的 helper

### `digitalmodulation.cpp` 侧

建议新增私有结构：

- `DigitalFullExportRequest`
- `DigitalFullExportResult`

建议新增私有方法：

- `startFullWaveformExport(...)`
- `buildFullExportRequest(...)`
- `runFullWaveformExport(...)`
- `writeWaveformToWav(...)`
- `ensureExportProgressDialog(...)`
- `updateFakeExportProgress()`
- `finishFullWaveformExport(...)`

### `digitalmodulator` 侧

当前实现直接复用现有成员函数 `computeWaveformSync()`。

这里不再额外增加 public static helper，原因是当前 `Save IQ` 入口先经过同步模态的 `QFileDialog::exec()`，用户在文件对话框返回前没有机会继续修改这些调制参数；因此直接使用成员函数即可，没必要为这条链路额外扩展一套公开接口。

需要额外补的是对象生命周期收尾：后台导出任务持有 `this`，因此析构阶段需要等待 `QFutureWatcher` 完成，避免对象先析构而后台任务仍在访问成员。

## 推荐交互时序

### 主流程

1. 用户点击 `Save IQ`
2. UI 拿到文件路径
3. `DigitalModulation` 整理 settings/filePath/estimatedSize 请求
4. 弹出模态 `QProgressDialog`
5. 启动后台任务：完整生成 + WAV 写盘
6. UI 线程 `QTimer` 按假进度曲线刷新进度条
7. 任务完成后：
   - 成功：进度置 100，关闭窗口
   - 失败：关闭窗口，弹错误提示

### 并发保护

导出进行中建议：

- 忽略新的 Save IQ 请求，或直接禁用保存按钮
- 不再重复打开第二个进度框

否则多次点击会形成多个完整导出任务，既浪费内存也没有业务价值。

## 关于“是否允许直接保存当前完整缓存”

### 回答：只有 trimmed / empty / stale 才重生成

- 当前缓存完整且 ready：直接保存，连进度条都免了
- 当前缓存 trimmed 或当前无 ready 数据：走完整导出任务

优点：

- 小波形体验更快
- 更符合之前文档里“完整缓存可直存”的规划

缺点：

- 需要更严格地区分 full vs trimmed vs not-ready
- 逻辑稍微复杂一点

我更推荐口径 B，因为它不牺牲小波形体验；但如果你希望 Save IQ 的产品语义更绝对，口径 A 也成立。

## 失败处理建议

成功时按你的要求：

- 不弹成功提示
- 直接关闭窗口

失败时建议保留显式提示：

- 生成失败：`MessageDialog` 提示导出失败
- 文件写入失败：`MessageDialog` 提示保存失败

否则当前 `saveData()` 的 `bool` 返回值实际上不会被 UI 明确消费，失败很容易变成静默失败。

## 已知边界和风险

### 1. 4GB WAV 上限

当前保存链路仍是普通 WAV / RIFF 语义，相关估算和 header 写入也基本是 32 位上限口径。

因此本次方案默认仍然受当前约束：

- 支持到现有系统认可的 WAV 上限
- 不在这轮顺手扩到 RF64 / W64

如果后续要稳定支持大于 4GB 的完整导出，需要单开一轮格式层改造。


### 2. 与常态异步生成存在串行等待

如果用户在 Save IQ 前刚触发了一次参数改动，常态异步生成可能还在跑。

由于 `waveformGuard` 的存在，导出任务会在算法边界串行等待。这是正确的，但会让假进度起步阶段更像“缓慢启动”。

所以进度曲线从一开始就不应过快冲高。

## 实施顺序建议

1. 先在 `DigitalModulation` 内部完成独立导出任务骨架，不改 panel。
2. 跑通“trimmed 缓存时重新生成完整波形并导出”的主流程。
3. 接入 `QProgressDialog + QTimer` 假进度。
4. 接入主题局部样式。
5. 再决定是否保留“完整缓存直存”的快路。
6. 最后补失败提示和并发保护。

## 验证建议

实现后至少验证下面几条：

1. 小波形 Save IQ：是否仍能快速导出。
2. trimmed 大波形 Save IQ：是否确实重新生成完整波形，而不是导出截断缓存。
3. 导出期间连续点保存按钮：是否只会有一个任务在跑。
4. 导出成功后：是否只关闭进度框，不弹成功提示。
5. 导出失败后：是否有明确错误提示。
6. 亮色 / 暗色主题下进度条是否可读。
7. 导出期间当前播放状态和预览缓存是否未被覆盖。

## 需要你拍板的点

### 1. Save IQ 是不是一律强制重生成？

我推荐：

- 仅当当前缓存 trimmed / not-ready 时重生成
- 当前缓存本来就是完整结果时直接保存

我同意

### 2. 对话框是否完全禁止取消？

我推荐：

- 禁止取消
- 禁止关闭按钮

理由是底层 API 不可取消，给 Cancel 会误导用户。同意

### 3. 模态级别最终选择

最终采用：

- `ApplicationModal`

### 4. 失败是否需要弹错误框？

我建议：

- 成功静默
- 失败显式弹框

### 5. 进度速度常量是否先固定 `8 MB/s`？

最终采用：

- 先固定 `8 MB/s`
