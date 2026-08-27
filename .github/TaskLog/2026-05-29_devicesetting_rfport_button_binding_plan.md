# DeviceSettingPanel RF Port EnumTextButton 接入计划

日期：2026-05-29

## 目标
- 在 Device Setting 的 RF Hardware 分组新增 RF Port EnumTextButton。
- 在 initializeBindings() 绑定 RfPort property。
- 保持与现有文档《rfport_open_query_and_ui_binding.md》一致：
  - 不在 UI 硬编码 RF Port 枚举选项。
  - 只依赖 property enumDisplayOptions/readOnly 驱动展示与可编辑性。

## 变更范围
- src/plugins/core/devicesettingdialog.h
- src/plugins/core/devicesettingdialog.cpp

## 实施步骤
1. 在头文件新增成员 `EnumTextButton *m_rfPortBtn = nullptr;`。
2. 在 RF Hardware 布局创建 `m_rfPortBtn`，使用 `setupValueButton`，并加入到红框对应位置（第二行左侧）。
3. 在 initializeBindings() 获取 `RfPort` property，纳入空指针校验。
4. 调用 `PropertyBindingManager::instance()->bindEnumTextButtonToProperty(m_rfPortBtn, rfPortProperty);`。
5. 做静态错误检查，确保无新编译问题。

## 设计约束
- 不新增任何设备下发逻辑。
- 不新增手工 enable/disable 逻辑。
- 不写死 Standard/High Power 文案与数值映射。
