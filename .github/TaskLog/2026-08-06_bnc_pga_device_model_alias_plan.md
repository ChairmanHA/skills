# BNC PGA device model alias plan

## Scope

- Fix the BNC status-bar device model display so model codes `122` and `132` show `RFS-7060-T` on PGA devices while keeping the existing Win32 display as `RFS-7060-U`.
- Make the model-name mapping runtime-configurable so future platform- or option-specific aliases can be solved by editing configuration files, without recompiling.
- Keep backward compatibility with the existing `configuration/device_info.xml` format.
- Verification level: `static` only. Do not build or run.

## Observations

- `configuration_files/CMakeLists.txt` copies the selected `${packet}_${language}` directory to the runtime `configuration/` directory at configure time, so `configuration/device_info.xml` is already the right non-code customization surface for BNC.
- `Utils::LocalConfigManager::readLocalDeviceInfo()` currently reads only `<Device name="..." code="...">` and stores a single `code -> name` map.
- `Core::DeviceInfoWidget::convertDeviceName()` and `Core::AboutDialog::convertDeviceName()` both use that flat map, so repeated codes cannot express conditional aliases.
- `Utils::Settings::hardwareType()` already distinguishes `HardwareNormal` and `HardwarePGA`.
- `Core::DeviceManager::currentDeviceCapabilities()` already exposes the current device option snapshot, so option-based display rules can be evaluated in UI code without changing the device open chain.

## Assumptions and evidence

- The user's requested Raspberry Pi behavior corresponds to `HardwarePGA`, because the current codebase uses that hardware type to differentiate PGA UI/runtime behavior.
- The desired customization should live in `device_info.xml`, because the user explicitly wants future adjustments without recompiling and this file is already per-profile runtime configuration.
- Document order is an acceptable rule priority model for conditional aliases: first matching rule wins, then the default device name is used as fallback.

## Success criteria

1. On BNC builds running with `HardwarePGA`, model `122` and `132` resolve to `RFS-7060-T` in the status bar.
2. On non-PGA platforms, the same BNC models still resolve to `RFS-7060-U`.
3. Existing plain `<Device name="..." code="...">` entries continue to work unchanged.
4. The new mechanism supports optional platform and option conditions in config so future display aliases can be added without recompiling.
5. Static verification confirms the changed XML is parseable and the modified source files are free of diagnostics relevant to this task.

## Planned changes

1. Extend `LocalConfigManager` from a flat model-name map to a rule-aware resolver with a lightweight runtime context.
2. Support optional conditional child nodes in `device_info.xml` for platform/option-specific display names while keeping the legacy default attributes.
3. Switch `DeviceInfoWidget` and `AboutDialog` to use the new resolver so device name display is consistent across the existing UI surfaces.
4. Update `configuration_files/BNC_en/device_info.xml` to add PGA-specific aliases for model `122` and `132`.
5. Run targeted static checks on the touched files.

## Proposed config shape

Legacy form remains valid:

```xml
<Device name="RFS-7060-U" code="122" />
```

Conditional aliases can be added as ordered child rules:

```xml
<Device name="RFS-7060-U" code="122">
    <DisplayName name="RFS-7060-T" hardwareType="PGA" />
    <DisplayName name="RFS-7060-X" optionCodes="320" />
</Device>
```

Planned semantics:

- `name`: resolved display name for that rule.
- `hardwareType`: optional comma-separated matcher, initially supporting `Normal` and `PGA`.
- `optionCodes`: optional comma-separated matcher supporting decimal or `0x` hex codes.
- `optionMatch`: optional `any` or `all`, default `any`.
- First matching `DisplayName` wins; if none match, fall back to the parent `Device` `name`.