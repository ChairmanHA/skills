# Arb period 下溢崩溃修复

## 问题

- `Arb_Period` 当前在 property 层被定义为 `NumericProperty<quint64>`，提交编辑时也通过 `toULongLong()` 传入 `ArbDataGenerator::setPeriod(quint64)`。
- 当 UI 把 period 从 `0` 继续减小时，负值会先在属性链路里无符号下溢成极大值，再进入 generator。
- `setPeriod()` 里的边界检查建立在已经无符号化的输入之上，无法表达“负值”语义；一旦异常值被写入 `m_period`，worker 线程后续计算可能按超大长度分配或拷贝，导致崩溃。

## 目标

- 不让任何负值或下溢后的极大值进入 `m_period`。
- 保持现有合法 period 的回写和重新生成行为不变。
- 让 property/UI 最终始终回显 generator 收口后的合法值。

## 设计

- 在 `ArbDataGenerator::setPeriod()` 中去掉 `period * 4` 这种会发生无符号回绕的比较，改成直接与 `MAXDOWNLOADSIZE / 4` 比较。
- 在 `Arb_Modulation` 中为 `sampleOffset`、`samplesToUse`、`period` 三个 `NumericProperty<quint64>` 补 `min/max` 元数据，阻止 UI 继续向负方向下溢。
- 编辑完成后立刻把 property 回写为 generator 收口后的值，避免界面上残留绕回后的超大数。

## 验证

- 对修改文件做文件级错误检查，确认没有新增语法或类型错误。
- 静态检查 `period` 的 property -> generator -> property 回写闭环，确认异常值不会先发布到 worker 可见状态。