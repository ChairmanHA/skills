# Minibar Touch Drag Realtime Update

Date: 2026-07-01

## Scope

Fix `MiniBarWindow` touch dragging on Raspberry Pi Wayland/layer-shell so finger movement updates the minibar position during `TouchUpdate`, not only after the finger is released.

## Verification Level

static

No build or runtime verification is planned unless explicitly requested.

## Observations

1. Mouse dragging already updates minibar position in `MiniBarWindow::handleDragMouseMove(...)`.
2. In layer-shell mode that path updates `m_collapsedTopLeft` and calls `applyLayerShellGeometry(...)`, which maps the desired top-left position to layer-shell margins.
3. Current `MiniBarWindow::eventFilter(...)` handles `MouseButtonPress`, `MouseMove`, and `MouseButtonRelease`, but does not handle `TouchBegin`, `TouchUpdate`, `TouchEnd`, or `TouchCancel`.
4. User Release field test on Raspberry Pi: finger dragging causes only a slight initial movement; after finger release, the minibar jumps to the release position.

## Inference

Qt/Wayland is likely synthesizing enough mouse input to start/end the interaction, but not delivering continuous `MouseMove` events for touch movement. The minibar therefore needs an explicit touch-event path instead of relying on mouse synthesis.

## Success Criteria

1. Touch begin on the minibar records the same drag origin state as mouse press.
2. Touch update computes the finger delta from screen/global coordinates and updates the same layer-shell geometry path used by mouse dragging.
3. Touch end/cancel persists the final position only after a real drag, matching mouse release behavior.
4. Existing mouse dragging and button click/tap behavior remain intact as much as possible.
5. The change stays localized to `src/plugins/core/minibarwindow.h/.cpp`.

## Implementation Plan

1. Add touch event helpers to `MiniBarWindow`.
2. Enable touch delivery on the minibar drag-filter widget tree.
3. Refactor the repeated drag-update and drag-finish logic so mouse and touch share it.
4. Route `TouchBegin`, `TouchUpdate`, `TouchEnd`, and `TouchCancel` in `eventFilter(...)`.
5. Run static diff checks.

## Implementation Update

Files changed:

1. `src/plugins/core/minibarwindow.h`
2. `src/plugins/core/minibarwindow.cpp`

Implemented:

1. Added explicit touch drag handlers for `TouchBegin`, `TouchUpdate`, `TouchEnd`, and `TouchCancel`.
2. Enabled `WA_AcceptTouchEvents` on the minibar drag-filter widget tree so touch updates can reach the filter path.
3. Refactored mouse and touch dragging through shared begin/update/complete helpers.
4. `TouchUpdate` now updates the same layer-shell geometry path as mouse dragging.
5. Touch taps that do not cross the drag threshold synthesize the original button click on touch end, preserving normal minibar button behavior.

Verification:

1. `git diff --check -- src/plugins/core/minibarwindow.h src/plugins/core/minibarwindow.cpp`

## Mouse Field Follow-Up

User field correction:

1. Release testing shows mouse dragging has the same symptom as touch dragging.
2. The minibar only moves to the final pointer position after mouse release.

Updated observation:

1. `MiniBarWindow::eventFilter(...)` already routes `MouseMove` to the drag path.
2. The mouse move path updates `m_collapsedTopLeft` and calls `LayerShellQt::Window::setMargins(...)`.
3. The vendored layer-shell implementation forwards that to `zwlr_layer_surface_v1.set_margin(...)`, but does not commit the underlying Wayland surface.
4. The layer-shell protocol states margin state is double-buffered and only applied on `wl_surface.commit`.

Updated inference:

The real-time failure is more likely not missing `MouseMove`, but missing a surface commit after drag-time margin changes. Mouse/touch move events update pending layer-shell state, while release causes a later Qt surface commit that finally applies the last margin.

Follow-up plan:

1. Patch the vendored layer-shell surface margin setter to commit when the layer surface is already configured.
2. Avoid committing during the initial layer-surface constructor setup before the first configure.
3. Remove the explicit touch event path from `MiniBarWindow` for this test pass because the mouse field result shows the missing commit is the better-supported root cause.
4. Keep only the `MiniBarWindow::applyLayerShellGeometry(...)` unchanged-margin guard to avoid duplicate commits.
5. Run static diff checks.

Follow-up implementation update:

1. Updated `3rdParty/layer-shell-qt-5.27.12/src/qwaylandlayersurface.cpp`.
2. `QWaylandLayerSurface::setMargins(...)` now calls `window()->commit()` after `set_margin(...)` only when `m_configured` is true.
3. This keeps the initial layer-surface setup untouched while making drag-time margin changes visible to the compositor immediately.
4. `MiniBarWindow::applyLayerShellGeometry(...)` now skips unchanged margins before calling into layer-shell, avoiding duplicate surface commits for repeated pointer positions.

Cleanup decision:

1. Remove the explicit `TouchBegin` / `TouchUpdate` / `TouchEnd` handling added in the first pass.
2. Restore the smaller mouse-driven drag path, because both mouse and touch already reached the final drag position and the stronger evidence now points to double-buffered layer-shell state not being committed.
3. Use the Raspberry Pi test to decide whether native touch handling is still needed after the layer-shell commit fix.

Cleanup implementation update:

1. Removed the explicit touch event handlers, touch state, touch coordinate helper, and `WA_AcceptTouchEvents` changes from `MiniBarWindow`.
2. Restored the original mouse press/move/release drag structure.
3. The only `MiniBarWindow` change retained for this test pass is unchanged-margin suppression in `applyLayerShellGeometry(...)`.

Verification:

1. `git diff --check -- src/plugins/core/minibarwindow.h src/plugins/core/minibarwindow.cpp 3rdParty/layer-shell-qt-5.27.12/src/qwaylandlayersurface.cpp`

## KnowledgeBase Documentation Update

Scope:

1. Update `.github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md` with the drag-time layer-shell commit optimization.
2. Record the current Raspberry Pi field result: touch dragging is good, mouse dragging still has severe jitter.
3. Separate observation from inference and leave a clear next-investigation direction for later work.

Verification:

1. Static documentation review only.
