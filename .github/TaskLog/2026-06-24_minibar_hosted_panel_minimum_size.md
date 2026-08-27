# Main Window Hosted Panel Minimum Size Restore

## Scope

Restore the pre-minibar minimum-size behavior for business panels shown in the main window, especially the OFDM panel, without reintroducing panel-level compact APIs or duplicating mini panel implementations.

Touched area:

- `src/plugins/core/mainwindowchrome_win.cpp`
- `.github/KnowledgeBase/mainwindow_frameless_minimum_size_contract.md`
- `.github/KnowledgeBase/Index.md`

## Verification Level

`static`

The task is a layout-behavior analysis and host-side fix. No build or runtime verification was requested.

## Observations

- `src/plugins/analog/ofdmpanel.ui` has no explicit panel minimum size. Its old minimum width/height came from the Qt layout's child `minimumSizeHint()` values.
- Global QSS gives normal `SwitchButton QPushButton` a `min-width: 72px`. In OFDM, the two lower-row `SwitchButton` controls are therefore the strongest width constraint.
- `InfoButton` / `LabelButton` labels currently allow horizontal compression with `minimumWidth(0)`, which lowers the content's natural width but does not remove the `SwitchButton` child-button floor in normal mode.
- `MainWindow` is frameless on Windows and delegates `WM_GETMINMAXINFO` to `MainWindowChromeWin`.
- `MainWindowChromeWin::handleGetMinMaxInfo()` currently rewrites maximized bounds and returns handled, but it does not populate `MINMAXINFO::ptMinTrackSize` from Qt's current layout minimum.
- The visible failure is in the main window, not only in minibar popups: native resizing can push the top-level window below the current central-widget layout's usable minimum.

## Inference

The old behavior relied on top-level layout constraint propagation: the main window could not be resized below the hosted content's effective minimum size. In the current Windows frameless path, the app handles `WM_GETMINMAXINFO` but omits the minimum tracking size. That leaves the native resize loop free to shrink the window below Qt's dynamic `minimumSizeHint()`, so OFDM controls collapse even though the layout still reports a non-zero content minimum.

## Design

- Keep the fix at the Windows chrome boundary where the native resize contract is owned.
- Compute the current Qt minimum from `minimumSize().expandedTo(minimumSizeHint())`.
- Activate the current Qt layout before reading the hint so active business page / responsive layout changes are reflected.
- Convert the Qt logical size to native pixels with the current device-pixel ratio.
- Write the result to `MINMAXINFO::ptMinTrackSize` while preserving the existing maximized work-area handling.

## Success Criteria

- The Windows main window reports a native minimum track size derived from the current Qt layout.
- OFDM can no longer be dragged into a fully collapsed two-column layout.
- The fix remains host/chrome-side and generic for all hosted panels.
- No unrelated QSS, OFDM panel, minibar popup, or global `SwitchButton` behavior changes are introduced.

## Static Verification

- Confirmed `MainWindowChromeWin::handleGetMinMaxInfo()` now writes `ptMinTrackSize`.
- Confirmed the minimum is computed from the current Qt window `minimumSize()` and `minimumSizeHint()`.
- Confirmed the existing maximized work-area handling remains in place.
- No build or runtime test was run.

## Documentation Update

- Added a KnowledgeBase document for the Windows frameless main-window minimum-size contract.
- Clarified that Qt has no `setMinimumSizeHint()` call path here: `minimumSizeHint()` is read from the current layout, while the effective native resize constraint is written to Win32 `MINMAXINFO::ptMinTrackSize`.
