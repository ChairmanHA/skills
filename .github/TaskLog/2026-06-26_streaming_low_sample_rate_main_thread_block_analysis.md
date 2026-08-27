# Streaming low sample-rate main-thread block analysis

## Scope

Investigate the reported Streaming behavior where sample rates from 2.5 MHz to 62.5 MHz can play normally, while rates at or below about 1 MHz can fail and may freeze the main thread.

This pass is static-analysis only. No build, run, or hardware test was requested.

## Verification level

static

## Success criteria

- Identify the code path that can freeze the UI/main thread.
- Separate observations from inferences about the H2 API behavior.
- Decide whether the evidence points primarily to application code, API behavior, or an interaction between both.

## Relevant documents read

- `.github/KnowledgeBase/streaming_dataflow.md`
- `.github/KnowledgeBase/htra_h2_api_v2_0_usage.md`
- `.github/KnowledgeBase/streaming_bridge_rearm_reboot_refactor_plan.md`
- `.github/TaskLog/2026-05-29_streaming_interface_rate_guard_plan.md`

## Source inclusion evidence

`src/plugins/htra/CMakeLists.txt` includes:

- `streamingbussiness.cpp/.h`
- `streamingdatagenerator.cpp/.h`
- `streamingpanel.cpp/.h/.ui`
- `fancydevice.cpp/.h`

## Observations

1. `StreamingDataGenerator` produces fixed-size frames:
   - `m_sampleCount = 2,000,000`
   - `m_readLen = m_sampleCount * 2 * 2 = 8,000,000 bytes`
   - sender passes `points = request.data.size() / 4 = 2,000,000 complex samples`

2. At low sample rates, one fixed frame represents a long playback duration:
   - 62.5 MHz: about 32 ms per frame
   - 2.5 MHz: about 800 ms per frame
   - 1 MHz: about 2 s per frame
   - 195.3125 kHz lower clamp: about 10.24 s per frame

3. Sender thread flow:
   - `StreamingBussiness::workerLoop()` calls `streamingDataGenerator->processRequests()`.
   - `DataSender::processRequests()` dequeues a frame.
   - callback calls `StreamingBussiness::sendData()`.
   - `sendData()` calls `DeviceOperator::downloadDataRealTime()`.
   - `FancyDevice::downloadDataRealTime()` calls `tx_send_stream(&channels[0], data, points)`.

4. `FancyDevice::downloadDataRealTime()` holds `m_mutex` while calling `tx_send_stream()`.

5. There is no timeout/cancel wrapper around `tx_send_stream()`.

6. `StreamingBussiness::stopBusiness()` runs synchronously in the business activation path and busy-waits:
   - calls `streamingDataGenerator->setEnabled(false)`;
   - sets `m_profileChanged=true`, `m_enabled=false`;
   - spins in `while (m_interrupt.loadRelaxed() == 0) continue;`.

7. `m_interrupt` is set back to 1 only after sender thread returns from the inner send loop. If sender is currently inside `tx_send_stream()`, `stopBusiness()` cannot complete until that API call returns.

8. The UI-side interface bandwidth guard only checks high sample-rate limits by interface. It does not explain a low-rate failure because sample rates below 1 MHz are under all current interface limits.

9. Streaming sample rate is clamped only to the global range `195.3125 kHz .. 62.5 MHz`. There is no low-rate guard specific to realtime stream mode.

## Inferences

1. The main-thread freeze is an application-level bug in stop/reconfigure synchronization:
   - even if the API legitimately blocks for the duration of a stream frame, the UI thread should not spin in a hot wait;
   - the current busy-wait converts a sender-thread long call into an apparent main-thread hang.

2. The rate-specific trigger likely comes from API or device semantics:
   - the application always sends the same 2,000,000-point frame regardless of sample rate;
   - at <= 1 MHz that frame corresponds to >= 2 seconds of realtime stream content;
   - if `tx_send_stream()` blocks until the device accepts/consumes enough of the frame, low sample rates naturally make each API call much longer.

3. The strongest current hypothesis is an interaction:
   - H2 API/device behavior makes `tx_send_stream()` slow or blocking at low sample rates with large fixed frames;
   - SGStudio then exposes that as a main-thread freeze because `stopBusiness()` busy-waits for the sender thread.

4. Static evidence does not prove the API is wrong. It may be behaving synchronously by design. The application should handle that possibility without blocking the UI thread.

## Recommended next checks

1. Add temporary timing logs around:
   - `StreamingBussiness::sendData()` before/after `downloadDataRealTime()`;
   - `FancyDevice::downloadDataRealTime()` before/after `tx_send_stream()`;
   - `StreamingBussiness::stopBusiness()` before entering and after leaving the wait.

2. Test at 62.5 MHz, 2.5 MHz, 1 MHz, and 195.3125 kHz with the same file and record per-call `tx_send_stream()` duration.

3. If `tx_send_stream()` duration scales roughly with `points / sampleRate`, treat the API as synchronous/backpressured and change SGStudio:
   - remove main-thread busy-wait;
   - shrink streaming frame size at low sample rates or target a bounded per-frame duration;
   - add a low-rate warning/guard if the device/API cannot support the mode reliably.

4. If `tx_send_stream()` hangs indefinitely only at <= 1 MHz, escalate as an API/device issue, but still fix SGStudio so stop does not spin the UI thread.

## 2026-06-26 update: new field evidence

User-provided facts after the first static pass:

- The issue is observed while Streaming continues playing; `stopBusiness()` is not being called.
- `tx_send_stream()` returns quickly.
- Program memory does not keep growing.
- The visible symptom is main-thread stutter / slow response, not a confirmed hard stop path.

## Revised hypothesis

The previous `stopBusiness()` busy-wait remains a real UI-freeze risk, but it does not explain this specific field case.

For the current case, the stronger hypothesis is:

1. `tx_send_stream()` is not pacing the producer/consumer loop at low sample rates.
2. SGStudio sends fixed 2,000,000-point frames regardless of sample rate.
3. When the API returns quickly, sender/generator run as fast as CPU/disk/memory copies allow.
4. Queue size stays bounded, so application memory can remain stable.
5. The UI can still become sluggish because worker threads continuously perform large reads/copies/transforms, frequent device calls, and cross-thread progress work without a realtime pacing budget.

This points more toward an application scheduling/backpressure problem than an API blocking problem for the currently reported symptom.

## Additional static findings for runtime stutter

1. `FancyDevice::downloadDataRealTime()` does not stop or back off on `tx_send_stream()` failure/warning.
   - `sendStatus < 0`: stores `m_errorCode`, logs `" error"` after the locked block, returns `false`.
   - `sendStatus > 0`: stores `m_warnningCode`, logs `" tx_send_stream warning"`, then returns `true`.
   - In both cases the sender loop continues to request the next frame immediately.

2. If low sample rates make the SDK return a fast non-zero status, the current code can enter a high-frequency error/warning loop.
   - Memory remains bounded because `DataSender` has a max queue size of 10.
   - UI can still become sluggish because logging is synchronous/global enough to hurt the process, and generator/sender continue doing large frame work.

3. The streaming data path has no software pacing by frame duration.
   - It assumes `tx_send_stream()` itself blocks or backpressures.
   - With quick successful returns, the program sends as fast as generator/device calls allow.
   - With quick failed/warning returns, the program also retries as fast as generator/device calls allow.

4. The current progress/UI signal path is probably not the main source:
   - `playbackPosChanged` is throttled to roughly once per second.
   - `DeviceManager` realtime status is also polled once per second.
   - `FancyDevice::getRealTimeStatus()` explicitly avoids taking the device mutex in streaming mode.

## Updated recommended checks

Before changing algorithm behavior, capture these counters for the bad case:

- `tx_send_stream()` return code histogram: count of `0`, `<0`, and `>0`.
- frames per second entering `FancyDevice::downloadDataRealTime()`.
- bytes per second generated/sent from SGStudio's perspective.
- number of `qWarning()` lines per second for stream send/config.
- sender/generator thread CPU while UI is sluggish.

The key decision is whether the bad low-rate case is:

- fast success (`sendStatus == 0`): missing pacing/backpressure;
- fast warning/error (`sendStatus != 0`): missing failure backoff plus possible log storm;
- mixed: both problems.

## 2026-06-26 update: realtime status bridge suspicion

New user observation:

- While Streaming is active, execution still enters `DeviceRuntimeBridge::onDeviceRealTimeStatusUpdated(const Core::DeviceRealTimeStatus &status)`.
- The expected Streaming realtime UI need is only throughput, but the bridge path still performs broader runtime-status work.

Static question to answer next:

- Whether this once-per-second status path does unnecessary main-thread/property work during Streaming.
- Whether `refreshDeviceFeatureSpecs(currentDevice)` is being called on every realtime status tick even though feature specs are effectively device-open/static state.
- Whether reducing this path during Streaming can remove UI stutter, or whether it is only a secondary load amplifier.

## Static conclusion for realtime status bridge

Observation:

- `DeviceRuntimeBridge::onDeviceRealTimeStatusUpdated()` calls `refreshDeviceFeatureSpecs(currentDevice)` on every runtime-status tick.
- `refreshDeviceFeatureSpecs()` calls `device->triggerSourceSpecs()`.
- `FancyDevice::triggerSourceSpecs()` takes `FancyDevice::m_mutex` and refreshes XPPS availability from GNSS state.
- `FancyDevice::downloadDataRealTime()` also takes `FancyDevice::m_mutex` around `tx_send_stream()`.

Inference:

- The streaming-specific optimization in `FancyDevice::getRealTimeStatus()` avoids the device mutex, but `DeviceRuntimeBridge` re-enters the mutex via feature-spec refresh.
- During Streaming, the only status value currently needed by the status bar is throughput. Refreshing common feature specs on the same tick is unnecessary and can create main-thread lock contention plus property/metadata churn.

Planned minimal fix:

- In `DeviceRuntimeBridge::onDeviceRealTimeStatusUpdated()`, skip `refreshDeviceFeatureSpecs(currentDevice)` while the active business provider kind is `Streaming`.
- Continue writing `DeviceRealTimeStatus` so throughput remains updated.
- Preserve the existing feature-spec refresh behavior for non-streaming modes, where GNSS/XPPS availability updates may still matter.

## Remaining FancyDevice mutex audit

The periodic/main-thread risk found in this pass was `DeviceRuntimeBridge -> refreshDeviceFeatureSpecs -> FancyDevice::triggerSourceSpecs()`. That is now guarded for active Streaming.

Other `FancyDevice::m_mutex` paths that can still be reached from UI/main-thread code, but are user-action or lifecycle driven rather than periodic:

- GNSS dialog:
  - `GpsInfoDialog::refreshGnssConfig()` calls `queryGnssSettings()`, but HTRA returns before locking when `m_mode == StreamModeId`.
  - `GpsInfoDialog::applyGnssConfig()` also checks `m_isStreaming`.
  - `refreshCurrentDeviceFeatureSpecs()` is only called after applying GNSS config, which is disabled during Streaming.
- Device settings:
  - `DeviceSettingPanel::refreshSystemClockOut()` calls `querySystemClockOut()`; HTRA locks and calls `device_query_clock()`.
  - `DeviceSettingPanel::applySystemClockOut()` calls `configureSystemClockOut()`; HTRA locks and calls `device_config_clock()`.
  - `DeviceSettingPanel::applyFanMode()` calls `configureFanMode()`; HTRA locks and calls `device_config_fan()`.
  - These can wait behind active stream sends if the panel is opened/used during low-rate Streaming.
- GPIO:
  - `MainWindow::refreshGpioDialogState()` calls `queryGpioState()`; HTRA locks.
  - `MainWindow::applyGpioState()` calls `configureGpioState()`; HTRA locks and calls GPIO APIs.
  - This is also user-triggered, not periodic.
- Lifecycle/configuration:
  - close/switch/reset/configuration/sweep overlay paths intentionally lock; if invoked during Streaming they are expected to coordinate with the stream sender and may wait.

Conclusion:

- After the RuntimeBridge guard, no other obvious once-per-second main-thread path remains that should continuously contend with `downloadDataRealTime()`.
- User-action panels can still pause while low-rate Streaming is actively sending; that is acceptable for now unless product requirements say device settings/GPIO must stay fully responsive during Streaming.

## 2026-06-26 update: breakpoint interpretation

New user hypothesis:

- In a breakpoint/debugger scenario, earlier stream data may already have been consumed by the device.
- While execution is paused, the device-side FIFO/buffer can drain.
- After resuming, the next `tx_send_stream()` call can therefore return quickly because the device has free buffer space.

Static interpretation:

- This is consistent with a synchronous/backpressured stream-send API.
- Continuous low-rate Streaming can still be slow even if a single post-breakpoint call appears fast.
- The fixed SGStudio frame is 8 MB / 2,000,000 complex points; at low sample rates each frame represents long realtime duration, so continuous calls can naturally spend more time waiting for device buffer availability.
