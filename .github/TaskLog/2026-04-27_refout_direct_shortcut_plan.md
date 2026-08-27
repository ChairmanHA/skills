# RefOut Direct Shortcut Plan

## Goal

- 将 Device Settings 页里的 `RefOut` 从 `CommonDeviceProfile -> profileChanged -> TxApplyRequest` 的重配触发链路中摘出来。
- 保留 `RefClockSource / RefClockFrequency` 在公共配置链路中，继续作为真正影响波形配置的 common setting。
- 让 `RefOut` 像 `Fan Control` 一样，直接调用当前 `IDevice` 做快捷配置，并在设备切换/打开时主动回读当前状态。

## Hypothesis

- 当前 `RefOut` 之所以会牵动业务重配，不是因为底层必须这样，而是因为它被加入了 `CommonProfileGroup`。
- 只要去掉 `SystemClockOut` 对 `profileChanged` 的贡献，同时给 `IDevice` 提供独立的 query/configure 接口，`RefOut` 就可以成为“不影响波形播放但能被普通 apply 保持”的设备局部状态。

## Design

### 1. Device shortcut

- 在 `IDevice` 增加可选接口：`querySystemClockOut(bool *)` / `configureSystemClockOut(bool)`。
- `FancyDevice` 直接基于现有 `device_query_clock()` / `device_config_clock()` 实现这两个接口。
- `configureSystemClockOut()` 只复用当前缓存的 `source_query / refFreq_query`，不再顺带走 `tx_config_ffm()`、trigger、LO 等后续通路。

### 2. UI wiring

- `DeviceSettingPanel` 的 `RefOut` 不再绑定 property click-to-set 逻辑。
- 页面显示时、设备切换时、设备 open 状态变化时，主动 query 当前设备的 `SystemClockOut` 状态并刷新按钮。
- 用户点击 `RefOut` 后，直接调用当前设备的 `configureSystemClockOut()`；成功后再把 property 镜像值同步到 `SystemClockOut`，仅作为当前状态缓存，不触发重配。

### 3. Common profile boundary

- `SystemClockOut` 继续保留在 `CommonDeviceProfile` 的运行时状态里，用于普通 apply 保持当前输出状态，避免其它配置重下发时把它意外冲回默认值。
- 但 `SystemClockOut` 不再加入 `CommonProfileGroup`，因此其 UI 改动不会触发 `profileChanged()`。
- `SystemClockOut` 不再参与 `Profile.json` 的保存/恢复；加载或重置业务配置时，不应隐式改变 RefOut。

## Non-goals

- 不改 `RefClockSource / RefClockFrequency` 的公共配置模型。
- 不改 `Trigger In / Trigger Out / LO Mode` 的当前公共配置路径。
- 不处理参考时钟失败即中止、二次 query 校验等更大范围的 ref clock 课题。

## Candidate parameters review

- `RefClockSource / RefClockFrequency`：会直接改变参考环境，必须保留在重配链路。
- `TriggerOutAction / TriggerOutEdge / TriggerOutState`：会改变对外时序标记语义，仍应跟随运行配置统一下发。
- `LoMode`：位于 `channel_config_lo_mode()` -> `tx_config_ffm()` 顺序中，仍属于公共 RF 配置，不纳入直配。
- 当前额外适合直配的仍只有 `Fan Mode` 和本次的 `RefOut`。