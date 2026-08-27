# Fan Auto Threshold Debug Control

## Scope

Expose the HTRA fan Auto-mode temperature threshold in the existing debug-only `Fan Control` group as a plain `QLineEdit`. Keep the setting device-local and out of CommonDeviceProfile/runtime/profile persistence.

Verification level: `static`

## Observations

- The whole `Fan Control` group is created by `DeviceSettingPanel` and shown only when `Settings.ini` contains `Debug/DebugMode=true`.
- `Fan Mode` already uses direct `IDevice` query/configure shortcuts rather than PropertySystem.
- `FancyDevice` maps On/Off to negative/positive infinity and Auto to the fixed `kDefaultFanAutoThresholdCelsius` value, then replays the cached mode on device open.
- The H2 fan API has no readback, so mode and threshold UI state must use the per-device cached values.

## Success Criteria

1. Debug mode shows a plain line edit for the Auto fan threshold alongside the existing fan mode selector; no custom soft keyboard is attached.
2. A newly created HTRA device displays and uses the existing 40 °C default.
3. Editing a numeric threshold while the mode is Auto immediately applies it to the open device and caches it after success.
4. Editing while the mode is On or Off updates the cache without changing that forced mode; the cached threshold is used on the next switch to Auto or device reopen.
5. Invalid/empty text is rejected by restoring the cached display value; no product-facing range policy is introduced.
6. Fan threshold remains outside CommonDeviceProfile, TxApplyRequest, and profile persistence.

## Plan

1. Add optional fan Auto-threshold query/configure shortcuts to `IDevice` and implement them in `FancyDevice` with per-instance cached state.
2. Make all Auto fan applications use the cached threshold while preserving the existing On/Off infinity mapping.
3. Add and bind a normal `QLineEdit` in the debug-only Fan Control group.
4. Update the existing Device Settings architecture note and perform focused static checks.

## Implementation Result

- Added optional `IDevice` shortcuts for querying and configuring the fan Auto threshold.
- `FancyDevice` now initializes its per-instance threshold cache from `kDefaultFanAutoThresholdCelsius`, uses that cache whenever Auto is applied, and replays it during device open.
- Auto-mode edits call `device_config_fan()` immediately and update the cache only after success. On/Off-mode edits update only the cached Auto value, preserving the active forced mode.
- The debug-only Fan Control group now contains an `Auto Threshold (℃)` label and ordinary `QLineEdit`. `editingFinished` performs only `QString::toFloat()` conversion; invalid/empty input or device failure restores the cached value.
- No PropertySystem binding, custom touch keyboard, CommonDeviceProfile field, runtime request field, or persistence entry was added.

## Static Verification

- Confirmed `devicesettingdialog.cpp/.h` and `fancydevice.cpp/.h` are included by their current plugin `CMakeLists.txt` files.
- Confirmed every new virtual threshold shortcut has a default `IDevice` implementation and matching HTRA override/declaration.
- Confirmed all Auto threshold applications now pass `m_fanAutoThresholdCelsius`; On/Off still map to negative/positive infinity.
- Confirmed `kDefaultFanAutoThresholdCelsius` remains the initialization source for the 40 ℃ default.
- Confirmed `git diff --check` reports no whitespace errors.
- No build or runtime test was performed, per the repository's default static-analysis workflow.
