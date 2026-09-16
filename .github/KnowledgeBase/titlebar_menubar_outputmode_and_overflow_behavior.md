# TitleBar / QMenuBar / OutputMode / Overflow 当前实现说明

## 2026-06-12 多屏 popup 维护入口

TitleBar 菜单 popup 的多屏/断屏问题已沉淀到 [多屏 Popup 几何与屏幕拓扑变更处理](multiscreen_popup_geometry_and_screen_topology.md)。

维护边界：

- `TitleBar` 负责接入的 `QMenuBar/QMenu` 菜单树几何刷新、overflow 按钮换肤、屏幕拓扑变化后的 popup 缓存失效。
- `TitleBar` 不负责菜单 action 注册、业务菜单结构、主窗口 Win32 非客户区边界。
- 断屏后菜单宽度异常时，优先检查 `TitleBar::scheduleMenuGeometryRefresh()` 和递归 `refreshMenuGeometry(QMenu *)`，不要先用 QSS 固定 `QMenu` 宽度掩盖问题。

本文记录当前主窗口 TitleBar 的最终落地实现，重点覆盖四件事：

- 标题栏几何结构如何分工。
- `QMenuBar` 与 `Single / Continue` 为什么现在是“并排但解耦”的关系。
- Qt 5.15 menubar overflow 扩展按钮当前如何被识别和换肤。
- 当窗口宽度充足、压缩、切换主题时，界面会出现什么行为。

本文描述的是当前代码现状，不再展开已废弃的 `cornerWidget` 方案。

## 1. 设计目标

当前 TitleBar 的设计目标很明确：

- 菜单系统只负责菜单项与自身 overflow。
- `Single / Continue` 属于标题栏业务操作按钮，但视觉上要紧跟菜单栏。
- 宽度足够时，菜单栏与输出模式按钮并排显示，右侧空白留在窗口控制按钮之前。
- 宽度不足时，先收缩右侧空白，再由 `QMenuBar` 自己进入 overflow，不让扩展按钮与业务按钮争抢同一块几何区域。

这套目标对应的几何关系可以概括为：

`[logo][QMenuBar][Single/Continue][spacer][min][max][close]`

`ui-mode=instrument` 是明确例外：隐藏 `logo` 后，布局直接变为
`[QMenuBar][Single/Continue][spacer]`；菜单与相邻命令组的既有顺序和间距不变，
main 模式仍保留完整结构。

注意：这里 `Single / Continue` 虽然视觉上紧跟 `QMenuBar`，但它们并不属于 `QMenuBar` 内部，也不再挂在 `cornerWidget` 上。

## 2. 当前布局结构

`src/plugins/core/titlebar.ui` 当前顶层是一个三段式 `QHBoxLayout`：

- 左侧：`iconLabel`
- 中间：`menubarLayout`
- 右侧：`horizontalLayout`

instrument 模式通过 `TitleBar::setLogoVisible(false)` 隐藏 `iconLabel`。Qt 布局会同时
回收该控件的占位和相邻间距，因此 `menubarLayout` 自然成为最左侧可见项；不需要重建
菜单栏或修改 action 注册。

顶层 stretch 当前为：

- `0,0,1`

这意味着：

- `iconLabel` 按内容取自然宽度。
- `menubarLayout` 按内容取自然宽度，不主动吃掉剩余空间。
- `horizontalLayout` 拿到额外空间，因此右侧的 spacer 才是那块“可伸缩空白”的真正承载者。

其中两段关键子布局如下。

### 2.1 `menubarLayout`

`menubarLayout` 内部当前顺序是：

- `QMenuBar`
- `outputModeLayout`

运行时 `TitleBar::setMenuBar()` 使用 `insertWidget(0, m_menubar)`，明确保证菜单栏插在最左侧；`TitleBar::setOutputModeWidget()` 则把输出模式容器放进 `outputModeLayout`。

因此中间区域的实际效果是：

- 菜单项先显示。
- `Single / Continue` 紧跟在菜单栏右侧。
- 两者视觉上连续，但层级上仍属于两个独立 widget。

### 2.2 `horizontalLayout`

右侧布局当前顺序是：

- `horizontalSpacer_2`
- `btnMin`
- `btnMax`
- `btnClose`

因为顶层额外宽度主要给了这一段，所以窗口宽度充足时，看起来就是：

- 菜单栏与 `Single / Continue` 挨在一起。
- 右侧留出一整段空白。
- 三个窗口控制按钮固定贴在最右侧。

这正是目前想要的视觉关系。

## 3. MainWindow 接入链路

主窗口接入分两条线。

### 3.1 标题栏本体的安装

`MainWindow` 构造时创建 `TitleBar`，然后直接把它设为主窗口布局的 menu bar：

- `m_titleBar = new TitleBar(this);`
- `layout()->setMenuBar(m_titleBar);`

这说明当前自定义标题栏本身就是主窗口顶部框架的一部分，而不是普通内容区 widget。

### 3.2 菜单栏与输出模式按钮的装配

`registerDefaultContainers()` 里通过 `ActionManager::createMenuBar(...)` 创建标准 `QMenuBar`，随后调用：

- `m_titleBar->setMenuBar(menubar->menuBar());`

之后在 `registerDefaultActions()` 中创建两个 `QPushButton`：

- `btnSingle`
- `btnContinue`

这两个按钮被包装进一个 `outputModeWidget`，再通过：

- `m_titleBar->setOutputModeWidget(outputModeWidget);`

插入到 `TitleBar` 的 `outputModeLayout` 中。

因此当前职责边界是：

- `ActionManager` / `QMenuBar` 继续负责顶部菜单。
- `MainWindow` 继续负责 `Single / Continue` 的业务逻辑、Action 注册和互斥状态。
- `TitleBar` 只负责把这两类 widget 摆到正确几何位置。

## 4. 为什么不再使用 cornerWidget

旧思路把 `Single / Continue` 放在 `QMenuBar` 的右上角，会带来两个根本问题：

- `QMenuBar` 的 overflow 按钮和业务按钮共用右端保留区。
- 一旦宽度继续压缩，overflow 触发后，内部扩展按钮和 corner 区域很容易在视觉上互相挤压。

当前实现把输出模式按钮迁出 `QMenuBar` 后，几何职责变成：

- `QMenuBar` 只处理菜单项与自己的 overflow。
- `TitleBar` 自己承载输出模式按钮。

这使得 overflow 行为重新回到 Qt 自己能稳定处理的范围内，也让标题栏业务按钮不再依赖 `QMenuBar` 的内部布局细节。

## 5. 宽度行为：为什么现在会先留空、再 overflow

这一点由两层策略共同决定。

### 5.1 `QMenuBar` 不再使用 `Expanding`

`TitleBar::setMenuBar()` 当前把菜单栏的横向 size policy 设为：

- `QSizePolicy::Preferred`

这表示 `QMenuBar` 在空间充足时优先保持自然宽度，而不是主动吞掉所有剩余空间。

直接效果是：

- 宽度够时，菜单栏只占自己需要的宽度。
- `Single / Continue` 会自然跟在其后。
- 多出来的空间交给右侧 spacer，形成“按钮右侧留空”。

### 5.2 顶层 stretch 让右侧 spacer 成为第一收缩对象

由于顶层 stretch 为 `0,0,1`，多余空间先分配给右侧 `horizontalLayout`。当窗口变窄时，也会先压缩这一段中的 spacer。

因此宽度变化的阶段性行为是：

1. 宽度充足：`QMenuBar` 与 `Single / Continue` 紧邻显示，右侧存在空白。
2. 宽度开始变窄：右侧空白先缩小。
3. spacer 被压到极限后：才开始真正压缩 `QMenuBar` 可用宽度。
4. `QMenuBar` 宽度不足以容纳全部菜单项后：Qt 自动出现 overflow 扩展按钮。

这比旧方案稳定得多，因为按钮与 overflow 不再争同一块区域。

## 6. overflow 扩展按钮的当前处理方式

Qt 5.15 的 menubar overflow 不是简单文本 `>>`，而是 `QMenuBar` 内部在必要时创建一个 objectName 为：

- `qt_menubar_ext_button`

的 `QToolButton`。

当前 `TitleBar` 的做法是：

### 6.1 通过 event filter 观察 menubar 生命周期变化

`TitleBar` 对 `m_menubar` 安装了 event filter，并在以下时机刷新扩展按钮：

- `QEvent::ChildAdded`
- `QEvent::LayoutRequest`
- `QEvent::Resize`
- `QEvent::Show`

其中 `ChildAdded` 用来捕获 Qt 在 overflow 触发时动态创建内部扩展按钮的那一刻。

### 6.2 定位内部扩展按钮并替换图标

`updateMenuBarOverflowButton()` 会通过：

- `findChild<QToolButton *>("qt_menubar_ext_button")`

定位该按钮，然后执行：

- 开启 hover 属性。
- 关闭 `autoRaise`。
- 替换 icon。
- 保持 `ToolButtonIconOnly`。
- 重新 polish 并触发布局刷新。

这意味着当前方案只对 menubar 的内部扩展按钮做最小必要干预，不会影响普通 toolbar button 或别的 `QToolButton`。

### 6.3 图标资源按主题切换

当前资源在 `src/plugins/core/core.qrc` 中定义：

- `:/Core/Custom/menubar_overflow_bright.svg`
- `:/Core/Custom/menubar_overflow_dark.svg`

选择规则是：

- 深色主题使用亮色图标。
- 浅色主题使用暗色图标。

`TitleBar::onThemeChanged()` 在更新 logo 后，会再次调用 `updateMenuBarOverflowButton()`，因此主题切换后，overflow 图标也会同步更新。

## 7. 当前实现明确保留与明确取消的点

为了避免后续维护时又回退到之前踩过的坑，这里明确记录当前边界。

### 7.1 当前明确保留的做法

- `Single / Continue` 保持为标题栏业务按钮，不并入菜单系统。
- overflow 图标通过运行时捕获 `qt_menubar_ext_button` 来替换。
- 主题切换时由 `TitleBar::onThemeChanged()` 统一刷新 logo 和 overflow 图标。

### 7.2 当前明确取消的做法

- 不再把输出模式按钮塞回 `QMenuBar::setCornerWidget(...)`。
- 不再对 overflow 按钮做额外 `iconSize` 放大。
- 不再通过主题 QSS 去人为放大 `qt_menubar_ext_button` 的最小宽度或 padding。
- 不再对该按钮单独安装 `QProxyStyle` 去改几何参数。

当前收口思路非常明确：

- 只解决“图标亮度/主题适配”问题。
- 不再试图通过额外几何放大去改变 Qt 内部 menubar overflow 的空间分配。

## 8. 维护建议

后续如果还要调整 TitleBar，请优先守住以下边界：

- 菜单系统问题优先在 `QMenuBar` 可用宽度、布局顺序、overflow 图标替换层面处理。
- 标题栏业务按钮继续放在 `TitleBar` 自己的布局里，不要重新挂回 `cornerWidget`。
- 如果只是希望“宽度够时右侧留空”，优先检查 stretch、spacer、sizePolicy，而不是去改菜单 Action 顺序。
- 如果 overflow 图标观感有问题，优先检查 SVG 资源与主题切换链路，不要先动 Qt 内部按钮的几何逻辑。

## 9. 结论

当前 TitleBar 的实现已经把三个层次分清楚了：

- `QMenuBar` 负责菜单与 overflow。
- `TitleBar` 负责整体几何编排。
- `MainWindow` 负责 `Single / Continue` 的业务状态和 Action 绑定。

因此现在的实际行为是：

- 宽度充足时，菜单栏与 `Single / Continue` 并排，右侧留空。
- 宽度压缩时，右侧空白先消失，再进入菜单 overflow。
- 主题切换时，logo 与 overflow 图标同步换肤。

这就是当前 TitleBar / QMenuBar / OutputMode / overflow 的稳定基线。

## 2026-06-24 Addendum: Screenshot Entry In The TitleBar Action Area

The title-bar action area is no longer only `Single / Continue`.
It now contains:

- `Single`
- `Continue`
- an icon-only screenshot button

This update keeps the same ownership boundary:

- `TitleBar` still only hosts the action widget to the right of the menubar.
- `MainWindow` still owns the creation, styling object names, signal wiring, and behavior of the controls placed in that widget.

Implementation notes:

- The screenshot button is added inside the existing `outputModeWidget`, not as a `QMenuBar` corner widget and not as a new standalone title-bar API.
- `Single`, `Continue`, and the screenshot button are arranged by the same `QHBoxLayout` spacing. There is no code-created separator widget between them.
- Theme switching still follows the existing QSS path; the screenshot icon is selected through dark/light QRC aliases.
- Screenshot files are saved by `MainWindow` into the runtime-root `images/` directory beside `bin/`, using the timestamp format `yyyyMMdd_HHmmss.png`.

Maintenance boundary:

- If future title-bar business controls are added near `Single / Continue`, prefer extending `outputModeWidget` again instead of moving business logic into `TitleBar`.
- If the screenshot icon appearance needs tuning, adjust the title-bar QSS object-name rules first; do not reintroduce `cornerWidget` coupling with `QMenuBar`.

## 2026-07-03 Addendum: Minibar Entry Button In The Same Action Area

The title-bar action area now also contains an icon-only `btnShowPanel` button to the right of `btnScreenshot`.

Implementation notes:

- `MainWindow` still creates and owns the button and signal wiring.
- `TitleBar` still only hosts the existing `outputModeWidget`; no new `TitleBar` business API is required.
- `btnShowPanel` shares the screenshot button's QSS geometry and pressed-state rules.
- For the standard Chinese package, the QSS source of record is `configuration_files/standard_cn/theme.css` and `configuration_files/standard_cn/theme_light.css`.
- The dark theme uses `/Core/Custom/showpanel.png`; the light theme uses `/Core/Custom/showpanel_light.png`.
- Since 2026-07-16, the button only asks `MinibarHelperController` to show the
  independent `SGStudioMiniBar` helper. It does not close or restart the main
  process, and helper startup failure leaves `MainWindow` visible.

## 2026-07-21 Addendum: Touching The Already Open Top-Level Menu

Mouse and touchscreen input do not enter the Qt Widgets menu stack through the
same event path. A physical mouse already participates in `QMenu` popup grab and
mouse replay handling. On Qt 5, an unaccepted `TouchBegin` is instead converted
by `QGuiApplication` into a synthesized left-button sequence when
`AA_SynthesizeMouseForUnhandledTouchEvents` is enabled (the default).
`WA_NoMouseReplay` controls popup mouse replay; it does not by itself accept the
original touch event or prevent touch-to-mouse synthesis.

Runtime tracing on Raspberry Pi Wayland established the exact order when the
user touches the title of the menu that is already open:

1. Wayland/Qt dismisses the popup and emits `QMenu::aboutToHide`.
2. Only after that does `TouchBegin` reach the title `QMenuBar`; by then
   `activePopupWidget()` is already null.
3. If the touch remains unaccepted, Qt synthesizes a left mouse press with
   `Qt::MouseEventSynthesizedByQt`, and `QMenuBar` opens the same menu again.

The important correction is that this is not merely a synthesized mouse event
arriving after application code hides an active popup. The native popup is
already gone before the menubar receives `TouchBegin`, so an
`activePopupWidget()`-only touch guard is necessarily too late on this Wayland
path.

`TitleBar` now owns a narrow input boundary for this case:

- The title `QMenuBar` opts in to touch events; menu items and other widgets do
  not have their touch policy changed.
- `TitleBar` observes `aboutToHide` on every top-level title menu and remembers
  that menu only through the current event-loop turn.
- A `TouchBegin` is consumed when its global position maps to the same
  just-hidden menu title. The corresponding TouchUpdate/TouchEnd/TouchCancel
  and compatibility mouse sequence are consumed as one physical gesture.
- The existing active-popup guard remains as a fallback for platforms/event
  paths where the popup is still visible when input reaches the menubar.
- Other touches remain unaccepted and retain Qt's normal touch-to-mouse path,
  so first-open, switching menus, choosing menu items, and standard widgets are
  unaffected.
- The existing physical-mouse same-menu guard remains in place.

Do not solve this issue by disabling
`AA_SynthesizeMouseForUnhandledTouchEvents` globally. Standard Qt Widgets in
this application depend on that compatibility path. Other custom controls only
need special handling when they perform business actions for both touch and
mouse themselves; in that case the handled touch sequence must be accepted in
full to avoid a second synthesized mouse action.

## 2026-07-21 Addendum: Menu Group And Tool Group Visual Boundaries

The title bar now presents two structural groups and one internal visual
subdivision:

```text
[File Device System] | [Preset Single Continue | Screenshot MiniBar]
```

Ownership remains unchanged:

- `File / Device / System` are the only visible top-level `QMenuBar` entries.
- `Preset` remains registered as `ACTION_PRESET`, but its visible entry is now a
  `QPushButton` inside `outputModeWidget`; it is no longer a plain top-level
  menubar action.
- `Preset / Single / Continue / Screenshot / MiniBar` all remain children of
  the same `outputModeWidget` and are arranged by one `QHBoxLayout` with one
  uniform spacing value.
- The separator before `Screenshot` is only a visual subdivision between text
  commands and icon commands. It does not create a second tool ownership group.

Geometry and styling boundaries:

- `titlebar.ui` owns the `QFrame#menuToolSeparator` between `QMenuBar` and the
  hosted tool widget.
- `MainWindow` creates `QFrame#toolIconSeparator` inside `outputModeWidget`
  immediately before `btnScreenshot`.
- Both separators use fixed `2 x 22` geometry. Profile QSS draws a `1px` medium
  edge plus a `1px` shadow/highlight edge to avoid a flat single-pixel line.
- Top-level menu item horizontal padding is `8px`, while group-to-group layout
  spacing is `10px` and tool layout spacing is `6px`.
- Text tools use `6px` horizontal padding. The icon-only tools keep `28px`
  icons inside `60px` button slots so their visible-content gap matches the
  text controls more closely.

Do not move `Preset` back into `QMenuBar` or split the icon commands into a
separate business widget merely to preserve these separator lines. The lines
express visual grouping only; the stable behavior boundary remains one
menubar host plus one tool host.

## 2026-09-14 Addendum: Instrument popup menu typography

In `ui-mode=instrument`, both the visible title `QMenuBar` entries and the
`QMenu` popup items use 24px text. The popup override is scoped through
`mainContent[uiMode="instrument"] Core--Internal--TitleBar QMenu::item` and is
appended after the active dark/light theme, so Main mode and unrelated popup
menus retain their base-theme typography.

The existing popup geometry remains authoritative: item minimum height stays
50px, horizontal padding and minimum width are unchanged, and submenu
ownership, touch handling, selection/disabled colors, and geometry refresh
behavior are unaffected.
