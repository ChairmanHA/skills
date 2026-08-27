# 2026-06-30 HTRA RMS Power Multitone/Pulse Principle Note

## Scope

- Produce a durable principle note for the current RMS power calculation.
- Focus only on HTRA `multitone` and `pulse`.
- Explain whether the current RMS display is mathematically correct under the current PEP/full-scale conventions.
- No code change, no build, no runtime verification.

## Assumptions And Evidence

Observation:

- Shared RMS metrics use final interleaved int16 IQ samples.
- The shared full-scale PEP reference is `32767^2 + 32767^2`.
- `TxSessionService` displays playback RMS as `Level + rmsOffsetFromFullScalePepDb`.
- HTRA multitone currently computes metrics from the final generated IQ after its existing AutoScale path.
- HTRA multitone still normalizes by `componentPeak = max(|I|, |Q|)`, so a default two-tone waveform can peak at `(32767, 0)` rather than `(32767, 32767)`.
- HTRA pulse now defines the full pulse level as `(32767, 32767)` and computes metrics from the final generated period, including the off portion.

Inference:

- Pulse is aligned with the shared full-scale PEP reference.
- Multitone's RMS calculation is correct for the final IQ data, but its generator full-scale convention is not yet aligned with the shared `(32767, 32767)` PEP reference.
- Therefore multitone may show a lower RMS than a local `(32767, 0)` PEP convention would imply; that is a generator/reference convention issue, not a bug in the shared RMS helper.

## Success Criteria

- Add a KnowledgeBase document explaining the shared formula.
- Explain pulse 100% duty and 50% duty behavior from first principles.
- Explain multitone `Count = 2`, `PEP = 0 dBm`, `RMS = -6 dBm` from first principles.
- Clearly separate calculation correctness from provider full-scale convention.
- Update `.github/KnowledgeBase/Index.md` with an entry for the new document.

## Verification Level

Static documentation review only.

## Files Inspected

- `.github/TaskLog/2026-06-30_realtime_rms_power_display_design.md`
- `.github/KnowledgeBase/Index.md`
- `.github/KnowledgeBase/htra_multitone_current_algorithm_and_vsg60_boundaries.md`
- `src/plugins/core/iplaybackbusiness.cpp`
- `src/plugins/core/txsessionservice.cpp`
- `src/plugins/htra/multitonegenerator.cpp`
- `src/plugins/htra/multitonemodulation.cpp`
- `src/plugins/htra/pulsemodulator.cpp`
- `src/plugins/htra/pulsemodulation.cpp`
