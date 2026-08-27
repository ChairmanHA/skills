# Minibar Layer-Shell Transient Popup Fix

Date: 2026-07-01

## Scope

Apply the minimal fix for Raspberry Pi Wayland minibar transient popups: keep the existing `QMenu` provider list and hosted `QDialog` business/sweep popups, but explicitly configure their layer-shell anchors and margins before showing them.

## Verification Level

static

No build or runtime verification is planned in this pass; the user will test on Raspberry Pi.

## Observations

1. `LayerShellQt::Shell::useLayerShell()` sets `QT_WAYLAND_SHELL_INTEGRATION=layer-shell` globally in minibar Wayland mode.
2. The vendored layer-shell shell integration creates a layer surface for every Qt top-level window.
3. `LayerShellQt::Window` defaults to `AnchorTop | AnchorBottom | AnchorLeft | AnchorRight`, which causes newly-created unconfigured popups/dialogs to behave like full-screen layer surfaces.
4. Replacing the provider `QMenu` with a tool-style `QDialog` did not help, proving the problem is the global shell-surface role, not the widget class.
5. `MiniBarWindow` already has a working top-right layer-shell geometry mapping for the minibar window itself.

## Inference

Minibar-owned transient windows must be configured as layer-shell surfaces before they are shown. They need narrow `AnchorTop | AnchorRight` anchors plus margins derived from their intended screen top-left, instead of inheriting the layer-shell default full-screen anchors.

## Success Criteria

1. Provider `QMenu` opens as a compact list near the `MOD` button, not a full-screen layer surface.
2. Clicking AM/FM/provider items opens the existing hosted business popup in the intended geometry.
3. Sweep popup continues to open in the intended geometry.
4. The change is localized to `MiniBarWindow` layer-shell handling.
5. Win32, X11, and non-layer-shell paths remain unchanged.

## Implementation Plan

1. Add a generic helper in `MiniBarWindow` to configure a minibar-owned popup widget as a layer-shell top-right anchored surface.
2. Reuse the existing margin calculation path for provider menu, business popup, sweep popup, and placeholder popup.
3. Call the helper immediately before each popup is shown.
4. Keep existing `QMenu` / `MiniBarBusinessMenuHost` code intact.

## Implementation Update

Files changed:

1. `src/plugins/core/minibarwindow.h`
2. `src/plugins/core/minibarwindow.cpp`
3. `3rdParty/layer-shell-qt-5.27.12/src/qwaylandlayersurface.cpp`

Implemented:

1. Added `MiniBarWindow::configureLayerShellTransient(...)` to set `AnchorTop | AnchorRight`, `LayerTop`, `exclusiveZone = 0`, `KeyboardInteractivityNone`, desired output, scope, and margins for minibar-owned transient widgets.
2. Added `MiniBarWindow::popupTopLeftFromWindowLocal(...)` so Wayland layer-shell popup placement uses the compositor-facing minibar visual top-left instead of relying on `mapToGlobal(...)`.
3. Applied the helper to:
   - provider `QMenu`
   - hosted business popup
   - sweep popup
   - placeholder popup
4. Kept the existing `QMenu` provider host and `MiniBarBusinessMenuHost` action mapping intact.
5. Updated the vendored layer-shell surface so changing anchors also sends the current surface size and commits when already configured. This prevents a transient from keeping the implicit default full-screen sizing state after anchors are corrected.

Verification:

1. `git diff --check -- src/plugins/core/minibarwindow.h src/plugins/core/minibarwindow.cpp 3rdParty/layer-shell-qt-5.27.12/src/qwaylandlayersurface.cpp`

## KnowledgeBase Update

Files changed:

1. `.github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md`
2. `.github/KnowledgeBase/minibar_popup_host_style_and_focus_status.md`
3. `.github/KnowledgeBase/ui_independent_runtime_and_minibar_design.md`
4. `.github/KnowledgeBase/Index.md`

Updated:

1. Documented that `Shell::useLayerShell()` is process-global and makes every Qt top-level window a layer surface in minibar Wayland mode.
2. Recorded the default `AnchorTop | AnchorBottom | AnchorLeft | AnchorRight` full-screen transient failure mode.
3. Corrected the provider list documentation: current code keeps `QMenu`; the fix is explicit layer-shell transient geometry, not replacing `QMenu` with a custom button popup.
4. Added the maintenance rule that future minibar-owned top-level popups/dialogs need explicit layer-shell anchors, size, and margins before display.

Verification:

1. `rg -n "provider popup button|provider list 使用本地按钮|而不是原生 `QMenu`|provider-list model|provider-list 入口|provider list popup|tool-style popup host" .github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md .github/KnowledgeBase/minibar_popup_host_style_and_focus_status.md .github/KnowledgeBase/ui_independent_runtime_and_minibar_design.md .github/KnowledgeBase/Index.md`
2. `git diff --check -- .github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md .github/KnowledgeBase/minibar_popup_host_style_and_focus_status.md .github/KnowledgeBase/ui_independent_runtime_and_minibar_design.md .github/KnowledgeBase/Index.md`
