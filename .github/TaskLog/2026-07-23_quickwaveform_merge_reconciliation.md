# Quick Waveform Merge Reconciliation

## Scope

- Repair the manual `dev` merge in `src/plugins/quickwaveform/quickwaveformbusiness.cpp`.
- Preserve the feature branch's immutable `Core::PlaybackPayload` and `loadQuickWaveform()` file-loading path.
- Retain the `dev` branch's remote editor state/actions, load progress/error publication, and `loadedFileChanged` projection.
- Do not restore the legacy full-size `QVector<float>`/`m_wavData`/`loadFileInThread()` path.
- Do not resolve the repository's other outstanding merge conflicts.

## Observation

- The staged file currently defines `QuickWaveformBusiness::onFileSelected()` twice.
- The first definition is a partial legacy loader body with undeclared locals and a removed `m_wavData` member.
- The second definition contains the feature payload worker but lost its generation increment and selected-path assignment.
- `buildPlaybackExecutionContext()` currently splices legacy float slicing into the immutable payload path and references undeclared variables.
- Repository architecture requires Quick Waveform to materialize the final `int16_t` payload once through `probePlaybackWav()`/`readPlaybackWavIqData()`.
- Cross-host state requires `loadedFileChanged(path)` only after a successful load commits; failures must preserve the previously committed path.

## Design

1. Remove the malformed legacy loader definition.
2. Restore one `onFileSelected()` implementation based on the feature worker:
   - increment the load generation for every selection/clear;
   - preserve the previously committed selected path while a new load is pending;
   - publish remote loading state when a non-empty load starts;
   - commit `m_selectedFilePath` and emit `loadedFileChanged(path)` only after the current worker succeeds;
   - clear and publish the committed path on explicit clear.
3. Restore `buildPlaybackExecutionContext()` to consume the capability-matched immutable payload without legacy float conversion.
4. Keep IQS-WAV raw complex data handling, forced AutoScale, and IQ Scale 100% behavior unchanged.

## Success Criteria

- Exactly one `QuickWaveformBusiness::onFileSelected()` definition remains.
- No active Quick Waveform source references `m_wavData`, `loadFileInThread`, `currentData`, or the removed fixed download-size constant.
- A load start publishes `loadInProgress=true` without prematurely changing `loadedFilePath`.
- A successful current-generation load commits and emits the final loaded path.
- A failed load leaves the previous committed path unchanged and publishes the load error.
- Clear/reset publishes an empty loaded path.
- `buildPlaybackExecutionContext()` uses only the immutable payload and matching power metrics.
- IQS-WAV still uses the shared packet-trimming reader and forced scaling policy.

## Verification Level

- `static`

## Verification Checklist

- [x] Re-read the modified source immediately before editing.
- [x] Confirm the source remains included by the active QuickWaveform CMake target.
- [x] Search for duplicate definitions and removed legacy identifiers.
- [x] Review the focused diff against both `HEAD` and `MERGE_HEAD`.
- [x] Run `git diff --check`.
- [x] Confirm no other unresolved merge file was modified.

## Result

- Restored a single `onFileSelected()` implementation around the feature branch's
  `QFutureWatcher<QuickWaveformLoadResult>` and `loadQuickWaveform()` worker.
- Removed all merged legacy float-loader and float execution-context fragments.
- Remote load start/finish/error state remains driven by
  `providerExecutionContextChanged()` and `waveformLoaded`.
- A successful current-generation load now commits `m_selectedFilePath` and emits
  `loadedFileChanged(path)`; explicit clear/reset emits an empty path.
- Reset and subsequent selections invalidate an older in-flight worker generation.
- `buildPlaybackExecutionContext()` again passes only the capability-matched immutable payload and
  its precomputed power metrics.
- Static checks found one `onFileSelected()` definition, balanced braces, zero removed-legacy
  identifiers, and no whitespace errors in the repaired source.
- Verification remained static per repository policy; no build or runtime execution was performed.
