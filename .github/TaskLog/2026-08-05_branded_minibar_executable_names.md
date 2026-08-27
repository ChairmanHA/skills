# Branded MiniBar executable names

## Scope

- Keep the existing internal CMake target `SGStudioMiniBar` stable.
- Select the built helper executable/application name by packet:
  - standard and other non-neutral/non-BNC packets: `SGStudioMiniBar`;
  - BNC: `VectorCoreMiniBar`;
  - neutral: `VSGMiniBar`.
- Ensure the main process resolves and launches the same branded filename on
  Windows and Linux/aarch64.
- Update the helper's Qt application identity and direct-launch information title
  to the selected branded name.
- Verification level: `static`; do not configure, compile, package, or run.

## Observations

- At task start, `src/app/minibarhelper/CMakeLists.txt` kept both the CMake target and
  `OUTPUT_NAME` fixed at `SGStudioMiniBar`.
- `MinibarHelperController::helperExecutablePath()` delegates the filename to
  `MinibarHelperProtocol::helperExecutableName()` and joins it to the main
  application's executable directory.
- At task start, `helperExecutableName()` hardcoded `SGStudioMiniBar` plus the Windows
  `.exe` suffix, so build output and launch lookup must be changed together.
- The helper's `QApplication::applicationName()` and direct-launch message title
  are also hardcoded.
- Packaging scripts copy the runtime layout rather than naming the helper
  explicitly; the internal CMake target name can remain unchanged.

## Design

- Define one root CMake variable, `SGS_MINIBAR_EXECUTABLE_FILE_NAME`, from
  `packet`, next to the existing main executable identity.
- Export that value as a first-party compile definition and use it in the shared
  `MinibarIpc` helper-name function.
- Add `helperApplicationName()` as the extension-free canonical runtime identity;
  derive `helperExecutableName()` from it by adding `.exe` only on Windows.
- Use the same CMake variable as the MiniBar target's `OUTPUT_NAME`.
- Keep protocol option names, IPC server naming, CMake target identity, and MiniBar
  business behavior unchanged.

## Success criteria

1. CMake maps standard/BNC/neutral to
   `SGStudioMiniBar`/`VectorCoreMiniBar`/`VSGMiniBar` respectively.
2. The helper target output and the main process launch lookup share the same
   canonical value.
3. Windows lookup adds `.exe`; Linux/aarch64 lookup uses the extension-free name.
4. The helper Qt application name and direct-launch message title match the
   branded helper name.
5. No hardcoded helper executable lookup remains outside the centralized helper
   identity implementation.
6. Static diff and CMake-path checks pass; no build or runtime verification is
   performed.

## Implementation result

- Added `SGS_MINIBAR_EXECUTABLE_FILE_NAME` at the root CMake identity boundary
  with the standard/BNC/neutral mapping defined above.
- Kept the logical target `SGStudioMiniBar`, while its `OUTPUT_NAME` now consumes
  the branded CMake value.
- Exported the same value to first-party code. `MinibarIpc` now exposes the
  extension-free `helperApplicationName()` and derives the platform filename in
  `helperExecutableName()`.
- The existing `MinibarHelperController::helperExecutablePath()` continues to
  resolve the helper beside the main executable, but now receives the branded
  filename and therefore starts the matching built artifact.
- Updated the helper's Qt application name and direct-launch information title to
  the branded helper identity. IPC option/server names and behavior are unchanged.

## Static verification result

- Confirmed exact CMake mappings for `SGStudioMiniBar`, `VectorCoreMiniBar`, and
  `VSGMiniBar`.
- Confirmed the build `OUTPUT_NAME`, shared runtime identity, and controller launch
  lookup form one value chain; Windows appends `.exe`, Linux/aarch64 does not.
- Source search finds branded MiniBar literals only in the three root mappings and
  the intentionally stable internal CMake target.
- `git diff --check` passed outside the inherited BNC configuration copies.
- No configure, compilation, packaging, or runtime launch was performed.
