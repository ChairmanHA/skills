# EditDialog Power Step Keyboard Commit Fix

## Goal

Fix the EditDialog power-step editor so that both the `dB` shortcut and the Enter key commit the edited value, close the soft keyboard, emit the property `editingFinished` signal, and update the dependent stop-power value.

## Observation and root cause

- Frequency step and dwell-time step declare their unit adapter types in property metadata before calling `prepareNumericKeyBoard()`.
- `powerStepProperty` only overrides its display text and manually adds a `dB` shortcut; it does not declare the `PowerStep` unit adapter type.
- Consequently, `prepareNumericKeyBoard()` creates a plain `BaseUnitAdapter`. The UI is then forced to display text such as `1dB`, but the plain adapter cannot parse the `dB` suffix.
- Both the `dB` shortcut and Enter therefore remain on the invalid-input path: no adapter `editingFinished`, no accepted keyboard close, and no property writeback.
- The existing `PowerStepUnitAdapter` already defines dB as its base unit, accepts a dB suffix case-insensitively, and converts it 1:1 to the stored step value.

## Design boundary

- Assign the existing `PowerStep` adapter type to `powerStepProperty` before its initial value is set.
- Keep the existing EditDialog-only `dB` shortcut and presentation behavior.
- Do not change the generic keyboard lifecycle, adapter factory, or absolute-power unit behavior.

## Success criteria

1. Clicking the `dB` shortcut with a valid numeric power step commits and closes the keyboard.
2. Clicking Enter with a valid numeric or `dB`-suffixed power step commits and closes the keyboard.
3. A changed value emits property `editingFinished`, runs `onBtnStepPowerChanged()`, and updates stop power.
4. Invalid input remains open and is not applied.
5. Frequency, dwell-time, start-power, and stop-power editors are unchanged.

## Verification level

`static`

## Plan

1. Declare the `PowerStep` unit type on `powerStepProperty` before setting its value.
2. Trace the adapter-to-property accepted-dialog lifecycle for both unit shortcut and Enter.
3. Run focused diff and whitespace checks; do not build or run unless requested.

## Implementation result

- Added `powerStepProperty->metadata()->setUnit("PowerStep")` before assigning the initial step value.
- `prepareNumericKeyBoard()` now creates the registered `PowerStepUnitAdapter` instead of `BaseUnitAdapter`.
- The existing `dB` shortcut is now recognized by `normalizeUnitToken()`, while Enter parses the existing `dB`-suffixed indicator through `convertToBase()`.
- A valid changed value follows the existing adapter `editingFinished` -> property value update -> accepted-dialog deferred property `editingFinished` chain; the existing `onBtnStepPowerChanged()` connection applies the edit and recalculates stop power.
- Invalid dB input still produces NaN and remains open without applying a value.

## Static verification

- Confirmed `editdialog.cpp` is included by `src/plugins/core/CMakeLists.txt`.
- Confirmed `powerstepunitadapter.cpp` is included by `src/libs/business/CMakeLists.txt` and registered under adapter type `PowerStep`.
- Confirmed both the unit shortcut and Enter reach `BaseUnitAdapter::commitInput()` with a `PowerStepUnitAdapter` receiver.
- `git diff --check -- src/plugins/core/editdialog.cpp` passes; Git only reports the repository's existing LF-to-CRLF checkout warning.
- No build or runtime test was performed, per the repository's static-verification default.
