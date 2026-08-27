# Remove Dead SystemBusyStatus Binding

## Goal

- Remove the obsolete `SystemBusyStatus` property and the `DeviceInfoWidget` warning binding if the large-waveform cleanup has left them without producers.

## Local Hypothesis

- `SystemBusyStatus` is now dead infrastructure: source search shows only three live references in project code.
- Those references are property creation in `CoreRuntimeServices`, setup/callback in `MainWindowDeviceController`, and the setup call in `MainWindow`.
- There are no remaining writers after the large-waveform handover/busy-path cleanup, so removing the whole chain should have no runtime effect.

## Cheap Disconfirming Check

- Search `src/**` for `SystemBusyStatus` and confirm there are no setters or additional consumers beyond the three known references.

## Planned Changes

1. Remove `setupSystemBusyStatusBinding()` declaration/definition and its call site.
2. Remove the dead `SystemBusyStatus` property creation.
3. Clean up now-unused comments/includes around the binding.
4. Update repo memory to record that the property chain has been removed.

## Validation

- Re-run targeted search for `SystemBusyStatus` under `src/**`.
- Run diagnostics on the touched files.
- No build in this task.
