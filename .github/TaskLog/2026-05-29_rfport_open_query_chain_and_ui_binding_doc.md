# 2026-05-29 RF Port Open Query Chain and UI Binding Doc

## Goal
- 在 KnowledgeBase 新增一篇文档，说明 RF Port 在设备 open 后按能力动态生效的完整链路。
- 给出 UI 层新增 EnumTextButton 并绑定 RfPort property 的落地步骤。

## Scope
- 仅文档更新，不改业务代码。
- 文档需覆盖 Core/Device/UI 三层连接点与常见注意事项。

## Design Notes
1. 先说明 RF Port 的语义与默认策略：Standard 恒定存在，High Power 由 open 后 options 查询决定。
2. 描述能力链路：
   - Property schema 预注册 RfPort。
   - FancyDevice 在 open 后执行 device_query_options，刷新 rfPortSpecs。
   - DeviceRuntimeBridge 在 current device/open state 变化时刷新 feature specs。
   - CommonDeviceProfile::setFeatureSpec 把 rfPortSpecs 转成 enumDisplayOptions，并做当前值回退。
3. 描述执行链路：
   - TxSessionService 快照 common.rfPort。
   - TxPipelineRuntime 填充 IDevice::Profile::rfPort。
   - FancyDevice::applyCommonDeviceSettingsLocked 下发 channel_config_port。
   - 写回阶段 channel_query_port -> writeback->rfPort -> CommonDeviceProfile::setProfile。
4. 描述 UI 接入步骤：
   - 在 DeviceSettingPanel 的 RF 分组加入 EnumTextButton。
   - 在 initializeBindings 获取 RfPort property 并 bindEnumTextButtonToProperty。
   - 强调 UI 不应手写选项；选项来自设备能力刷新。

## Deliverables
- .github/KnowledgeBase/rfport_open_query_and_ui_binding.md
- 更新 .github/KnowledgeBase/Index.md 的“设备/信号/性能专题”索引条目
