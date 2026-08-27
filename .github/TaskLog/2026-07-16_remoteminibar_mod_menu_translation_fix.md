# RemoteMinibar MOD Menu Translation Fix

## Scope

- Make the standalone `SGStudioMiniBar` MOD menu translate the business short names with the same `Language.xlsx` translator used by the main modulation list.
- Preserve business IDs, selection state, editor routing, full-name panel titles, and all IPC request semantics.

## Observations

- `SGStudioMiniBar` already loads `configuration/Language.xlsx` and installs `Utils::Translator` before constructing `RemoteMiniBarWindow`.
- `RemoteMiniBarWindow::rebuildModMenu()` currently adds `business.name` directly to each `QAction`, so the installed translator is never queried.
- The main modulation list translates the raw `IBusiness::name()` value before displaying it.
- `RemoteMinibarService` currently calls `simplified()` before sending the short name. This destroys significant source text such as `Digital\nMod `, which is the exact key stored in `Language.xlsx`.

## Inference

- The missing translation is a display-path defect, not a translator-loading defect.
- The helper must receive the raw short name, translate that exact source string, and only then normalize whitespace for the one-line menu presentation.

## Success Criteria

1. Every RemoteMinibar MOD menu action is produced by the helper's installed translator from the raw business short name.
2. Multi-line or padded source names remain valid translation keys but render as normalized one-line menu labels.
3. `QAction::data()` continues to contain the stable business ID, and checked-state/editor selection behavior is unchanged.
4. MOD panel titles continue to use the existing full-name `text` field.

## Verification Level

- `static`

## Static Verification Checklist

- [x] Confirm `remoteminibarwindow.cpp` is included by `src/app/minibarhelper/CMakeLists.txt`.
- [x] Confirm `remoteminibarservice.cpp` is included by `src/plugins/core/CMakeLists.txt`.
- [x] Read the current version of every source file changed by this task.
- [x] Confirm the snapshot preserves the raw business short name.
- [x] Confirm the menu translates before whitespace normalization.
- [x] Run `git diff --check` and review the final diff for unrelated changes.

## Implementation Result

- `RemoteMinibarService` now sends the exact `IBusiness::name()` source text in the MOD snapshot instead of simplifying it before transport.
- `RemoteMiniBarWindow` preserves that source text, resolves it through the already-installed helper translator, and only then normalizes it for the one-line `QMenu` label.
- Business UUID data, checked-state synchronization, editor kind selection, and full-name panel titles are unchanged.
- Static verification passed; `git diff --check` reports no whitespace errors in the changed source files.
