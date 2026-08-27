# Center Minimum And Fractional Step

Date: 2026-07-15

## Scope

- Set the shared `Center` property minimum to `9 kHz` (`9000 Hz`).
- Keep the Center step editable and allow fractional frequency steps such as `5.1`
  in the active display unit.
- Preserve the existing device/API authoritative writeback path.

Out of scope:

- Changing the Center default value or default `1 MHz` step.
- Reworking the existing Center output-frequency resolution policy.
- Adding separate behavior above or below `500 MHz` for the step value.

## Evidence And Assumptions

### Observations

- `CoreRuntimeServices::initializePropertySchema()` creates `Center` as
  `NumericProperty<double>` with a base unit of Hz, a default value of `1 GHz`, and
  a default step of `1 MHz`.
- `CommonPanel` and minibar controls use the same shared property and numeric keyboard
  path.
- `PropertyMetadata::setMinValue()` and `setStep()` both store `double` values.
- The editable step currently reuses the main property's unit adapter. For `Center`,
  that is `OutputFrequencyUnitAdapter`, whose output-value resolution policy therefore
  also quantizes the step value according to the step's own magnitude.
- The ordinary `FrequencyUnitAdapter` parses and stores frequency values as `double`
  without applying the Center output-resolution policy.

### Inference

- `9k` means `9 kHz`, represented as `9E3` in the property's Hz base unit.
- The step is a user-selected delta, not an output Center value. It should use ordinary
  floating-point frequency parsing; the resulting Center is still normalized and
  corrected by the existing main adapter/API writeback flow.

## Design

1. Set `Center` metadata minimum to `9E3` during property schema initialization.
2. When an editable numeric step belongs to `OutputFrequencyUnitAdapter`, construct
   its step editor with `FrequencyUnitAdapter` instead.
3. Leave other unit adapters, step editors, Center resolution handling, and API
   writeback unchanged.

## Success Criteria

- Center keyboard input and step-down operations cannot produce a value below
  `9000 Hz`.
- The Center step editor accepts and retains a fractional value such as `5.1` in its
  displayed frequency unit.
- Applying that step still updates the shared Center property through the existing
  keyboard/property path, and the existing device/API response remains authoritative.
- Non-Center frequency fields and power step editing retain their current behavior.

## Verification Level

- `static`: inspect active CMake inclusion, focused source assertions, changed-file
  diff review, and `git diff --check`.
- No build or runtime verification requested.

## Static Verification Performed

- Confirmed the shared `Center` property remains `NumericProperty<double>` and now
  initializes `minValue` to `9E3` in the active Core plugin source.
- Confirmed only an editable `OutputFrequency` step substitutes the ordinary
  `FrequencyUnitAdapter`; power steps and other unit adapters retain their existing
  construction paths.
- Traced fractional step parsing through `FrequencyUnitAdapter::convertToBase()` to
  the `double` `StepController::singleStep()`, then through the existing Center
  property edit/API writeback path.
- Confirmed `coreruntimeservices.cpp`, `utils.cpp`, and `frequencyunitadapter.cpp`
  remain included by their active CMake targets.
- Focused source assertions and `git diff --check` pass. No build or runtime test was
  run, following the repository's default static-analysis workflow.
