# 2026-06-29 RefClock Unlock Warning Statusbar Design

## Scope

- Analyze the current `DeviceInfoWidget` warning mechanism.
- Decide whether external-reference unlock should be driven by existing `device_query_state()` polling or by adding `device_query_clock()` into the real-time status loop.
- Implement the selected controller/UI-layer stateful warning path.

## Observations

- `DeviceManager` already polls open devices through `device->updateRealTimeStatus()` and publishes `DeviceRealTimeStatus`.
- `FancyDevice::updateRealTimeStatus()` already calls `device_query_state()` on every non-streaming polling cycle.
- `device_query_state()` returns RLO warning codes including `STATUS_WARNING_SYSCLK_UNLOCKED` (`15`), which is the requested RLO15 unlock signal.
- `DeviceInfoWidget::updateDeviceRealTimeStatus()` already consumes raw `warnningCode` for the small RLO indicator.
- Rolling statusbar warnings are owned by `MainWindowDeviceController`, which currently only adds `WarningCategory::General` warnings and does not remove persistent warning messages when the warning disappears.
- `device_query_clock()` is already used by explicit reference-clock query/configuration paths and exposes `locked`, but adding it to the high-frequency status poll would introduce another device API call and duplicate a lock signal already represented by `device_query_state()`.

## Recommendation

- Reuse `device_query_state()` as the primary real-time source for external-reference unlock warning.
- Do not add `device_query_clock()` to the periodic real-time status loop only for `locked`.
- Treat RLO15 as a stateful UI warning when current reference source is External:
  - when active, add/show a rolling warning such as "External reference clock not locked"
  - when cleared, remove that warning from the `DeviceInfoWidget` queue/current display
- Keep `device_query_clock()` for explicit settings/readback paths where source/frequency/output/locked snapshot is needed.

## Suggested Implementation Boundary

- Device layer:
  - keep `FancyDevice::updateRealTimeStatus()` based on `device_query_state()`
  - continue publishing `STATUS_WARNING_SYSCLK_UNLOCKED` through `DeviceRealTimeStatus::warnningCode`
- Controller/UI layer:
  - detect `warnningCode == STATUS_WARNING_SYSCLK_UNLOCKED`
  - gate the rolling message by current reference source being External
  - maintain a small boolean/current-active flag so disappearance triggers `removeWarnningMessage()`
- Optional later enhancement:
  - if firmware proves `device_query_state()` cannot distinguish the needed lock state reliably, add a narrow `ReferenceClockState` cache field to `DeviceRealTimeStatus` instead of calling `device_query_clock()` directly from the widget.

## Verification

- Static analysis only, per repository default workflow.
- Confirmed no new `device_query_clock()` call was added to the periodic real-time status loop.

## Implementation Notes

- `MainWindowDeviceController` now owns the External RefClock unlock rolling warning lifecycle.
- It reuses `DeviceRealTimeStatus::warnningCode == 15` from the existing HTRA `device_query_state()` poll.
- The rolling warning is gated by current `CommonDeviceProfile::refClockSource() == External`.
- The statusbar message is added once while active and removed when RLO15 disappears, the source is no longer External, the device disconnects, or the displayed device state resets.
- `DeviceInfoWidget` was not changed; its existing `addWarnningMessage()` / `removeWarnningMessage()` queue API already supports this lifecycle.
- `device_query_clock()` remains reserved for explicit settings/readback paths and was not added to the periodic status poll.
