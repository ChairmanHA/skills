# HTRA Low-Power Device Setting

## Scope

- Use H2 API v2.0.28
  `device_config_power_state(void **device, power_state state)` to configure
  the device power state.
- Add a `Low Power` ON/OFF toggle to the `RF Hardware` group in
  `DeviceSettingPanel`, using the same centered `LabelButton` toggle style as
  `Trigger Output`.
- Keep the Low Power checked/ON visual identical to Trigger Output by sharing
  its device-settings QSS selector group in both dark and light themes.
- Keep this setting independent from `CommonDeviceProfile`, PropertySystem,
  `TxApplyRequest`, and runtime reapply.
- Add platform-neutral direct query/configure hooks to `Core::IDevice`; only
  HTRA implements them.
- Do not change trigger, RF port, LO mode, fan, Playback capability, or
  pipeline behavior.

## Observation

- The staged H2 API headers define:
  - `POWERON = 0x00`;
  - `POWEROFF = 0x01`;
  - `device_config_power_state(...)`.
- HTRA already determines PGA single-port power during plugin construction:
  `PGA_PowerSourceType == 1` sets `Plugin::usbPortOnly() == true`.
- `FancyDevice::open()` already uses `usbPortOnly()` for the pre-open USB
  power-routing helper, so this is the existing authoritative detection
  result.
- `DeviceSettingPanel` already has two independent direct-control patterns:
  Reference Clock Output and Fan Mode.
- H2 exposes power-state configuration but no power-state query. The HTRA
  device therefore needs to cache the last successfully requested state for UI
  projection.
- `configuration_files/CMakeLists.txt` copies the selected
  `configuration_files/<packet>_<language>/` variant into the runtime
  `configuration/` directory. The five tracked variant pairs are the source of
  truth; root `configuration/theme*.css` files are generated runtime copies
  and are not edited by this task.

## Design

1. Add `IDevice::queryLowPowerEnabled(bool *)` and
   `IDevice::configureLowPowerEnabled(bool)` defaulting to unsupported.
2. On every successful HTRA device open:
   - `Plugin::usbPortOnly() == true`:
     configure `POWEROFF`, cache Low Power ON;
   - otherwise:
     configure `POWERON`, cache Low Power OFF.
3. If the open-time power configuration fails, keep the device open, log the
   status, and retain the required platform-derived UI default. This matches
   existing non-fatal post-open hardware initialization behavior.
4. A UI edit calls `configureLowPowerEnabled()` directly:
   - ON maps to `POWEROFF`;
   - OFF maps to `POWERON`;
   - cache changes only after a non-error API result;
   - failure refreshes the button back to the cached value.
5. Add the toggle at RF Hardware grid row 0, column 1, next to LO Mode.
   Output Port remains at row 1, column 0, and Fan Control remains the final
   independently appended group below RF Hardware.
6. Refresh it on current-device open-state changes. Unsupported or closed
   devices show OFF and disable interaction.
7. Keep `checked` as the Low Power business-state carrier and add
   `#lowPowerBtn` to every Trigger Output QSS rule that controls the toggle
   background, label spacing, and `parentChecked` info-label color across all
   tracked dark/light theme variants under `configuration_files/`. Do not edit
   generated runtime copies or add a second UI-only state property.

## Success Criteria

- Every successful HTRA open calls `device_config_power_state()` once with the
  platform-derived default.
- PGA single-port devices open with `POWEROFF` and the UI toggle ON.
- Other HTRA devices open with `POWERON` and the UI toggle OFF.
- Clicking the toggle calls the current open device directly and maps ON/OFF
  to `POWEROFF`/`POWERON`.
- The toggle is in RF Hardware and visually uses the same setup helper as
  Trigger Output.
- In both themes, Low Power OFF/ON and its child labels resolve through the
  same selectors as Trigger Output; in the dark theme, ON uses the established
  green `#00CE01` background and `#054604` state text.
- No new CommonDeviceProfile field, property, external mapping, apply request,
  runtime field, or persistence entry is added.
- Declaration/call audit and `git diff --check` pass.

## Verification Level

`static`. Do not build or run in this pass.

## Implementation Result

- Added platform-neutral direct hooks to `Core::IDevice`:
  `queryLowPowerEnabled(bool *)` and
  `configureLowPowerEnabled(bool)`. Their default implementation reports
  unsupported.
- `FancyDevice::open()` now uses the existing
  `Plugin::usbPortOnly()` result on every successful open:
  - single-port PGA: cache ON and send `POWEROFF`;
  - all other HTRA devices: cache OFF and send `POWERON`.
- A non-zero open-time power configuration status is logged without turning
  the already-open device into an open failure.
- HTRA direct edits map ON/OFF to `POWEROFF`/`POWERON`; the cached UI value is
  updated only when the API result is not an error.
- Added a centered checkable `Low Power` `LabelButton` to RF Hardware row 0,
  column 1, next to LO Mode. It uses the same `setupToggleButton()` and
  ON/OFF label projection as Trigger Output.
- Added `#lowPowerBtn` to the complete Device Settings toggle selector group
  in all five tracked dark/light theme pairs under `configuration_files/`.
  Dark-theme ON now uses the same green `#00CE01` background and `#054604`
  status text as Trigger Output; light-theme visuals likewise match the
  existing Trigger Output rules.
- Output Port remains alone at RF Hardware row 1, column 0; Fan Control remains
  the final independently appended group and may be hidden by its existing
  debug-mode policy.
- The panel refreshes on current-device open-state changes. Closed or
  unsupported devices show OFF and disable the toggle.
- No CommonDeviceProfile, PropertySystem, TxApplyRequest, runtime, or profile
  persistence field was added.
- Updated the Device Settings, H2 API, and device-open KnowledgeBase documents
  and the KnowledgeBase index.

## Static Verification

- Static search confirms the H2 declarations provide `POWERON`, `POWEROFF`, and
  `device_config_power_state()`.
- The HTRA open path contains exactly one default power-state call and uses
  `Plugin::usbPortOnly()` as the single-port PGA decision.
- The direct edit path contains one additional power-state call with exact
  ON-to-`POWEROFF` and OFF-to-`POWERON` mapping.
- Interface declaration/override/call sites agree for both low-power hooks.
- The Low Power and Trigger Output buttons both use `setupToggleButton()`;
  Low Power is in the RF Hardware layout at row 0, column 1, beside LO Mode.
- Each tracked theme template contains six scoped `#lowPowerBtn` selector
  entries matching the six Trigger Output rules; generated
  `configuration/theme*.css` runtime copies remain untouched.
- Static search across CommonDeviceProfile, runtime/apply types, session, and
  property-schema sources finds no low-power field.
- Core and HTRA CMake targets include every modified source/header.
- `git diff --check` passes; only expected Windows line-ending conversion
  warnings are reported.
- No build or runtime execution was performed.
