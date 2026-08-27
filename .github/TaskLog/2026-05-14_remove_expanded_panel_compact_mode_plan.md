# 2026-05-14 删除 5 月扩散的进阶 compact mode 计划

## 目标

- 删除 2026-05-07 ~ 2026-05-09 为 minibar / hosted panel 扩散出来的 panel 级 compact mode。
- 保留 2026-04-15 为主窗口宽度收窄引入的基础 `SwitchButton::compactMode` 属性与主题选择器。

## 删除边界

- 删除 `src/plugins/analog/compactpanelhelper.h` 与 `src/plugins/htra/compactpanelhelper.h`。
- 删除 analog / htra panel 上新增的 `supportsCompactMode()`、`setCompactMode(bool)`、`compactMode() const`、`m_compactMode` 与 helper 调用。
- 删除 `src/plugins/core/panel.h/.cpp` 中 `PanelDisplayMode`、`applyPanelDisplayMode()` 以及 compact 虚接口。
- 删除 `FancyTabWidget`、`MiniBarWindow` 中仅用于显式恢复 `Normal` 的调用点。
- 删除 `SwitchButton::setCompactMode()` 中 2026-05-09 新增的隐藏 label / 改高度 / 改 grid stretch / 直接压按钮最小宽度的增强逻辑，仅保留 2026-04-15 版本的动态属性 + 内边距 + 样式刷新。
- 删除 `StepSweepPanel` 自己的 compact 实现。

## 保留边界

- 保留 `SwitchButton` 的 `Q_PROPERTY(compactMode ...)`、`setCompactMode()` 基本骨架。
- 保留 `configuration/theme.css` / `configuration/theme_light.css` 中 `SwitchButton[compactMode="true"]` 的基础 QSS 规则。

## 风险点

- `CMakeLists.txt` 里显式列出的 helper 头文件需要同步移除。
- 删除 `Panel` compact 虚接口后，所有 override 声明也必须一起删掉，否则会产生编译错误。
- `StepSweepPanel` 当前已无 compact 调用点，但删除其实现时仍需一并移除头文件声明和状态字段。

## 验证

- 先用文本搜索确认 `compactpanelhelper.h`、`supportsCompactMode`、`applyPanelDisplayMode`、`PanelDisplayMode`、`applyCompactMode`、相关 override/字段无残留。
- 再对已改文件跑一次错误检查，确认没有悬空声明、缺少 include 或 CMake 残留。

## 后续最小接线：popup-local SwitchButton compact

- 仅在 `MiniBarWindow::showBusinessPanelPopup()` 中接入一个局部 helper，不恢复 panel 级 compact 接口。
- helper 只遍历当前 hosted business panel 下的 `SwitchButton`，调用 `setCompactMode(true/false)`。
- 实际调用点放在 panel 已经 `setParent()` 到 popup、且 popup 已经 `setStyleSheet()` 之后，再执行 `setCompactMode(true)`，让 popup 局部 QSS 真正命中。
- 这次先不做 close/release 时的 `false` 恢复，按当前需求仅用于 minibar 模式下看样式效果。

## 补充结论：QSS 选择器 / setProperty / setter

- `SwitchButton[compactMode="true"]` 这类 QSS 选择器只读取属性值并决定是否命中样式，不负责写入属性。
- 对 `SwitchButton` 来说，`QObject::setProperty("compactMode", true)` 与直接调用 `setCompactMode(true)` 最终都会走到同一个 setter，因为该属性已通过 `Q_PROPERTY(bool compactMode READ compactMode WRITE setCompactMode)` 注册到 Qt meta-object。
- 如果对象上不存在同名 `Q_PROPERTY`，`setProperty()` 才会退化为“仅写一个 dynamic property”，不会执行自定义布局调整和刷新逻辑。
- 当前方案的责任边界是：能纯靠 QSS 表达的视觉变化收敛到主题样式；QSS 无法承担的内部 `QHBoxLayout` margin 收紧与显式 repolish 保留在 `SwitchButton::setCompactMode()`。
- 代码里优先直接调 `setCompactMode(true)`，只是为了表达更直接、类型更安全，也避免属性名拼写错误时悄悄变成 dynamic property。

## 文档同步后的当前口径

- 仓库已删除扩散到 `Core::Panel`、analog / htra panel、`StepSweepPanel` 的 panel-level compact mode。
- 当前唯一保留的 compact 能力是基础控件 `SwitchButton::compactMode`。
- 当前唯一主动写入 compact 的宿主代码，是 `MiniBarWindow` business popup 中的局部 helper：在 panel 已挂入 popup 且 popup 已安装样式后，遍历 hosted panel 下的 `SwitchButton` 并执行 `setCompactMode(true)`。
- sweep popup 不再通过 `StepSweepPanel::setCompactMode(true)` 承载，而是直接按 normal host 形态复用现有 panel。