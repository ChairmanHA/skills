# Updater Online Update Flow Analysis

## Scope

-  Implement a feature : updater online-update path with focus on startup/background download, local package staging, version comparison, and new-version prompting.

## Verification Level

Static.

No build, run, or network update rehearsal was requested.

## Observations

- Active CMake includes `src/plugins/updater` and excludes `src/plugins/updater_bak`; the old `UpdateCoordinator` startup prefetch path is not built.
- Active `Plugin::initialize()` only registers `System -> Update`; `extensionsInitialized()` only removes first-level old cache entries from `AppLocalDataLocation`.
- Active `UpdateDialog` starts `PacketSpec::loadUrl()` only when the dialog is constructed, so online download is menu/dialog-triggered rather than application-startup-triggered.
- Active `PacketSpec::loadUrl()` ignores its URL argument and constructs a hard-coded Harogic URL from compile-time package/language macros.
- Active new-version prompt code is present only as TODO/commented code. `showNewVersionNotification()` does not compare versions or show `NewVersionNotifiction`.
- The `_bak` implementation contains the intended shape: read `[Update] online/packageUrl`, prefetch the package at startup, wait for both device-open and package-ready, compare `SGS_VERSION` with package `Version`, and prompt `Update Now / Later / Ignore`.

## Remediation Direction

1. Reintroduce a small active coordinator rather than reviving `_bak` wholesale.
2. Read `[Update] online/packageUrl` from runtime configuration and keep compile-time URL construction only as an explicit fallback if desired.
3. Prefetch into an updater-owned cache subdirectory under `AppLocalDataLocation`, with TTL cleanup that does not delete unrelated app-local data.
4. Parse and validate the package before any prompt: version metadata, release notes, updater executable, maintenance executable, package root, and integrity metadata.
5. Use a dedicated version comparison helper with one chosen version dimension; prefer local `PACKET_VERSION` vs remote package `Version` unless product semantics require `SGS_VERSION`.
6. Trigger the prompt only when online is enabled, the package is valid, the relevant local version is lower, the version is not ignored, and the same version has not already been prompted in the current session.
7. Make `Update Now` reuse the prefetched package and then enter the current safe maintenance handoff path.

## Requested Implementation Plan: GUI Software Update Prompt

This section supersedes the broader remediation direction for the next implementation step.

### User Decisions

- Treat this as a GUI software update check.
- Compare local `SGS_VERSION` with remote `version.json` field `Software`.
- Respect `[Update]/online` in `configuration/Settings.ini` for the startup silent check.
- Do not use `PACKET_VERSION` for this check.
- Do not change the current download mechanism.
- Do not change the current cache cleanup mechanism.
- After the background package finishes downloading and extracting into the existing cache path, validate/read version metadata, then compare versions.
- If the remote software version is newer, show a two-button prompt: found a new version, update now?
- If the user chooses Yes, open `UpdateDialog`, select `Latest Online`, and let the user read release notes and decide whether to click `Update`.
- If the user chooses No, close the prompt and do nothing else.
- Absolutely avoid a second online download after the startup silent download has already produced a usable remote package.

### P0 Behavior

1. On updater plugin startup, start one silent online package load using the existing `PacketSpec::loadUrl()` path.
2. `PacketSpec` keeps its current behavior:
   - It builds the remote URL internally.
   - It downloads into `Plugin::appLocalPath()` plus a uuid subdirectory.
   - It decompresses in that cache directory.
   - It reads `version.json` and `releasenote.txt`.
   - It locates the package updater and maintenance executable.
   - It emits `validChanged()` only after successful package parsing.
3. On `validChanged()`, compare:
   - local: `QString::fromLatin1(SGS_VERSION)`
   - remote: `m_startupRemoteSpec->softwareVersion()`
4. If remote `Software` is empty, unparsable, or not greater than local `SGS_VERSION`, do not prompt.
5. If remote `Software` is greater, show one `Controls::MessageDialog` with `Yes` and `No`.
6. On `Yes`, reuse the existing startup `PacketSpec` inside `UpdateDialog`, then call `UpdateDialog::switchOnline()`.
7. `UpdateDialog` remains responsible for showing release notes and for the final user-driven update start.
8. `UpdateDialog` must not call `m_remoteFile->loadUrl(url)` from its constructor.
9. If the startup silent download failed or did not produce a valid package, `UpdateDialog` may perform one fallback online load only when the user actually switches to `Latest Online`.

### Important Scope Choice

P0 must reuse the prefetched `PacketSpec` inside `UpdateDialog`.

Reason:

- The startup silent check already downloads, extracts, and parses the remote package before prompting.
- A second online download after `Yes` is both unnecessary and explicitly disallowed for this slice.

Practical result:

- When the startup `PacketSpec` is valid, opening `UpdateDialog` must directly show that existing package metadata and use that package for the later maintenance handoff.
- Only if the startup silent download failed may `UpdateDialog` attempt one fallback online load when the user explicitly selects `Latest Online`.

### Files To Modify

#### `src/plugins/updater/plugin.h`

Add active startup-check state:

- Forward declare `PacketSpec`.
- Store a startup package spec pointer:
  - `PacketSpec *m_startupRemoteSpec = nullptr;`
- Store prompt/session state:
  - `QString m_promptedSoftwareVersion;`
  - `bool m_startupOnlineCheckStarted = false;`
  - optionally `QPointer<Controls::MessageDialog> m_newSoftwarePrompt;`

Add private slots/helpers:

- `void startStartupOnlineCheck();`
- `void onStartupRemoteSpecValid();`
- `void showNewSoftwarePrompt(const QString &localVersion, const QString &remoteVersion);`
- `void showOnlineUpdateSelected();`

#### `src/plugins/updater/plugin.cpp`

Keep existing behavior:

- Keep `removeOldFile(m_appLocalPath, 60);` unchanged.
- Keep `PacketSpec::loadUrl()` unchanged.
- Keep URL construction inside `PacketSpec::loadUrl()` unchanged.

Add startup trigger:

- In `extensionsInitialized()`, after `removeOldFile(...)`, schedule startup check with `QTimer::singleShot(0, this, &Plugin::startStartupOnlineCheck);`.
- `startStartupOnlineCheck()` should:
  - guard against repeated starts,
  - allocate `m_startupRemoteSpec`,
  - connect `PacketSpec::validChanged` to `onStartupRemoteSpecValid`,
  - call `m_startupRemoteSpec->loadUrl(url())`.

Add version comparison helper in the anonymous namespace:

- Parse dotted numeric versions such as `2.6.3`, `2.6.3.1`.
- Trim whitespace.
- Treat missing trailing segments as `0`.
- Return invalid if any non-empty segment is not numeric.
- Do not attempt semantic suffix support in P0.

Add prompt logic:

- `onStartupRemoteSpecValid()` should:
  - ignore invalid/missing specs,
  - read remote software via `softwareVersion()`,
  - compare remote `Software` to local `SGS_VERSION`,
  - avoid repeating the same prompt version in one session,
  - skip showing a second prompt if an update dialog is already visible.

- `showNewSoftwarePrompt(...)` should:
  - create a `Controls::MessageDialog` parented to `Core::ICore::dialogParent()` when possible,
  - use `SG::MSG_QUESTION`,
  - buttons: `Yes` and `No`,
  - `Yes` callback calls `showOnlineUpdateSelected()`,
  - `No` callback only closes the prompt.

Add dialog selection helper:

- `showOnlineUpdateSelected()` should:
  - call or share logic with `showOnlineUpdate()`,
  - ensure `m_updateDialog` exists,
  - call `m_updateDialog->switchOnline()`,
  - raise/activate the dialog.

Lifecycle:

- In `aboutToShutdown()`, if `m_startupRemoteSpec` exists and is still running, wait for it briefly or let the existing thread finish before deleting the object. Avoid destroying a running `QThread`.
- Delete `m_startupRemoteSpec` after shutdown handling.

#### `src/plugins/updater/updatedialog.h`

Add a small API for reusing the startup-loaded remote package spec:

- `void setRemotePacketSpec(PacketSpec *remoteSpec);`

Add local fallback-load state/helper as needed:

- a bool to prevent repeated fallback attempts
- a helper that starts fallback online load only when the current remote spec is invalid and not already running

#### `src/plugins/updater/updatedialog.cpp`

Update `UpdateDialog::switchOnline()`:

- It currently only checks `radioOnline`.
- It should also call `typeChanged(1)` so the target version/release note area immediately reflects the online package when the dialog is opened from the prompt.

Update constructor behavior:

- Do not start online load from the constructor.
- Keep the remote `PacketSpec` object creation and signal wiring, but defer fallback `loadUrl()` until the user actually selects `Latest Online` and only if the startup spec was not valid.

No change should be made to:

- `PacketSpec::loadUrl()`
- `PacketSpec::run()`
- `https_download_v2()`
- `removeOldFile()`
- maintenance handoff
- copy/install behavior

#### `src/plugins/updater/newversionnotifiction.*`

No P0 change.

The existing three-button notification class should remain unused for now. Removing it would be cleanup outside this behavioral slice.

### Success Criteria

- Application startup triggers exactly one silent online package load through `PacketSpec::loadUrl()`.
- Choosing Yes after a successful startup silent download must not trigger a second online package download.
- Existing cache cleanup still runs exactly as before.
- Existing online download URL construction remains exactly as before.
- When the package is valid and remote `Software` is greater than local `SGS_VERSION`, a two-button prompt appears.
- Choosing No does not open the update dialog and does not persist an ignore state.
- Choosing Yes opens `UpdateDialog`, selects `Latest Online`, and directly displays the already-downloaded online target information.
- If the startup silent download failed, selecting `Latest Online` in `UpdateDialog` may trigger exactly one fallback online load attempt.
- Clicking `Update` still follows the current safe maintenance handoff path.

### Static Verification

- Search confirms no edits to `PacketSpec::loadUrl()`, `https_download_v2()`, or `removeOldFile()`.
- Search confirms `PACKET_VERSION` is not used in the new prompt comparison.
- Search confirms the new comparison uses `SGS_VERSION` and `PacketSpec::softwareVersion()`.
- Search confirms `NewVersionNotifiction` is not used for the new two-button prompt.
- `git diff --check` passes.

### Optional Debug-Run Verification

- Temporarily use a remote `version.json` with `Software` greater than local `SGS_VERSION`.
- Start SGStudio and watch logs for:
  - startup online check start,
  - package parse success,
  - local/remote software comparison,
  - prompt shown.
- Click No and confirm no dialog opens.
- Restart, click Yes and confirm:
  - `UpdateDialog` opens,
  - `Latest Online` is selected,
  - release notes and target version populate after the dialog's normal online load.

### Resolved Decision

- The startup silent check must obey `[Update]/online` in `configuration/Settings.ini`.

Reason:

- Otherwise the background thread would silently download packages even when the user has explicitly disabled online updates or when local development keeps `online=false`.

## Documentation Follow-up

### Scope

- Update the active updater KnowledgeBase documents so future debugging can rely on the current startup-online-check behavior rather than the older dialog-owned online-download behavior.

### Documentation Targets

- `.github/KnowledgeBase/updater_firmware_update_mechanism.md`
- `.github/KnowledgeBase/updater_mechanism_gap_and_remediation.md`

### Required Documentation Points

- Record that updater startup now performs one silent online package check gated by `[Update]/online`.
- Record that the startup `PacketSpec` is reused by `UpdateDialog` after the user chooses `Yes`.
- Record that `UpdateDialog` constructor no longer starts online download.
- Record that a fallback online load may happen only when the startup silent check failed or did not produce a valid package, and only after the user actually selects `Latest Online`.
- Add concrete debugging guidance: which objects own the remote package, which log messages indicate startup check / fallback load / prompt display, and what symptoms imply reuse vs fallback paths.

### Success Criteria For Documentation

- The KnowledgeBase no longer describes the active online-update path as dialog-construction-triggered download.
- A future reader can distinguish the three paths clearly: startup silent check, prompt-to-dialog reuse path, and fallback online load path.
- The documentation includes actionable debug checkpoints without requiring `_bak` code archaeology.

## EIO Display Follow-up

### Observation

- In `UpdateDialog`, the `Current -> EIO` field can legitimately display `0.0.0` on machines that do not have the EIO option installed.

### Confirmed Design

- `UpdateDialog` reads the current device-side firmware versions from `currentDevice->getDeviceInfo()`.
- `EIO` is not inferred from package metadata or from a separate updater-specific source.
- When the device open snapshot reports `EIOVersion == 0`, `UpdateDialog` displays `0.0.0`.
- For devices without the EIO option, this is expected behavior rather than a UI bug.

### Documentation Requirement

- Record this explicitly in updater KnowledgeBase debugging guidance so future investigations do not misclassify `Current -> EIO = 0.0.0` as an updater regression.

## Local Default Version Source Follow-up

### Observation

- Active `Local Default` metadata is currently built by `PacketSpec::createNativeInfoPacket()`.
- The package root contract already defines `version.json` as the source of software / firmware target versions.
- `package-info/version.json` already contains the local package `Software` / `MCU` / `FPGA` / `FX3` / `EIO` values.
- The updater firmware compile-time macros are only still present because `createNativeInfoPacket()` has not yet switched to runtime `version.json`.

### Decision

- `Local Default` must read its target package metadata from runtime-root `version.json` and `releasenote.txt`, not from updater-only firmware compile-time macros.
- The build/configure flow must ensure those two files exist in the active runtime root used by `createNativeInfoPacket()`.
- After this switch, updater-specific `FPGA_VERSION` / `MCU_VERSION` / `BUS_VERSION` / `EIO_VERSION` compile definitions can be removed.

### Success Criteria

- `PacketSpec::createNativeInfoPacket()` reads `version.json` from the active runtime root.
- `package-info/version.json` and the selected release note file are copied both to the existing build-root packaging location and to the active runtime root.
- `Local Default` target FPGA / MCU / BUS / EIO values come from `version.json`.
- Updater code no longer depends on `FPGA_VERSION` / `MCU_VERSION` / `BUS_VERSION` / `EIO_VERSION` compile definitions.
