# 2026-06-24 Digital Export Progress Dialog QSS

## Scope

- Move the Digital Save IQ full-export `QProgressDialog` styling out of `digitalmodulation.cpp` and into theme QSS.
- Use a narrowly scoped selector so the change only affects this progress dialog.
- Keep behavior unchanged apart from the style ownership move.

## Verification Level

- `static`

## Observations

- `src/plugins/analog/digitalmodulation.cpp` is included by `src/plugins/analog/CMakeLists.txt`.
- `DigitalModulation::ensureExportProgressDialog()` dynamically creates a standard `QProgressDialog`.
- The current implementation calls `applyExportProgressDialogStyle(dialog)` and also reconnects `ThemeManager::themeChanged` to reapply a widget-local stylesheet.
- The theme source directories under `configuration_files/` already contain a working precedent for dynamic progress-dialog styling:
  - `ArbSequenceEditor QProgressDialog`
  - `ArbSequenceEditor QProgressDialog QLabel`
  - `ArbSequenceEditor QProgressDialog QProgressBar`
  - `ArbSequenceEditor QProgressDialog QProgressBar::chunk`
- `MainWindowSettingsController::onThemeChanged()` reloads the application stylesheet via `qApp->setStyleSheet(styleSheet)` and then refreshes visible widgets, so theme changes already flow through the global QSS path.

## Inferences

- A dynamically created `QProgressDialog` can be styled from theme QSS as long as it matches a stable selector and does not keep a conflicting widget-local stylesheet.
- Using a dedicated object name such as `QProgressDialog#digitalSaveIqProgressDialog` is safer than a bare `QProgressDialog` selector because it avoids changing unrelated progress dialogs.
- Once the dialog no longer sets a local stylesheet, the theme-change reconnect in `digitalmodulation.cpp` becomes redundant.

## Success Criteria

1. `digitalmodulation.cpp` no longer builds or applies a widget-local stylesheet string for the export progress dialog.
2. The export progress dialog gets a stable object name before it is shown.
3. Every shipped theme source pair under `configuration_files/` adds matching dark/light rules for:
   - `QProgressDialog#digitalSaveIqProgressDialog`
   - `QProgressDialog#digitalSaveIqProgressDialog QLabel`
   - `QProgressDialog#digitalSaveIqProgressDialog QProgressBar`
   - `QProgressDialog#digitalSaveIqProgressDialog QProgressBar::chunk`
4. The export progress dialog still has the same visual values as before:
   - `qproperty-windowOpacity: 0.98`
   - dark background `#1D2228`
   - light background `#ffffff`
   - dark border `#797E87`
   - light border `#868178`
   - chunk color `#00ce00`
   - chunk width `10px`
   - chunk margin `0.5px`

## Non-Goals

- No runtime behavior changes to the export workflow itself.
- No broader cleanup of other hardcoded local styles outside this dialog.
