# HTRA four unlock warning and statusbar plan

## Goal
- Extend HTRA realtime unlock warning handling from two legacy codes to the current four API warning codes 15/16/17/18.
- Keep these unlock warnings on the dedicated status-bar indicator path instead of the generic rolling warning queue.
- Change the status-bar indicator text from fixed `RLO` to dynamic `RLO15`/`RLO16`/`RLO17`/`RLO18`.
- Remove duplicated stale helper logic in Core that still hardcodes the old two-code model.

## Local hypothesis
- `FancyDevice::updateRealTimeStatus()` remains the owning polling point, so expanding its unlock-code filter is enough to feed the existing realtime status pipeline.
- `DeviceInfoWidget` already owns the responsive status-bar layout, so the indicator should carry the concrete warning code there instead of introducing controller-side state.
- The existing width-gated layout can be preserved by reserving width for the longest indicator text `RLO18`.

## Planned changes
- Update HTRA unlock warning classification and warning text mapping to cover API codes 15/16/17/18.
- Replace `DeviceInfoWidget`'s boolean RLO state with an integer warning-code state and compute indicator text from that code.
- Reserve indicator width for `RLO18` and refresh the label text whenever realtime status changes.
- Delete the unused `MainWindowDeviceController` helper that still recognizes only 17/18.

## Validation
- No build requested.
- Run static diagnostics on touched files after editing to catch local syntax issues.