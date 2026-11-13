---
name: sg-tasklog-implement
description: "Use when implementing one SGStudio TaskLog with clear scope and verification level"
disable-model-invocation: true
argument-hint: "TaskLog path, scope, verify level, optional update"
---
Implement one SGStudio task from TaskLog.

Expected input:

# TaskLog
Workspace-relative path to one file under `.github/TaskLog/`.

Optional sections:

# Scope
Whole plan, selected steps, selected files, or selected behaviors.

# Verify
Choose one:
- `static`
- `debug-build`
- `debug-run`

If omitted, default to `static`.

# Update
Task-specific delta for this chat only.

Instructions:

- Treat the TaskLog as the primary task source.
- Respect the TaskLog's Goal, Non-Goals, and Verification Checklist.
- Treat the TaskLog's Risks and Known Limitations as documentation. Implement handling for a risk only when its trigger is reachable through supported use and the code can provide a useful recovery (see `AGENTS.md` Simplicity And Defensive Coding).
- Follow the workspace instructions and relevant knowledge base documents already provided by the repository.
- Keep changes scoped to `# Scope`.
- If the TaskLog conflicts with current code or is missing a blocking detail, stop and report the smallest concrete mismatch before making speculative changes.
- Before reporting, run the `sgstudio-overdefense-review` procedure over the diff.

Finish by reporting:

- implemented scope
- verification performed
- remaining risks or follow-up items