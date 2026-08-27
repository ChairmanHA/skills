# QuickWaveform Restore Load Replay Fix

## Scope

- Fix startup "Last state" restore for Quick Waveform when a previously loaded file must be restored.
- Preserve the existing runtime profile schema and the current `TxSessionService -> TxPipelineRuntime` playback flow.
- Restrict changes to QuickWaveform restore/load finalization and the minimum UI-side snapshot replay needed for file info consistency.

## Verification Level

- `static`

## Observations

1. Manual file loading goes through `QuickWaveformPanel::loadFilePath()` -> `QuickWaveformBusiness::onFileSelected()` -> `startWaveformLoad(filePath, true)`.
2. The `resetPlaybackParameters=true` path is important because it first loads the file with file-derived defaults, then reapplies the restored slice/profile values once `samplesInFile` is known, and finally reloads with those restored values.
3. Startup restore does not call `onFileSelected()` because `restoreSettings()` runs before `activedBussiness` is restored, so it stores the profile snapshot and later calls `scheduleRestoredWaveformLoad()`.
4. The queued restore load currently calls `startWaveformLoad(selectedFilePath, false)` directly.
5. Before any real file load, `applyRestoredProfileValues(profile, samplesInFile())` runs with `samplesInFile()==0`, so `sampleOffset` and `samplesToUse` are clamped to zero.
6. Therefore the later restore load reuses zeroed slice values and can publish a waveform payload whose period/sample-rate UI looks restored while the actual copied I/Q sample region is empty.
7. The current restore load is also only a one-shot queued attempt. If Quick Waveform is not selected at that moment, the restore intent is not preserved as an explicit pending action that can be fulfilled later when selection is restored.
8. Panel-side loaded-file markers and navigation snapshot are partly restored from saved fields, but they are still coupled to whether the restore path replays enough of the normal load side effects.

## Root Cause

- Quick Waveform startup restore currently bypasses the real "Load" path and directly performs a parameterized rebuild before file-derived metadata exists.
- That early rebuild zeroes slice-dependent fields and does not preserve a durable pending restore-load intent when the business is not yet selected.

## Design

1. Treat startup file restore as a pending load intent instead of a one-shot queued guess.
2. When that pending intent is fulfilled, run the same initial load path as the UI `Load` button (`resetPlaybackParameters=true`) so file metadata is established before restored slice values are re-applied.
3. Keep the pending restore intent until the provider is both selectable and capability-ready, then consume it exactly once.
4. Restore panel-side loaded-file snapshot even when directory-navigation fields are incomplete, so previously loaded file info is visible even when the business starts disabled.

## Success Criteria

- If Quick Waveform was enabled before shutdown, startup restore automatically rematerializes and replays the saved file without requiring a manual reload.
- The restored waveform keeps the saved sample rate, period, sample offset, and samples-to-use semantics instead of silently becoming an empty slice.
- If Quick Waveform was not enabled before shutdown, opening the page still shows the previously loaded file information and keeps the saved file ready for later enable.
- No runtime profile schema or restore ordering outside Quick Waveform changes.

## Verification Checklist

- [x] Confirm restore-time file replay no longer bypasses the initial `Load` path.
- [x] Confirm pending restore values are no longer clamped to an empty slice solely because `samplesInFile` is not known yet.
- [x] Confirm panel-side loaded-file snapshot restore no longer depends on `currentPath` being non-empty.
- [x] Run focused static diagnostics for `quickwaveformbusiness.cpp`.
- [x] Run `git diff --check` for the touched files.

## Result

- Root cause was local to QuickWaveform restore finalization, not JSON persistence and not the Playback executor itself. Startup restore kept the saved profile object, but its delayed reload path skipped the same initial `Load` behavior that the UI button uses.
- That meant `applyRestoredProfileValues()` ran before any file metadata existed, so `sampleOffset` and especially `samplesToUse` were clamped against `samplesInFile == 0`. The next reload reused those zeroed values and produced a formally valid payload with restored period/sample-rate UI but no actual copied waveform samples.
- The fix makes pending restore replay use the same initial load path as a real file load whenever restore is still pending, including the fallback reload launched from `buildPlaybackExecutionContext()`.
- The fix also preserves pending slice values until file metadata exists, and restores the loaded-file snapshot on the panel even if saved navigation fields are incomplete or the business starts disabled.