# Windows Ninja mt manifest link serialization

## Scope

- Fix the Qt Creator Windows Ninja build failure where `mt.exe` intermittently fails while embedding manifests into `maintenance.exe` and `SGStudio.exe`.
- Keep the fix inside the CMake build system so the user does not need to manually rerun `mt.exe` or disable parallel builds in Qt Creator.
- Verification level: `debug-build` equivalent for the affected Qt Creator Release build tree only.

## Observations

- The user-provided failure shows `link.exe` completed and `mt.exe` then failed with `c101008d` while writing `bin/maintenance.exe` and `bin/SGStudio.exe`.
- In the local workspace, both `build/Qt_5_15_9_msvc2022_64-Release/bin/maintenance.exe` and `build/Qt_5_15_9_msvc2022_64-Release/bin/SGStudio.exe` exist, are writable, and have fresh timestamps.
- Manually rerunning `mt.exe /manifest ... /outputresource:...;#1` serially on both files succeeds.
- Therefore the manifest data and target files are valid after link, and the failure is a build-time contention/timing issue rather than a broken manifest payload.
- The affected Qt Creator build tree uses the `Ninja` generator.

## Assumptions and evidence

- The failure is specific to Windows + Ninja concurrent link/manifest steps, because the manual serial `mt.exe` pass succeeds on the same generated files.
- Serializing link jobs at CMake level is an acceptable fix because it removes the contested phase without changing runtime behavior or target outputs.

## Success criteria

1. The affected Qt Creator Ninja build tree can build `maintenance` and `SGStudio` without `mt.exe c101008d`.
2. The fix does not change application runtime layout or branding behavior.
3. The change stays scoped to Windows Ninja link scheduling.

## Planned change

1. Add a Windows + Ninja specific single-slot link job pool in top-level CMake before targets are created.
2. Rebuild the affected Qt Creator Release targets in a VS developer environment to confirm the manifest embedding phase no longer fails.