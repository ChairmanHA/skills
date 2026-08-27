# Updater lowercase executable packaging fix

## Scope

- Update the active updater package parser to use the current lowercase updater executable names only.
- Update the CMake updater file staging rule so Linux packages preserve executable permission for `updater`.
- Make the generated build-tree `updater/` directory mirror the current source directory so incremental packaging does not carry stale old-name files.
- Refresh the updater KnowledgeBase notes that still describe the old `Updater_*` contract.

## Assumptions And Evidence

- Observation: `updater_files/windows/` now contains `updater.exe`.
- Observation: `updater_files/linux_aarch64/` and `updater_files/linux_x86/` now contain `updater`.
- Observation: `PacketSpec::findUpdater()` currently searches `updater/Updater_*`, so current packages are not considered valid.
- Observation: `updater_files/CMakeLists.txt` only applies execute permission to Linux files matching `^Updater_`.
- Assumption: old `Updater_*` packages no longer need compatibility, per user instruction.

## Design

- Windows package parsing should look for exactly `updater/updater.exe`.
- Linux package parsing should look for exactly `updater/updater`.
- Linux staging should apply executable permissions only to the exact lowercase `updater` file.
- The generated build-tree updater directory should be cleared before copying current updater assets.
- No fallback to old uppercase names will be added.

## Verification Level

- static

## Success Criteria

- `PacketSpec::findUpdater()` no longer references `Updater_*`.
- `updater_files/CMakeLists.txt` no longer grants execute permission based on `^Updater_`.
- Reused build trees cannot keep deleted old-name updater files in the generated `updater/` staging directory.
- The durable updater documentation reflects the lowercase executable naming contract.

## Implementation Notes

- `PacketSpec::findUpdater()` now searches `updater.exe` on Windows and `updater` on Linux.
- `updater_files/CMakeLists.txt` now clears `${CMAKE_BINARY_DIR}/updater` before copying current updater assets.
- Linux staging now applies executable permissions only when the copied file name is exactly `updater`.
- `updater_firmware_update_mechanism.md` and `updater_mechanism_gap_and_remediation.md` now describe the lowercase exact-name contract.

## Static Verification

- `rg -n 'Updater_\*|\^Updater_|Updater_Win|Updater_Linux' src/plugins/updater updater_files scripts .github/KnowledgeBase/updater_firmware_update_mechanism.md .github/KnowledgeBase/updater_mechanism_gap_and_remediation.md` returned no matches.
- Build/run verification was not performed because the requested workflow only required this static packaging correction.
