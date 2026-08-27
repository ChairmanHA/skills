# TitleBar 触摸关闭当前菜单后重复弹出修复

## Scope

- 延续 `2026-07-21_titlebar_prevent_current_menu_reopen.md` 的菜单切换语义。
- 修复树莓派触摸屏上再次触摸已展开的顶层菜单时，popup 先关闭又被合成鼠标事件重新打开的问题。
- 分析 touch 与 mouse 的 Qt 事件差异，并审计其他手工处理两类事件的控件。

## Evidence and assumptions

- 现场证据：Win32 鼠标路径已通过；树莓派鼠标可收起，触摸仍会重复 popup。
- Qt 5.15 源码证据：未被 widget 接受的触摸事件在 `QGuiApplication` 中会按照
  `Qt::AA_SynthesizeMouseForUnhandledTouchEvents` 转换为
  `Qt::MouseEventSynthesizedByQt` 鼠标事件。
- 现有修复只在 `QMenuBar` 收到 `MouseButtonPress` 且目标菜单仍是
  `activePopupWidget()` 时关闭 popup；触摸路径可能先使 popup 隐藏，再生成鼠标按下，
  因而该条件已经失效并由 `QMenuBar` 重新打开菜单。
- 假设目标交互在鼠标和触摸上相同：再次点击/触摸当前菜单应只关闭，不立即重开。

## Success criteria

- 树莓派触摸再次点击已展开的 `File` 时，菜单关闭且不会重新 popup。
- 鼠标再次点击当前菜单的既有行为不回归。
- 首次触摸菜单、触摸切换到其他顶层菜单、点击菜单项继续由 Qt 正常处理。
- 不全局关闭 Qt 的 touch-to-mouse 合成，不改变普通按钮等标准控件的点击语义。
- 触摸序列被拦截时完整消费 Begin/Update/End/Cancel，避免留下半截输入序列。

## Verification level

- `static`

## Verification checklist

- [x] `QMenuBar` 显式接收 touch，只有“同一菜单已展开”的 TouchBegin 被消费。
- [x] 被消费的触摸序列不会继续合成用于重开的鼠标事件。
- [x] 现有 MouseButtonPress 防重放路径保留。
- [x] 审计其他 touch/mouse 手工处理点并记录影响边界。
- [x] `git diff --check` 通过。

## Verification result

- 静态核对 Qt 5.15 `QApplication/QGuiApplication/QMenu/QMenuBar` 事件路径，确认
  touch 接受状态决定是否生成兼容鼠标序列，而 `WA_NoMouseReplay` 只覆盖 popup
  的鼠标重放。
- 修改局限于已纳入 Core CMake target 的 `titlebar.cpp/.h`；未全局修改应用属性。
- `git diff --check` 通过。
- 按仓库默认规则未执行编译或运行；仍需在树莓派触摸屏回归同菜单关闭、不同菜单
  切换、菜单项触发三种交互。
