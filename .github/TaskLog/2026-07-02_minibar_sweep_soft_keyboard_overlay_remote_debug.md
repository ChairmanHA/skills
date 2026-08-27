# MiniBar Sweep Popup After Soft Keyboard Remote Debug

Date: 2026-07-02

## Scope

Investigate and fix the Raspberry Pi Wayland minibar sequence:

1. Open the frequency/power sweep popup.
2. Open `TouchNumKeyboard` from a sweep numeric value.
3. Dismiss the keyboard by clicking inside the sweep panel.
4. Close the sweep popup with the sweep button.
5. Reopen the sweep popup.
6. Click inside the panel.

Before the fix, the reopened sweep popup closed immediately and the clicked panel button did not respond.

## Remote Context

- SSH host: `192.168.3.179`
- SSH user: `htra`
- Password: `htra`
- Actual project path found by probe: `~/Desktop/SGSProject`
- Remote branch during debug: `minibar`
- you can use skill to connect to host to debug
- the log file is `/home/htra/Desktop/SGSProject/build-SGSProject-Desktop-Debug/debug.log`
- `home/htra/Desktop/SGSProject/build/linux_aarch64/standard/cn/SGStudio` 打包路径
## Discarded Directions

These directions were field-tested and rejected:

- Destroying `TouchNumKeyboardScreenOverlayHost` when the keyboard finishes.
  - Result: closing the keyboard and then closing the sweep panel could crash.
- Delaying `OverlayContainer` outside-dismiss from press/begin to release/end.
  - Result: the reopened sweep popup still closed on internal click.
- Treating the issue as a Qt Creator focus handoff or stale minibar drag state.
  - Result: the problem reproduced without Qt Creator focus involvement.

## Diagnostic Evidence

Temporary remote logging in `MiniBarWindow::eventFilter()` captured the failing click:

```text
[MiniBarPopupDebug] press watched QWidgetWindow(..., name = "miniBarSweepPopupWindow")
global QPoint(230,132)
activePopup QDialog(..., name="miniBarSweepPopup")
activeVisible true
activeGeom QRect(695,414 560x233)
belongsActive false
ownedArea false
...
[MiniBarPopupDebug] closing hosted transient from outside press watched QWidgetWindow(...)
```

Observations:

- The first event for the reopened sweep popup can be delivered to the popup's native `QWidgetWindow`, before a child `QWidget` such as `LabelButton` receives the press.
- `QWidgetWindow` is not in the `QDialog` QObject parent chain, so the old `objectBelongsTo(watched, m_activePopup)` guard returned false.
- Under layer-shell, `m_activePopup->frameGeometry()` and `QMouseEvent::globalPos()` can be in different coordinate semantics after the keyboard/sweep close-reopen sequence.
- With both ownership and geometry checks failing, `MiniBarWindow::eventFilter()` closed the hosted popup before the child button received the click.

## Final Fix

Implemented in `src/plugins/core/minibarwindow.cpp`:

- Added `objectBelongsToWidgetOrWindow(...)`.
- The helper treats both QObject parent-chain descendants and `widget->windowHandle()` as belonging to the widget.
- The existing inside-click guard for `m_activePopup` / `m_providerMenu` now uses that helper.

This keeps the fix narrow:

- No `TouchNumKeyboard` changes.
- No `OverlayContainer` changes.
- Existing `frameGeometry()` outside-click fallback remains for true outside clicks.

## Verification

Remote build:

```text
cd ~/Desktop/SGSProject
cmake --build build-SGSProject-Desktop-Debug --target Core -j4
```

Result:

```text
BUILD_EXIT=0
```

## Remote Cleanup

After the final field test, the temporary remote source synchronization was reverted so the Raspberry Pi workspace can later receive the fix through normal `git pull`.

Verified remote source cleanup:

```text
cd ~/Desktop/SGSProject
git status --short -- src/plugins/core/minibarwindow.cpp src/libs/controls/overlaycontainer.cpp
```

Result: no modified files for those paths.

## Follow-up: Sweep Type Popup Then Closing Sweep Panel Exits App

### Scope

Investigate the Raspberry Pi Wayland minibar sequence:

1. Open the frequency/power sweep popup.
2. Click `Sweep Type`.
3. The enum popup appears at the screen top-left but remains interactive.
4. Select frequency or power sweep mode.
5. Click the frequency/power sweep button again to close the sweep panel.
6. The whole application exits without hitting a debugger exception breakpoint.

### Current Static Evidence

Observation:

- `StepSweepPanel::onSweepTypeChanged()` only switches the page, previews the current carrier, and emits args if the panel is enabled.
- `MiniBarWindow::ensureSweepPopup()` creates the sweep host as `Qt::Tool | Qt::FramelessWindowHint | Qt::WindowStaysOnTopHint` and explicitly sets `WA_QuitOnClose=false`.
- `EnumTextButton` creates its dropdown as `PopupWidget`, and `PopupWidget` uses `Qt::Popup`.
- `PopupWidget` does not currently set `WA_QuitOnClose=false`.
- In Wayland minibar mode, every top-level `QWindow` is routed through the global layer-shell integration unless explicitly configured by the host.

Inference:

- The top-left enum popup is likely an unconfigured layer-shell top-level popup, not a `StepSweepPanel` layout issue.
- The later process exit is likely normal Qt application shutdown from a top-level close / last-window-close path, not a C++ exception, which explains why Debug does not stop at an exception location.
- The highest-risk object is the `PopupWidget` created by `EnumTextButton`: it is a top-level `Qt::Popup`, closes during selection, and currently has no explicit `WA_QuitOnClose=false` guard.

### Success Criteria

- Closing the enum popup and then closing the sweep panel must not emit `QApplication::aboutToQuit`.
- The debug log must show whether the failing run reaches normal Qt shutdown (`aboutToQuit` / `PluginManager::shutdown`) or a crash/abort path.
- Any fix should be narrow: first prevent helper popups from participating in app quit semantics; only then address layer-shell enum popup positioning if it remains independently reproducible.

### Verification Level

Runtime investigation on Raspberry Pi Wayland minibar, with temporary debug logs in local/remote source and user field testing.

### Runtime Evidence

Remote log after the temporary `PopupWidget` guard:

- `PopupWidget closeEvent ... quitOnClose false`
- `showSweepPopup toggleOff true ... miniBarSweepPopup ... quitOnClose false`
- no `QApplication::aboutToQuit`
- no `Start Shutdown`

Field result from the user:

- Selecting `Sweep Type` and then clicking the frequency/power sweep button closes the sweep panel without exiting the application.
- The remaining issue is that the `PopupWidget` opened by `Sweep Type` appears at the screen top-left.

Conclusion:

- The application-exit part was the `PopupWidget`/helper-popup `WA_QuitOnClose` pitfall.
- The top-left placement is a separate layer-shell geometry issue: `PopupWidget` is another top-level `Qt::Popup`, so in Wayland minibar mode it is routed through the global layer-shell integration but is not configured with anchors/margins.

### Positioning Fix Plan

Keep ownership split narrow:

- `PopupWidget` remains a Controls widget and does not link to LayerShellQt.
- `PopupWidget` keeps `WA_QuitOnClose=false` and leaves list/icon state in the existing `PopupWidget::viewMode()` API; it does not gain layer-shell-specific state.
- `MiniBarWindow`, already the layer-shell owner, catches minibar-owned `PopupWidget` show events and configures them as layer-shell transients using the visual geometry of their owning minibar popup.
- Existing normal main-window / Windows / X11 `EnumTextButton::updatePopupPosition()` behavior remains unchanged.

Success criteria:

- `Sweep Type` popup appears adjacent to the button inside the sweep panel rather than at screen top-left.
- Selecting frequency/power still updates the mode.
- Closing the sweep panel after selection still does not exit the app.
- Temporary debug logs are removed before finalizing the fix.

### Positioning Fix Implementation

Implemented in local source. The Raspberry Pi workspace was temporarily synchronized for build verification, then reverted at the user's request so it can receive the final code through normal `git pull`:

- `src/libs/controls/popupwidget.cpp`
  - Keeps `PopupWidget` as `Qt::Popup`.
  - Sets `WA_QuitOnClose=false`.
- `src/plugins/core/minibarwindow.cpp/.h`
  - Writes `sgstudioLayerShellVisualTopLeft` / `sgstudioLayerShellVisualSize` for layer-shell transients, not only the minibar base window.
  - Adds `configureLayerShellOwnedPopupWidget(...)`.
  - Catches minibar-owned `PopupWidget` show events in the application event filter.
  - Computes popup placement from the owning layer-shell popup's visual top-left plus the anchor widget's local coordinates.
  - Reads the popup's list/icon mode through `PopupWidget::viewMode()` instead of adding persistent dynamic state to the Controls widget.
  - Reuses `configureLayerShellTransient(...)` so the `PopupWidget` gets explicit layer-shell anchors/margins.
- Temporary sweep-exit debug logs were removed.

Scope refinement:

- Static call-site check: current source creates `PopupWidget` only from `EnumTextButton` with the button as parent.
- Win32, X11, and Wayland `--ui-mode=main` keep the existing `EnumTextButton::updatePopupPosition()` path.
- The layer-shell reposition path exists only in `MiniBarWindow` and is guarded by `SGS_HAVE_LAYER_SHELL_QT`, `isLayerShellActive()`, and a minibar-owned `PopupWidget` check.
- The only Controls-layer behavioral change is `WA_QuitOnClose=false`, which prevents a helper popup from becoming an application-exit trigger without changing popup geometry, content, selection, or close semantics.

Remote build during investigation, before the final no-dynamic-metadata scope refinement:

```text
cd ~/Desktop/SGSProject
cmake --build build-SGSProject-Desktop-Debug --target SGStudio Core -j4
```

Result:

```text
BUILD_EXIT=0
```

Remote cleanup after the user requested normal `git pull` synchronization:

```text
cd ~/Desktop/SGSProject
git restore -- src/libs/controls/popupwidget.cpp src/plugins/core/minibarwindow.cpp src/plugins/core/minibarwindow.h
git status --short -- src/app/main.cpp src/libs/controls/popupwidget.cpp src/plugins/core/minibarwindow.cpp src/plugins/core/minibarwindow.h
```

Result: no modified files for those paths.

Pending field verification:

- Open frequency/power sweep popup.
- Click `Sweep Type`.
- Confirm the dropdown appears next to the button, not at screen top-left.
- Select frequency/power mode.
- Click the frequency/power sweep button to close the panel.
- Confirm the application remains running.


## Follow-up: Hosted Panel Outside Click Needs Owned Overlay

### Scope

Analyze and fix the Raspberry Pi Wayland minibar behavior for hosted AM/business panels and `StepSweepPanel`:

- The hosted panel itself is interactive after the earlier fixes.
- Unlike `TouchNumKeyboard`, the hosted panel has no full-screen overlay surface.
- A click outside the panel can go directly to the window behind SGStudio, such as Qt Creator, so the minibar process does not receive that click and cannot close the panel.

### Observation vs Inference

Observation:

- `TouchNumKeyboard` creates a screen-sized transparent host when the minibar host cannot contain the keyboard.
- The keyboard then runs as a child widget inside `OverlayContainer`, so outside clicks are still delivered to SGStudio and can be consumed before the desktop gives focus to another application.
- Business and sweep panels are currently separate `Qt::Tool` top-level `QDialog` layer-shell transients. They have no equivalent outside-catching surface.

Inference:

- `MiniBarWindow::eventFilter()` can only close a hosted panel on application-internal outside events. It cannot close the panel when the compositor sends the pointer event directly to a different client.
- The missing piece is not another focus restore or `ApplicationDeactivate` fallback. Those paths were already shown to break panel internal interaction.
- The safer direction is to borrow the keyboard's owned overlay idea for hosted business/sweep panels, but keep the panel content and host styling intact.

### Constraints From Existing Fixes

- Do not re-enable `ApplicationDeactivate` closing for visible layer-shell hosted panels; that previously caused internal clicks to close AM/sweep panels.
- Do not rely on `frameGeometry()` global hit testing for layer-shell panel internals; native `QWidgetWindow` ownership was the reliable guard.
- Do not destroy `TouchNumKeyboardScreenOverlayHost` from keyboard finished paths; that was field-tested and caused a crash.
- Do not convert generic `Controls::Dialog` or system-keyboard dialogs to child overlays; the Wayland system keyboard path has a different protocol boundary.
- Keep `PopupWidget` layer-shell positioning owned by `MiniBarWindow`, using the existing visual geometry properties.

### Implementation Plan

- Add a `MiniBarWindow`-owned transparent full-screen overlay host only for layer-shell hosted business/sweep panels.
- Put the hosted `QDialog` inside a `Controls::OverlayContainer` child of that host as a `Qt::Widget`, so panel-internal clicks go to the panel and outside clicks go to the overlay.
- Configure the overlay host as an explicit layer-shell top-level with visual geometry properties, so nested `PopupWidget` and `TouchNumKeyboard` anchoring can still use the same visual coordinate model.
- On overlay outside input, call the existing `closeTransientWidgets()` path.
- Keep provider `QMenu` behavior unchanged in this step.

### Success Criteria

- Clicking outside an open AM/business panel closes the panel without giving the click to Qt Creator.
- Clicking outside an open StepSweep panel closes the panel without giving the click to Qt Creator.
- Panel-internal controls still work.
- Opening `TouchNumKeyboard` from a hosted panel still works, and clicking inside the panel to dismiss the keyboard does not close the panel.
- `Sweep Type` `PopupWidget` remains positioned relative to its button, not at the screen top-left.
- The expanded minibar does not auto-collapse as a side effect.

### Verification Level

Static edit first. Remote sync/build/run only when explicitly requested for this follow-up.

### Implementation

Implemented in `src/plugins/core/minibarwindow.cpp/.h`:

- Added a layer-shell-only `miniBarPopupOverlayHost`.
- The overlay host is a full-screen transparent `Qt::Tool` surface owned by `MiniBarWindow`.
- The overlay host is configured through the existing `configureLayerShellTransient(...)` path and receives the same visual geometry dynamic properties used by keyboard / `PopupWidget` positioning.
- Added a `Controls::OverlayContainer` child inside the overlay host.
- In layer-shell mode, business panel popup and sweep popup `QDialog` hosts are reparented into the overlay as child widgets before showing.
- The overlay outside handler calls `closeTransientWidgets()`.
- `MiniBarWindow::eventFilter()` now ignores events belonging to the popup overlay subtree so those clicks are handled by `OverlayContainer`, not by minibar drag/collapse logic.
- Non-layer-shell paths still use the existing top-level `QDialog` positioning and transient configuration.
- Provider `QMenu` is unchanged.
- `TouchNumKeyboard` and generic `OverlayContainer` code are unchanged.

KnowledgeBase updates:

- Updated `minibar_popup_host_style_and_focus_status.md`.
- Updated `minibar_wayland_layer_shell_qt_integration.md`.

### Static Verification

```text
git diff --check -- src/plugins/core/minibarwindow.cpp src/plugins/core/minibarwindow.h ...
```

Result:

- No whitespace errors.
- Windows working tree still reports the existing LF-to-CRLF warning for `minibarwindow.cpp/.h`.

### Remote Synchronization And Build

Pending field verification:

- Open AM/business panel, click outside, confirm panel closes and Qt Creator does not receive that click.
- Open StepSweep panel, click outside, confirm panel closes and Qt Creator does not receive that click.
- Open keyboard from panel, click panel blank area, confirm keyboard closes while panel stays open.
- Open `Sweep Type`, confirm dropdown positioning and selection still work.

## Follow-up Bug: Keyboard From Sweep Panel Shows Pressed State But Does Not Edit

User field report:

- Open StepSweep panel from expanded minibar.
- Open `TouchNumKeyboard` from a sweep panel numeric field.
- Keyboard buttons visually enter pressed state, but the edited value does not change.

Observation:

- A pressed visual state means pointer events can reach at least the keyboard button press path.
- Numeric keyboard text updates still depend on synthetic key events reaching the current focus widget / receiver chain.
- After the hosted-panel overlay change, a sweep-panel trigger widget resolves its `window()` to `miniBarPopupOverlayHost`, which is a full-screen layer-shell surface configured with `KeyboardInteractivityNone`.
- Because that host is large enough to contain the keyboard, `TouchNumKeyboard` can choose `OverlayInResolvedHost` and embed its own overlay inside the panel overlay host instead of creating the separate `TouchNumKeyboardScreenOverlayHost` path used by the compact minibar host.

Hypothesis:

- The keyboard is being nested inside the panel overlay host.
- That layer-shell host can receive pointer events but does not provide normal keyboard/focus interactivity, so `QApplication::focusWidget()` is not the keyboard edit/step edit when a button is clicked.
- This yields the exact symptom: button pressed style changes, but the synthetic key is delivered to the wrong focus target or to no effective target.

Design Constraints:

- Do not remove the panel overlay; it is currently the mechanism that lets outside clicks close AM/sweep panels before the desktop behind the app receives the click.
- Do not switch the whole panel overlay host to a broad keyboard-interactive mode unless logs prove it is necessary; that would be a larger window-protocol behavior change.
- Preserve keyboard behavior for normal minibar `Frequency` / `Level` entry points.
- Preserve sweep panel internal button/dropdown interaction.

Success Criteria:

- Opening keyboard from StepSweep panel and pressing number keys edits the target value.
- StepSweep panel remains open while interacting with the keyboard.
- Clicking outside the keyboard but inside panel closes only the keyboard according to keyboard policy; clicking outside the panel closes the panel.
- Normal minibar frequency/level keyboard still works.
- Remote Core build succeeds.

Implementation:

- Updated `src/libs/controls/touchnumkeyboard.cpp`.
- `TouchNumKeyboard` synthetic key dispatch now treats the keyboard input box as the fallback target when `QApplication::focusWidget()` is outside the keyboard subtree and the keyboard has an active receiver.
- This preserves the existing step-edit path when focus is correctly on `stepEdit`, while recovering from layer-shell / overlay focus loss where pointer events reach keyboard buttons but keyboard focus does not settle on the internal edit.
- If no receiver is configured, synthetic key dispatch preserves the old `QApplication::focusWidget()` behavior. This keeps Win32 and Wayland main-window / standalone keyboard usage from being forced through the keyboard's internal edit.
- The panel overlay implementation in `MiniBarWindow` was not broadened to keyboard-interactive layer-shell mode.
- No temporary debug logs were left in the source.
