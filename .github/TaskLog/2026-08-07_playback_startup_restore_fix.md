# Playback Startup Restore Fix

## Scope

- Fix startup "Last state" restore for HTRA Arb Playback and Quick Waveform.
- Preserve the existing runtime-profile full-reset/full-restore flow and the current `TxSessionService -> TxPipelineRuntime` Playback pipeline.
- Change only the provider-local restore/finalization behavior needed to survive startup ordering.

## Verification Level

- `debug-build`

## Observations

1. Runtime profile loading resets all businesses to defaults, restores common settings and per-business profiles, then restores selected/current business, then runs exactly one `selectBusiness2Work()` pass under the current full-restore model.
2. During that per-business restore phase, the previously selected business has already been cleared, and pipeline updates are still suspended.
3. `ArbDataGenerator::restoreSettings()` replays the saved profile through public setters, but Ordinary/IQS file restore depends on current Playback capability data:
   - `setFileName()` calls `handleFile()`;
   - `handleFile()` currently rejects file Playback when `maxWaveformBytes == 0`;
   - `setArbSampleRate()` normalizes against the current sample-rate domain; and
   - `setPeriod()` clamps against the current Playback capability.
4. Therefore, if startup restore runs before a usable Playback capability snapshot is available, Arb Ordinary/IQS restore collapses back to defaults instead of preserving the saved authoring state.
5. `QuickWaveformBusiness::restoreSettings()` restores UI-facing properties immediately, but only calls `onFileSelected()` when `BusinessManager::selectedEntryBusiness() == this` at restore time.
6. During startup full-restore that selected business has not yet been restored, so Quick Waveform keeps only the saved file path/profile snapshot and does not proactively rematerialize the file payload.
7. The later async Playback refactor did not break JSON persistence itself; it removed any legacy business-activation guarantee for core-managed Playback providers, so a provider that misses its post-restore materialization window can now stay in a "UI restored, payload absent" state until a manual file reload or enable cycle creates a fresh refresh edge.

## Root Cause

- Arb Playback loses state because its restore path is capability-dependent but startup profile restore occurs before a usable Playback capability snapshot is guaranteed.
- Quick Waveform keeps the saved authoring/UI state but misses payload rematerialization because restore happens before selected-business restoration, and core-managed Playback no longer gives it a later legacy `startBusiness()` style recovery hook.

## Design

1. Arb:
   - persist a pending restore profile when file Playback restore arrives before usable Playback capabilities;
   - replay that profile automatically once capabilities become usable; and
   - keep the existing public-setter restore path once replay happens.
2. Quick Waveform:
   - keep the current restored profile snapshot and selected file path;
   - schedule a post-restore self-check that loads the file if this business becomes the selected provider on the next event turn; and
   - also retry that pending load when usable capabilities arrive and a restored selected file still has no payload.
3. Do not change runtime-profile file format, `loadRuntimeProfileFile()` ordering, or the core-managed Playback runtime.

## Success Criteria

- Startup "Last state" restore preserves Arb Playback file, sample rate, sample offset, samples-to-use, period, and related UI state instead of falling back to defaults.
- Startup "Last state" restore brings Quick Waveform back to a runnable state without manual file reload.
- If Quick Waveform was the selected Playback business before shutdown, its restored payload rematerializes automatically once selection/capabilities are ready.
- The fix is explained in terms of startup restore ordering and current core-managed Playback design, not blamed on JSON corruption.

## Verification Checklist

- [x] Confirm Arb restore now survives missing Playback capabilities at restore time.
- [x] Confirm Quick Waveform restore schedules payload materialization after selected-business restoration instead of requiring manual reload.
- [x] Confirm no runtime-profile schema/order change was introduced.
- [x] Incrementally build the touched HTRA and QuickWaveform plugin targets in the existing Debug build tree.
- [x] Run `git diff --check`.

## Result

- Arb Playback restore no longer tries to consume file Playback settings before usable Playback capabilities exist. Instead, it caches a pending profile and replays the same public-setter restore path once capabilities become usable.
- Quick Waveform restore still restores UI-facing authoring state immediately, but now also schedules a post-restore selected-business check and a capability-ready retry so the saved file payload rematerializes automatically without a manual file reload.
- The runtime profile format and `loadRuntimeProfileFile()` ordering were intentionally left unchanged. The defect was not JSON corruption; it was a provider-local restore/finalization mismatch against the current startup ordering.
- This is related to the recent Playback pipeline refactor only at the boundary level: core-managed Playback providers no longer receive a legacy activation lifecycle that might have hidden missed post-restore materialization. The bug is therefore an exposed restore-order dependency, not a queue/executor correctness problem inside `TxSessionService` or `TxPipelineRuntime`.
- Validation passed: touched Arb/QuickWaveform sources remain in their active CMake targets, focused diagnostics report no new errors, `git diff --check` completed without reporting patch issues, `HTRA` linked successfully in the existing Debug build tree, and `QuickWaveform` linked successfully with only the pre-existing AutoMoc warning about `quickwaveformpanel.cpp` including `quickwaveformpanel.moc` without a `Q_OBJECT` macro.