# 2026-07-06 minibar direct keyboard layer-shell overlay

## Scope

Investigate and fix the regression after `5e0cc7b92621c21e735389fdcf355467a23666e6`
where the minibar itself stays visible above SAStudio, but direct minibar
`Freq` / `Level` numeric keyboards are created behind SAStudio and become
visible only when SAStudio opens a modal dialog.

Out of scope:

- Reworking sweep/business popup presentation, because `StepSweepPanel` numeric
  keyboards are currently reported as visible and dismiss correctly.
- Reintroducing the reverted `TouchNumKeyboardScreenOverlayHost` show-event
  layer-shell retrofit, because the KnowledgeBase records that field testing
  falsified that direction.

## Observations

- `5e0cc7b...` changed the minibar base surface and minibar-owned transient
  layer surfaces from `LayerTop` to `LayerOverlay`.
- Current HEAD `94414146` reverted a later attempt that configured
  `TouchNumKeyboardScreenOverlayHost` after `Show`; that reverted attempt was
  not a reliable fix.
- `MiniBarWindow` binds direct `Freq` / `Level` buttons through shared
  `IProperty::beginEditing`, then opens a generic `TouchNumKeyboard`.
- `TouchNumKeyboard` falls back to a separate screen-sized top-level overlay
  host when its resolved owner window is too small to contain the keyboard.
- A keyboard opened from `StepSweepPanel` resolves to the already managed
  fullscreen `miniBarPopupOverlayHost`, so it stays visible above SAStudio.

## Inference

The direct minibar `Freq` / `Level` path still lets `TouchNumKeyboard` create an
independent screen overlay host. In the global layer-shell integration process,
that host becomes another layer surface but is not owned or layered through
`MiniBarWindow`'s managed overlay policy. Repeated clicks can therefore create
multiple hidden keyboard sessions because the existing hidden session is not the
active managed minibar keyboard.

## Plan

1. Add a narrow host override to `TouchNumKeyboard` so a caller can keep the
   clicked button as the anchor while forcing the containing overlay host.
2. In `MiniBarWindow`, handle only edit triggers originating from minibar-owned
   buttons, and in layer-shell mode route the direct numeric keyboard into the
   existing managed fullscreen overlay host.
3. Track the active direct numeric keyboard so opening another minibar transient
   closes it first and repeated `Freq` / `Level` clicks cannot stack hidden
   instances.
4. Guard `CommonPanel`'s shared `Center` / `Level` editing slots by trigger
   ownership to avoid another live panel responding to a minibar-origin edit.

## Success Criteria

- Direct minibar `Freq` and `Level` keyboards are visible above SAStudio in
  Wayland/labwc while SAStudio has a normal window open.
- Clicking outside the direct minibar keyboard dismisses it.
- Repeated clicks on `Freq` or `Level` do not leave multiple hidden keyboard
  instances.
- Sweep-panel numeric keyboards keep their current visible/dismiss behavior.


## Implementation Notes

- Added `TouchNumKeyboard::setPreferredOverlayHost(QWidget *)`.
- Updated `TouchNumKeyboard::initialOverlayPosition()` to prefer
  `sgstudioLayerShellVisualTopLeft` / `sgstudioLayerShellVisualSize` anchor
  geometry whenever available, not only for the old independent screen-host
  fallback.
- Updated `MiniBarWindow` direct `Center` / `Level` editing to:
  - ignore `beginEditing` triggers that do not originate from minibar widgets;
  - close existing minibar transients before opening a direct numeric keyboard;
  - route direct numeric keyboards into the managed `miniBarPopupOverlayHost`
    in layer-shell mode while preserving the clicked `LabelButton` as anchor;
  - track and close the active direct numeric keyboard session.
- Added `CommonPanel` trigger ownership guards for shared `Center` / `Level`
  `beginEditing` slots.

## Verification Result

- Local `git diff --check`: pass.
- Remote `git diff --check`: pass after converting Windows-transferred files
  back to LF.
- Remote Debug build:

```bash
cmake --build build-SGSProject-Desktop-Debug --target Core -j4
```

Result: `BUILD_EXIT=0`. Existing warnings were limited to unrelated
`-Winconsistent-missing-override` diagnostics in older files.

### Follow-up After Field Test

- Field result: the hosted `PxSaveFileDlg` is now clickable, but its centered
  size/position is worse than the earlier fullscreen file-dialog presentation.
- Field result: clicking the filename `QLineEdit` does not bring up the Wayland
  system keyboard.
- Remote screenshot confirmed the hosted Save dialog is shown as a centered
  large child panel and is clipped on the right edge.

Planned adjustment:

1. Make hosted Save dialogs fill the whole `miniBarPopupOverlayHost` rect.
2. After changing the overlay host from `KeyboardInteractivityNone` to
   `KeyboardInteractivityOnDemand`, force a layer-shell state commit by
   re-submitting the current margins. The vendored layer-shell wrapper commits
   margin changes after configure, but `setKeyboardInteractivity(...)` by itself
   only updates pending state.
3. Apply the same commit step when restoring the previous keyboard
   interactivity after the dialog closes.

### Follow-up: System Keyboard Still Hidden

Field result after the fullscreen hosted Save dialog change:

- The Save dialog is full screen and clickable.
- With SAStudio not running, tapping/clicking the filename edit still does not
  show the Wayland system keyboard.
- External mouse/keyboard can focus and edit the filename.

Remote observation:

- Only `SGStudio --ui-mode=minibar` and `squeekboard` were running; SAStudio was
  not running.
- A screenshot showed the full-screen hosted `PxSaveFileDlg`, with the filename
  edit focused.
- A manual DBus call to `sm.puri.OSK0.SetVisible(true)` returned successfully,
  but the screenshot still did not show the keyboard.

Updated inference:

- The failure is not primarily that `Keyboard::showPopup()` is not called.
- The system keyboard can be requested, but the full-screen SGS layer-shell
  overlay remains above it. This matches the `5e0cc7b...` change that promoted
  minibar-owned layer-shell surfaces from `LayerTop` to `LayerOverlay`.
- The previous `KeyboardInteractivityOnDemand` change is insufficient by itself:
  it may help focus semantics, but it does not solve overlay-layer occlusion.

Rejected attempted fix:

1. Keeping the Save dialog full-screen while the filename edit is not active and
   resizing the overlay host only for text input looked plausible, but field
   testing showed the approach was not reliable.
2. Reserving bottom space during touch/mouse input can prevent the filename
   `QLineEdit` from entering edit state at all.
3. Reserving bottom space after queued `FocusIn` still did not restore the
   touchscreen path on the remote Raspberry Pi.
4. The keyboard/focus-specific modifications are therefore rolled back. The
   unresolved issue is documented here for a later, more targeted investigation.

Verification:

- Remote source `git diff --check`: pass after LF normalization.
- Remote Debug build:

```bash
cmake --build build-SGSProject-Desktop-Debug --target Core -j4
```

Result: `BUILD_EXIT=0`.
- Existing remote `SGStudio` / `SAStudio4` processes were stopped so the next
  field test starts with the rebuilt `Core` plugin.

### Follow-up: Touch Focus Regression

Field result:

- After reserving bottom space on `TouchBegin` / `MouseButtonPress`, touch input
  could no longer enter the filename `QLineEdit`; no caret appeared.
- External mouse/keyboard could still edit the field.

Inference:

- Resizing the layer-shell overlay during `TouchBegin` moves the dialog before
  the `QLineEdit` has completed its own touch/focus handling. This can lose the
  touch focus sequence before `PxSaveFileDlg`'s original
  `Keyboard::showPopup(ui->name)` path establishes focus.

Adjustment:

1. Roll back the immediate reserve-on-press behavior.
2. Roll back the queued-`FocusIn` reserve behavior as well, because the remote
   touchscreen still could not enter the filename `QLineEdit` edit state.
3. Keep the original touch/mouse event path undisturbed until the system-keyboard
   problem has a verified root cause.

## Follow-up: Minibar Business Save Dialog

### Remote Observation

- 2026-07-06 remote state on Raspberry Pi had:
  - `SAStudio4`
  - `SGStudio --ui-mode=minibar`
  - `squeekboard`
- The user clarified that the screenshot was captured after Alt+Tab brought the
  file dialog forward. The original failure state is:
  `PxSaveFileDlg` is below SAStudio, while SGS minibar + the PM panel remain
  above SAStudio.
- The screenshot after Alt+Tab showed the custom `PxSaveFileDlg` visible, but
  still below the minibar `miniBarPopupOverlayHost` and below the hosted
  modulation panel.
- The fullscreen transparent overlay host remained above the file dialog. This
  explains why Alt+Tab can reveal the file dialog while the dialog is still not
  operable: pointer input reaches the layer-shell overlay surface first.
- Per user request, the remote `SGStudio` and `SAStudio4` processes were then
  terminated with exact process-name matching.

### Current Code Chain

- `PmPanel::onSaveClicked()` calls
  `Controls::getSaveFilePath(..., this)`.
- On Linux, `getSaveFilePath()` creates a stack `PxSaveFileDlg` and calls
  `dialog.exec()`.
- In minibar Wayland mode, the modulation panel itself has already been
  reparented into `miniBarPopupOverlayHost` through
  `prepareLayerShellPopupOverlay()`.
- `PxSaveFileDlg` is still shown as a normal top-level `Dialog`; it is not
  hosted inside the existing overlay and is not given its own minibar
  layer-shell policy.

### Inference

This is not primarily a system keyboard failure. The system keyboard is only
requested by `PxSaveFileDlg` when its filename `QLineEdit` receives focus.
The earlier failure is that the Save dialog is outside the managed minibar
overlay hierarchy, while the managed overlay remains on top and blocks input.

### Proposed Fix Boundary

Preferred fix:

1. Add a narrow Linux/Wayland/minibar dialog-hosting path for custom
   `Controls::Dialog` file dialogs opened from hosted minibar panels.
2. Reuse the existing `miniBarPopupOverlayHost` and `OverlayContainer` instead
   of creating another independent top-level layer surface.
3. Run the dialog with the local dialog loop helper already used by hosted
   overlays, so synchronous `getSaveFilePath()` semantics remain unchanged.
4. Keep the default `QFileDialog`/`PxSaveFileDlg::exec()` path unchanged for
   Win32, main-window mode, and non-minibar Linux paths.

Rejected as first choice:

- Globally changing every `QDialog` or every layer-shell transient to
  `KeyboardInteractivityOnDemand`. That could make unrelated popups steal focus
  from SAStudio and does not solve the fullscreen overlay input blocker.

### Implementation Plan

1. Add a default-disabled file-dialog execution hook in `Controls`, used by the
   Linux custom `PxSaveFileDlg` branch only.
2. Register the hook from `MiniBarWindow` while minibar exists.
3. Let the hook accept only Wayland layer-shell requests whose parent belongs to
   the currently hosted minibar business/sweep popup.
4. Reparent the dialog into `miniBarPopupOverlayHost` / `OverlayContainer`, raise
   it above the panel, temporarily block outside clicks without closing the
   panel, and keep the dialog full-screen inside the existing overlay host.
5. Restore the previous hosted panel after the dialog finishes.

### Implementation Notes

- Added `Controls::FileDialogExecutionHook` with
  `setFileDialogExecutionHook(...)` / `clearFileDialogExecutionHook()`.
- The Linux `Controls::getSaveFilePath()` custom `PxSaveFileDlg` branch asks the
  hook first; when no hook accepts the dialog it falls back to the existing
  `dialog.exec()` path.
- `MiniBarWindow` registers the hook only in `SGS_HAVE_LAYER_SHELL_QT` builds
  and the hook returns `false` unless:
  - layer-shell is active;
  - a hosted minibar popup is visible;
  - the save request parent belongs to that hosted popup.
- Accepted Save dialogs are reparented into `miniBarPopupOverlayHost` /
  `OverlayContainer` and run with `OverlayContainer::execLocalDialogLoop(...)`.
- During the hosted Save dialog:
  - outside clicks are blocked without closing the underlying panel;
  - the dialog is stretched to the full `miniBarPopupOverlayHost` rect;
  - the previous active panel is restored after the dialog finishes.

### Verification Result

- Local source `git diff --check`: pass.
- Remote source `git diff --check`: pass after converting Windows-transferred
  files back to LF.
- Remote Debug build:

```bash
cmake --build build-SGSProject-Desktop-Debug --target Core -j4
```

Result: `BUILD_EXIT=0`. Existing warnings were limited to unrelated
`-Winconsistent-missing-override` diagnostics in older files.

### Follow-up: Rollback And Open Dialog

Field result:

- After the queued-`FocusIn` reserve change, the remote touchscreen still could
  not enter the `PxSaveFileDlg` filename `QLineEdit` edit state; no caret
  appeared.
- Per rollback request, the keyboard/focus-specific hosted-dialog changes are
  removed. The remaining hosted file-dialog behavior is only: put the custom file
  dialog inside `miniBarPopupOverlayHost`, stretch it full-screen, and run it
  with `OverlayContainer::execLocalDialogLoop(...)`.

Open dialog attempted scope:

- `PxOpenFileDlg` opened from a hosted minibar business/sweep panel has the same
  layer/order problem as `PxSaveFileDlg`: as a normal top-level dialog it can sit
  behind SAStudio or behind the minibar overlay.
- A first attempt made the Linux `Controls::getOpenPath()` custom-dialog branch
  use the same default-disabled execution hook as `getSaveFilePath()`.

Result: pass. Existing warnings were limited to unrelated
`-Winconsistent-missing-override` diagnostics in older files.
- Remote `SGStudio` processes were stopped after the build; `pgrep -a SGStudio`
  returned no process.

### Follow-up: Open Dialog Geometry

Observation:

- Remote screenshot showed `PxOpenFileDlg` at the screen's upper-left with its
  normal size-hint geometry instead of filling the screen.
- The dialog could still receive pointer input, which means the immediate
  failure is geometry/hosting state rather than a total input blocker.

Inference:

- Some open-file requests can fail the strict hosted-source check and fall back
  to the normal top-level `dialog.exec()` path.
- Even when hosted, `Controls::Dialog::showEvent()` calls `moveToCenter()`, which
  calls `adjustSize()` and can fight the full-screen geometry applied by
  `MiniBarWindow::execLayerShellHostedDialog(...)`.

Attempted fix:

1. Keep the hook narrow to minibar layer-shell hosted popups, but let it accept
   a request when either the request parent, current focus widget, active modal
   widget, or active window belongs to the active business/sweep popup content.
2. While running a hosted file dialog, temporarily constrain the dialog
   min/max size to the overlay parent size and set geometry to `(0, 0, parent)`.
   Restore the original min/max size after the dialog finishes.

Verification:

- Remote source `git diff --check`: pass after LF normalization.
- Remote Debug build:

```bash
cmake --build build-SGSProject-Desktop-Debug --target Core -j4
```

Result: pass.
- Remote `SGStudio` processes were stopped after the build; `pgrep -a SGStudio`
  returned no process.

Field result after the attempted fix:

- `PxOpenFileDlg` still appeared at the screen's upper-left instead of filling
  the hosted overlay.

Rollback:

1. `Controls::getOpenPath()` was restored to the original Linux
   `PxOpenFileDlg dialog; dialog.exec();` path.
2. `MiniBarWindow::execLayerShellHostedDialog(...)` was restored to the narrower
   source-parent check against `m_activePopup`.
3. The temporary min/max size constraint added for hosted file dialogs was
   removed.

Current status:

- The `PxOpenFileDlg` fullscreen hosted-overlay issue is unresolved.
- The rollback intentionally leaves the already verified `PxSaveFileDlg`
  hosted-overlay path in place.

Rollback verification:

- Remote source `git diff --check`: pass after LF normalization.
- Remote Debug build:

```bash
cmake --build build-SGSProject-Desktop-Debug --target Core -j4
```
