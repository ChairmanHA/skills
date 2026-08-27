# Minibar Mod Provider Popup Wayland Fix

Date: 2026-07-01

## Scope

Fix the minibar `MOD` provider list on Raspberry Pi Wayland/layer-shell so the provider list is not shown as a full-screen popup and selecting an item opens the corresponding business panel popup, such as AM or FM.

## Verification Level

static

No build or runtime verification is planned unless explicitly requested.

## Observations

1. `MiniBarWindow` hosts AM/FM/Sweep panels with `QDialog(Qt::Tool | Qt::FramelessWindowHint | Qt::WindowStaysOnTopHint)`, matching the current minibar popup host guidance.
2. The `MOD` provider list still uses `QMenu::popup(...)`.
3. `MiniBarBusinessMenuHost` is already the entry owner for the provider list; it maps business objects to actions and emits `businessTriggered(...)`.
4. The Raspberry Pi field result is that clicking the `MOD` button shows a full-screen popup, then clicking an item hides that popup but does not open the AM/FM business panel.
5. Theme files only style title-bar menus (`Core--Internal--TitleBar QMenu`); the minibar provider menu has no explicit local style.

## Inference

The provider list is likely hitting an unreliable `QMenu` / popup-window path when its owner is a Wayland layer-shell surface. Reusing the existing tool-style minibar popup host pattern for the provider list should avoid the full-screen popup behavior and keep triggered handling inside normal QWidget event delivery.

## Success Criteria

1. The `MOD` provider list is shown as a compact menu-like widget near the `MOD` button, not as a full-screen native popup.
2. The provider list uses explicit minibar-local styling that matches the Win32 dark screenshot and has a light-theme counterpart.
3. Selecting an item updates the current entry business and emits the existing `businessTriggered(...)` signal.
4. `MiniBarWindow` still opens the corresponding hosted panel popup through `showBusinessPanelPopup(...)`.
5. Existing business reload, visibility, selection, and selected-business persistence behavior remains in `MiniBarBusinessMenuHost`.

## Implementation Plan

1. Replace the minibar provider `QMenu` with a tool-style `QWidget` host containing one checkable `QPushButton` per visible business.
2. Keep `MiniBarBusinessMenuHost` as the model/entry host, but map businesses to buttons instead of menu actions.
3. Add minibar-local provider list QSS for dark and light themes.
4. Update owned-popup, close, toggle, and positioning logic to use the new provider popup widget.
5. Run static diff checks.

## Implementation Update

Files changed:

1. `src/plugins/core/minibarwindow.h`
2. `src/plugins/core/minibarwindow.cpp`
3. `src/plugins/core/minibarbusinessmenuhost.h`
4. `src/plugins/core/minibarbusinessmenuhost.cpp`
5. `.github/KnowledgeBase/minibar_popup_host_style_and_focus_status.md`
6. `.github/KnowledgeBase/ui_independent_runtime_and_minibar_design.md`

Implemented:

1. Replaced the minibar provider `QMenu` with `m_providerPopup`, a non-modal tool-style `QDialog` using the same window flag family as the existing minibar business and sweep popup hosts.
2. Changed `MiniBarBusinessMenuHost` from action/menu mapping to button/container mapping while preserving the existing `IBusinessEntryHost` API and `businessTriggered(...)` signal.
3. Added explicit dark and light QSS for `QDialog#miniBarProviderPopup` and `QPushButton#miniBarProviderItem`.
4. Kept provider list close/toggle/owned-area behavior inside `MiniBarWindow::closeTransientWidgets(...)`, `isOwnedPopupArea(...)`, and `eventFilter(...)`.
5. Kept AM/FM panel opening on the existing `showBusinessPanelPopup(...)` path.
6. Updated KnowledgeBase notes so future minibar provider list changes keep the tool-style host instead of reverting to `QMenu`.

Verification:

1. `rg -n "m_providerMenu|QMenu|QAction|QActionGroup|syncSelectionActionState|actionForBusiness|m_action" src/plugins/core/minibarwindow.cpp src/plugins/core/minibarwindow.h src/plugins/core/minibarbusinessmenuhost.cpp src/plugins/core/minibarbusinessmenuhost.h`
2. `rg -n "provider menu|owned menu|menu action|business-backed menu|QMenu::popup|QMenu" .github/KnowledgeBase/minibar_popup_host_style_and_focus_status.md .github/KnowledgeBase/ui_independent_runtime_and_minibar_design.md .github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md`
3. `git diff --check -- src/plugins/core/minibarwindow.h src/plugins/core/minibarwindow.cpp src/plugins/core/minibarbusinessmenuhost.h src/plugins/core/minibarbusinessmenuhost.cpp .github/KnowledgeBase/minibar_popup_host_style_and_focus_status.md .github/KnowledgeBase/ui_independent_runtime_and_minibar_design.md`
