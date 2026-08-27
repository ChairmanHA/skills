# USB-only Battery Visibility

## Scope

- Keep the PGA runtime battery polling and parsing unchanged.
- Hide the PGA status-bar battery group by default.
- Let the HTRA plugin enable the battery group only when the host is Linux
  aarch64 and `Plugin::usbPortOnly()` is true.
- Preserve the existing Core <- HTRA dependency direction; Core must not read
  HTRA plugin internals.

## Evidence and assumptions

- Observation: `HostInfo::isPgaHost()` currently identifies every Linux
  aarch64 host as `HardwarePGA`, regardless of physical battery presence.
- Observation: `Plugin::checkPowerSupply()` owns `m_usbPortOnly` and sets it
  from `PGA_PowerSourceType == 1` on supported Linux ARM builds.
- Observation: the HTRA plugin already uses `extensionsInitialized()` to pass
  this state to `MainWindow` for Device-menu visibility.
- Assumption: `m_usbPortOnly == true` is the product-level capability signal
  that the user wants to use for battery presentation. It is independent of a
  currently connected RF device.

## Success criteria

- The battery group is hidden on Linux aarch64 when `m_usbPortOnly == false`,
  including when HLC battery polling succeeds, fails, or reports zero.
- The battery group is visible when the host is Linux aarch64 and
  `m_usbPortOnly == true`.
- Platform status updates cannot override the product-level visibility gate.
- Non-PGA layouts remain unchanged.
- Core does not add a dependency on HTRA.

## Verification level

- `static`

## Verification checklist

- [x] CMake includes every changed source file.
- [x] HTRA passes the combined host/power-routing decision during
  `extensionsInitialized()`.
- [x] The Core battery widget defaults hidden and uses one explicit visibility
  flag for subsequent updates.
- [x] `git diff --check` passes.

## Verification result

- Static call-chain review passed: only
  `HostInfo::isPgaHost() && Plugin::usbPortOnly()` enables the battery group.
- Core remains independent of HTRA; HTRA passes the product decision through
  the existing `MainWindow` integration boundary.
- No build or runtime test was performed, following the repository's default
  static-verification policy.
