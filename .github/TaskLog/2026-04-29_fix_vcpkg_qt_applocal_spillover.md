# Fix vcpkg Qt Applocal Spillover

## Problem

- The previous Ctrl+F5 failure was fixed by re-preparing repo-root runtime before launch, but that only masks the deeper build-time pollution source.
- Current source tree explicitly disables `VcpkgApplocalDeps` only for plugin targets.
- Generated Release project files confirm plugin vcxproj files contain:
  - `VcpkgApplocalDeps=false`
  - `VcpkgXUseBuiltInApplocalDeps=false`
- Generated vcxproj files for `SGStudio`, `maintenance`, `Business`, `Controls`, `ExtensionSystem`, `Utils`, and `QXlsx` do not contain those properties.

## Local Hypothesis

- vcpkg / Visual Studio applocal is still active for Qt-linked targets that output to repo-root `bin/`.
- That allows vcpkg's Qt 5.15.13 runtime to be copied into repo-root `bin/`, where it can mix with the intended Qt 5.15.9 runtime and break startup.
- Plugin pollution was already contained earlier, but app/bin pollution remains possible until applocal is disabled for the rest of the Qt-linked repo-root runtime targets.

## Minimal Change Plan

- Add one shared CMake helper that disables Visual Studio vcpkg applocal on selected targets.
- Apply it to all Qt-linked repo-root runtime targets that can write into `bin/`:
  - `SGStudio`
  - `maintenance`
  - `Business`
  - `Controls`
  - `ExtensionSystem`
  - `Utils`
  - `QXlsx`

## Validation Plan

- Run plain `CMake Build Release` without the launch preparation script.
- Check whether repo-root `bin/Qt5Core.dll`, `Qt5Gui.dll`, and `Qt5Widgets.dll` stay aligned with Qt 5.15.9 instead of being overwritten by vcpkg 5.15.13.
- If plain build stays clean, reassess which VS Code launch-side mitigations are still necessary and roll back only the redundant ones.

## Implemented Fix

- Added shared helper `sgstudio_disable_vcpkg_applocal(target)` in repo-root `CMakeLists.txt`.
- Applied that helper to all confirmed Qt-linked repo-root runtime targets that can write into `bin/`:
  - `SGStudio`
  - `maintenance`
  - `Business`
  - `Controls`
  - `ExtensionSystem`
  - `Utils`
  - `QXlsx`
- Unified plugin targets onto the same helper instead of keeping an isolated duplicate implementation.

## Verification Result

- Plain `CMake Build Release` succeeded after reconfigure.
- Generated Release vcxproj files for `SGStudio`, `maintenance`, `Business`, `Controls`, `ExtensionSystem`, `Utils`, `QXlsx`, and all plugin targets now contain:
  - `<VcpkgApplocalDeps>false</VcpkgApplocalDeps>`
  - `<VcpkgXUseBuiltInApplocalDeps>false</VcpkgXUseBuiltInApplocalDeps>`
- After plain build, repo-root runtime stayed on Qt 5.15.9:
  - `Qt5Core.dll` = `5.15.9.0`
  - `Qt5Gui.dll` = `5.15.9.0`
  - `Qt5Widgets.dll` = `5.15.9.0`
  - `Qt5Xml.dll` = `5.15.9.0`
  - `Qt5Svg.dll` = `5.15.9.0`
- Launching `bin/SGStudio.exe` with only repo-root `bin;plugin` on `PATH` succeeded; no explicit `QT_PLUGIN_PATH` or `QT_QPA_PLATFORM_PLUGIN_PATH` was required after `Prepare Release Launch Runtime`.

## Rollback Decision

- Safe to roll back:
  - the temporary `.vscode/launch.json` runtime environment overrides that injected `C:/Qt/5.15.9/msvc2022_64/bin` and explicit Qt plugin paths
- Not safe to roll back yet:
  - plugin staging + plugin applocal disable, because that isolates repo-root `plugin/` from MSBuild applocal spillover and prevents the earlier `MSB3541` / mixed-config plugin pollution path
  - `.vscode/prepare-launch-runtime.ps1` local Qt runtime preparation, because plain build is now clean but still does not reconstruct a fresh repo-root Qt runtime from an empty or stale `bin/plugins` layout

## Follow-up Refinement

- Additional validation showed your concern was valid for Release: after applocal spillover was fixed, `Prepare Release Launch Runtime` no longer needed to redeploy Qt on every launch.
- However, a full rollback of `.vscode/prepare-launch-runtime.ps1` was still unsafe because plain Debug build did not provide:
  - `Qt5Cored.dll`
  - `Qt5Guid.dll`
  - `Qt5Widgetsd.dll`
  - `platforms/qwindowsd.dll`
- The script was therefore narrowed instead of fully reverted:
  - for the selected config, it now probes a small required Qt runtime set in repo-root `bin/`
  - if the files are present and match the configured Qt installation version, it skips `windeployqt`
  - if files are missing or mismatched, it refreshes the Qt runtime as before

## Follow-up Verification

- `Prepare Release Launch Runtime` now prints `Qt runtime already matches Release; skipping redeploy`.
- After that skip path, `bin/SGStudio.exe` still launches successfully with only repo-root `bin;plugin` on `PATH`.
- `Prepare Debug Launch Runtime` still refreshes missing Debug Qt runtime files and restores:
  - `Qt5Cored.dll`
  - `Qt5Guid.dll`
  - `Qt5Widgetsd.dll`
  - `platforms/qwindowsd.dll`
- `SGStudiod.exe` still exits with `-1073741819` in the current environment even after Debug runtime refresh; that behavior was not treated as evidence against the conditional deploy change because the validated release path is intact and the debug failure requires a separate runtime/debugging investigation.