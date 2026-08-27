# HTRA AWGN fallback provider switch plan

## Scope

Add an HTRA-owned AWGN provider that uses local waveform generation when the Analog license is not available, while preserving the existing Analog AWGN provider when the license is available.

The user-facing AWGN UI and interaction should match the existing `src/plugins/analog/awgnpanel.*` behavior:

- Same entry name: `AWGN`
- Same full name: `Additive White Gaussian Noise`
- Same properties: `Awgn_Bandwith`, `Awgn_Length`
- Same enable switch / save-data interaction
- Same save target naming convention: `AWGN_yyyyMMdd_HHmmss.wav`

## Current Facts

- HTRA already owns local fallback businesses for AM, FM, and Digital Ramp.
- Analog plugin currently switches AM/FM/Ramp providers according to `DeviceParamsManager::LicenseValidationState`.
- Analog AWGN is currently directly registered as a license-controlled business, so adding an HTRA `AWGN` without changing Analog would produce duplicate AWGN entries when licensed.
- Current AWGN parameter semantics are documented as:
  - `Bandwith`: complex baseband total bandwidth
  - `Length`: waveform length, seconds
  - `sampleRate = clamp(1.25 * Bandwith)`

## Design

1. Add HTRA local AWGN files:
   - `awgnpanel.*` copied behaviorally from Analog AWGN panel
   - `awgnmodulator.*` with local fixed-seed, band-limited complex Gaussian AWGN generation
   - `awgnmodulation.*` mirroring HTRA AM/FM/Ramp `IPlaybackBusiness` integration

2. Add HTRA plugin registration:
   - Ensure `Awgn_Bandwith` / `Awgn_Length` properties exist before constructing the panel.
   - Register the HTRA AWGN business alongside AM/FM/Ramp.
   - Unregister it during HTRA shutdown if still registered.

3. Change Analog provider management:
   - Remove direct `registerLicenseControlledBusiness(new Analog::AwgnModulation)`.
   - Add `ensureAnalogAwgnBusiness()` and `switchAwgnProvider(...)`.
   - On `Licensed`, switch AWGN entry to Analog AWGN.
   - On `Unknown` or `Unlicensed`, switch AWGN entry to HTRA AWGN.

## Local AWGN Algorithm

The first HTRA fallback implementation used a constant-envelope LFM/chirp surrogate based on an incorrect VSG60 reference file. The corrected reference file is `data/AWGN40M.csv` with VSG60 parameters:

```text
Bandwidth = 40 MHz
Length    = 10 ms
Seed      = 23
```

The corrected local AWGN implementation must follow `.github/TaskLog/2026-06-17_awgn_vsg60_algorithm_replacement_plan.md`:

```text
sampleRate = clamp(1.25 * Bandwith)
N = round(sampleRate * Length)
seed = 23 fixed internally for now
```

Generate seed-driven complex Gaussian noise, band-limit it to the complex baseband target band, then normalize to the observed VSG60 component RMS scale before int16 IQ quantization:

```text
uI[n], uQ[n] ~ N(0, 1)
x[n] = FIR_lowpass(uI[n] + j*uQ[n])
targetComponentRms ~= 0.2 * 32767
I/Q = round(scale * real/imag(x[n]))
```

Do not keep the old LFM/chirp pieces:

- no `fStart`, `chirpRate`, or phase accumulator sweep
- no 4-sample `sin^2` chirp edge taper
- no constant-envelope full-scale output
- no generated zero tail

Seed is intentionally not exposed in the UI in this step. The panel already hides the seed row; the generator should use fixed `23` so the same parameters are reproducible and remain aligned with the current supplier reference setting.

### 2026-06-18 Correction Scope

Scope:

- Replace `src/plugins/htra/awgnmodulator.cpp`'s local chirp generator with fixed-seed, band-limited complex Gaussian AWGN.
- Keep the existing HTRA AWGN public profile unchanged: `Bandwith` and `Length` only.
- Keep `AwgnPanel` seed hidden; do not add an `Awgn_Seed` property.
- Preserve existing playback/save integration and provider switching.

Verification level: `static`.

Static success criteria:

- `src/plugins/htra/awgnmodulator.cpp` contains no chirp-phase generation path.
- HTRA AWGN still calculates `sampleRate = clamp(1.25 * Bandwith)`.
- `Bandwith = 40 MHz`, `Length = 10 ms` still maps to `500000` complex samples at `50 Msps`.
- Generated IQ is non-constant-envelope, zero-mean-ish complex Gaussian noise after filtering and RMS scaling.
- Same input parameters regenerate the same IQ sequence because seed is fixed to `23`.
- `src/plugins/htra/CMakeLists.txt` still includes the HTRA AWGN files.

## Verification Level

Static.

No build/run is requested in this task. The static check should confirm:

- New HTRA files are included by `src/plugins/htra/CMakeLists.txt`.
- HTRA AWGN uses `Core::IPlaybackBusiness` and does not depend on Analog license-gated base classes.
- Analog plugin no longer directly registers Analog AWGN as a license-controlled business.
- Provider switch preserves selected/current AWGN entry across swaps, matching AM/FM/Ramp behavior.

## Implementation Notes

Implemented files:

- `src/plugins/htra/awgnpanel.h`
- `src/plugins/htra/awgnpanel.cpp`
- `src/plugins/htra/awgnpanel.ui`
- `src/plugins/htra/awgnmodulator.h`
- `src/plugins/htra/awgnmodulator.cpp`
- `src/plugins/htra/awgnmodulation.h`
- `src/plugins/htra/awgnmodulation.cpp`

Updated files:

- `src/plugins/htra/CMakeLists.txt`
- `src/plugins/htra/plugin.h`
- `src/plugins/htra/plugin.cpp`
- `src/plugins/analog/analogmodulationplugin.h`
- `src/plugins/analog/analogmodulationplugin.cpp`

Static verification performed:

- Confirmed HTRA AWGN source files are listed in `src/plugins/htra/CMakeLists.txt`.
- Confirmed HTRA fallback does not call `GenerateAWGNWaveform`.
- Confirmed Analog no longer directly registers `new Analog::AwgnModulation` through `registerLicenseControlledBusiness(...)`.
- Confirmed Analog now creates `Analog::AwgnModulation` only through `ensureAnalogAwgnBusiness()` and switches it through `switchAwgnProvider(...)`.
- Ran whitespace checks on the new HTRA AWGN files.
- Ran `git diff --check` for the touched tracked files; it reported only existing CRLF-normalization warnings from Git.

Build verification was not run because this task requested implementation and the repository default is static verification unless build/run is explicitly requested.

## 2026-06-18 AWGN Algorithm Correction Notes

Applied correction:

- Replaced the HTRA fallback AWGN generator's incorrect chirp surrogate with fixed-seed, band-limited complex Gaussian noise generation.
- Kept seed internal and fixed to `23`; no `Awgn_Seed` property or UI exposure was added.
- Kept `sampleRate = clamp(1.25 * Bandwith)` and `N = round(sampleRate * Length)`.
- Added deterministic Gaussian generation using `std::mt19937(23)` plus an explicit Box-Muller transform.
- Added a Blackman-windowed low-pass FIR at `Bandwith / 2`, with RMS normalization to about `0.2 * 32767` per I/Q component before int16 quantization.
- Preserved HTRA AWGN playback/save/provider integration.

Static verification performed for the correction:

- Confirmed `src/plugins/htra/awgnmodulator.cpp` no longer contains the old chirp symbols or flow (`fStart`, `chirpRate`, `currentFreq`, `phase` accumulator sweep, edge taper helpers).
- Confirmed fixed `kFixedSeed = 23` is used for both the measurement pass and the quantization pass, making repeated generation deterministic for the same parameters.
- Confirmed `src/plugins/htra/CMakeLists.txt` already includes the HTRA AWGN source files.
- Ran `git diff --check` for `src/plugins/htra/awgnmodulator.cpp`; it reported no whitespace errors, only the repository's existing CRLF-normalization warning.

Build verification was not run because the active repository default is static verification unless build/run is explicitly requested.
