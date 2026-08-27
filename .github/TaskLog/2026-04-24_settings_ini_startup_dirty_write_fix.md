# 2026-04-24 Settings.ini Startup Dirty Write Fix

## Problem

- `configuration/Settings.ini` was rewritten on every startup even when no user-visible setting changed.
- The diff could misleadingly appear on the last `[Update]` line because `QSettings` rewrites the whole ini file and normalizes EOF formatting.

## Local Findings

- `src/app/main.cpp` unconditionally called `setPendingRestartRequested(..., false)` during startup, which always issued `setValue + sync`.
- `src/plugins/core/mainwindowsettingscontroller.cpp` calls `applyStartupTheme()` during startup, which routed into `onThemeChanged(...)` and unconditionally rewrote `APP/Theme`.
- `src/plugins/updater/plugin.cpp` only reads `[Update]` and does not write `Update/online`; the `[Update]` diff is a rewrite side effect, not a direct write to that key.
- Runtime reproduction after the first fix shows startup no longer changes `Settings.ini`, but a normal close still rewrites the file with byte-only EOF normalization.
- The remaining write comes from `src/plugins/core/mainwindow.cpp`: normal close goes through `APP/Reboot=True` in the second `closeEvent`, then the confirmation callback writes `APP/Reboot=False`, so the final visible content stays the same while `QSettings` still rewrites the ini.

## Change

- Only persist `APP/PendingRestart` when the requested value differs from the current stored value.
- Only persist `APP/Theme` when the desired theme differs from the stored theme.
- During close handling, only keep `APP/Reboot=True` for a real pending restart, and only write `APP/Reboot=False` when clearing a stale reboot flag.

## Validation Plan

- Rebuild Debug.
- Launch the app once without changing settings and confirm `configuration/Settings.ini` hash stays unchanged.
- Launch the app and close it normally, then confirm `configuration/Settings.ini` hash stays unchanged.
