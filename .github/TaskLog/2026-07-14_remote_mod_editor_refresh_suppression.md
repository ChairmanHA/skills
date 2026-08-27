# Remote MOD Editor Refresh Suppression

## Scope

- Make minibar-helper MOD editors follow the existing Remote Sweep request model:
  an in-flight request must not disable the editor, and another edit may be sent
  before the earlier response returns.
- Avoid replaying an unchanged MOD business snapshot through every editor control.
- Investigate and remove the visible `LabelButton::labelText` left-then-right movement
  seen on Raspberry Pi when a MOD panel opens or a parameter response arrives.
- Keep Main as the authoritative business/profile owner and retain the existing MOD
  request schema, revision conflict handling, response-id filtering, and snapshot
  sequence ordering.

## Evidence And Assumptions

### Observations

- `RemoteModEditorHost::emitEnabledChange()` and `emitEditorAction()` call
  `setPending(true)`, and `setPending()` disables the complete host with
  `setEnabled(false)`. `applyBusiness()` re-enables it only after a later snapshot.
  This is the visible intermediate disabled state reported in the field.
- Remote Sweep no longer connects request transport state to UI disabling.
  `MinibarClient` already supports multiple in-flight requests, discards response
  payloads older than `m_latestRequestId`, and lets `RemoteMiniBarWindow` reject
  lower snapshot sequences.
- FM and the other hosted form editors configure each `LabelButton` once with a
  left-aligned title and right-aligned value. No later source path resets those
  alignment properties.
- `RemoteModEditorHost::applyBusiness()` nevertheless calls `adjustSize()` on the
  editor and host, then invalidates/activates their layouts for every snapshot.
  The visible MOD dialog is fixed to the popup width by its parent. Thus a response
  can first shrink the right-aligned value's container to its content size and then
  let the parent layout expand it again. Remote Sweep does not resize its panel while
  applying ordinary snapshots.
- Selecting a MOD menu entry shows the editor from the already-held snapshot and then
  sends a selection request. The response commonly differs only in revision/sequence,
  so the current unconditional editor replay and geometry pass are redundant.
- All implementation sources in scope are included by
  `src/app/minibarhelper/CMakeLists.txt`.

### Inference

- The reported value text does not change from right alignment to left alignment;
  it stays right-aligned inside a temporarily narrower widget. Removing redundant
  visible-time `adjustSize()` calls should remove the apparent left-to-right travel.
  This inference is based on the current geometry call chain and must still be
  confirmed on Raspberry Pi.

### Behavioral Assumption

- "Immediately effective" means the user edit is emitted immediately and the editor
  remains interactive; it does not introduce a second optimistic business-state
  owner in the helper. Enum/switch controls retain their direct interaction state,
  while numeric values remain authoritative-snapshot-driven like Remote Sweep.

## Design

1. Remove MOD editor pending state and all host-wide `setEnabled(false/true)` writes.
2. Preserve the current `MinibarClient` response-id and window sequence filters; do
   not add a MOD-specific queue or duplicate IPC command.
3. Cache the last business UI state applied by `RemoteModEditorHost`. Always accept a
   newer revision for subsequent requests, but call `RemoteModEditor::applySnapshot()`
   only when profile, editor state, or enabled state actually changes.
4. Mark a local editor action as requiring one authoritative refresh. This allows an
   accepted correction or rejected edit to restore enum/switch presentation even if
   the returned logical data equals the last cached snapshot.
5. Perform spacer normalization once when an editor is constructed. Do not call
   editor/host `adjustSize()` or force layout activation during ordinary snapshot
   application; `showModPanelForBusiness()` remains the owner of initial popup sizing.

## Success Criteria

- Editing any MOD field does not disable the editor while its request is in flight.
- A second edit can be emitted before the first response returns.
- Older response payloads remain discarded by the existing request-id filter, while
  the latest response or a newer authoritative event can write corrected values back.
- Reapplying a MOD business with unchanged profile/editor/enabled state updates its
  revision bookkeeping but does not replay editor setters or force geometry changes.
- A local enum/switch edit can still be corrected by the next authoritative snapshot,
  including rejection to the previously cached value.
- Opening FM and receiving its immediate selection response no longer causes the
  value labels to travel left and then back right through a shrink/expand cycle.
- No Main business logic, protocol schema, plugin UI file, shared `LabelButton`, or
  theme stylesheet is changed.

## Verification Level

- `static`: inspect state comparison, local-edit correction, response ordering, popup
  sizing ownership, active CMake inclusion, and focused diff/whitespace checks.
- Raspberry Pi runtime confirmation remains required for the field-only visual timing
  symptom; no build or run is requested in this task.

## Static Verification Performed

- Confirmed no `RemoteModEditorHost` pending API/state or host-wide request-time
  `setEnabled(false)` path remains. Editor actions still emit synchronously and do not
  acquire a UI interaction lock.
- Confirmed the host compares profile, `editorState`, and enabled state before calling
  `RemoteModEditor::applySnapshot()`, while always updating the received revision used
  by the next request.
- Confirmed both enabled edits and editor actions set the one-shot authoritative refresh
  flag. An unchanged rejection/correction snapshot therefore still restores a directly
  edited switch/enum; a normal equivalent selection response skips editor setters.
- Confirmed `MinibarClient` still accepts multiple timers/in-flight ids and publishes
  only the latest response payload, and `RemoteMiniBarWindow` still rejects lower
  snapshot sequences. Error/conflict paths remain able to publish an ordinary
  authoritative EVT.
- Confirmed `RemoteModEditorHost` contains no `adjustSize()` call after the change.
  Spacer trimming runs once in `rebuildEditor()`, while
  `showModPanelForBusiness()` retains initial dialog sizing before the popup is shown.
- Confirmed the changed host sources remain in the active `SGStudioMiniBar` CMake target.
  Focused source assertions, trailing-whitespace scans, and `git diff --check` pass;
  Git reports only the repository's existing LF-to-CRLF conversion warnings.
- No build or runtime test was run. Raspberry Pi must confirm that FM no longer shows
  the response-time value-label left/right travel and that rapid consecutive edits have
  the expected revision-conflict/writeback behavior.
