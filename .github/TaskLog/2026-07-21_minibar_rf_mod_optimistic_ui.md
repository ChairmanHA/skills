# Minibar RF / MOD Optimistic UI

## Scope

- RF button clicks update the collapsed and expanded RF buttons immediately.
- Hosted modulation business `Enabled` edits update the top-level MOD button immediately.
- Clicking the top-level MOD button remains navigation-only and only opens/closes the list menu.
- RF and MOD enabled requests each keep at most one request in flight and coalesce subsequent edits to the latest UI intent.
- Ordinary EVT snapshots and older request results update the authoritative cache but do not overwrite an active local intent.
- A final accepted response confirms the local display; a rejection, timeout, or transport failure restores the latest authoritative state unless a newer intent still needs to be sent.

## Assumptions and evidence

### Observed

- `RemoteMiniBarWindow::onRfButtonClicked()` currently sends `!m_rfEnabled` without changing the local display.
- MOD button clicks call `showModMenu()` and must not toggle modulation state.
- MOD enable edits originate from `RemoteModEditorHost::changeRequested()` with `ModChangeRequest::hasEnabled == true`.
- `MinibarClient` currently correlates only Sweep completions; RF and MOD responses otherwise reach the UI only through the global latest-response snapshot filter.

### Inferred design

- Main remains the authoritative business/runtime owner. The helper only overlays a short-lived RF or MOD UI intent.
- RF and MOD enabled are scalar last-write-wins interactions, so they can use a smaller serialized intent state than Sweep's revisioned full-state edit mask.
- A queued MOD enabled request must be rebased to the latest cached business revision before it is sent.

## Success criteria

1. RF ON/OFF changes visually in the same click event on both RF button instances.
2. A hosted MOD Enabled edit changes the top-level MOD ON/OFF display immediately.
3. Clicking the top-level MOD button never changes its checked state and still opens/closes the menu.
4. While a request is in flight, later RF/MOD enabled edits remain visible and replace the queued intent.
5. Stale EVT/RSP payloads cannot visibly restore an older RF/MOD enabled state.
6. Final rejection/timeout/transport failure reconciles the UI to the authoritative cache when no newer intent exists.

## Planned files

- `src/app/minibarhelper/minibarclient.h`
- `src/app/minibarhelper/minibarclient.cpp`
- `src/app/minibarhelper/remoteminibarwindow.h`
- `src/app/minibarhelper/remoteminibarwindow.cpp`
- `src/app/minibarhelper/main.cpp`
- `src/app/minibarhelper/remotemodeditorhost.cpp`
- `.github/KnowledgeBase/minibar_cs_helper_scpi_architecture.md`

## Verification

- Level: static.
- Confirm the helper target includes all modified source files through its current CMake target definition.
- Inspect request classification, timeout/disconnect cleanup, signal wiring, snapshot overlay, and MOD menu click path.
- No build or runtime execution unless explicitly requested.

## Implementation result

- Added RF and MOD-enabled request classification/completion signals in `MinibarClient`, including response, timeout, send-failure, and disconnect paths.
- Added independent RF/MOD scalar intent state in `RemoteMiniBarWindow`; both serialize enabled requests and coalesce later edits to the latest generation.
- Added resource-specific RF/MOD response snapshot extraction so a specialized completion cannot write back unrelated fields from an older full snapshot.
- Preserved `m_modButton -> showModMenu()` and the externally-owned checked-state button subclass. MOD optimistic state is created only by hosted editor requests with `hasEnabled=true`.
- Updated `RemoteModEditorHost` enabled diff caching so an equal authoritative confirmation does not replay the editor switch.
- Updated the minibar IPC KnowledgeBase with the scalar latest-intent and MOD navigation-only rules.

## Static verification result

- Confirmed all modified helper sources are listed in `src/app/minibarhelper/CMakeLists.txt`.
- Confirmed every new completion signal has send-failure, RSP, timeout, and disconnect emission paths and is wired in helper `main.cpp`.
- Confirmed the MOD button click connection remains `showModMenu()` and no checked-state toggle was introduced.
- Confirmed declarations and definitions exist for all added window methods.
- `git diff --check` passes; only the repository's existing LF/CRLF conversion warnings were reported.
- Build/runtime verification was not performed, per the repository's static-analysis default.
