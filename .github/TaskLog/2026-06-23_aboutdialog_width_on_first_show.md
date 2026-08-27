# AboutDialog Width On First Show

## Scope

- Fix About dialog occasionally showing truncated value text (UID64, RF/USB power) on first open.
- Root cause: label text is updated in `showEvent` before `Controls::Dialog::adjustSize()`, but QLabel size hints may still reflect placeholder text until the layout is reactivated; power values can also arrive asynchronously after the first size pass.

## Success Criteria

- First open sizes wide enough for longest current value labels.
- Reopen behavior unchanged.
- RF Power row reserves width for two-digit wattage (e.g. `11.211W`, up to `99.999W`) using the actual label font plus a 24px safety margin so the trailing `W` is not clipped.
- Dialog width calculation accounts for effective grid spacing and applies the computed minimum width to the top-level dialog, not only the content widget.
- Real-time power text updates do not call `updateContentLayout()` (width is pre-reserved).

## Verification

- Static review of `aboutdialog.cpp` show/update paths.
