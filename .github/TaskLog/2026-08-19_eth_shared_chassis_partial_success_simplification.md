# ETH shared-chassis partial-success simplification

Date: 2026-08-19  
Status: implemented; static verification complete  
Verification level: static

## 1. Goal

Simplify the shared-chassis ETH Connect batch from all-or-nothing transaction semantics to ordered independent attempts.

The dialog still attempts selected ports strictly in displayed order and waits for each coordinator transaction to become terminal before starting the next port. A successful endpoint is committed immediately and is never removed merely because another selected endpoint fails.

## 2. Product behavior

- Both selected ports are attempted unless the user cancels.
- A successful endpoint remains registered/open and can be used normally.
- A failed newly created endpoint is removed after that attempt reaches coordinator terminal state.
- A failed attempt may still use the coordinator's existing per-switch rollback to its immediate source. This is local switch recovery, not batch rollback.
- If at least one selected endpoint succeeds, finish and close the ETH Connect dialog. The last successful endpoint remains current.
- If every selected endpoint fails, keep the dialog open and show one failure summary.
- Cancellation stops future attempts, aborts an active attempt through the coordinator, preserves already completed successes, and closes the dialog after terminal cleanup.
- Title-bar A/B remains derived from the endpoints that are actually open. No direct A/B state is added.

Expected final-current matrix:

| 5000 | 5001 | Result |
| :--- | :--- | :--- |
| success | success | Both retained; 5001 current |
| success | failure | 5000 retained and current |
| failure | success | 5001 retained and current |
| failure | failure | No new endpoint committed; dialog stays open |

## 3. Simplification scope

### `MainWindowDeviceController`

Remove state and branches used only by final batch rollback:

- pre-batch current UUID / no-current snapshot;
- batch-created endpoint set;
- rollback phase, rollback error, and rollback watchdog branch;
- all-success-only commit gate;
- final restoration and deletion of successful endpoints.

Keep only the ordered queue, attempt result/terminal latches, per-attempt watchdog, cancellation, local cleanup of a failed newly created endpoint, and the batch-active UI gate.

### `DeviceRuntimeProfileCoordinator`

Remove `requestNoCurrent()` and `m_switchingToNoCurrent`; they exist only for the deleted final batch rollback. Keep coordinator rollback for an individual failed switch and keep `abortDeviceSwitch()` for cancellation/watchdog recovery.

### `EthConnectDialog`

Remove rollback wording and rollback-error formatting from the all-failed summary. Partial success and cancellation close the dialog.

## 4. Success criteria

- No batch-level restore-to-original-current function or state remains.
- No successful endpoint is unregistered by another row's failure.
- Failed newly created endpoints are still cleaned only after coordinator terminal state.
- Port attempts remain serial; no next attempt starts while the coordinator is busy.
- Partial success accepts the dialog; all-failed keeps it open with one summary.
- Cancel preserves earlier successes and waits for active abort completion before closing.
- `requestNoCurrent()` and its coordinator-only flag have no remaining declaration, implementation, or call site.
- Existing title-bar A/B projection and device I/O ownership remain unchanged.
- All touched files remain included by `src/plugins/core/CMakeLists.txt` and `git diff --check` passes.

No build or runtime test is planned unless explicitly requested, following the repository-default static workflow.

## 5. Implementation result

Implemented on 2026-08-19:

- `MainWindowDeviceController` now finalizes a batch through one function: cancellation or any success closes the dialog; all-failed shows one summary.
- Batch-level original-current restoration, created-endpoint collection, rollback phase/error/watchdog state, and all-success-only commit branches were removed.
- A failed newly created endpoint is still removed after both its outcome and coordinator-terminal latches are observed. Successful endpoints never enter that cleanup path.
- `DeviceRuntimeProfileCoordinator::requestNoCurrent()` and `m_switchingToNoCurrent` were removed. Per-switch source rollback and abort recovery remain.
- `EthConnectDialog` no longer formats transaction rollback or rollback-error text.
- Durable ETH design documents and the KnowledgeBase index now describe ordered independent attempts and partial-success retention.

Static verification:

- reviewed `success -> failure` sequencing: the failed second switch restores its immediate source, so the first successful endpoint remains current and registered;
- reviewed `failure -> success` sequencing: the first failed new endpoint is locally removed, then the second success remains current and registered;
- no removed rollback symbol or `requestNoCurrent()` call remains under `src/plugins/core`;
- exactly one `connectRequested` consumer remains;
- the controller contains no direct device open/close/H2 lifecycle call;
- all six touched source files remain listed in `src/plugins/core/CMakeLists.txt`;
- working-tree and staged `git diff --check` pass.

No Debug build or runtime/hardware test was run because this task used the repository-default static verification level.
