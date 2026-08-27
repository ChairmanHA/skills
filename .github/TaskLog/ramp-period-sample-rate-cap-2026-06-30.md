# Ramp Period Sample-Rate Cap Fix

## Scope

- Fix HTRA Digital Ramp period handling when the actual generated sample rate makes `round(Fs * Period)` exceed the download sample cap.
- Keep the change local to `src/plugins/htra/rampmodulator.cpp` unless static analysis shows a required writeback hook elsewhere.
- Do not change the playback runtime trim model, ARB behavior, or unrelated analog modulators.

## Evidence

- `src/plugins/htra/rampmodulator.cpp` is included by `src/plugins/htra/CMakeLists.txt`.
- `RampModulator::sanitizeProfile()` currently clamps `Period` by `RampModulator::maxPeriodSecForSpan(span)`, which uses the minimum feasible ramp sample rate.
- Before this fix, `generateWaveform()` then computed the actual sample rate with `calculateRampSampleRate(span, period)`.
- Before this fix, if `sampleCountForDuration(actualFs, Period) > kMaxComplexSamples`, `generateWaveform()` returned `"Ramp period exceeds max download size"`, leaving no waveform to play and no adjusted period written back from the generation result.

## Assumptions

- The desired behavior is to preserve the user's span and reduce `Period` only as much as needed for the actual sample rate/download cap.
- `SweepTime` must remain `<= Period`, so any reduced `Period` also clamps `SweepTime`.
- Verification level is `static`, per repository default; no build/run unless explicitly requested.

## Success Criteria

- A ramp profile that would exceed `kMaxComplexSamples` after sample-rate selection is converted to a valid profile instead of producing the max-download-size error.
- The adjusted `Period` is emitted through the existing `periodChanged` path so the property/UI can reflect the value that is actually generated.
- `generateWaveform()` uses the adjusted profile and produces IQ data for normal playback.
- Existing normal profiles keep their current generated sample rate and period behavior.

## Verification

- Static review of the updated control flow.
- Targeted source search to confirm the old hard return path no longer blocks Ramp generation.

## Implementation Notes

- `sanitizeProfile()` now applies a second clamp using the actual `calculateRampSampleRate(span, period)` result and the complex-sample download cap.
- `requestGenerateData()` applies the sanitized profile before queueing generation, so existing property bindings receive `periodChanged` / `sweepTimeChanged` for writeback.
- `generateWaveform()` also sanitizes its input and asserts the sample-count cap invariant instead of showing a second max-download-size error.
- `Waveform_Parameters_Constraints.md` was updated to match the new Ramp generation behavior.

## Cleanup Notes

- The later user-visible `"Ramp period exceeds max download size"` branch in `generateWaveform()` was removed because `sanitizeProfile()` owns the cap.
- A conservative `DATA_SAMPLE_RATE_MAX` fallback was added inside the sanitize clamp before removing that dead generation-stage error path.
