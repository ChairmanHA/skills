# Analog Basic Modulation Switch Cleanup

## Scope

- Remove the Analog-side basic modulation provider replacement path for `AM`, `FM`, `Pulse`, `Digital Ramp`, and `AWGN`.
- Keep HTRA as the direct owner of those basic modulation providers.
- Keep Analog device license validation and remaining Analog digital-family business visibility logic unless a direct dependency on the removed basic provider switch requires adjustment.
- Remove redundant HTRA panel guards that only existed to protect hidden same-name Analog/HTRA panels from shared property edit signals.

## Observations

- `src/plugins/analog/analogmodulationplugin.cpp` currently lazily creates Analog basic modulation businesses and switches them against same-name HTRA businesses on `LicenseValidationState`.
- `src/plugins/htra/plugin.cpp` already creates and registers HTRA `AM`, `FM`, `Pulse`, `Digital Ramp`, and `AWGN` providers during plugin initialization.
- `.github/KnowledgeBase/analog_htra_provider_lifecycle_and_duplicate_panel_guard.md` documents the old double-provider lifecycle and explicitly describes how to remove it once HTRA fully replaces Analog AM-like providers.

## Success Criteria

- Analog no longer includes, creates, registers, unregisters, or switches AM/FM/Pulse/Digital Ramp/AWGN business objects.
- No HTRA provider pointer cache or same-name provider switching logic remains in `AnalogModulationPlugin`.
- HTRA basic modulation panels no longer carry owner-trigger guards whose only purpose was stale hidden provider protection.
- Analog CMake no longer lists AM/FM/Pulse/Ramp/AWGN implementation files, and those removed Analog basic modulation source files are deleted from `src/plugins/analog/`.
- Static search shows no remaining references to the removed Analog basic provider switch symbols.

## Verification

- Static inspection and targeted search.
- Read lints on edited source files.
- No build/run requested for this task.
