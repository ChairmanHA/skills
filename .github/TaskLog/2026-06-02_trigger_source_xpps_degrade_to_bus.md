## 背景

- 当前 TriggerSource 的 XPPS 可用性已经通过 `FeatureSpec::enabled()` 门控：当 `gnssSettgins.pps_en != 1` 或 `gnss.locked != STATE_ON` 时，XPPS 选项会变为 disabled。
- `CommonDeviceProfile::setFeatureSpec()` 已经在 UI 枚举刷新时按 `normalizeTriggerSourceId()` 做回退，因此前端枚举层具备“XPPS 失效后退回 BUS”的能力。
- 但这条退化语义还没有完整贯穿到 `setProfile()`、`restoreSettings()` 和 runtime cached request 重放；因此在 writeback / restore / reconnect reapply 边界上，仍可能残留失效的 XPPS。

## 局部假设

- 用户提出“像 HPPort 回退到 RFPort 一样，XPPS 失效时默认退化到 BUS”是合理的，而且比退化到 External 更符合默认可用原则：BUS 不依赖外部线缆与额外时序源。
- 更稳妥的策略是“只降级，不自动恢复”：当 GNSS 重新锁定或用户重新打开 XPPS 输出时，不主动把 TriggerSource 从 BUS 切回 XPPS，避免系统悄悄改写用户当前意图。

## 便宜校验

- 检查 `CommonDeviceProfile::setProfile()` / `restoreSettings()` 是否对 `TriggerSource` 做了与 `RfPort` 同等级别的 enabled 归一化。
- 检查 `TxPipelineRuntime::normalizeRequestForCurrentDevice()` 是否只处理了 `rfPort`，未处理 `triggerSource`。
- 检查 `DeviceRuntimeBridge::onDeviceRealTimeStatusUpdated()` 与 `gpsdialog::applyGnssConfig()` 是否已经会触发 feature spec 刷新；若已存在，则不需要新增事件链，只需补归一化落点。

## 计划

- 在 `CommonDeviceProfile::setProfile()` 与 `restoreSettings()` 中对 `TriggerSource` 应用 `normalizeTriggerSourceId()`，确保 XPPS 失效时默认回退 BUS。
- 在 `TxPipelineRuntime` 中为 cached request 增加 `triggerSource` 归一化，保证设备重连或 reapply 时不会带着旧的 XPPS 再下发。
- 更新知识库，明确 TriggerSource 的 XPPS 退化策略与“不自动恢复”的设计取舍。