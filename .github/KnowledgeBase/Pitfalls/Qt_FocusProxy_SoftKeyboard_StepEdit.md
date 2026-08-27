# Qt 软键盘 overlay 下：步进编辑（stepEdit）焦点/输入路由陷阱与修复

## 现象
在 overlay/frameless 模式下显示 `TouchNumKeyboard` 时，标题区的步进编辑框 `stepEdit` 已进入编辑状态，但点击数字键后输入却回到了软键盘主输入框（键盘内部的 `edit`），导致步进无法正常编辑。

## 根因（为什么只在 overlay 更容易触发）
- `TouchNumKeyboard` 内部有自己的主输入 `QLineEdit`（对象名通常为 `edit`），并且键盘把它设置为 `focusProxy`。
- overlay 场景下，焦点更容易在“键盘本体/键盘内部控件”之间发生短暂切换（例如点击键帽、点击标题区、样式/平台窗口管理器导致的 focus churn）。
- 软键盘按键点击的字符注入逻辑依赖 `QApplication::focusWidget()`；一旦焦点被代理/跳回键盘内部 `edit`，按键事件就会发送给错误的输入框。

## 设计约束与决策
- 软键盘 `TouchNumKeyboard` 作为通用控件，**不在键盘层引入额外的“显式输入目标”封装**（避免扩大 API 面与职责）。
- 标题区步进编辑（`EditableWidget`/`stepEdit`）是一个非常特殊的场景：它与键盘共存且需要临时接管输入。
- 因此选择 **局部修复**：在步进编辑激活期间，临时覆盖键盘的 `focusProxy` 指向 `stepEdit`，并在退出时恢复。

## 方案：EditableWidget 临时覆盖 focusProxy
实现位于 `Controls::IEditableTitleWidget`：
- `overrideKeyboardFocusProxy(QWidget *proxy)`
  - 首次覆盖时保存 `commonKeyboard->focusProxy()` 到 `previousKeyboardFocusProxy`
  - 调用 `commonKeyboard->setFocusProxy(proxy)` 指向 `stepEdit`
- `restoreKeyboardFocusProxy()`
  - 退出步进编辑时恢复之前保存的 `focusProxy`

在 `Controls::EditableWidget::onFocus()` 中调用：
- 先执行 `IEditableTitleWidget::onFocus()`（设置单位快捷键、设置 receiver、延迟选择文本）
- 然后 `overrideKeyboardFocusProxy(ui->stepEdit)`

### 退出步进编辑时的处理
`EditableWidget` 监听 `qApp->focusChanged(old, now)`：
- 当 `old == stepEdit`：表示 `stepEdit` 失焦，需要判断这是否真的是“退出编辑”。
- overlay 下可能只是焦点在键盘内部控件间移动，因此加入保护：
  - 若 `now` 仍在 `commonKeyboard` 内部，且不是用户明确点回键盘主输入框 `edit`，则不认为退出。
  - 判断方式依赖：`now->objectName() == "edit"` 且 `now->parentWidget() == commonKeyboard`。

若确实退出：
- 若并非已由 Enter/Esc/单位键等流程处理过（`alreadyQuitEditing == false`），则向 `stepEdit` 发送一次 Return 按键事件用于提交。
- `restoreKeyboardFocusProxy()`（避免后续焦点又被代理回 `stepEdit`）
- `emit focusedOut()` 通知外层恢复主输入区的 receiver/快捷键等。

### Enter/Esc 等特殊键
`stepEdit` 安装了事件过滤器：
- 捕获 Return/Enter/Escape：把事件转发给 `BaseUnitAdapter` 统一处理，并设置 `alreadyQuitEditing = true`，随后恢复 focusProxy 并把焦点切回键盘本体。

## 维护注意事项
- 该保护逻辑依赖键盘主输入框对象名 `edit`。若 UI 文件中改名或结构变化，需要同步更新判断条件。
- `overrideKeyboardFocusProxy()` 只应在“步进编辑激活”期间使用；退出务必调用 `restoreKeyboardFocusProxy()`，避免后续其他编辑场景焦点异常。
- 若未来出现多个“标题区编辑器”同时需要接管输入，建议重新评估是否需要更通用的输入上下文/目标路由（但当前项目明确选择不改键盘层）。

## 相关文件
- `src/app/ui/editablewidget.cpp`：`EditableWidget` 与 `IEditableTitleWidget` 的焦点与 focusProxy 处理
- `src/app/ui/touchnumkeyboard.cpp`：按键仍走 `QApplication::focusWidget()`（保持通用实现）
