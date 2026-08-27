# Analog / HTRA Provider Lifecycle Documentation

## Scope

- Write durable KnowledgeBase documentation for the runtime provider-switch duplicate-panel keyboard issue.
- Cover:
  - symptom and root cause;
  - rules for adding an HTRA modulation that already exists in Analog and should behave like AM;
  - steps to remove the dual-provider mechanism when HTRA AM fully replaces Analog AM.

## Sources Read

- `.github/KnowledgeBase/Index.md`
- `.github/KnowledgeBase/analog_device_license_gating.md`
- `.github/KnowledgeBase/ui_independent_runtime_and_minibar_design.md`
- `.github/KnowledgeBase/soft_keyboard_architecture.md`
- `.github/TaskLog/2026-06-25_analog_htra_provider_switch_review.md`

## Design

- Add one focused KnowledgeBase document under `.github/KnowledgeBase/`.
- Update the KnowledgeBase index in the device/signal/performance section near `analog_device_license_gating.md`.
- Keep the document concrete and code-oriented, with current file/function names and explicit do/don't guidance.

## Verification

- Static documentation review.
- Confirm the new document is linked from `.github/KnowledgeBase/Index.md`.
- `git diff --check` for changed docs.
