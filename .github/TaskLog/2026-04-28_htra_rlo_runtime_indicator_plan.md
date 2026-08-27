# HTRA RLO runtime indicator plan

## Goal
- Query HTRA runtime state via `device_query_state()` during realtime polling.
- Surface warning codes 17/18 through existing realtime warning pipeline.
- Show a compact `RLO` label immediately left of device UID in the status bar when those warnings are active and width is sufficient.
- Hide the `RLO` label when width is insufficient, instead of squeezing or eliding it.

## Local hypothesis
- `FancyDevice::updateRealTimeStatus()` is the owning runtime polling point; adding `device_query_state()` there can safely populate `m_warnningCode` for 17/18 without changing the broader status model.
- `DeviceInfoWidget` already owns responsive status-bar layout, so the width-gated `RLO` indicator should be implemented there rather than in the controller.
- To avoid duplicate UI signals, warning codes 17/18 should not enter the generic rolling warning queue.

## Validation
- Build touched slice with existing Debug build task and fix any compile errors.
