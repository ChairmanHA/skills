# ETH shared-chassis dual-port sequential connect plan

Date: 2026-08-19  
Status: superseded for failure handling by `2026-08-19_eth_shared_chassis_partial_success_simplification.md`  

> The original all-or-nothing rollback design below is retained as implementation history. Current behavior preserves successful endpoints when another selected port fails; see the superseding TaskLog above.

## 1. Goal

Extend the existing `ETH Connect` dialog for a shared chassis. The user enters one IP address, sees fixed port rows for 5000 and 5001, selects the requested rows through trailing checkboxes, and clicks `Connect` once. Both rows are selected by default.

The application then attempts the ports strictly in displayed order. It waits for each complete device-switch transaction, including any profile rollback or abort, before starting the next attempt. Both rows are attempted even when the first row fails. The whole request is transactional: if any row fails or times out, the batch is a failure and newly created endpoints from the batch are cleaned up; a later retry creates them again.

After every requested endpoint has been attempted:

- if all endpoints succeeded, close the dialog;
- if any endpoint failed, restore the pre-batch current when possible, remove endpoints created by this batch, keep the dialog open, and show one transaction-failure summary;
- pre-existing remembered/open endpoints are not removed merely because the batch failed;
- only an all-success batch commits its newly opened endpoints and leaves the last successfully completed row current. In the normal `5000 -> 5001` success case, port 5001 is current;
- title-bar A/B remains an automatic projection of the two open endpoints. The dialog must not set A/B state directly.

## 2. Scope

Planned source changes:

- `src/plugins/core/ethconnectdialog.h`
- `src/plugins/core/ethconnectdialog.cpp`
- `src/plugins/core/mainwindowdevicecontroller.h`
- `src/plugins/core/mainwindowdevicecontroller.cpp`
- `src/plugins/core/deviceruntimeprofilecoordinator.h`
- `src/plugins/core/deviceruntimeprofilecoordinator.cpp`
- `src/plugins/core/devicemanager.h`
- `src/plugins/core/devicemanager.cpp`
- `src/plugins/core/mainwindow.cpp`
- `src/plugins/core/mainwindow.h` (only if a batch-state accessor/member is needed for A/B button refresh)

Boundaries:

- `DeviceManager` ownership and I/O serialization remain authoritative; only the stale-result terminal notification/abort contract is extended;
- `DeviceRuntimeProfileCoordinator` profile save/restore and reachability semantics remain authoritative; switch watchdog/abort completion is added;
- `MainWindow::updateEthQuickSwitchButtons()` A/B discovery and mapping remain unchanged; only its batch-busy enable gate is added;
- device SDK/H2 APIs;
- startup restore, Device List, scanner, updater, or firmware behavior.

This TaskLog is the implementation boundary. A new lower-level batch-open API is not part of the design.

## 3. Current evidence

### Observation

1. `EthConnectDialog` currently owns one IP edit and one port edit and emits one `{IP, port}` request.
2. `MainWindowDeviceController` currently tracks one `m_pendingEthDevice` and one `m_provisionalEthDevice`.
3. A pending ETH success in `onDeviceOpenStateChanged(true)` immediately accepts the dialog.
4. A switch request can complete through several paths:
   - the target is already current and open, so no asynchronous completion signal is emitted;
   - a new or closed endpoint is opened by `DeviceManager`;
   - a retained non-current ETH first performs the asynchronous ping preflight;
   - an open failure can trigger coordinator rollback to the previous source device;
   - a preflight failure can unregister non-current endpoints in the same-IP 5000/5001 group.
5. `deviceSwitchFinished` is emitted before `DeviceRuntimeProfileCoordinator` has necessarily completed restore/rollback and cleared its busy state.
6. When emitted, `switchProgressChanged(false)` represents the end of the coordinator transaction, including rollback; stale-result early returns currently prevent it from being emitted.
7. Title-bar A/B is recomputed from `DeviceManager::allDevices()`: A is same-IP port 5000, B is same-IP port 5001, and both must be open.

### Inference

The second endpoint must not be started directly from `currentDeviceOpenStateChanged`, `deviceSwitchFinished`, `switchFailed`, or `switchPreflightFailed`. Those signals can arrive while the coordinator is still busy or before rollback is complete. They may record the current attempt outcome, but queue advancement must wait for both an attempt outcome and a coordinator terminal/idle notification, including watchdog abort recovery.

The implementation must store endpoint values, not device pointers, for future rows. Shared-chassis cleanup can unregister a retained target after a failed ping, so every row must resolve `{IP, port}` immediately before its own attempt.

## 4. UI design

### 4.1 Layout

Always show the two supported product ports and place one checkbox in the trailing control column of each row:

```text
IP Address       [ 192.168.1.100                 ]
Port  1          [ 5000 ] 
Port  2          [ 5001 ] 
Local Interface  Physical: 192.168.1.1
```

Rules:

- Keep both port-display widths stable and align the two trailing checkboxes in the column previously occupied by `+`/`-`.
- Port values are fixed, read-only product endpoints; the user chooses which endpoint participates through its checkbox.
- Use the same standard `QCheckBox`, checked-by-default behavior, 24 px themed indicator resources, and theme refresh pattern as Digital Modulation's `Default Trim Download` control.
- Both checkboxes are selected whenever a fresh dialog is constructed. The user may select either endpoint alone or both endpoints.
- Do not add persistent explanatory text beside the checkboxes; use accessible names and translated tooltips to identify each endpoint.
- During connection, disable IP, both port displays, both checkboxes, local-interface controls, and `Connect`. Show an explicit `Cancel` action; the window close button/reject path is routed to the same cancel request instead of being silently ignored.
- Fixed read-only port displays do not invoke the Linux software keyboard.

### 4.2 Validation

- Preserve IPv4 validation.
- Keep the controller's existing authoritative restriction that only ports 5000 and 5001 are accepted.
- Require at least one checked endpoint before enabling `Connect`.
- Emit ports in visual row order; do not sort them. This makes the final-current rule deterministic.
- Local-interface and same-subnet hints remain based on the shared IP only; no per-port route logic is added.

### 4.3 Status text

While running, display the current position and endpoint, for example:

```text
Connecting 1/2 192.168.1.100:5000...
```

After all attempts, if at least one failed, show one summary in the existing dialog status area. Do not display an intermediate modal or intermediate inline error. The summary includes every attempted endpoint and makes the rollback explicit, for example:

```text
Transaction failed; batch-created endpoints were rolled back.
192.168.1.100:5000  Connected, rolled back
192.168.1.100:5001  Failed: <SDK/coordinator error>
```

The summary is the single connection error prompt. The dialog becomes editable again so `Connect` can retry the same requested set; retries create a fresh object for any endpoint that was cleaned up by the failed transaction. Already-registered endpoints are still resolved by exact `{IP, port}` and are never duplicated.

## 5. Dialog API changes

Replace the single-port request signal with one request carrying the ordered ports:

```cpp
void connectRequested(const QString &ipAddress, const QList<quint16> &ports);
```

The signal is used by one in-process direct connection, so no queued metatype registration is required. Include the container type explicitly in the header.

Add narrowly scoped dialog helpers:

- return the ordered checked ports;
- enable/disable all endpoint editing controls together;
- show progress for `{attempt index, total, endpoint}`;
- show the final batch result summary.
- request cancellation while a batch is active;
- keep the dialog visible until an active abort/rollback has reached a terminal state.

Avoid exposing line edits or making the dialog own device lifecycle state.

## 6. Controller batch state

Treat every dialog request as a short ordered batch. A normal single-port request is a batch of length one, which keeps one code path for both modes.

The controller needs UI-orchestration and transaction state, conceptually:

```cpp
struct EthConnectBatchState {
    QString ipAddress;
    QList<quint16> ports;
    int attemptIndex = 0;
    QStringList resultLines;
    QString originalCurrentUuid;
    QSet<QString> createdEndpointKeys;
    bool active = false;
    bool cancelRequested = false;
    bool outcomeRecorded = false;
    bool coordinatorTerminalObserved = false;
};
```

The exact representation may use individual members if that is simpler, but it must preserve these invariants:

- only one endpoint attempt is outstanding;
- the future queue stores only IP/port values;
- an attempt result is recorded at most once;
- the next row starts only after the previous coordinator transaction is terminal and no longer busy;
- `createdEndpointKeys` contains only endpoints created by this batch, never pre-existing remembered endpoints;
- any failure/cancel path enters rollback/cleanup and cannot commit a partial batch;
- closing the dialog when idle clears all batch state;
- dialog destruction must not delete remembered/open devices.

`m_pendingEthDevice` remains the pointer for the current attempt only. `resolveManualEthDevice()` must not clear or overwrite it. The existing single `m_provisionalEthDevice` is retained only for compatibility with the non-batch path; batch-created endpoints are tracked by endpoint key and are explicitly cleaned on transaction failure. This deliberately chooses failure-cleanup/recreate over a multi-provisional lifetime model.

## 7. Sequential state machine

### 7.1 Start

1. Reject a new request if another batch or device switch is already active.
2. Copy the trimmed IP and ordered ports from the dialog.
3. Snapshot the original current UUID (or an explicit no-current state).
4. Clear previous results, created endpoint keys, and set `attemptIndex = 0`.
5. Mark the batch active and notify Device List/A/B controls immediately.
6. Enter connecting UI state.
7. Queue `startNextEthConnectAttempt()` with a zero-delay invocation so synchronous outcomes do not recurse through button handlers.

### 7.2 Start one row

For the current `{IP, port}`:

1. Call the batch-aware `resolveManualEthDevice()` immediately before the attempt. It may exact-reuse an existing endpoint or create a new one, but it must not mutate `m_pendingEthDevice` or silently unregister a different endpoint.
2. If resolution fails, record `Failed: <prepare error>` and mark the attempt terminal. No coordinator wait is needed.
3. If the resolved device is already current and open, record `Connected` and queue the next row. This preserves the existing no-signal fast path.
4. Otherwise set `m_pendingEthDevice`; if newly created, retain the existing provisional marker and add the endpoint key to `createdEndpointKeys`.
5. Update progress text.
6. Call the existing `DeviceRuntimeProfileCoordinator::requestDeviceSwitch()`.
7. If the request is rejected synchronously, perform the existing newly-created-device cleanup, record the error, and queue the next row.
8. If it starts, arm the per-attempt watchdog and wait for a terminal outcome plus coordinator idle/abort completion. Do not start the next row immediately.

### 7.3 Success

When `currentDeviceOpenStateChanged(true)` matches `m_pendingEthDevice`:

1. clear its provisional marker exactly as today;
2. record `Connected` once;
3. clear `m_pendingEthDevice` only in the batch attempt completion handler;
4. disarm the attempt watchdog;
5. do not accept the dialog;
6. mark the outcome and wait for the coordinator terminal/idle latch before advancing.

An open notification for a rollback source cannot count as target success because it does not match the cleared/failed pending target.

### 7.4 Open/switch failure

Use `DeviceRuntimeProfileCoordinator::switchFailed` as the canonical asynchronous target-open failure result:

1. match it to the current pending target;
2. record the endpoint and error once;
3. preserve the pending target identity until the attempt result is recorded, then clear it in one batch-owned completion function;
4. disarm the attempt watchdog;
5. wait for coordinator terminal/idle, because the coordinator may still be rolling back to the source.

While a dialog batch is active, `handleSystemMessage()` must suppress `Device Open Failed` messages belonging to the active transaction without using `m_pendingEthDevice` as the correlation key. Target failure and rollback failure can both generate this message. Count/track expected open-failure notifications from `deviceSwitchFinished`, consume them when the queued `systemMessage` arrives, and never use `singleShot(0)` as the reset mechanism. Unrelated system messages retain current behavior.

### 7.5 Preflight failure

Use `switchPreflightFailed` as the canonical preflight result:

1. record the failure once;
2. preserve the endpoint value, not a raw pointer, before same-IP group unregistration can destroy it;
3. disarm the attempt watchdog;
4. wait for coordinator terminal/idle;
5. resolve the next row from the registry afresh.

Do not assume that a device resolved before the failed ping remains registered.

### 7.6 Coordinator idle and advancement

Do not assume signal order. Maintain two latches for each attempt:

- `attemptOutcomeRecorded`;
- `coordinatorTerminalObserved` (normal idle, explicit abort, or timeout recovery).

Either signal may arrive first. Advance only when both latches are true. This covers the existing path where `switchProgressChanged(false)` is emitted before `switchPreflightFailed`, as well as the normal `switchFailed`-then-rollback-then-idle path.

When both latches are true for a failed attempt, unregister that attempt's endpoint immediately if it was created by this batch and is still registered. This is the explicit failure-cleanup/recreate policy. A pre-existing endpoint is never removed by this step. Then use a queued zero-delay invocation to allow deferred registry detach/delete events to settle before resolving the next endpoint. This queued invocation is not used as an error-message suppression reset.

### 7.7 Finish

After `attemptIndex == ports.size()`:

- if every result is successful, commit: clear batch bookkeeping and accept the dialog;
- if any result failed or the user cancelled, enter transaction rollback before re-enabling the dialog;
- restore the pre-batch current through the coordinator when it still exists; if the original state had no current device, use the coordinator's explicit no-current rollback path;
- after rollback is terminal, unregister only endpoints in `createdEndpointKeys` that still exist and were not part of the pre-batch registry;
- clear batch bookkeeping and show one failure/cancel summary;
- a retry resolves endpoints again and creates fresh objects for endpoints removed by the failed transaction.

Expected final-current matrix:

| Port 1 | Port 2 | Final current |
| :--- | :--- | :--- |
| success | success | Port 2 |
| success | failure | Original source after batch rollback; batch-created endpoints removed |
| failure | success | Original source after batch rollback; batch-created endpoints removed |
| failure | failure | Original source after existing/coordinator rollback; no batch-created endpoint committed |

## 8. A/B behavior

Do not add any dialog-to-title-bar signal.

After same-IP 5000 and 5001 are both registered and open, existing `MainWindow::updateEthQuickSwitchButtons()` will expose:

- A = port 5000;
- B = port 5001;
- checked button = `DeviceManager::currentDevice()`.

For the normal displayed order `5000, 5001`, B is checked only when the transaction commits. If either row fails, the transaction rollback restores the original current and no newly created A/B pair is exposed. No third switch back to A is performed after a successful commit because it would add profile save/restore, pipeline suspension, and business stop/start work unrelated to opening the two endpoints.

During the entire batch, including gaps between attempts and rollback/cleanup, Device List actions and A/B quick-switch buttons are disabled. This requires a batch-state signal/accessor from `MainWindowDeviceController`; checking only `DeviceRuntimeProfileCoordinator::isSwitchInProgress()` is insufficient because the coordinator is intentionally idle between sequential rows.

## 9. Failure and lifecycle boundaries

- Both attempts must stay on the existing coordinator and DeviceManager I/O thread path. The dialog/controller must never call `open()`, `close()`, or H2 APIs directly.
- A success is rolled back if another row fails; this is the selected all-or-nothing transaction policy.
- A failed newly created endpoint is cleaned up after its attempt reaches terminal state. A later retry creates it again. This intentionally avoids adding a multi-provisional collection and avoids retaining half of a failed transaction.
- Pre-existing remembered/open endpoints are not removed by batch rollback or cancellation merely because they were resolved by the batch.
- Endpoints created by the batch are removed only after the pre-batch current has been restored (or the explicit no-current rollback path has completed).
- Existing same-IP group cleanup after a retained ETH ping failure remains authoritative. Batch code must tolerate it and must not recreate a future-row device until the prior coordinator transaction is idle.
- Existing reachability limitations remain: successful ICMP does not prove the endpoint service or stale H2 handle is healthy.
- Each row gets exactly one user-requested attempt. A bounded watchdog is mandatory; on timeout it records failure and invokes the coordinator's abort/terminal recovery before the batch can advance.
- Cancellation stops future rows immediately. If an attempt is active, cancellation first aborts/terminates that coordinator transaction, then performs the same rollback and cleanup as any other batch failure.
- Do not persist the checkbox selection or an A/B endpoint set. Startup restore remains a single-endpoint feature.

## 10. Watchdog, cancellation, and terminal completion

The batch must not treat `switchProgressChanged(false)` as the only completion proof. A watchdog is required for every attempt, including ping/preflight and rollback recovery.

The watchdog timeout must not merely re-enable the dialog. It must call a coordinator-owned abort path that:

1. marks the current switch generation abandoned;
2. prevents a late `DeviceManager` result from mutating the abandoned coordinator state;
3. ends pipeline suspension and clears coordinator busy state exactly once;
4. emits a terminal/idle notification consumed by the batch;
5. lets the batch record timeout failure and enter transaction rollback/cleanup.

`DeviceManagerPrivate::onDeviceSwitchFinished()` must stop silently dropping stale results from the coordinator's perspective. Extend the device-manager/coordinator contract with an explicit invalidated/aborted terminal notification for the `finishedRequestId != requestId` and `currentDevice != device` branches, or route those branches through the same coordinator abort generation. A UI-only watchdog that leaves `m_switchInProgress` true is not acceptable.

The dialog's Cancel action follows the same terminal path:

- between attempts: mark cancellation, skip future rows, then rollback/cleanup;
- during an attempt: request coordinator abort, keep the dialog visible and non-editable until terminal completion, then rollback/cleanup;
- after terminal cleanup: show cancellation status and re-enable editing/close behavior.

`Device Open Failed` suppression is tied to the active batch transaction and an expected-message count observed from `deviceSwitchFinished`, not to `m_pendingEthDevice` and not to a `singleShot(0)` reset. Target failure and rollback failure are both covered. The suppression state is cleared only when the corresponding queued messages are consumed or the watchdog/abort generation invalidates them.

## 11. Implementation sequence

1. Add the fixed 5000/5001 rows, trailing checked-by-default controls, ordered selected-port signal, and Cancel action in `EthConnectDialog`.
2. Make `resolveManualEthDevice()` batch-aware: no pending-pointer mutation or implicit different-provisional cleanup during a batch; preserve the existing non-batch behavior where required.
3. Add batch state, original-current snapshot, created-endpoint keys, transaction rollback/cleanup, and explicit failure-cleanup/recreate semantics in `MainWindowDeviceController`.
4. Add coordinator terminal abort/watchdog support and an explicit stale-result invalidation path through `DeviceManager`.
5. Add canonical result recording for success, switch failure, preflight failure, timeout, cancellation, and rollback failure.
6. Gate advancement on outcome + terminal latches, suppress target/rollback open-failure UI by batch generation/count, and remove `singleShot(0)` suppression reset.
7. Add controller batch-state notification and disable Device List/A/B quick-switch actions for the whole transaction, including inter-attempt gaps and rollback.
8. Add final commit/rollback result formatting; accept the dialog only after an all-success commit.
9. Perform static signal/lifetime review.

## 12. Success criteria

### Static

- Core CMake inclusion remains unchanged; all modified files are already included.
- There is exactly one dialog request consumer and its signature accepts ordered ports.
- No new direct device I/O call exists in dialog or controller.
- No second attempt can begin while the coordinator or batch transaction is active; inter-attempt gaps remain locked.
- Every attempt has a bounded watchdog whose timeout reaches coordinator terminal recovery; no timeout path leaves `m_switchInProgress` set.
- Stale `DeviceManager` results have an explicit invalidated/aborted terminal path rather than a silent return.
- Intermediate `Device Open Failed`, `switchFailed`, and `switchPreflightFailed` paths cannot produce duplicate user prompts for one attempt.
- Target and rollback `Device Open Failed` messages are suppressed by batch generation/count, without `m_pendingEthDevice` or `singleShot(0)` coupling.
- `resolveManualEthDevice()` cannot clear `m_pendingEthDevice` while a batch attempt is active.
- Batch rollback removes only endpoints created by the batch and restores the pre-batch current/no-current state.
- Device List and A/B quick-switch controls remain disabled until commit, rollback, cancellation, or timeout cleanup is terminal.
- A preflight group cleanup cannot leave a future attempt holding a stale `IDevice *`.
- The existing title-bar A/B pairing code is unchanged.
- `git diff --check` passes.

### Debug-run without hardware

- Dialog always displays fixed 5000 and 5001 rows with aligned trailing checkboxes and no layout shift or overlap at supported desktop and target display sizes.
- Both checkboxes are selected by default and use the same themed indicator treatment as Digital Modulation's `Default Trim Download` control.
- Selecting only 5000 emits `[5000]`; selecting only 5001 emits `[5001]`; selecting both emits `[5000, 5001]`.
- Clearing both selections disables `Connect`; unsupported or duplicate ports cannot be emitted by the fixed UI.
- Fixed port displays do not open the Linux software keyboard.
- Cancel is available while a batch is active; the dialog remains visible until abort/rollback/cleanup is terminal.
- Closing/cancel works again after the final summary or cancellation cleanup.

### Shared-chassis hardware

1. `5000 -> 5001`, both succeed: attempts occur serially, dialog closes, title bar shows A/B, B is current.
2. Select only 5000 or only 5001: exactly the selected endpoint is attempted and becomes current on success.
3. 5000 succeeds and 5001 fails: both attempts occur, the transaction is rolled back, batch-created 5000 is removed, the original source is restored, and one final summary is shown.
4. 5000 fails and 5001 succeeds: both attempts occur, the transaction is rolled back, batch-created 5001 is removed, the original source is restored, and one final summary is shown.
5. Both fail: both endpoint errors appear once after the second attempt; no batch-created endpoint remains.
6. One endpoint is already current/open: it counts as success without hanging, and the peer is still attempted.
7. One endpoint is retained/open but unreachable: ping failure finishes group cleanup before the next endpoint is resolved; no stale-pointer crash or false success occurs.
8. Retry after a failed transaction: cleaned endpoints are recreated, no duplicate registry entry is created, and a fully successful retry commits both.
9. Cancel during the first or second attempt: no future row starts, active I/O reaches terminal abort, the original source is restored, and batch-created endpoints are removed.
10. Watchdog timeout in target open, ping, rollback, and stale-result paths: the dialog recovers, coordinator is not left busy, and no later attempt overlaps the timed-out one.
11. During every test, logs show no overlapping coordinator switch, no parallel H2 open, and no extra switch back to A.

## 13. Review focus

Claude review should specifically challenge:

- whether the watchdog/abort path truly clears coordinator state and safely ignores late I/O results;
- whether intermediate `Device Open Failed` suppression can be scoped to the matching active ETH attempt without hiding unrelated failures;
- whether failure-cleanup/recreate has any path that removes pre-existing remembered endpoints;
- whether `resolveManualEthDevice()` has any remaining pending/provisional side effects during a batch;
- whether rollback to an explicit no-current state is implemented without bypassing lifecycle serialization;
- whether any existing signal ordering can record the same attempt twice;
- whether the result summary should remain in the ETH dialog status area or product UX requires one separate `MessageDialog`. The implementation default in this plan is the existing status area to avoid nested/duplicate dialogs.

## 14. Current task result

Implemented against this plan on 2026-08-19.

Source result:

- `EthConnectDialog` now provides fixed 5000/5001 rows with checked-by-default endpoint selectors, ordered selected-port emission, per-attempt progress, explicit Cancel, and one final batch summary.
- `MainWindowDeviceController` now owns the single/dual-port batch state machine, result/terminal latches, per-attempt and final-rollback watchdogs, original-current restoration, batch-created endpoint cleanup, and transaction-scoped open-failure suppression.
- `DeviceRuntimeProfileCoordinator` now provides explicit abort and no-current transitions. `DeviceManager` invalidates abandoned request IDs and emits an explicit request-correlated stale-result notification instead of silently returning.
- Device List and title-bar A/B actions remain disabled for the whole batch. Existing A/B endpoint discovery/mapping remains unchanged.
- `manual_eth_connect_temporary_design.md`, `htra_multi_device_stageA_design_and_debug.md`, and the KnowledgeBase index were synchronized with the implemented behavior.

Static verification:

- all 9 modified source files remain listed in `src/plugins/core/CMakeLists.txt`;
- exactly one `connectRequested` consumer exists and accepts the ordered port list;
- the dialog contains no remaining add/remove-row symbols, handlers, optional-row visibility state, or editable port validation;
- the fixed selectors initialize checked, emit only `[5000]`, `[5001]`, or `[5000, 5001]`, and use the same themed 24 px checkbox resources as Digital Modulation;
- clearing both fixed selectors disables `Connect` through the existing ordered-port validity gate;
- no dialog/controller device lifecycle I/O call was added;
- legacy zero-delay open-failure suppression and the two silent stale-result returns are absent;
- `git diff --check` passes.

No Debug build or runtime/hardware test was run because this task used the repository-default static verification level.

## 15. Selected last-connection restore and startup dialog gate

The selected behavior from commit `6b59ad1620ecc790c4b0b884d71673601ffd5be4` was adapted to the current dual-port ETH implementation:

- `Settings.ini` records the last successfully opened transport and ETH endpoint. USB updates only the transport marker and preserves the last ETH endpoint for the manual dialog.
- The HTRA plugin restores one last successful ETH endpoint through `DeviceManager`; failed startup restore unregisters the temporary manual device so later USB fallback can use the existing runtime coordinator path.
- The ETH dialog reuses the saved IP while retaining the current fixed 5000/5001 batch UI. The old single-port controller changes from the selected commit were not applied.
- The temporary startup ETH dialog is controlled by `SGS_ENABLE_STARTUP_ETH_CONNECT_DIALOG`, defined in the root CMake configuration and defaulting to `OFF`. Special-device builds can enable it with `-DSGS_ENABLE_STARTUP_ETH_CONNECT_DIALOG=ON`.

Static verification for this addition: default-off compile gating is present in the root and Core CMake files, the startup dialog call is the only gated popup path, and no Device List/A-B/coordinator batch source was changed.

## 16. Startup connection intent gate

Scope: compute one immutable startup connection intent in `src/app/main.cpp` before plugin loading and let HTRA consume it before creating a startup manual ETH device.

Policy:

- restore the last successful transport after an application continuity restart (`APP/Reboot=True`), after `--UpdateCompleted`, or during a normal `APP/StartSetting=Last` startup;
- normal `Default` and `User` startups do not restore manual ETH, so USB discovery retains ownership of automatic selection;
- the intent only permits restoration. HTRA must still require the last successful transport to be ETH, a valid 5000/5001 endpoint, no current device, and no competing instance;
- startup ETH failure keeps the existing unregister-and-USB-fallback path;
- no DeviceManager, coordinator, scanner, A/B, or startup-dialog behavior is changed.

Success criteria: the property is set before `PluginManager::loadPlugins()`, is never mutated afterward, Default/User cannot proceed from `tryRestoreLastSuccessfulEthConnection()` into ETH creation, and existing restart/Last/update paths can still attempt the saved ETH endpoint.

Implementation result:

- `main.cpp` snapshots `APP/Reboot`, `APP/StartSetting`, and `--UpdateCompleted` before plugin loading and publishes `SGStudio.RestoreLastDeviceConnection` once.
- HTRA checks the property before reading the saved transport or creating a manual ETH device.
- Static truth table: normal Default/User -> no restore; normal Last -> restore; continuity restart or update-completed -> restore regardless of the configured startup profile.
- `git diff --check` passes. No build or runtime verification was requested.
