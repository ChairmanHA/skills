# Device Info Status Layout USB Throughput

## Scope

- Update `DeviceInfoWidget` status-bar layout only.
- Keep connecting and disconnected text in the original left-side position.
- Hide the connected text state.
- Move USB speed display from the connection-state text to the left of the device UID as `U2` / `U3`.
- Show realtime throughput only when non-zero, positioned to the left of the USB speed marker.
- Allow warning text to start from the left side while a device is connected.

## Success Criteria

- Connected USB devices no longer show `Connected USB2.0:` or `Connected USB3.0:` in the left connection-state slot.
- USB devices show `U2` or `U3` immediately before the UID area.
- Throughput text is hidden when `throughputBps == 0` or the device is disconnected.
- When connected, warning text is not blocked by an empty visible `Connected` label.

## Verification

- Verification level: static.
- Confirm `deviceinfowidget.cpp` remains included by `src/plugins/core/CMakeLists.txt`.
- Check lints for edited files after implementation.

## Implementation Notes

- `m_deviceState` remains the left-side owner for connecting and disconnected text, but is hidden while connected.
- The warning status group now only owns rolling warning text, so connected-state warning text can use the left side of the status bar.
- Non-PGA full detail layout now places realtime throughput, then interface indicator, then UID.
- USB interface text is shortened to `U2` / `U3` based on `DeviceInfo::BusSpeed`.
- The interface indicator and UID are separated by the same vertical separator style used by the rest of the detail fields, and the separator follows the interface indicator visibility.
