# Minibar Wayland layer-shell-qt Integration

> 现状说明（2026-07-21）：LayerShellQt 现在只由独立 `SGStudioMiniBar` helper
> 链接和启用；Core 中的 legacy `MiniBarWindow` 已删除。本文继续保留
> layer-shell-qt 引入过程、构建细节和长期有效的协议约束。当前问题状态和
> 标准 Debug 流程以
> [Minibar Wayland / LayerShellQt 交互与 Debug 指南](minibar_wayland_layershell_debug_guide.md)
> 为准；独立 helper 的实现边界另见 `minibar_helper_layershell_parity_gaps.md`。

本文记录 SGStudio 为解决 Raspberry Pi / Linux Wayland 下 `minibar` 的窗口语义问题而引入 KDE `layer-shell-qt` 的当前实现、构建依赖和排查入口。

## 目标问题

Wayland 下普通 `QWidget` 顶层窗口不能像 Win32 一样可靠地用 `move()` / `setGeometry()` 做全局定位，也容易被桌面环境当作普通应用窗口显示在任务栏里。`minibar` 的期望语义更接近桌面 panel / overlay：

1. 只在 `Linux + Wayland + minibar` 模式启用特殊窗口协议。
2. 不影响 Win32、Linux X11、普通 main window 模式。
3. `minibar` 不作为普通任务栏窗口出现。
4. 拖动时通过 layer-shell margins 更新位置，而不是依赖 Wayland 禁止/限制的普通全局移动。

## 引入内容

### Vendored Source

源码放在：

```text
3rdParty/layer-shell-qt-5.27.12/
```

版本选择 KDE Plasma 5.27.12 对应的 Qt5 时代源码，因为 SGStudio 当前 Raspberry Pi 构建使用 Qt 5.15.x。保留上游源码和 license；SGStudio 自己只增加本地包装层：

```text
3rdParty/layer-shell-qt-5.27.12/sgstudio/CMakeLists.txt
```

这个包装层不执行 KDE 上游根 `CMakeLists.txt`，因此不依赖 ECM / KDEInstallDirs / KDECompilerSettings 等 KDE 构建体系。

### SGStudio CMake Entry

`3rdParty/CMakeLists.txt` 在 Linux 非 Apple 平台且 `SGS_ENABLE_VENDORED_LAYER_SHELL_QT=ON` 时加入本地包装层：

```cmake
add_subdirectory(layer-shell-qt-5.27.12/sgstudio EXCLUDE_FROM_ALL)
```

构建产物：

1. `LayerShellQtInterface`
2. `layer-shell`

父 CMake 创建 `LayerShellQt::Interface` alias，并把 `layer-shell` 输出到：

```text
${SGS_BIN_DIR}/wayland-shell-integration/liblayer-shell.so
```

Qt Wayland 通过这个目录找到 shell integration plugin。

### Wrapper Responsibilities

SGStudio wrapper 做了这些事：

1. 查找 Qt5 Core / Gui / WaylandClient。
2. 要求 `Qt5::WaylandClientPrivate`，因为 layer-shell-qt 使用 QtWayland private API。
3. 用 `find_program()` 查找 `qtwaylandscanner`，避免依赖 ECM 提供的 `FindQtWaylandScanner.cmake`。
4. 用 `wayland-scanner` 生成 Wayland C 协议代码。
5. 同时生成两个协议：
   - `stable/xdg-shell/xdg-shell.xml`
   - vendored `wlr-layer-shell-unstable-v1.xml`
6. 显式启用 C 语言，因为 `wayland-scanner` 生成 `.c` 文件。
7. 显式使用 C++17，因为上游 Qt5 layer-shell-qt 5.27 源码使用 `std::optional`。

注意：即使 SGStudio 代码没有直接 include `qwayland-xdg-shell.h`，也必须生成 `xdg-shell.xml`。`wlr-layer-shell-unstable-v1.xml` 的 `set_popup` 请求引用 `xdg_popup`，缺少 xdg-shell 生成代码时运行期会出现：

```text
libLayerShellQtInterface.so.5: undefined symbol: xdg_popup_interface
```

当前 vendored integration 仍没有真正实现 layer-shell `set_popup` / `xdg_popup` 子弹窗路径；`qwaylandlayershell_p.h` 里仍保留 `TODO: Popups`。因此生成 `xdg-shell.xml` 只保证协议符号完整，不代表普通 Qt popup 会自动变成正确的 layer-shell child popup。

## Helper Integration

`src/app/minibarhelper/CMakeLists.txt` 在 Linux 且
`LayerShellQt::Interface` 存在时：

1. `SGStudioMiniBar` 私有链接 `LayerShellQt::Interface`。
2. 只为 helper 定义 `SGS_HAVE_LAYER_SHELL_QT`。
3. 让 helper 依赖 `layer-shell` plugin target。

helper `main.cpp` 在运行平台是 Wayland 时调用：

```cpp
LayerShellQt::Shell::useLayerShell();
```

这必须发生在 `RemoteMiniBarWindow` 的 native Wayland surface 创建之前。Core
不再链接或启用 LayerShellQt，主 SGStudio 进程始终使用普通 shell integration。
helper 是由 `MinibarHelperController` 使用私有认证参数启动的，不是用户可直接选择
的第二个产品入口。

### Main Process Environment Boundary

`LayerShellQt::Shell::useLayerShell()` writes
`QT_WAYLAND_SHELL_INTEGRATION=layer-shell` into the helper process environment.
环境修改不会反向传播给 parent main process。通用 SGStudio restart 仍显式移除该
变量，保证重启后的主进程不受外部或旧部署环境污染。

Current restart policy:

1. 普通设置/升级重启只使用统一的 main-window 启动参数，不再携带 UI mode。
2. Before starting the detached child process, `main.cpp` removes
   `QT_WAYLAND_SHELL_INTEGRATION` from the child `QProcessEnvironment`.
3. If the current process is a Linux Wayland packaged run and bundled Wayland
   platform plugins are present under `bin/platforms` or
   `bin/plugins/platforms`, the restart child also receives the controlled
   launcher environment: `QT_QPA_PLATFORM=wayland`, `XDG_SESSION_TYPE=wayland`,
   fallback `WAYLAND_DISPLAY` / `DISPLAY` / DBus / Pulse variables, and no
   inherited `QT_PLUGIN_PATH` or `QT_QPA_PLATFORM_PLUGIN_PATH`.
4. That packaged Wayland branch starts the child from
   `QApplication::applicationDirPath()`, matching the package launcher cwd.
5. `MinibarHelperController` 之后按需启动独立 helper；只有 helper 自己启用
   layer-shell。

## Historical MiniBarWindow Runtime Policy (removed)

以下记录描述 2026-07-16 删除前的 in-process `MiniBarWindow`，仅用于理解已经迁移
到 helper 的几何与 ownership 设计，不对应当前可编译类型。

`MiniBarWindow` 当时在 `SGS_HAVE_LAYER_SHELL_QT` 且运行时 platform name 包含
`wayland` 时配置 layer-shell：

1. `LayerOverlay`
2. `AnchorTop | AnchorRight`
3. `exclusiveZone = 0`
4. `KeyboardInteractivityNone`
5. `scope = "sgstudio-minibar"`

2026-07-03 field note: `LayerTop` was not high enough on Raspberry Pi labwc
when another application such as SAStudio was opened; the minibar became visible
only while SAStudio showed a modal confirmation dialog. `LayerOverlay` is now
used for the minibar base surface and minibar-owned layer-shell transient
surfaces so the minibar remains visible above normal/fullscreen application
windows without taking keyboard focus.

拖动路径在 layer-shell 模式下不再调用 `move()`，而是：

1. 根据鼠标 delta 计算期望 top-left。
2. clamp 到当前屏幕可用区域。
3. 保存到 `m_collapsedTopLeft`。
4. 转换为 top/right margins。
5. 如果 margins 相对上一帧发生变化，调用 `LayerShellQt::Window::setMargins(...)`。

Win32、X11、非 layer-shell fallback 保持原来的 QWidget geometry / move 路径。

`SGStudioMiniBar::RemoteMiniBarWindow` 使用相同的
`AnchorTop | AnchorRight` 约定。横向位置必须写入 right margin，即
`QMargins(0, top, right, 0)`；写到 left margin 会表现为只能垂直拖动。
该 helper 路径已经过 Raspberry Pi Wayland 测试。

### Transient Popup Layer-Shell Policy

重要发现：

`LayerShellQt::Shell::useLayerShell()` 是通过设置全局环境变量 `QT_WAYLAND_SHELL_INTEGRATION=layer-shell` 生效的。进入这个 shell integration 后，Qt Wayland 会把每个 top-level `QWindow` 都交给 vendored `QWaylandLayerShellIntegration::createShellSurface(...)`，而该实现当前无条件创建 layer surface。

这意味着在 minibar Wayland 模式下，不只是 `MiniBarWindow` 主窗口会成为 layer surface，任何仍以 top-level 形态显示的 minibar-owned transient 也会成为 layer surface。历史上包括：

1. provider `QMenu`
2. AM / FM / Pulse 等 hosted business `QDialog`
3. sweep popup `QDialog`
4. placeholder popup
5. `EnumTextButton` 内部创建的 `PopupWidget` (`Qt::Popup`)

`LayerShellQt::Window` 的默认 anchors 是 `AnchorTop | AnchorBottom | AnchorLeft | AnchorRight`。如果这些 transient 没有在显示前显式配置 layer-shell role，它们会继承默认四边 anchor，在 Raspberry Pi Wayland 上表现为全屏 popup。曾经尝试把 provider list 从 `QMenu` 换成 tool-style `QDialog`，但问题完全不变；这证明根因不是 widget class，而是全局 shell integration 给所有 top-level 分配了 layer-surface role。

当前最小修法与后续收敛：

1. 保留现有 provider `QMenu` 与 `MiniBarBusinessMenuHost` action 映射。
2. 保留业务 / sweep popup 的 `QDialog` host、标题区、QSS 和 panel 绑定，但在 layer-shell 路径下把该 `QDialog` 作为 owned overlay 内的 child widget 显示。
3. `MiniBarWindow::configureLayerShellTransient(...)` 在 transient 显示前显式设置：
   - `LayerOverlay`
   - `AnchorTop | AnchorRight`
   - `exclusiveZone = 0`
   - `KeyboardInteractivityNone`
   - scope，例如 `sgstudio-minibar-provider-menu`
   - 按目标 top-left 和 size 换算出的 top/right margins
4. provider menu、placeholder popup、`PopupWidget` 这类仍是 top-level 的 transient 继续显式配置 layer-shell anchors/margins。
5. business/sweep panel 的真正 top-level surface 变成 full-screen transparent overlay host；panel `QDialog` 自身是 overlay 内 child widget，因此 panel 外部点击仍会先进入 SGStudio，而不是直接落到后方应用。
6. transient 定位在 layer-shell 模式下不依赖 `mapToGlobal(...)` 作为最终事实，而是用 `MiniBarWindow` 或 overlay host 已提交给 compositor 的视觉 top-left 加上 anchor widget 的窗口内局部坐标。
7. vendored `QWaylandLayerSurface::setAnchor(...)` 在 anchors 改变时同步下发当前 surface size，并在已 configured 后 commit，避免 transient 从默认四边 anchor 收敛到 top/right anchor 后仍保留隐式全屏尺寸状态。
8. `PopupWidget` 自身不链接 LayerShellQt；Controls 层只提供同步的
   `prepareToShow` 扩展点。`EnumTextButton` 在 popup 尺寸确定后、首次
   `show()` 前，仅在 Wayland 平台发送该事件。`RemoteMiniBarWindow` 确认 popup
   属于当前 Sweep/MOD owner 后，同步配置 layer、output、anchors、margins 和
   size，避免首个 native surface 先按默认状态映射。Show event 只登记
   ownership/session，不再通过下一 event-loop turn 延迟配置。

维护规则：

1. 在 `QT_WAYLAND_SHELL_INTEGRATION=layer-shell` 的 minibar 进程中，新建任何 minibar-owned top-level popup/dialog 后，显示前都要显式配置 layer-shell anchors、size 和 margins；如果该 popup/dialog 改为 overlay child widget，则必须配置承载它的 overlay top-level surface。
2. 不要把“换成 `QDialog` / 换成自绘 widget”当作解决全屏 popup 的充分条件；只要它是 top-level，就仍会被 layer-shell integration 接管。
3. 不要把 2026-07-01 的“把 transient 视觉 `topLeft/size` 存成动态属性并替代 `frameGeometry()` hit test”当作当前结论；现场测试显示该方向会破坏 provider menu、frequency/power、sweep 等 popup 的打开行为，应视为已撤回假设。
4. `Controls` 层的通用 popup 不应直接依赖 LayerShellQt；由已拥有 layer-shell
   policy 的 host（当前是 `RemoteMiniBarWindow`）识别 owned popup 并在首次映射
   前配置协议状态。
5. 如果未来要从根上解决，应改 vendored shell integration 的架构：只让明确 opt-in 的窗口走 layer surface，普通 transient 回到 xdg-shell / xdg-popup。这比当前最小修法更干净，但改动面更大。

### Hosted Dialog Internal Click Dismissal

2026-07-02 的后续现场问题：`AM` 这类 hosted business `QDialog` 可以显示，但点击 dialog 内部任意位置都会立刻关闭。这不是 `AM` / `FM` 业务绑定失效，也不是软键盘创建链路本身的问题；即使点击 panel 空白处也会触发关闭。

当前更可信的根因是 activation / focus 语义：

1. `Shell::useLayerShell()` 让 business `QDialog` 也成为独立 layer surface。
2. vendored wrapper 没有实现真实 popup / child popup 路径，因此 business dialog 不是 minibar layer surface 的 compositor-level child popup。
3. `MiniBarWindow::configureLayerShellTransient(...)` 当前给 business dialog 设置 `KeyboardInteractivityNone`。
4. layer-shell 协议中 `none` 表示 compositor 不应给该 surface 分配 keyboard focus；pointer / touch 事件仍可正常发送。
5. 点击这个独立且不可获得 keyboard focus 的 layer surface 时，Qt 可能先进入 `ApplicationDeactivate` 或无法把它呈现为 `activeModalWidget()` / `activePopupWidget()`。
6. `MiniBarWindow::eventFilter(...)` 的 `ApplicationDeactivate` 分支在有 hosted transient 可见时会直接调用 `closeTransientWidgets()`，且该分支没有坐标 hit test。

因此点击 dialog 内部也会被 host 层解释成“应用失活，需要关闭当前 popup”。这条路径比单纯 `MouseButtonPress + frameGeometry()` hit test 更能解释“点击内部任意位置，包括空白区域，都会关闭”。

后续修复应优先围绕这两类方向验证：

1. business/sweep 这类需要内部交互的 hosted dialog 是否应使用 `KeyboardInteractivityOnDemand`，而 minibar base window 保持 `KeyboardInteractivityNone`。
2. `ApplicationDeactivate` 分支是否应在 hosted layer-shell transient 可见时避免立即关闭，至少不能在没有坐标证据的情况下把点击内部和点击外部合并处理。

当前最小缓解已经先落在 host close policy 上：

1. `MiniBarWindow::eventFilter(...)` 在 `MouseButtonPress` 分支里，先检查事件 `watched` 对象是否属于当前 `m_activePopup` 或 provider menu；如果属于，就直接放行，不再进入 global geometry outside-click 判断。
2. 在 layer-shell active 且 `m_activePopup` 可见时，`ApplicationDeactivate` 不再单独触发 `closeTransientWidgets()`。这是为了避免 `KeyboardInteractivityNone` hosted dialog 的内部点击被误解释为应用外部失活。
3. 这只是保证 AM / sweep 等 hosted dialog 内部点击不被 host 误关；真正的长期设计仍应评估 `KeyboardInteractivityOnDemand` 或 opt-in layer-shell 架构。

2026-07-02 的进一步现场结果显示，`StepSweepPanel` 首次打开时内部交互正常；通过 sweep 按钮关闭后第二次打开，同一个 panel 内的控件点击可以生效，但 popup host 随后关闭。该问题与软键盘无关，点击 enable 这类不会打开软键盘的控件也会复现。

当前最小修法只针对 sweep popup host 复用：

1. `closeTransientWidgets()` 关闭 `m_sweepPopup` 时，不再只 `hide()` 该 top-level `QDialog`。
2. 关闭时先从 popup layout 中移除 `m_sweepPanel`，把它 `hide()` 并 `setParent(nullptr)`，保留 panel 状态和 property binding。
3. 将 `m_sweepPopup` 置空，并对旧 `QDialog` host 调 `deleteLater()`。
4. 下一次 `showSweepPopup(...)` 复用已有 `StepSweepPanel`，但通过 `ensureSweepPopup()` 创建新的 top-level host / layer surface。
5. 因为关闭旧 host 时会把 `m_sweepPanel` `hide()` 后再 detach，下一次把它加入新 host layout 后必须显式 `show()` 并 `updateGeometry()`，再让 popup `adjustSize()`；否则第二次 dialog 会只剩 title，内容 widget 不参与布局。

这条规则的边界是：避免复用经历过 hide/unmap 的 sweep top-level layer surface。它不是对所有 hosted popup 的统一销毁策略。

2026-07-02 继续收敛后还发现一条独立的后续问题：从 sweep popup 中打开软键盘后，即使点击 `StepSweepPanel` 内部关闭软键盘，后续关闭/重开 sweep popup 仍可能出现“第一次内部点击正常，第二次内部点击关闭 popup”的状态。该问题不要求 Qt Creator 获得焦点；此前的 stale minibar drag-state 判断已撤回。

已证伪方向：

1. 在 keyboard finished 时销毁 `TouchNumKeyboardScreenOverlayHost`。现场测试显示该方向会在“关闭软键盘后再关闭 sweep panel”时导致崩溃。
2. 把 `OverlayContainer` 的 outside handler 从 press/begin 延后到 release/end。现场测试显示该方向不能解决第二次打开 sweep popup 后内部点击关闭的问题。

最终日志证据表明根因在 `MiniBarWindow::eventFilter()` 的 owned-area 判断：

1. 第二次打开 sweep popup 后，第一条点击事件可能先发给 popup 的 native `QWidgetWindow`，而不是 `StepSweepPanel` 的 child `QWidget`。
2. `QWidgetWindow` 不是 `QDialog` 的 QObject parent-chain descendant，所以 `objectBelongsTo(watched, m_activePopup)` 返回 false。
3. 同时，layer-shell 下 `m_activePopup->frameGeometry()` 和 `QMouseEvent::globalPos()` 可能处于不同坐标语义。现场日志中 popup frame 是 `QRect(695,414 560x233)`，同一次内部点击的 global pos 却是 `QPoint(230,132)`。
4. 因此对象归属和几何 hit-test 同时失败，host 把内部点击误判为 outside-click，并在 child button 收到事件前调用 `closeTransientWidgets()`。

当前最小修法：

1. hosted popup/menu 的 inside 判断不仅检查 QObject parent-chain，也把 `widget->windowHandle()` 视为属于该 widget。
2. 当 `watched` 是 `m_activePopup->windowHandle()` 或 `m_providerMenu->windowHandle()` 时，直接放行，不进入 global geometry outside-click 判断。
3. 继续保留原有 `frameGeometry()` outside-click 作为真正外部点击的 fallback；不再改 `TouchNumKeyboard` 或 `OverlayContainer`。

现场验证结果：

1. 打开频率功率扫描，点击数值弹出软键盘。
2. 点击 `StepSweepPanel` 内部关闭软键盘。
3. 点击频率功率扫描按钮关闭 sweep popup。
4. 重新打开 sweep popup 后，点击 panel 内部、点击数值再次弹软键盘、点击使能按钮均正常。
5. 后续其他问题应作为独立 bug 继续跟踪。

后续现场观察指出，AM/business panel 和 `StepSweepPanel` 没有 overlay 时，真正外部点击会直接交给 Qt Creator 等后方应用，SGStudio 完全收不到这次点击，因此无法靠 `MiniBarWindow::eventFilter()` 关闭 panel。

当前 follow-up 对 business/sweep hosted panel 使用 owned overlay：

1. `MiniBarWindow` 在 layer-shell active 时创建 full-screen transparent `miniBarPopupOverlayHost`。
2. overlay host 自身显式配置为 layer-shell top-level，并写入 `sgstudioLayerShellVisualTopLeft` / `sgstudioLayerShellVisualSize`。
3. business/sweep `QDialog` reparent 到 `Controls::OverlayContainer`，以 `Qt::Widget | Qt::FramelessWindowHint | Qt::CustomizeWindowHint` 形态显示。
4. panel 内部点击仍走 panel 原始 widget 事件；panel 外部点击由 overlay outside handler 调用 `closeTransientWidgets()`。
5. 因为 overlay host 提供 visual geometry，panel 内 `EnumTextButton` 的 `PopupWidget` 和 `TouchNumKeyboard` 仍能从 owner window 的 visual top-left 计算定位。
6. 这条规则只覆盖 minibar hosted business/sweep panel；provider `QMenu` 和通用系统键盘 dialog 不纳入该 overlay。

Keyboard follow-up: a keyboard opened from a hosted `StepSweepPanel` can be pointer-interactive even when the layer-shell overlay host does not provide normal keyboard focus. `TouchNumKeyboard` now falls back to its internal edit widget when `QApplication::focusWidget()` is outside the keyboard subtree during a synthetic key dispatch and an active receiver is configured. This keeps the receiver/adapter path unchanged, preserves no-receiver keyboard behavior, and avoids broadening the panel overlay host's layer-shell keyboard-interactivity policy.

2026-07-06 Save dialog follow-up: Linux `PxSaveFileDlg` instances opened from a
hosted minibar business/sweep panel are hosted inside the existing
`miniBarPopupOverlayHost`. `Controls::getSaveFilePath()` exposes a default-empty
execution hook path; `MiniBarWindow` registers the hook and accepts only requests
whose parent belongs to the current hosted minibar popup. Accepted Save dialogs
use `OverlayContainer::execLocalDialogLoop(...)` so the synchronous return value
is preserved, fill the fullscreen overlay host, block outside clicks without
closing the underlying panel, and then restore the previous active panel when the
dialog finishes. Win32, main-window mode, non-minibar Linux paths, and unhosted
file dialogs continue through their existing `dialog.exec()` path.

2026-07-06 Open dialog follow-up: applying the same hook to Linux
`Controls::getOpenPath()` / `PxOpenFileDlg` did not produce a correct fullscreen
hosted dialog. Field testing still showed `PxOpenFileDlg` at the screen's
upper-left with normal size-hint geometry. A follow-up attempt to broaden the
hosted-source check and temporarily constrain dialog min/max size also failed.
Those Open-dialog changes were rolled back; the `PxOpenFileDlg` fullscreen
hosted-overlay problem remains unresolved.

Do not use the hosted file-dialog hook to change the overlay host's keyboard
interactivity or to resize the overlay around `QLineEdit` focus. Field testing
showed that switching the host to `KeyboardInteractivityOnDemand` did not make
the Wayland system keyboard visible above the full-screen layer overlay, and
bottom-reserve experiments could prevent touchscreen input from entering the
filename edit state at all. The hosted Save dialog system-keyboard problem
remains unresolved and should be investigated separately from the layer/order fix.

### Drag-time Commit Optimization

layer-shell 的 `margin` 状态是 double-buffered 的：客户端发出 `zwlr_layer_surface_v1.set_margin(...)` 后，compositor 只有在对应 `wl_surface.commit` 到来时才会应用这次位置变化。

早期实现只在拖动过程中更新 `LayerShellQt::Window::setMargins(...)`。在 Raspberry Pi Wayland 上，实测现象是 mouse / touch 拖动时窗口内部已记录最终位置，但屏幕上的 minibar 往往只在 release / touch end 后跳到最终位置。这个现象说明 drag move 事件路径本身可以到达，但 layer-shell 的 pending margin 没有被实时提交给 compositor。

当前 vendored `layer-shell-qt` wrapper 在 `QWaylandLayerSurface::setMargins(...)` 中做了本地收敛：

1. 继续调用 `set_margin(top, right, bottom, left)`。
2. 仅当 layer surface 已经收到首个 configure 并进入 `m_configured` 状态后，调用 `window()->commit()`。
3. 不在构造阶段的初始 `setMargins(...)` 里强行 commit，避免打乱 layer-shell 初始 empty commit -> configure -> map 流程。

`MiniBarWindow::applyLayerShellGeometry(...)` 同时增加了 unchanged-margin guard：如果计算出的 margins 与当前 `LayerShellQt::Window::margins()` 一致，就不再重复调用 `setMargins(...)`。这可以避免鼠标/触摸 move 事件密集时对同一位置重复发 Wayland commit。

### Soft Keyboard Position In Minibar Mode

重要发现：

在 Wayland + layer-shell 下，`minibar` 的真实屏幕位置来自 compositor 应用 layer-shell anchors/margins 后的结果，而不是普通 Qt `QWidget` 顶层窗口几何。也就是说，`mapToGlobal(...)` / `mapFromGlobal(...)` 在这类 layer-shell surface 上只能视为 Qt 侧坐标推导，不能作为最终屏幕位置的可靠事实来源。

维护规则：

1. 需要知道 `minibar` 实际视觉位置时，优先使用 `MiniBarWindow` 自己保存并提交给 layer-shell 的 `m_collapsedTopLeft` / size 派生状态。
2. 不要在 Wayland layer-shell 路径里把 `frameGeometry()`、`mapToGlobal(...)`、`mapFromGlobal(...)` 当成 compositor 最终放置位置的唯一依据。
3. 普通主窗口、Win32、X11 fallback 仍可沿用 QWidget global mapping；这个限制只针对 Wayland layer-shell surface。

`TouchNumKeyboard` 在 Wayland + minibar 小 host 下不会直接挂到 `MiniBarWindow` 的 client area，否则键盘会被 minibar 自身尺寸裁剪。当前路径是：

1. 创建临时的 screen-sized transparent host。
2. 在该 host 内放 `OverlayContainer`。
3. 再把 `TouchNumKeyboard` 作为 overlay 子控件显示。

2026-07-03 follow-up: field testing falsified the idea of fixing direct
minibar `Freq` / `Level` keyboards by detecting `TouchNumKeyboardScreenOverlayHost`
after `Show` and retroactively configuring that host as `LayerOverlay`. Repeated
clicks still created multiple hidden keyboard instances, and those instances
became visible together only after SAStudio showed its own modal confirmation
dialog. The current rule is that direct minibar numeric editing must be owned as
a single keyboard session by `MiniBarWindow`; in layer-shell mode that session is
opened inside the already managed `miniBarPopupOverlayHost` instead of creating
independent screen overlay hosts.

2026-07-06 implementation note: `TouchNumKeyboard` now has a preferred overlay
host override. `MiniBarWindow` uses it only for direct minibar `Freq` / `Level`
editing in layer-shell mode, so the clicked `LabelButton` remains the anchor for
positioning while the containing surface is the managed fullscreen
`miniBarPopupOverlayHost`. `MiniBarWindow` also tracks the active direct numeric
keyboard session and closes it before opening another minibar transient. Shared
`CommonPanel` `Center` / `Level` editing slots guard their `beginEditing`
handlers with `isEditTriggerFromWidget(...)` so a hidden or unrelated panel does
not respond to a minibar-origin edit trigger.

这条路径不能再单纯依赖 `anchorWidget->mapToGlobal(...) -> parent->mapFromGlobal(...)` 计算初始位置。原因是 layer-shell surface 的视觉位置由 compositor 按 anchors/margins 应用，普通 QWidget global mapping 在这类 surface 上不一定等价于最终屏幕位置。

当前约定是：

1. `MiniBarWindow::applyLayerShellGeometry(...)` 在更新 layer-shell margins 的同时，把同一个视觉 top-left 和 size 写到窗口动态属性：
   - `sgstudioLayerShellVisualTopLeft`
   - `sgstudioLayerShellVisualSize`
2. `TouchNumKeyboard::initialOverlayPosition(...)` 仅在 `OverlayInScreenHost` 路径下读取这两个属性。
3. 键盘用 anchor widget 相对 minibar window 的局部坐标，加上 minibar 的视觉 top-left，得到可信的视觉 anchor rect。
4. 如果 minibar 位于屏幕右侧，键盘优先放在 minibar 左侧；如果 minibar 位于屏幕左侧，键盘优先放在右侧。最后仍会 clamp 到当前 screen overlay 范围内。

主窗口模式和 Win32 / 非 Wayland top-level fallback 不使用这两个动态属性，继续保持原定位路径。

`SGStudioMiniBar` helper 的 base surface 和 fullscreen keyboard overlay
都写入同名 visual geometry 属性；Center/Level 已使用该坐标契约完成定位。
helper 当前状态和后续 popup/Sweep 缺口维护在
[minibar_helper_layershell_parity_gaps.md](minibar_helper_layershell_parity_gaps.md)。

### Current Drag Behavior And Mouse Jitter

当前 Raspberry Pi field result：

1. touch 拖动已经可以实时跟随，主观效果较好。
2. mouse 拖动不再只是 release 后跳转，但仍有严重抖动。

观察：

1. `MiniBarWindow` 的 mouse path 本来就处理 `MouseMove`，并在 layer-shell 模式下用 margins 表示位置。
2. commit 优化后，touch 拖动变好，说明 layer-shell margin 缺少实时 commit 是此前“结束后才移动”的主要原因。
3. mouse 和 touch 的剩余表现不同，说明剩余问题更可能在输入事件频率 / compositor 重定位节奏 / Qt hover-press 状态反馈，而不是同一个“没有 move 事件”的问题。

推断：

1. mouse move 事件通常比 touch update 更密集，并且带有 hover / enter / leave / pressed state 等 QWidget 反馈；每个有效像素变化都可能触发一次 layer-shell margin commit，让 compositor 高频重排 layer surface。
2. layer-shell reposition 不是 compositor 原生 interactive move-loop，而是客户端不断改 panel margins。mouse 的高频小步提交更容易暴露 compositor 重排、Qt 事件回投、整数像素舍入和 top-right margin 换算之间的抖动。
3. touch update 在当前树莓派环境下看起来更像被 compositor / Qt 自然合并或降频后的输入流，没有 hover 状态，也更少触发鼠标样式/按钮 hover 反馈，因此实际观感更稳定。

下一步如果继续优化 mouse 拖动，优先方向不是重新加 touch 专用路径，而是减少 mouse drag-time layer-shell commit 抖动：

1. 对 mouse drag margin commit 做帧级 coalescing，例如最多每 16 ms 提交一次最新位置。
2. 只在 delta 超过 1 个实际屏幕像素或 margin 确实改变时提交，保持当前位置缓存明确。
3. 拖动期间尽量降低 hover/pressed 样式刷新和 transient close 的重复影响。
4. 若仍抖动，再考虑是否需要把 mouse drag 做成“拖动中显示本地轻量反馈、release 时提交 layer-shell 位置”的两阶段体验；但这会改变交互语义，需单独评估。

## Raspberry Pi Fresh Machine Dependencies

### Runtime Machine

For a Raspberry Pi that only needs to run an extracted `build_pi` SGStudio package,
do not install the Qt/layer-shell development packages listed below. The package
already carries the SGStudio-built Qt runtime dependency closure, the selected
client-side Qt plugin groups, bundled application fonts,
`libLayerShellQtInterface.so`, and
`bin/wayland-shell-integration/liblayer-shell.so`. Development-only Qt plugins,
Wayland compositor/server plugins, and unrelated Qt modules are intentionally
not staged. ICU runtime families resolved by the same closure (for example
`libicui18n`, `libicuuc`, and `libicudata`) are bundled as well, so a runtime
machine does not need a matching Qt development installation.

Use the runtime helper script instead:

```bash
bash scripts/install_pi_runtime_deps.sh --app-root /software/SGStudio --yes --with-osk --with-tools
```

The script installs only target-machine runtime libraries such as Wayland,
GL/EGL, xkbcommon, fontconfig/freetype, DBus, audio, USB, and GStreamer
runtime packages. It also verifies the extracted package with `ldd` and checks
that bundled artifacts such as fonts and layer-shell plugins are present.

Use check-only mode before changing a machine:

```bash
bash scripts/install_pi_runtime_deps.sh --app-root /software/SGStudio --check-only
```

### Build Machine

如果一台新 Raspberry Pi 已经有 Qt 运行环境，但没有 QtWayland / Wayland 开发包，构建 layer-shell 集成至少需要下面这些包。

Debian 12 / Raspberry Pi OS Bookworm:

```bash
sudo apt install \
  qtbase5-dev \
  qtbase5-private-dev \
  libqt5waylandclient5-dev \
  qtwayland5-dev-tools \
  qtwayland5-private-dev \
  libwayland-dev \
  libwayland-bin \
  wayland-protocols \
  libxkbcommon-dev \
  pkg-config
```

包用途：

1. `libqt5waylandclient5-dev`: 提供 `Qt5WaylandClientConfig.cmake`。
2. `qtwayland5-dev-tools`: 提供 `qtwaylandscanner`，常见路径是 `/usr/lib/qt5/bin/qtwaylandscanner`。
3. `qtwayland5-private-dev`: 提供 `Qt5::WaylandClientPrivate` 和 QtWayland private headers。
4. `libwayland-dev`: 提供 Wayland client development files。
5. `libwayland-bin`: 提供 `wayland-scanner`。
6. `wayland-protocols`: 提供 `stable/xdg-shell/xdg-shell.xml`。
7. `libxkbcommon-dev`: 提供 `xkbcommon` pkg-config dependency。
8. `qtbase5-dev` / `qtbase5-private-dev`: Qt5 base development and private targets。
9. `pkg-config`: CMake `pkg_check_modules(...)` 入口。

常见缺包错误：

```text
Could not find a package configuration file provided by "Qt5WaylandClient"
```

安装 `libqt5waylandclient5-dev`。

```text
Could not find Qt5::WaylandClientPrivate
```

安装 `qtwayland5-private-dev`。

```text
Could not find xdg-shell protocol XML
```

安装 `wayland-protocols`。

```text
Could not find qtwaylandscanner
```

安装 `qtwayland5-dev-tools`。

```text
Could not find wayland-scanner
```

安装 `libwayland-bin`。

## Runtime Verification On Raspberry Pi

典型 build-tree 验证：

```bash
cd ~/Desktop/SGSProject
cmake -S . -B build-SGSProject-Desktop-Debug -G Ninja \
  -DCMAKE_BUILD_TYPE=Debug \
  -DCMAKE_PREFIX_PATH=/usr \
  -DSGS_RUNTIME_LAYOUT=build-tree

cmake --build build-SGSProject-Desktop-Debug --target SGStudioMiniBar -j4
```

确认关键产物：

```bash
ls -l build-SGSProject-Desktop-Debug/lib/libLayerShellQtInterface.so
ls -l build-SGSProject-Desktop-Debug/bin/wayland-shell-integration/liblayer-shell.so
ls -l build-SGSProject-Desktop-Debug/bin/SGStudioMiniBar
```

确认没有未解析符号：

```bash
ldd -r build-SGSProject-Desktop-Debug/lib/libLayerShellQtInterface.so
ldd -r build-SGSProject-Desktop-Debug/bin/SGStudioMiniBar
```

在 Raspberry Pi labwc / Wayland 桌面上测试：

```bash
cd ~/Desktop/SGSProject/build-SGSProject-Desktop-Debug/bin
env -u QT_PLUGIN_PATH -u QT_QPA_PLATFORM_PLUGIN_PATH \
  XDG_RUNTIME_DIR=/run/user/1000 \
  WAYLAND_DISPLAY=wayland-0 \
  DISPLAY=:0 \
  QT_QPA_PLATFORM=wayland \
  LD_LIBRARY_PATH=../lib \
  ./SGStudio
```

主窗口启动后，通过标题栏 minibar 按钮让 `MinibarHelperController` 启动 helper；
不要直接把 `SGStudioMiniBar` 当作产品入口运行。

运行成功时，`bin/debug.log` 应包含：

```text
Load Core OK
Start Core OK
DeviceManager: device connected
```

可用 `grim` 截图确认：

```bash
XDG_RUNTIME_DIR=/run/user/1000 WAYLAND_DISPLAY=wayland-0 grim /tmp/minibar.png
```

## Known Warnings And Notes

### Historical Window Opacity

Wayland layer-shell 路径可能记录：

```text
This plugin does not support setting window opacity
```

该警告来自已删除的 `MiniBarWindow` 在 collapsed 状态切换 opacity，而
layer-shell/Wayland plugin 不支持这条窗口属性。它只保留为历史日志识别信息；
当前 helper 的问题应以 helper 自身日志和 surface 证据重新判断。

### Qt Runtime Mixing

远程验证时曾观察到环境中有 `/opt/Qt/5.15.18/...` 的 `QT_PLUGIN_PATH`，而系统构建使用 Debian Qt 5.15.8。构建阶段会清除这些外部变量；正式包启动时则将搜索路径固定为包内目录：

```bash
QT_PLUGIN_PATH=<package>/bin
QT_QPA_PLATFORM_PLUGIN_PATH=<package>/bin/platforms
LD_LIBRARY_PATH=<package>/lib:<package>/bin:<package>/plugin:<package>/updater
```

包内 `bin/qt.conf` 同时把 `Prefix`、`Plugins` 和 `Libraries` 固定到该相对布局，
避免 Qt 再使用目标机编译进 QtCore 的系统插件前缀。正式部署必须通过包根目录的
launcher 启动，保证运行时 Qt 库和 Qt platform/shell integration plugin 属于同一套 Qt。

### OpenSSL Copy Scope

Raspberry Pi 上 `/opt/aarch64-openssl/lib` 可能是 `/usr/lib/aarch64-linux-gnu` 的 symlink。不要 glob 拷贝该目录下的所有 `.so*`，否则会把系统库和循环 symlink 一起拷进 build/lib。当前 CMake 只复制明确配置的：

```text
OPENSSL_SSL_LIBRARY
OPENSSL_CRYPTO_LIBRARY
```

## Remote Debug Checklist

1. 先看当前进程参数：

```bash
ps -u htra -o pid=,stat=,etimes=,comm=,args= | grep SGStudio
```

2. 看 build-tree 日志：

```bash
tail -n 160 ~/Desktop/SGSProject/build-SGSProject-Desktop-Debug/bin/debug.log
```

3. 如果完全不可见，先区分：
   - `Core` 是否加载失败。
   - 是否进入了 `minibar` 启动模式。
   - 是否启用了 layer-shell。
   - 设备是否只是 discovered 但没有 connected。
4. `Core` 加载失败且出现 `xdg_popup_interface` 时，优先检查 `xdg-shell.xml` 是否生成并参与链接。
5. 程序启动成功但没有界面时，用 `grim` 截图确认是否是屏幕位置/层级问题，而不是肉眼观察被 Qt Creator 或其它窗口遮挡。
