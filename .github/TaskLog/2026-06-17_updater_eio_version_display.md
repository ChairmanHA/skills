# Updater EIO Version Display

Date: 2026-06-17

## Scope

- Replace the active updater firmware-version path that still uses the old GNSS naming with EIO naming.
- Keep the change limited to CMake-included updater files and durable updater build documentation.
- Display EIO firmware versions in `UpdateDialog` for both current device state and target package state.

## Observations

- Root `CMakeLists.txt` now defines `EIO_VERSION`.
- Active updater CMake still injects the old GNSS firmware macro, so `PacketSpec::createNativeInfoPacket()` cannot receive the renamed value.
- Active `PacketSpec` stores the old GNSS-named firmware field and parses the remote package's old GNSS JSON key.
- `package-info/version.json` already uses an `EIO` field.
- `Core::DeviceInfo` already exposes `EIOVersion`, and other UI code treats EIO as a peer of FPGA, MCU, and BUS.
- `src/plugins/updater_bak` is excluded from the active CMake updater path and is not part of this implementation slice.

## Success Criteria

- Active updater CMake defines `EIO_VERSION` for compiled updater code.
- `PacketSpec` exposes and fills `eioVersion()` from local `EIO_VERSION` and remote `version.json` field `EIO`.
- `UpdateDialog` shows an EIO row with current device EIO and target package EIO.
- Existing updater maintenance handoff, download URL construction, and package parsing flow remain otherwise unchanged.

## Verification Level

Static.

- Search active updater code for stale GNSS-named updater firmware references.
- Search for `EIO_VERSION` / `eioVersion` usage in the active updater path.
- Run `git diff --check`.
- Do not build unless explicitly requested.

## Verification Results

- Active updater search found no stale GNSS-named updater firmware references.
- Active updater search confirmed `EIO_VERSION`, `eioVersion()`, `m_eioVersion`, `current_eio`, and `latest_eio_version` are wired.
- Whole-repository stale-name search still finds only `src/plugins/updater_bak` and historical TaskLog notes; `_bak` is excluded from the active CMake updater path.
- `git diff --check` passed. Git reported only normal CRLF conversion warnings for touched Windows-worktree files.
- No build or runtime launch was run.
