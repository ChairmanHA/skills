# Device model display name rules

## Purpose

`configuration/device_info.xml` is the runtime source of truth for user-facing
device model names shown in `DeviceInfoWidget` and `AboutDialog`.

The mapping is no longer limited to a flat `model -> name` table. It now
supports ordered conditional aliases so the same hardware model code can display
different names on different runtime platforms or when different option codes
are present, without recompiling.

## Control path

1. `configuration_files/<packet>_<language>/device_info.xml` is copied into the
   runtime `configuration/device_info.xml` by `configuration_files/CMakeLists.txt`.
2. `Utils::LocalConfigManager` reads `../configuration/device_info.xml` at startup.
3. `Core::DeviceInfoWidget` and `Core::AboutDialog` call
   `LocalConfigManager::resolveDeviceName(...)` when converting a device model
   code to display text.
4. Platform matching comes from `Utils::Settings::hardwareType()` and the target
   OS; option matching comes from `DeviceManager::currentDeviceCapabilities()`.

## Supported XML shape

Legacy entries remain valid:

```xml
<Device name="RFS-7060-U" code="122" />
```

Conditional aliases are expressed as ordered child nodes:

```xml
<Device name="RFS-7060-U" code="122">
    <DisplayName name="RFS-7060-T" hardwareType="PGA" />
    <DisplayName name="RFS-7060-X" os="linux" optionCodes="0x140,320" optionMatch="any" />
    <DisplayName name="RFS-7060-Y" hardwareType="Normal" optionCodes="320,321" optionMatch="all" />
</Device>
```

## Matching semantics

- The parent `Device name` is the default fallback display name.
- `DisplayName` rules are evaluated in document order. The first matching rule wins.
- If no `DisplayName` matches, the parent `Device name` is used.
- If the model code is absent from the file, the numeric model code is shown.

## Rule attributes

- `name`: required resolved display name.
- `hardwareType`: optional comma-separated hardware type filter.
  Supported values: `PGA`, `Normal`.
- `platform`: alias of `hardwareType` for configuration compatibility.
- `os`: optional comma-separated OS filter.
  Supported values: `windows`, `linux`, `macos`.
- `optionCodes`: optional comma-separated decimal or `0x` hexadecimal option codes.
- `optionNamespace`: optional namespace filter for option-based rules.
- `optionMatch`: optional `any` or `all`. Default is `any`.

Attribute matching is case-insensitive after trimming.

## Current use

The BNC profile uses this mechanism so model `122` and `132` keep the default
display name `RFS-7060-U` on normal desktop builds, but resolve to
`RFS-7060-T` when the runtime hardware type is `PGA`.