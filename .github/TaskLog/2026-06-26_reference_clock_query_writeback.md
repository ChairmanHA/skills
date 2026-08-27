# Reference Clock Query Writeback

## Scope

Fix the current Reference Clock readback gap with the smallest cross-layer change:

- Keep `RefClockSource` and `RefClockFrequency` in the existing common profile/property path.
- Keep `SystemClockOut` as a device shortcut that does not trigger common reconfiguration by itself.
- Expose a complete reference clock query through `Core::IDevice`, because the current shortcut only returns `SystemClockOut` even though HTRA `device_query_clock()` already returns source and frequency.

## Observations

- Before this task, `DeviceSettingPanel::refreshSystemClockOut()` called only `IDevice::querySystemClockOut(bool*)`, so UI refresh could only sync `SystemClockOut`.
- Before the follow-up simplification, `FancyDevice::querySystemClockOut()` duplicated `queryReferenceClock()` even though both used `device_query_clock()`.
- `FancyDevice::fillWritebackProfileLocked()` writes reference clock fields from `source_query/refFreq_query/clockOut_query` caches, not from a fresh post-config query.
- `CommonDeviceProfile::setProfile()` already accepts `refClockSource`, `refClockFrequency`, and `systemClockOut`, then refreshes the bound properties/UI.

## Assumptions

- HTRA `device_query_clock()` is the authoritative readback source for reference clock state after open and after configuration.
- A failed `device_query_clock()` should not invent a new state. The fallback remains the current cache so existing behavior is preserved.
- This task does not add reference lock UI or alter external frequency validation.

## Success Criteria

- Opening the device settings page after a device is open can query and write back `RefClockSource`, `RefClockFrequency`, and `SystemClockOut`.
- After a normal `configuration()` succeeds, HTRA performs a fresh clock query before filling writeback, so `CommonDeviceProfile` receives the actual device reference clock state.
- After a `RefOut` shortcut change and delayed readback, the complete reference clock state is synchronized, not only output.
- Verification level: static analysis only, unless the user explicitly requests a build/run.

## Implementation Notes

- Added `Core::IDevice::ReferenceClockState` and `IDevice::queryReferenceClock()`.
- Added `CommonDeviceProfile::setReferenceClock()` so device shortcut query can update only reference clock fields without replacing center/level/trigger settings.
- Updated `FancyDevice` to expose complete `device_query_clock()` results and to query again during `fillWritebackProfileLocked()`.
- Updated Device Settings refresh/readback to use the complete reference clock query directly.
- Updated `.github/KnowledgeBase/htra_reference_clock_integration.md` to match the new query/writeback behavior.

## Verification

- Static inspection of the affected CMake-included source files.
- `git diff --check` passed; output only contained existing line-ending normalization warnings.

## Follow-up Simplification

`FancyDevice::querySystemClockOut()` duplicates `queryReferenceClock()` and both read through the same `device_query_clock()` API. Keep `IDevice::querySystemClockOut()` only as a compatibility helper derived from `ReferenceClockState::outputEnabled`, remove the HTRA override, and let Device Settings refresh use `queryReferenceClock()` directly.

Follow-up applied: removed the remaining `queryReferenceClockLocked()` private helper. `fillWritebackProfileLocked()` now performs the post-config `device_query_clock()` directly while already holding `m_mutex`, and public `queryReferenceClock()` performs its own locked query.
