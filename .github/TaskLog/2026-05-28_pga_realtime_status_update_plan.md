# PGA 平台相关实时状态查询更新流程实施方案

## 日期
- 2026-05-28

## 目标
- Win32 现有状态栏行为保持不变。
- PGA 手持等设备在状态栏稳定显示电池状态。
- PGA 手持等设备的 CPU 温度与 RFU 温度按 1s 频率交替显示。
- PGA 平台相关实时状态通过 `hlc` 查询，不把平台 I/O 放在 UI Widget 内执行。

## 范围

### 本次方案覆盖
- 电池状态显示的数据链路
- CPU 温度与 RFU 温度的双源合并
- 平台相关轮询 owner、线程、节拍、错误处理、生命周期
- Win32 与 PGA 的平台分流

### 本次方案不覆盖
- 电池图标样式与布局细节，视为已完成
- 亮度、风扇、震动等非状态栏需求
- `hlc` 写操作

## 当前现状

### 现有有效链路
- 设备实时状态由 [src/plugins/core/devicemanager.cpp](src/plugins/core/devicemanager.cpp#L299) 统一 1s 轮询。
- 设备状态经 [src/plugins/core/mainwindowdevicecontroller.cpp](src/plugins/core/mainwindowdevicecontroller.cpp#L142) 转发给状态栏。
- RFU 温度已经在 `DeviceRealTimeStatus.temperature` 中，由设备 API 查询得到。
- PGA 的 CPU 温度和电池状态当前由 [src/plugins/core/deviceinfowidget.cpp](src/plugins/core/deviceinfowidget.cpp#L96) 内部定时器直接拉取。

### 当前问题
- UI Widget 直接承担平台 I/O 与解析职责，owner 错位。
- 设备状态与平台状态分属两条链路，节拍不同步，后续扩展容易继续分叉。
- `hlc` 通过外部命令调用，若继续在 UI 线程轮询，会引入卡顿风险。
- 电池与 CPU 温度属于平台状态，不应混在纯视图组件内部拉取。

## 已确认的 PGA 真实运行时输入
- 实机 `hlc --help` 比旧文档多出 `--get-cpu-temp`、`--get-fan-level`、`--device-kernel-info*`。
- 实机 `hlc --get-cpu-temp` 返回纯数字字符串，例如 `49.05`。
- 实机 `hlc -b` 当前会返回全字段 `Unknown`，这不是错误，而是“电池信息尚未整备好”的合法空态。
- 这些结果已记录在 [ .github/TaskLog/2026-05-28_hlc_pga_probe_results.md ](.github/TaskLog/2026-05-28_hlc_pga_probe_results.md)。

## 设计原则
- 设备状态归设备链路 owner，平台状态归平台链路 owner。
- Widget 只做显示，不做 `hlc` 调用、不做平台判断、不做重试控制。
- PGA 增量实现应尽量复用现有 `DeviceManager -> MainWindowDeviceController -> DeviceInfoWidget` 更新路径。
- 不为 PGA 改坏 Win32；Win32 继续只消费设备 API 的实时温度。
- 温度交替是 UI 展示节拍，不等于所有底层查询都必须 1s 一次。

## 推荐目标架构

### 核心结论
- 不建议继续把 PGA 的 `hlc` 查询留在 `DeviceInfoWidget`。
- 推荐新增“平台运行时状态服务”，由 `MainWindowDeviceController` 持有并订阅。
- `DeviceManager` 继续负责 RFU 设备状态轮询；平台服务负责 CPU 温度和电池查询。
- `MainWindowDeviceController` 负责把两路状态合并后喂给 `DeviceInfoWidget`。

### 推荐 owner 划分
- `DeviceManager`
  - 继续 1s 轮询设备 API
  - 产出 `DeviceRealTimeStatus`
  - 只关心设备连接、RFU 温度、warning、error、吞吐率等设备态
- 新增 `PlatformRuntimeStatusService` 或 `PgaRuntimeStatusService`
  - 只在 PGA 平台启用
  - 负责 `hlc` 查询、超时、去重、防重入、缓存与错误退化
  - 产出平台态快照：CPU 温度、电池状态、最近更新时间、可用性
- `MainWindowDeviceController`
  - 订阅设备态与平台态
  - 合并成状态栏专用 view model
  - 控制生命周期：初始化、连接、断开、销毁
- `DeviceInfoWidget`
  - 只接收结构化结果并刷新显示
  - 保留 1s 的温度交替定时器
  - 不再自己发起 `hlc` 查询

## 为什么 owner 放在 MainWindowDeviceController 侧，而不是继续塞进 DeviceManager
- `DeviceManager` 当前职责非常明确：当前设备选择、打开关闭、设备状态轮询、断线恢复。
- 电池与 CPU 温度属于宿主平台状态，不完全是“当前设备状态”。
- 把平台态直接并入 `DeviceManager` 会扩大 `DeviceRealTimeStatus` 的语义边界，影响其它消费者。
- `MainWindowDeviceController` 本来就拥有状态栏 Widget，天然适合做“设备态 + 平台态”的汇聚层。
- 这是最小扩散方案：既把 I/O 从 Widget 挪走，又不强迫全局所有设备状态消费者理解 PGA 专属字段。

## 推荐新增的数据结构

### 平台态快照
- `PlatformRuntimeStatus`
  - `bool available`
  - `bool cpuTemperatureValid`
  - `double cpuTemperature`
  - `bool batteryPresent`
  - `bool batteryPercentValid`
  - `uint8_t batteryPercent`
  - `enum BatteryState { Unknown, Discharging, Charging, Full }`
  - `bool batteryLifetimeValid`
  - `uint32_t batteryLifetimeMinutes`
  - `QDateTime sampledAt`
  - `QString lastError`

### 状态栏汇聚态
- `DeviceInfoRuntimeState` 或 `DeviceInfoStatusSnapshot`
  - `bool deviceConnected`
  - `double rfuTemperature`
  - `double cpuTemperature`
  - `PlatformRuntimeStatus battery`
  - `int warningCode`
  - `bool rloWarningActive`
  - 只保留状态栏需要的字段，不把全量业务状态继续塞给 Widget

## 推荐线程与节拍设计

### 线程
- `DeviceManager` 维持现有设备状态轮询线程，不改。
- 平台状态服务使用独立 worker 线程。
- 原因：`hlc` 是外部命令，可能超时或偶发阻塞，不应与 UI 线程或设备 I/O 线程互相影响。

### 节拍
- 设备 API 状态：保持现有 1s。
- CPU 温度查询：1s。
- 电池查询：4s。
- 温度交替显示：1s，仅在 UI 层切换，不触发额外 I/O。

### 为什么电池不用 1s 查询
- 电池百分比和剩余时间变化远慢于温度。
- `hlc -b` 每次都启动外部进程，1s 查询没有收益，只有额外负担。
- 4s 对状态栏体验足够，同时显著降低进程创建与解析成本。

## PGA 平台更新流程

### 启动阶段
1. `MainWindowDeviceController::initialize()` 时判断当前是否为 PGA 平台。
2. 如果不是 PGA：不创建平台状态服务，保持 Win32 原逻辑。
3. 如果是 PGA：创建 `PgaRuntimeStatusService`，移动到独立线程。
4. 服务启动后立即执行一次“快速首帧查询”：
   - CPU 温度：立刻查一次
   - 电池状态：立刻查一次
5. 首帧结果回到主线程后更新状态栏，避免启动后长时间显示占位值。

### 周期更新阶段
1. `DeviceManager` 每 1s 发出设备态更新，提供 RFU 温度。
2. `PgaRuntimeStatusService` 每 1s 查询 CPU 温度。
3. `PgaRuntimeStatusService` 内部计数，每 4 个快节拍执行一次电池查询。
4. `MainWindowDeviceController` 维护两份最近快照：
   - 最近设备态
   - 最近平台态
5. 任一快照更新时，重新合并并下发给 `DeviceInfoWidget`。

### UI 展示阶段
1. `DeviceInfoWidget` 保存最近的 `rfuTemperature` 和 `cpuTemperature`。
2. 1s 定时器只负责切换“当前展示哪一个温度”。
3. 展示规则：
   - 两个温度都有效：按 1s 交替显示 `RFU xx.x℃` 和 `CPU xx.x℃`
   - 仅 RFU 有效：只显示 RFU
   - 仅 CPU 有效：只显示 CPU
   - 都无效：显示 `-`
4. 电池区始终显示最近一次平台态，不参与温度切换。

## Win32 平台更新流程
- 不创建 PGA 平台状态服务。
- 继续使用现有 `DeviceManager -> MainWindowDeviceController -> DeviceInfoWidget` 链路。
- 状态栏仍然只显示设备 API 的温度。
- 这样 Win32 没有额外线程、没有 `hlc`、没有行为变化。

## `hlc` 查询策略

### 推荐命令
- CPU 温度：`hlc --get-cpu-temp`
- 电池：`hlc -b`

### 进程模型结论
- `QProcess` 本身可以管理常驻子进程，但当前实机 `hlc` 空启动即退出，也没有表现出可持续从标准输入接收命令的交互模式。
- 因此推荐的模型是“平台状态服务对象常驻，`hlc` 子进程按次启动并在查询完成后退出”，而不是维持一个常驻 `hlc` 进程。
- 可以复用同一个调度对象或同一个 `QProcess` 使用方式，但每次查询仍应视为一次独立的 `hlc` 调用。

### 不建议做的事
- 不在 UI 线程执行 `waitForFinished()`。
- 不把 `hlc` 输出字符串直接交给 UI 层判断。
- 不在多个地方各自调用 `hlc`，必须由平台状态服务唯一持有。

### 解析规则
- 全部按大小写不敏感、label 前缀匹配。
- `Unknown` 视为“无有效值”，不是 0。
- `Battery Charging Status`
  - `Yes` / `Charging` -> `Charging`
  - 未充电且 `percent >= 99` -> `Full`
  - 其它且存在剩余分钟数 -> `Discharging`
- `Battery lifetime`
  - `Unknown` 或 `calculating` -> `batteryLifetimeValid = false`
  - 数字 -> 分钟数
- CPU 温度
  - 纯数字字符串解析为 `double`
  - 非数字或超时 -> 当前轮无效

## 失败与退化策略

### CPU 温度失败
- 单次失败：本轮 CPU 温度无效，但不影响 RFU 温度显示。
- 连续失败：继续保留服务运行，只是不显示 CPU 温度。
- 不弹框，不打断用户操作；最多写调试日志。

### 电池失败或 `Unknown`
- 电池图标保留空态。
- 百分比显示 `--`。
- 状态文案显示 `--`。
- 不把 `Unknown` 转成 `0%`，避免误导。

### 设备断开
- RFU 温度立即失效。
- CPU 温度和电池是平台态，可继续显示。
- 如果产品定义要求“设备断开时全部隐藏”，则由 controller 在汇聚层统一裁剪，而不是让 Widget 自己推断。

### 服务防重入
- 如果上一轮 `hlc` 查询尚未结束，下一轮直接跳过，不并发发起第二个同类命令。
- 必须记录 `inFlight` 状态，避免多个 `QProcess` 堆积。

## 生命周期设计

### 创建
- `MainWindowDeviceController` 构造或 `initialize()` 时按平台决定是否创建服务。

### 启动
- PGA 平台启动后立即启动平台状态服务。
- 不依赖设备是否已连接，因为 CPU 温度和电池属于宿主平台态。

### 停止
- 主窗口销毁、插件卸载或应用退出时停止服务线程。
- 当前设备切换时无需重建服务。

### 设备切换
- 平台态不因设备切换而清空。
- RFU 温度跟随 `DeviceManager` 当前设备变化自动更新。

## 推荐的最小改造步骤

### 第一步：抽 owner
- 从 `DeviceInfoWidget` 移除 `m_hardwareStatusTimer` 与 `refreshPgaHardwareStatus()` 的调度职责。
- 新建 `PgaRuntimeStatusService`，先只实现“定时查询 + 发信号”。

### 第二步：引入结构化平台态
- 定义 `PlatformRuntimeStatus`。
- 把 `hlc` 解析全部收口到该服务中，不新增任何对 `IHardwareSettings` 的依赖
- 删除 IHardwareSettings 的 `cpuTemperatureAvailable()`、`getCpuTemperature()`、`hasBattery()`、`getBatteryInfo()` 这组运行时状态接口，改由 `PgaRuntimeStatusService` 独占 `hlc` 查询。
- 保证 UI 层不再看到原始 `hlc` 文本。

### 第三步：在 controller 层合并
- `MainWindowDeviceController` 保存最近设备态和最近平台态。
- 每次任一状态变化，都重新生成状态栏专用快照。

### 第四步：简化 Widget
- `DeviceInfoWidget` 只保留：
  - 电池显示函数
  - 温度切换定时器
  - 状态文字渲染
- 删除平台判断与 `hlc` 拉取逻辑。


### 第五步：稳定性收口
- 增加日志：查询超时、解析失败、连续失败计数。
- 验证设备断开、切换、退出时线程是否干净停止。

## 建议的接口关系

### 服务对外信号
- `void platformStatusUpdated(const PlatformRuntimeStatus &status)`

### controller 对 widget 的输入
- 推荐新增：`void updatePlatformRuntimeStatus(const PlatformRuntimeStatus &status)`
- 或更彻底：`void updateRuntimeState(const DeviceInfoRuntimeState &state)`

### 推荐取舍
- 如果只想最小改动：保留现有 `updateDeviceRealTimeStatus()`，再新增 `updatePlatformRuntimeStatus()`。
- 如果想把链路一次理顺：改成单一 `updateRuntimeState()` 更干净。
- 当前实现已采用单一 `updateRuntimeState()` 版本，controller 负责汇聚后一次性下发给 widget。

## `IHardwareSettings` 去留建议

### 结论
- 从当前代码基线看，`IHardwareSettings` 现在不适合直接删除，但适合显著瘦身。
- `IHardwareSettings` 如果继续保留，更合理的定位应是“平台设置能力”，而不是“平台设置 + 平台运行时遥测”的混合接口。

### 为什么现在不能直接删
- 亮度能力当前仍有多个真实调用点，例如 [src/plugins/core/mainwindow.cpp](src/plugins/core/mainwindow.cpp#L341)、[src/plugins/core/preferencedialog.cpp](src/plugins/core/preferencedialog.cpp#L47)、[src/plugins/core/brightnesssettingdialog.cpp](src/plugins/core/brightnesssettingdialog.cpp#L10) 和 [src/libs/utils/autobrightness.cpp](src/libs/utils/autobrightness.cpp#L127)。
- 震动反馈开关也还通过该接口暴露，例如 [src/plugins/core/mainwindow.cpp](src/plugins/core/mainwindow.cpp#L349) 和 [src/plugins/core/preferencedialog.cpp](src/plugins/core/preferencedialog.cpp#L14)。
- 因此它虽然来源于别的平台，但现在已经承载了本项目真实在用的“亮度/震动/时间/音量”平台能力入口。

### 建议的简化方向
- 保留亮度、时间、震动、音量等设置型能力；如果后续想继续去耦，可以把它重命名为 `IPlatformSettings` 或按能力继续拆分。



## 温度交替显示规则建议
- 交替频率：2s。
- 首次显示：优先 RFU，再切到 CPU。
- 连接中断后：
  - RFU 无效时停止交替，只显示 CPU。
  - CPU 也无效时显示 `-`。
- 当任一温度重新变有效时，下一次 2s tick 自动恢复双温交替。

## 本轮已完成实现（步骤 1-2）

### 已完成内容
- 新增 `PlatformRuntimeStatus` 结构化平台态，统一承载 CPU 温度、电池百分比、电池状态、剩余分钟数、采样时间与最近错误。
- 新增 `PgaRuntimeStatusService`，在 Core 插件内独占 `hlc` 查询与解析；服务按固定节拍轮询 CPU 温度，并按较低频率轮询电池信息，再通过信号发送结构化结果。
- `DeviceInfoWidget` 已移除 `m_hardwareStatusTimer` 和 `refreshPgaHardwareStatus()` 这条 PGA 轮询链路，不再直接访问 `IHardwareSettings` 获取 CPU 温度和电池信息。
- `MainWindowDeviceController` 已在 PGA 平台下创建并持有 `PgaRuntimeStatusService` 的 worker 线程，接收平台态更新后转发给 `DeviceInfoWidget`。
- `IHardwareSettings` 与 `PGAHardwareSettings` 已删除 `cpuTemperatureAvailable()`、`getCpuTemperature()`、`hasBattery()`、`getBatteryInfo()` 这组运行时状态接口；`hlc` 解析现已只存在于 `PgaRuntimeStatusService`。
- PGA 温度文案已从原先的 `DEV/SYS` 调整为 `RFU/CPU`，避免 UI 继续暴露平台实现细节。

### 本轮边界说明
- 这次只实现了步骤 1 和步骤 2，以及使这两步生效所需的最小 controller 接线。
- 尚未引入统一的 `DeviceInfoRuntimeState` / `DeviceInfoStatusSnapshot` 汇聚模型；当前仍保持“设备态单独更新 + 平台态单独更新”的最小接线方式。
- Widget 现阶段仍保留原有温度切换定时器；后续步骤再统一收口展示节拍与完整汇聚逻辑。

### 本轮验证情况
- 按要求未执行编译。
- 已对本轮改动文件执行静态诊断，当前无新增错误。

## 本轮已完成实现（步骤 3-4）

### 已完成内容
- 已删除 `AC Line` 相关字段与解析；`PlatformRuntimeStatus` 不再承载 `acOnline*` 信息，`PgaRuntimeStatusService` 也不再解析该行输出。
- 已恢复独立的 `platformruntimestatus.h`，并在其中新增状态栏专用 `DeviceInfoRuntimeState`。
- `MainWindowDeviceController` 现在保存最近设备态与最近平台态；每次任一状态更新，都会构造 `DeviceInfoRuntimeState` 并统一下发。
- `DeviceInfoWidget` 已删除分别消费设备态与平台态的两套 runtime 入口，改为只保留 `updateRuntimeState()` 一个入口；widget 内只负责电池显示、温度轮播和状态文字渲染。
- `resetDisplayedDeviceState()` 现在只清空设备 runtime，不会误清 PGA 平台态；设备断开后 CPU 温度与电池可继续保留显示。
- PGA 温度切换定时器已调整为 1s，和设计文档中的展示节拍一致。

### 当前最终运行时边界
- `PgaRuntimeStatusService`：唯一 owner，负责 `hlc` 查询与平台态解析。
- `MainWindowDeviceController`：唯一汇聚层，负责把设备态与平台态合成为 `DeviceInfoRuntimeState`。
- `DeviceInfoWidget`：纯视图，只消费 `DeviceInfoRuntimeState`，不直接理解 `hlc` 输出，也不再承担双路 runtime 协调。

### 本轮验证情况
- 按要求未执行编译。
- 已对本轮改动文件执行静态诊断；未发现新的本地类型或调用错误。

## 本轮续做计划（步骤 3-4）

### 调整目标
- 删除 `AC Line` 相关字段与解析，平台态不再承载该信息。
- 恢复独立的 `PlatformRuntimeStatus` 头文件定义，避免结构体定义重新回流到 service 头文件。
- 新增状态栏专用 `DeviceInfoRuntimeState`，由 `MainWindowDeviceController` 保存最近设备态与平台态并统一汇聚。
- `DeviceInfoWidget` 改为只接收单一 `updateRuntimeState()` 输入，不再分别消费设备态与平台态。

### 本轮取舍
- 保留 `updateDeviceInfo()` 处理版本号与连接摘要等静态设备信息；本轮只彻底收口 runtime 链路。
- 平台服务 owner 与线程边界保持不变，仍由 `PgaRuntimeStatusService` 独占 `hlc` 查询。

## 验证计划

### 静态验证
- Win32 编译通过且无额外平台依赖。
- PGA 代码路径不再从 Widget 直接访问 `IHardwareSettings` 进行轮询。

### 运行时验证
- PGA 空闲状态下启动应用：CPU 温度和电池区在首帧后出现。
- 连接设备后：RFU 温度开始参与交替显示。
- 拔掉设备或断开 API：RFU 温度消失，CPU 温度保留。
- 电池仍为 `Unknown` 时：显示 `--` / 空态，不出现 `0%`。
- 让 `hlc` 故意失败时：UI 不冻结，主线程无卡顿，日志可见失败信息。

## 风险与取舍
- 如果把平台态直接塞进 `DeviceManager`，一次性能更“统一”，但会扩大核心设备态结构的影响面。
- 如果继续让 Widget 自拉 `hlc`，短期改动最小，但线程边界和 owner 都是错的，后面一定继续返工。
- 独立平台服务 + controller 汇聚是当前代码基线下风险最低、边界最清晰的方案。

## 最终建议
- 采用“双源、单汇聚、纯视图”的结构：
  - 设备 API 负责 RFU 温度
  - PGA 平台服务负责 CPU 温度和电池
  - `MainWindowDeviceController` 做统一汇聚
  - `DeviceInfoWidget` 只负责显示和 1s 温度切换
- 这样既满足 PGA 的平台差异，又不会破坏 Win32 既有行为，也为后续扩展风扇、亮度、电源状态留下干净入口。

## 本轮 Bugfix（状态栏布局与连接态）

### 修复目标
- PGA 状态栏中，电池区从最左侧移到最右侧。
- PGA 右侧信息顺序调整为：API 版本、GUI 版本、温度轮播、电池。
- 修复“设备已打开且设备信息可见，但状态栏仍停留在 Disconnected”的显示错误。

### 局部根因判断
- PGA 布局顺序由 `DeviceInfoWidget::buildPgaUi()` 决定，当前把电池区插在了最左侧。
- PGA runtime 渲染在 `deviceConnected == false` 时会把文案强制改成 `Disconnected`，但后续 `deviceConnected == true` 时没有对应地恢复连接态文案，因此一旦被覆盖就可能长期停留在未连接显示。

### 本轮实现边界
- 只改 `DeviceInfoWidget` 的 PGA 布局和连接态显示恢复逻辑。
- 不改 `PgaRuntimeStatusService`、`DeviceManager` 的轮询与线程模型。
- 按要求不执行编译，只做本地静态诊断验证。

## 本轮跟进收口（移除 runtime 连接态字段）

### 判断
- status bar widget 挂载于 `MainWindow`，但实际输入 owner 是 `MainWindowDeviceController`。
- 设备连接/断联 owner 仍然是 `DeviceManager`，status bar 的连接文案、Win32 版本信息应继续沿用 `currentDeviceOpenStateChanged -> updateDeviceInfo()/resetDisplayedDeviceState()` 这条既有链路。
- `DeviceInfoRuntimeState::deviceConnected` 与上述链路语义重复，且容易被平台态/运行时快照的到达时序污染。

### 收口方向
- 删除 `DeviceInfoRuntimeState::deviceConnected`。
- `DeviceInfoWidget::updateRuntimeState()` 不再承担连接态切换，只消费运行时值本身。
- 连接文案、版本区清空/恢复统一回到 `updateDeviceInfo()` 的既有机制，保证与 `DeviceManager` 的断联管理一致。

## 本轮继续收口（删除重复设备态聚合）

### 判断
- `DeviceInfoRuntimeState` 中的 `deviceTemperature / throughputBps / sampleRate / rloWarningActive` 本质上只是搬运 `DeviceRealTimeStatus` 的旧字段。
- PGA 这次真正新增的平台实时状态只有 `hlc` 提供的 CPU 温度与电池态；其余设备态继续走既有 `DeviceManager -> MainWindowDeviceController -> DeviceInfoWidget` 链路即可。
- 因此不应再保留一个名字接近但语义重复的聚合结构，避免 status bar 同时存在“旧设备态”和“新包装设备态”两套来源。

### 收口方向
- 删除 `DeviceInfoRuntimeState`。
- `DeviceInfoWidget` 恢复两条输入：`updateDeviceRealTimeStatus(const DeviceRealTimeStatus &)` 负责旧设备态，`updatePlatformRuntimeStatus(const PlatformRuntimeStatus &)` 负责 PGA 新平台态。
- `MainWindowDeviceController` 不再构造中间 runtime 快照；设备态更新直接转发 `DeviceRealTimeStatus`，平台态更新直接转发 `PlatformRuntimeStatus`。

## 本轮 Bugfix（PGA 吞吐量不显示）

### 现象
- 树莓派 / PGA 平台连接设备后，status bar 不显示实时吞吐量。
- 即使 `throughputBps == 0` 或已进入流模式，界面都没有 `B/s` 文案。
- Win32 正常。

### 本地根因
- `DeviceInfoWidget::buildPgaUi()` 中创建了 `m_bandwidth`，但显式 `setVisible(false)`，且没有加入 PGA 的 `statusLayout`。
- `DeviceInfoWidget::updateDeviceRealTimeStatus()` 的 PGA 分支还在每次设备态刷新时执行 `m_bandwidth->clear()`。

### 修复方向
- PGA 布局重新接入 `m_bandwidth`，与 warning 区共用左侧状态区。
- PGA 设备态刷新时使用既有 `throughputBps -> normalize1K(...) + "B/s"` 逻辑更新文本；连接后即使为 0 也应显示为 `0B/s` 风格文案。

## 本轮文档更新计划（KnowledgeBase）

### 用户诉求
- 功能已测试完毕，需要把“新增实时数据链路设计”与“如何复用既有链路”沉淀到 KnowledgeBase。

### 文档落点
- 新增：`.github/KnowledgeBase/pga_runtime_realtime_status_chain.md`
  - 说明 PGA 新增平台实时状态链路（`PgaRuntimeStatusService`）的 owner、线程、节拍、解析与退化语义。
  - 明确“新增链路”和“复用既有设备态链路”的边界，不再混淆为单一聚合快照。
- 更新：`.github/KnowledgeBase/device_status_ui_feedback.md`
  - 增补 PGA 平台新增链路章节，说明与原 error/warning/RLO 链路如何并行共存。
- 更新：`.github/KnowledgeBase/Index.md`
  - 把新文档加入索引，便于后续检索。

### 当前代码基线（用于文档对齐）
- `DeviceManager -> MainWindowDeviceController -> DeviceInfoWidget` 的设备实时状态链路保持不变（`DeviceRealTimeStatus` 仍是旧链路输入）。
- `MainWindowDeviceController` 在 PGA 平台下新增并持有 `PgaRuntimeStatusService` 线程；平台态通过 `PlatformRuntimeStatus` 单独下发给 `DeviceInfoWidget::updatePlatformRuntimeStatus(...)`。
- `DeviceInfoWidget` 维持两条输入：
  - `updateDeviceRealTimeStatus(const DeviceRealTimeStatus&)`：设备态（RFU 温度、吞吐、RLO）
  - `updatePlatformRuntimeStatus(const PlatformRuntimeStatus&)`：平台态（CPU 温度、电池）
- `IHardwareSettings/PGAHardwareSettings` 已移除 CPU/电池运行时读取接口；`hlc` 查询 owner 已收敛到 `PgaRuntimeStatusService`。