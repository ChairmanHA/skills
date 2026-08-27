# Large Waveform Streaming Intent Split Plan

## 结论

这套交互方向总体合理，但需要加三条工程约束，才能既符合业务预期，也不把系统带回“强制全量生成”的老问题：

1. 可以去掉显式的 `Adjust Params` 按钮，但不能去掉 `Cancel / Close` 退出入口。
2. `是否显示流式播放选项` 不能只看波形大小，必须同时看 `sampleRate <= STREAMING_SAMPLE_RATE_MAX`。
3. `保存波形一律完整生成` 是合理的，但前提是“保存”不能再复用当前下载缓冲区，尤其是 Digital 下载路径一旦改成按 `SymbolLength` 生成片段后，保存必须走独立的全量导出任务。

## 背景判断

当前大波形场景里其实混了三种不同意图：

1. 立即截断下发到设备播放
2. 切到 Streaming 业务做连续播放
3. 保存完整波形用于离线分析/导出

这三种意图对应的最优策略并不相同：

- Ramp：超限时通常是高采样率驱动，Streaming 上限 62.5M 无法承接。
- OFDM：在合适参数下，完整波形可以直接交给 Streaming。
- Digital：下载意图不应该再固定生成完整 `2^PN`，但保存意图又必须保留完整序列。

因此应该按“意图 + 可用能力”做分流，而不是继续用一个统一的“波形太大”提示去覆盖所有路径。

## 是否合理

### 你提出的主干方案是合理的

可以采用下面这条总规则：

- 如果流模式支持：给出 `截断下发` 和 `完整生成后流式播放` 两个动作
- 如果流模式不支持：只给 `截断下发`
- 保存波形：一律按完整生成

### 需要补的三个约束

#### 1. 去掉 `Adjust Params` 按钮，不等于去掉取消能力

最佳实践上，不建议让模态对话框变成“只能选业务动作”的强制决策框。

推荐做法：

- 去掉显式文案 `Adjust Params`
- 保留标准 `Cancel` / 关闭按钮 / Esc 关闭

这样用户关闭弹框后自然回到参数编辑界面，本质上仍然可以继续调参，但不会把“调参”做成一个和“下载/流播”并列的业务动作按钮。

#### 2. `流式播放可用` 的判断必须绑定采样率上限

判定条件至少需要：

- 当前调制支持 Streaming handover
- 当前目标采样率 `<= STREAMING_SAMPLE_RATE_MAX`，即 62.5M

对应当前代码基础：

- Streaming 上限来自 `utils/constants.h` 中的 `STREAMING_SAMPLE_RATE_MAX`
- Analog 公共层已有 `requestGenerateAndStream()` / `onHandoverDataReady()` / `saveToTempWavFile()`，可以继续复用

这意味着：

- Ramp：大波形提示里默认不提供流式选项
- OFDM：只有在 `sampleRate <= 62.5M` 时提供流式选项
- Digital：只有在 `sampleRate <= 62.5M` 时才可能提供流式选项

#### 3. `保存一律完整生成` 需要从“当前缓冲区保存”切到“导出任务保存`

这是整个方案里最关键的工程边界。

当前很多 `saveData()` 逻辑默认直接把 `modulator->data()` 写成 wav。这个前提默认了：

- 当前内存中的 payload 就是你想保存的完整波形

但 Digital 如果改成：

- 下载意图只生成按 `MAXDOWNLOADSIZE` 反推出来的 `SymbolLength`

那么当前 `modulator->data()` 就只是一段“下载片段”，不再是完整序列。

此时如果 `saveData()` 还沿用当前内存缓冲区，保存出来的文件会变成截断版本，和“保存一律完整生成”的新语义冲突。

所以必须明确：

- `保存` 不是 dump 当前播放缓冲区
- `保存` 是一个独立的“全量导出任务”

## 推荐交互矩阵

### 1. Ramp

#### 能力判断

- 超限时通常由高 `Span` / 高 `sampleRate` 驱动
- 这类场景 Streaming 通常无法承接，因为 sampleRate 往往高于 62.5M

#### 提示动作

- 主动作：`Generate And Trim For Download`
- 次动作：`Cancel`

#### 不提供

- `Full Generate & Stream`

#### 保存语义

- 用户点击保存时，继续按完整波形生成并保存
- 不受下载截断策略影响

### 2. OFDM

#### 能力判断

- `sampleRate <= 62.5M`：允许 Streaming
- `sampleRate > 62.5M`：不允许 Streaming

#### 提示动作

当 `estimatedSize > MAXDOWNLOADSIZE` 且 `sampleRate <= 62.5M`：

- `Generate And Trim For Download`
- `Generate Full Waveform And Stream`
- `Cancel`

当 `estimatedSize > MAXDOWNLOADSIZE` 且 `sampleRate > 62.5M`：

- `Generate And Trim For Download`
- `Cancel`

#### 保存语义

- 用户点击保存时，始终保存完整 OFDM 波形
- OFDM API 没有长度裁剪参数，因此保存与流播都仍走完整生成

### 3. Digital

#### 能力判断

- `sampleRate <= 62.5M`：可以考虑提供完整生成后流式播放
- `sampleRate > 62.5M`：不提供流式播放

但 Digital 还要多一层判断：

- 下载意图不能再默认生成完整 `2^PN`
- 必须优先按 `MAXDOWNLOADSIZE` 计算“设备可下载的最大片段长度”

#### 提示动作

当 `estimatedFullSize > MAXDOWNLOADSIZE` 且 `sampleRate <= 62.5M`：

- `Generate Download Segment`
- `Generate Full Sequence And Stream`
- `Cancel`

当 `estimatedFullSize > MAXDOWNLOADSIZE` 且 `sampleRate > 62.5M`：

- `Generate Download Segment`
- `Cancel`

#### 保存语义

- 用户点击保存时，始终走 `FullExport`
- 不复用当前下载片段

## Digital 的核心改法

### 当前问题

当前 Digital 生成路径把算法库的 `SymbolLength` 固定写成完整 PN 长度：

- `GenerateDigitalModWaveform(..., pow(2, params.PN), ...)`

这会导致：

- 即使用户只是想下发 125MB RAM payload，也要先生成完整 `2^PN`
- 在大 PN、大 oversample 下会带来数百 MB 到 GB 级的无意义内存和等待成本

### 目标改法

#### 下载路径

新增一个“下载意图长度计算”函数，例如：

- `computeDigitalSymbolLengthForDownload(const DigitalModParams &params, qint64 maxBytes)`

它的职责是：

1. 先算当前 profile 下每个 symbol 需要多少字节
2. 再按 `MAXDOWNLOADSIZE` 反推最大可下发 `SymbolLength`
3. 把这个 `SymbolLength` 直接传给 `GenerateDigitalModWaveform`

即：

- 不再先生成完整序列
- 直接生成“下载所需片段”

#### 全量流播路径

如果用户明确选择 `Generate Full Sequence And Stream`，则仍允许：

- 按完整 `2^PN` 生成
- 生成完成后交给 Streaming handover

但这里应加一个实现注意事项：

- 这条路径仍然是“完整生成后再 handover”，所以内存成本依旧存在
- 因此建议后续再增加一个交互保护阈值，避免极端大序列把 UI 拖死

#### 全量保存路径

保存必须始终走：

- `FullExport`

不能走：

- 当前下载片段的缓冲区保存

## 推荐的意图模型

建议单独引入一个生成意图枚举：

- `DownloadTrimmed`
- `StreamFull`
- `FullExport`

这样三条路径在工程语义上完全分开：

- `DownloadTrimmed`：面向设备 RAM，Digital 允许按片段长度直接生成
- `StreamFull`：面向 Streaming，要求 sampleRate <= 62.5M
- `FullExport`：面向保存/分析，始终生成完整波形

## 公共交互建议

现有 `showLargeWaveformPrompt()` 是：

- `Adjust Params`
- `Generate And Trim Data`
- 可选 `Generate & Stream`

建议后续升级为“按能力动态拼按钮”的通用 helper，例如：

- `Cancel`
- `Generate And Trim For Download`
- `Generate Full Waveform And Stream`

并支持按 modulation / sampleRate / intent 选择性展示按钮。

## 建议的实施顺序

### 阶段 1

统一文案与弹框能力模型：

- 去掉 `Adjust Params` 显式按钮
- 改成 `Cancel`
- 增加“是否允许 StreamFull”的判定 helper

### 阶段 2

先改 Ramp / OFDM：

- Ramp：只保留 `TrimDownload`
- OFDM：按 sampleRate 决定是否显示 `StreamFull`

### 阶段 3

单独改 Digital：

- 引入 `DownloadTrimmed` / `StreamFull` / `FullExport`
- 下载路径改为按 `MAXDOWNLOADSIZE` 反算 `SymbolLength`
- 保存路径改为独立全量导出，不再复用当前 payload

## 最终判断

你的方向是对的，而且比“保留 Adjust Params / 对所有调制一视同仁”更接近真实用户意图。

但要把它做成一个长期稳定的方案，必须同步接受下面这三个结论：

1. Ramp 不应该出现流式播放选项
2. OFDM / Digital 的流式播放选项必须绑定 `sampleRate <= 62.5M`
3. `保存一律完整生成` 意味着保存路径必须和下载路径彻底解耦，尤其是 Digital