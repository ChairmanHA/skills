## 背景

- 在 `StreamingPanel` 的 `MSG_QUESTION` 提示框中，标题栏关闭按钮本应隐藏，但实际仍可见，并与标题文本区域重叠。
- 该问题与历史现象一致：`setTitleButtons(NoButtons)` 后按钮切出布局但未立即从界面消失。

## 根因判断

- `src/libs/controls/dialog.cpp` 的 `TitleBar::setHeaderButton()` 在移除按钮时采用 `removeWidget + deleteLater`。
- `deleteLater` 依赖事件循环，当前调用栈内按钮对象仍是可见子控件；一旦脱离布局，可能以残留几何位置悬浮在标题栏上，造成与标题重叠。

## 修改方案

1. 标题栏按钮改为“创建后复用 + 即时显隐”，不在每次切换按钮集合时 `deleteLater`。
2. 每次重排前先统一 `removeWidget + hide()`，再按当前 `Buttons` 集合把需要的按钮重新 `addWidget` 并 `show()`。
3. 重排后主动 `invalidate/activate/updateGeometry`，确保本次事件循环内布局立即收敛。

## 修改范围

- `src/libs/controls/dialog.cpp`

## 预期结果

- `MSG_QUESTION` 使用 `TitleBar::NoButtons` 时，关闭按钮不会残留显示。
- 标题文本区域不再与关闭按钮重叠。
- 反复切换不同消息类型（有/无关闭按钮）时行为保持幂等且无重复按钮。