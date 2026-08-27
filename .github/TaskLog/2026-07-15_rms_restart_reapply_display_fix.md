# RMS Display Refresh After Restarted RF Restore

## Scope

- Fix the main-window RMS display when a language change restarts SGStudio and the persisted profile restores RF to ON.
- Keep the existing RMS calculation, formatting, profile persistence, and RF restore behavior unchanged.
- Limit the implementation to the core-managed pipeline apply-result propagation needed by the display.

## Observation And Inference

### Observations

- `MainWindowSettingsController::requestApplicationRestart()` sets `APP/Reboot=True`; startup then loads `Profile.json`, including the persisted `RF` value.
- `TxSessionService` derives and publishes RMS only from the synchronous result returned by its own call to `TxPipelineRuntime::requestApply()`.
- When that first request occurs before a device is open, the runtime caches the request but returns failure, so RMS correctly remains invalid (`--`).
- `TxPipelineRuntime` later handles `currentDeviceOpenStateChanged(true)` by calling `reapply()` with the cached request. This restores RF, but the result is not reported back to `TxSessionService`.
- `MainWindow` clears RMS directly when the device becomes unavailable, while `TxSessionService` retains its last published RMS value.

### Inference

- The missing RMS update is an apply-result propagation gap, not an RMS formula, translation, or formatting problem.
- The runtime should report both explicit apply results and device-lifecycle invalidation. `TxSessionService` can then remain the single owner that converts the applied request into `TxRmsPowerDisplay` and publishes it to UI subscribers.

## Success Criteria

1. If a persisted RF-ON request is initially cached before device open, a successful automatic reapply after device open publishes the correct RMS value.
2. Device unavailability invalidates the service-side RMS cache as well as the visible `CommonPanel`, so reconnecting with the same RMS value is not suppressed by deduplication.
3. Failed apply/reapply keeps RMS invalid (`--`).
4. Normal RF toggles and parameter changes continue to publish RMS through the same calculation and formatting code.
5. No RMS formula, profile persistence, translation, or device configuration behavior changes.

## Verification Level

- `static`

## Static Verification Checklist

- [x] Confirm `txpipelineruntime.cpp/.h` and `txsessionservice.cpp` are included through `src/plugins/core/CMakeLists.txt`.
- [x] Confirm every core-managed `requestApply()` exit publishes its success/failure result.
- [x] Confirm active runtime state publishes failure when the current device becomes unavailable and publishes the reapply result when it opens again.
- [x] Confirm `TxSessionService` derives RMS from the runtime-reported request/result and no longer performs a duplicate direct publication for the same explicit apply.
- [x] Review the final diff for unrelated changes.

## Implementation Result

- Added `TxPipelineRuntime::applyStateChanged(request, succeeded)` as the apply-result boundary.
- `requestApply()` now publishes one result after every core-managed apply attempt; cached `reapply()` naturally uses the same path.
- An active runtime publishes a failed state when the current device becomes unavailable, keeping the session-side RMS cache aligned with the visible `--` state.
- `TxSessionService` now derives RMS from runtime apply results. Its previous direct publication after `requestApply()` was removed to avoid duplicate ownership.
- Static verification passed: active CMake inclusion confirmed, repository-wide call sites reviewed, and `git diff --check` reported no errors.
