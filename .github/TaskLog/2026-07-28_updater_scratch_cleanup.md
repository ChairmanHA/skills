# Updater Scratch Cleanup

Date: 2026-07-28

## Scope

- Make updater download/extraction scratch data disposable because the current transport does not support resume.
- Retain a completed, valid package when the user chooses `No`, closes the prompt, or closes the update dialog so the same application session can reuse it.
- Retain a completed package across a real update handoff and let the restarted application adopt it once, avoiding an immediate duplicate download.
- Clean scratch data after failed download/decompression/parse, an incomplete download at application exit, a strictly older remote version, and abandoned-process recovery.
- Keep the current full-package download, version comparison, maintenance handoff, and install/copy behavior unchanged.

## Verification Level

Static.

No build or runtime update rehearsal was requested.

## Observations

- At task start, `PacketSpec` downloaded and extracted into
  `QStandardPaths::TempLocation/SGStudio/updater/<uuid>`.
- At task start, `PacketSpec` had no destructor or explicit scratch cleanup.
- At task start, `https_download_v2()` removed the partial archive only when libcurl returned an error; the UUID directory and extracted files were not owned by a cleanup object.
- At task start, `Plugin::extensionsInitialized()` cleaned `AppLocalDataLocation`, so it did not normally reach the current temp scratch root.
- A successful online/local-file update launches `maintenance` from inside the extracted package. The main application therefore must not delete that package during shutdown after maintenance has accepted the handoff.
- Multiple SGStudio processes can exist, so startup cleanup must not delete a scratch directory still used by another process.

## Design

1. Give every generated `PacketSpec` scratch directory a `QLockFile`.
2. Add explicit `PacketSpec` scratch ownership:
   - delete immediately on download/decompression/parse failure;
   - delete on explicit discard;
   - on application exit, cancel and delete an incomplete download;
   - retain a valid completed package for the owning application session;
   - preserve across process shutdown only after maintenance confirms handoff.
3. In the startup version decision:
   - discard when the remote version is invalid or strictly older;
   - retain an equal version because its package may still be used to update firmware;
   - retain a newer version when the user chooses `No` or closes the prompt.
4. Closing the update dialog does not discard or cancel its online package. A running download may continue in the background; normal application shutdown cancels and removes it if still incomplete.
5. During a real update:
   - mark the selected `PacketSpec` as handed off only after the maintenance ready marker arrives;
   - write the handoff-cache marker before the main application releases its `PacketSpec` lock;
   - leave `maintenance/ProgressDialog` unchanged because it already owns updater execution/output, copy completion, and application restart, while cache protection belongs to the updater workspace owner.
6. Write a handoff-cache marker for a successfully handed-off online package. On the restarted application's first startup check:
   - adopt the extracted package without downloading it again;
   - remove the marker after successful adoption so the cache is scoped to that application session;
   - discard it if revalidation fails.
7. Sweep the actual updater temp roots at startup and shortly after startup:
   - acquire each workspace lock before deletion;
   - skip workspaces locked by a live `PacketSpec`;
   - skip a marked handoff cache until startup adoption has had a chance to consume it;
   - remove stale/unlocked workspaces left by crashes or forced process termination.
8. Replace the broad legacy `AppLocalDataLocation` age-based cleanup with UUID-only cleanup for packages created by older builds.

## Success Criteria

- A failed download leaves no archive or UUID work directory after the worker exits normally.
- Decompression, package-layout, version-file, updater-discovery, or maintenance-discovery failure removes the whole generated workspace.
- A strictly older remote version is discarded immediately; an equal valid version is retained for firmware update use.
- Choosing `No`, closing the version prompt, or closing the update dialog retains the valid package.
- Closing SGStudio during an active download requests libcurl cancellation, waits for the worker, and removes its workspace.
- A committed update keeps the package through maintenance and lets the restarted SGStudio adopt it once without another download.
- The adopted package remains reusable for that application session and is removed on an ordinary later application exit unless handed off again.
- Startup recovery removes unlocked crash residue from the current temp scratch root and does not remove a workspace holding a live lock.
- No resume/retry/download-format behavior is added.

## Static Verification

- Confirm every failure return after scratch creation goes through cleanup.
- Confirm the handoff-preserve flag is set only after the maintenance ready marker is observed.
- Confirm the handoff-cache marker protects the workspace before the main process releases ownership.
- Confirm `No`, prompt dismissal, and update-dialog dismissal have no discard path.
- Confirm only `local > remote`, not equality, discards a valid remote package.
- Confirm a handoff-marked online package can be revalidated and adopted without downloading.
- Confirm startup cleanup targets the same roots selected by `PacketSpec`.
- Confirm cleanup validates that the deletion target is a direct child of an updater scratch root.
- Run targeted searches and `git diff --check`.

## Outcome

- Added lock-owned UUID scratch workspaces and whole-workspace cleanup for download, decompression, package parsing, updater discovery, and maintenance discovery failures.
- Added libcurl cancellation and asynchronous shutdown waiting so an incomplete download is removed when SGStudio exits.
- Removed all cleanup callbacks from `No`, prompt dismissal, update-dialog dismissal, and failed maintenance launch. Closing only the update window leaves a running download in the background and retains a valid result.
- Changed the version decision so only invalid metadata or `local > remote` discards the package; equality is retained for firmware update use.
- Added a handoff-cache marker after maintenance confirms takeover. Startup now revalidates and adopts that package without downloading, even when automatic online checking is disabled.
- Kept the adopted package for the restarted application's session, allowing the update window to be opened repeatedly without another download.
- Added startup and periodic lock-aware cleanup for abandoned UUID workspaces while excluding pending handoff caches.
- Replaced the broad legacy `AppLocalDataLocation` age-based deletion with direct-child UUID cleanup.
- Left `maintenance/ProgressDialog` unchanged; online handoff retention is implemented entirely by `PacketSpec` ownership and the handoff-cache marker.
- Updated both active updater KnowledgeBase documents to match the corrected retention and cleanup lifecycle.

## Verification Result

- `git diff --check` passed.
- Static invariant checks confirmed there is no remaining update-dialog close cleanup callback and no `order >= 0` equal-version discard.
- Targeted inspection confirmed all `PacketSpec::run()` failures after workspace creation clean the workspace.
- Targeted inspection confirmed `preserveScratchDataForHandoff()` is called only after the main process observes the maintenance ready marker.
- Targeted inspection confirmed a marked handoff package is skipped by startup cleanup, revalidated, locked, adopted, and then exposed as `m_startupRemoteSpec`.
- `src/maintenance/progressdialog.cpp` and `.h` have no staged or unstaged diff from `HEAD`.
- Active CMake inclusion was reconfirmed for all modified C++ sources.
- No build or runtime update rehearsal was run, matching the static verification level.

## Follow-up Scope (2026-07-28)

- Remove the delayed and once-per-minute abandoned-workspace sweeps.
- At startup, first attempt to adopt a handoff package, then perform exactly one abandoned-workspace sweep.
- Keep failed owned-workspace deletion state so `PacketSpec` destruction retries the deletion.
- Validate handoff markers. Unreadable, URL-mismatched, invalid-package, and duplicate markers/workspaces must not remain reusable.
- Do not claim a package is reusable unless its handoff marker was written successfully.
- On Linux, `CopyThread` must copy symbolic links as symbolic links and preserve their stored link targets.
- On Windows, do not terminate Explorer. After maintenance, restart SGStudio only through the Explorer-based unelevated launch path; if that launch fails, report failure and do not fall back to an elevated child process.

Directory-permission handling on Linux aarch64 is explicitly outside this follow-up because deployment already guarantees the required permissions.

## Follow-up Design

1. `Plugin::startStartupOnlineCheck()` owns the startup ordering: adopt any valid handoff first, then sweep once. The adopted workspace remains protected by its lock during that sweep.
2. `PacketSpec::takeHandoffRemotePacket()` scans all marker candidates, adopts at most one matching valid package, and removes unlocked malformed, mismatched, invalid, or duplicate candidates.
3. `PacketSpec::preserveScratchDataForHandoff()` returns success. It only enters preserved/reusable state after an atomic marker write succeeds. The update dialog creates this marker before launching maintenance and cancels the handoff state if launch or readiness fails.
4. `PacketSpec::cleanupScratchData()` clears its paths and parsed state only after removal succeeds. A failed removal therefore remains available for the destructor's final retry.
5. Linux `CopyThread` handles symlinks before directory traversal and recreates the original raw link target.
6. Windows maintenance never kills Explorer and never uses an elevated restart fallback.

## Follow-up Success Criteria

- No periodic scratch-cleanup timer remains.
- Startup adoption occurs before the single sweep.
- Marker-write failure stops the maintenance launch and does not set preserved state.
- Invalid marker paths either delete the unlocked workspace or remain protected by an active lock.
- Failed owned-workspace deletion retains the cleanup target for destruction-time retry.
- Linux symlink entries bypass regular file/directory copying.
- The Windows Explorer termination code and elevated restart fallback are absent.
- Verification remains static; no build or runtime rehearsal is requested.

## Follow-up Outcome

- Removed both the delayed startup sweep and the once-per-minute sweep. Startup now adopts first and sweeps once immediately afterward.
- Changed handoff preservation to a checked operation. Remote marker creation must succeed before maintenance is launched; all launch/readiness failures cancel the marker and preserved state without discarding the valid package.
- Tightened marker adoption to the exact atomic marker format and current platform/package URL. The post-adoption sweep deletes every other unlocked UUID workspace, including unreadable, mismatched, invalid, and duplicate handoff candidates.
- Preserved failed owned-workspace cleanup state so later `PacketSpec` destruction retries the same directory.
- Added Linux raw `readlink()` / `symlink()` copying before directory traversal. The inspected `D:\linux_aarch64\standard\cn\SGStudio.tar.gz` contains 120 symbolic links, all with relative targets.
- Removed the Windows Explorer termination/restart block from `CopyThread`.
- Removed the elevated `QProcess::startDetached()` restart fallback. Windows restart now succeeds only through Explorer's unelevated shell; failure remains visible in maintenance instead of launching SGStudio as administrator.
- Added restart-result handling on both platforms so maintenance does not close after a failed application launch.
- Updated the two active updater KnowledgeBase documents to match the new lifecycle.

## Follow-up Verification Result

- `git diff --check` passed.
- Targeted search found one `cleanupAbandonedScratchData()` call site, directly after `takeHandoffRemotePacket()`, and no 5-second or 60-second cleanup timer.
- Targeted search found no Explorer `taskkill`, `explorer.exe` restart, or elevated restart fallback in maintenance.
- Static inspection confirmed Linux symlinks are handled before `isDir()` recursion and recreate the stored link target.
- Static inspection confirmed cleanup paths are cleared only when recursive removal succeeds.
- No build or runtime update rehearsal was run, matching the requested static verification scope.
