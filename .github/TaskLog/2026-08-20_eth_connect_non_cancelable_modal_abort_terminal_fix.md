# ETH Connect non-cancelable modal and abort terminal fix

Date: 2026-08-20  
Status: implemented; static verification complete  
Verification level: static

## Scope

- Make an active single/dual-port ETH Connect batch non-cancelable from the dialog Cancel button, title-bar close button, Escape, and dialog rejection.
- Present ETH Connect through the existing asynchronous modal dialog path so the parent MainWindow cannot be edited during the connection workflow.
- Keep `DeviceRuntimeProfileCoordinator` busy and keep pipeline updates suspended until an abort-triggered source/no-current lifecycle request has actually completed.
- Retain the existing ordered independent-attempt behavior and watchdog policy.
- Do not add new `DeviceManager` lifetime guards and do not change watchdog timeout/continuation policy.

## Cause

`abortDeviceSwitch()` currently invalidates the target request, queues a source/no-current switch, and immediately calls `finishSwitch()`. The controller therefore observes a false terminal state and may unregister the provisional ETH object before the queued rollback result has returned to the main thread. That result still carries the provisional object as `oldDevice`, which can become a stale pointer. Independently, `EthConnectDialog::runAsShow()` is non-modal, so the rest of MainWindow remains interactive while current selection and profile restoration are incomplete.

## Success criteria

- No user-driven cancellation signal or controller cancellation branch remains for an active ETH batch.
- Connecting state disables Cancel and hides the title-bar close action; `reject()` is ignored until the attempt/batch reaches a normal terminal UI state.
- ETH Connect uses `runAsync()` rather than `runAsShow()`.
- Abort during an active lifecycle switch queues rollback to the registered source or explicit no-current state and does not call `finishSwitch()` until the matching `deviceSwitchFinished` arrives.
- Pipeline suspension and coordinator busy state remain active throughout that rollback.
- Watchdog still records the same timeout outcome and advances only after the coordinator reports the real terminal state.
- Existing partial-success behavior remains unchanged.
- Touched source files remain listed in Core CMake and `git diff --check` passes.

## Implementation result

- Removed the dialog cancellation signal, request state, controller cancellation slot, and batch cancellation branches.
- Connecting state now disables `Cancel`, hides the title-bar close action, and ignores `reject()`; idle and all-failed states restore normal close behavior.
- `MainWindowDeviceController` now opens ETH Connect with the existing asynchronous modal `runAsync()` path.
- Active lifecycle abort now retains coordinator busy state and pipeline suspension while the source/no-current rollback is pending. A matching `deviceSwitchFinished` or `deviceSwitchInvalidated` result is required before `finishSwitch()`.
- The watchdog remains the only active-batch abort caller and retains the existing timeout and ordered-attempt behavior.
- Current KnowledgeBase documents and the index were updated; historical TaskLogs remain unchanged as implementation history.

Static verification:

- no user-cancellation signal, slot, request method, or batch cancellation state remains under `src/plugins/core`;
- ETH Connect has one `runAsync(nullptr)` presentation path and no `runAsShow(nullptr)` path;
- all five touched Core source/header files remain listed in `src/plugins/core/CMakeLists.txt`;
- source review confirms failed provisional endpoint cleanup still requires both attempt outcome and coordinator terminal latches;
- `git diff --check` passes.

No build or runtime test was run because this task used the repository-default static verification level.
