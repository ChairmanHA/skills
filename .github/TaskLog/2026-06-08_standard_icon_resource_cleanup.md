# Standard Icon Resource Cleanup

## Goal

- Confirm which resource files control exe icon, taskbar icon, and titlebar logo for standard/neutral/ru variants.
- Update the standard Windows icon to the new SGSTUDIO-LOGO asset.
- Remove dead root-level `src/app/app.rc`, `src/app/app.qrc`, and `src/app/app.ico` if current CMake flow no longer references them.

## Findings

- `src/app/CMakeLists.txt` selects `res_standard/app.rc` and `res_standard/app.qrc` for `packet=standard`.
- `src/app/main.cpp` sets the runtime window/taskbar icon from `:/App/Image/app_icon`.
- `src/libs/controls/thememanager.cpp` maps `AppLogo` to `:/App/Image/logo` and `:/App/Image/logo_light`; these are separate from the Windows `.ico`.
- Current repo-wide non-generated references do not point to root-level `src/app/app.rc`, `src/app/app.qrc`, or `src/app/app.ico`.

## Plan

- Generate a multi-size `.ico` from `SGSTUDIO-LOGO` PNG assets.
- Replace `src/app/res_standard/app.ico`.
- Delete dead root-level `src/app/app.rc`, `src/app/app.qrc`, and `src/app/app.ico`.
- Run a focused Debug build for `SGStudio` to validate resource compilation.