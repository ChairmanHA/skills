# QuickWaveform Disabled Restore Metadata Fix

## Scope

- Fix Quick Waveform startup restore when a file was previously loaded but the business itself was not enabled.
- Keep the existing runtime profile schema and core restore order unchanged.
- Restrict changes to QuickWaveform file-path resolution, metadata restore, and file-dependent property replay.

## Verification Level

- `static`

## Observations

1. `restoreSettings()` currently applies restored waveform parameters before it has established whether the saved file path can be resolved into a valid WAV file.
2. When Quick Waveform is not the restored selected business, the provider intentionally skips real payload materialization and keeps only pending restore intent.
3. In that disabled path, file-dependent UI properties can already be restored, while `samplesInFile` and panel-side loaded-file markers still depend on a later real load that never happens until the user enables the business.
4. `fullFilePath` reconstruction currently prefers `selectedFilePath`, then `currentPath + fileName`, but does not fall back to `filePath + fileName`.
5. Therefore a saved profile can end up with `samplesToUse` restored from JSON while the provider still has no validated loaded-file snapshot, which matches the observed "parameters restored but file looks unloaded" state.

## Root Cause

- QuickWaveform restore still mixes two states in the disabled case: it eagerly replays file-dependent parameters, but it defers the file's real metadata/load state.
- Its path reconstruction is also too narrow, so if `selectedFilePath` is unavailable and `currentPath` is not sufficient, the file snapshot is lost even though enough information still exists under `filePath`.

## Design

1. Resolve the restored file path more robustly: `selectedFilePath` first, then `currentPath + fileName`, then `filePath + fileName`.
2. If a valid WAV file can be probed, restore its lightweight metadata immediately even when the business is not enabled.
3. Replay file-dependent parameters only against known file metadata; if no valid file can be resolved, clear the loaded-file state instead of leaving stale slice values visible.
4. Keep actual payload materialization deferred until the provider is selected/enabled, as before.

## Success Criteria

- When Quick Waveform was not enabled before shutdown, startup still shows the previously loaded file as loaded.
- `samplesToUse`, `sampleOffset`, `period`, and related UI state no longer survive without a corresponding valid restored file snapshot.
- Actual playback data is still only materialized once Quick Waveform becomes the selected provider.