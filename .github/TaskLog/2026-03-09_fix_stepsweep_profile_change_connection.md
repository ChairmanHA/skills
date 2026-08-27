# StepSweep profile change connection fix

## Problem

- `StepSweepPanel::argsChanged` was connected to `StepSweepBusiness::onDeviceProfileChanged()` only inside a `deviceThread->started` connection created in the constructor.
- At constructor time `deviceThread` is `nullptr`, so that connection is never established.
- As a result, analog profile edits such as `DwellTime_Analog` do emit `argsChanged`, but the business layer never receives it.
- `startBusiness()` also reconnects `QThread::started` and `QThread::finished` on every call, which can cause duplicate trigger paths if the same thread object is reused.

## Design

- Connect `m_widget->argsChanged` to `onDeviceProfileChanged()` directly in the constructor because this UI-to-business relationship does not depend on thread startup.
- Create the device thread connections only when a new `deviceThread` object is allocated.
- Guard `startBusiness()` against restarting an already running thread.
- Keep `onDeviceProfileChanged()` runtime guards unchanged so inactive business state still suppresses device reconfiguration.

## Validation

- Editing analog properties should now reach `onDeviceProfileChanged()` whenever the business is active and the device thread is running.
- Re-entering `startBusiness()` with an existing running thread should no longer add duplicate `started/finished` connections.