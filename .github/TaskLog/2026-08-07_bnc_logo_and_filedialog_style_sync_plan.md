# BNC logo and file dialog style sync plan

## Scope

- Revert the previous Windows manifest-related CMake changes and helper resource files, per user direction to keep the build logic simple after the machine restart.
- Make the BNC titlebar app logo switch correctly in light theme using the newly added `BNC_Light.png`.
- Sync the verified `standard_cn` aarch64 file dialog header styling fallback into the BNC themes so the custom open/save dialogs render the header background correctly.
- Verification level: `static` plus a focused Windows build check after the manifest revert.

## Observations

- The previous manifest workaround lives only in `src/CMakeLists.txt`, `src/app/CMakeLists.txt`, `src/app/minibarhelper/CMakeLists.txt`, `src/maintenance/CMakeLists.txt`, plus the added files `src/windows/asInvoker.manifest` and `src/windows/embedded_asinvoker_manifest.rc`.
- `ThemeManager` resolves the titlebar logo through `:/App/Image/logo` for dark theme and `:/App/Image/logo_light` for light theme.
- `src/app/res_bnc/app.qrc` currently maps both `logo` and `logo_light` to `BNC-170-30.png`, so light theme cannot show a distinct asset yet.
- `configuration_files/standard_cn/theme.css` and `theme_light.css` style the file dialog header with both `Controls--FileWidget` selectors and `QWidget#FileWidgetForm` / `QWidget#DirWidgetForm` fallbacks, including `QTableCornerButton::section`.
- `configuration_files/BNC_en/theme.css` and `theme_light.css` still keep the older, narrower header rules and are missing those fallback selectors.

## Success criteria

1. The manifest helper files and related CMake hook calls are fully removed.
2. BNC light theme resolves the titlebar logo through `BNC_Light.png` without changing the existing runtime code path.
3. BNC deep/light theme CSS both include the same file dialog header fallback selectors and visual values as the verified `standard_cn` implementation.
4. The touched files are free of diagnostics relevant to this task, and a focused Windows build check after the revert no longer depends on the removed manifest workaround.