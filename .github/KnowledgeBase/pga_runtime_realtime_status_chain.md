# PGA 平台实时状态链路设计与复用边界

本文记录 2026-05 已落地并完成测试的 PGA 状态栏实时数据链路，重点回答两件事：

1. 新增了哪条实时数据链路。
2. 旧链路复用了哪些部分、没有复用哪些部分。

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
- `batteryPercentValid` / `batteryPercent`
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
- 百分比从文本中提取整数并做 `0~100` clamp
- `Battery Lifetime` 中 `unknown/calculating` 视为无效
- 电池状态推导规则：
  - 充电标志为 `Yes/Charging` -> `Charging`
  - 百分比 `>=99` -> `Full`
  - 有剩余分钟数 -> `Discharging`
  - 否则 `Unknown`

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
- 电池区：独立于温度切换，始终显示最近平台态
  - 百分比无效 -> `--`
  - 状态无效 -> `--`

注意：温度切换节拍是 UI 展示节拍，不等于底层命令查询频率。

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
