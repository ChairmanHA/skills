# 2026-06-23 PGA Fan Apply Runtime Thread Plan

## TaskLog Path
- `.github/TaskLog/2026-06-23_pga_fan_apply_runtime_thread_plan.md`

## Scope
- Platform: PGA / Linux
- UI entry: `PreferenceDialog` fan control
- Runtime owner candidates:
  - `src/libs/utils/hardwaresettings/pgahardwaresettings.*`
  - `src/plugins/core/pgaruntimestatusservice.*`
  - `src/plugins/core/mainwindowdevicecontroller.*`
  - `src/plugins/core/mainwindow.cpp`

## Verification Level
- `static`

## User-Requested Behavior
- `hlc --set-fan-mode <mode>` must no longer be a one-shot synchronous action from the UI thread.
- It must be issued every 3 seconds on another thread.
- At the same cadence, `hlc --fan-temp-apply <temp>` must also be issued.
- The preferred temperature source is RFU temperature from the device API.
- If there is no device temperature available, use CPU temperature instead.

## Observation
- `PGAHardwareSettings::setFanMode()` currently runs `hlc --set-fan-mode` synchronously and immediately in `Utils` code.
- RFU temperature is currently available only on the Core device runtime chain via `DeviceRealTimeStatus::temperature`.
- CPU temperature is currently available only on the PGA platform runtime chain via `PgaRuntimeStatusService`.
- `PgaRuntimeStatusService` already owns a dedicated worker thread for PGA-side `hlc` polling.

## Inference
- Keeping the periodic fan apply owner inside `PGAHardwareSettings` would require feeding Core runtime temperatures back into `Utils`, which reverses the current dependency direction.
- Reusing `PgaRuntimeStatusService` as the fan-apply worker keeps all temperature-aware PGA `hlc` I/O in one worker thread and keeps `Utils` as a cache/config surface only.

## Success Criteria
1. Changing PGA fan mode from `PreferenceDialog` updates the desired mode immediately in app state without blocking the UI thread on `hlc`.
2. A worker-thread path issues `hlc --set-fan-mode` every 3 seconds while PGA runtime services are active.
3. The same worker-thread path also issues `hlc --fan-temp-apply` every 3 seconds using:
   - RFU temperature when the current device runtime status has a valid temperature
   - CPU temperature when RFU temperature is unavailable
4. Loss of device runtime temperature does not stop the periodic fan-mode apply loop.
5. Existing PGA CPU/battery polling ownership remains in `PgaRuntimeStatusService`.
6. Non-PGA behavior remains unchanged.
7. Relevant KnowledgeBase documentation is updated to describe the new periodic fan-apply ownership and temperature-source fallback.

## Design Plan
1. Convert PGA fan mode in `PGAHardwareSettings` from direct `hlc` write owner to desired-state cache owner.
2. Extend `PgaRuntimeStatusService` with:
   - a 3-second fan-apply timer
   - latest desired fan mode cache
   - latest RFU temperature cache
   - latest CPU temperature reuse from existing platform polling state
3. Let `MainWindowDeviceController` forward device runtime temperature changes to the service thread.
4. Let `MainWindow` notify the controller when the user changes fan mode, while preserving existing preference refresh behavior.
5. Update the PGA runtime KnowledgeBase document with the new fan-control thread ownership and temperature fallback semantics.

## Assumptions
- If both RFU and CPU temperatures are invalid for one cycle, the safest minimal behavior is to keep issuing `--set-fan-mode` and skip `--fan-temp-apply` for that cycle with a warning log.
- The periodic fan apply should stay active for the whole PGA app lifetime, not only while the preference dialog is open, because the user expectation is a persistent operating mode rather than a transient dialog action.
