# TitleBar 防止当前菜单重复展开

## Scope

- 处理 MainWindow 自定义 TitleBar 中顶层菜单的重复点击。
- 当某个顶层菜单（例如 `File`）已经展开，再次点击同一个顶层菜单时关闭当前 popup，并阻止同一次鼠标按下继续触发重新展开。
- 点击其他顶层菜单时继续沿用 Qt 原有的菜单切换行为。

## Evidence and assumptions

- 观察：当前 `TitleBar` 只对 `QMenuBar` 做 overflow 与几何刷新，没有覆盖菜单点击行为。
- 观察：Qt 的标准 `QMenuBar::mousePressEvent()` 在重复点击当前 action 时会给 popup 设置 `Qt::WA_NoMouseReplay` 后隐藏菜单。
- 观察：本项目把 parentless `QMenuBar` 接入自定义 frameless TitleBar，并重新挂接 `QMenu`，现场行为已表现为关闭后再次展开。
- 假设：用户期望的行为是再次点击当前已展开菜单时将其关闭，且不重新弹出；不是让菜单保持展开。

## Success criteria

- `File` 已展开时再次点击 `File`，菜单关闭且不会立即重新展开。
- `File` 已展开时点击其他顶层菜单，仍可正常切换。
- 菜单 action 注册、popup 内容和既有多屏几何刷新逻辑不变。

## Verification level

- `static`

## Verification checklist

- [x] 仅拦截鼠标左键点击当前已展开的同一顶层菜单。
- [x] 关闭前对活动 popup 与顶层菜单设置 `Qt::WA_NoMouseReplay`。
- [x] 其他菜单点击与非鼠标事件继续交给 `QMenuBar`。
- [x] `git diff --check` 通过。

## Verification result

- 静态验证通过：改动仅位于已纳入 Core CMake 目标的 `titlebar.cpp`。
- 按仓库默认规则未执行编译或运行验证；现场应回归同菜单二次点击关闭、不同顶层菜单切换、子菜单展开后三种交互。
