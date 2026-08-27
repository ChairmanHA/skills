# Device Status Interface And Realtime IO Layout

## Scope

- Adjust `DeviceInfoWidget` status-bar ordering only.
- Place the transport/interface indicator immediately to the left of the CPU/RFU temperature area on both Win32 and PGA layouts, separated by the existing detail spacing.
- Move PGA real-time IO throughput from the far-left warning/status area into the same detail position used by Win32.
- Preserve all status data, visibility rules, and non-layout behavior.

## Observation And Assumption

- Observation: `m_interfaceIndicator` renders the connected interface as `USB`, `U<speed>`, `ETH`, or `QSFP`.
- Observation: Win32 already places `m_bandwidth` in the detail group after RLO, while PGA currently places it before the expanding warning label.
- Assumption: the requested "Usb type control" is `m_interfaceIndicator`; the requested "real-time IO" is `m_bandwidth`.

## Success Criteria

1. Win32 and PGA both order the interface indicator immediately before the temperature group.
2. The interface indicator and temperature group have one `kDetailSpacing` gap and no separator between them.
3. PGA and Win32 both place real-time throughput after the RLO indicator and before device/version details.
4. Existing interface/throughput show-hide behavior remains unchanged.

## Verification

- Level: static.
- Confirm both layout construction paths use the intended widget order.
- Inspect the focused diff and verify no unrelated source changes are introduced.

## Verification Result

- Win32 order: `RLO -> real-time IO -> device/version details -> interface -> spacing -> temperature`.
- PGA order: `warning/status stretch -> RLO -> real-time IO -> device/version details -> interface -> spacing -> CPU/RFU temperature -> battery`.
- `git diff --check` passed for the modified source file.
- Build/run intentionally not performed under the repository's default static-verification policy.
