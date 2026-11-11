# PGA 平台实时状态链路设计与复用边界

本文记录 2026-05 已落地并完成测试的 PGA 状态栏实时数据链路，重点回答两件事：

1. 新增了哪条实时数据链路。
2. 旧链路复用了哪些部分、没有复用哪些部分。

2026-10-28 新增电量显示映射、严格低电量阈值和中央充电提醒水印；用户实测反馈先显示 0%，稍后出现水印。后续接入水印后 20 秒的 HLC 主动关机，该增量仅完成静态检查，尚未编译或进行真实关机验收。

---

## 1. 背景与目标

PGA 平台需要在状态栏显示：

- `CPU` 温度（来自 `hlc --get-cpu-temp`）
- 电池百分比、充电状态、剩余时间（来自 `hlc -b`）

同时必须保持 Win32 和现有设备状态链路行为稳定：

- 设备实时状态仍由 `DeviceManager` 轮询并发布 `DeviceRealTimeStatus`
- 现有 error/warning/RLO 反馈机制不被改坏
- UI Widget 不直接承担平台 I/O

---

## 2. 最终架构（已落地）

### 2.1 设备态旧链路（复用）

这条链路保持不变：

- `DeviceManager` 在工作线程按 1s 轮询设备状态
- 发布 `deviceRealTimeStatusUpdated(const DeviceRealTimeStatus &)`
- `MainWindowDeviceController::onDeviceRealTimeStatusUpdated(...)` 继续处理
  - error 弹窗去重
  - warning 分类与队列转发
  - 设备态下发给 `DeviceInfoWidget::updateDeviceRealTimeStatus(...)`

这意味着以下展示仍完全复用旧逻辑：

- RFU 温度
- 吞吐率
- RLO 小指示
- 设备运行态 warning 队列

### 2.2 平台态新链路（新增）

新增平台实时状态服务：`Core::Internal::PgaRuntimeStatusService`。

- 只在 PGA 平台创建（`MainWindowDeviceController::initializePgaRuntimeStatusService()`）
- 独立 `QThread` 承载，不在 UI 线程跑 `hlc`
- 产出结构化平台态：`PlatformRuntimeStatus`
- 通过信号 `platformStatusUpdated(const PlatformRuntimeStatus &)` 回到 controller
- 由 `MainWindowDeviceController::handlePlatformRuntimeStatusUpdated(...)` 下发到 `DeviceInfoWidget::updatePlatformRuntimeStatus(...)`

### 2.3 Widget 输入边界（最终方案）

`DeviceInfoWidget` 最终保留两条并行输入，不做中间聚合结构：

- `updateDeviceRealTimeStatus(const DeviceRealTimeStatus &status)`：设备态
- `updatePlatformRuntimeStatus(const PlatformRuntimeStatus &status)`：平台态

这样做的目的：

- 不重复包装旧 `DeviceRealTimeStatus` 字段
- 避免“伪统一快照”造成 owner 混乱
- 让 PGA 增量变成“加一条平台支路”，而不是重写整个状态栏数据模型

---

## 3. 新增链路详细设计

## 3.1 数据结构

平台态结构定义在 `src/plugins/core/platformruntimestatus.h`：

- `available`
- `cpuTemperatureValid` / `cpuTemperature`
- `batteryPresent`
- `batteryPercentValid` / `batteryPercent`（保留 HLC 原始 0–100 整数；`mappedBatteryPercent()` 提供有符号映射，`batteryChargerRequired()` 定义临界条件）
- `batteryState`（`Unknown/Discharging/Charging/Full`）
- `batteryLifetimeValid` / `batteryLifetimeMinutes`
- `sampledAt`
- `lastError`

## 3.2 轮询节拍与线程

`PgaRuntimeStatusService` 当前参数（`src/plugins/core/pgaruntimestatusservice.cpp`）：

- 总轮询周期：1s（`kPollIntervalMs = 1000`）
- CPU 温度：每个 tick 查询一次
- 电池：每 4 个 tick 查询一次（`kBatteryPollEveryTicks = 4`）
- 单次命令超时：2s（`kCommandTimeoutMs = 2000`）

### 3.3 命令与解析

- CPU：`hlc --get-cpu-temp`
- 电池：`hlc -b`

解析规则要点：

- `Unknown` 视为“无有效值”，不是 `0`
- 百分比从文本中提取整数；无数字、整数溢出或超出 `0~100` 时保持无效，不以默认 0 触发低电量提醒
- `Battery Lifetime` 中 `unknown/calculating`、无数字或负数视为无效
- 电池状态推导规则：
  - 充电标志为 `Yes/Charging` 且有效原始百分比为 100 -> `Full`
  - 充电标志为 `Yes/Charging` 的其余情况 -> `Charging`
  - 充电标志为 `No/Discharging` -> `Discharging`，不要求剩余时间有效
  - 否则 `Unknown`

用户确认当前 HLC 的 `AC Line Statue` 始终为 Unknown，接电判定只依据 `Battery Charging Status` 的 Yes/No。主动关机接入后，不再以“剩余分钟数有效”推断 Discharging，避免充电状态未知时误当成 No。

### 3.4 失败退化

- 单次 `hlc` 失败不弹窗，不打断主流程
- 对应字段置无效，保留上层可显示的其它字段
- 错误文本写入 `lastError` 并输出 `qWarning`

---

## 4. 复用旧链路的具体方式

## 4.1 复用点

本次改造明确复用了以下既有链路：

1. `DeviceManager` 的设备态轮询线程与 1s 节拍
2. `MainWindowDeviceController::onDeviceRealTimeStatusUpdated(...)` 的 error/warning/RLO 处理
3. `DeviceInfoWidget::updateDeviceRealTimeStatus(...)` 的设备态渲染
4. `updateDeviceInfo()/resetDisplayedDeviceState()` 的连接态与版本区更新路径

## 4.2 不复用点（新建并隔离）

以下内容不再复用旧接口，而是独立收敛：

1. `hlc` 进程调用与文本解析
2. CPU 温度与电池运行时状态读取
3. 平台态轮询 owner（由 `PgaRuntimeStatusService` 独占）

对应地，`IHardwareSettings` / `PGAHardwareSettings` 已不再承载 CPU/电池运行时查询接口，保持为平台设置能力（亮度/时间/震动/音量）语义。

---

## 5. UI 呈现语义（PGA）

- 温度区：RFU/CPU 双源展示
  - 两者都有效时，按 2s 定时交替显示
  - 仅一者有效时，固定显示该源
  - 都无效时，显示 `-`
- 电池区（2026-10-29 更新）：PGA 电池布局下，启动时 `hlc --device-kernel-info-all` 正常退出且返回 0 即持续显示电池栏，不要求 `PGA_PowerSourceType` 字段存在或值为 1
  - 无 HLC、查询超时/异常或非零退出时保持隐藏；供电字段缺失、0、空或非整数不再阻止显示
  - 显示后独立于温度切换，呈现最近平台态；后续 `hlc -b` 查询失败或返回 99%、0 分钟不会关闭电池栏
  - 百分比无效 -> `--`
  - 状态无效 -> `--`

HTRA `Plugin::detectHlcService()` 在启动时缓存 HLC 可用性，`extensionsInitialized()` 将该缓存传给主窗口。菜单隐藏、连接后 Low Power 默认 ON、电池栏显示共用 `hasHlcService()`，不再读取 `usbPortOnly` 或依赖供电字段存在性；旧 `m_hasPowerSourceType` 已移除。电池 Widget 自身的 PGA 布局边界保持。

2026-10-29：新平板均已非单口供电，但用户进一步明确软件仍须兼容旧单口平板。三项平板行为统一只依据 HLC 可用性：HLC 正常返回即隐藏设备菜单、显示电池，当前设备实际 open 时将 Low Power 默认设为 ON；不再要求 `PGA_PowerSourceType == 1`，USB/ETH 均适用。无 HLC/查询失败时，菜单保持普通显示，Low Power 默认 OFF，电池隐藏。

`usbPortOnly` 判据及两处按该判据执行的 `device_ignore_pd()` 已按用户要求恢复保留：同一次 HLC 输出中的 `PGA_PowerSourceType` 解析为整数 1 时为 true，只用于正常 USB open 和零身份探测的旧硬件兼容，不再决定上述三项平板行为。其余值或字段缺失时为 false，不将 USB PD 忽略推广到所有平板。

HLC 检测结果和单口供电标志均在插件构造阶段只初始化一次，不增加命令探测、周期轮询或重试，也不修改 `Utils::HostInfo::isPgaHost()` 等全局平台分类。此增量仅静态检查，新旧平板均待设备验收。

注意：温度切换节拍是 UI 展示节拍，不等于底层命令查询频率。

### 5.1 电量映射与三级提醒（2026-10-28）

电池栏启用时，由 `DeviceInfoWidget::updatePgaBatteryDisplay()` 使用 `PlatformRuntimeStatus` 的统一派生规则呈现：

- 有符号映射值：`(int(HLC) - 5) * 100 / 95`，使用整数除法；非负值向下取整。
- 百分比文字和电池填充显示映射值截断到 `[0, 100]` 的结果；HLC 5 → 0%，100 → 100%，低于 5 仍显示 0%。
- 阈值 1（2026-10-29 按用户要求恢复原配色）：显示百分比严格 `<20` 时，仅内部填充由绿变红；外框和电池头始终使用 `m_borderColor`，不随低电量变红。0% 无填充时也不把外框染红。
- 阈值 2：显示百分比严格 `<10`，电池图标以现有 500 ms 渐隐、500 ms 渐显循环闪烁；恰好 10 不闪烁，恰好 20 不变红。
- 阈值 3（用户后续明确修订）：显示电量为 0%，即有符号映射值 `<=0`（HLC `<=5`），且状态为 `Discharging`，同一 UI 刷新即在主窗口中央显示红色水印并请求 20 秒关机计时。文案为 `Please connect charger to the power port.`，使用 `tr()` 源键，翻译表由用户维护。
- `Charging / Full / Unknown`、电量无效或电池栏关闭时不显示水印；下一状态满足恢复条件即隐藏水印。无效电量显示 `--` 并关闭低电量颜色和闪烁；隐藏电池栏会立即停止闪烁并撤掉水印。

`Controls::Battery` 在数值或提醒设置改变时同步更新颜色和动画，不再额外依赖 100 ms 告警轮询定时器。映射与临界条件只定义在 `PlatformRuntimeStatus::mappedBatteryPercent()` / `batteryChargerRequired()`；Widget 和服务复用它们，避免水印与关机分别定义阈值。Widget 在 GUI 线程发出 `pgaBatteryChargerRequired(bool)`，`MainWindow` 只负责显示/隐藏水印。

水印为 `MainWindow` 拥有的透明背景 `QLabel`，红色、32 px 粗体、居中且允许换行，随主窗口尺寸更新几何；不接受焦点或鼠标/触摸输入，不创建弹窗。它随父窗口销毁，不承担关机计时或 HLC I/O；未扩展 Minibar IPC。现有内部发布水印仍独立受其原编译选项控制。

静态边界核对：

| HLC 电量 | 显示百分比 | 内部填充红色 | 闪烁 | 未充电时充电水印 |
| --- | --- | --- | --- | --- |
| 100 | 100 | 否 | 否 | 否 |
| 24 | 20 | 否 | 否 | 否 |
| 23 | 18 | 是 | 否 | 否 |
| 15 | 10 | 是 | 否 | 否 |
| 14 | 9 | 是 | 是 | 否 |
| 5 | 0 | 无填充 | 是 | 是 |
| 4 / 0 | 0 | 无填充 | 是 | 是 |

静态检查已覆盖映射、阈值比较、有效性与充电门控、动画释放、水印 ownership 和窗口缩放接入；真实 HLC 输出、明暗主题、0% 闪烁、点击穿透与全屏几何仍需 PGA 验收。

### 5.2 0% 到水印的时间差，以及 20 秒关机

用户实测“先显示 0%，稍后出现水印”的旧实现原因是严格 `<0`：HLC 为 5% 时映射恰好为 0，要等到 4% 才出现水印，两个值在界面都显示为 0%。用户随后明确要求“显示 0% 就立即显示水印”，因此当前公共临界规则已改为 `<=0`；现在 HLC 5% 且 No 时，百分比和水印在同一次 UI 刷新出现并请求计时，不再等到 4%。电池仍每 4 个 tick 查询一次，首次观测到 HLC 变化的时间仍受该采样间隔和 CPU/风扇命令耗时影响，没有额外延迟水印的计时器。

用户确认新版 HLC 支持 `hlc --shutdown` 后，主动关机流程如下：

1. Controller 将 Widget 的水印状态信号以 `QueuedConnection` 传给 `PgaRuntimeStatusService::setBatteryChargerRequired()`。服务仅在运行中、当前样本仍符合临界条件、尚未尝试关机且没有正在等待时启动 20000 ms 单次 `Qt::PreciseTimer`。重复 true 不重置计时。
2. 新平台状态不满足临界条件时，服务立即取消计时，不等待 UI 回传。充电 Yes/Full、有效电量恢复、Unknown/无效值会取消本轮；Widget 关闭电池栏发送 false 也取消。Unknown 不冒充未充电。
3. 到期额外查询一次 `hlc -b` 并发布新快照，重新使用同一 `batteryChargerRequired()` 判定。这样在常规 4-tick 缓存尚未刷新时，最后时刻插电也能取消关机；命令失败或字段无效不会按旧样本关机。
4. 新鲜样本仍确认临界低电量且 No 时，标记本轮已尝试，通过 `QProcess::startDetached()` 启动 `hlc --shutdown`。这里的成功仅表示进程启动被接受，不表示设备已完成关机；进程由操作系统管理，不套用普通查询的 2 s 超时/kill。
5. 关机进程成功启动后继续电池、CPU 温度和风扇轮询，直到系统/应用实际退出；启动失败仍写入现有 `lastError` 并输出日志。无论进程是否成功启动，同一轮低电量都不自动重试。Unknown/临时无效值不会清除已尝试标记；只有明确充电或有效显示电量恢复到 `>0` 才允许下一轮，0% 不再算恢复。

2026-10-30：用户确认部分旧机器虽有 HLC，但 `--shutdown` 可能不生效。因此不能仅凭 `startDetached()` 成功就停止 PGA service；命令无效而设备继续运行时，原电池轮询仍可采样到 `Battery Charging Status: Yes`，经平台状态链取消计时并隐藏水印。保持同一个关机请求流程，不增加 HLC 版本探测、执行失败探测或自动重试。

关机定时器由 PGA service 以 QObject parent 拥有，在同一个工作线程执行并随服务销毁。`stop()` 会停止它，之后排队到达的 true 不会重新计时；controller 继续使用原来的 stop → quit → wait 清理顺序。倒计时从 GUI 显示 0% 且 No、发出水印需求开始；既有同步 HLC 工作与到期确认查询可能令实际启动关机命令略晚于 20 秒，不承诺硬实时截止。

本次保持水印源文案和翻译表不变，不增加弹窗、额外周期探测、关机重试或旧 HLC 兼容路径。静态检查覆盖重复信号、插电取消、到期新鲜确认、Unknown、失败去重及 stop 后排队消息；20 秒时序和最终设备关机仍待 PGA 设备验收，本次未执行真实关机命令。

---

## 6. 平台差异与兼容性

- PGA：创建平台服务线程，启用平台态链路
- 非 PGA（Win32 等）：不创建平台服务，仍只走既有设备态链路

因此这次改造对非 PGA 行为是增量无侵入。

---

## 7. 相关代码锚点

- `src/plugins/core/mainwindowdevicecontroller.cpp`
  - `initializePgaRuntimeStatusService()`
  - `handlePlatformRuntimeStatusUpdated(...)`
  - `onDeviceRealTimeStatusUpdated(...)`
- `src/plugins/core/pgaruntimestatusservice.h`
- `src/plugins/core/pgaruntimestatusservice.cpp`
- `src/plugins/core/platformruntimestatus.h`
- `src/plugins/core/deviceinfowidget.h`
- `src/plugins/core/deviceinfowidget.cpp`
- `src/plugins/core/mainwindow.cpp`
- `src/libs/utils/ihardwaresettings.h`
- `src/libs/utils/hardwaresettings/pgahardwaresettings.h`

### 7.1 2026-06 Fan Apply Follow-up

- PGA fan mode is no longer a one-shot `hlc --set-fan-mode` call on the UI thread.
- `PGAHardwareSettings` keeps the desired fan mode cache for the current application session.
- Every application startup resets the desired mode to `Fan Quiet` (`0`); it no longer restores `APP/FanMode` or adopts the hardware's previous mode.
- `PgaRuntimeStatusService` owns the periodic fan apply loop on its worker thread.
- The worker starts with Quiet as its defensive default, and `start()` immediately applies that desired mode once before starting the timers.
- Every 3 seconds the worker thread issues:
  - `hlc --set-fan-mode <mode>`
  - `hlc --fan-temp-apply <temp>`
- The temperature source order is:
  - RFU temperature from `DeviceRealTimeStatus::temperature` when the current device has a valid runtime temperature
  - CPU temperature from `PlatformRuntimeStatus::cpuTemperature` when RFU temperature is unavailable
- `MainWindowDeviceController` bridges the RFU temperature from the existing device runtime chain into `PgaRuntimeStatusService`, so the `Utils` layer does not depend on Core runtime types.
- A FanMode selection in `PreferenceDialog` updates the session cache and is queued to the worker immediately, but it is intentionally not restored on the next application startup.

---

## 8. 结论

本次不是“替换旧实时状态链路”，而是“在 controller 侧新增一条平台态支路，并与旧设备态链路并行接入同一 Widget”。

这保证了：

- 新需求（PGA CPU/电池）可落地
- 旧行为（DeviceRealTimeStatus error/warning/RLO）不回归
- owner、线程和平台边界清晰，后续扩展仍可沿用该分层模式
