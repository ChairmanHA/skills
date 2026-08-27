# 2026-05-14 Arb move padding to provider boundary

## Background
- OrdinaryWav currently calls `Core::IPlaybackBusiness::normalizePlaybackPayloadForDownload()` inside `ArbDataGenerator::handleData()`.
- That helper encodes download/runtime minimum payload rules rather than waveform authoring semantics.
- The user wants the responsibility boundary tightened immediately and the KnowledgeBase updated to match.

## Local hypothesis
- `ArbDataGenerator` should stop at waveform semantics: parse, scale, slice by sampleOffset/samplesToUse, and zero-pad to `period`.
- The short-waveform minimum-download normalization should move to `ArbModulationOnly::buildPlaybackExecutionContext()`, because that is the provider-to-runtime payload boundary.
- This is safe because the current generated `m_data_handled` for OrdinaryWav is only consumed by `ArbModulationOnly`, which already snapshots payload into `TxProviderExecutionContext`.

## Planned edits
1. Remove the shared playback normalization call and now-unused include from `src/plugins/htra/arbdatagenerator.cpp`.
2. Add the shared playback normalization call to `src/plugins/htra/arbmodulation.cpp` after copying the current payload into a local `QVector<int16_t>`.
3. Update `.github/KnowledgeBase/arb_mode_summary.md` so it states that:
   - generator owns waveform semantics only;
   - provider boundary owns the minimum-download normalization;
   - OrdinaryWav still shares the same canonical helper as other playback providers.

## Validation
- Run file-level diagnostics on the touched files.
- Grep the touched slice to ensure the normalization call moved from generator to provider boundary.
- No build/run requested.