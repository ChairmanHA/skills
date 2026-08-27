# USB disconnect fallback through profile coordinator

日期：2026-08-17  
状态：已完成  
验证级别：`static`

## Scope

修复当前 USB 运行期断连后 fallback selection 直接调用 `DeviceManager::setCurrentDevice()`、绕过 `DeviceRuntimeProfileCoordinator` 的问题。运行期 fallback 必须复用正常 Device List 切换的 profile 保存/恢复和 pipeline suspension；启动期首次自动选择保持原有 seed 语义。

## Evidence

- 2026-08-17 15:32:37 的联机日志中，当前 USB2 以 `errorCode=-8` 注销后，request 3 直接切到 USB1。
- request 3 没有 coordinator 的 `m_switchInProgress`，因此 USB1 未恢复自己的 profile，反而立即接收 USB2 留在 UI 中的 `Multitone/LScan`。
- 后续 USB1 切到 ETH 时，这份错误 UI 状态被保存到 USB1 UID profile，造成 ETH 切回 USB1 后看似 load 错误。

## Design

- `DeviceManager` 增加运行期 fallback selection 请求信号，不直接依赖 UI/profile 实现。
- `preferredReconnectUsbKey != 0` 表示本次无 current 状态来自运行期 USB 断连；`registerDevice()`、解除 startup suppression、scanner snapshot 对账末尾三个 auto-select 入口统一改为发出该请求。
- `DeviceRuntimeProfileCoordinator` 接收请求并调用现有 `requestDeviceSwitch()`，因此在 `setCurrentDevice()` 前进入 pipeline suspension，在目标 open 后 restore 目标 UID profile，再执行一次最终 refresh。
- `preferredReconnectUsbKey == 0` 的启动期首次自动选择仍直接调用 `setCurrentDevice()`，保留首台设备使用启动 UI seed 的现状。
- 增加聚焦日志，记录 fallback target、profile save 路径以及 restore 使用 file/default 的结果。

## Success criteria

- 运行期 USB 断连后的所有 fallback auto-select 入口均不再直接调用 `setCurrentDevice()`。
- fallback target 通过 coordinator 完成 suspension、目标 profile restore 和单次 pipeline refresh。
- fallback 到 USB1 后，USB1 的 profile 在任何设备配置下发前恢复，不继承断连 USB2 的 UI 状态。
- 启动期首台 USB 自动选择行为不变。
- 日志可以按完整 UID 和 profile path 对账 save/restore/fallback 顺序。
- `git diff --check` 和静态引用检查通过；按仓库规则不编译、不运行。

## Result

- 新增 `DeviceManager::runtimeFallbackSelectionRequested(IDevice *)`，Core 只负责选择 fallback 候选，不直接依赖 profile/UI 实现。
- `registerDevice()`、解除 startup suppression、scanner snapshot 对账末尾三个 auto-select 入口均按 `preferredReconnectUsbKey` 区分启动首选与运行期 fallback。
- `DeviceRuntimeProfileCoordinator` 同步接收运行期 fallback 请求并复用 `requestDeviceSwitch()`，确保 `setCurrentDevice()` 前 suspend，目标 open 后 restore，再执行最终 refresh。
- 新增 `DeviceProfile: saved/restored` 和 runtime fallback 请求/拒绝日志，可按 UID、transport、source、success 和 path 对账。
- 已同步多实例 USB、HTRA 多设备、设备 open/UI 配置流及 KnowledgeBase Index。
- 静态检查通过：`git diff --check` 无 whitespace error；运行期 fallback 不再存在直接 `setCurrentDevice(fallbackDevice)` 调用。
- 按仓库默认验证规则未编译、未运行，需用两 USB + 一 ETH 场景进行 Release 联机回归。
