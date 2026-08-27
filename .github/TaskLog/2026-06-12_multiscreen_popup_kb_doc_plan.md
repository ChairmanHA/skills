# 2026-06-12 Multiscreen Popup KnowledgeBase Doc Plan

## Scope
- Add a durable KnowledgeBase note for the recent multiscreen popup fixes:
  - `EnumTextButton` popup screen selection and content-width sizing.
  - `TitleBar` menu popup refresh after screen topology changes.
  - Windows frameless main window restore on external monitors as related context.
- Update existing KnowledgeBase routing docs so future popup/multiscreen bugs have a clear entry point.
- Update `.github/KnowledgeBase/Index.md` for the new document.

## Verification Level
- static

## Design
- Read the current included implementation files and the relevant TaskLogs.
- Create a concise KB article under `.github/KnowledgeBase/` focused on root cause, owner boundaries, fix pattern, and future checklist.
- Link the new article from the existing TitleBar and High-DPI/multimon documents rather than duplicating every detail.

## Validation
- Added `.github/KnowledgeBase/multiscreen_popup_geometry_and_screen_topology.md`.
- Updated `.github/KnowledgeBase/titlebar_menubar_outputmode_and_overflow_behavior.md` with a TitleBar popup maintenance entry.
- Updated `.github/KnowledgeBase/high_dpi_development_practices.md` with a multiscreen popup routing note.
- Updated `.github/KnowledgeBase/Index.md` with the new KnowledgeBase entry.
- Verified the new entry and cross-links with `Select-String`.
- `git diff --check` passed for the touched documentation files.
- No build or runtime verification requested.
