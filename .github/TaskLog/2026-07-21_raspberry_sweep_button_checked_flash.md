# Raspberry Pi Sweep 按钮绿色闪烁修复

## Scope

- 修复 Raspberry Pi 上点击 CommonPanel 的 Sweep 导航按钮时，背景短暂进入绿色 enabled 样式再恢复的问题。
- 保留 Sweep 业务使能时由 `checked` 表达的绿色高亮。
- 保留进入 Sweep 页面时由 `pageHighlighted` 表达的页面选中高亮。
- 不改变 RF、MOD、General Settings 等其他按钮行为。
- 按仓库 LabelButton skill 回写 externally-owned checked 的复用规则。

## Observation and inference

- 观察：深色主题中 Sweep 的绿色背景只由 `LabelButton#sweep:checked` 规则产生，没有 Sweep 专属绿色 `:pressed` 规则。
- 观察：Sweep 当前是 checkable；Qt 用户点击会自动翻转 `checked`，随后 `clicked` 槽再调用 `setChecked(m_sweepHighlighted)` 恢复业务状态。
- 观察：`setSweepHighlighted()` 才是 Sweep enabled 状态的 owner；`sweepClicked()` 只用于导航到 Sweep 页面。
- 推断：树莓派显示链路暴露了“自动翻转 -> clicked 中恢复”之间的瞬态 checked 样式；Windows 下该中间态通常未被单独绘制。
- 设计：为 Sweep 使用 CommonPanel 局部的 externally-checked LabelButton，覆盖 `nextCheckState()` 阻止用户点击自动翻转；程序化 `setChecked()` 仍保持有效。

## Success criteria

- Sweep 未使能时点击按钮只触发导航，不产生临时 `checked=true`，因此不闪绿色。
- Sweep 已使能时仍保持绿色 checked 高亮，点击导航不会改变该状态。
- `setSweepPageHighlighted()` 的页面高亮逻辑不变。
- 不增加平台宏；Win32 与 Raspberry Pi 走同一确定性状态流。

## Verification level

- `static`

## Verification checklist

- [x] Sweep 的用户点击不再调用 QAbstractButton 默认 checked 翻转。
- [x] `setSweepHighlighted()` 仍是 checked 状态唯一 owner。
- [x] `sweepClicked()` 导航信号仍正常发出。
- [x] 深浅主题 QSS 无需修改，既有 checked/pageHighlighted 规则保持不变。
- [x] `git diff --check` 通过。

## Verification result

- Qt 5 `QAbstractButtonPrivate::click()` 的源码顺序确认：先调用 `nextCheckState()`，随后同步 `repaint()`，最后才发出 `clicked()`；因此旧实现确实会在恢复前绘制一次临时 checked 状态。
- 新实现中 Sweep 的 `nextCheckState()` 不改变状态，而 `setSweepHighlighted()` 仍可程序化调用 `setChecked()`。
- 修复不包含平台宏，Win32 与 Raspberry Pi 共用同一状态模型。
- 按仓库默认规则未执行编译或运行验证。
