# Analog Licensed Panels Soft Keyboard Close

> Superseded: this first hypothesis changed `BaseUnitAdapter` close timing. The user reverted it and confirmed `parentObj->accept()` works in the good paths. The active follow-up is `2026-06-25_analog_runtime_provider_keyboard_duplicate.md`, which targets duplicate panel listeners after runtime provider switching.

## Scope

- User-visible issue: in the licensed Analog AM/FM/Pulse/Ramp/AWGN panels, tapping a unit shortcut commits the value, but the numeric soft keyboard may remain open.
- Comparison point from user: Digital modulation does not show the issue.
- Intended change: keep the shared soft-keyboard unit-commit contract intact while making the popup close robust after adapter listeners finish syncing values.

## Observations

- Analog AM/FM/Pulse/Ramp/AWGN and Digital modulation use the same `PropertyBindingHelper::prepareNumericKeyBoard(property, triggerObj)` path for property-backed numeric buttons.
- `BaseUnitAdapter::event()` implements unit shortcut commit by converting the current text with the shortcut unit, emitting `editingFinished(realValue)`, then directly accepting or closing the parent `QDialog`.
- `PropertyBindingHelper::prepareNumericKeyBoard(...)` intentionally syncs the property immediately from `BaseUnitAdapter::editingFinished`, but defers the business-level `property->editingFinished()` until the keyboard `finished(Accepted)` signal.
- The soft-keyboard KnowledgeBase warns that the adapter signal path can synchronously touch dialogs, focus, and object lifetime, so code after adapter signal emission should avoid fragile assumptions.

## Assumption

The licensed Analog panels do not need panel-local keyboard logic. The symptom is caused by the shared unit-commit close happening in the same stack as property/business synchronization; making the close a queued dialog action after adapter listeners run should preserve Digital behavior and fix the heavier Analog path.

## Success Criteria

- Unit shortcut commit still updates the adapter/property value before keyboard `finished(Accepted)`.
- The keyboard closes automatically after a successful unit shortcut commit.
- Invalid input still stays open and selects the invalid text.
- Step edits remain immediate and do not close the keyboard.

## Verification

- Static review of `BaseUnitAdapter`, `TouchNumKeyboard`, and `PropertyBindingHelper` call chain.
- `git diff --check` after edits.
- No build/run unless requested.
