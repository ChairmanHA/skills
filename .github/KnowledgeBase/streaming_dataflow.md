# 流模式（Streaming）数据读取/加载/下发调度机制说明

本文基于当前代码实现，对“Streaming Playback”模式从底层设备接口到 UI 的数据调度链路进行梳理，包含：组件职责、线程模型、参数/属性联动、数据生产与消费（阻塞/唤醒）、设备配置与实时下发、以及 UI 进度反馈。

> 2026-03-31 更新：本文仍主要关注“双线程 + 队列”的数据流本体。`FixedStream / SweepStream` 当前已经通过 `MainWindow` 桥接到 `StreamingBussiness`，且 `fileList` 为空时不再由 workerLoop 内部额外配置 `Mute` 兜底。关于当前桥接实现以及下一步 `在线热切 / RearmSession / HardReboot` 的设计基线，见 `streaming_bridge_rearm_reboot_refactor_plan.md`。

> 2026-11-24 更新：启动、重配、停止与 IQ 队列共用同一个操作代次。Core 等到当前代次的首包成功、失败或停止确认后解除 SCPI 屏障。[实施记录](../TaskLog/2026-11-24_streaming-scpi-operation-completion.md)

> 相关核心文件：
>
>- `src/plugins/htra/streamingbussiness.{h,cpp}`
>- `src/plugins/htra/streamingdatagenerator.{h,cpp}`
>- `src/plugins/htra/streamingpanel.{h,cpp,ui}`
>- `src/plugins/htra/plugin.cpp`（属性创建、业务注册）
>- `src/plugins/core/deviceoperator.cpp`（业务到设备的统一下发封装）
>- `src/plugins/core/idevice.h`（设备模式 Id 常量）
>- `src/plugins/core/panel.h`、`src/plugins/core/fancytabwidget.cpp`（Enabled 开关与业务选择链路）

---

## 1. 总览：端到端数据链路

Streaming 模式整体可以抽象为“两线程+一队列”的生产-消费架构：

- **UI线程（主线程）**：用户操作、属性编辑、文件列表展示、进度显示。
- **生成线程（StreamingDataGenerator::m_workerThread）**：从 WAV 文件读取原始 IQ，按固定帧长组帧并做 IQScale 变换，产出 `DeviceDataRequest`。
- **发送线程（StreamingBussiness::m_senderThread）**：消费者线程，阻塞等待队列数据；拿到一帧后调用 `DeviceOperator::downloadDataRealTime()` 下发到设备；同时按 1s 周期计算播放进度并通知 UI。

数据与控制流（简化）：

```
UI(StreamingPanel) ──(PropertySystem属性值变更/编辑结束)──▶ StreamingBussiness
   ▲                                                       │
   │                                                       │ setEnabled/start/stop
   │                                             StreamingDataGenerator
   │                                                       │ 生成IQ帧 enqueue
   │                                                       ▼
   └──────────────(playbackPosChanged 进度信号)────────── StreamingBussiness(sendData)
                                                           │
                                                           ▼
                                                   DeviceOperator::downloadDataRealTime
                                                           │
                                                           ▼
                                                      Core::IDevice::downloadDataRealTime
```

---

## 2. 关键组件职责

### 2.1 `StreamingPanel`（UI 层）

- 负责 UI 元素：Enabled 开关、SampleRate/IQScale 编辑按钮、Load/Unload/Remove、FileList 列表、TotalFiles/TotalSamples/TotalDuration/PlaybackPos 等显示。
- 通过 **PropertySystem** 将 UI 与业务参数绑定：
  - `Streaming_SampleRate`（double）
  - `Streaming_IQScale`（double）
  - `Streaming_FileList`（QStringList）
- 通过 `onFileListPropertyChanged()`：
  - 清空并重建列表 UI；
  - 用 `Utils::WavHeader` 校验 WAV 并统计每个文件的 samples；
  - 更新 TotalFiles / TotalSamples / TotalDuration。
- 通过 `onPlaybackPosChanged(double)` 更新 PlaybackPos（百分比字符串）。

### 2.2 `StreamingBussiness`（业务调度层）

- 主线程接收编辑、启动和停止，按代次提交文件、公共设备参数和 carrier plan 快照。
- sender 线程执行设备配置及实时下发。启动或重配成功以首包为准，停止以当前设备 I/O 已结束为准。
- 当前代次的结果在主线程发布，并进入 Core 操作完成判定。失败先发布错误，再结束等待。

### 2.3 `HTRA::StreamingDataGenerator`（数据生产层）

- `m_workerThread` 获取当前文件列表、IQScale 和采样率的快照，打开普通 WAV 或 IQS-WAV，按文件顺序组帧。
- 通过 `DataSender::enqueueRequest()` 提交 IQ 数据、采样率和实际源点数。无法打开有效源或读取数据失败时，同一队列传递错误。

### 2.4 `HTRA::DataSender`（队列与背压控制）

- `QQueue<DeviceDataRequest>` 最多容纳 10 个块。生产者在队列满时阻塞，消费者在队列为空时阻塞。
- `reset(revision)` 清空队列并唤醒生产者、消费者。过期生产者停止入队，过期消费者退出下发循环。

### 2.5 设备接口层（从 Business 到硬件）

- `Core::DeviceOperator` 封装了对 `Core::IDevice` 的调用，并在每次调用前校验业务是否 active：
  - `configuration(input, writeback, errorMessage)`
  - `downloadDataRealTime(int16_t *data, uint32_t points, double sampleRate, ...)`
- `Core::Constants` 定义设备模式 Id：
  - `DEVICE_MODE_STREAM` 等。

---

## 3. 属性系统（PropertySystem）与参数联动

### 3.1 属性创建位置

在 `src/plugins/htra/plugin.cpp` 的 `Plugin::initialize()` 中创建 Streaming 相关属性：

- `Streaming_SampleRate`：`NumericProperty<double>`，并设置单位为 Frequency。
- `Streaming_IQScale`：`NumericProperty<double>`。
- `Streaming_FileList`：`ContainerProperty<QStringList>`。

### 3.2 UI ↔ 属性 ↔ Business ↔ Generator 的联动方式

#### SampleRate / IQScale

- UI：`PropertyBindingManager` 将按钮绑定到属性。
- UI：`beginEditing` 时弹出触控数字键盘；键盘结束后属性触发 `editingFinished`。
- Business（`StreamingBussiness`）：监听属性 `editingFinished`：
  - 调用 `setSampleRate()` 或 `setIqScale()` 保存编辑值。运行中提交新代次。
- SCPI 通过业务远程编辑入口提交同样的值，沿用界面的参数归一化。

#### FileList

- UI：Load/Unload/Remove/拖拽排序会 `fileListProperty->setValue(QStringList)`，并**显式 `emit fileListProperty->editingFinished()`**（用于触发业务侧重配置）。
- UI：属性 `valueChanged` 会触发 `onFileListPropertyChanged()`，用于刷新列表与统计信息。
- 文件拖放（2026-10-26，静态核对）：`StreamingFileTable` 自行处理 `startDrag/dropEvent`，以行间插入位置发出 `fileMoveRequested(sourceRow, destinationRow)`；`StreamingPanel` 在权威 `Streaming_FileList` 上按行索引移动，再通过属性刷新重建文件名/点数/采样率三列。不要依赖 `QTableWidget::InternalMove` 的默认单元格搬移或模型 `rowsMoved`；默认搬移不能保证完整行排序，且拖放结束后的源项清理可能再次破坏已刷新的内容。
- 只有最终路径列表发生变化时，拖放才提交一次 `editingFinished`；取消、原位拖放和交换相同路径且列表值不变时不触发重配置。拖动期间列表重建会使源 `QPersistentModelIndex` 失效，该次拖放被取消。
- Business 监听 `editingFinished`，更新文件顺序后提交用来重配和生成的快照。

#### “回写”机制（Generator → Property）

Business 也监听 Generator 的 `*Changed` 信号，把 Generator 的当前值回写到对应属性：

- `iqScaleChanged` → `Streaming_IQScale`
- `sampleRateChanged` → `Streaming_SampleRate`
- `fileListChanged` → `Streaming_FileList`

这使得 profile 恢复/重置时，属性系统与 UI 可以同步到真实运行参数。

---

## 4. 业务激活/Enabled 开关与框架调度

### 4.1 Enabled 开关如何影响业务选择

- `StreamingPanel` 中：`SwitchButton::statusChanged` 连接到 `Core::Panel::enabledChanged(bool)` 信号。
- `core/fancytabwidget.cpp`：每个业务 TabWidget 会连接 `Panel::enabledChanged` 到 `TabWidget::setSelected(bool)`。
- TabWidget::setSelected 会同步调用 `controlPanel()->setBtnEnabledChecked()`（保证 UI 状态一致），并发出 `selectedStateChanged`（由更上层 FancyTabWidget 管理“当前选择业务”）。

### 4.2 业务真正 start/stop 触发点（高层）

- 更高层（如 MainWindow / BusinessManager）在满足 RF/Mod 状态与业务选择后调用 `business->active()`。
- `BusinessManager` 会在设备 open/close、设备切换时触发 `activedBusiness->startBusiness()` 或 `stopBusiness()`。

对 Streaming 来说：
- `startBusiness()` 提交启动。sender 配置硬件，generator 开始生成。
- `stopBusiness()` 提交停止并清队列，等 sender 结束设备 I/O，然后将进度归零。

---

## 5. 线程模型与阻塞点（非常关键）

### 5.1 线程列表

- **UI线程**：所有 QWidget / Property 触发都在此。
- **Generator线程**：`StreamingDataGenerator::m_workerThread`。
- **Sender线程**：`StreamingBussiness::m_senderThread`。

### 5.2 阻塞与唤醒

- 两个 worker 空闲时等待主线程提交新代次。运行时，sender 在 `processRequests()` 等待数据，generator 在 `enqueueRequest()` 等待队列空间。
- 编辑或停止提交新代次后，队列立即丢弃旧帧，并唤醒两个 worker。已经取出的帧可能处于设备 I/O 中，停止等待该调用返回。
- 与新代次不匹配的旧配置回写、首包确认和错误均不能发布到当前操作。

---

## 6. 设备配置阶段

启动、运行中的文件/采样率/缩放编辑和桥接载波设置都会提交配置。主线程通过 `getCurrentProfile()` 取得公共参数，填入 nominal SampleRate、持续触发、Sweep 触发动作和 Stream 模式，再交给 sender。

1. 先调用 `m_operator.configuration()` 配置 FixedStream。
2. SweepStream 再叠加 FScan/LScan/MScan 配置。
3. 当前代次在主线程发出 `deviceConfigurationEnd`。配置成功且文件列表有效时进入下发，逐次配置均重置进度统计。
4. 清空文件列表时，Panel 取消使能，Core 走正常停流流程。在线重配仅处理非空列表。

---

## 7. 数据生产：WAV 读取、组帧与 IQScale

### 7.1 数据帧大小与 points 的对应关系

Generator 内部常量：

- `m_sampleCount = 2,000,000`
- `m_readLen = m_sampleCount * 2 * 2`
  - `*2`：I/Q 两路（short）
  - `*2`：short 的字节数
- 所以每一帧 `m_readLen = 8,000,000 bytes`。

发送线程从帧大小得到总点数：

- `framePoints = request.data.size() / 4`
- 解释：每个 complex sample = I(short,2B) + Q(short,2B) = 4B。
- 普通生成帧含 2,000,000 个复数 IQ 点。短文件循环缓存按完整周期补齐，可能超过这个帧大小。

### 7.2 WAV 文件校验与数据段定位

- `StreamingDataGenerator::workerLoop()` 对 fileList 中每个文件：
  - `QFile::open(ReadOnly)`
  - `Utils::WavHeader header(file)`
  - `header.isValid()` 后记录：
    - `dataPos = header.dataStartPos()`
    - `dataSize = header.dataSize()`

无效文件会被跳过（不会进入 fileList 生成队列）。

### 7.3 跨文件拼帧（保证固定帧长）

实现细节：

- `m_readed` 表示“上一轮未拼满的一帧已读字节数”。
- 如果某个文件尾部不足一帧，Generator 会把剩余字节读入缓冲并 **不立即发送**，而是 `break` 进入下一个文件；下一文件会继续读满这一帧后再发送。

因此：

- 普通路径按固定帧长入队，短文件循环缓存沿用完整波形周期的重复块。
- 多个文件会被视为一条连续流：尾部残片会与下一个文件头部拼接。

### 7.4 IQScale 处理

- 当 `currentIqScale != 100.0`：
  - `iqScaleToDecimal = currentIqScale * 0.01`
  - 对 `IQ` 向量中每个 `short` 执行 `v * iqScaleToDecimal`。

注意：这里对 short 的乘法结果会隐式转换回 short（截断/饱和行为取决于编译器与转换规则），因此极端缩放可能引入溢出或精度损失。

---

## 8. 数据消费：实时下发与进度统计

### 8.1 消费与首包

- sender 调用 `processRequests(revision, callback)` 等待和取帧。队列锁在回调处理数据或文件错误前释放。
- 每次 H2 发送最多 `min(2,000,000, floor(请求采样率 × 0.1 秒))` 个复数 IQ 点。发送端按偏移直接取原帧数据，末块按剩余点数下发。
- 每块进入 SDK 前检查操作代次。改参或停止时丢弃旧帧剩余部分，已进入 SDK 的调用按现有顺序等待返回。
- `m_operator.downloadDataRealTime()` 调用 H2 的 `tx_send_stream()`。返回 0 才算该块被接受，H2 正值仍经设备告警路径记录。
- 首块被接受后发布运行状态，连续下发期间不会维持 pending。首块失败会报错并解除等待，发送失败后放弃本帧余量。

### 8.2 播放进度

- 以首个成功块的时间为起点，以采样率估算播放位置，每约 1 秒更新一次。总点数来自实际打开的数据源。
- 时长不超过进度更新周期时显示 100%。较长的数据按总点数取模计算位置。

### 8.3 UI 进度显示

- Business 发出 `playbackPosChanged(double)`。
- 连接到 UI：`StreamingBussiness` 构造函数中 `connect(playbackPosChanged, StreamingPanel::onPlaybackPosChanged)`。
- UI 把 `pos` 格式化为 `"xx%"` 显示。

---

## 9. 启动/停止序列（时序梳理）

### 9.1 初始化和启动

- HTRA 插件注册 `StreamingBussiness`，构造时启动 sender 和 generator。两条线程在收到首个操作前等待。
- Core 完成交接后调用 `startBusiness()`。主线程提交代次、发布 pending，sender 配置设备，generator 按同一代次产出首包。

### 9.2 停止

- `stopBusiness()` 提交空生成快照并清队列，等待 sender 用条件变量确认停止。Core 收到确认后可完成现有模式交接。
- 停止确认后主线程结束 pending 并将 UI 进度归零。

---

## 10. 参数变化的传播路径

- GUI 和 SCPI/Minibar 在线编辑共用业务端字段更新。运行中编辑立即提交新代次。配置恢复和 Preset 先停业务，再按正常生命周期重新应用。
- 公共设备参数和桥接载波变化使用同样的代次机制。生成线程重新打开新文件列表，发送线程按快照完成配置并开始发送。
- 清队列不会撤回已经进入设备 I/O 的帧。旧代次结束后，只有新代次的结果能解除当前操作屏障。

---

## 11. 典型场景走读

### 11.1 进入 Streaming 模式后清空文件列表

- 清空列表和移除最后一项都由 Panel 取消使能。Core 登记刷新，并在重新仲裁时执行 `stopBusiness()`。
- 业务保存空列表。停流操作更新队列代次、清队列并等待 sender 确认。

当用户随后重新 Load 文件并重新进入 streaming：
- UI 更新 `fileList` 并重新满足 enable 前提；
- orchestrator 重新 resolve 到 streaming pipeline；
- sender thread 再次完成 stream 配置；
- generator 重新开始生产数据，下发恢复。

### 11.2 播放中拖拽排序/移除文件

- UI 在最终列表确实改变时更新 property 并发 `editingFinished`。
- business 保存新文件顺序并提交新代次。
- 队列立即丢弃上一代的软件帧。generator 从新快照生成，sender 丢弃上一代待发送的数据。
- sender 重配设备（仍是 STREAM，但会重置统计）。
- 重排后 `openStreamSources(currentFileList)` 按新列表顺序建立数据源，普通逐帧路径和小文件 replay cache 都使用新建的 `StreamCursor`，因此从新列表第一个有效文件的起点重新生成并循环下发。该行为不是保留原文件播放位置的热重排；软件队列清空也不代表已进入设备的帧在拖放瞬间撤回。此处为静态链路核对，GUI/设备切换时序仍待运行验收。

---

## 12. 当前实现的边界与注意点（如需排查问题时重点关注）

### 12.1 低采样率下的发送块与设备反压

原先在 195.3125 kSa/s 下，一次提交 2,000,000 点相当于 10.24 秒播放量。设备缓冲满时，发送可能等到设备腾出一块空间。现在发送端按目标 100 ms 分块。同一采样率下一次最多提交 19,531 点，1 MSa/s 时最多提交 100,000 点，20 MSa/s 起最多提交 2,000,000 点。

100 ms 表示每块覆盖的播放时长。SDK 调用耗时还取决于设备释放缓冲的粒度、是否正在消费及传输状态。已经进入设备缓冲的数据由设备侧配置行为处理。满缓冲下的实际调用耗时和切换后旧 IQ 输出仍待实机验收。[实施记录](../TaskLog/2026-11-24_streaming-adaptive-send-blocks.md)

### 12.2 Streaming 状态轮询与设备锁冲突

`FancyDevice::downloadDataRealTime()` 在发送线程中持有 `FancyDevice::m_mutex` 调用 `tx_send_stream()`。Streaming 运行时，任何主线程或 UI 侧路径如果也调用会拿同一把锁的设备查询/配置函数，都可能被低采样率下的连续 stream 下发放大成 UI 卡顿。

已确认的一个坑位是实时状态桥接：

- `DeviceManager` 每 1s 发出 `deviceRealTimeStatusUpdated(status)`。
- `DeviceRuntimeBridge::onDeviceRealTimeStatusUpdated()` 原本每次都调用 `refreshDeviceFeatureSpecs(currentDevice)`。
- `refreshDeviceFeatureSpecs()` 会调用 `device->triggerSourceSpecs()`。
- HTRA 的 `FancyDevice::triggerSourceSpecs()` 会拿 `FancyDevice::m_mutex` 并根据 GNSS / XPPS 状态刷新触发源 availability。

这条路径的初衷是维护公共 feature spec，但在 Streaming 中状态栏主要只需要吞吐量；每秒刷新 feature spec 不属于实时吞吐的必要工作。当前已收敛为：**Streaming active 时，`DeviceRuntimeBridge` 跳过 `refreshDeviceFeatureSpecs()`，但仍写入 `DeviceRealTimeStatus` property，因此吞吐量显示继续更新。**

维护规则：

- Streaming 运行期间，实时状态 tick 不应进入需要 `FancyDevice::m_mutex` 的 feature-spec / 设备能力刷新路径。
- feature spec 应优先在 device open / close / 设备切换 / 明确配置变更后刷新。
- 如果未来要让 XPPS availability 在 Streaming 中实时变化，应通过不抢 stream 发送锁的缓存路径或显式低频/延迟刷新实现，而不是在每个实时状态 tick 里直接查询 `triggerSourceSpecs()`。
- 设备设置、GPIO、关闭/切换设备、显式重配置等用户动作仍可能需要拿 `FancyDevice::m_mutex`；这些属于有意的设备事务，不应设计成 Streaming 播放中的周期性 UI 刷新路径。

---

## 13. 历史记录（旧调度机制的并发修复）

本节保存旧调度机制的并发修复记录。2026-11-24 起，清队列、唤醒双线程和结果归属统一由操作代次管理。

### 13.1 已落地改动点

1) **`DataSender::interruptProcessing()` 立即清队列**

- 目标：降低“profile 改变后继续下发旧 request”的风险窗口。
- 做法：在 `interruptProcessing()` 内部持锁将 `m_running=false` 后，立刻 `m_queue.clear()` 并 `notify_all()`，确保消费者线程不会在后续恢复时把旧队列残留继续发给设备。
- 预期影响：profileChange/stop 之后，队列中尚未消费的帧会被丢弃（符合“中断=抛弃历史 backlog”的语义）。允许存在极少量 in-flight（已经取出队列并进入下发的那一帧）。

2) **Streaming 侧跨线程标志位原子化（消除 data race/UB）**

- 目标：将 UI/业务线程与 worker 线程之间的低频控制标志从“未同步的 bool 读写”升级为有定义的并发行为。
- 做法：将 `StreamingBussiness` / `StreamingDataGenerator` 中用于跨线程控制的 flag（如 `m_enabled` / `m_profileChanged` / `m_exitFlag` 等）统一改为 `std::atomic_bool`，实现侧按需使用 `.load()` / `.store()`。
- 预期影响：功能行为不变，但避免在高压下出现“偶现卡死/错过唤醒/状态读到撕裂值”等未定义行为。

### 13.2 代码位置（便于追溯）

- `src/plugins/htra/streamingdatagenerator.{h,cpp}`
  - `HTRA::DataSender::interruptProcessing()`：中断语义收敛为“立刻清队列+唤醒”。
  - `HTRA::StreamingDataGenerator`：跨线程控制 flag 原子化。
- `src/plugins/htra/streamingbussiness.{h,cpp}`
  - `HTRA::StreamingBussiness`：跨线程控制 flag 原子化。

---

## 14. 下一步：吞吐性能优化任务（拷贝/分配热区）

这一部分用于把“以后要做的性能优化”任务化、可验收化。

背景：当前帧大小固定为 **8,000,000 bytes/帧**（2,000,000 complex samples）。在高采样率、长时间循环、多文件拼接时，主要压力会集中在：

- 每帧的缓冲区分配/扩容（`QByteArray resize` / allocator）
- 可能存在的额外拷贝（例如 `read` 返回临时 buffer，再 `memcpy` 到目标 buffer；或拼帧逻辑导致的中间搬运）
- 队列节点/请求对象的构造与拷贝（即使 `QByteArray` 是隐式共享，频繁创建大对象仍会对缓存与内存带宽有压力）

### 14.1 优化目标（建议的验收指标）

- **吞吐稳定性**：持续运行时，下发节奏抖动减少（可用 1s 内发送点数的方差/最大抖动衡量）。
- **调参响应时间**：profileChanged 后从 UI 操作到“新 profile 生效并开始下发新数据”的时间更短、更稳定。
- **分配次数下降**：每秒大块内存分配次数显著下降（目标：从“每帧一次甚至多次”降为“启动时预分配 + 运行期接近 0”）。
- **CPU 占用下降**：减少在 memcpy/allocator 上的 CPU 时间占比（以 profiler 结果为准）。

### 14.2 任务拆解（按优先级）

#### P0（优先做：不改架构/风险最低）

1) **把 WAV 读取路径改为“读入预分配的连续缓冲”**

- 现状风险：`QFile::read()` 若返回临时 `QByteArray`，再拼接/复制到帧 buffer，会产生额外拷贝与额外分配。
- 任务：改用 `QFile::read(char* data, qint64 maxSize)` 直接读入帧缓冲（或剩余空间），避免中间 `QByteArray`。
- 验收：确认“每帧读取路径”只发生一次目标 buffer 写入，不产生额外临时大块 buffer。

2) **减少每帧 `QByteArray` 的重复分配**

- 任务：在 generator 侧持有可复用的帧缓冲（例如固定容量的 `QByteArray frame; frame.resize(m_readLen)` 只在 profileChanged/启动时发生；循环内仅写入内容）。
- 注意：需要确保“入队后不会再修改同一块 data”，否则会触发隐式共享 detach 造成隐性深拷贝。

3) **对拼帧路径做拷贝审计**

- 任务：梳理 `m_readed` 跨文件拼帧时的数据流，确认是否存在“把已有数据搬来搬去”的路径；尽量改成“只写入一次最终帧 buffer”。

#### P1（收益高：需要小幅改队列数据结构/所有权）

4) **让队列承载“可移动/可共享的帧所有权”，而不是反复构造大对象**

- 方向 A（共享所有权）：`DeviceDataRequest` 存 `QSharedPointer<QByteArray>` / `std::shared_ptr<FrameBuffer>`，队列只复制指针；Buffer 来自池。
- 方向 B（移动语义）：调整 `enqueueRequest` 接口为移动（`DeviceDataRequest&&`），并确保底层容器能 move（需要结合当前 Qt 版本验证 `QQueue`/`QList` 的 move 行为）。
- 验收：队列入队/出队不触发大块数据复制（以 profiler + 自定义计数器验证）。

5) **引入固定大小的帧 Buffer 池（Buffer Pool）**

- 任务：预分配 `N = maxQueueSize + 2` 个 8MB buffer（或按实际 backpressure 调整），生产者取空闲 buffer 填充后入队；消费者发送完归还。
- 价值：把运行期的“分配/释放”变成“常数次预分配+复用”，降低 allocator 抖动。
- 风险：中断清队列时要正确归还 buffer，避免泄漏或 pool 耗尽。

#### P2（锦上添花：与拷贝/分配相关但可能需要更多验证）

6) **IQScale 路径向 SIMD/并行优化靠拢（前提：拷贝/分配已压下去）**

- 说明：当前缩放是对 `short` 的逐元素乘法，8MB/帧会产生明显的内存访问压力。
- 建议：先用 profiler 确认它在总时间里占比，再考虑 SSE/AVX 或分块并行。

7) **进度统计与首包逻辑的轻量化（非核心，但有助于减少抖动）**

- 说明：当前每 1s 做一次进度计算与信号发射，一般不是瓶颈，但可以在 profiling 里确认是否有锁竞争/字符串格式化开销。

### 14.3 建议的落地顺序

1) 先加 profiler/计数器，确认“分配次数/复制次数/热点函数”。
2) 做 P0：读入预分配缓冲 + 减少 resize。
3) 若瓶颈仍明显，再做 P1：队列所有权模型 + buffer pool。
4) 最后再考虑 P2：SIMD/并行等。

---

## 15. 一句话总结

Streaming 模式是以 PropertySystem 为“参数总线”，由 UI 触发参数变更；Business 负责设备配置与消费队列下发；Generator 负责读取 WAV、组固定帧并按 IQScale 处理；二者通过 DataSender 的有界队列实现背压与中断，从而在参数变化时能快速清队列并重新进入稳定流式下发。
