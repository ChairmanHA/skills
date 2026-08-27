# 2026-07-08 Minibar CS Helper SCPI Architecture

## Scope

Draft a durable design note for a two-process minibar architecture on Wayland:
the existing SGStudio backend/main process owns devices and normal UI, while a
separate lightweight minibar helper process owns only the layer-shell UI and
talks to the backend through a shallow command/state IPC surface.

Verification level: static.

## User Constraints

1. `main` and `minibar` are not visible at the same time.
2. `minibar` functionality may be reduced because the normal main window can be
   shown quickly for detailed inspection and operations.
3. The first command surface may stay shallow and SCPI-like; it should not expose
   deep data-layer or waveform-generation details such as generating a Pulse
   waveform from raw parameters.

## Observations

1. Current Wayland layer-shell integration is process-global through
   `LayerShellQt::Shell::useLayerShell()` and `QT_WAYLAND_SHELL_INTEGRATION`.
2. Current restart-based `main <-> minibar` switching exists mostly to cross
   that process-global shell integration boundary safely.
3. `CoreRuntimeServices`, `DeviceRuntimeBridge`, and `TxSessionService` already
   establish a single backend owner for shared device/runtime/apply state inside
   the SGStudio process.
4. The existing `MiniBarWindow` is still an in-process UI host and directly
   consumes shared property/business/runtime objects; it should not be moved
   unchanged into a separate helper process.

## Assumptions

1. "Not simultaneous" means no simultaneous visible `MainWindow` and minibar
   surfaces; the backend may keep the `MainWindow` object constructed but hidden
   if that is needed for fast restore.
2. The first-stage helper is an internal UI client, not a general external SCPI
   automation client.
3. The backend process remains the only device owner and the only process that
   loads the business/device plugin runtime.

## Plan

1. Create a new KnowledgeBase architecture note.
2. Include a draw.io-compatible `.drawio` file next to the note.
3. Update `.github/KnowledgeBase/Index.md` with the new durable design entry.
4. Keep the result design-only; do not change build or source code in this task.

## Success Criteria

1. The design states whether the constrained SCPI-like first stage is reasonable.
2. The architecture diagram can be opened by diagrams.net / draw.io.
3. The document clearly distinguishes Stage A minimal scope from later expansion.
4. The KnowledgeBase index links the new document.

## Static Verification

Planned:

```text
git diff --check -- .github/TaskLog/2026-07-08_minibar_cs_helper_scpi_architecture.md .github/KnowledgeBase/minibar_cs_helper_scpi_architecture.md .github/KnowledgeBase/minibar_cs_helper_scpi_architecture.drawio .github/KnowledgeBase/Index.md
```

