# RemoteMiniBar Sweep 乐观 UI 与 latest-intent 回写

## Scope

- 修复 Raspberry Pi 上 Remote Sweep Panel 的 Enabled `SwitchButton` 点击后短暂恢复旧状态、再切到回写状态的闪烁。
- 让 Remote Sweep Panel 的 Enabled、Sweep Type 和数值提交统一遵循“UI 先响应，Main 权威确认”的交互原则。
- 同一 Sweep 请求回写前发生的后续操作只保留最新本地意图；不让旧回写覆盖新 UI，也不并发提交基于同一旧 revision 的完整 Sweep 状态。
- 保持 Main/`StepSweepPanel` 为业务事实 owner，helper 只持有请求确认前的临时 UI intent overlay。

## Observation and inference

- 观察：`SwitchButton` 用户点击时已经先切换内部 On/Off 状态并发出 `statusChanged`。
- 观察：`RemoteSweepPanel::requestEnabledChange()` 随即用旧 snapshot 调用 `setStatus()` 恢复按钮，然后才发送请求，形成真实的“新 → 旧 → 权威回写”绘制链。
- 观察：Sweep 请求提交完整 `SweepAuthoringState` 并携带 `expectedRevision`；两个快速请求若都基于旧 revision，后一个会被 Main 以 conflict 拒绝。
- 观察：`MinibarClient` 的全局 `latestRequestId` 只过滤 response payload，不能通知 Sweep Panel 某一个 Sweep 请求已经确认或失败。
- 推断：正确实现需要同时解决 UI 状态所有权和请求串行化，而不是只删除一次 `setStatus()`。

## Design

1. Remote Sweep Panel 保存最新权威 snapshot，并在请求未确认期间叠加一份临时 UI state。
2. 用户操作立即写临时 UI state；首个操作立即发送完整 Sweep 请求。
3. 请求飞行期间继续接受操作，但只合并到 latest intent，不再立即发送第二个旧 revision 请求。
4. `MinibarClient` 单独关联 Sweep request id，并把该请求的 accepted/error 与 response snapshot 通知窗口和 panel；全局 response 过滤规则保持不变。
5. 首个请求 accepted 后：
   - 没有后续操作且权威值与 UI 一致时，不再调用控件 setter；
   - 权威值不一致时，只刷新实际不一致的字段；
   - 有后续操作时，丢弃旧回写的显示效果，把后续字段叠加到新 revision 后再提交一次最新状态。
6. conflict 时保留全部最新本地意图，等待 Main 已有的 conflict snapshot EVT，再基于新 revision 重试；其他错误撤销临时 intent，恢复当前权威状态。
7. Main 继续是唯一业务事实源；helper 的临时状态只存在于未确认交互窗口，不进入设备/runtime ownership。

## Success criteria

- Enabled 点击后立即保持用户选择，不再恢复旧 snapshot，因此不产生旧色闪烁。
- 单次 accepted 回写若与当前 UI 一致，不重复调用 `SwitchButton::setStatus()`。
- 回写前连续 On → Off（或更多次）时，旧 accepted snapshot 不覆盖最新显示；最终只用新 revision 提交最新状态。
- Sweep Type 与数值提交使用同一 latest-intent 规则，避免完整状态请求之间的 stale revision 冲突。
- conflict 后以 Main 推送的新 snapshot 为 base 重放最新 intent；超时、传输错误或其他拒绝恢复权威状态。
- 非 Sweep 请求的 response correlation、普通 EVT、全局 snapshot sequence 和 Main runtime 路径不变。

## Verification level

- `static`

## Verification checklist

- [x] 用户点击路径不再主动恢复旧 Enabled/Sweep Type 状态。
- [x] Sweep 请求同一时刻最多一个 in flight，后续编辑只合并 latest intent。
- [x] accepted 一致值不触发控件 setter，不一致值才差分刷新。
- [x] conflict EVT 能基于新 revision 重发 latest intent。
- [x] Sweep request timeout/disconnect/error 能结束本地临时状态。
- [x] `git diff --check` 通过。

## Verification result

- `requestEnabledChange()` 不再调用 `setSwitchStatusIfChanged()` 恢复旧 snapshot；`SwitchButton` 的用户选择直接成为临时 UI state。
- Sweep request 的唯一发出点收口在 `dispatchCurrentIntent()`；请求飞行期间 Enabled、Plan、各数值字段只更新 `m_uiState` 与 edit mask。
- accepted response 先更新权威 Sweep snapshot，再比较 `m_uiState`：一致时直接清理 intent，不调用渲染 setter；存在后续编辑时只把 queued edit mask 叠加到新 revision。
- conflict response 保留 active edit mask，随后即使 Main 推送的是逻辑相同 snapshot，`applySnapshot()` 也会优先执行 rebase/retry，而不会被 equality fast-path 提前返回。
- timeout、transport error、非 conflict 拒绝及 Sweep unavailable/clear 路径都会清理本地 intent；设备不可用时不会留下无法发出的悬空请求。
- `MinibarClient` 保留全局 `latestRequestId` 过滤，同时用 request-id 集合独立通知 Sweep accepted/error；快速后续操作在旧 response signal 内发出新请求后，旧 response payload 不再通过全局 latest 检查。
- 静态断言确认旧状态恢复路径已删除、Sweep 发出点唯一、响应关联与 conflict 状态存在；`git diff --check` 通过。
- 按仓库默认静态验证规则未执行编译或运行；Raspberry Pi 仍需实机确认 SwitchButton 无闪烁及连续快速操作最终落到最后一次意图。
