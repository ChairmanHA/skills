# 2026-04-10 USB Parked Handle Reuse Plan

## Goal

- 仅聚焦 USB 设备。
- 保持当前切换语义为 `handover / park`，不把设备切换默认改成 `release / disconnect`。
- 切换回已 park 的 USB 设备时，**不得重复调用 `device_open_usb()`**。
- 对已经断开的 USB 设备，后续清理时**仍必须调用 `device_close()`**；当前实现对这一点的忽略需要修正。

## Non-Negotiable Constraints

1. 切换语义仍然是 `handover / park`。
2. 已经 park 的 USB 设备再次切回时，不允许因为框架状态机而重复 `device_open_usb()`。
3. 对检测到断开的 USB 设备，不允许仅靠 `m_skipCloseOnDestruct` 跳过清理；只要句柄仍然存在，就必须最终走一次 `device_close()`。
4. 当前工作只聚焦 USB；手工 ETH 的 release/disconnect 入口后续单独讨论。

## Root Cause In Current Code

### 1. 重复 open 的直接原因

- `DeviceManager::switchDevice()` 在切换时会先对旧 current 调用 `closeForSwitch()`，再对新 current 走 `newDevice->isOpen() ? true : newDevice->open()`。
- 现有 `FancyDevice::closeForSwitch()` 会把：
  - `m_isOpen = false`
  - `device = nullptr`
  - `channelCount = 0`
- 因此设备虽然在产品语义上被认为是 `parked`，但在驱动对象语义上已经退化回“没有句柄、未打开”，导致切回时必然再次 `device_open_usb()`。

### 2. 断开后未 close 的直接原因

- `updateRealTimeStatus()` 在温度查询失败时，把设备标成断开，并设置 `m_skipCloseOnDestruct = true`。
- 后续析构时直接跳过 `close()`，于是 `device_close()` 根本没有执行。

## Design Decision

### Key Principle

不要把“是否是当前 active 设备”与“是否仍持有可复用/可释放的底层 USB 句柄”混为一谈。

当前代码最大的问题，是把这两件事都塞进了 `m_isOpen` 和 `device == nullptr` 这两个状态里，导致：

- 想 park 旧设备时，不得不把句柄清掉；
- 一旦句柄清掉，切回就只能重新 open；
- 一旦标成断开，又因为析构跳过 close，句柄从未被正式 release。

### Proposed USB Session State

建议为 USB 设备引入一个明确的内部会话状态，仅在 `FancyDevice` 内部使用：

1. `Closed`
   - 没有底层句柄。
   - 下一次进入 current 时，需要真正 `device_open_usb()`。

2. `CurrentActive`
   - 当前设备。
   - 持有有效句柄。
   - 允许业务配置、轮询状态、正常输出。

3. `Parked`
   - 不是当前设备。
   - 仍保留 USB 句柄，旧设备可继续自主输出。
   - 不应主动重新 open。
   - 切回 current 时，先验证句柄是否还可复用。

4. `DisconnectedPendingClose`
   - 已确认断开。
   - 不再把句柄当可用句柄使用。
   - 但句柄尚未正式 release，必须在后续清理路径里执行 `device_close()`。

## Minimal Implementation Strategy

### A. 不改 DeviceManager 的大框架，复用现有 `open()` 入口做“resume or reopen”

这是本次最小、最稳的做法。

原因：

- `DeviceManager` 当前已经把“切到某设备”统一建模为：必要时调用该设备的 `open()`。
- 没必要把 Core 层再扩大成新接口；在 USB 设备内部把 `open()` 改成“真正 open 或复用已 park 句柄”即可。

因此：

- `closeForSwitch()` 对 USB 不再清空句柄，而是把 `CurrentActive -> Parked`。
- `open()` 对 USB 改成三段逻辑：
  1. `CurrentActive`：直接返回 true。
  2. `Parked`：先验证旧句柄；成功则恢复为 `CurrentActive`，不调用 `device_open_usb()`。
  3. `Closed / DisconnectedPendingClose`：必要时先 release 旧句柄，再执行真正的 `device_open_usb()`。

### B. 切回 parked USB 设备时，先做“温度查询探活”，再决定是否 reopen

用户提出的“切换时做简单温度查询确保句柄正确性”是合理的，而且当前代码已有现成实践。

建议切回某个 parked USB 设备时：

1. 加锁并拿到当前保存的 USB 句柄。
2. 调用 `device_query_temperature()` 做轻量探活。
3. 若成功：
   - 认为句柄仍可复用；
   - 不调用 `device_open_usb()`；
   - 直接把状态切回 `CurrentActive`。
4. 若失败：
   - 认为旧句柄已 stale；
   - 先对旧句柄执行 `device_close()`；
   - 再进入真正的 reopen 路径。

### C. 真正需要 reopen 时，立即同步执行一次 `device_list_usb()` 以刷新 dnum

只依赖 scanner 周期刷新还不够，因为：

- parked 句柄可能刚失效；
- 用户也可能在下一次扫描到来前立刻切回；
- 此时若直接复用旧 `m_deviceNumber`，dnum 可能已经变化。

因此更合理的 reopen 方案是：

1. 当 parked 句柄探活失败时，不直接拿旧 `m_deviceNumber` reopen。
2. 在 `open()` 的 USB 路径里同步执行一次 `device_list_usb()`。
3. 按当前对象的 UID 在快照中重新定位 dnum。
4. 定位成功：更新 `m_deviceNumber` 后再 `device_open_usb()`。
5. 定位失败：认为设备确实已不在，返回断开错误。

这样就把“list_usb + 温度查询”两步各自放到了正确位置：

- `device_query_temperature()` 负责验证旧句柄是否还能用；
- `device_list_usb()` 负责在需要 reopen 时给出最新 dnum。

## Disconnected Device Cleanup Rule

这是本次实现必须补上的关键规则：

- **断开设备也必须最终执行 `device_close()`。**

但应区分：

1. **Healthy release**
   - 设备仍正常在线。
   - 可以 `device_preset()` + `device_close()`。

2. **Stale/disconnected release**
   - 温度查询已失败，或已判定为断开。
   - 此时不再做 `device_preset()`，避免对坏链路做额外操作。
   - 但仍然必须做一次 `device_close()`。

换句话说：

- 断开后可以跳过 `device_preset()`；
- **不能跳过 `device_close()`。**

## Required Code Changes

### 1. `src/plugins/htra/fancydevice.h`

- 增加 USB 内部会话状态枚举，例如：
  - `Closed`
  - `CurrentActive`
  - `Parked`
  - `DisconnectedPendingClose`
- 增加 USB 句柄复用相关 helper 声明，例如：
  - 校验 parked 句柄
  - 通过 `device_list_usb()` 按 UID 刷新 dnum
  - 对 stale handle 执行 close-only release

### 2. `src/plugins/htra/fancydevice.cpp`

- 重写 `closeForSwitch()` 的 USB 分支：
  - 不清 `device`
  - 不清 `channelCount`
  - 不把 USB 会话直接退回 `Closed`
  - 只把状态转成 `Parked`
- 重写 `open()` 的 USB 分支：
  - 优先尝试 resume parked handle；
  - 仅当探活失败时才 `device_close()` + `device_list_usb()` + `device_open_usb()`。
- 重写 `close()`：
  - 不再只依赖 `m_isOpen` 判断是否需要 `device_close()`；
  - 只要句柄存在，就要根据当前会话状态执行 release；
  - 对 `DisconnectedPendingClose` 只做 close，不做 preset。
- 重写 `updateRealTimeStatus()` 的断开处理：
  - 不再设置“析构跳过 close”的 USB 语义；
  - 改为把状态切到 `DisconnectedPendingClose`，等待后续统一 release。

### 3. `src/plugins/htra/htradevicescanner.cpp`

- 当前实现可以先尽量少改。
- 因为 reopen 路径在 `FancyDevice::open()` 内已经同步调用 `device_list_usb()` 刷新 dnum，scanner 无需承担“切回时必须先刷新 dnum”的责任。
- 但建议保留现有“非 open 时刷新扫描信息”的逻辑，作为外层兜底。

### 4. `src/plugins/core/devicemanager.cpp`

- 预计无需大改。
- 现有 `switchDevice()` 逻辑可以保留：
  - 旧设备仍走 `closeForSwitch()`；
  - 新设备仍走 `open()`；
- 核心变化全部收敛在 USB 设备内部。

## Why This Plan Is Better Than A Pure 'Keep isOpen=true' Plan

另一种直觉方案是：既然要复用句柄，那 park 后就继续让 `isOpen()==true`。

不建议这样做，原因有两点：

1. `DeviceManager` 当前把 `isOpen()` 当成“当前设备是否已进入 open 成功态”的关键判据。
   - 若 park 后仍长期返回 true，容易混淆“当前 active”和“旧会话保留”两种状态。

2. 现有轮询、open-failed retry、UI 状态切换都默认 `isOpen()` 代表“current 设备当前已打开”。
   - 强行把 parked 也当 open，会扩大 Core 层语义变化范围。

因此更稳的方案是：

- `isOpen()` 继续服务当前 DeviceManager 语义；
- USB 设备内部通过新的会话状态，实现在 `open()` 里“resume parked handle or reopen”。

## Validation Plan

### Static

- 检查 `FancyDevice::closeForSwitch/open/close/updateRealTimeStatus` 四条路径状态转换是否闭合。
- 检查是否仍存在“断开后仅设 `m_skipCloseOnDestruct` 而不 `device_close()`”的遗漏路径。

### Runtime

1. 两台 USB 设备都已成功启动输出。
2. 在设备列表中 A/B 来回切换。
3. 验证：
   - 切回已 park 的设备时，不再出现新的 `device_open_usb()` 调用；
   - 当前设备可正常恢复为 `CurrentActive`；
   - 旧设备输出不被切换动作打断。

4. 拔掉其中一台已 park 的 USB 设备。
5. 验证：
   - 后续清理路径仍执行 `device_close()`；
   - 不再因为跳过 close 而留下 stale 句柄。

## Non-Goals

- 本轮不处理 ETH 的 release/disconnect 入口设计。
- 本轮不重构 `DeviceManager` 为多 current / 多 active 设备架构。
- 本轮不解决“同时实时交互控制多台设备”的更大架构问题。