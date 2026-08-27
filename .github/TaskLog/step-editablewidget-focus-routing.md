# 步进编辑模式输入回到主输入区的问题（overlay 适配后）

## 现象
- 在 `PropertyBindingHelper::createKeyboardBase()` 中启用 `cfg.stepEditable` 时，标题区会插入 `Controls::EditableWidget` 用于编辑步进。
- 设计预期：当焦点在 `EditableWidget`/其内部 `stepEdit` 上时，软键盘输入应作用于步进编辑（由 `stepAd` 维护状态/最终回写 `StepController`）。
- 实际：overlay 适配后，用户进入步进编辑后，继续按软键盘数字/按键时，输入回到主输入区（`TouchNumKeyboard` 的 indicator `edit`），导致步进编辑失效/很难继续编辑。

## 根因分析（结合代码）
- `TouchNumKeyboard::clickedKey()` 当前将虚拟按键生成的 `QKeyEvent` 发送给 `QApplication::focusWidget()`。
- overlay 模式下 `TouchNumKeyboard` 被作为 overlay 子控件运行，并且 `TouchNumKeyboard::showEvent()` 会强制执行 `d->ui->edit->setFocus()`。
- 当标题区存在可聚焦控件（`EditableWidget` 内部 `QLineEdit stepEdit`）时：
  - 进入步进编辑后，焦点可能被 overlay/窗口激活/某些交互重新指向 `TouchNumKeyboard` 本体。
  - 由于 `TouchNumKeyboard` 设置了 `setFocusProxy(d->ui->edit)`，一旦键盘本体获得焦点，焦点会被代理到主输入区 `edit`。
  - 结果：`clickedKey()` 的事件目标变成主输入区 `edit`，步进编辑不再接收输入。
- 同时 `EditableWidget` 里通过 `QApplication::focusChanged` 做“失焦自动回车/退出编辑”的逻辑，对 overlay 下的焦点抖动更敏感，可能进一步加速退出步进编辑。

## 修改方案（最小侵入）
目标：在“步进编辑激活期间”，避免 overlay 导致焦点回到主输入区。

方案：在进入步进编辑时临时把 `TouchNumKeyboard` 的 `focusProxy` 切换到 `stepEdit`，在退出步进编辑时恢复。
- 在 `EditableWidget::onFocus()`（新增 override）中：
  - 先调用 `IEditableTitleWidget::onFocus()` 完成 receiver/快捷键切换。
  - 再将 `commonKeyboard` 的 `focusProxy` 临时改为 `ui->stepEdit`（保存原 focusProxy 以便恢复）。
- 在 `IEditableTitleWidget::eventFilter()` 处理 Return/Enter/Escape 时：
  - 在把焦点切回键盘之前，先恢复键盘 focusProxy（避免 `commonKeyboard->setFocus()` 又被代理回 stepEdit 造成循环）。
- 在 `EditableWidget` 的 `focusChanged` 触发“真正退出编辑”时（old == stepEdit）：
  - 退出前恢复 focusProxy，再发 focusedOut。
  - 同时加一个保护：如果焦点切换发生在键盘内部控件之间（now 是 keyboard 的子控件），不认为是退出编辑。

