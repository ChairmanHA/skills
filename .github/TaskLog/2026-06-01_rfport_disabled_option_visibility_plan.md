# RFPort 固定枚举 + 能力禁用方案

日期：2026-06-01

## 目标
- RfPort property 初始化时就提供 Standard 与 High Power 两个枚举项。
- 初始阶段两个选项都可见，但 `enabled=false`。
- 设备 open 后仍由设备能力刷新 `display/value/enabled`，未授权或不支持的 High Power 保持可见但不可选。

## 局部假设
- 当前 High Power 选项消失的根因是 `CommonDeviceProfile::setFeatureSpec()` 直接用 `rfPortList` 重建 `enumDisplayOptions`，未保留固定全集。
- 只要把 RfPort 枚举列表改为“固定全集 + 按 `rfPortList` 标记 enabled”，UI 绑定层就会自动显示禁用项，无需改 DeviceSettingPanel。

## 实施点
1. 在 `CoreRuntimeServices::initializePropertySchema()` 为 `RfPort` 写入两个默认枚举项，均禁用。
2. 在 `CommonDeviceProfile::setFeatureSpec()` 中把 `RfPort` 的 `enumDisplayOptions` 改为固定两项：
   - 设备返回该 spec：同步其 `display/value`，并设为 `enabled=true`
   - 设备未返回该 spec：保留默认 `display/value`，并设为 `enabled=false`
3. 保留现有当前值回退逻辑，避免能力收缩后停留在不可用值。

## 验证
- 对触达文件做静态错误检查，确认无新增诊断。

## 2026-06-02 简化说明

- 现在 `IDevice::FeatureSpec` 已经具备 `enabled()` 语义，因此 `FancyDevice` 不需要再靠“是否把 HPPort spec 放进列表”来表达可用性。
- 更直接的做法是：`rfPortSpecs()` 始终返回固定全集 `Standard + High Power`，其中 `Standard.enabled=true`，`High Power.enabled` 由设备 option 决定。
- 这样 `CommonDeviceProfile` 与 `TxPipelineRuntime` 都可以统一按“spec 存在且 enabled=true 才可选/可保留”处理，省掉设备侧的条件增删列表，同时保持 UI 行为不变：默认只有 RFPort 可选，open 后若设备支持则 HPPort 变为可选。