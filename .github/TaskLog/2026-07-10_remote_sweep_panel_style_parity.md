# Remote Sweep Panel Style Parity

## Scope

Fix the newly added `RemoteSweepPanel` visual mismatch in `SGStudioMiniBar`:

- Keep the helper minibar main buttons from inheriting changed text colors when the remote Sweep panel is present.
- Bring the remote Sweep popup closer to the previous in-process minibar behavior, where `StepSweepPanel` was hosted as a normal panel and relied on the shared `InfoButton` / `LabelButton` / `SwitchButton` theme rules.
- Keep the helper process boundary intact: no Core plugin, `PropertySystem`, `DeviceManager`, or in-process `StepSweepPanel` dependency in `src/app/minibarhelper`.

## Evidence

- `src/app/minibarhelper/CMakeLists.txt` includes `remotesweeppanel.cpp/.h`, so the helper panel is part of the active CMake target.
- `.github/KnowledgeBase/minibar_cs_helper_scpi_architecture.md` states helper Sweep is a child view that renders typed snapshots and must not load Core/business plugin ownership.
- `src/plugins/core/stepsweeppanel.ui` uses `SwitchButton enabled`, `EnumTextButton btnMode`, and `LabelButton` fields with no panel-local QSS. Its visual style mostly comes from shared theme rules.
- `src/app/minibarhelper/remotesweeppanel.cpp` currently installs a full local QSS for `SwitchButton#remoteSweepEnabled` and `LabelButton#remoteSweepField`, overriding shared button colors, font sizes, checked/disabled text colors, and the original `LabelButton` text/value alignment.
- User-provided screenshots show the previous in-process Sweep panel used left-aligned title text and right-aligned value text. `src/plugins/core/stepsweeppanel.ui` also shows `EnumTextButton btnMode` for Sweep Type, not an inline two-button selector.
- `EnumTextButton` lives in `Controls` and can be used by the helper without `PropertySystem`; copying the full `StepSweepPanel.cpp` would also copy Core/plugin authoring dependencies, so the safer parity point is to mirror the UI structure and widget types while keeping the existing IPC snapshot/request model.
- Follow-up screenshot shows the remote panel content reaches the popup host bottom edge. The helper `QDialog` layout currently has zero bottom margin, while the title label already has visual top/bottom margin.
- The remote enable `SwitchButton` was put into compact mode, which makes the On/Off child buttons lose the original 72 px themed minimum width and stretch with the available cell width.
- Follow-up simplification: remote minibar should temporarily hide the `MOD` button and not mirror MainWindow's provider/modulation gating in the helper. The helper should focus on the current carrier plan snapshot for Sweep.
- Follow-up UI reuse requirement: remote minibar primary buttons should reuse `Controls::LabelButton` like legacy `MiniBarWindow`, not a helper-local `QPushButton` composite class.
- Follow-up refresh issue: RF toggles receive a final main-process snapshot, but unchanged helper buttons are still force-polished, causing a visible full-minibar flicker.
- Follow-up Sweep refresh chain: `RemoteMiniBarWindow::applySnapshot()` forwards every decoded Sweep snapshot to `RemoteSweepPanel::applySnapshot()`. The panel currently calls `renderSnapshot()` unconditionally, which resets `SwitchButton`, `EnumTextButton`, all Sweep `LabelButton`s, `QStackedWidget`, and write availability even when the Sweep payload is identical.
- Follow-up pending issue: clicking RF still changes other button backgrounds because `MinibarClient::requestStarted` drives `RemoteMiniBarWindow::setPendingRequest()`, which calls `setTxButtonsEnabled(false)` and marks Sweep pending. This temporary disabled state is visually visible before the main-process snapshot returns.
- Request ordering requirement: helper interactions should stay enabled while a request is in flight. If a second operation is sent before the first response snapshot returns, the first response snapshot should be treated as stale and ignored in favor of the latest request response.

## Design

1. Reduce `RemoteSweepPanel` local stylesheet to popup-specific unsupported-message rules; do not override shared button text colors or alignment.
2. Let `SwitchButton`, sweep parameter `LabelButton`s, and Sweep Type `EnumTextButton` use the same shared theme rules as the original `StepSweepPanel`.
3. Use original `StepSweepPanel.ui` object names for equivalent remote controls where practical (`enabled`, `btnStartFreq`, `btnStopFreq`, etc.) so future theme rules are easier to reason about.
4. Preserve original `LabelButton` alignment: title left/top side, value right/bottom side.
5. Keep a bottom host margin below `RemoteSweepPanel` so the title/content area has balanced vertical breathing room.
6. Keep the enable switch child buttons at the original themed width instead of compact stretched widths.
7. Hide `RemoteMiniBarWindow`'s `MOD` button for now and stop publishing/consuming MOD state in the remote helper snapshot path.
8. Make remote Sweep availability depend on device open state, main sweep panel presence, and whether the configured carrier plan is helper-supported (`Freq`/`Power`), not on `Mod` or MainWindow provider selection.
9. Replace helper-local `MiniBarInfoButton : QPushButton` with shared `LabelButton`, using the same objectName, alignment, and style refresh pattern as legacy `MiniBarWindow`.
10. Make `RemoteMiniBarWindow` snapshot rendering diff-aware: do not repolish unchanged `LabelButton`s, and only call `setEnabled()` when a button's enabled state actually changes.
11. Make `RemoteSweepPanel` snapshot rendering diff-aware: ignore identical snapshots, avoid redundant `SwitchButton::setStatus()`, avoid redundant `EnumTextButton::setCurrentEnum()` because it emits `currentItemToggled`, update popup item enabled flags only when they change, and only set Sweep field labels/checked/enabled/page index when the target value differs.
12. Remove request-pending UI disabling from `RemoteMiniBarWindow`; pending requests should not disable RF/Frequency/Level/Sweep controls or block a second user operation.
13. Allow `MinibarClient` to have multiple in-flight requests. Apply snapshot payloads only from the latest request response and discard older response payloads. Keep ordinary snapshot events flowing, because they carry initial state and main-process correction state that are not always tied to a request id.
14. Have main-process request handlers return an authoritative snapshot payload in accepted responses so the helper can correlate response snapshots by request id.
15. Preserve helper IPC behavior and numeric editor state except for the explicit Sweep availability simplification above.

## Verification Level

Static.

Success criteria:

- Local QSS no longer overrides remote sweep `LabelButton` / `SwitchButton` text color globally.
- `RemoteSweepPanel` field buttons use shared theme style and original control names.
- Sweep Type is represented by `EnumTextButton#btnMode`, and selecting Freq/Power still emits the existing full Sweep authoring request.
- Sweep popup leaves a bottom host margin instead of letting the content fill to the host edge.
- Enable `SwitchButton` On/Off buttons use matching original widths.
- Remote minibar expanded page no longer shows `MOD`.
- Remote snapshot publishing is not driven by `Mod` property changes.
- Remote Sweep remains available when the current carrier plan is helper-supported, even if MainWindow's current modulation/provider state would normally hide/disable the in-process Sweep entry.
- Remote minibar primary buttons are `LabelButton` instances and the helper-local composite `MiniBarInfoButton` is removed.
- Re-applying an equivalent snapshot does not force-refresh all minibar button styles; only controls with changed text, checked state, enabled state, or Sweep title update.
- Re-applying an equivalent Sweep snapshot does not re-render `RemoteSweepPanel`; changed Sweep snapshots update only the changed child controls.
- RF and other helper requests no longer temporarily disable unrelated controls while waiting for the main response.
- When multiple helper requests are in flight, stale older response snapshots do not overwrite the UI after a newer request has been sent, while ordinary main-process snapshot events can still restore initial Sweep availability and RF state.
- IDE diagnostics do not show new errors in edited files.

## Commit Review: Diff-Aware Rendering And Request Correlation

Reviewed commits:

- `02c4e2c1be2b93acda9f0ed660ade1383c923360`: helper minibar and Sweep snapshot rendering.
- `0d0520b305386a221b8ea3be345c66a785652828`: multiple in-flight requests and response snapshot correlation.

### Why The Changes Are Necessary

1. `LabelButton` is a compound widget. A repolish affects the button, `textLabel`, and
   `infoLabel`; doing that for an unchanged snapshot produces visible whole-minibar
   refresh even though the authoritative state did not change.
2. `RemoteSweepPanel` previously replayed every setter for every snapshot.
   `EnumTextButton::setCurrentEnum()` emits `currentItemToggled`, while repeated
   `SwitchButton`, checked, enabled, popup-item, and page-index writes also create
   unnecessary UI work. Equality and target-value guards keep snapshot application
   idempotent without changing the snapshot-as-authority model.
3. Request-pending disabling was not only transport bookkeeping: `setEnabled(false)`
   selected a visible disabled QSS state for unrelated controls. Removing that state
   is required to remove the intermediate color/background flash and to let a second
   independent user intent be sent.
4. Once requests are no longer serialized, an accepted response needs its own
   authoritative snapshot and request id correlation. Otherwise an older response
   can reapply an older view after a newer request has been issued.
5. Ordinary snapshot events must remain independent of request correlation. They
   carry READY/visibility/device state and later main-process correction/writeback;
   filtering them by request id would remove valid state transitions.

### Confirmed Design

- `MinibarClient` owns one timeout timer per in-flight request and finishes each
  request independently.
- `m_latestRequestId` gates only snapshot payloads carried by responses. Older or
  already-finished response payloads are discarded.
- Ordinary `EVT STAT:SNAP` payloads continue directly to `snapshotReceived`.
- Accepted RF/Center/Level/Sweep handlers build a snapshot after the synchronous
  main-side authoring/property update and return it in the matching response.
- Response and event snapshots share the monotonic snapshot sequence. The window's
  sequence guard therefore prevents a late lower-sequence payload from rolling back
  a newer event or response.
- The UI stays authoritative-snapshot-driven rather than optimistic. Sweep keeps its
  `expectedRevision` conflict check; two Sweep edits created from the same old
  revision may legitimately reject the later request instead of merging stale full
  authoring state.
- Difference guards compare logical target state before calling widget setters.
  A forced Sweep plan refresh is retained only to restore the authoritative enum
  after a user edit temporarily changes the `EnumTextButton` itself.

### Review Findings

1. **Behavioral mismatch to remove before considering the task fully clean:**
   `RemoteMiniBarWindow::applySnapshot()` now derives `m_rfText` locally from
   `m_rfEnabled` instead of consuming `tx.rfText`. This change is unrelated to
   difference-aware rendering, leaves the main-side `rfText` field published but
   unused, and weakens the main-as-authoritative-display contract. Restore the
   payload value with the existing local `ON/OFF` fallback; the new text-difference
   guard already prevents redundant refresh.
2. **Non-blocking redundant scaffolding:** after pending UI disabling was removed,
   `MinibarClient::requestStarted/requestFinished` have no consumers;
   `RemoteSweepPanel::setPending()` is only called with `false`; and
   `RemoteMiniBarWindow::setTxButtonsEnabled(bool)` is only called with `true`.
   The timers remain necessary, but these UI-facing pending signals/state and the
   always-true parameter can be removed or renamed to a narrow Sweep availability
   refresh in a cleanup change.
3. **Adjacent pre-existing Wayland gap, not introduced by the two reviewed commits:**
   `EnumTextButton` owns a `Qt::Popup` `PopupWidget`. The helper recognizes it through
   the overlay QObject parent chain, but no helper-side show path was found that gives
   this popup an explicit layer-shell transient surface configuration. Ownership
   prevents false outside-dismiss; it does not prevent the process-wide layer-shell
   integration from assigning default top-level geometry. Raspberry Pi validation or
   a focused transient configuration is still required.
4. **Adjacent redundant publisher trigger:** `RemoteMinibarService` still schedules a
   snapshot from `MainWindow::sweepAvailabilityChanged`, although current helper Sweep
   availability no longer consumes MainWindow provider/modulation gating. Unless that
   signal is retained for an explicitly documented future field, the connection only
   advances `seq` and publishes an equivalent snapshot and should be removed.

### Static Verification Result

- All reviewed helper files are included by `src/app/minibarhelper/CMakeLists.txt`;
  `RemoteMinibarService` is on the active Core plugin path.
- The response/event ordering, timeout cleanup, snapshot sequence guard, Sweep
  revision guard, `LabelButton`, `EnumTextButton`, and `SwitchButton` implementations
  were inspected at their current call sites.
- `git diff --check` passes for both commits.
- No build or runtime test was performed; this TaskLog's verification level is
  static. The two findings above remain code changes to consider separately.

## Follow-Up: Sweep Outside-Click And Owned Enum Popup

### Scope

Restore the legacy interaction contract in `RemoteMiniBarWindow` on Win32 and
Wayland:

1. Clicking outside an open Sweep panel closes only the Sweep panel and keeps the
   minibar expanded.
2. Opening or selecting an item in the Sweep Type `PopupWidget` must not close the
   Sweep panel or collapse the minibar.
3. The owned `PopupWidget` must receive explicit layer-shell transient geometry on
   Wayland instead of inheriting the integration's default four-edge anchors.

### Evidence And Cause

- The fullscreen `OverlayContainer` already blocks and handles outside input when the
  compositor delivers that input to its surface.
- A fully transparent Win32 overlay area may let the click activate a window behind
  SGStudio. In that case the helper sees `ApplicationDeactivate`, not an overlay mouse
  event. The current `m_sweepPanelSessionActive` guard suppresses minibar collapse but
  also leaves the Sweep panel open.
- The application-wide event filter treats the overlay host and its own native
  `windowHandle()` as owned, but does not recognize the separately native
  `PopupWidget::windowHandle()`. A popup item press can therefore be classified as an
  outside minibar press before the list item receives it.
- `PopupWidget` is `Qt::Popup`. Under the process-wide Wayland layer-shell integration
  it becomes another layer surface. Legacy `MiniBarWindow` already demonstrates the
  required pattern: catch the owned popup's `Show`, derive its position from the
  owning host's published visual geometry plus the anchor's local coordinates, then
  configure top/right anchors, output, size, margins, scope, and exclusive zone.

### Design

1. Track the currently owned Sweep `PopupWidget` and recognize both its QObject tree
   and native `windowHandle()` as Sweep-owned interaction.
2. On popup `Show`, register it before any mouse handling. On Wayland, defer one event
   loop turn so `PopupWidget::popup()` has finalized its size, then configure it using
   the Sweep overlay host's visual top-left/size and the enum button's local anchor.
3. Add a one-event-loop popup-dismiss guard. A selection or outside click first closes
   the nested enum popup; the same native gesture must not continue into the overlay
   and close the Sweep panel.
4. While Sweep is visible, application mouse presses are classified in this order:
   owned enum popup, Sweep dialog, numeric keyboard, outside. Outside presses close
   and consume only the Sweep panel; they do not call `setState(Collapsed)`.
5. On Win32, an unowned `ApplicationDeactivate` while Sweep is visible closes only
   Sweep, covering transparent-overlay click-through. On Wayland, continue ignoring
   deactivate as a close signal because an internal click on a
   `KeyboardInteractivityNone` layer surface may also look deactivated; the fullscreen
   overlay remains the authoritative outside catcher there.
6. Close in nested order: enum popup, numeric keyboard, Sweep dialog/overlay. Keep the
   minibar state unchanged.

### Verification Level

Debug build plus platform-specific static verification. Win32 behavior should be
runtime-checked when the helper can be launched in the local environment; Wayland
surface ownership and geometry must be checked statically here and retained on the
Raspberry Pi field checklist.

Success criteria:

- With Sweep open and no nested editor, one blank-area mouse/touch press closes Sweep
  and leaves `RemoteMiniBarWindow::State::Expanded` unchanged.
- Selecting Freq/Power closes only the enum popup, sends the existing request, and
  leaves Sweep visible.
- Clicking outside while the enum popup is open dismisses that nested popup without
  letting the same gesture dismiss Sweep.
- Popup widget/list/native-window events are treated as owned on both platforms.
- Wayland popup geometry is derived from the overlay host's published visual geometry
  and configured through `LayerShellQt::Window` before interaction.
- Hiding/restoring/shutting down still closes popup, keyboard, Sweep dialog, and
  overlay without leaving a fullscreen input surface.

### Implementation And Verification Result

- `PopupWidget` is now exported from `Controls`. This is required because the helper
  performs `qobject_cast<PopupWidget *>` across the `Controls.dll` boundary and the
  cast references `PopupWidget::staticMetaObject`; without `CONTROLS_EXPORT`, Win32
  fails with `LNK2019/LNK2001`.
- The helper tracks the owned Sweep Type popup and accepts both its QObject subtree
  and its native `windowHandle()` as internal interaction.
- A follow-up Win32 test exposed an additional child-host distinction: the remote
  Sweep dialog is not an independent native popup. Its input can first arrive on the
  fullscreen overlay host's `QWidgetWindow`. The final inside test therefore compares
  the mouse event's host-local position with `m_sweepDialog->geometry()` whenever the
  event source is the shared host/overlay/native window. It deliberately avoids
  `globalPos()` so the same rule remains valid for layer-shell visual geometry.
- Win32 transparent-overlay click-through is handled by closing Sweep, but not
  collapsing the minibar, on an unowned `ApplicationDeactivate`. Wayland continues
  to use the overlay as the outside authority because internal layer-surface clicks
  may also report deactivation.
- Wayland `PopupWidget` show handling now mirrors the legacy host: use the overlay
  host's published visual geometry plus the enum button's local coordinates, then
  configure output, layer, top/right anchors, margins, scope, size, keyboard
  interactivity, and exclusive zone.
- `git diff --check` passes.
- `cmake --build build/cmake-win-debug --config Debug --target SGStudioMiniBar --
  /m:1 /p:CL_MPCount=1 /p:UseMultiToolTask=false` passes and produces
  `bin/SGStudioMiniBar.exe`. The existing missing-`pwsh.exe` post-build message remains
  non-fatal and is unrelated to this change.
- Win32 compile/link verification is complete. The final Win32 interaction and
  Raspberry Pi Wayland surface behavior still require the corresponding runtime
  click sequence to be exercised in those environments.
