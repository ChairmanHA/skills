# 2026-06-30 Realtime RMS Power Display Implementation

## Scope

- Add a read-only RMS power display beside the existing `Level` button in `CommonPanel`.
- The existing `Level` value is treated as PEP power.
- RMS is a derived display value, not an editable setting.
- Mute / unavailable states display `--`.
- Playback RMS is calculated in each provider / generator worker thread and propagated through the existing Tx execution path.
- Programmed ARB is explicitly out of scope for this implementation.

## Decisions From User

- The new RMS display should be a separate button to the right of `Level`.
- It should look like the existing `Level` value button. Tracked stylesheet templates live under `configuration_files/*/theme*.css`; local runtime copies also exist as `configuration/theme.css` and `configuration/theme_light.css`.
- `Mute` should show `--`.
- Playback RMS calculation belongs in provider / generator workers, not in the download/apply main path.
- Playback ARB ordinary WAV should include `period` zero padding in the RMS calculation.

## Current Facts

- `src/plugins/core/commonpanel.cpp` is included by `src/plugins/core/CMakeLists.txt`.
- `CommonPanel` currently has two value buttons:
  - `m_freq`, bound to `Center`
  - `m_level`, bound to `Level`
- `m_level` already carries the `UNLEVEL` badge and remains the editable PEP setpoint.
- `TxSessionService::buildApplyRequest()` synchronously calls `IBusiness::buildPlaybackExecutionContext()`.
- `TxPipelineRuntime::requestApply()` and `uploadPlaybackWaveform()` are synchronous.
- Therefore large-vector RMS calculation must not be added to `buildPlaybackExecutionContext()`, `requestApply()`, or `uploadPlaybackWaveform()` as the normal path.
- Current shared playback short-waveform padding is `IPlaybackBusiness::normalizePlaybackPayloadForDownload()`, which repeats the full already-final payload segment.

## Target UI

Top value row becomes:

```text
Frequency        Level         RMS
1GHz             -20dBm        -27.4dBm
```

Behavior:

- `Level`: editable PEP setpoint, existing behavior unchanged.
- `RMS`: read-only display, no keyboard, no editing.
- `RMS` should use the same value-button visual style as `Level`.
- `UNLEVEL` remains on `Level`; RMS has nothing to do with it.
- Invalid, unavailable, RF-off, Mute, no-device, and pending-data states display `--`.

Implementation notes:

- Add `LabelButton *m_rmsPower = nullptr;` to `CommonPanel`.
- Create it in `CommonPanel::buildUi()` as object name `rmsPower`.
- Do not bind it to an editable property.
- Prefer `setCheckable(false)` and no click handler.
- Add `CommonPanel::setRmsPowerText(const QString &text)` or `setRmsPowerDisplay(bool valid, double dbm)`.
- Add `LabelButton#rmsPower` to both dark and light theme selectors that currently style `LabelButton#freq` and `LabelButton#level`.
- Update value-row width constants and layout stretches:
  - current wide row is `freq + level + spacing`
  - new wide row should be `freq + level + rms + 2 * spacing`
  - wide/compact thresholds may need adjustment so the added button does not force overlap.

Implemented initial widths:

- Keep `Frequency` wide max at `210`.
- Keep `Level` wide max at `150`.
- Add `RMS` wide max at `120`.
- Add RMS minimum at `80`.

Open UI tradeoff:

- If the added RMS button causes the main window to fold too early, either increase the CommonPanel wide comfort threshold or reduce `Frequency` / `Level` max widths slightly.

## Runtime State Model

Add a small metrics structure in Core, preferably near `TxProviderExecutionContext` in `txpipelinestate.h`.

Suggested shape:

```cpp
struct TxPowerMetrics {
    bool valid = false;
    double rmsOffsetFromFullScalePepDb = 0.0; // <= 0, includes IQScale / non-full-scale peak
    double peakOffsetFromFullScalePepDb = 0.0; // <= 0, current peak relative to full-scale PEP
    double paprDb = 0.0;
    quint64 complexSampleCount = 0;

    bool operator==(const TxPowerMetrics &o) const;
    bool operator!=(const TxPowerMetrics &o) const;
};
```

Add it to `TxProviderExecutionContext`:

```cpp
TxPowerMetrics powerMetrics;
```

For playback:

```text
RMS dBm = Level full-scale PEP dBm + rmsOffsetFromFullScalePepDb
```

For CW:

```text
RMS dBm = Level PEP dBm
```

For Mute:

```text
RMS = invalid / display "--"
```

Product decision:

- `Level` represents the full-scale PEP reference.
- If the final payload peak is below full scale, the actual payload PEP is lower than `Level`.
- For `IQScale < 100%`, the reduced digital amplitude lowers the actual RF peak relative to `Level`.
- Therefore RMS conversion must include both:
  - current peak relative to full-scale PEP
  - average power relative to current peak / PAPR

Equivalent display formula:

```text
RMS dBm = Level full-scale PEP dBm + 10 * log10(avgPower / fullScalePeakPower)
```

- If `IQScale = 50%` uniformly halves I/Q amplitude, `avgPower` drops by `6.02 dB`, so displayed RMS also drops by `6.02 dB`.
- `PAPR` remains useful, but RMS display must not be calculated as only `Level - PAPR`.

## RMS Formula

For interleaved signed int16 IQ:

```text
power[n] = I[n]^2 + Q[n]^2
avgPower = mean(power[n])
peakPower = max(power[n])
PAPR dB = 10 * log10(peakPower / avgPower)
peak offset from full-scale PEP = 10 * log10(peakPower / fullScalePeakPower)
RMS offset from full-scale PEP = 10 * log10(avgPower / fullScalePeakPower)
```

No `sqrt()` is required for the displayed dBm if the metric is expressed as a power ratio.

Full-scale reference:

- Use a single shared constant/helper for `fullScalePeakPower`.
- For current HTRA int16 IQ payloads, full-scale PEP is defined as the maximum complex-envelope power with both I and Q at positive full scale:

```text
fullScalePeakPower = 32767^2 + 32767^2
```

- This is a device/product convention for this repository, not a generic RF assumption.
- Evidence in current code: HTRA AM generation writes the same quantized full-scale-capable envelope value into both I and Q (`result.iqData[base] = iValue; result.iqData[base + 1] = iValue;`), so the waveform path can intentionally use both components at once.
- Do not switch to `32767^2` unless the device calibration model is explicitly changed.

Invalid cases:

- no samples
- odd word count
- `avgPower <= 0`
- `peakPower <= 0`
- `fullScalePeakPower <= 0`

These should produce `valid=false` and display `--`.

Numeric safety:

- Promote before squaring.
- Do not use 32-bit `int` for `I * I + Q * Q`.
- For 128 MiB interleaved int16 IQ:
  - complex samples: about `128 MiB / 4 = 33,554,432`
  - max per complex sample: `(-32768)^2 + (-32768)^2 = 2,147,483,648`
  - max sum: about `7.2e16`
  - signed 64-bit max: about `9.22e18`
- `uint64_t` / `int64_t` accumulation is safe for current playback download sizes.
- Convert to `double` only after accumulation for average and dB conversion.

Reusable helper:

- Add a shared helper for interleaved IQ metrics so all providers use one formula.
- Candidate location:
  - `src/plugins/core/iplaybackbusiness.h/.cpp` as a static helper, or
  - a small new Core utility header/source if this should not belong to `IPlaybackBusiness`.
- Keep it allocation-free and single-pass.

## Provider Calculation Ownership

Calculation should happen where final logical waveform payload is generated, before shared repeat-only download padding.

Preferred rule:

```text
provider-owned waveform semantics first
  scale / autoscale / trim / slice / period zeros / pulse off region
calculate and cache metrics in worker thread
copy metrics into TxProviderExecutionContext
shared normalizePlaybackPayloadForDownload repeat padding
download
```

Why before shared padding:

- `normalizePlaybackPayloadForDownload()` repeats the complete payload segment.
- Repeating a complete segment does not change RMS, PEP, or PAPR.
- Recomputing after repeat wastes time and can happen on the synchronous apply path.

Exception wording for ARB:

- ARB ordinary WAV `period` zero padding is not the shared padding.
- It is part of the user-visible playback period / duty-cycle semantics.
- RMS must include those zeros.

## Provider Checklist

### HTRA ARB Ordinary WAV

Files:

- `src/plugins/htra/arbdatagenerator.h`
- `src/plugins/htra/arbdatagenerator.cpp`
- `src/plugins/htra/arbmodulation.cpp`

Plan:

- Add cached `TxPowerMetrics` or provider-local equivalent to `ArbDataGenerator`.
- In `handleData()` ordinary WAV path, calculate metrics after:
  - autoscale / IQScale
  - `sampleOffset`
  - `samplesToUse`
  - `period` zero padding
- Efficiency option:
  - do not scan the zero tail
  - sum only copied non-zero/candidate samples
  - use `period` as the denominator because zeros are part of the waveform
  - peak is also taken from the copied region; zero tail cannot increase peak
- Expose metrics through a thread-safe getter.
- In `ArbModulationOnly::buildPlaybackExecutionContext()`, copy cached metrics into `context->powerMetrics`.
- Do not implement Programmed ARB in this pass.

### HTRA AM / FM / PM / AWGN / Pulse / Ramp / Multitone

Files:

- `src/plugins/htra/ammodulator.*`
- `src/plugins/htra/fmmodulator.*`
- `src/plugins/htra/pmmodulator.*`
- `src/plugins/htra/awgnmodulator.*`
- `src/plugins/htra/pulsemodulator.*`
- `src/plugins/htra/rampmodulator.*`
- `src/plugins/htra/multitonegenerator.*`
- corresponding `*modulation.cpp` files

Plan:

- Calculate metrics in the generator worker when final IQ is assigned to the generator cache.
- Copy cached metrics into `TxProviderExecutionContext` in each `buildPlaybackExecutionContext()`.
- Pulse must include the off / zero part of the full period.
- Ramp and AWGN should use the generated payload as-is.
- Multitone should use the post-autoscale final IQ, matching existing PAPR / average-power reasoning.

Efficiency note:

- Prefer a generic one-pass helper first.
- Only use hand-derived formulas for trivial cases if they are clearly equivalent and covered by spot checks.

### Analog Playback Providers

Files:

- `src/plugins/analog/analogplaybackbusiness.cpp`
- individual analog generator classes that already run in worker threads

Plan:

- Prefer caching metrics in the analog generators when data is generated.
- For Digital paths that already intentionally trim before generation, metrics should reflect the generated trimmed payload.
- For generic `AnalogPlaybackBusiness` fallback paths:
  - if cached metrics are available from the provider, use them
  - otherwise calculate after `trimPlaybackDataToDownloadSize()` and conversion, but before shared repeat padding
  - this fallback is acceptable for correctness but should not become the primary large-waveform path.

### QuickWaveform

Files:

- `src/plugins/quickwaveform/quickwaveformbusiness.cpp`

Plan:

- QuickWaveform may not have the same generator-worker model.
- Calculate metrics during load/convert if possible.
- If not, calculate once when preparing the capped int16 payload, before shared repeat padding.
- Avoid recalculating on every unchanged apply.

## Tx Session / Runtime Propagation

Add a display state signal owned by the Tx session layer, not by the UI widget.

Suggested structure:

```cpp
struct TxRmsPowerDisplay {
    bool valid = false;
    double rmsDbm = 0.0;
};
```

Suggested signal:

```cpp
void rmsPowerDisplayChanged(const Core::TxRmsPowerDisplay &display);
```

Owner:

- `TxSessionService` computes the display value from the effective pipeline and `TxApplyRequest`.
- `MainWindow` connects this signal to `CommonPanel`.
- `CommonPanel` only formats / displays.

Display computation:

- `Mute`: invalid.
- `FixedCw`: valid, `rmsDbm = request.context.common.level`.
- `SweepCw`:
  - FScan: valid, use fixed level.
  - LScan / MScan: open decision below.
- `FixedPlayback`: valid if `provider.powerMetrics.valid`, `rmsDbm = level + rmsOffsetFromFullScalePepDb`.
- `SweepPlayback`:
  - FScan: valid if metrics valid, use fixed level + `rmsOffsetFromFullScalePepDb`.
  - LScan / MScan: open decision below.
- `FixedStream` / `SweepStream`: invalid in this pass unless streaming metrics are later defined.
- no device / apply failure / provider data pending: invalid.

Open runtime tradeoff:

- If `TxPipelineRuntime::requestApply()` fails, the display should not keep a stale RMS value. Clear to `--` unless the failure is only "awaiting waveform" with a known pending provider state.

## Sweep Display Open Question

`LScan` and `MScan` can have multiple PEP levels, so there is no single scalar RMS power.

Recommendation for first implementation:

- Use `--` for `LScan` / `MScan` to avoid implying a single live RMS value.
- Keep FScan valid because level is constant.

Needs user confirmation.

## CommonPanel Wiring

Files:

- `src/plugins/core/commonpanel.h`
- `src/plugins/core/commonpanel.cpp`
- `src/plugins/core/mainwindow.cpp`
- `configuration_files/*/theme.css`
- `configuration_files/*/theme_light.css`
- local runtime copies: `configuration/theme.css`, `configuration/theme_light.css`

Plan:

- Add `m_rmsPower`.
- Insert after `m_level` in `m_valueRowLayout`.
- Add `setRmsPowerText()` or `setRmsPowerDisplay()`.
- Initialize as `--`.
- In `MainWindow`, connect `TxSessionService::rmsPowerDisplayChanged` to update CommonPanel.
- Also clear RMS to `--` when device closes, matching the existing `UNLEVEL` reset path.

Formatting:

- valid: reuse the same dBm display rule as the `Level` property: `PowerUnitAdapter` formats the dBm value with up to 2 decimal places and `Utils::toString()` removes trailing `.00` / `.0` before appending `dBm`.
- invalid: `--`

Translation:

- Source text should be `tr("RMS")`.
- Translation files should not be edited in this implementation unless explicitly requested.

## Success Criteria

- `CommonPanel` shows a third value button, `RMS`, to the right of `Level`.
- The RMS button visually matches the `Level` button.
- Mute, RF off, no device, unavailable provider data, and unsupported states show `--`.
- CW shows RMS equal to `Level`.
- Playback providers show RMS derived from cached worker-thread metrics.
- ARB ordinary WAV RMS includes `period` zero padding.
- Shared short-waveform repeat padding does not trigger a second RMS scan.
- Large payloads do not add a new synchronous full-vector scan to Tx apply/download.
- 64-bit accumulation prevents overflow for current 125/128 MiB playback payloads.
- Programmed ARB behavior is unchanged.

## Verification Plan

Default verification is static unless build/run is explicitly requested.

Static checks:

- Confirm all new source files are listed in the relevant `CMakeLists.txt` if a new helper file is added.
- Confirm `CommonPanel` value-row width and compact layout still have stable constraints.
- Confirm dark/light QSS both include `LabelButton#rmsPower`.
- Confirm all playback providers either pass valid cached metrics or intentionally pass invalid metrics.
- Confirm `normalizePlaybackPayloadForDownload()` remains repeat-only; if that changes later, revisit metrics timing.
- Confirm `TxProviderExecutionContext::operator==` includes power metrics so request change detection remains complete.

## Implementation Notes

- Added `Core::TxPowerMetrics` and `Core::TxRmsPowerDisplay` in `txpipelinestate.h`.
- Added the shared one-pass helper `IPlaybackBusiness::calculateInterleavedIqPowerMetrics()`.
- The helper uses `fullScalePeakPower = 32767^2 + 32767^2`, clamps `-32768` magnitude to `32767`, accumulates in `quint64`, and converts to dB after averaging.
- Added `CommonPanel::setRmsPowerDisplay()` and a read-only `LabelButton#rmsPower` placed to the right of `Level`.
- Updated dark and light theme selectors in all tracked `configuration_files/*/theme*.css` templates so `rmsPower` shares the value-button style with `freq` and `level`; local runtime `configuration/theme*.css` copies were updated the same way.
- Updated CommonPanel wide layout comfort threshold from `818` to `910` and status-bar compact threshold from `1030` to `1122`.
- Added `TxSessionService::rmsPowerDisplayChanged()` and MainWindow wiring.
- Core-managed display behavior:
  - `Mute`, stream, invalid, apply failure, pending provider data: `--`
  - fixed CW: `Level`
  - FScan CW: `Level`
  - fixed playback: `Level + rmsOffsetFromFullScalePepDb`
  - FScan playback: `Level + rmsOffsetFromFullScalePepDb`
  - LScan / MScan: `--`
- HTRA AM/FM/PM/AWGN/Pulse/Ramp/Multitone now compute and cache metrics in their generator worker loops.
- HTRA ARB ordinary WAV computes metrics in `ArbDataGenerator::handleData()` after autoscale/IQScale and slicing, scanning only `samplesToUse` while using `period` as the denominator so period zero padding is included. Programmed ARB remains out of scope and reports invalid metrics to the core playback path.
- QuickWaveform and Analog fallback playback paths compute metrics after their final int16/capped payload is prepared and before shared repeat padding, so those providers also populate RMS display.

## RMS Display Formatting Alignment - 2026-06-30

Scope:

- Align the read-only `RMS` button text with the existing `Level` button when `Level` is displayed in `dBm`.
- Do not change RMS calculation, propagation, button layout, or editable `Level` behavior.

Observation:

- `CommonPanel::setRmsPowerDisplay()` currently formats valid RMS values with `QString::number(rmsDbm, 'f', 1)`, so it always keeps one decimal place.
- `Level` is bound to the `Level` numeric property through `PropertyBindingManager::bindButtonToProperty()`.
- `Level` property metadata uses unit `Power` and no explicit decimals.
- `PowerUnitAdapter::convertFromBaseValue()` formats dBm values through `Utils::toString(convertedValue, 2) + unit`; `Utils::toString()` removes trailing zeros and a trailing decimal point by default.

Plan:

- Replace the fixed one-decimal RMS formatter with the same dBm unit-adapter path used by the `Power` unit.
- Keep invalid RMS display as `--`.

Success criteria:

- `-20.0 dBm` style numeric input displays as `-20dBm`, matching `Level`.
- Values with one or two meaningful decimals keep those decimals, for example `-27.4dBm` or `-27.35dBm`.
- RMS no longer forces exactly one decimal place.

Verification level: static.

Implementation status:

- `CommonPanel::setRmsPowerDisplay()` now formats valid RMS values through `PowerUnitAdapter::convertFromBaseValue(..., "dBm")` instead of forcing one decimal place.

Static verification:

- `git diff --check` reported no whitespace errors; it only reported the existing LF-to-CRLF working-copy warning for `src/plugins/core/commonpanel.cpp`.

## Analog Plugin Extension - 2026-06-30

Additional scope requested after the first implementation:

- Analog plugin digital-style providers should also compute/cache RMS in their modulator generation path instead of relying only on the `AnalogPlaybackBusiness` synchronous fallback.
- Add `AnalogPlaybackBusiness::currentPowerMetrics()` as a protected virtual hook.
- `buildPlaybackExecutionContext()` should prefer cached metrics when they match the untrimmed final IQ payload; if it has to trim the payload further, it should recalculate from the trimmed payload to preserve correctness.
- Add worker-side cached metrics to Analog digital-style modulators:
  - `DigitalModulator`
  - `DsssModulator`
  - `OfdmModulator`
  - `MultitoneModulator`
- Expose interleaved IQ data for DSSS/OFDM where needed so base playback can avoid converting through `QVector<float>`.
- Keep existing fallback metrics calculation for providers that still do not expose cached metrics.

Implementation status:

- `AnalogPlaybackBusiness` now consumes cached metrics through `currentPowerMetrics()` and recalculates only if it has to trim the payload.
- Digital / DSSS / OFDM / Multitone modulators now cache `TxPowerMetrics` in their worker result commit path.
- Digital / DSSS / OFDM / Multitone modulation classes now pass cached metrics to the shared playback context.

Runtime checks if requested:

- Mute: RMS shows `--`.
- Fixed CW at `-20 dBm`: RMS shows `-20.0dBm`.
- ARB ordinary WAV with `samplesToUse = period`: RMS matches full active segment.
- ARB ordinary WAV with `samplesToUse = period / 10`: RMS drops by about `10 dB` for same active peak.
- Short waveform below minimum download length: RMS unchanged before/after shared repeat padding.
- Large near-limit waveform: UI remains responsive during apply because metrics were calculated in the generator worker.

## Files Inspected

- `src/plugins/core/CMakeLists.txt`
- `src/plugins/core/commonpanel.h`
- `src/plugins/core/commonpanel.cpp`
- `src/plugins/core/mainwindow.cpp`
- `src/plugins/core/txsessionservice.h`
- `src/plugins/core/txsessionservice.cpp`
- `src/plugins/core/txpipelinestate.h`
- `src/plugins/core/txpipelineruntime.h`
- `src/plugins/core/txpipelineruntime.cpp`
- `src/plugins/core/iplaybackbusiness.cpp`
- `src/plugins/htra/arbdatagenerator.h`
- `src/plugins/htra/arbdatagenerator.cpp`
- `src/plugins/htra/arbmodulation.cpp`
- `src/plugins/htra/ammodulation.cpp`
- `src/plugins/htra/fmmodulation.cpp`
- `src/plugins/htra/pmmodulation.cpp`
- `src/plugins/htra/awgnmodulation.cpp`
- `src/plugins/htra/pulsemodulation.cpp`
- `src/plugins/htra/rampmodulation.cpp`
- `src/plugins/htra/multitonemodulation.cpp`
- `src/plugins/analog/analogplaybackbusiness.cpp`
- `src/plugins/quickwaveform/quickwaveformbusiness.cpp`
- `.github/KnowledgeBase/labelbutton_style_state_workflow.md`
- `.github/KnowledgeBase/commonpanel_level_unlevel_badge_ui.md`
- `.github/KnowledgeBase/htra_multitone_current_algorithm_and_vsg60_boundaries.md`
