# TitleBar QMenu 外接屏断开后 popup 宽度异常分析

## Scope

- 分析 mainwindow 自定义 TitleBar 上的 `QMenuBar/QMenu` 在外接屏断开后 popup 宽度计算异常、内容显示不全的可能根因。
- 对比 2026-06-11 EnumTextButton popup 修复思路，提出最小修复方向。
- 本轮只做静态分析，不编译、不运行、不修改业务代码。

## Verification Level

static

## Current Code Path

- `MainWindow::registerDefaultContainers()` 通过 `ActionManager::createMenuBar(Constants::MENU_BAR)` 创建标题栏菜单。
- `ActionManager::createMenuBar()` 先创建 parentless `QMenuBar`，之后 `TitleBar::setMenuBar()` 把它插入自定义标题栏布局。
- `ActionManager::createMenu()` 先创建 parentless `QMenu`，之后 `MenuBarActionContainer::insertMenu()` 用 `menu->setParent(m_menuBar, menu->windowFlags())` 挂到 menubar。
- `TitleBar` 目前只处理 menubar overflow 内部按钮的图标替换和几何刷新，不处理 `QMenu` popup 的显示前重算、屏幕裁剪或显示后校正。
- Windows frameless 多屏逻辑集中在 `MainWindowChromeWin`，目前只监听 `QWindow::screenChanged` 和 Win32 DPI/window-state 事件，没有监听 `QGuiApplication::screenRemoved/screenAdded` 并向 titlebar 菜单发起 popup 缓存失效。

## Local Root-Cause Hypothesis

`QMenu` 是 Qt Widget 菜单，不等同于 Windows 原生标题栏菜单。它显示时仍会创建 Qt popup 顶层窗口，并依赖 Qt 当前记录的 parent window/screen、style/QSS、action geometry 和 `sizeHint`。

外接屏断开后，主窗口/menubar 可能已经被系统迁回主屏，但部分 Qt 层上下文会晚一拍或保留旧状态：

- `QMenuBar/QMenu` 曾经在外接屏 DPI/QSS metrics 下完成过 action geometry/size hint 计算。
- popup 显示时没有项目级的 `screenAt(globalPoint)` fallback 与 available-geometry clamp。
- parentless 创建再后挂到 menubar 的生命周期，让菜单更依赖首次 show 时 Qt 自己推断 screen。
- 主题 QSS 对 `Core--Internal--TitleBar QMenu::item` 写了 `font: 16pt`、`padding-right: 50px`、`min-height: 50px`、`min-width: 140px`，因此一旦 size hint 没有按当前屏幕/字体重新计算，视觉上会直接表现为宽度不足或文本被裁掉。

这与 EnumTextButton 的问题同族，但不是同一个控件 bug：EnumTextButton 是自定义 popup 固定宽度和旧屏幕选择；TitleBar 菜单是 Qt 菜单 popup 的屏幕归属和 size-hint/action-geometry 缓存没有被显式失效。

## Why Native QMenu Can Still Fail

- `QMenu` 在当前架构中不是系统菜单栏，也不是 Win32 `HMENU` 直接接管布局；它是 Qt popup widget。
- 自定义 frameless titlebar 绕过了普通 `QMainWindow::menuBar()` 的典型使用场景，Qt 无法完全依赖平台标题栏/非客户区来管理菜单 popup。
- 多屏热插拔后，Qt 的 popup 选择屏幕与 Windows 的最终窗口迁移/重算可能不在同一时刻完成。
- Qt 内部菜单宽度通常来自 action 文本、icon、shortcut、submenu indicator 和 style/QSS 的一次性 size hint 计算；没有 action/model 变化时，不一定会主动全量重算。

## Proposed Minimal Fix

### 2026-06-12 implementation decision

User feedback narrowed the first implementation to the screen-topology-change path only.

- Do not refresh menu sizes on every `QMenu::aboutToShow` / `QEvent::Show` in the first pass.
- Put the refresh owner in `TitleBar`, because the menu objects and titlebar geometry live there, not in the Win32 chrome helper.
- Listen to `QGuiApplication::screenAdded`, `screenRemoved`, and `primaryScreenChanged`.
- Close the current active popup only when the active popup belongs to the titlebar menu tree.
- Defer one event-loop turn, then refresh the existing `QMenuBar` and all recursive `QMenu` objects with public Qt geometry/style APIs.
- Keep `aboutToShow` as a possible fallback only if the event-driven refresh is not enough in real testing.

1. 在 `TitleBar` 为接入的 `QMenuBar` 和其所有 `QMenu` 安装轻量 event filter，至少处理：
   - `QMenu::aboutToShow` 前执行菜单 geometry/cache refresh；
   - `QEvent::Show` 后按当前 anchor 全局点和当前屏幕 available geometry 做一次 clamp；
   - `QEvent::ScreenChangeInternal`/`QEvent::ApplicationFontChange`/`QEvent::StyleChange`/`QEvent::LayoutRequest` 时失效菜单尺寸缓存。
2. 菜单 refresh 建议只做 Qt 公共 API 能表达的操作：
   - `menu->ensurePolished()`;
   - `menu->adjustSize()`;
   - `menu->updateGeometry()`;
   - 对 `menu->actions()` 中的子菜单递归 refresh；
   - 必要时对 menu 执行 hide/show 前的 `QTimer::singleShot(0, ...)` 二次校正，避免 show 前 `windowHandle()->screen()` 尚未稳定。
3. 统一提取一个 `availableGeometryForPopup(anchor, popup, globalPoint)` helper，策略与 EnumTextButton/ComboBox 保持一致：
   - 优先 `QGuiApplication::screenAt(globalPoint)`；
   - 退回 popup `windowHandle()->screen()` / `popup->screen()`；
   - 再退回 anchor top-level window 的 screen；
   - 最后退回 primary screen。
4. 在 Windows 多屏热插拔路径上补一个菜单级 refresh 入口：
   - `MainWindowChromeWin` 或 `TitleBar` 监听 `QGuiApplication::screenAdded/screenRemoved/primaryScreenChanged`；
   - 事件发生后关闭当前活动 popup，并延迟一帧刷新 menubar 和所有 QMenu 的 geometry/cache；
   - 避免只依赖 `QWindow::screenChanged`，因为断屏迁移不一定先落在主窗口的 screenChanged 上。
5. 不建议的方案：
   - 不要改 QSS 去硬塞更大的 `min-width` 掩盖问题；
   - 不要改 action 文本或菜单结构；
   - 不要把菜单重新实现成自定义 PopupWidget；
   - 不要在每次 paint/resize 中频繁重建菜单，避免引入闪烁和 action 生命周期风险。

## Static Verification Checklist

- `TitleBar` 仍只承担 titlebar/menubar 几何与 popup 校正，不把业务 action 注册逻辑搬进来。
- `ActionManager` 菜单 ownership 不被破坏，已有 `QMenu`/`QAction` 对象不重建。
- popup 屏幕选择不再依赖旧 `QDesktopWidget` 或单一 widget screen。
- 子菜单、Device 动态 USB Connect 菜单、System/Language/Theme 子菜单都能经过同一 refresh/clamp 路径。
- 不改变 Linux/Wayland 菜单语义；平台差异只在 Windows 热插拔事件触发入口上最小化处理。
