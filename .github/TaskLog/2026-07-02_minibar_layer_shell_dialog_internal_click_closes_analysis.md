# MiniBar Layer-Shell Dialog Internal Click Closes Analysis

Date: 2026-07-02

## Scope

Analyze why Raspberry Pi Wayland minibar hosted modulation dialogs such as `AM` close immediately when clicking anywhere inside the dialog.

This TaskLog also corrects the previous documentation note that proposed visual-rectangle hit testing as the current conclusion. The field test showed that change was wrong: it prevented menu, frequency/power, and sweep popups from opening correctly.

## Current Field Evidence

- The previous visual-rect hit-test patch has been reverted by the user.
- With current code, `AM` panel can be shown.
- Clicking anywhere inside the shown panel closes it immediately.
- The problem affects the hosted dialog behavior itself, not just numeric buttons or soft-keyboard creation.

## Observations From Current Code

- `showBusinessPanelPopup(...)` creates the business popup as `QDialog(this, Qt::Tool | Qt::FramelessWindowHint | Qt::WindowStaysOnTopHint)`.
- The business popup is not a `Qt::Popup`, so it should not auto-close by normal Qt popup semantics.
- `MiniBarWindow::eventFilter(...)` closes visible hosted transients on:
  - `ApplicationDeactivate`, without any pointer-position hit test.
  - `MouseButtonPress` when `isOwnedPopupArea(globalPos)` is false.
- `configureLayerShellTransient(...)` sets hosted transients to:
  - `LayerTop`
  - `AnchorTop | AnchorRight`
  - `exclusiveZone = 0`
  - `KeyboardInteractivityNone`
- Vendored `QWaylandLayerShellIntegration::createShellSurface(...)` creates a layer surface for every top-level `QWindow`.
- Vendored `QWaylandLayerShell` still has `// TODO: Popups`; AM/FM dialogs are not real xdg child popups of the minibar layer surface.

## Current Inference

The stronger current hypothesis is activation/focus, not visual hit testing:

1. `Shell::useLayerShell()` makes the AM panel `QDialog` an independent layer surface.
2. `KeyboardInteractivityNone` tells the compositor that this surface must never receive keyboard focus.
3. Clicking the dialog can therefore cause Qt to emit `ApplicationDeactivate` or leave no active owned modal/popup widget, even though pointer input is delivered to the visible dialog.
4. `MiniBarWindow::eventFilter(...)` treats `ApplicationDeactivate` while a hosted transient is visible as an outside-dismiss signal and calls `closeTransientWidgets()`.
5. This branch has no coordinate check, so clicking inside the dialog body and clicking outside are indistinguishable to the current close policy.

This explains why clicking blank space inside the AM panel also closes it: the clicked child does not need to be a numeric button; the host closes the dialog before normal child interaction matters.

## Discarded Direction

Do not treat the previous visual-rectangle hit-test patch as the current fix. Field testing showed it broke menu and sweep/frequency-power popup behavior. The docs should keep it only as a retracted hypothesis, not as a maintenance rule.

## Next Design Questions

- Whether hosted business/sweep dialogs should use `KeyboardInteractivityOnDemand` instead of `None` while the minibar base window remains `None`.
- Whether `ApplicationDeactivate` should avoid closing `m_activePopup` when a hosted layer-shell transient is visible and the close decision cannot be backed by pointer hit testing.
- Whether the long-term fix should move away from process-global layer-shell for every top-level window and instead make layer-shell opt-in for the minibar host.

## Minimal Implementation Plan

Goal: first guarantee that clicking inside a visible hosted dialog, such as `AM` or frequency/power sweep, does not auto-close it.

Planned narrow changes:

1. Add a local QObject parent-chain helper for checking whether the current event target belongs to a visible hosted transient.
2. In the `MouseButtonPress` branch of `MiniBarWindow::eventFilter(...)`, return early for events whose `watched` object belongs to `m_activePopup` or the provider menu. This treats object ownership as stronger evidence than global geometry for an inside click.
3. In the `ApplicationDeactivate` branch, when layer-shell is active and `m_activePopup` is visible, do not call `closeTransientWidgets()` based on activation loss alone. In layer-shell mode a click inside a `KeyboardInteractivityNone` hosted dialog can look like application deactivation even though it is not an outside click.
4. Keep normal outside click handling through `MouseButtonPress + isOwnedPopupArea(...)` for other in-app clicks.

## Minimal Implementation Result

Implemented in `src/plugins/core/minibarwindow.cpp`:

- Added local `objectBelongsTo(...)` helper for QObject parent-chain checks.
- `MiniBarWindow::eventFilter(...)` now lets `MouseButtonPress` events pass through when `watched` belongs to the visible `m_activePopup` or provider menu.
- In layer-shell mode, `ApplicationDeactivate` no longer closes a visible `m_activePopup` by itself, because hosted dialogs currently use `KeyboardInteractivityNone` and can look deactivated even for an internal click.

Expected field behavior:

- Open AM / FM / frequency-power sweep dialog.
- Click blank space or a control inside the dialog.
- The dialog should remain open and the clicked child should receive the event.

Static check:

```text
git diff --check -- src/plugins/core/minibarwindow.cpp .github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md .github/KnowledgeBase/minibar_popup_host_style_and_focus_status.md .github/TaskLog/2026-07-02_minibar_layer_shell_dialog_internal_click_closes_analysis.md
```

Result: passed, with only CRLF normalization warnings for the touched Windows worktree files.

## Follow-Up Field Result

The first opened frequency/power sweep popup behaves correctly after the host close-policy patch:

- First click on the sweep button opens `StepSweepPanel`.
- Internal interaction works.
- The visible sweep popup can only be closed by clicking the sweep button again.

After closing it with the sweep button and opening it again, the failure returns:

- Clicking a numeric control can still open the soft keyboard.
- Clicking a non-keyboard control such as the enable switch also takes effect.
- The sweep popup then closes unconditionally.

## Updated Inference For First-Close / Second-Open

The difference between the first and second popup is top-level host reuse:

- First open creates a fresh `m_sweepPopup` `QDialog`, configures it as a layer-shell transient, and shows it.
- Closing by clicking the sweep button calls `closeTransientWidgets()`.
- For `m_sweepPopup`, `closeTransientWidgets()` only calls `hide()` and keeps the same `QDialog` / native layer surface object for reuse.
- Second open reuses that hidden/unmapped top-level layer surface.

In layer-shell mode, reusing a hidden top-level `QDialog` is a plausible source of stale activation / surface state. This matches the field result better than sweep-panel binding bugs, because the second-open click still reaches the child control before the host disappears. The soft keyboard is not the trigger; it is only one visible way to prove the child click already ran.

## Follow-Up Minimal Plan

For the sweep popup only:

1. When closing `m_sweepPopup`, detach and preserve `m_sweepPanel`.
2. Destroy the `QDialog` host instead of hiding it.
3. Let `ensureSweepPopup()` create a fresh top-level host on the next open.

This keeps `StepSweepPanel` state but avoids reusing a previously hidden layer-shell top-level surface.

## Follow-Up Implementation Result

Implemented for `m_sweepPopup` in `MiniBarWindow::closeTransientWidgets()`:

- Detach `m_sweepPanel` from the popup layout.
- Hide and reparent `m_sweepPanel` to `nullptr` so its state and property bindings survive.
- Set `m_sweepPopup` to `nullptr`.
- `deleteLater()` the old `QDialog` host.

The next `showSweepPopup(...)` call will reuse the existing `StepSweepPanel` but create a fresh `QDialog` host and fresh layer-shell surface.

Follow-up field result:

- The second sweep popup no longer closes when clicking its title.
- The dialog only shows the title; the `StepSweepPanel` content is missing.

Root cause:

- `closeTransientWidgets()` hides `m_sweepPanel` before detaching it from the old host.
- The next `ensureSweepPopup()` re-adds the same hidden panel to the new dialog layout.
- A hidden widget in the layout does not contribute visible content, so the new dialog sizes to the title only.

Follow-up fix:

- After `layout->addWidget(m_sweepPanel)` in `ensureSweepPopup()`, call `m_sweepPanel->show()` and `m_sweepPanel->updateGeometry()` before `popup->adjustSize()`.

## Soft Keyboard Outside-Dismiss Follow-Up

Further field result:

- If the soft keyboard is opened from `StepSweepPanel` and then dismissed by clicking inside `StepSweepPanel`, the dismissing click is consumed by the keyboard overlay.
- After closing and reopening the sweep popup, the first internal click can still work, but the second click closes the sweep popup. After that, internal clicks may close the popup immediately.
- Qt Creator does not have to receive focus for this to reproduce.

Updated root-cause inference:

- The stale minibar drag-state hypothesis was wrong and has been reverted from code.
- The stronger current suspect is `TouchNumKeyboardScreenOverlayHost`.
- In Wayland minibar mode, `TouchNumKeyboard` cannot fit inside the sweep popup, so it creates a screen-sized transparent `Qt::Tool` host for the keyboard overlay.
- Because `QT_WAYLAND_SHELL_INTEGRATION=layer-shell` is process-global, this transparent host is also a top-level layer surface.
- `OverlayContainer` intentionally consumes clicks outside the keyboard to close it; that part is expected.
- The problematic part is that `TouchNumKeyboardScreenOverlayHost` was only hidden when the keyboard finished, and was left alive until the keyboard object was later destroyed.
- A hidden but still-live top-level layer surface is a plausible source of the later “one click offset / stale overlay” behavior.

Implemented minimal fix:

- Reverted the stale drag-state patch in `MiniBarWindow::eventFilter(...)`.
- `TouchNumKeyboard::onFinished(...)` now explicitly calls `hidePresentationSurfaces()`.
- `TouchNumKeyboard::hidePresentationSurfaces()` now destroys `screenOverlayHost` with `deleteLater()` and clears the pointer instead of only hiding it.
- If the overlay belongs to that screen host, it is also scheduled for deletion and cleared.

Expected behavior:

- The click that closes the keyboard may still be consumed by the overlay.
- After keyboard dismissal, no hidden full-screen overlay layer surface should remain alive to affect the next sweep popup interaction sequence.

Static check:

```text
git diff --check -- src/plugins/core/minibarwindow.cpp src/libs/controls/touchnumkeyboard.cpp .github/TaskLog/2026-07-02_minibar_layer_shell_dialog_internal_click_closes_analysis.md
```

Result: passed, with only the existing CRLF normalization warning for `src/plugins/core/minibarwindow.cpp`.

## Documentation Update

- Updated `.github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md` to retract the visual-rect hit-test rule and add the current activation/focus hypothesis.
- Updated `.github/KnowledgeBase/minibar_popup_host_style_and_focus_status.md` with the same retraction and current hosted-dialog internal-click analysis.
- Updated `.github/KnowledgeBase/Index.md` descriptions for the two minibar popup/layer-shell documents.

Static check:

```text
git diff --check -- .github/KnowledgeBase/Index.md .github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md .github/KnowledgeBase/minibar_popup_host_style_and_focus_status.md .github/TaskLog/2026-07-02_minibar_layer_shell_dialog_internal_click_closes_analysis.md
```

Result: passed.
