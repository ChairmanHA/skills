# Digital Modulation 400M Sample Rate

## Scope

- Raise the Digital Modulation device sample-rate ceiling to 400 MSps.
- Allow `oversample = 2` with `Rb = 200 MSps`, producing and downloading a 400 MSps digital modulation waveform.
- Keep the change local to Digital Modulation; do not change the global analog waveform or Streaming sample-rate ranges.

## Findings

- `digitalmodulation.cpp`, `digitalmodulator.cpp`, and `packing.cpp` are included by `src/plugins/analog/CMakeLists.txt`.
- Digital Modulation currently derives sample rate through `calculateDigitalSampleRate(...)`.
- The Digital Modulation configuration path, FSK hardware budget, and oversample option filtering currently reuse the global `DATA_SAMPLE_RATE_MAX = 125e6`.
- The playback apply path carries `DigitalModulator::sampleRate()` through `AnalogPlaybackBusiness::buildPlaybackExecutionContext(...)` into `TxPipelineRuntime`, where it is used for both waveform download and playback profile configuration.

## Plan

1. Add a Digital Modulation specific max sample-rate constant of 400 MSps.
2. Use the Digital-specific range in Digital Modulation packing, sample-rate calculation, FSK budget normalization, and oversample option filtering.
3. Preserve the existing minimum sample rate and global limits for other waveform types.
4. Update the waveform constraints KnowledgeBase entry for the Digital Modulation exception.

## Success Criteria

- `Rb = 200e6` with `sps = 2` remains configured as `Fs = 400e6`.
- `oversampleOptions()` includes `2` at `Rb = 200e6` and excludes oversamples that would exceed 400 MSps.
- Non-Digital waveform types continue to use `DATA_SAMPLE_RATE_MAX`.
- The documented Digital Modulation range matches the code.

## Verification Level

- `static`
- No build or runtime verification unless explicitly requested.

## Result

- Added `Analog::DIGITAL_SAMPLE_RATE_MAX = 400e6`.
- Updated Digital Modulation packing to use the Digital-specific range for normal sample-rate clamp, FSK hardware budget, FSK sample-rate selection, and max-rate fallback.
- Updated `DigitalModulator::oversampleOptions()` filtering so `Rb = 200e6` allows `oversample = 2` and rejects oversamples above 400 MSps.
- Updated `Waveform_Parameters_Constraints.md` to document the Digital Modulation 400 MSps exception while preserving the global 125 MSps and Streaming 62.5 MSps rules.
- Static linter check reported no diagnostics on the edited files.
