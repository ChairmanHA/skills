# Analog deviceConnected signal fix plan

## Problem

- `src/plugins/analog/analogmodulationplugin.cpp` connects to `Core::Internal::MainWindow::deviceConnected` and expects it to mean "device has opened and usable UID/model are available".
- Actual runtime path is `DeviceManager::setCurrentDevice()` -> immediate `currentDeviceChanged(device)` + immediate `currentDeviceOpenStateChanged(false)` -> async worker open -> final `currentDeviceOpenStateChanged(opened)`.
- `src/plugins/core/mainwindowdevicecontroller.cpp` currently emits `deviceConnected` only in `onCurrentDeviceChanged()`, guarded by `device->isOpen()`.
- Because `currentDeviceChanged` is emitted before async open finishes, that guard is usually false, so `deviceConnected` is never emitted on the normal USB/ETH connect path.

## Fix direction

- Move the effective `deviceConnected` emission to the `currentDeviceOpenStateChanged(true)` path, where open completion is guaranteed.
- Keep `currentDeviceChanged()` responsible for early UI/property refresh only.
- In analog plugin, after wiring the signal, bootstrap once from `DeviceManager::currentDevice()` when a device is already open, so plugin-load ordering cannot miss the current device params.
- Add a device-license state in `DeviceParamsManager`: when `FixedLic=false`, clear waveform-generation availability on every `currentDeviceChanged`, then re-check on `deviceConnected` after open succeeds.
- Propagate the license result into `AnalogPlaybackBusiness` so analog modulation panels are disabled while generation is unavailable, stale ready-state is cleared, and late results from the previous device are ignored.
- If the post-connect license check fails, show a single error dialog: `许可证校验失败，调制波形生成不可用。`

## Validation

- Build Debug.
- Verify no compile errors.
- Expected runtime behavior: after device open completes, analog `DeviceParamsManager::onDeviceConnected()` receives model/uid once; if plugin initializes after a device is already open, it still receives a bootstrap update.
- Expected UI behavior with `FixedLic=false`: on device switch, analog waveform-generation UI becomes unavailable immediately during handover/connecting, then re-enables only if the newly connected device passes license validation.

## Knowledge base update

- Add a dedicated knowledge-base article for analog runtime license gating, including `FixedLic=true/false` semantics, signal timing, USB vs ETH differences, and UI behavior.
- Add cross-references from `device_open_ui_config_flow.md`, `device_status_ui_feedback.md`, and `Index.md` so later debugging can enter from either device-timing or UI-feedback angles.