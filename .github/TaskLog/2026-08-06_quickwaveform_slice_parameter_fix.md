# Quick Waveform Slice Parameter Fix

## Scope

- Fix Quick Waveform so `sampleOffset`, `samplesToUse`, and `period` actually change the downloaded Playback waveform.
- Align Quick Waveform file Playback behavior with current Arb file Playback semantics for slice, zero-fill, scale, and rematerialization.
- Keep the existing `TxSessionService -> TxPipelineRuntime` provider-selection and released-payload rematerialization model.

## Verification Level

- `static`

## Observations

1. Quick Waveform setters update the UI-facing properties and emit `providerExecutionContextChanged()`, but do not rebuild the file payload while host storage is still present.
2. `QuickWaveformBusiness::requestGenerateData()` is currently specialized for released-payload rematerialization only; it intentionally returns early when `m_wavPayload.hasStorage()` is true.
3. `QuickWaveformBusiness::buildPlaybackExecutionContext()` returns `m_wavPayload` directly and does not apply `sampleOffset`, `samplesToUse`, `period`, `autoScale`, or `iQScale` to derive a new payload.
4. `loadQuickWaveform()` currently materializes the whole file from offset 0 to the full sample count and normalizes the short payload immediately, so the stored descriptor already bakes in the pre-edit waveform.
5. Arb file Playback applies slice semantics to the generated payload before it is published to the runtime, so changing `sampleOffset` / `samplesToUse` / `period` changes the actual downloaded waveform.

## Root Cause

Quick Waveform materializes a full-file Playback payload only at file-selection time and then keeps returning that immutable descriptor. Later edits to slice-related parameters never trigger a new materialization path for the storage-present payload, so the runtime keeps reusing stale waveform content.

## Design

1. Extend the Quick Waveform async file-loader to build payloads from the current effective playback parameters instead of always materializing the whole file.
2. Split file selection from parameter-preserving rebuilds so that:
   - selecting a new file still resets slice parameters to file defaults; and
   - editing `sampleOffset`, `samplesToUse`, `period`, `sampleRate`, `autoScale`, or `iQScale` rebuilds the current file payload with the current property values.
3. Match Arb ordinary-file semantics during materialization:
   - read only the selected slice;
   - apply AutoScale or IQScale to the selected IQ words;
   - zero-fill up to `period`; and
   - normalize short payloads for download using the current sample rate.
4. Keep IQS-WAV forced AutoScale behavior unchanged.
5. Keep rematerialization for released payloads on the same callback path, but rebuild with the current parameter values instead of resetting to whole-file defaults.

## Success Criteria

- With a loaded Quick Waveform file, changing `sampleOffset`, `samplesToUse`, or `period` changes the actual Playback payload rather than only the UI labels.
- Quick Waveform slice/zero-fill behavior matches current Arb file Playback semantics.
- Editing `sampleRate`, `autoScale`, or `iQScale` rebuilds the payload consistently with the current file.
- Released-payload rematerialization still works and preserves the edited parameter state.

## Static Verification Checklist

- [x] Confirm Quick Waveform source remains included by its active CMake target.
- [x] Confirm the async loader can materialize either file defaults or current edited parameters.
- [x] Confirm `sampleOffset`, `samplesToUse`, `period`, `autoScale`, and `iQScale` now participate in payload generation.
- [x] Confirm released-payload rematerialization rebuilds with the current parameter state.
- [x] Run `git diff --check`.

## Result

- Reworked Quick Waveform payload materialization so it can build either file-default payloads or parameter-preserving payloads from the current UI state.
- `sampleOffset`, `samplesToUse`, `period`, `autoScale`, `iQScale`, and edited `sampleRate` now participate in the actual Playback payload build instead of only updating properties.
- Quick Waveform now matches Arb file semantics for slice readback, zero-fill to `period`, scale application, and short-payload normalization.
- Released-payload rematerialization now rebuilds the current edited waveform instead of reverting to whole-file defaults.
- Static validation passed: edited Quick Waveform files report no diagnostics, the plugin target still includes the touched source/header, and `git diff --check` completed without reporting patch issues.