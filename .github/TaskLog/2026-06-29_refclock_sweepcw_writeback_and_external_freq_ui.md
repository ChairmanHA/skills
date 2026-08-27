# 2026-06-29 RefClock SweepCw Writeback And External Frequency UI

## Scope

- Make the SweepCw base `device->configuration()` call use a real writeback profile instead of `nullptr`, matching the other runtime paths.
- Keep internal reference clock fixed at 100 MHz and non-editable.
- Reframe the visible frequency control as external reference clock frequency:
  - default external frequency is 10 MHz
  - user can edit it only when external reference is selected
  - switching back to internal disables the external frequency control
- Simplify the old mixed Internal/External frequency state now that the control only represents external frequency.
- Do not edit translation files.

## Assumptions

- Reference clock source writeback is again trusted after the firmware workaround was removed.
- Internal reference frequency remains an implementation/default value and does not need user-facing editing.
- The existing `RefClockFrequency` property should now store the external reference frequency UI value. Device profile builders should receive the effective frequency through `CommonDeviceProfile::refClockFrequency()`.

## Success Criteria

- SweepCw emits a `deviceConfigurationDone` profile derived from device writeback even on the base configuration phase.
- All current `device->configuration()` paths in `TxPipelineRuntime` pass a writeback profile when they need to synchronize common profile state.
- The Device Settings frequency button text is changed to external-reference wording in source.
- Selecting Internal makes `RefClockFrequency` read-only while `CommonDeviceProfile::refClockFrequency()` returns 100 MHz as the effective device frequency.
- Selecting External makes `RefClockFrequency` editable and uses the external frequency value, defaulting to 10 MHz.
- The old `m_lastExternalRefClockFreq`, `m_externalRefClockFrequencyCustomized`, and `m_syncingRefClockUi` state is removed.

## Verification

- Static inspection only, per repository default workflow.
- Use `rg`/diff to confirm changed call sites and reference-clock policy.

## Implementation Notes

- `TxPipelineRuntime::applySweepCw()` now passes a real `baseWriteback` profile to the CW configuration phase and uses that writeback as the base for sweep writeback.
- `DeviceSettingPanel` source text for the frequency button is now `External Ref Clock Freq`; translation files are intentionally untouched.
- `CommonDeviceProfile::applyRefClockUiPolicy()` now:
  - keeps the frequency control value as external frequency and marks the control read-only for Internal
  - formats the external frequency display and enables editing for External
  - keeps `m_profile.refClockFrequency` synchronized with the external frequency value
  - leaves Internal effective 100 MHz to `CommonDeviceProfile::refClockFrequency()`
- Removed the previous external-frequency fallback/cache state (`m_lastExternalRefClockFreq`, `m_externalRefClockFrequencyCustomized`, `m_syncingRefClockUi`) because the control no longer switches between Internal and External frequency meanings.
