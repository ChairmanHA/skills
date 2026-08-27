# Revert HTRA Complex-IQ PEP Experiment

## Scope

User clarified that the current PEP normalization discussion should stay focused on real-IQ waveforms such as HTRA AM and Pulse. Complex-IQ waveforms need a separate follow-up design before changing their amplitude semantics.

This task reverts the previous `32367` / overfull complex-gain experiment from:

- `src/plugins/htra/fmmodulator.cpp`
- `src/plugins/htra/rampmodulator.cpp`
- `src/plugins/htra/multitonegenerator.cpp`

Do not change the latest AM / Pulse `Q = I` behavior in this task.

## Success Criteria

1. HTRA FM returns to direct `I = 32767*cos(phase)`, `Q = 32767*sin(phase)` quantization.
2. HTRA Ramp returns to direct `gain*cos(phase)` / `gain*sin(phase)` full-scale quantization.
3. HTRA Multitone returns to quantizing the autoscaled complex sample directly.
4. HTRA FM / Ramp / Multitone no longer contain `32367`, `kOverfull*`, `writeOverfull*`, or `applyOverfull*`.
5. No build/run verification; static diff and whitespace check only.

## Verification

- `rg` for old overfull symbols in the three reverted files.
- `git diff --check` for the three reverted files and this TaskLog.

## Result

- FM now quantizes `32767*cos(phase)` and `32767*sin(phase)` directly.
- Ramp now quantizes `gain*cos(phase)` and `gain*sin(phase)` through `quantizeFullScale(...)`; the one-sample active segment is back to `32767,0`.
- Multitone now quantizes the autoscaled complex sample directly again.
- `rg -n "32367|kOverfull|writeOverfull|applyOverfull|quantizeOverfull"` found no matches in the three reverted files.
- `git diff --check` passed for the three reverted files and this TaskLog; only LF/CRLF normalization warnings were reported for the C++ files.
