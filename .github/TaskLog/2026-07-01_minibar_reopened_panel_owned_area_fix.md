# MiniBar Reopened Panel Owned-Area Fix

Date: 2026-07-01

## Scope

Fix the Raspberry Pi Wayland minibar regression where:

1. The first `AM` business panel opens.
2. Tapping `Rate` opens the numeric soft keyboard correctly.
3. After closing the keyboard and panel, reopening `AM` still shows the panel.
4. Tapping any button inside the reopened panel closes the panel instead of dispatching the button click or opening the keyboard.

## Observations

- `AM` / `FM` panel numeric buttons are still wired through `LabelButton -> IProperty::beginEditing -> prepareNumericKeyBoard(...)`.
- The first edit works, so the business binding and keyboard creation path are valid.
- `MiniBarWindow::eventFilter()` handles every application `MouseButtonPress` while minibar is expanded.
- If `isOwnedPopupArea(globalPos)` returns false while a hosted popup is visible, the filter calls `closeTransientWidgets()` before the clicked child button can run its normal handler.
- In Wayland layer-shell mode, hosted business panels are top-level layer surfaces configured by `configureLayerShellTransient(...)`.
- Current owned-area checks still rely on `frameGeometry()` for `m_activePopup`, `m_providerMenu`, `activeWindow()`, and `activePopupWidget()`.

## Inference

The second-open failure is most likely a hit-test problem, not a keyboard or AM/FM binding problem. After a layer-shell transient is hidden and shown again, Qt-side `frameGeometry()` can be stale or not match the compositor-placed visual rectangle. A click inside the visible business panel is then misclassified as an outside click, so the panel closes and the child button never opens the keyboard.

## Success Criteria

- Layer-shell transient owned-area hit testing uses the compositor-facing visual rectangle recorded when configuring the transient.
- `frameGeometry()` remains the fallback for non-layer-shell paths and widgets without recorded visual geometry.
- Provider `QMenu`, business `QDialog`, sweep `QDialog`, and placeholder popup all get the same recorded visual geometry before show.
- The change stays local to minibar popup hit testing and does not alter AM/FM property bindings or soft keyboard internals.

## Verification

Static:

- Inspect the event-filter path and transient geometry path.
- Run `git diff --check` on changed files.

Runtime to be performed by field test:

- On Raspberry Pi Wayland minibar mode, open `MOD -> AM`, tap `Rate`, close keyboard/panel, reopen `AM`, then tap `Rate`, `Depth`, shape, or other modulation-panel buttons.

## Implementation

- Added a local `layerShellVisualRect(...)` helper in `MiniBarWindow` to read the existing layer-shell visual geometry dynamic properties.
- Added `widgetGlobalRectContains(...)` so owned-area checks use the recorded visual rect when present, otherwise keep the old `frameGeometry()` fallback.
- Updated `ownedMiniBarWindowContains(...)` and `isOwnedPopupArea(...)` to route provider menu, active popup, active popup widget, active window, and minibar self hit tests through the shared helper.
- Updated `configureLayerShellTransient(...)` to store every transient popup's visual `topLeft/size` before show.
- Updated the Wayland/minibar KnowledgeBase docs with the new hit-test rule.

## Static Verification Result

Passed:

```text
git diff --check -- src/plugins/core/minibarwindow.cpp .github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md .github/KnowledgeBase/minibar_popup_host_style_and_focus_status.md .github/TaskLog/2026-07-01_minibar_reopened_panel_owned_area_fix.md
```

Only the existing CRLF normalization warning was reported for `src/plugins/core/minibarwindow.cpp`.
