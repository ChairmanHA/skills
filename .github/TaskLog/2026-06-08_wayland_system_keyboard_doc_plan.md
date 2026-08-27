# Wayland System Keyboard Documentation Plan

- Date: 2026-06-08
- Goal: add a KnowledgeBase document describing how SGStudio integrates the Raspberry Pi Wayland system keyboard and how to debug it.

## Context

- The implemented solution targets Raspberry Pi desktop sessions using labwc + wf-panel-pi + squeekboard.
- Plain `QInputMethod::show()` is not sufficient on this environment even when SGStudio is already running on native Qt Wayland.
- The shipped solution now uses a shared Linux path in `Controls::Keyboard` and falls back to DBus service `sm.puri.OSK0.SetVisible(true/false)`.
- `EthConnectDialog` and file save/open related text entry now reuse that shared path.

## Planned Documentation Scope

- Environment prerequisites and runtime assumptions.
- Root cause summary and why the first Qt-only attempt was insufficient.
- Final integration architecture and owning code paths.
- Minimal implementation steps for future dialogs.
- Show/hide lifecycle rules.
- Remote diagnostics checklist used during bring-up.

## Deliverables

- New KnowledgeBase article under `.github/KnowledgeBase/`.
- One entry added to `.github/KnowledgeBase/Index.md`.