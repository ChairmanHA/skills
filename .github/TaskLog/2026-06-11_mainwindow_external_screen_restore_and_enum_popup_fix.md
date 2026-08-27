# MainWindow 外接屏恢复最大化与 EnumTextButton 断屏弹窗修复

## 问题

1. 用户在外接大屏上最大化 MainWindow 后最小化，再从任务栏恢复时，窗口不能继续以外接屏的最大化状态显示。
2. 外接屏断开后，主屏上的 EnumTextButton 下拉 popup 宽度计算异常，内容显示不全。

## 本地根因假设

### 1. MainWindow 恢复最大化

当前 Windows 路径只有 frame refresh，没有在“最小化恢复后的再次显示”这一时机按窗口当前实际所在屏重新确认最大化边界。

对于 frameless 窗口，这会让 Win32/Qt 在恢复时沿用不准确的 monitor/maximize 结果；代码里也没有在 show 后做一次按当前 screen 的补偿。

### 2. EnumTextButton popup 宽度与屏幕选择

当前 EnumTextButton 在 ListMode 下固定把 popup 宽度设为按钮宽度，并在定位时使用 `QDesktopWidget::screenNumber(this)` 取屏幕可用区域。

这两个假设在外接屏断开后都不稳：
- 屏幕选择可能仍落在旧索引或错误屏幕，而不是 popup 实际所在屏。
- 宽度只跟按钮宽度绑定，无法保证覆盖最长 item 文本，断屏后更容易出现内容被裁剪。

## 最小判别检查

1. `MainWindow::showEvent()` / Windows chrome 路径中是否存在“恢复显示后按当前 screen 重新确认最大化”的逻辑。
2. `EnumTextButton::updatePopupPosition()` 是否仍依赖旧的 `screenNumber(this)` 路径。
3. `PopupWidget::popup()` 在 ListMode 下是否只使用外部传入固定宽度，而不考虑 item 内容宽度。

## 最小修改策略

1. 在 MainWindow 的 Windows 显示路径中补一个延迟同步点：仅当窗口应处于最大化态时，按当前 screen/work area 重新确认最大化显示，避免扩大到普通 show 流程。
2. 给 EnumTextButton popup 引入“当前屏实际可用区域”选择：优先 `screenAt(globalPoint)`，退回 popup/window/button 所在屏，最后才退回主屏。
3. 让 PopupWidget 在 ListMode 下把最终宽度收敛为 `max(按钮宽度, 内容宽度)`，并保持现有定位与视觉行为不变。

## 验证口径

1. 触达文件无新增静态诊断错误。
2. 静态确认主窗口恢复路径增加了按当前 screen 的最大化同步。
3. 静态确认 EnumTextButton popup 的屏幕裁剪不再绑定旧 `screenNumber(this)`，且宽度不再只依赖按钮宽度。
