# Minibar Soft Keyboard Overlay Tradeoff

Date: 2026-07-01

## Scope

Static analysis of the current minibar popup architecture and `TouchNumKeyboard` overlay path, focused on the tradeoff between Raspberry Pi Wayland drag support and normal soft-keyboard popup display under minibar on Win32 and Wayland.

## Verification Level

static

No build or runtime verification is planned unless explicitly requested.

## Success Criteria

1. Identify how `MiniBarWindow` owns/collapses popups and how it treats soft-keyboard descendants.
2. Identify where `TouchNumKeyboard` switches between overlay child-widget behavior and top-level popup/dialog behavior.
3. Separate observed code behavior from inference about the Win32 screenshot symptom.
4. Recommend whether to keep, narrow, or remove the overlay mechanism, including expected impact on Win32 and Wayland minibar behavior.

## Initial Assumptions

1. The screenshot shows the minibar-triggered numeric keyboard being clipped or layered as if it were constrained by the minibar host area.
2. The overlay path was introduced primarily to make an application-owned frameless soft keyboard draggable on Raspberry Pi Wayland.
3. Cross-platform correct display is more important than Wayland-only drag support unless the code can narrow overlay usage without destabilizing popup ownership.

## Static Findings

1. `PropertyBindingManager::bindButtonToProperty(...)` emits `property->beginEditing(button)`, so the numeric keyboard anchor for minibar `Frequency` / `Level` is the clicked `LabelButton`.
2. `MiniBarWindow` binds `Center` / `Level` begin-editing to `PropertyBindingHelper::prepareNumericKeyBoard(property, triggerObj)` and immediately calls `keyboard->open()`.
3. `TouchNumKeyboard` is currently constructed as `Qt::Widget | Qt::FramelessWindowHint | Qt::CustomizeWindowHint`; `open()` only calls `setVisible(true)`.
4. `TouchNumKeyboard::setVisible(true)` always calls `ensureOverlay()`, reparents the keyboard to `OverlayContainer`, and clamps the keyboard inside the overlay parent.
5. `resolveOverlayHost()` uses the anchor widget's `window()` as the overlay host. For direct minibar buttons this resolves to `MiniBarWindow`, whose client area is far smaller than the keyboard.
6. Because the keyboard is a child widget under the minibar-owned overlay, normal QWidget clipping explains the Win32 screenshot: only the portion inside the minibar host can be painted.
7. The overlay mechanism still has value for large hosts such as the main window because it avoids Wayland top-level frameless move limitations. The conflict is specifically the small-host minibar case.

## Recommendation

Prefer narrowing the overlay path instead of deleting it globally:

1. Keep overlay child-widget presentation when the resolved host is large enough to contain the keyboard.
2. Fall back to a top-level owned keyboard presentation when the resolved host is too small, especially direct minibar buttons.
3. Accept that the fallback top-level keyboard may not be draggable on Raspberry Pi Wayland; it should display correctly on Win32 and Wayland.

If implementation must be extremely small and conservative, removing the overlay path globally is acceptable, but it deliberately gives up the already-validated Wayland drag behavior for main-window style hosts.

## Refined Plan After User Direction

User direction: remove overlay on Win32, keep overlay on Wayland, and guarantee minibar mode remains usable on Wayland.

### Presentation Policy

1. Win32:
   - Do not use `OverlayContainer`.
   - Show `TouchNumKeyboard` as an owned top-level tool/dialog.
   - Position it near the anchor and clamp to the current screen available geometry.
   - Outside-click behavior should still send the configured `PressOutsidePolicy` key to the receiver before closing.

2. Linux Wayland, host large enough:
   - Keep the existing `OverlayContainer` child-widget path.
   - This preserves the reason the overlay exists: draggable frameless keyboard without relying on Wayland top-level `move()`.

3. Linux Wayland, host too small:
   - Do not use the minibar window itself as the overlay parent.
   - Preferred path: create a temporary screen-sized transparent top-level overlay host, then put the existing `OverlayContainer` and `TouchNumKeyboard` inside that host. The keyboard remains a child widget, so dragging still uses client-side coordinates.
   - Fallback path: if the screen overlay host is not viable in a target environment, show the keyboard as an owned top-level tool/dialog; this gives up dragging only for the small-host Wayland case but preserves normal display and input.

4. Linux non-Wayland / unknown:
   - Prefer the top-level tool/dialog path unless there is concrete evidence that overlay is still required.

### Implementation Shape

Keep the change localized to `src/libs/controls/touchnumkeyboard.h/.cpp` unless minibar needs an explicit marker later.

1. Add a private presentation mode:
   - `OverlayInResolvedHost`
   - `OverlayInScreenHost`
   - `TopLevelTool`

2. Add runtime helpers:
   - `isWaylandPlatform()` using `QGuiApplication::platformName()`.
   - `keyboardDesiredSize()` from `sizeHint()/minimumSizeHint()` after `adjustSize()`.
   - `hostCanContainKeyboard(host, desiredSize)` with a small margin.
   - `screenForAnchor()` using anchor global center, host window center, then primary screen.

3. Extend private state:
   - `QPointer<QWidget> screenOverlayHost`
   - `PresentationMode presentationMode`
   - optional event-filter installation state for top-level outside-click handling

4. Refactor `setVisible(true)`:
   - Decide presentation mode before reparenting.
   - Win32 chooses `TopLevelTool`.
   - Wayland with adequate host chooses `OverlayInResolvedHost`.
   - Wayland with inadequate host chooses `OverlayInScreenHost` first.

5. Implement `OverlayInScreenHost`:
   - Create a temporary top-level transparent host with frameless/tool-ish flags and `WA_QuitOnClose=false`.
   - Size/show it to the selected screen; prefer fullscreen on Wayland because compositor controls top-level position.
   - Parent `OverlayContainer` to this host, then parent the keyboard to the overlay.
   - Clamp keyboard movement inside the screen overlay host, not the minibar.
   - Destroy or hide this host when the keyboard finishes/destroys.

6. Implement `TopLevelTool`:
   - Ensure keyboard has a top-level window flag, not `Qt::Widget`.
   - Keep QObject ownership with the anchor when possible, but avoid QWidget child clipping.
   - Position near the anchor when possible; otherwise center on the current screen.
   - Clamp to `QScreen::availableGeometry()`.
   - Install an application event filter while visible to implement outside-click close for in-app clicks; handle application deactivation conservatively.

7. Keep `PressOutsidePolicy` centralized:
   - Extract the existing outside-input lambda into a private helper such as `handleOutsideInput()`.
   - Both overlay and top-level paths call the same helper, so Enter/Esc semantics do not diverge.

### Static Success Criteria

1. On Win32, `TouchNumKeyboard::setVisible(true)` never creates or reparents to `OverlayContainer`.
2. On Wayland with normal main-window host, the keyboard still becomes an overlay child and remains draggable.
3. On Wayland with minibar as anchor, the keyboard is not parented under `MiniBarWindow`'s small client area.
4. Outside-click behavior continues to route through `PressOutsidePolicy`.
5. Lifetime cleanup covers `OverlayContainer`, screen overlay host, and top-level event filters.

## Implementation Update

Files changed:

1. `src/libs/controls/touchnumkeyboard.h`
2. `src/libs/controls/touchnumkeyboard.cpp`

Implemented presentation policy:

1. Win32 and non-Wayland platforms now use a top-level owned `Qt::Tool` keyboard path and do not create `OverlayContainer` from `setVisible(true)`.
2. Wayland keeps the existing overlay child-widget path when the resolved host can contain the keyboard.
3. Wayland small-host cases, including direct minibar buttons, create a temporary transparent screen-sized top-level overlay host and attach the existing `OverlayContainer` / keyboard under that host instead of under `MiniBarWindow`'s small client area.
   - The screen host keeps the resolved host as QObject parent, so minibar's owned-transient checks can still recognize the keyboard stack.
4. Top-level and overlay paths share `handleOutsideInput()`, preserving `PressOutsidePolicy` Enter/Esc semantics.
5. Cleanup now removes the top-level application event filter and hides/deletes the temporary screen overlay host.

Verification performed:

1. Static diff review.
2. `git diff --check` for touched files.

Build/runtime verification not run per repository default workflow.

## Wayland Field Follow-Up

User field test:

1. Main-window mode on Wayland works.
2. Minibar mode on Wayland opens a draggable keyboard, but the keyboard background is fully transparent.

Observation:

1. The issue only affects the `OverlayInScreenHost` path.
2. The screen overlay host currently sets a generic `background: transparent;` stylesheet.

Inference:

1. The generic stylesheet can cascade to child widgets under the temporary screen host and override the keyboard's normal painted background.

Follow-up fix:

1. Scope the transparent background rule to `TouchNumKeyboardScreenOverlayHost` itself instead of using an unqualified stylesheet rule.
2. Keep the screen host transparent while allowing the child `TouchNumKeyboard` to use the normal application/theme stylesheet.

## Layer Shell Follow-Up Scope

User field test after keyboard style fix:

1. Minibar itself still cannot be dragged on Wayland like Win32.
2. Minibar is visible in the desktop taskbar instead of behaving like a background utility/tool window.

Analysis target:

1. Inspect KDE `layer-shell-qt` as a candidate Wayland protocol integration.
2. Determine whether it can solve taskbar visibility and/or dragging for `MiniBarWindow`.
3. Identify the smallest architecture path if the dependency is useful.

Verification level:

static + upstream source/documentation review

No build/runtime verification planned unless explicitly requested.

## Layer Shell Static Findings

Upstream references reviewed:

1. KDE `layer-shell-qt` README and `Window` interface.
2. KDE `layer-shell-qt` Plasma/5.27 branch API, because SGStudio supports Qt5/Qt6 through `Qt${QT_VERSION_MAJOR}`.
3. `wlr-layer-shell-unstable-v1` protocol description.
4. Current `MiniBarWindow` implementation.

Observed current code:

1. `MiniBarWindow` is a normal top-level QWidget with `Qt::Tool | Qt::FramelessWindowHint | Qt::WindowStaysOnTopHint`.
2. Collapsed/expanded positioning is stored as `m_collapsedTopLeft` and applied with `setGeometry(...)`.
3. Dragging updates the window with `move(m_windowPressTopLeft + delta)`.
4. This matches Win32 behavior, but on Wayland a normal client cannot reliably position its own top-level window with `move()`/`setGeometry()`.

Upstream behavior:

1. `layer-shell-qt` exposes `LayerShellQt::Window::get(QWindow *)`.
2. The surface can be assigned a layer, anchors, margins, exclusive zone, keyboard interactivity, and scope.
3. The layer-shell protocol positions surfaces by layer + output + anchors + margins, not by an arbitrary client-side global move.
4. Layer surfaces are not normal `xdg_toplevel` application windows, so they are the right protocol-level shape for a panel/overlay/minibar that should not appear as a taskbar application.
5. Plasma/5.27 `Shell::useLayerShell()` sets `QT_WAYLAND_SHELL_INTEGRATION=layer-shell`; therefore in a Qt5 build it must be called before the minibar native Wayland surface is created.
6. The current master package depends on Qt6; Qt5 deployments need a 5.27-compatible package such as Debian's `liblayershellqtinterface-dev` / `liblayershellqtinterface5`.

Inference:

1. `layer-shell-qt` should solve the taskbar visibility issue if the Wayland compositor supports `wlr-layer-shell`.
2. It can also make minibar drag-like repositioning work, but only if `MiniBarWindow` stops treating Wayland drag as `move()` and instead updates layer-shell margins during drag.
3. It is not a general replacement for Win32 `Qt::Tool`; it should be a Linux Wayland + minibar-only integration with Win32 and non-Wayland behavior left intact.

Recommended implementation direction:

1. Add an optional layer-shell dependency for Linux builds only.
2. Configure layer shell before the minibar is shown and before its native platform surface is created.
3. Anchor the minibar to a screen edge/corner, initially `AnchorTop | AnchorRight`, with `LayerTop`, `exclusiveZone = 0`, and no forced keyboard focus.
4. Convert stored `m_collapsedTopLeft` into layer-shell margins:
   - `top = topLeft.y() - screen.top()`
   - `right = screen.right() - topLeft.x() - width + 1`
5. During Wayland layer-shell drag, compute the desired top-left from pointer delta, clamp it, store it in `m_collapsedTopLeft`, and call `setMargins(...)` instead of `move(...)`.
6. Keep all existing `setGeometry(...)` / `move(...)` paths for Win32 and non-layer-shell runtime fallback.

## Vendored Layer Shell Qt5 Plan

User direction:

1. Stop relying on the previous overlay workaround as the main answer for Linux + Wayland + minibar.
2. Vendor a Qt5-compatible `layer-shell-qt` source copy into the project.
3. First implementation step: make `MiniBarWindow` a layer-shell surface only under Linux + Wayland + minibar, so it can be repositioned by drag and does not appear in the taskbar.

Source selection:

1. Prefer KDE Plasma 5.27.12 `layer-shell-qt-5.27.12.tar.xz` from KDE's stable release archive.
2. Rationale:
   - It is the final Plasma 5.27 LTS-era Qt5 line.
   - Its CMake declares `QT_MIN_VERSION 5.15.2`.
   - Its public `LayerShellQt::Window` API contains the needed Qt5 methods: `setAnchors`, `setMargins`, `setExclusiveZone`, `setKeyboardInteractivity`, `setLayer`, `setScope`, and `setDesiredOutput`.
3. Keep upstream license files and a local README noting source URL/version/date.

Vendoring shape:

1. Add source under `3rdParty/layer-shell-qt-5.27.12/`.
2. Do not build it on Win32 or non-Linux platforms.
3. In `3rdParty/CMakeLists.txt`, add the subdirectory only when `UNIX AND NOT APPLE`.
4. Prefer upstream CMake first, because it already generates the Qt Wayland protocol client sources and the wayland shell integration plugin.
5. If upstream CMake pulls in too much KDE install/export behavior for in-tree use, wrap or patch only the CMake entry points while keeping source files unmodified where possible.

Expected Linux build prerequisites:

1. Qt5 Wayland client development files, including private QtWayland targets.
2. Wayland client development files.
3. `wayland-protocols`.
4. `xkbcommon`.
5. `extra-cmake-modules` / ECM, unless replaced by a local minimal CMake wrapper.

First-step SGStudio integration:

1. Add a compile definition such as `SGS_HAVE_LAYER_SHELL_QT` only for Linux builds where the vendored target is present.
2. Link `Core` privately to `LayerShellQt::Interface` or the vendored target equivalent.
3. Make `CorePlugin` call a small helper before constructing/showing minibar in `--ui-mode=minibar`.
   - For Qt5, `LayerShellQt::Shell::useLayerShell()` sets `QT_WAYLAND_SHELL_INTEGRATION=layer-shell`, so it must run before the native Wayland surface exists.
   - Gate this by runtime `QGuiApplication::platformName().contains("wayland", ...)`.
4. Add `MiniBarWindow` methods for layer-shell configuration and geometry:
   - `bool isWaylandLayerShellEnabled() const`
   - `void configureLayerShellWindow()`
   - `void applyLayerShellGeometry(const QPoint &topLeft, const QSize &size)`
   - `QMargins layerShellMarginsForTopRightAnchor(...)`
5. Call `configureLayerShellWindow()` before first `show()` in minibar visible state.
6. Replace Wayland layer-shell drag movement:
   - Current non-layer-shell path keeps `move(m_windowPressTopLeft + delta)`.
   - Layer-shell path calculates desired top-left, clamps it, stores it in `m_collapsedTopLeft`, then updates margins.

Initial layer-shell policy:

1. `LayerTop`, not `LayerOverlay`, to behave like a panel/tool without unnecessarily overriding system overlays.
2. `AnchorTop | AnchorRight`, matching the current default collapsed top-right placement.
3. `exclusiveZone = 0`, so maximized applications are not forced to reserve screen space for the minibar.
4. `KeyboardInteractivityNone` if minibar pointer/touch interaction is sufficient; only revisit if hardware keyboard focus breaks a required workflow.
5. `scope = "sgstudio-minibar"`.

Success criteria for first implementation step:

1. Win32 and main-window mode code paths are unchanged in behavior.
2. Linux non-Wayland fallback remains normal QWidget behavior.
3. Linux + Wayland + minibar creates a layer-shell surface before the first visible show.
4. The desktop taskbar does not list minibar as a normal application window.
5. Dragging updates layer-shell margins and persists the new collapsed position.
6. Popups and the numeric keyboard remain usable after the minibar layer-shell change.

Risks:

1. Some Wayland compositors may not support `wlr-layer-shell`; in that case the code must fall back to current QWidget behavior with a warning.
2. Qt5 layer-shell-qt uses QtWayland private APIs, so target systems need matching QtWayland private development/runtime pieces.
3. QWidget + layer-shell requires the `QWindow`/shell configuration to happen before the surface is committed; ordering around first `show()` is critical.
4. Existing top-level popups/keyboard may need a second pass if they become separate taskbar entries or lose correct transient behavior under a layer-surface parent.

## Vendored Layer Shell Implementation Update

Files changed:

1. `3rdParty/CMakeLists.txt`
2. `3rdParty/layer-shell-qt-5.27.12/`
3. `src/plugins/core/CMakeLists.txt`
4. `src/plugins/core/coreplugin.cpp`
5. `src/plugins/core/minibarwindow.h`
6. `src/plugins/core/minibarwindow.cpp`

Implemented:

1. Downloaded and unpacked KDE Plasma 5.27.12 `layer-shell-qt` source into `3rdParty/layer-shell-qt-5.27.12/`.
2. Added a local vendoring note with source URL and SGStudio integration purpose.
3. Added Linux-only `SGS_ENABLE_VENDORED_LAYER_SHELL_QT` CMake option, default ON.
4. Added the vendored project through its upstream CMake on `UNIX AND NOT APPLE`.
5. Added a build-tree alias `LayerShellQt::Interface` for the vendored `LayerShellQtInterface` target.
6. Set the Qt Wayland shell integration plugin target `layer-shell` to output under `${SGS_BIN_DIR}/wayland-shell-integration`, so Qt can find it next to the runtime `bin/qt.conf` / plugin layout.
7. Linked `Core` privately to `LayerShellQt::Interface` when available and made `Core` depend on the `layer-shell` plugin target.
8. In `CorePlugin`, enabled `LayerShellQt::Shell::useLayerShell()` only for runtime Wayland + minibar startup mode.
9. In `MiniBarWindow`, added layer-shell configuration before first visible geometry application.
10. In layer-shell mode, replaced drag-time `move(...)` with top-right anchored margin updates while retaining the original QWidget path for Win32 and non-layer-shell runtimes.

Static verification planned:

1. Review CMake and source diffs.
2. Run `git diff --check`.

Runtime verification still required on Raspberry Pi Wayland:

1. Confirm `bin/wayland-shell-integration/liblayer-shell.so` is produced.
2. Confirm `--ui-mode=minibar` does not appear in the taskbar.
3. Confirm dragging updates minibar position.
4. Confirm numeric keyboard and minibar popups still open and dismiss correctly.

## Raspberry Pi CMake Follow-Up

User field report:

1. Raspberry Pi configure fails in `3rdParty/layer-shell-qt-5.27.12/CMakeLists.txt`.
2. The missing package is KDE `ECM`, requested as `ECMConfig.cmake` / `ecm-config.cmake`.

Observation:

1. The SGStudio parent CMake currently enters the KDE upstream `layer-shell-qt` root project directly.
2. That root project uses KDE Extra CMake Modules for macros, install rules, export headers, and Qt Wayland protocol generation.

Inference:

1. Installing `extra-cmake-modules` would fix this immediate configure error, but it keeps SGStudio coupled to KDE's full upstream CMake dependency surface.
2. SGStudio only needs the Qt5 library target and Wayland shell-integration plugin, so a smaller local wrapper is a better in-tree integration point.

Fix plan:

1. Add `3rdParty/layer-shell-qt-5.27.12/sgstudio/CMakeLists.txt` as a minimal Qt5 wrapper over the vendored upstream source.
2. Generate only the needed `wlr-layer-shell-unstable-v1` Wayland client sources with `wayland-scanner` and `qtwaylandscanner`.
3. Change `3rdParty/CMakeLists.txt` to add the wrapper directory instead of the upstream root directory.
4. Keep the remaining required Linux packages explicit: QtWayland client/private development files, `qtwaylandscanner`, `wayland-scanner`, `wayland-client`, and `xkbcommon`.

Static success criteria:

1. CMake no longer executes the upstream `find_package(ECM ...)`.
2. `LayerShellQtInterface` and `layer-shell` targets still exist for the existing SGStudio core plugin integration.
3. Missing QtWayland private development files fail with a direct SGStudio message instead of a later unresolved target error.

Implementation update:

1. Added `3rdParty/layer-shell-qt-5.27.12/sgstudio/CMakeLists.txt` as the SGStudio wrapper.
2. Changed `3rdParty/CMakeLists.txt` to call `add_subdirectory(layer-shell-qt-5.27.12/sgstudio EXCLUDE_FROM_ALL)`.
3. The wrapper no longer calls KDE upstream root CMake and therefore no longer requires ECM.
4. The wrapper still produces the expected `LayerShellQtInterface` and `layer-shell` targets.
5. The wrapper explicitly enables C for generated Wayland protocol code and emits a clear fatal message if `Qt5::WaylandClientPrivate` is unavailable.

Second field report:

1. Raspberry Pi configure now fails with `add_subdirectory given source "layer-shell-qt-5.27.12/sgstudio" which is not an existing directory`.

Observation:

1. The local wrapper file exists in the development workspace.
2. The wrapper file is a new untracked path unless it is explicitly added/synced with the rest of the vendored source.

Inference:

1. The Raspberry Pi source tree received the parent `3rdParty/CMakeLists.txt` change but did not receive `3rdParty/layer-shell-qt-5.27.12/sgstudio/CMakeLists.txt`.

Follow-up:

1. Add a parent CMake existence check with a direct diagnostic for the missing wrapper.
2. Ensure the wrapper directory/file is included in source control or manual deployment before re-running CMake on Raspberry Pi.

## Raspberry Pi QtWayland Dependency Follow-Up

User field report:

1. After syncing the SGStudio wrapper, Raspberry Pi configure reaches `3rdParty/layer-shell-qt-5.27.12/sgstudio/CMakeLists.txt`.
2. Configure now fails because Qt cannot find `Qt5WaylandClientConfig.cmake` / `qt5waylandclient-config.cmake`.

Observation:

1. This is now a target system dependency issue rather than the previous missing wrapper/ECM issue.
2. The vendored wrapper intentionally calls `find_package(Qt5 REQUIRED COMPONENTS Core Gui WaylandClient)`, so the target Qt5 installation must include the WaylandClient development CMake package.

Remote handling plan:

1. Use the repository `putty-remote-build-workflow` to connect to `192.168.3.179`.
2. Confirm the actual project path under `~/Desktop/SGStudio` or the older `~/Desktop/SGSProject`.
3. Inspect OS, Qt, CMake, and installed QtWayland/Wayland development packages.
4. Install the smallest package set that explains the missing `Qt5WaylandClient` CMake config.
5. Re-run CMake configure only far enough to verify this dependency layer, then record the next concrete blocker if one appears.

Remote observations:

1. Actual source root is `/home/htra/Desktop/SGSProject`; `/home/htra/Desktop/SGSProject/SGStudio` is a runtime/output directory.
2. The target system is Debian 12 bookworm on aarch64 with Qt 5.15.8.
3. Installed before the fix: `libqt5waylandclient5`, `qtbase5-dev`, `qtbase5-private-dev`, `libxkbcommon-dev`, and `pkg-config`.
4. Missing before the fix: `libqt5waylandclient5-dev`, `qtwayland5-dev-tools`, `qtwayland5-private-dev`, `libwayland-dev`, and `libwayland-bin`.

Remote package fix:

1. Installed `libqt5waylandclient5-dev`, `qtwayland5-dev-tools`, `qtwayland5-private-dev`, `libwayland-dev`, and `libwayland-bin`.
2. Re-running configure passed the previous `Qt5WaylandClient` failure.

Next blocker:

1. Configure then failed at `find_package(QtWaylandScanner REQUIRED)`.
2. Debian's `qtwayland5-dev-tools` provides the `qtwaylandscanner` executable, but not necessarily a `QtWaylandScannerConfig.cmake` package config.
3. KDE upstream normally gets a scanner finder from ECM; because SGStudio intentionally removed ECM from this vendored wrapper, the wrapper should find the executable directly instead of requiring a CMake package.

Follow-up wrapper fix:

1. Replace `find_package(QtWaylandScanner REQUIRED)` with `find_program(QtWaylandScanner_EXECUTABLE NAMES qtwaylandscanner ...)`.
2. Prefer the Qt5 qmake binary directory as a hint, because Debian Qt tools commonly live under `/usr/lib/qt5/bin`.

Build validation follow-up:

1. After the scanner fix, CMake configure completed successfully on Raspberry Pi.
2. A targeted `cmake --build ... --target layer-shell` then generated the Wayland protocol sources correctly.
3. The build failed because `window.cpp` uses `std::optional`, while the wrapper target was being compiled as `gnu++11`.

Follow-up wrapper fix:

1. Set both `LayerShellQtInterface` and `layer-shell` targets to `CXX_STANDARD 17`.
2. This matches the repository's C++17 boundary and the upstream layer-shell-qt source requirements.

Existing CMake blocker uncovered during verification:

1. After syncing the C++17 wrapper change, configure failed earlier in `3rdParty/CMakeLists.txt`.
2. The failing line copied `${OPENSSL_ROOT_DIR}/lib/*.so*` with `FOLLOW_SYMLINK_CHAIN`.
3. On the Raspberry Pi, `/opt/aarch64-openssl/lib` is a symlink to `/usr/lib/aarch64-linux-gnu`, so the glob copied far more than OpenSSL.
4. The existing build output contained a `libblas.so.3 <-> libblas.so.3-aarch64-linux-gnu` symlink loop, producing `Too many levels of symbolic links`.

Follow-up OpenSSL copy fix:

1. Stop globbing every `.so` under the OpenSSL lib directory.
2. Copy only the explicitly configured `OPENSSL_SSL_LIBRARY` and `OPENSSL_CRYPTO_LIBRARY` paths.
3. This keeps the intended runtime OpenSSL copy while avoiding unrelated system library symlink chains.

Remote verification result:

1. Synced the wrapper CMake fix and parent `3rdParty/CMakeLists.txt` fix to `/home/htra/Desktop/SGSProject`.
2. Re-ran configure with the existing Qt Creator build directory:
   - Source: `/home/htra/Desktop/SGSProject`
   - Build: `/home/htra/Desktop/SGSProject/build-SGSProject-Desktop-Debug`
   - Generator: Ninja
   - Build type: Debug
3. Configure completed successfully.
4. Built the targeted `layer-shell` target successfully.
5. Verified output from Ninja: `bin/wayland-shell-integration/liblayer-shell.so`.

Remaining warning:

1. CMake still warns that some runtime libraries in `/usr/lib/aarch64-linux-gnu` may be hidden by files already present in `build-SGSProject-Desktop-Debug/lib`.
2. This warning is caused by existing copied system libraries in the build output directory.
3. The warning is not fatal for the current layer-shell configure/build verification, but a clean build directory would be the best way to confirm the runtime library layout after the OpenSSL copy scope fix.

## Raspberry Pi Runtime Follow-Up: Minibar Not Visible

User field report:

1. The program now compiles and runs on Raspberry Pi.
2. Starting with a minibar mode argument shows no minibar at all.
3. The user is confident the device is connected to the Raspberry Pi.

Initial observations from local code:

1. `CorePlugin::parseStartupUiMode()` currently recognizes only the long option `--ui-mode <mode>` / `--ui-mode=<mode>`.
2. It does not recognize `--uimode`, and command lines with spaces around `=` may be parsed as separate arguments instead of an option assignment.
3. If startup mode is not parsed as `minibar`, `MiniBarWindow` is never created, so device visibility is irrelevant.
4. If startup mode is parsed correctly, the next distinction is whether `MiniBarWindow` stays hidden due to device visibility logic or whether the layer-shell surface itself is created but not visible.

Remote runtime plan:

1. Inspect running `SGStudio` process command line and environment.
2. Inspect runtime logs from the root/build `bin/debug.log` or captured terminal output.
3. Launch a controlled test from the desktop Wayland environment using the exact supported argument spelling.
4. If the issue is option spelling, support the existing user/deployment spelling while keeping the current `--ui-mode` spelling.
5. If the issue is layer-shell display, add focused runtime diagnostics around layer-shell setup and minibar visibility before changing behavior.

Remote runtime findings:

1. The build-tree runtime log shows `Core` failed to load.
2. The direct error is `libLayerShellQtInterface.so.5: undefined symbol: xdg_popup_interface`.
3. Because `Core` fails to load, `MiniBarWindow` is never created; this explains "nothing visible" independently of device connection state.

Inference:

1. The SGStudio minimal layer-shell wrapper incorrectly generated only `wlr-layer-shell-unstable-v1.xml`.
2. Although SGStudio source does not directly include `qwayland-xdg-shell.h`, the layer-shell protocol contains an `xdg_popup` request, so the generated Wayland C code references `xdg_popup_interface`.
3. KDE's upstream Qt5 CMake generated both `xdg-shell.xml` and `wlr-layer-shell-unstable-v1.xml`; the wrapper must do the same.

Follow-up implementation plan:

1. Add a `wayland-protocols` pkg-config check in the wrapper.
2. Generate `stable/xdg-shell/xdg-shell.xml` before the wlr layer-shell protocol.
3. Keep both generated protocol outputs in `LayerShellQtInterface`.
4. Make startup mode parsing accept both `--ui-mode` and the observed/deployed `--uimode` spelling.
5. Also tolerate separated equals forms such as `--uimode = minibar`, because those are common when arguments are copied into launch configuration fields.

Implementation update:

1. Updated the SGStudio layer-shell wrapper to require `wayland-protocols` and generate `xdg-shell.xml` alongside `wlr-layer-shell-unstable-v1.xml`.
2. Installed `wayland-protocols` on the Raspberry Pi target.
3. Updated `CorePlugin::parseStartupUiMode()` to accept:
   - `--ui-mode=minibar`
   - `--ui-mode minibar`
   - `--ui-mode = minibar`
   - `--uimode=minibar`
   - `--uimode minibar`
   - `--uimode = minibar`
4. Reconfigured and rebuilt the Raspberry Pi `Core` target, which also rebuilt `LayerShellQtInterface` and the `layer-shell` Wayland shell integration plugin.

Remote runtime verification:

1. `ldd -r` no longer reports `xdg_popup_interface` as unresolved.
2. A foreground controlled run with `--ui-mode=minibar` loaded all plugins successfully, enabled layer-shell, discovered the USB device, and connected the device.
3. A live run with `--uimode = minibar` also loaded all plugins successfully and remained running.
4. A Wayland screenshot captured with `grim` shows the minibar visible at the top-right of the Raspberry Pi desktop.
5. The screenshot does not show a normal SGStudio taskbar entry, matching the layer-shell goal.

Remaining note:

1. The log still contains `This plugin does not support setting window opacity` from the layer-shell/Wayland platform path.
2. The minibar remains visible despite that warning; opacity polish can be handled separately if needed.

## Documentation And Remote Cleanup Follow-Up

User direction:

1. Summarize what was changed for introducing `layer-shell-qt`.
2. Document what a fresh Raspberry Pi needs to install when Qt exists but QtWayland/layer-shell build dependencies are missing.
3. Stop the currently running SGStudio process on the Raspberry Pi.
4. Discard the source files that were synced manually to the Raspberry Pi, because the user will commit on Windows and then update the Raspberry Pi by `git pull`.

Plan:

1. Add a durable KnowledgeBase document for the layer-shell Qt5 minibar integration and Raspberry Pi dependency checklist.
2. Update `.github/KnowledgeBase/Index.md` with the new document.
3. On the Raspberry Pi, terminate only the SGStudio process started for runtime verification.
4. Restore/remove only the source files that were manually synced during the remote verification:
   - `3rdParty/CMakeLists.txt`
   - `3rdParty/layer-shell-qt-5.27.12/sgstudio/CMakeLists.txt`
   - `src/plugins/core/coreplugin.cpp`

## Layer-Shell Reuse Analysis For Minibar Soft Keyboard

User question:

1. Now that `layer-shell-qt` makes the Wayland minibar draggable, can the minibar numeric keyboard use the same mechanism and let us simplify/delete the current overlay mechanism?

Verification level:

static

No build or runtime verification was performed.

### Observations

1. `Controls` owns `TouchNumKeyboard` and `OverlayContainer`; `Core` owns `MiniBarWindow` and privately links `LayerShellQt::Interface`.
2. Directly configuring `TouchNumKeyboard` as a layer-shell surface from `Controls` would either move the layer-shell dependency into `Controls` or require a new host/presentation bridge owned by `Core`.
3. `TouchNumKeyboard::setVisible(true)` currently has three presentation modes:
   - Wayland + large resolved host: child `OverlayContainer` in that host.
   - Wayland + small host, including direct minibar buttons: temporary screen-sized top-level host, then `OverlayContainer` and the keyboard inside it.
   - Win32 / non-Wayland fallback: owned top-level `Qt::Tool`.
4. In minibar Wayland startup, `LayerShellQt::Shell::useLayerShell()` enables the shell integration globally before later top-level surfaces are created. Therefore the temporary screen overlay host may already be routed through the layer-shell Wayland shell integration, even though `TouchNumKeyboard` itself does not call `LayerShellQt::Window::get(...)`.
5. The vendored public `LayerShellQt::Window` API exposes layer, anchors, margins, exclusive zone, keyboard interactivity, desired output, and scope. It does not expose a high-level API for binding a Qt `QWidget` popup to an existing layer surface through `zwlr_layer_surface_v1.get_popup`.
6. The vendored protocol XML contains `get_popup`, but the current wrapper only uses it indirectly as a generated protocol dependency; SGStudio does not currently implement a layer-surface-owned `xdg_popup` path.
7. The soft keyboard is not only a window: it also owns outside-click policy, local modal loop behavior, drag clamping, touch/mouse swallowing, focus proxy handling for step editing, and synthetic key routing.
8. `wlr-layer-shell` keyboard interactivity is a protocol-level choice. `KeyboardInteractivityNone` is good for the minibar panel itself, but a keyboard surface that steals focus with `Exclusive` / `OnDemand` can disturb the existing `QApplication::focusWidget()` based key routing. Conversely `None` may preserve the target focus but requires pointer/touch-only interaction to remain sufficient.

### Inference

1. Using layer-shell for the keyboard is plausible, but it is not a drop-in replacement for `OverlayContainer`.
2. A separate keyboard layer surface would solve compositor-level placement/stacking, but it would not replace the application-level duties currently handled by `OverlayContainer`.
3. A true layer-shell `get_popup` integration would be architecturally cleaner for "keyboard belongs to minibar layer surface", but it requires deeper QtWayland/private wrapper work than the current public `LayerShellQt::Window` API provides.
4. The current Wayland small-host path is already close to a hybrid solution: a compositor-level top-level host plus application-level overlay semantics inside it. This is why it can avoid minibar client-area clipping while keeping drag/outside-click behavior localized.
5. Deleting overlay globally would regress known-good behavior for application-owned frameless keyboard dragging and local modal/outside-click handling.

### Recommendation

Do not delete `OverlayContainer` as the next step.

Prefer a smaller cleanup direction:

1. Keep `OverlayContainer` as the application-level event/lifetime/mask primitive.
2. Treat the Wayland minibar small-host path as a dedicated `KeyboardScreenOverlayHost` strategy, not as a generic minibar child overlay.
3. If cleanup is desired, rename/narrow the code so it is clear that the overlay is an input-capture container, while the screen host/layer-shell side is the compositor-level presentation container.
4. Only consider a true layer-shell keyboard after a separate prototype verifies:
   - whether the keyboard can be configured with safe keyboard interactivity without stealing the receiver focus;
   - whether `QApplication::focusWidget()` routing, `BaseUnitAdapter`, and `EditableWidget` step editing still behave;
   - whether outside-click close still works when the click is outside SGStudio/layer surfaces;
   - whether the implementation can stay out of `Controls`' public dependency surface or intentionally move the optional Linux dependency there.

### Possible Future Paths

1. Conservative path:
   - Keep current overlay behavior.
   - Polish the Wayland screen overlay host and document it as the minibar keyboard presentation path.

2. Medium cleanup path:
   - Introduce a small presentation strategy abstraction inside `TouchNumKeyboard`.
   - Keep `OverlayContainer` for event handling, but isolate the temporary screen host creation and platform decisions from the keyboard UI logic.

3. Experimental layer-shell path:
   - Add a Wayland-only prototype that makes the temporary screen host or the keyboard itself an explicitly configured layer surface with scope such as `sgstudio-keyboard`.
   - Avoid `KeyboardInteractivityExclusive` unless field testing proves focus routing remains correct.
   - Keep the existing overlay path as fallback until Raspberry Pi runtime tests cover commit, step edit, Esc/Enter outside-click, and hosted minibar popup flows.
