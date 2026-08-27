# Updater local aarch64 package parse diagnostics

## Scope

- Inspect why `D:\SGStudio.tar.gz`, produced on aarch64 and runnable on Raspberry Pi, is not parsed by the Update dialog through `Local File`.
- Keep the current lowercase updater executable naming contract.
- Improve the active parser so local parse failures expose the actual stage that failed.

## Assumptions And Evidence

- Observation: `D:\SGStudio.tar.gz` contains a single package root, `SGStudio/`.
- Observation: the package contains `SGStudio/version.json`, `SGStudio/releasenote.txt`, `SGStudio/bin/maintenance`, and `SGStudio/updater/updater`.
- Observation: `version.json` contains target `MCU`, `FPGA`, `FX3`, and `EIO` fields.
- Observation: `SGStudio/updater/updater` and `SGStudio/bin/maintenance` both have executable bits in the tar listing.
- Observation: `PacketSpec::run()` currently ignores the boolean result returned by `Decompressor::decompress()`, so a tar failure can appear later as a generic invalid package or missing version information.
- Observation: `findUpdater()` and `findMaintenance()` do not check the return value of `QDir::cd(...)`, so a missing subfolder can fail silently.
- Assumption: the Raspberry Pi should parse `.tar.gz` through the Linux `tar -xzf` path; Windows `.zip` behavior is outside this task.

## Design

- Treat decompression failure as a hard parse failure and log the selected file and target extraction path.
- Reset `PacketSpec` parse state before starting a new local or remote load, avoiding stale metadata when a `PacketSpec` instance is reused.
- Strip a UTF-8 BOM from `version.json` defensively before JSON parsing.
- Log concrete file open, JSON parse, package root, updater, and maintenance lookup failures.
- Preserve the exact lowercase executable names: Windows `updater.exe`, Linux `updater`.

## Verification Level

- static

## Success Criteria

- A tar/unzip failure is no longer followed by misleading package-root parsing.
- Failure to open or parse `version.json` logs the exact file path and parser error.
- Missing `updater/` or `bin/` subdirectories are logged with the expected directory.
- Missing lowercase updater executable logs the expected filename and files found in `updater/`.
- No compatibility fallback to old `Updater_*` names is reintroduced.

## Implementation Notes

- `PacketSpec::loadFile()` and `PacketSpec::loadUrl()` now reset parsed metadata before a new load.
- `PacketSpec::run()` now stops immediately when the extraction directory cannot be created or decompression fails.
- `parseVersionFile()` now logs open failures, strips a UTF-8 BOM defensively, and rejects non-object JSON roots.
- `findUpdater()` and `findMaintenance()` now check `QDir::cd(...)` directly and log expected filenames plus actual files on lookup failure.
- Downloaded or selected packages are now considered valid only when both `maintenance` and the platform updater executable are found.

## Static Verification

- `rg -n "Updater_\*|Updater_Win|Updater_Linux|\^Updater_" src/plugins/updater updater_files .github/KnowledgeBase/updater_firmware_update_mechanism.md .github/KnowledgeBase/updater_mechanism_gap_and_remediation.md -S` returned no matches.
- `D:\SGStudio.tar.gz` was inspected statically and contains `SGStudio/version.json`, `SGStudio/releasenote.txt`, `SGStudio/bin/maintenance`, and `SGStudio/updater/updater`.
- A byte-level scan of the package's `SGStudio/plugin/libUpdater.so` found no old `Updater_Win`, `Updater_Linux`, `Updater_Arm`, `Updater_Aarch64`, or `Updater_*` strings; it does contain lowercase `updater`.
- Build/run verification was not performed; this pass stayed at static inspection and targeted parser diagnostics.
