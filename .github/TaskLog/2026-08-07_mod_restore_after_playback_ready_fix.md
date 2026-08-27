# Mod Restore After Playback Ready Fix

## Scope

- Fix startup/profile restore where `Mod=true` is persisted but gets forced back to `false` before delayed-readiness Playback providers finish becoming selectable.
- Cover Arb Playback and Quick Waveform through the shared restore/UI host path.
- Keep runtime profile format and global restore ordering unchanged.

## Verification Level

- `debug-build`

## Observations

1. `CommonDeviceProfile::restoreSettings()` restores the persisted `Mod` property early from the `common` section.
2. `MainWindow::updateSweepAndBusinessAvailability()` force-clears `Mod=false` whenever the current selected business does not yet support the `MOD` button.
3. During startup/profile restore, delayed-readiness Playback providers can temporarily leave `selectedBusiness()` empty or not yet selectable, so this availability pass can overwrite the saved `Mod=true`.
4. After the provider later becomes selectable, the UI host now retries business selection, but nothing replays the earlier persisted `Mod` intent.
5. The saved profile already contains both `common.Mod` and `activedBussiness`, so the missing piece is not serialization data but restore-time ordering/finalization.

## Root Cause

The persisted `Mod` state is restored too early relative to delayed-readiness Playback provider selection. Availability gating correctly prevents the user from enabling `Mod` with no selectable modulation business, but during restore it also erases the previously saved `Mod=true` before the saved active Playback business becomes ready.

## Design

1. Parse the persisted `common.Mod` value and `activedBussiness` name before running the full restore.
2. Store that `Mod` intent temporarily on `MainWindow`.
3. Keep the existing availability rule that force-clears `Mod` while no selectable modulation business exists.
4. As soon as `updateSweepAndBusinessAvailability()` sees a selectable provider whose name matches the restored active business, replay the pending `Mod` state and clear the pending intent.
5. Do not let the pending `Mod` intent leak to an unrelated user-selected business if the original restored active business never becomes available.

## Success Criteria

- Startup/profile restore with Arb Playback enabled and `Mod=true` ends with `Mod` automatically turned back on once Arb becomes selectable.
- Quick Waveform follows the same delayed-readiness restore path.
- The persisted `Mod` intent is only replayed for the original restored active business.
- Normal non-restore `Mod` availability rules remain unchanged.

## Verification Checklist

- [x] Confirm the fix lives in restore-time host finalization rather than persistence format changes.
- [x] Confirm the pending `Mod` intent is tied to the restored `activedBussiness` name.
- [x] Confirm focused diagnostics report no new errors.
- [x] Confirm the existing Debug build tree links `Core` successfully after the change.
- [x] Run `git diff --check`.

## Result

- Added a restore-only pending `Mod` intent on `MainWindow`, populated by `MainWindowSettingsController` from the saved profile before the full restore runs.
- `updateSweepAndBusinessAvailability()` now reapplies that saved `Mod` state once the restored active Playback business really becomes selectable, while still forcing `Mod=false` during the temporary unavailable phase.
- The replay is bound to the saved `activedBussiness` name, so it does not leak onto an unrelated business if restore later falls back or the user manually changes selection.
- Focused diagnostics stayed clean, `Core` linked successfully in the existing Debug build tree, and `git diff --check` passed.