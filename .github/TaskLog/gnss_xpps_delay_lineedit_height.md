# GNSS XPPS Delay LineEdit Height

## Scope

- Adjust only the GNSS dialog UI sizing for `xpps` and `delayEdit`.
- Match their height with the existing read-only `QLineEdit` fields below.
- Do not change GNSS configuration, keyboard, or realtime display behavior.

## Success Criteria

- `xpps` and `delayEdit` use the same fixed 35 px height as the lower GNSS info line edits.
- The GPS plugin still uses `gpsinfodialog.ui` through its existing CMake target.

## Verification

- Verification level: static.
- Check lints/diagnostics for edited files after the UI change.
