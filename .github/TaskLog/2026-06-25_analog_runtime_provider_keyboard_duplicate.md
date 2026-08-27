# Analog Runtime Provider Keyboard Duplicate

## Scope

- User-visible issue: after license validation succeeds at runtime, newly registered Analog AM/FM/Pulse/Ramp/AWGN panels can commit a unit shortcut value, but the soft keyboard appears to stay open.
- Known good cases from user:
  - Digital modulation uses the same keyboard path and closes correctly.
  - HTRA Pulse without a license also closes correctly.
- Do not change `BaseUnitAdapter`; the user reverted that experiment and confirmed `parentObj->accept()` works in the good paths.

## Observations

- HTRA and Analog AM/FM/Pulse/Ramp/AWGN providers share the same property names, for example `Pulse_Width` and `Pulse_Period`.
- Switching providers unregisters the inactive business from `BusinessManager`, but the business object and its panel remain alive.
- Panel constructors connect shared `property->beginEditing(triggerObj)` directly to keyboard creation.
- When the licensed Analog provider is created after the HTRA provider already existed, both panels are connected to the same property edit signal.

## Inference

Clicking a button in the newly added Analog panel can emit one shared property `beginEditing` signal, and the hidden/unregistered HTRA panel may also respond by opening another keyboard. The visible keyboard can commit and close correctly while another keyboard remains, matching the symptom.

## Design

- Add a small helper that checks whether the edit trigger object belongs to a given panel/widget.
- In the duplicate provider panels, ignore `beginEditing` unless the trigger widget is inside that panel.
- Apply the guard to both Analog and HTRA AM/FM/Pulse/Ramp/AWGN panels so switching in either direction does not leave stale provider panels responding.

## Success Criteria

- A shared property edit signal opens only the keyboard for the panel that owns the clicked trigger.
- Runtime license success no longer leaves duplicate keyboards for AM/FM/Pulse/Ramp/AWGN.
- Digital modulation remains unchanged.
- No change to unit conversion or `BaseUnitAdapter::accept()` behavior.

## Verification

- Static review of provider switching and panel begin-edit signal ownership.
- `git diff --check`.
- No build/run unless requested.

## Verification Result

- Confirmed `beginEditing(triggerObj)` is emitted only by `PropertyBindingManager::bindButtonToProperty(...)`, with the clicked button as `triggerObj`.
- Confirmed Analog and HTRA AM/FM/Pulse/Ramp/AWGN property edit slots now check that `triggerObj` belongs to the receiving panel before opening a keyboard.
- Confirmed Digital modulation was not changed.
- `git diff --check` reported no whitespace errors; only existing CRLF normalization warnings for edited files.
