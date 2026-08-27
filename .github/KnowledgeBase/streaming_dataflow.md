# 流模式（Streaming）数据读取/加载/下发调度机制说明

本文基于当前代码实现，对“Streaming Playback”模式从底层设备接口到 UI 的数据调度链路进行梳理，包含：组件职责、线程模型、参数/属性联动、数据生产与消费（阻塞/唤醒）、设备配置与实时下发、以及 UI 进度反馈。

> 2026-03-31 更新：本文仍主要关注“双线程 + 队列”的数据流本体。`FixedStream / SweepStream` 当前已经通过 `MainWindow` 桥接到 `StreamingBussiness`，且 `fileList` 为空时不再由 workerLoop 内部额外配置 `Mute` 兜底。关于当前桥接实现以及下一步 `在线热切 / RearmSession / HardReboot` 的设计基线，见 `streaming_bridge_rearm_reboot_refactor_plan.md`。

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

- 继承 `Core::IBusiness`，由框架负责激活/停止。
- 维护一个发送线程 `m_senderThread`，在线程里运行 `workerLoop()`：
  - **设备配置阶段**：当 profile 变化时，切换设备模式（Stream）并调用 `m_operator.configuration()`。
  - **实时下发阶段**：循环调用 `streamingDataGenerator->processRequests()`，阻塞等待队列数据并下发。
- 负责“参数变化→全链路重启”的协调：
  - Property 的 `editingFinished` 触发 `m_profileChanged=true` 并更新 `StreamingDataGenerator`。
  - `onDeviceProfileChanged()` 触发时（公共设备参数变化），置 `m_profileChanged=true` 并 `streamingDataGenerator->interrupt()`。
- 负责播放进度统计：每 1s 依据已发送 sample 数计算进度百分比并 `emit playbackPosChanged()`。

### 2.3 `HTRA::StreamingDataGenerator`（数据生产层）

- 内部自带生成线程 `m_workerThread`，在线程里运行 `workerLoop()`：
  - 监听配置变化（fileList/iqScale/sampleRate）或 enabled 变化；
  - 打开 fileList 中的 WAV 文件，解析 `WavHeader` 得到 data 块位置/大小；
  - 按固定帧大小生成 IQ 数据块，做 IQScale 缩放；
  - 将 `DeviceDataRequest{data, sampleRate}` 交给 `DataSender::enqueueRequest()`。

### 2.4 `HTRA::DataSender`（队列与背压控制）

- 线程安全队列 `QQueue<DeviceDataRequest> m_queue`。
- 两个条件变量实现背压：
  - 生产者 `enqueueRequest()`：当 `m_queue.size() >= m_maxQueueSize(10)` 且运行中时阻塞。
  - 消费者 `processRequests()`：当队列为空且运行中时阻塞。
- `interruptProcessing()`：
  - `m_running=false`，唤醒所有等待线程，并清空队列（由调用方逻辑触发）。
- `restartProcessing()`：
  - `m_running=true`，恢复生产/消费。

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
  - `m_profileChanged = true;`
  - `streamingDataGenerator->setSampleRate(...)` 或 `setIqScale(...)`。
- Generator：setter 内部 `emit *Changed()`；若非 resetting 且已 enabled，会调用 `interrupt()` 触发“全链路重启”。

#### FileList

- UI：Load/Unload/Remove/拖拽排序会 `fileListProperty->setValue(QStringList)`，并**显式 `emit fileListProperty->editingFinished()`**（用于触发业务侧重配置）。
- UI：属性 `valueChanged` 会触发 `onFileListPropertyChanged()`，用于刷新列表与统计信息。
- Business：监听 `fileListProperty->editingFinished`：
  - **必须先** `streamingDataGenerator->setFileList(...)`，再 `m_profileChanged=true`。
    - 原因：sender 线程会在看到 `m_profileChanged` 后立刻调用 `streamingDataGenerator->toProfile()` 读取 `fileList` 并决定设备模式（空=Mute，非空=Stream）。
    - 若顺序对调（先置 `m_profileChanged`），在 Release 下可能出现线程竞态：sender 线程先一步读取到旧的空 `fileList`，把设备继续配置在 Mute，随后即使 generator 开始产数/下发，也可能表现为“无波形”。

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
- `startBusiness()` → 允许发送线程循环 & 开启 Generator 的 enabled。
- `stopBusiness()` → 关闭 Generator、等待发送线程退出下发循环、清空统计并回到 0%。

---

## 5. 线程模型与阻塞点（非常关键）

### 5.1 线程列表

- **UI线程**：所有 QWidget / Property 触发都在此。
- **Generator线程**：`StreamingDataGenerator::m_workerThread`。
- **Sender线程**：`StreamingBussiness::m_senderThread`。

### 5.2 典型阻塞点

1) Sender线程阻塞点：
- `StreamingBussiness::workerLoop()` 内部循环调用 `streamingDataGenerator->processRequests()`。
- `processRequests()` 会进入 `DataSender::processRequests()` 并在队列为空时阻塞：
  - 阻塞条件：`m_running == true && m_queue.isEmpty()`。
  - 唤醒条件：`!m_running || !m_queue.isEmpty()`。

2) Generator线程阻塞点：
- `StreamingDataGenerator::workerLoop()` 在 `m_profileChanged == false` 时 `m_condVar.wait()`。
- `DataSender::enqueueRequest()` 在队列满时阻塞：
  - 阻塞条件：`m_running == true && m_queue.size() >= m_maxQueueSize`。
  - 唤醒条件：`!m_running || m_queue.size() < m_maxQueueSize`。

### 5.3 interrupt / restart 的作用

当参数变化或停止时，会调用：

- `DataSender::interruptProcessing()`：
  - 令 `m_running=false`；
  - 唤醒生产者/消费者；
  - 让阻塞的 `enqueueRequest/processRequests` 立即返回并清空队列。

当开始正常工作时：

- `DataSender::restartProcessing()`：
  - 令 `m_running=true`；
  - 恢复正常阻塞/唤醒节奏。

---

## 6. 设备配置阶段（profileChanged → configuration）

发生条件：

- 属性编辑完成（IQScale/SampleRate/FileList 的 `editingFinished`）
- 公共设备参数变化 `StreamingBussiness::onDeviceProfileChanged()`
- 启动业务 `startBusiness()`

Sender线程在 `StreamingBussiness::workerLoop()` 中检测 `m_profileChanged`：

1) 从 `streamingDataGenerator->toProfile()` 获取 widgetProfile。
2) `getCurrentProfile(&m_deviceProfile)` 获取公共设备 profile，并写入：
  - `m_deviceProfile.sampleRate = widgetProfile.sampleRate`
  - `m_deviceProfile.triggerCount = -1`
  - `m_deviceProfile.mode = DEVICE_MODE_STREAM`
3) 读取当前桥接过来的 `carrier plan`。
4) 先调用：`m_operator.configuration(m_deviceProfile, &m_deviceProfile, &errorStr)`。
5) 若当前是 `SweepStream(FScan/LScan)`，再调用 streaming sweep overlay。
6) 发信号：`deviceConfigurationEnd(m_deviceProfile, errorStr)`。
7) 重置统计：`m_dataDownLoaded=false`、`m_totalSendSamples=0` 等。

备注：当前实现里，`fileList` 为空不再由 `StreamingBussiness` 内部额外配置 `Mute`；Panel 层会自动取消选中并重新仲裁。这里的配置阶段应理解为“active streaming session 的设备重配阶段”，而不是旧语义下的“空列表时内部保活 mute”。

---

## 7. 数据生产：WAV 读取、组帧与 IQScale

### 7.1 数据帧大小与 points 的对应关系

Generator 内部常量：

- `m_sampleCount = 2,000,000`
- `m_readLen = m_sampleCount * 2 * 2`
  - `*2`：I/Q 两路（short）
  - `*2`：short 的字节数
- 所以每一帧 `m_readLen = 8,000,000 bytes`。

发送到设备时 points 的计算：

- `points = request.data.size() / 4`
- 解释：每个 complex sample = I(short,2B) + Q(short,2B) = 4B。
- 因此每帧 points = 8,000,000 / 4 = 2,000,000 complex samples。

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

- **单个 request.data 总是固定帧长（除非参数中断/停止）**。
- 多个文件会被视为一条连续流：尾部残片会与下一个文件头部拼接。

### 7.4 IQScale 处理

- 当 `currentIqScale != 100.0`：
  - `iqScaleToDecimal = currentIqScale * 0.01`
  - 对 `IQ` 向量中每个 `short` 执行 `v * iqScaleToDecimal`。

注意：这里对 short 的乘法结果会隐式转换回 short（截断/饱和行为取决于编译器与转换规则），因此极端缩放可能引入溢出或精度损失。

---

## 8. 数据消费：实时下发与进度统计

### 8.1 消费/下发机制

- Sender线程循环调用 `streamingDataGenerator->processRequests()`。
- 该函数内部最终会调用 `DataSender::processRequests()`：
  - 队列为空则阻塞；
  - 取出一个 `DeviceDataRequest` 后释放锁；
  - 调用回调 `m_callback(request)`。
- 回调由 `StreamingBussiness` 注册：
  - `streamingDataGenerator->setProcessingCallback(std::bind(&StreamingBussiness::sendData,...))`

### 8.2 `StreamingBussiness::sendData()` 的关键逻辑

1) 若 `m_interrupt` 为 1，则直接返回（用于 stop/profileChanged 时的快速退出）。
2) 调用 `m_operator.downloadDataRealTime((int16_t*)request.data.data(), request.data.size()/4, request.sampleRate)`。
3) “首包不计入统计”：
   - `m_dataDownLoaded == false && result==true` 时仅记录时间戳并 return。
   - 之后的包才会累加 `m_totalSendSamples`。
4) 每隔约 1s 计算播放进度：
   - `totalSamples = m_widget->totalSamples()`（由 UI 统计的所有文件总 sample 数）
   - `samples = m_totalSendSamples % totalSamples`
   - `pos = samples / totalSamples * 100`
   - `emit playbackPosChanged(pos)`

### 8.3 UI 进度显示

- Business 发出 `playbackPosChanged(double)`。
- 连接到 UI：`StreamingBussiness` 构造函数中 `connect(playbackPosChanged, StreamingPanel::onPlaybackPosChanged)`。
- UI 把 `pos` 格式化为 `"xx%"` 显示。

---

## 9. 启动/停止序列（时序梳理）

### 9.1 初始化

- Plugin 初始化创建属性并注册业务：`Core::BusinessManager::registerBusiness(new StreamingBussiness)`。
- 构造 `StreamingBussiness`：
  - 创建 UI `StreamingPanel`。
  - 创建 `StreamingDataGenerator`（其内部立即启动 `m_workerThread`）。
  - 创建并启动 `m_senderThread`（其 started 后 invoke `workerLoop()`）。

此时：
- Sender线程在 `workerLoop()` 里由于 `m_enabled==false` 会阻塞在条件变量等待。
- Generator线程在 `workerLoop()` 中也会等待 `m_profileChanged`（初始化后通常会很快被 `setEnabled()` 或 setter 触发）。

### 9.2 startBusiness

- `StreamingBussiness::startBusiness()`：
  - `m_enabled=true; m_profileChanged=true; notify_one()` 唤醒 sender loop。
  - `streamingDataGenerator->setEnabled(true)`：
    - `m_enabled=true; m_profileChanged=true; notify_one()` 唤醒 generator loop。
    - `DataSender::restartProcessing()` 开启队列运行。

结果：
- Sender线程先进入“配置设备阶段”，把设备切到 `Stream` 并按需要叠加 sweep overlay。
- Generator线程开始读取文件并向队列生产数据；Sender线程阻塞消费并下发。

### 9.3 stopBusiness

- `StreamingBussiness::stopBusiness()`：
  - `streamingDataGenerator->setEnabled(false)`：
    - `DataSender::interruptProcessing()` 唤醒并清队列。
  - `m_profileChanged=true; m_enabled=false`。
  - busy-wait 等待 `m_interrupt` 变为 1（sender loop 退出内层发送循环后设置）。
  - 清统计并 `emit playbackPosChanged(0)`。

备注：stop 这里使用了忙等（`while (m_interrupt==0) continue;`），这会占用 CPU；但它的意图是确保 sender loop 已经跳出阻塞/下发状态。

---

## 10. 参数变化（profileChanged）的传播路径

### 10.1 UI 修改参数

- IQScale/SampleRate：用户编辑完成触发 `Property::editingFinished`。
- FileList：Load/Unload/Remove/拖拽后 UI 主动 `emit fileListProperty->editingFinished()`。

### 10.2 Business 响应

- 对应 `editingFinished` 回调：
  - 设置 `StreamingBussiness::m_profileChanged=true`（用于 sender loop 重配设备）。
  - 调用 Generator 的 setter（触发 Generator 侧 `interrupt()`）。

### 10.3 Generator 响应

- setter：更新内部值 → `emit *Changed()` → 若 enabled 且非 resetting → `interrupt()`。
- `interrupt()`：
  - `m_profileChanged=true; notify_one()` 唤醒生成线程；
  - `DataSender::interruptProcessing()` 立即唤醒并让消费者线程从阻塞中退出。

### 10.4 Sender 响应

- sender loop 的内层循环条件是 `while (!m_profileChanged && m_enabled)`。
- 一旦 `m_profileChanged=true`：
  - sender loop 跳出“下发循环”，回到外层进行设备重配置。

---

## 11. 典型场景走读

### 11.1 进入 Streaming 模式后清空文件列表

- 当前实现里，`fileList` 清空后，Panel 层会自动失能并取消选中；
- `FancyTabWidget` 随后重新仲裁，streaming business 不再依赖内部 `Mute` 兜底继续维持 active；
- generator 看到 `currentFileList.isEmpty()` 时不会继续产数；
- sender 若仍在等待队列，会被 interrupt / re-arbitration 路径唤醒并退出当前运行态。

当用户随后重新 Load 文件并重新进入 streaming：
- UI 更新 `fileList` 并重新满足 enable 前提；
- orchestrator 重新 resolve 到 streaming pipeline；
- sender thread 再次完成 stream 配置；
- generator 重新开始生产数据，下发恢复。

### 11.2 播放中拖拽排序/移除文件

- UI 更新 property 并发 `editingFinished`。
- business 标记 profileChanged 并更新 generator。
- generator interrupt → 清空队列、停止当前生成循环 → 重新按新 fileList 生成。
- sender 重配设备（仍是 STREAM，但会重置统计）。

---

## 12. 当前实现的边界与注意点（如需排查问题时重点关注）

### 12.1 低采样率下的固定帧时长与设备反压

当前 Streaming 每帧固定为 **8,000,000 bytes / 2,000,000 complex samples**。这意味着同一帧在不同采样率下代表的实时播放时长差异很大：

- 62.5 Msps：约 32 ms / 帧
- 2.5 Msps：约 800 ms / 帧
- 1 Msps：约 2 s / 帧
- 195.3125 ksps：约 10.24 s / 帧

因此，低采样率下 `tx_send_stream()` 的单次调用可能表现为明显更慢，这不一定表示 API 错误。更合理的模型是：`tx_send_stream()` 可能受设备端 FIFO / stream buffer 反压影响；当设备内部缓存空间不足时，连续下发会等待设备消耗前面的 IQ 数据。

调试时要特别注意断点对结论的影响：如果线程在断点处暂停，设备端可能已经继续消耗完前面送入的 IQ，恢复后下一次 `tx_send_stream()` 会因为 FIFO 变空而快速返回。这种“断点后很快”不能证明连续运行时 API 没有反压。

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

## 13. 近期已完成工作（Correctness/并发语义）

这一部分用于记录本次评审中已经落地的改动，避免后续回归时“重复踩坑”。这些改动主要是并发语义与中断行为的收敛，不改变 Streaming 的业务功能目标。

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
