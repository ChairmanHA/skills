# 2026-03-25 TriggerSource / RefClockSource 持久化文档更新

## 目标

- 更新知识库，记录当前配置加载后是否会重新下发设备配置。
- 更新知识库，记录 `TriggerSource` / `RefClockSource` 当前持久化格式的真实语义与兼容性边界。
- 本次只更新文档，不改动现有持久化实现，等待后续单独决策是否改造。

## 已核对事实

- `MainWindow::loadSettingsFile()` 中，`CommonDeviceProfile::restoreSettings()` 本身不会直接触发设备配置。
- 同一函数后续会先终止当前激活业务，再调用 `selectBusiness2Work()`，因此完整“加载配置文件”流程不是只刷新 UI，而是会尝试重新激活业务并重新下发设备配置。
- 若当前设备未 open，则不会在该时刻立即完成设备下发；待设备 open 后，`BusinessManager` 会根据 `currentDeviceOpenStateChanged(true)` 再次驱动当前激活业务执行配置。
- `TriggerSource` 与 `RefClockSource` 当前都以 `unsigned int` 数值持久化。
- 该数值并不是“稳定字符串哈希协议值”，而是 `Utils::Id` 在当前进程内按初始化顺序分配的运行时编号。
- 因此当前 JSON 数值格式不应被视为跨版本、跨初始化顺序变化的长期兼容承诺。

## 文档更新计划

- 在 `htra_reference_clock_integration.md` 中补充：
  - “加载配置文件”与“仅 restoreSettings”之间的语义区别。
  - `RefClockSource` 当前数值持久化的兼容性风险。
  - 与 `TriggerSource` 同类问题的说明，以及“暂不改造、等待后续决定”的状态说明。
- 在 `device_open_ui_config_flow.md` 中补充：
  - `loadSettingsFile()` 恢复 common 配置后，依靠业务重新激活完成设备重新下发，而不是靠 `restoreSettings()` 直接触发。

## 本次不做

- 不修改 `Profile.json` 格式。
- 不引入字符串化持久化或旧格式迁移逻辑。
- 不修改 `TriggerSource` / `RefClockSource` 的运行时桥接方式。