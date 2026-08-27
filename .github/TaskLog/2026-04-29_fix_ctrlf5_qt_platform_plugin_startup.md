# Ctrl+F5 Qt Platform Plugin Startup Fix

## Problem

- VS Code `Launch SGStudio Release` via Ctrl+F5 shows `This application failed to start because no Qt platform plugin could be initialized`.
- The repo runtime already contains `bin/plugins/platforms/qwindows.dll`, so this is not a simple "plugin not found" case.
- Current `.vscode/launch.json` only prepends repo `bin/` and `plugin/` to `PATH`, but does not explicitly provide the Qt installation `bin` directory or an explicit platform plugin directory.

## Local Hypothesis

- Ctrl+F5 is resolving the Qt platform plugin from an ambiguous runtime search path.
- The `qwindows` plugin is discovered, but its dependent Qt runtime DLLs or its selected platform plugin directory are not guaranteed to match the current Qt 5.15.9 installation, so plugin initialization fails before the app UI starts.

## Minimal Fix

- Keep program path and cwd unchanged.
- Update both launch configurations in `.vscode/launch.json` to:
  - prepend `C:\Qt\5.15.9\msvc2022_64\bin` to `PATH`
  - set `QT_PLUGIN_PATH` to prefer repo `bin/plugins` and keep Qt install plugins as fallback
  - set `QT_QPA_PLATFORM_PLUGIN_PATH` explicitly to `C:\Qt\5.15.9\msvc2022_64\plugins\platforms`

## Validation

- Re-run the same startup path after editing: use the launch-equivalent environment and confirm the platform plugin initialization error no longer appears.

## Findings

- Root `bin/` had a mixed Qt runtime:
  - `bin/Qt5Core.dll`, `Qt5Gui.dll`, `Qt5Widgets.dll` were `5.15.13.0`
  - configured Qt installation `C:/Qt/5.15.9/msvc2022_64/bin` provides `5.15.9.0`
- After pointing `QT_QPA_PLATFORM_PLUGIN_PATH` to the Qt install, Qt could enumerate platform plugins from `C:/Qt/5.15.9/msvc2022_64/plugins/platforms`, but initialization still failed.
- This confirmed the failure was not plugin discovery but Qt runtime mixing in repo-root `bin/`.

## Implemented Fix

- Kept the launch configuration explicit about the Qt installation path.
- Extended `.vscode/prepare-launch-runtime.ps1` so each pre-launch run now:
  - stops existing SGStudio processes
  - clears repo-root `plugin/`
  - builds the requested configuration
  - copies fresh business plugins from `build/.../plugin-runtime` into repo-root `plugin/`
  - removes stale Qt DLLs and stray Qt plugin DLLs from repo-root `bin/`
  - runs `windeployqt` from the configured Qt 5.15.9 installation against the active SGStudio executable, self-built Qt-linked bin modules, and current business plugin DLLs
- The deployment target filter was tightened to avoid feeding third-party DLLs such as brotli/libcurl into `windeployqt`.
- The bin-module target set is config-aware so Release prep no longer scans stale Debug DLLs, and vice versa.

## Verification Result

- `Prepare Release Launch Runtime` now completes successfully.
- Repo-root `bin/Qt5Core.dll`, `Qt5Gui.dll`, `Qt5Widgets.dll`, `Qt5Xml.dll`, and `Qt5Svg.dll` are all `5.15.9.0` after the prepare step.
- Launching `bin/SGStudio.exe` under the same environment as Ctrl+F5 leaves the process running past the previous platform-plugin crash point.