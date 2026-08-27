---
name: minibar-optimistic-enabled-workflow
description: Use when adding or modifying SGStudio minibar buttons, switches, or modulation editor controls whose checked/ON/OFF state follows a remote business enabled state; also use when fixing delayed enable feedback, stale server writeback, or Raspberry Pi pressed/checked flicker in the minibar helper. Implements optimistic UI, per-resource latest-intent request correlation, differential reconciliation, and navigation-only button boundaries.
---

# Minibar Optimistic Enabled Workflow

Apply this workflow to enabled-state interactions in `SGStudioMiniBar`. Keep main/property/business/runtime authoritative while making user-visible state change immediately.

Read [Minibar Helper IPC Architecture](../../KnowledgeBase/minibar_cs_helper_scpi_architecture.md) before changing request or snapshot semantics. Use [LabelButton Style Workflow](../labelbutton-style-workflow/SKILL.md) as well when checked-state styling, child labels, dynamic properties, or QSS are involved.

## Classify the interaction

Decide what the click or edit means before changing code:

- Treat an RF button, Sweep Enabled switch, or modulation editor Enabled switch as an enabled-state edit. Apply optimistic UI.
- Treat a button that only opens a menu or page as navigation-only. Do not create an enabled intent and do not let Qt auto-toggle its checked state.
- Keep business enabled, current-page selection, and widget availability as separate states.

For a navigation-only checkable `LabelButton`, override `nextCheckState()` and let programmatic state updates remain the only checked-state writer. The minibar MOD button is the reference case: clicking opens/closes the business list; only a hosted modulation business Enabled edit changes the MOD enabled display.

## Implement optimistic enabled state

Maintain these values per logical resource, not per widget instance:

```text
authoritativeEnabled
intentActive
desiredEnabled
requestInFlight
generation
sentGeneration
```

Also retain the latest request payload when the resource needs an ID, business ID, revision, or other request context.

On a user enabled-state edit:

1. Compute the next value from the currently displayed value.
2. Set `intentActive` and `desiredEnabled` immediately.
3. Increment `generation`.
4. Refresh every linked representation immediately, such as collapsed/expanded RF buttons, the MOD summary button, menu checks, and the active modulation editor switch.
5. Send only when no request for that logical resource is in flight.

Before emitting a request, set `requestInFlight` and copy `generation` to `sentGeneration`. This ordering lets synchronous send failure resolve the correct intent.

## Merge rapid edits

Keep at most one enabled request in flight for each logical resource.

- While it is in flight, accept further UI edits and replace the stored desired value/request payload.
- Do not disable the control and do not send concurrent stale requests.
- On completion, compare `generation` with `sentGeneration`.
- If they differ, discard the completed request as a visual confirmation and send the latest intent.
- If they match, clear the intent and reconcile against the authoritative cache.

For scalar RF/MOD enabled state, use last-write-wins serialization. For a revisioned full-state resource such as Sweep, rebase the queued user edit mask onto the newest accepted/EVT snapshot before sending it.

## Handle snapshots and responses

Separate authoritative cache updates from displayed-state rendering:

```text
displayedEnabled = intentActive ? desiredEnabled : authoritativeEnabled
```

- Let ordinary EVT snapshots update the authoritative cache while an intent is active, but continue rendering the desired value.
- Correlate RF, Sweep, and MOD-enabled completions explicitly in `MinibarClient`, including send failure, RSP, timeout, and disconnect paths.
- In a specialized completion, extract only that resource from the RSP snapshot and apply its resource sequence guard. Do not bypass the global latest-response filter by applying an older full snapshot to unrelated fields.
- On rejection, timeout, or transport failure, restore the authoritative value only when no newer intent exists. If a newer intent exists, keep it visible and continue with that request.

## Avoid redundant redraws

- Call `setChecked()`, text setters, style refreshes, and editor snapshot replay only when the effective displayed value changes.
- After a modulation editor Enabled edit, record the local switch value in the editor host's diff cache. An equal authoritative confirmation must not replay the switch; a differing value must correct it.
- Keep externally-owned checked controls protected from Qt's automatic press toggle. Optimistic state must be applied programmatically to all linked controls in one UI operation.

## Preserve ownership boundaries

- Keep main as the only property/business/runtime/device owner.
- Store only short-lived UI intent in the helper.
- Do not directly operate device or business objects from the helper.
- Do not broaden a navigation click into an enable/disable command.

## Verification checklist

- Confirm the initial authoritative state renders correctly.
- Confirm one click/edit changes every linked UI representation immediately.
- Confirm identical EVT/RSP confirmation causes no checked-state or editor replay.
- Confirm a stale EVT/RSP cannot overwrite a newer local intent.
- Confirm rapid `ON -> OFF -> ON` edits end at the latest value with one request per resource in flight.
- Confirm rejection, timeout, and disconnect reconcile correctly.
- Confirm the top-level MOD button still only opens/closes its list menu.
- Confirm both Win32 and Raspberry Pi paths retain the no-flicker checked-state behavior.
- Record task boundaries and verification in `.github/TaskLog/`; update the minibar KnowledgeBase when the durable protocol changes.

## Primary files

- `src/app/minibarhelper/minibarclient.h`
- `src/app/minibarhelper/minibarclient.cpp`
- `src/app/minibarhelper/remoteminibarwindow.h`
- `src/app/minibarhelper/remoteminibarwindow.cpp`
- `src/app/minibarhelper/remotemodeditorhost.cpp`
- `src/app/minibarhelper/remotesweeppanel.cpp`
- `src/app/minibarhelper/main.cpp`

