## 背景

- 上一轮修复把 XPPS 的前置条件收到了设备侧：自动补 PPS enable、默认频率，并强制 XPPS 使用 rising edge。
- 用户明确要求放宽其中的 edge 约束：XPPS 允许用户继续选择 rising / falling，不应再由设备侧偷偷改写。
- 用户希望真正的门控前移到 UI 枚举层：只有当 `pps_en == 1` 且 `gnss_info.locked == STATE_ON` 时，Trigger In 的 XPPS 选项才可选；实现方式优先通过 property 的 `enumDisplayOptions.enabled`。

## 局部假设

- Trigger In 枚举的实际控制点不在 `DeviceSettingPanel`，而在 `CommonDeviceProfile::setFeatureSpec()` 把 `IDevice::FeatureSpec` 转成 `enumDisplayOptions`。
- 当前 `FeatureSpec` 没有 enabled 语义，且 `CommonDeviceProfile` 对 TriggerSource 选项全部写死 `enabled=true`，所以需要在这条链上补一层 enabled 状态。
- `gnss_info.locked` 是动态量，因此仅在设备 open/切换时刷新 feature specs 不够；至少还要在实时状态更新时重刷 TriggerSource options。

## 便宜校验

- 检查 `CommonDeviceProfile::setFeatureSpec()` 是否确实对 TriggerSource 选项统一写入 `enabled=true`。
- 检查 `IDevice::FeatureSpec` 是否只有 `id/displayName` 两个接口，且仓库里只有 `FancyFeatureSpec` 一个实现。
- 检查 `DeviceRuntimeBridge` 是否只在 currentDevice/openState 变化时刷新 feature specs，而不会在 `DeviceRealTimeStatus` 更新时刷新。

## 计划

- 给 `IDevice::FeatureSpec` 增加 `enabled()` 语义，并在 `FancyFeatureSpec` 中实现可变 enabled 状态。
- 在 `FancyDevice::triggerSourceSpecs()` 中根据 `gnssSettgins.pps_en == 1 && gnss.locked == STATE_ON` 更新 XPPS 选项可用性；同时把 `pps_en` 的本地默认值设为 1。
- 在 `CommonDeviceProfile::setFeatureSpec()` 中按 spec.enabled 写 TriggerSource 的 `enumDisplayOptions`，并在当前值落到 disabled 项时回退到可用源，保证用户不能保持选中 XPPS 失效态。
- 在 `DeviceRuntimeBridge::onDeviceRealTimeStatusUpdated()` 中补一轮 feature spec 刷新，并在 GNSS 配置写回成功后主动刷新一次，缩短 UI 同步延迟。
- 移除上一轮对 XPPS edge/rising 的强制收口，并同步修正文档。