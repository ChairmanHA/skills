# Updater Preserve Cal Licenses And Admin Fix Review

## Scope

- Understand the active updater / maintenance copy flow from the current KnowledgeBase docs and compiled source path.
- Fix the reported loss of `bin/CalFile/*_mod.lic` authorization files after update without preventing `bin/CalFile/132.ini` from updating.
- Review commit `e75bb991ad89f23d06f168c6b608b76153c8e9bb` for the administrator-run update issue and record remaining gaps.
- Inspect the Raspberry Pi desktop-entry launch path and explain whether it can affect updater copy behavior.
- Refresh updater KnowledgeBase documentation with the concrete copy-preservation rule and review result.

## Verification Level

Static.

No build or runtime update rehearsal was requested. Verification will be limited to source inspection, CMake inclusion checks, and targeted diff review.

## Initial Findings

- The active copy phase is `src/maintenance/copythread.cpp`, included by `src/maintenance/CMakeLists.txt`.
- The current workspace has `bin/CalFile`, not `bin/Cal`.
- `bin/CalFile/132.ini` is calibration/spec configuration and should be updated by the package.
- `bin/CalFile/132_..._mod.lic` is a user/site authorization file and must be restored after update.
- The admin-run fix commit touches maintenance path handling and updater package extraction/path normalization; it needs review for coverage and residual edge cases.

## Plan

1. Confirm current source inclusion through CMake.
2. Inspect `CopyThread` and the maintenance argument/path handoff code after commit `e75bb991`.
3. Preserve old-installation `*.lic` files under `bin/` without restoring the whole `bin/CalFile` directory.
4. Update KnowledgeBase docs with the current preservation behavior and admin-fix assessment.
5. Re-run targeted static searches/diffs.

## Completed

- Confirmed `src/maintenance/copythread.cpp` is included by `src/maintenance/CMakeLists.txt`.
- Updated `CopyThread` so it no longer depends on whole-directory `bin/CalFile` restoration for licenses.
- Added recursive `bin/**/*.lic` restoration from the old installation into the new installation.
- Kept `bin/CalFile/132.ini` package-owned so the updated package version survives.
- Changed the copy target to the original `destPath` so desktop entries and external absolute paths keep pointing at the same install root after update.
- Renamed the internal merge helper from directory-specific wording to entry-specific wording because the current implementation supports both directories and ordinary files.
- Updated updater KnowledgeBase docs with the `bin/CalFile/132.ini` vs `bin/CalFile/*.lic` ownership split and the `e75bb991` review.
- Removed the temporary `setCurrent(applicationDirPath())` startup-path mitigation after field testing confirmed authorization works when the `.lic` is physically present in `/software/SGStudio/bin/CalFile` even when launched through the desktop-entry wrapper script.

## Raspberry Pi Desktop Entry Findings

- `/home/htra/Desktop/SGStudio.desktop` launches `sh "/software/app.sh"`.
- `/home/htra/.config/autostart/SGStudio.desktop` launches `sh "/software/start.sh"`.
- Both scripts run the real binary by absolute path: `/software/SGStudio/bin/SGStudio`.
- Neither script changes directory to `/software/SGStudio/bin` before launching the binary.
- `/software/SGStudio/bin/CalFile` currently contains `132.ini` but no `.lic` files.
- `/software/SGStudio/bin/debug.log` shows a license check failure for model `132`.

Conclusion: this desktop-entry launch style is unlikely to make updater copy to the wrong install root, because the final executable path is absolute and `applicationDirPath()` should still resolve to `/software/SGStudio/bin`. Follow-up field testing confirmed the missing-`cd` wrapper is not the cause of the license loss: when the `.lic` file is physically present in `/software/SGStudio/bin/CalFile`, authorization succeeds through this launch path. The remaining issue is the update copy phase physically losing or failing to restore `.lic`.

## Review Result For `e75bb991`

The commit is a useful stopgap for administrator-run updates:

- It avoids repeating `ShellExecuteExW(..., "runas")` when the main process is already elevated.
- It parses maintenance arguments from the UTF-16 Windows command line.
- It improves Unicode path handling for download and zip extraction.
- It tries to restart SGStudio through Explorer Shell COM so the restarted app can return to the normal user context.

It is not complete:

- Explorer COM failure falls back to `QProcess::startDetached()`, which can still restart elevated.
- Restart failure is not surfaced because the return value is ignored.
- `CopyThread` still kills and restarts `explorer.exe`.
- Package validation still does not require a valid maintenance path and still matches updater by broad `Updater_*`.
- On Linux/aarch64, deployment must still verify desktop entries resolve to the real install root; a wrapper or symlink can still cause `m_currentFolderPath` to point at the wrong directory.

## Verification

- `git diff --check` passed with only a CRLF normalization warning for `src/maintenance/copythread.cpp`.
- Targeted `rg` confirmed the old `mergeSelectedBackupDirs` name and stale `Settings.ini` documentation text are gone from the updated files.
- Targeted source inspection confirmed `bin/CalFile` is no longer a whole-directory keep entry and `bin/**/*.lic` is restored separately.
- Remote read-only inspection confirmed the Raspberry Pi desktop entry launches wrapper scripts, those scripts use an absolute SGStudio binary path but do not `cd` into `bin/`, and `/software/SGStudio/bin/CalFile` had `132.ini` but no `.lic` files at inspection time.
- No build or runtime update rehearsal was run.
