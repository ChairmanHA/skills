# Quick Waveform Period Boundary Fix

## Scope

- Fix Quick Waveform so `period` can extend a file waveform beyond the source file length by zero-filling, matching current Arb ordinary WAV behavior.
- Keep the existing parameterized payload materialization and rematerialization flow.
- Change only the Quick Waveform period-clamp boundary.

## Verification Level

- `static`

## Observations

1. Quick Waveform now rebuilds payloads from current `sampleOffset` / `samplesToUse` / `period` settings.
2. However, `clampQuickWaveformPeriod()` currently limits `period` to `min(samplesInFile, maxWaveformBytes / 4)`.
3. Arb ordinary WAV uses `clampEditablePeriod()` against device capacity only, then limits `samplesToUse` separately with `maxSamplesToUseForSlice(...)`.
4. That Arb split is what allows a short source slice to be copied into the front of a larger `period` buffer and zero-filled for the remainder.
5. Quick Waveform's extra `samplesInFile` cap prevents this zero-fill extension path, so increasing `period` beyond file length has no effect.

## Root Cause

Quick Waveform constrains `period` by source file length instead of only by Playback device capacity. This collapses the distinct Arb semantics of `samplesToUse` versus `period`, making period-based zero-fill extension impossible.

## Design

1. Change `clampQuickWaveformPeriod()` so its upper bound matches Arb ordinary WAV semantics: device capacity only.
2. Keep `samplesToUse` clamped separately by `maxSamplesToUseForSlice(samplesInFile, sampleOffset, period)`.
3. Leave file-load defaults unchanged: initial `period` still starts from the file sample count, but later edits may increase it up to the device capacity.

## Success Criteria

- Quick Waveform accepts `period > samplesInFile` when device capacity allows it.
- The rebuilt payload zero-fills from `samplesToUse` to `period`, matching Arb ordinary WAV behavior.
- `samplesToUse` and `sampleOffset` limits remain unchanged.

## Static Verification Checklist

- [x] Confirm the only behavior change is the Quick Waveform period upper bound.
- [x] Confirm `samplesToUse` still clamps by file slice reachability.
- [x] Run `git diff --check`.

## Result

- Changed `clampQuickWaveformPeriod()` so Quick Waveform `period` now matches Arb ordinary WAV semantics and is limited only by Playback device capacity.
- Kept the existing `samplesToUse` clamp on `maxSamplesToUseForSlice(samplesInFile, sampleOffset, period)`, so file reachability rules are unchanged.
- Initial file-load defaults remain file-length based; only later period edits can now extend the waveform with zero-fill beyond the source file length.
- Static validation passed: the edited Quick Waveform source reports no diagnostics, no old three-argument `clampQuickWaveformPeriod(...)` calls remain, and `git diff --check` completed without reporting patch issues.