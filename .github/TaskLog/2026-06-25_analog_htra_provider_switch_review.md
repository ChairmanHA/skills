# Analog / HTRA provider switch review

## Scope

Review and fix the merged AM / FM / Pulse / Digital Ramp / AWGN provider selection.

Goal:

- When Analog license state is `Licensed`, the active registered provider for each of these five businesses is the Analog vendor implementation.
- When license state is `Unknown` or `Unlicensed`, the active registered provider is the HTRA implementation.
- Avoid leaving both same-name providers registered at the same time, because `BusinessManager::getBusinessByName()` is name-based and not provider-aware.

## Observations

- `analog_device_license_gating.md` says Analog license state is `Unknown / Licensed / Unlicensed`; only `Licensed` should enable vendor waveform generation.
- `analog.json` and `htra.json` both depend only on `Core`; current plugin manager may initialize Analog before HTRA because it sorts the post-Core load queue by plugin name.
- `BusinessManager` stores all businesses in a `QSet` and `getBusinessByName()` returns the first same-name match from that unordered set.
- AM, FM, Pulse, Digital Ramp, and AWGN have identical `name()` values in Analog and HTRA.

## Design

- Add a small ordered read-only `BusinessManager` query for entry-registered businesses.
- In `AnalogModulationPlugin`, resolve HTRA fallback providers from the ordered registered list while explicitly excluding the Analog provider pointer.
- Preserve HTRA pointers once discovered, even when the fallback provider is temporarily unregistered.
- Switch provider registrations through a common path that removes the inactive same-name provider before/while registering the target provider.

## Verification

Level: static.

Checks:

- Confirm CMake includes all modified source files.
- Inspect static control flow for all five providers:
  - `Licensed` selects Analog target.
  - `Unknown` and `Unlicensed` select HTRA target.
  - inactive alternate provider is unregistered if present.
- Run targeted text search to ensure old unordered `getBusinessByName()` provider selection is gone from Analog switching logic.

## Verification Result

- Confirmed `src/plugins/core/CMakeLists.txt` includes `businessmanager.cpp/.h`.
- Confirmed `src/plugins/analog/CMakeLists.txt` includes `analogmodulationplugin.cpp/.h`.
- Confirmed Analog provider selection now uses one shared `Licensed` branch for all five providers.
- Confirmed old unordered `getBusinessByName(QStringLiteral("AM/FM/Pulse/Digital Ramp/AWGN"))` selection is no longer present in `AnalogModulationPlugin`.
- `git diff --check` reported no whitespace errors; only existing CRLF normalization warnings for edited files.
