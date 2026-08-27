## 背景

- AM 的问题不是单个业务独有，而是所有“软键盘先提交数值/单位，随后业务层再同步修正数值”的数值属性链路共有的问题。
- 频率/时间类属性在软键盘提交时会先把 `currentUnit` 写成用户本次编辑单位；如果业务层紧接着在 `editingFinished` 回调中把数值夹回合法值或回滚原值，当前公共逻辑仍会保留本次编辑单位，导致合法值显示和后续打开键盘时都沿用错误单位上下文。

## 方案

- 修复下沉到 `PropertyBindingHelper::prepareNumericKeyBoard(PropertySystem::IProperty*, QObject*)`。
- 在一次键盘编辑会话中记录：编辑前的 value/currentUnit/displayText，以及本次键盘提交的数值。
- 当属性在键盘仍存活时被业务层改成“不同于本次提交值”的数值时，判定为业务修正：
  - 若修正结果回到编辑前数值，则恢复编辑前的单位/displayText。
  - 若修正结果是新的合法值，则对 `Frequency/Time` 清理本次编辑残留单位，按合法值重新归一化显示。
- 关闭键盘时仅在本次提交未被业务修正的情况下持久化单位，避免 `finished` 再次把错误单位写回 property。
- 删除 AM 中新增的局部特判，避免公共修复与局部补丁重复叠加。

## 预期覆盖

- AM / FM rate
- Pulse width / period
- Ramp period
- 以及其他通过同一 `prepareNumericKeyBoard(property, triggerObj)` 打开、并在 `editingFinished` 中同步修正值的频率/时间类属性

## 验证

- 静态检查 `src/libs/business/utils.cpp` 与 `src/plugins/analog/amplitudemodulation.cpp`，确认没有新增错误。