---
name: "sg-tasklog-implement"
description: "Use when implementing one SGStudio TaskLog with clear scope and verification level"
argument-hint: "TaskLog path, scope, verify level, optional update"
agent: "agent"
model: "GPT-5 (copilot)"
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
- Respect the TaskLog's Goal, Non-Goals, Risks, and Verification Checklist.
- Follow the workspace instructions and relevant knowledge base documents already provided by the repository.
- Keep changes scoped to `# Scope`.
- If the TaskLog conflicts with current code or is missing a blocking detail, stop and report the smallest concrete mismatch before making speculative changes.

Finish by reporting:

- implemented scope
- verification performed
- remaining risks or follow-up items