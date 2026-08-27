# 2026-08-18 Fix Neutral Updater Restart

## Scope

- Fix the post-update restart lookup for the neutral package executable.
- Keep the existing firmware update, package copy, and non-elevated Windows restart flows unchanged.

## Observation

- The neutral package builds the main application as `bin/VSG.exe` on Windows and `bin/VSG` on Linux.
- Package copy completes into the expected installation root.
- Maintenance searches the copied `bin` directory, but its application-name filters do not include the neutral executable name.
- The same directory also contains `VSGMiniBar.exe`, so a broad `VSG*` filter would not identify the main application precisely.

## Design

1. Add exact `VSG.exe` and `VSG` candidates to the maintenance application-name filters.
2. Do not add a wildcard candidate that could select `VSGMiniBar`.
3. Preserve all existing branded and localized executable candidates.

## Verification Level

- Static only. Do not build or run unless explicitly requested.

## Success Criteria

- Maintenance can resolve `VSG/bin/VSG.exe` after a Windows neutral-package copy.
- Maintenance can resolve `VSG/bin/VSG` after a Linux neutral-package copy.
- `VSGMiniBar` cannot match either new candidate.
- Existing `VectorCore`, `SGStudio`, `FSStudio`, and Russian application lookup behavior remains unchanged.

