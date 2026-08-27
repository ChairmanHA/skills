# 2026-07-22 RMS Device Level Writeback Synchronization

## Scope

- Make core-managed RMS display use the device-confirmed Level after configuration writeback.
- Preserve RMS values in dBm and the existing display-unit synchronization.
- Keep the fix inside the runtime/session apply-result boundary.
- Do not issue a second device configuration after writeback.

## Observation

- `TxPipelineRuntime` emits `deviceConfigurationDone(writeback)` before `applyStateChanged(...)`.
- `MainWindow` applies that writeback to `CommonDeviceProfile`, so the Level button shows the device-confirmed value.
- `applyStateChanged` still carries `m_request.context.common.level` from the original request.
- `TxSessionService::rmsPowerDisplayForRequest()` therefore derives RMS from the stale requested Level.

## Design

1. Centralize successful device-profile publication in `TxPipelineRuntime`.
2. Before emitting `deviceConfigurationDone`, copy `writeback.level` into `m_request.context.common.level`.
3. Let the existing later `applyStateChanged(m_request, succeeded)` publish the effective Level to RMS consumers.
4. After a core-managed apply, let `TxSessionService` retain `TxPipelineRuntime::currentRequest()` rather than the original request.

## Success Criteria

- Fixed CW RMS equals the device-confirmed Level.
- Fixed/FScan playback RMS uses device-confirmed Level plus the existing waveform RMS offset.
- Failed apply still publishes invalid RMS.
- Unit-only synchronization remains unchanged.
- Device writeback does not cause another apply request.

## Verification

Verification level: `static`.

- Confirm every successful `deviceConfigurationDone` emission passes through the effective-Level helper.
- Confirm `applyStateChanged` remains after the synchronous apply completes.
- Confirm session-side `m_appliedRequest` uses runtime `currentRequest()` for core-managed paths.
- Run focused source searches and `git diff --check`.
- Do not build or run unless separately requested.

## Implementation Result

- Added `TxPipelineRuntime::publishDeviceConfigurationWriteback()` as the single successful common-profile writeback boundary.
- The helper stores `profile.level` in the cached effective request before notifying profile/UI consumers.
- All nine Mute/CW/Playback/Sweep successful writeback exits now use the helper.
- The existing later `applyStateChanged(m_request, succeeded)` therefore carries the device-confirmed Level.
- Core-managed `TxSessionService::m_appliedRequest` now stores `currentRequest()` from the runtime.
- No second apply or device configuration was added.

## Verification Results

- Confirmed only the helper directly emits `deviceConfigurationDone`.
- Confirmed all nine successful writeback exits route through the helper.
- Confirmed `applyStateChanged` remains after the synchronous pipeline apply.
- Confirmed the session retains the runtime effective request.
- `git diff --check` passed; only existing LF/CRLF working-copy warnings were reported.
- Build/run verification was not performed, per the repository's default static-verification policy.
