# Embedded Fonts And build.bat Redist Refresh

## Scope

- Stop packaging a visible `fonts/` directory for Windows and Linux package outputs.
- Compile the HarmonyOS Sans SC runtime fonts into first-party Qt resources and load them from code instead of `../fonts/`.
- Keep the change inside currently built first-party targets only.
- Make `scripts/build.bat` choose a fresh `vc_redist.x64.exe` at packaging time, with a fallback to the repository `bin/vc_redist.x64.exe` supplied by the user.
- Verification level: `static`; do not configure, build, run, or package.

## Observations

- `src/app/main.cpp` and `src/app/minibarhelper/main.cpp` both recurse `../fonts/` and call `QFontDatabase::addApplicationFont(...)` on `.ttf` files.
- `src/CMakeLists.txt` includes `app` and `maintenance`, but not `src/tools`; therefore the in-project runtime font change only needs to cover the main app and minibar helper.
- `configuration_files/CMakeLists.txt` currently copies `configuration_files/fonts/` into `${SGS_FONTS_DIR}` during configure.
- `scripts/build.bat`, `scripts/build.sh`, and `scripts/build_pi.sh` all package the generated `fonts` directory.
- Local source fonts currently live under `configuration_files/fonts/HarmonyOS_Sans_SC/` with three `.ttf` files.
- `scripts/build.bat` does not choose `vc_redist.x64.exe` explicitly; it packages whatever `.exe` already exists in `build/.../bin`.
- The current workspace `bin/vc_redist.x64.exe` reports product version `14.44.35211.0`.
- Local Visual Studio redist candidates under `C:/Program Files/Microsoft Visual Studio/2022/Enterprise/VC/Redist/MSVC/` currently report `14.42.34438.0`, which is older than the repository copy.
- Existing generated CMake install output in build trees hardcodes `MSVC_REDIST_DIR` to `14.42.34433`, which supports the hypothesis that incremental reuse can preserve an older packaged redistributable.

## Design

- Add a small `Utils::loadBundledFonts()` helper and a new Qt resource file inside `src/libs/utils/`.
- Reference the HarmonyOS source `.ttf` files from the resource file using neutral aliases so runtime resource paths do not expose the font family name.
- Replace the two in-project filesystem `loadFonts()` implementations with the shared `Utils::loadBundledFonts()` call.
- Stop copying `configuration_files/fonts/` into runtime output during configure.
- Remove `fonts` from the Windows and Linux packaging directory assembly steps.
- In `scripts/build.bat`, select the package redist explicitly in this order:
  1. `%VCToolsRedistDir%\vc_redist.x64.exe` when present.
  2. The newest matching file under `%VS_INSTALL_DIR%\VC\Redist\MSVC\*\vc_redist.x64.exe`.
  3. Repository fallback `bin\vc_redist.x64.exe`.
- Overwrite `RELEASE_DIR\bin\vc_redist.x64.exe` with that chosen file after the generic `.exe` copy loop so stale build-tree copies do not win.

## Success Criteria

1. The main app and minibar helper no longer read `../fonts/` at runtime.
2. HarmonyOS Sans SC font bytes are compiled into a first-party Qt resource that both built executables can access.
3. `configuration_files/CMakeLists.txt` no longer copies a runtime `fonts` directory.
4. `scripts/build.bat`, `scripts/build.sh`, and `scripts/build_pi.sh` no longer package a `fonts` directory.
5. Windows package assembly selects `vc_redist.x64.exe` explicitly from current environment or the repository fallback, instead of implicitly trusting an existing build output copy.
6. Static checks show no remaining in-project `../fonts/` dependency in built targets and no package script still stages `fonts`.

## Implementation Result

- Added `Utils::loadBundledFonts()` plus `src/libs/utils/bundledfonts.qrc`; the three HarmonyOS Sans SC `.ttf` files are now compiled into `Utils` with neutral resource aliases under `:/Runtime/Fonts/`.
- Replaced the main app and minibar helper filesystem font scans with the shared bundled-font loader.
- Removed the configure-time runtime `fonts/` copy path and removed `fonts` staging from Windows and Linux packaging scripts.
- Removed the top-level runtime-layout `SGS_FONTS_DIR` output because runtime fonts are no longer materialized as a standalone directory.
- Updated the Raspberry Pi runtime dependency helper and relevant KnowledgeBase/package-structure documents to describe bundled fonts instead of a packaged `fonts/` folder.
- Updated `scripts/build.bat` to skip any stale build-tree `vc_redist.x64.exe`, then explicitly copy a selected installer into the package `bin/`.

## Cause Analysis For Old vc_redist

- Observation: `scripts/build.bat` previously copied every `.exe` already present in `build/.../bin` into the package, without selecting `vc_redist.x64.exe` independently.
- Observation: the current workspace `bin/vc_redist.x64.exe` reports `14.44.35211.0`, while the local Visual Studio redist candidates under `C:/Program Files/Microsoft Visual Studio/2022/Enterprise/VC/Redist/MSVC/` report `14.42.34438.0`.
- Observation: existing generated build output contains cached `MSVC_REDIST_DIR` paths pinned to `14.42.34433`.
- Inference: because the Windows packaging script reuses existing build trees by default and trusted the already-present build output copy, an older redistributable could continue to ship even after toolchain state changed or a newer fallback file existed in the repository.
- Fix: the package now resolves the installer at packaging time from `%VCToolsRedistDir%`, then the newest `VS_INSTALL_DIR\VC\Redist\MSVC\*` match, then repository fallback `bin\vc_redist.x64.exe`.

## Static Verification Result

- Confirmed `src/app/` no longer contains in-project `../fonts/` runtime loading.
- Confirmed the only remaining `../fonts/` reference is `src/tools/arbeditor/main.cpp`, which is outside the current built `src/CMakeLists.txt` graph.
- Confirmed `configuration_files/CMakeLists.txt` and the packaging scripts no longer stage a runtime `fonts` directory.
- `get_errors` reported no issues in the touched CMake, C++, shell, or batch files checked during the task.

## Follow-up Build Fix

- Build observation: `bundledfonts.cpp.obj` failed to link because `Q_INIT_RESOURCE(bundledfonts)` was expanded inside `namespace Utils`, producing a reference to `Utils::qInitResources_bundledfonts()`.
- Local hypothesis: Qt's rcc-generated init symbol stays in global scope, so the namespaced reference is the entire linker failure.
- First attempted fix moved the call only into an anonymous namespace helper, which still produced `anonymous namespace::qInitResources_bundledfonts()` and therefore did not solve the linker mismatch.
- Correct fix: place the helper in true file-scope global context, outside both `namespace {}` and `namespace Utils {}`.

## Build Validation Result

- Incremental `Utils` target rebuild on `build/cmake-win-debug` re-ran CMake, regenerated `qrc_bundledfonts.cpp`, and no longer reported the original `Utils::qInitResources_bundledfonts()` unresolved symbol.
- Full Debug build then passed with the final file-scope helper fix; produced `Utils.dll`, `SGStudio.exe`, `SGStudioMiniBar.exe`, plugin DLLs, and `maintenance.exe` in the expected runtime locations.
- For the clean confirmation pass, `cmake --build build/cmake-win-debug --config Debug --target ALL_BUILD -- /m:1 /p:CL_MPCount=1 /p:UseMultiToolTask=false /p:VcpkgApplocalDeps=false /p:VcpkgXUseBuiltInApplocalDeps=false` completed successfully.
- Remaining output included an existing AutoMoc warning from `src/plugins/quickwaveform/quickwaveformpanel.cpp`, but it did not fail the build and is unrelated to the bundled-fonts fix.
