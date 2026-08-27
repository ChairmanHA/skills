# 2026-05-18 Multitone Modulator Configuration Unification

## Background
- Current Analog Multitone duplicates `Multitone_Configuraion` in `multitonemodulation.cpp` and `multitonemodulator.cpp`.
- Other analog/digital modulators keep UI/property layer thin: UI only forwards user input to modulator setters; modulator owns normalization, dependent-field writeback, and signal emission.

## Goal
- Move Analog Multitone to the same pattern.
- `multitonemodulation.cpp` should stop calling `Multitone_Configuraion` directly.
- `MultitoneModulator` should remain the single owner of parameter normalization and request generation.

## Design
1. Keep property bindings in `multitonemodulation.cpp` as:
   - property value <- changed signals from modulator
   - `editingFinished` -> corresponding setter on modulator
2. Keep `Multitone_Configuraion` only in modulator-owned paths:
   - `restoreSettings`
   - `configurationAndGenerateData`
   - reset/default restoration path through deinit + profile application
3. Ensure dependent parameter writeback continues to happen through `param2Profile(...)` calling setters while `m_isResetting` is true, so normalized values still emit the relevant changed signals without recursively scheduling generation.
4. After first edit, run file diagnostics first; if clean, run a narrow build for the touched target/file slice if practical.

## Expected Outcome
- No duplicate configuration logic in UI layer.
- Count/freqSpacing cross-clamp updates propagate only from modulator signals.
- Future Multitone parameter growth stays inside modulator instead of spreading into panel/business glue.
