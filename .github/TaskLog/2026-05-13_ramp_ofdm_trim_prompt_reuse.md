# Ramp / OFDM Large Waveform Trim Prompt Reuse

## Scope

- Keep the current trim-only large-waveform baseline.
- Change Ramp and OFDM so large-waveform branches no longer use their legacy "generate this waveform" prompt wording.
- Reuse the same business-layer trim confirmation flow already used by Digital Modulation.
- Do not add the Digital default-trim checkbox to Ramp or OFDM.
- Do not add Digital-style generation-side trimmed cache state to Ramp or OFDM modulator internals in this change.

## Local Hypothesis

The shared behavior boundary is already in `AnalogPlaybackBusiness`:

- `requestGenerateAndTrim(...)` records the user's trim intent.
- `buildPlaybackExecutionContext(...)` is the common place that trims playback payloads to `MAXDOWNLOADSIZE`.

Ramp and OFDM currently diverge only at the UI/business confirmation step, where they still show older prompts and directly apply parameters after confirmation. If that confirmation is moved into a shared helper in `AnalogPlaybackBusiness`, then:

- Digital can keep its extra `m_trimLargeWaveformByDefault` option by deciding whether to auto-confirm or prompt.
- Ramp and OFDM can always prompt, then reuse the same shared trim-intent path.

## Planned Changes

1. Add a shared trim-confirmation helper to `AnalogPlaybackBusiness` that:
   - builds the common "will be trimmed before playback" dialog
   - optionally skips the dialog when a caller wants default auto-trim
   - routes confirmation through `requestGenerateAndTrim(...)`
   - supports an optional pre-apply hook for Digital's generation-side trim flag
2. Refactor `DigitalModulation` to call the shared helper instead of maintaining its own duplicate prompt/confirm flow.
3. Refactor `RampModulation` large-waveform `Span` / `Period` branches to use the shared helper and updated trim wording.
4. Refactor `OfdmModulation` large-waveform `FFT Size` / `Symbol Count` / `Guard Interval` branches to use the shared helper and updated trim wording.
5. Emit `basicParamsChanged()` on Ramp / OFDM waveform-affecting edit paths so pending trim requests can be canceled consistently when the user keeps editing.
6. Run a narrow build for the touched analog plugin slice.

## Non-Goals

- No redesign of Ramp / OFDM save/export semantics.
- No new panel option for Ramp / OFDM default trim behavior.
- No attempt to make Ramp / OFDM generate already-trimmed waveform caches.
- No reintroduction of any Generate & Stream / handover path.

## Expected Outcome

- Digital, Ramp, and OFDM all use one shared trim-confirmation path.
- Digital remains the only modulation with a default-trim toggle.
- Ramp and OFDM always prompt on large waveforms, and the prompt explicitly tells the user playback will be trimmed to fit device download memory.
