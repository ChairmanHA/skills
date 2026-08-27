# Remove legacy in-process minibar

## Scope

Make the independent `SGStudioMiniBar` helper the only minibar product path.
Delete the legacy Core-hosted `MiniBarWindow`, its private business menu/panel
adapters, the `--ui-mode=minibar` startup split, and the restart fallback that
could still enter that path.

Keep the main-process ownership model and the reusable design that the remote
implementation still depends on:

1. `CoreRuntimeServices`, `DeviceRuntimeBridge`, `BusinessManager`,
   `IBusinessEntryHost`, and `TxSessionService` remain shared runtime boundaries.
2. `MinibarHelperController`, `RemoteMinibarService`, `MinibarIpc`, and
   `src/app/minibarhelper` remain the supported minibar implementation.
3. Shared controls and the proven popup/LayerShellQt interaction patterns remain
   in their current reusable locations; LayerShellQt is linked only by the helper.
4. The dynamic remote-editor interaction context used to avoid main-window
   prompts during helper requests remains supported. Only the obsolete
   `miniBarWindow` object-name detection is removed.
5. Historical TaskLogs remain unchanged. Durable KnowledgeBase documents are
   updated to distinguish removed implementation from reusable historical design.

## Verification level

`static`

The repository default is static analysis. No build or runtime test is requested.
CMake configure is allowed as a non-compiling structural check if an existing
toolchain can perform it without changing the requested verification boundary.

## Assumptions and evidence

### Observations

1. `CorePlugin` still parses `--ui-mode` and constructs either `MainWindow` or
   the legacy `MiniBarWindow`.
2. `MainWindow` first requests the helper, then restarts into
   `--ui-mode=minibar` when helper startup fails.
3. `src/plugins/core/CMakeLists.txt` still compiles the three legacy UI units and
   links LayerShellQt into Core solely for that path.
4. `MinibarHelperController` and `RemoteMinibarService` implement the current
   main/helper lifecycle and typed state/request bridge.
5. `src/app/minibarhelper` owns the current remote UI and its Wayland layer-shell
   behavior.
6. The packaged Raspberry Pi launcher still publishes the deleted startup mode.
7. Current KnowledgeBase documents already identify the helper as the product
   path and the in-process window as legacy reference material.
8. The user reports that the remote minibar has been tested and now implements
   all required product functionality.

### Inference

Because the remote helper has field-tested feature parity, keeping the startup
split as a fallback no longer adds a valid recovery path. It instead preserves a
second runtime architecture, forces Core to carry LayerShellQt, and makes package
scripts advertise an obsolete mode.

## Implementation plan

1. Delete `MiniBarWindow`, `MiniBarBusinessMenuHost`, and
   `MiniBarBusinessPanelPresenter` source/header files and remove them from Core.
2. Make `CorePlugin` unconditionally create `MainWindow`; remove startup UI mode
   state and the Core LayerShellQt dependency.
3. Remove the legacy helper-failure restart path. A helper startup failure keeps
   the already-visible main window and logs the concrete error.
4. Remove the in-process-only Wayland property-schema exception and obsolete
   minibar window object-name detection while retaining the remote-editor
   context behavior.
5. Remove the packaged launcher's UI-mode selector and update the Pi dependency
   script's controlled launch example.
6. Update current KnowledgeBase documents so the helper-only architecture and
   retained reusable design are explicit.

## Success criteria

1. No compiled source or CMake target references `MiniBarWindow`,
   `MiniBarBusinessMenuHost`, or `MiniBarBusinessPanelPresenter`.
2. `CorePlugin` has one startup path: `MainWindow`.
3. Clicking the minibar action can only request `MinibarHelperController`; helper
   failure does not close or restart SGStudio.
4. `Core` no longer links LayerShellQt or defines `SGS_HAVE_LAYER_SHELL_QT`;
   `SGStudioMiniBar` still does.
5. Runtime/deployment source no longer advertises or emits
   `--ui-mode=main|minibar`.
6. Remote minibar controller/service/IPC/helper files remain included in their
   respective CMake targets.
7. Durable documentation names the removed path as historical and retains the
   reusable runtime, host, UI-control, popup, and LayerShellQt design guidance.

## Static verification checklist

- [x] Inspect `git diff --check`.
- [x] Search compiled and deployment sources for removed class names and
      `--ui-mode`.
- [x] Confirm Core and helper CMake source/link boundaries.
- [x] Confirm remote controller/service/IPC/helper references are intact.
- [x] Inspect all final diffs for accidental changes to unrelated user files.

## Verification result

1. `git diff --check` passes.
2. Compiled/deployment sources contain no exact legacy class, startup mode,
   restart-argument, or `--ui-mode` references.
3. Every file listed by the Core CMake source block exists after deletion.
4. Core has no LayerShellQt compile/link reference; `SGStudioMiniBar` still links
   `LayerShellQt::Interface`, defines `SGS_HAVE_LAYER_SHELL_QT`, and depends on
   the `layer-shell` target when present.
5. Core still includes `MinibarHelperController`, `RemoteMinibarService`, and
   `MinibarIpc`; app/libs CMake still include the helper and IPC subdirectories.
6. Bash is not installed in the current Windows environment, so `bash -n` could
   not be run. The two small shell-script diffs were inspected statically.
7. No compile or runtime test was run, matching the requested static verification
   level and repository default.

## Follow-up: simplify remote interaction context

### Observation and boundary

The dynamic property formerly named as a "hosted panel" flag is no longer an
in-process minibar ownership test. Its remaining purpose is to mark a synchronous
remote MOD request as non-interactive so main-process business code does not open
a hidden modal confirmation dialog and block the helper response.

`widgetBelongsToMiniBarHost()` and `objectBelongsToMiniBarHost()` therefore carry
obsolete ownership semantics and unnecessary QObject/window traversal. The
non-interactive request context itself must remain until those confirmation flows
have an explicit non-UI API.

### Follow-up implementation plan

1. Make `RemoteMinibarService` the only owner of a scoped remote-interaction
   property, covering the whole validated MOD request.
2. Save and restore the previous property value with RAII so every early return
   clears the context and no later main-window operation inherits it.
3. Let analog playback read the property directly from its control panel; remove
   `widgetBelongsToMiniBarHost()` and `objectBelongsToMiniBarHost()`.
4. Remove Digital Modulation's duplicate scoped writer. Quick Waveform's current
   dialog callers are the panel itself, so replace its analogous ancestor lookup
   with the same direct context read.
5. Centralize the property key in Core constants.

### Follow-up success criteria

1. Synchronous remote MOD requests cannot wait on the affected hidden modal
   confirmation paths.
2. The property is restored on normal completion and every early return.
3. Only `RemoteMinibarService` writes the remote-interaction property.
4. No source reference remains to the old minibar-host helper names or property
   name.
5. Static searches and `git diff --check` pass; no build or runtime test is run.

### Follow-up verification result

- [x] `objectBelongsToMiniBarHost()`, `widgetBelongsToMiniBarHost()`, the
      Digital Modulation scoped duplicate, and the old dynamic-property name are
      absent from `src/`.
- [x] The new property has one definition, two direct reader locations, and one
      RAII writer in `RemoteMinibarService`.
- [x] The scoped object is created after provider/revision validation and before
      every MOD mutation. C++ stack unwinding restores the previous value for the
      action-rejection early return and normal completion.
- [x] The four affected `.cpp` files remain listed by their CMake targets.
- [x] `git diff --check` passes.
- [x] No build or runtime test was run, matching the repository's static default.
