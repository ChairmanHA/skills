# 2026-07-28 updater option-profile firmware versions

## Goal

- Replace the updater dialog's single global firmware target with option-profile metadata.
- Select the displayed firmware profile from the current device's effective H2 option IDs, never from `Model`.
- Keep the legacy top-level `FPGA` / `MCU` / `FX3` / `EIO` fields readable by old SGStudio packages and clients.
- When no device is open or the option query is unavailable, display the legacy top-level values as the package default.
- Keep the external updater as the authority that selects and flashes the actual firmware artifact.

## Confirmed Facts

- `device_query_options()` is already called once during `FancyDevice::open()`.
- `OPTION_BW_320M_TX` is H2 option ID `51`.
- The current code discards the complete option set and cannot distinguish a successful empty query from a failed query.
- `PacketSpec` currently parses one global firmware target from the top-level JSON fields.
- `UpdateDialog` currently displays that one target without considering device options.
- The external updater selects firmware itself; maintenance and `ProgressDialog` do not participate in target-profile selection.

## Design

1. Add an opaque Core option snapshot containing:
   - option namespace;
   - `known` state;
   - the complete, sorted effective option-code list.
2. Cache that snapshot in `FancyDevice` from the existing H2 option query and publish it through `CurrentDeviceCapabilitySnapshot`.
3. Extend `version.json` with schema version 2 and exact option-set firmware profiles.
   - The current selector list contains option `51`.
   - The parser remains generic so option `73` and the `{51, 73}` combination can be added later without code changes.
   - Top-level firmware values remain as the legacy/default target; top-level FPGA is `2.0.19`.
4. Parse all profiles in `PacketSpec` and resolve a target by exact comparison after intersecting the device's options with the profile selector list.
5. If the current device/options are unavailable, use the legacy/default target.
6. If the options are known but the new schema has no exact profile, show no firmware target and report the package/profile mismatch instead of guessing.
7. Refresh the target whenever the current capability snapshot or selected package changes.
8. Do not modify maintenance arguments or `ProgressDialog`.

## Compatibility

- Old package without `Firmware`: use top-level `FPGA` / `MCU` / `FX3` / `EIO` unconditionally.
- New package with valid `Firmware`: resolve the exact option profile when options are known.
- New package with unavailable device options: use the top-level values as the explicit default.
- New package with malformed `Firmware`: reject profile metadata; do not silently treat it as an old package.

## Success Criteria

- No device: target FPGA displays top-level default `2.0.19`.
- Known options without `51`: target FPGA displays `2.0.14`.
- Known options with `51`: target FPGA displays `2.0.19`.
- Unrelated options do not affect selection.
- Model cross-checks produce the same result for the same option set.
- Query failure is distinguishable from a successful empty option set.
- Old top-level-only JSON remains readable.
- The parser can accept selector `73` and `{51, 73}` profiles when metadata is added later.
- Current package selection and device switching refresh the target immediately.
- `maintenance/progressdialog.cpp` remains unchanged.

## Verification

- Level: static.
- Inspect relevant diffs and run `git diff --check`.
- Search the active updater path for remaining unconditional target firmware getters.
- Confirm the active CMake target still compiles only `src/plugins/updater`, not `updater_bak`.
- A real firmware update is intentionally not launched by this task.

## Verification Result

- `git diff --check` passed.
- `package-info/version.json` parses as schema 2 with one selector and two unique
  exact profiles: `{}` -> `standard-bandwidth`, `{51}` -> `tx-bandwidth-320m`.
- The top-level default remains FPGA `2.0.19`.
- The old unconditional firmware-version getters were removed from `PacketSpec`;
  `UpdateDialog` resolves one `FirmwareTarget` for both display and update-start
  validation.
- `DeviceManager` publishes a fresh unavailable capability snapshot on disconnect,
  so the no-device path has `options.known == false` and uses the top-level default
  instead of retaining the previous device profile.
- `src/maintenance/progressdialog.cpp` has no diff.
- No build, runtime test, or real firmware update was performed under the selected
  static verification level.

## Win32 Compile Follow-up

- MSVC reported C2589 at `std::numeric_limits<quint32>::max()` because
  `windows.h` defines a function-like `max` macro.
- Use `(std::numeric_limits<quint32>::max)()` so the token after `max` is not an
  opening parenthesis during macro expansion.
- Re-scan the active source file for other unprotected `min()` / `max()` calls and
  run the user-requested build verification if requested.
