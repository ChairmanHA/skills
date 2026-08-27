# 250 Cross-Build To 179 Runtime Validation

Date: 2026-07-17

## Scope

- Inspect the remote packaging flow under `vsg2.0/sgstudio/scripts/build.sh` on `192.168.3.250`.
- Build an aarch64 package from `192.168.3.250` without broadly mutating that host environment.
- Transfer the produced package to `192.168.3.179`, extract it, launch it, and verify that the SGStudio UI appears.
- If runtime launch fails on `192.168.3.179`, identify the concrete blocker, apply the minimum necessary server-side or packaging-side fix, then rebuild and re-verify until the package produced on `192.168.3.250` can show UI on `192.168.3.179`.

## Evidence

- The current workspace `scripts/build.sh` already tries to constrain the build shell with `export PATH="/usr/bin:${QT_BIN_DIR}:${PATH}"` and clears `QT_PLUGIN_PATH` / `QT_QPA_PLATFORM_PLUGIN_PATH`.
- Repository memory records prior Linux packaging failures caused by packaged dependency shape rather than missing repo code logic, including SONAME/symlink issues and missing launcher/runtime assets.
- Historical TaskLogs show Raspberry Pi packaged SGStudio can fail to launch from SSH shells that lack Wayland/X11 environment variables, even when the package contents themselves are otherwise valid.

## Assumptions

- `192.168.3.250` is the intended build host and has a checkout at `vsg2.0/sgstudio`.
- `192.168.3.179` is the intended runtime host and can display the UI once the package and launch environment are correct.
- The preferred fix order is: packaging script or package content, then host-local launch/runtime setup, and only finally host environment mutation if a narrower fix is not sufficient.

## Initial Hypothesis

- The most likely failure is not the `export PATH=/usr/bin:$PATH` requirement on `192.168.3.250` itself, but a packaged runtime gap on `192.168.3.179` such as missing staged launcher/assets, lost SONAME-compatible library entries, or launch from an SSH shell with no desktop display environment.

## Cheap Discriminating Checks

1. Compare the remote `scripts/build.sh` on `192.168.3.250` with the current workspace version to see whether the remote script already lacks known packaging fixes.
2. Run the produced package once on `192.168.3.179` and capture the first concrete failure line from the loader or Qt platform initialization.

## Success Criteria

- `scripts/build.sh` on `192.168.3.250` builds an aarch64 package successfully.
- The package can be transferred to `192.168.3.179`, extracted, and launched there without manual ad hoc file surgery after extraction.
- The UI is verified to appear on `192.168.3.179` via screenshot or equivalent visual proof.
- Any repository-side script fix needed for this result is captured in the workspace, not left only as an opaque remote edit.

## Verification Level

- Remote build
- Remote run
- Visual UI proof