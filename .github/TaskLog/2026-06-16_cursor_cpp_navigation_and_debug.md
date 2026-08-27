# Cursor C++ Navigation And Debug Setup

## Scope

- Fix Cursor-side C++ navigation for the SGStudio Qt/CMake project.
- Restore F5 / Ctrl+F5 launch support after migrating from VS Code.
- Keep the existing repo-root runtime layout and `Prepare ... Launch Runtime` tasks unchanged.
- Do not modify business source code or CMake target topology.

## Findings

- Cursor only had `anysphere.remote-ssh` installed, while VS Code had `ms-vscode.cpptools` and `ms-vscode.cmake-tools`.
- `.vscode/launch.json` uses `type: cppvsdbg`, which is provided by `ms-vscode.cpptools`; without that extension Cursor reports `Configured debug type 'cppvsdbg' is not supported`.
- The workspace had no `.vscode/c_cpp_properties.json`; C++ navigation depended on extension defaults, which is fragile for this Qt/CMake/MSVC project.
- Existing launch tasks already prepare repo-root `bin/`, `plugin/`, `configuration/`, and `fonts/` through `.vscode/prepare-launch-runtime.ps1`.

## Plan

1. Install the missing Cursor extensions needed by the existing VS Code workflow.
2. Add workspace C++ configuration that activates cpptools, uses CMake Tools as the primary configuration provider, and keeps a fallback browse/include path for source and Qt headers.
3. Update launch configuration to remove the stale VS Code workspaceStorage natvis path and add explicit release run/debug entries that continue to use the existing prepare tasks.
4. Validate edited JSON/JSONC files statically and document any required Cursor reload.

## Verification Level

- `static`
- Extension installation verified through Cursor CLI.
- No project build/run is required unless the user asks for runtime verification.

## Result

- Installed `ms-vscode.cmake-tools` into Cursor from the extension source.
- Installed `ms-vscode.cpptools` into Cursor from the Microsoft Marketplace VSIX because Cursor extension search did not find it by ID.
- Added `.vscode/c_cpp_properties.json` for CMake-provider based IntelliSense plus fallback source/Qt browse paths.
- Updated `.vscode/settings.json` to use the real MSVC x64 compiler path, CMake Tools configuration provider, MSVC IntelliSense mode, and C++17.
- Removed the stale VS Code workspaceStorage `visualizerFile` entries from `.vscode/launch.json`.
- Added `.vscode/extensions.json` recommendations for future Cursor / VS Code workspace opens.
- Static JSON/JSONC sanity checks passed for the edited configuration files.

## Debug Follow-Up

- `Ctrl+F5` Release launch was confirmed by the user to work in Cursor.
- Debug runtime outputs were confirmed to be Debug-mode: `bin/.sgstudio-launch-config` contains `Debug`, `bin/SGStudio.pdb` exists, and Debug Qt runtime DLLs such as `Qt5Cored.dll` / `qwindowsd.dll` are present.
- Plugin PDBs such as `plugin/Core.pdb` and `plugin/HTRA.pdb` are present, so plugin source breakpoints should be resolvable by `cppvsdbg`.
- Updated the `Debug SGStudio` launch entry to:
  - search symbols in `bin`, `plugin`, and `build/cmake-win-debug`;
  - allow source mismatch tolerance with `requireExactSource=false` for migrated workspaces.

## Cursor Debug Adapter Fix

- The user confirmed the same workspace debugs correctly in VS Code, while Cursor F5 only starts `SGStudio.exe` in the integrated terminal and does not attach a debugger.
- Compared the Cursor and VS Code `ms-vscode.cpptools` installations.
- Found the first Cursor install was the wrong target-platform package: it lacked `debugAdapters/bin/OpenDebugAD7.exe` and `debugAdapters/bin/WindowsDebugLauncher.exe`, which are required for Windows `cppvsdbg`.
- Reinstalled `ms-vscode.cpptools` in Cursor from the `win32-x64` Marketplace VSIX.
- Verified the Cursor extension directory now contains:
  - `OpenDebugAD7.exe`
  - `WindowsDebugLauncher.exe`
  - `OpenDebugAD7.dll`
- Restored `Debug SGStudio.stopAtEntry=false` per user request.

## Cursor `cppvsdbg` License Boundary

- Cursor then reported: `C/C++ Debugging is supported only in Microsoft versions of VS Code`.
- This is a Microsoft C/C++ extension license/product check for `cppvsdbg`; it is expected to work in Microsoft VS Code and fail in Cursor even when the adapter files are present.
- Installed `vadimcn.vscode-lldb` from the official CodeLLDB GitHub release package `codelldb-win32-x64.vsix`.
- Changed the default `Debug SGStudio` launch configuration to `type: lldb` for Cursor.
- Kept `Debug SGStudio (VS Code cppvsdbg)` as a separate VS Code-only configuration.
- Added `vadimcn.vscode-lldb` to `.vscode/extensions.json`.
