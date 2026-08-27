# MiniBar ListMode Placeholder

## Goal

Represent ListMode explicitly across the MiniBar IPC boundary and provide a read-only placeholder when the user enters MiniBar while ListMode is configured.

## Observations

- Sweep IPC uses stable string plan IDs rather than transporting the numeric `Sweep::SweepType` value.
- The main process currently maps ListMode to the generic `unsupported` plan ID, which discards the fact that the configured plan is specifically ListMode.
- The helper only enables the Sweep button when `SweepSnapshot::available` is true. That flag intentionally means the configured plan is editable in MiniBar, so ListMode currently cannot open an explanatory view.
- `RemoteSweepPanel` already has a third stacked page for a non-editable plan, but it uses generic unsupported copy and still renders the Sweep type header controls.

## Design

- Add the stable `mscan` plan ID and `SweepPlanKind::List` to the shared IPC vocabulary.
- Keep `supportedPlanIds` limited to `fscan` and `lscan`; ListMode is identifiable but not writable from MiniBar.
- Accept a List snapshot only when `available == false`. List requests remain rejected by the existing request decoder.
- In the MiniBar button, show `tr("List Mode")` and preserve the existing checked style as the enabled-state indication.
- Allow the Sweep button to open for ListMode even though it is not writable.
- In the Sweep popup, hide the common Enabled/Sweep Type controls and show only:
  - `List Mode`
  - `Frequency table editing is only available in the main interface.`
- Do not transmit or render ListMode table contents.

## Success criteria

1. A paired main/helper build decodes ListMode as `mscan` without falling back to `--`.
2. The MiniBar Sweep button displays `List Mode`, remains clickable, and retains its checked style according to the authoritative enabled state.
3. Clicking the button opens only the two-line ListMode placeholder.
4. No ListMode value, table, type, or enabled write request can be sent from MiniBar.
5. Frequency and power Sweep editing behavior remains unchanged.
6. Unknown future plan IDs still fail decoding instead of being mislabeled as ListMode.

## Verification level

`static`

## Plan

1. Extend the shared Sweep plan string mapping and snapshot validation.
2. Publish ListMode explicitly from `RemoteMinibarService`.
3. Add the ListMode-only placeholder rendering and button availability behavior in the helper.
4. Leave `Language.xlsx` unchanged; the user will add the new placeholder translation.
5. Update the MiniBar architecture document and run focused static checks.

## Implementation result

- Added the stable Sweep plan ID `mscan` and the typed `SweepPlanKind::List` value.
- `RemoteMinibarService` now maps `Sweep::SweepType::List` to the explicit List plan; unknown values continue to map to `Unsupported`.
- Snapshot decoding accepts List only as a non-writable configured plan. `supportedPlanIds` and request decoding remain limited to frequency and power.
- The MiniBar Sweep button displays `tr("List Mode")`, keeps the authoritative checked style, and remains clickable for the List placeholder.
- The List popup hides the normal Sweep title, Enabled control, Sweep Type control, and all numeric fields. Its page contains only `List Mode` and the translatable placeholder sentence.
- No List table content or List edit request was added to IPC.
- Per the user's follow-up instruction, no Excel translation workbook was modified.

## Static verification

- Confirmed all changed source files are included by their existing CMake targets.
- Reviewed every `SweepPlanKind` branch: string encoding/decoding, snapshot validation, main mapping, MiniBar title, panel page routing, and non-writable request boundary.
- Confirmed `decodeSweepChangeRequest()` still accepts only `Frequency` and `Level`.
- `git diff --check` passes; Git only reports the repository's existing LF-to-CRLF checkout warnings.
- No build or runtime test was performed, following the repository's default static verification policy.

## Follow-up: restore Freq/Power editability after ListMode

### Field observation

- Entering MiniBar in ListMode correctly opens the read-only placeholder.
- After restoring the main window, selecting and enabling frequency Sweep, then
  entering MiniBar again, the Sweep snapshot renders the frequency/checked state
  but the entry button remains disabled.

### Cause and design

- The ListMode placeholder deliberately allows its entry button to open while
  `SweepSnapshot::available` is false.
- Freq/Power still depend on `available`, while the producer currently couples that
  authoring flag to the instantaneous `device->isOpen()` state. A visibility/device
  transition can therefore publish a correctly selected Freq/Power plan as
  unavailable and leave both the entry and editor disabled.
- `available` should describe whether the configured Sweep authoring plan is
  supported by MiniBar. It is true when the main Sweep panel exists and the selected
  plan is Freq or Power; ListMode remains unavailable/read-only. Device presence and
  open state continue to be published independently in the snapshot's `device`
  section and continue to drive the existing MiniBar visibility lifecycle.

### Success criteria

1. After ListMode -> main -> Freq Sweep or Power Sweep -> MiniBar, the Sweep entry is
   clickable and its editor controls are writable.
2. ListMode remains a clickable, read-only placeholder.
3. Unsupported future Sweep modes remain unavailable.
4. No frequency-table data or Excel translation file is changed.
5. Static verification covers producer/consumer availability semantics and
   `git diff --check`; no build or runtime verification is performed unless requested.

### Implementation and static verification

- `RemoteMinibarService::buildSweepSnapshot()` no longer takes or consumes the
  instantaneous device-open flag when calculating Sweep authoring availability.
- Freq and Power produce `available=true` whenever the main Sweep panel exists;
  List and unknown modes still produce `available=false` because their selected
  plans are not helper-supported authoring plans.
- Confirmed the helper Sweep entry and all editor controls consume the corrected
  `available` value, while the explicit List entry exception only opens the
  placeholder.
- Confirmed `supportedPlanIds` and `decodeSweepChangeRequest()` remain limited to
  Freq and Power, so ListMode cannot submit an edit request.
- `git diff --check` passes with only the repository's existing LF-to-CRLF checkout
  warnings. No build or runtime test was performed.

## Follow-up: restore Sweep Type text after ListMode

### Observation and cause

- After ListMode -> main -> Freq Sweep -> MiniBar, the authoritative page and popup
  options are Freq/Power, but the Sweep Type button can still display `List Mode`.
- ListMode writes placeholder text directly to `EnumTextButton` and disables its two
  editable popup items. On the first Freq/Power render, `renderPlanButton()` runs
  before `updateWriteAvailability()`, so `setCurrentEnum()` rejects the still-disabled
  authoritative item. The render cache is then advanced despite the rejected setter,
  leaving the stale ListMode text in place.

### Design and success criteria

- Refresh item write availability before rendering the authoritative Sweep enum.
- Freq and Power must restore their correct displayed enum text immediately after a
  ListMode snapshot; their popup must continue to contain only Freq and Power.
- ListMode remains a hidden, non-writable enum control with the existing placeholder.
- Verify the render order and transition statically; do not modify translations or
  frequency-table data.

### Implementation and static verification

- `RemoteSweepPanel::renderSnapshot()` now restores popup-item availability before
  asking `EnumTextButton` to render the authoritative Freq/Power selection.
- `renderPlanButton()` updates `m_renderedPlanValue` only after `setCurrentEnum()`
  succeeds; a rejected setter remains eligible for the next authoritative refresh.
- Programmatic `setCurrentEnum()` emits `currentItemToggled`, while business requests
  are connected only to `currentItemEdited`, so snapshot rendering does not submit a
  Sweep plan change.
- `git diff --check` passes with only existing LF-to-CRLF checkout warnings. No build
  or runtime test was performed.

## Follow-up: allow Power-to-Freq selection

### Observation and cause

- When MiniBar starts with Power Sweep selected, choosing Freq in the Sweep Type
  popup can immediately return to Power even though both popup items are enabled.
- The helper-side selection filters all pass and produce a Freq request. The main
  request handler then rejects the request when the newly selected plan's retained
  step is non-positive.
- Core initializes `StepSweep_FreqStep` to `0`, while the full Sweep request contract
  requires the selected plan's step and shared dwell to be greater than zero. A
  dormant invalid Freq value therefore prevents entering the Freq page where the
  user could otherwise edit it.

### Design and success criteria

- On a plan-only change, preserve all authoritative retained values except a
  non-positive target-plan step or shared dwell. Replace only those invalid fields
  with the existing `SweepAuthoringState` defaults and include them in the local edit
  mask so conflict rebasing retains the correction.
- Power -> Freq and Freq -> Power must both submit a valid complete authoring request
  and remain on the selected page after the authoritative response.
- Already-valid retained values must not be changed. ListMode request boundaries,
  translations, and frequency-table data remain untouched.

### Implementation and static verification

- `RemoteSweepPanel::requestPlanChange()` now repairs only a non-positive target
  Freq/Power step and non-positive shared dwell using the existing DTO defaults.
- Each repaired field is added to the edit mask, so conflict rebasing does not lose
  the correction before retrying the complete authoring request.
- Confirmed the producer advertises both Freq and Power, the helper filters accept
  both directions, and the resulting request satisfies the main handler's selected
  step/dwell validation.
- `git diff --check` passes with only existing LF-to-CRLF checkout warnings. No build
  or runtime test was performed.

## Redesign: ListMode keeps common Sweep editing

### Updated product boundary

- The previous read-only-placeholder design was too broad: it hid and disabled the
  common Enabled and Sweep Type controls together with the ListMode table.
- MiniBar fully supports the configured ListMode state. Enabled and Sweep Type remain
  editable, including transitions among Freq, Power, and List.
- Only the ListMode table authoring surface remains main-window-only. The numeric/table
  area is replaced by one centered row:
  `List scan editing is only supported in main-window mode.`
- The ordinary `Sweep` popup title and its common first row stay visible in ListMode.

### Synchronization design

- `mscan` is a supported Sweep plan ID, not a non-writable placeholder identity.
  A Sweep snapshot is writable whenever the main `StepSweepPanel` exists and the
  configured plan is one of Freq, Power, or List.
- Every snapshot continues to carry the authoritative enabled state, selected plan,
  retained Freq parameters, retained Power parameters, and shared analog dwell value.
  The ListMode frequency/power/dwell table is deliberately not serialized.
- Helper requests continue to use the revisioned complete authoring snapshot. A main
  edit advances the Sweep revision; an in-flight helper edit is therefore either
  applied to the matching state or conflicts and rebases onto the next authoritative
  snapshot. This prevents either side from overwriting newer scalar parameters.
- Selecting List applies only the List plan identity plus the already-authoritative
  common/retained scalar state; `StepSweepPanel` keeps using its existing local
  `ListModePanel` table. List requests do not validate unused analog step/dwell fields.
- Selecting Freq or Power still repairs only a non-positive target step/shared dwell
  before dispatch, so a dormant invalid retained value cannot make Sweep Type snap
  back. All already-valid retained values remain unchanged.

### Success criteria

1. ListMode renders the normal Sweep title, editable Enabled control, and editable
   Sweep Type control; only the parameter/table area becomes the one-row notice.
2. Sweep Type can switch in all directions among Freq, Power, and List. Selecting List
   reuses the main window's existing ListMode table without serializing or replacing it.
3. Enabled can be changed while List is selected and the accepted main state is written
   back to the helper.
4. Main-side edits to enabled/type/Freq/Power/dwell appear in the next helper snapshot;
   helper-side edits return an accepted authoritative snapshot and remain visible.
5. Stale full-state requests conflict by revision and rebase instead of overwriting a
   newer main-side parameter value.
6. Unknown future plans remain unsupported and unavailable.
7. Verification is static only: inspect every plan mapping/validation/apply branch and
   run focused searches plus `git diff --check`. Do not build or run unless requested.

### Scope note

- Keep the existing stable `mscan` IPC identity and the current user-authored working
  tree changes.
- Do not change the ListMode table DTO or any `Language.xlsx` workbook in this pass.

### Implementation result

- `mscan` now round-trips through plan ID encoding, supported-plan encoding/decoding,
  snapshot validation, and change-request decoding.
- Main publishes Freq, Power, and List as supported whenever the included
  `StepSweepPanel` owns a recognized configured plan. Device-open state is no longer
  conflated with Sweep authoring availability.
- Main accepts requests while List is currently configured, maps requested List back
  to `Sweep::SweepType::List`, and skips analog step/dwell validation only when the
  requested target is List. The existing `ListModePanel` table is never replaced.
- The helper Sweep Type enum contains Freq, Power, and List. Enabled and Sweep Type stay
  visible/writable in List; the two-row numeric stack is hidden and replaced by the
  fixed-height one-row notice. A live type transition also recomputes popup height.
- The outer MiniBar Sweep button consumes the panel's effective display state while a
  local intent is active, so its title/checked representation changes together with the
  popup and is not temporarily overwritten by an older authoritative snapshot.
- Freq/Power numeric controls retain the revisioned optimistic full-state workflow.
  List type changes do not repair or mutate dormant analog parameters; Freq/Power type
  changes still repair only invalid target step/dwell values and include those repairs
  in the rebase mask.

### Static verification result

- Confirmed all seven changed C++/header files remain included by their existing CMake
  targets.
- Reviewed every `SweepPlanKind` branch in the repository. List is covered by stable ID
  mapping, supported-plan filtering, snapshot validation, request decoding, helper enum
  mapping/rendering, main snapshot mapping, validation, and apply mapping.
- Confirmed an unknown/unsupported plan is not admitted to `supportedPlanIds` or change
  requests and keeps the Sweep surface unavailable.
- Confirmed main-side state changes still advance the revision from the complete scalar
  authoring state, while conflict handling rebases the helper's local edit mask onto the
  next accepted/event snapshot.
- `git diff --check` passes; output contains only the repository's existing
  LF-to-CRLF checkout warnings.
- No build or runtime test was performed, following the requested static verification
  boundary.

## Follow-up: Sweep Type popup items do not switch

### Field observation

- In MiniBar, the Sweep panel opens, but selecting another item from Sweep Type does
  not change the scan plan.
- Commit `e959f490` (SCPI integration) changed the shared `EnumTextButton` contract:
  `setCurrentEnum()` now rejects an item whose popup flag is disabled. The popup also
  suppresses `currentItemChanged` for disabled items.

### Cause and design

- The user's field observation is that none of the three popup items is disabled.
  Static lifecycle analysis agrees: if the Sweep panel entry is enabled/open, the
  decoded snapshot is available and the producer advertises Freq, Power, and List, so
  `updateWriteAvailability()` restores all three item flags before interaction.
- Therefore the SCPI `setCurrentEnum()` guard is a relevant regression risk for
  programmatic rendering, but disabled flags are not yet a demonstrated cause of this
  click failure. Do not keep a speculative availability rewrite as the fix.
- The remaining distinct breakpoints are: the native popup receives no item click;
  the popup receives it but does not emit `currentItemChanged`; `EnumTextButton` does
  not emit `currentItemEdited`; the helper guard rejects the target; or the revisioned
  request/response converges to the old plan.
- Add focused diagnostics at each of those boundaries, including Windows application
  deactivation/owned-popup lifetime, without changing normal selection behavior.

### Success criteria

1. When the Sweep Type button is enabled, all advertised Freq/Power/List items can be
   selected and immediately render the optimistic target plan.
2. Logs report the actual Qt `ItemIsEnabled` flags instead of inferring them from QSS.
3. Unsupported future plans remain disabled and cannot pass `requestPlanChange()`.
4. Logs identify popup show/click/close, enum signal conversion, helper guard state,
   request dispatch, and response convergence.
5. Verification remains static unless the user explicitly requests a build/run.

### Plan

1. Add narrow popup/base-control/helper diagnostics around one Sweep Type selection.
2. Use a field log to identify the first missing boundary.
3. Apply the smallest confirmed fix, then recheck optimistic rebase, CMake inclusion,
   and `git diff --check`.

### Diagnostic implementation and static verification

- Restored the existing item-availability behavior after the disabled-item hypothesis
  conflicted with the user's field evidence.
- Added `[MiniBarSweepPopup]` diagnostics for button click/close-guard state, the three
  real popup item flags, popup visibility, low-level `QListWidget::itemClicked`, enum
  value conversion, popup registration, and application deactivation.
- Added `[MiniBarSweep]` diagnostics for authoritative snapshots, helper selection
  guards, dispatched revision/plan/edit mask, and response revision/plan.
- `MinibarHelperController` now forwards the child helper's stderr into the main Qt
  log with a `[MiniBarHelper]` prefix, so the focused helper trace is visible in the
  launch console and in `bin/debug.log` when file logging is enabled.
- The only defensive behavior change is an index bounds check before converting popup
  text back to an enum value; valid item selection behavior is unchanged.
- Confirmed Freq, Power, and List remain producer-advertised, protocol-decodable, and
  main-handler-applied; unknown plans remain filtered/rejected.
- The final fix is intentionally deferred until one field trace identifies the first
  missing boundary. No build or runtime test was performed locally.

### Field-log correction and cross-process aggregation

- The 17:41 and 17:45 `debug.log` sessions contain only main-process messages. The
  apparent Sweep Type clicks are followed by `StepSweepPanel::previewCurrentCarrier()`,
  so those entries describe the main panel; they do not contradict the user's direct
  observation that only the MiniBar popup was exercised in the latest test.
- `SGStudioMiniBar.exe` is built as a Windows GUI executable. Its Qt diagnostics do
  not arrive on the `QProcess` stderr pipe in this runtime, so the earlier stderr
  forwarding connection cannot expose helper-side popup events.
- Add a narrow authenticated IPC message for helper diagnostics. The helper forwards
  only messages whose text begins with `[MiniBar`, and the main controller writes them
  to its normal logger with a `[MiniBarHelper]` prefix. Newlines are flattened so one
  diagnostic remains one framed protocol line.
- Install forwarding only after the helper has sent `AUTH`; this preserves the rule
  that authentication must be the first child-to-main message. Keep the stderr
  forwarding as a fallback for environments where Qt does use the inherited pipe.
- The next field trace must distinguish popup deactivation/closure, missing item click,
  missing enum edit signal, helper guard rejection, request dispatch, and authoritative
  response convergence before the behavioral fix is selected.

### Confirmed cause, fix, and diagnostic cleanup

- The no-device trace proved that all three popup items were enabled and that the
  click, enum conversion, helper guard, and request dispatch all succeeded. The main
  process rejected every request with `NOT_OPEN` before calling
  `handleSweepRequest()`, after which the helper correctly rolled back to the last
  authoritative Freq state.
- This was inconsistent with the redesigned snapshot contract: Sweep `available`
  describes authoring support and is deliberately independent of device-open state.
  Route `SOUR:SWEEP:CONF` before the device-command `NOT_OPEN` gate. RF, Center,
  Level, and MOD commands keep their existing open-device requirement.
- With a connected device, the field trace exercised Freq, Power, and List repeatedly.
  Every request matched the current revision, the main panel applied it, the revision
  advanced, and the accepted snapshot returned the same plan. A subsequent main-side
  plan edit also appeared in the helper's next authoritative snapshot.
- Remove all temporary popup/helper/main diagnostics, stderr forwarding, and the
  temporary `HELPER_LOG` IPC message after preserving the confirmed routing fix.
