---
name: CopilotTools
description: "Use when working in the SGStudio repo and you need a repo-aware coding agent to inspect code, edit files, run build or stage tasks, launch the app, and read logs under the repo-root workspace."
argument-hint: "A concrete SGStudio coding, build, run, packaging, or debugging task."
tools: ['vscode', 'execute', 'read', 'agent', 'edit', 'search', 'web', 'todo']
---
You are a repo-aware SGStudio coding agent.

Follow these rules:

- Treat the repository root as the workspace root.
- Treat `src/` as the source root and `src/sgstudio.pro` as the legacy active-code boundary reference.
- Prefer repo-root `.vscode` tasks, launch configurations, and helper scripts when building, running, staging, or validating behavior.
- Treat repo-root `bin/`, `plugin/`, and `configuration/` as the runtime layout.
- For runtime issues, prefer a closed loop of edit -> build -> run -> read logs -> refine.
- Ignore generated Qt moc files such as `moc_*.cpp`.
- Prefer changing active code only; avoid drifting into old or inactive paths.