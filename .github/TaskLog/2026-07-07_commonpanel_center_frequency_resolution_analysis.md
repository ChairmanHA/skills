# CommonPanel Center Frequency Resolution Analysis

Date: 2026-07-07

## Scope

Static analysis for the `CommonPanel` `Frequency` / `Center` control and the matching minibar frequency buttons.

Requested behavior:

- `freq < 500 MHz`: frequency resolution is `1 Hz`.
- `500 MHz <= fout <= 6 GHz`: frequency resolution is `0.1 Hz`.
- The change affects soft keyboard input and UI display.
- Minibar frequency controls should follow the same rule.

Out of scope unless confirmed separately:

- Step sweep `StartFreq`, `StopFreq`, and `FreqStep`.
- Reference clock frequency.
- Device capability min/max validation beyond the stated resolution rule.

## Static Evidence

- `src/plugins/core/coreruntimeservices.cpp` creates the shared `Center` property as `NumericProperty<double>` with `Unit = "Frequency"`, `Step = 1E6`, and `STEPEDITABLE = true`.
- `src/plugins/core/commonpanel.cpp` binds `m_freq` directly to the shared `Center` property and opens `PropertyBindingHelper::prepareNumericKeyBoard(centerProperty, triggerObj)`.
- `src/plugins/core/minibarwindow.cpp` binds both collapsed and expanded frequency buttons to the same `Center` property and uses the same `prepareNumericKeyBoard(...)` path.
- `PropertyBindingManager::bindButtonToProperty(...)` consumes `metadata()->displayText()`; the UI layer does not format frequency by itself.
- `NumericProperty<T>::updateDisplayText()` creates an adapter from `metadata()->unit()` and writes the adapter text back to `metadata()->displayText()`.
- `FrequencyUnitAdapter` currently formats by display unit with default precision equivalent to `1 Hz` for GHz/MHz/kHz display, but not `0.1 Hz` for `>= 500 MHz` values.
- `BaseUnitAdapter` / `StepController` can carry values through as `double`; display rounding alone would not guarantee true value resolution.

## Assumptions

- `freq` / `fout` refers to the shared output carrier frequency property named `Center`.
- The requested "resolution" means both user-visible precision and committed numeric value granularity, not only label formatting.
- The `6 GHz` upper bound describes the high-frequency resolution range. Adding hard max-value validation is a separate behavior change unless the user confirms it.

## Minimal Design Direction

Prefer one shared `Center`-property solution instead of separate `CommonPanel` and `MiniBarWindow` UI formatting.

The narrowest robust implementation is:

1. Mark only the shared `Center` property with a carrier/output frequency resolution policy in `CoreRuntimeServices::initializePropertySchema()`.
2. Teach the frequency adapter path to honor that policy when formatting display text and when committing keyboard values.
3. Ensure the same policy is applied in both keyboard creation and `NumericProperty::updateDisplayText()`, because the keyboard and UI label are created through different adapter instances.
4. If strict step-key resolution is required, add a small adapter normalization hook used by `BaseUnitAdapter::updateValueAndInputText()` so `StepController` output is snapped before it is emitted back to the property.

## Success Criteria

Static success:

- `CommonPanel` and minibar frequency labels both derive from the same `Center` `displayText`.
- Opening either `CommonPanel` frequency or minibar frequency uses a keyboard adapter with the same resolution policy.
- Values below `500 MHz` display/commit on `1 Hz` granularity.
- Values at or above `500 MHz` display/commit on `0.1 Hz` granularity.
- Existing frequency fields that are not `Center` are not changed unless explicitly opted in.

Suggested verification level:

- Static first.
- If implementation is requested, run focused static checks. Build/runtime verification only if explicitly requested.

## Implementation Plan

User confirmed implementing `OutputFrequencyUnitAdapter` and explicitly leaving step-key quantization for later.

Planned narrow changes:

1. Add `OutputFrequencyUnitAdapter` under `src/libs/business/`.
   - Derive from `FrequencyUnitAdapter`.
   - Register adapter type `"OutputFrequency"`.
   - On keyboard text commit, parse with normal frequency units and snap the returned Hz value to:
     - `1.0 Hz` when `abs(value) < 500e6`
     - `0.1 Hz` when `abs(value) >= 500e6`
   - On display, format with enough digits for the active display unit:
     - `1 Hz`: GHz 9, MHz 6, kHz 3, Hz 0
     - `0.1 Hz`: GHz 10, MHz 7, kHz 4, Hz 1
2. Add the new files to `src/libs/business/CMakeLists.txt`.
3. Change the shared `Center` property unit in `CoreRuntimeServices::initializePropertySchema()` from `"Frequency"` to `"OutputFrequency"`.
4. Treat `"OutputFrequency"` as a frequency-like unit in `PropertyBindingHelper::shouldNormalizeCorrectedUnit(...)`, preserving the existing keyboard correction/unit recovery behavior.

Expected binding behavior after implementation:

- `CommonPanel::m_freq` and minibar frequency buttons stay unchanged because they already consume `Center` `displayText`.
- `prepareNumericKeyBoard(...)` automatically creates the new adapter through `UnitAdapterFactory` because it reads `Center` metadata unit.
- Other `"Frequency"` fields keep the existing adapter and existing display/commit behavior.

Known follow-up:

- Step up/down and editable step values are not treated as part of this change. The default `Center` step remains `1 MHz`, which is already aligned to both requested grids.
