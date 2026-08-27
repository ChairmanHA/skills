# HTRA Panel Show Regeneration Fix

## Scope

- Remove the `Panel::widgetShowed -> requestGenerateData` connection from:
  - AM
  - FM
  - PM
  - Pulse
  - Digital Ramp
  - AWGN
- Preserve generation triggered by construction, parameter/profile changes,
  device-capability changes, explicit reset, and payload rematerialization.
- Do not change Digital Modulation, DSSS, or OFDM one-time `init()` behavior.
- Do not change executor behavior in this task.

## Evidence

- Returning from minibar to MainWindow shows the current child panel again.
- Each affected panel emits `widgetShowed` from `showEvent`.
- The six affected businesses connect that signal directly to
  `requestGenerateData()`.
- Their constructors already call `resolveAndGenerate()`, while parameter,
  profile, reset, capability, and rematerialization paths also explicitly
  regenerate.
- Runtime logs show identical AM parameters and payload sizes receiving new
  payload identities, followed by `tx_clear_waveform`, download, and trigger
  on repeated panel display.
- Digital Modulation connects `widgetShowed` to a guarded one-time `init()`;
  repeated panel display therefore does not regenerate.

## Success Criteria

- Showing MainWindow with one of the six affected panels current does not
  create a new payload identity.
- Switching minibar/MainWindow while parameters and enabled selection are
  unchanged does not request a new device apply.
- Parameter edits and explicit materialization requests still generate data.
- Static search finds no affected HTRA modulation connecting `widgetShowed` to
  `requestGenerateData`.

## Verification Level

`static`. No build or runtime execution unless separately requested.

## Implementation Result

- Removed the display-triggered generation connection from all six scoped
  businesses.
- Kept each `requestGenerateData()` override for the runtime's explicit released
  payload materialization request.
- Kept constructor and all parameter/profile/reset/capability generation paths.
- Confirmed the only remaining generated-business `widgetShowed` connections
  are Digital Modulation, DSSS, and OFDM one-time `init()` calls.

## Static Verification

- `rg` finds no HTRA modulation connecting `widgetShowed` to
  `requestGenerateData`.
- The six constructors still call `resolveAndGenerate()`.
- The six public `requestGenerateData()` paths remain available.
- `git diff --check` passes; only Windows line-ending conversion warnings are
  reported.
