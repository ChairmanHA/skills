# Remote MiniBar Business Menu Name

## Scope

- Make the `minibarhelper` MOD `QMenu` display each remote business's `name` instead of the `text` produced from `fullName()`.
- Keep newline replacement and whitespace simplification for the menu label.
- Restore the unintended legacy `MiniBarWindow` popup-title change.
- Keep the remote hosted-editor title and all menu selection behavior unchanged.

## Observations

- `RemoteMiniBarWindow` receives both `ModBusinessEntry::name` and `ModBusinessEntry::text` from the main-process snapshot.
- `RemoteMinibarService` populates `name` from `IBusiness::name()` and `text` from `IBusiness::fullName()`.
- `RemoteMiniBarWindow::rebuildModMenu()` currently creates each menu action from `business.text`.
- The previous correction changed legacy `MiniBarWindow::businessPopupTitleText()` instead of the helper menu.

## Inference

- The narrow correction is to normalize `business.name` while creating helper menu actions, without changing `business.text` consumers such as the hosted-editor title.

## Success Criteria

1. `RemoteMiniBarWindow::rebuildModMenu()` creates MOD menu actions only from `business.name`.
2. Menu text still replaces embedded newlines with spaces and passes through `simplified()`.
3. Remote hosted-editor titles and menu action IDs/check states remain unchanged.
4. Legacy `MiniBarWindow::businessPopupTitleText()` is restored to its original `fullName()`-with-`name()`-fallback behavior.

## Verification Level

- `static`

## Static Verification Checklist

- [x] Confirm `remoteminibarwindow.cpp` is included by `src/app/minibarhelper/CMakeLists.txt`.
- [x] Read the current helper snapshot parsing, MOD menu construction, and relevant KnowledgeBase documentation.
- [x] Confirm helper menu actions use normalized `business.name`.
- [x] Confirm hosted-editor titles continue to use `business.text`.
- [x] Confirm the unintended legacy source and documentation edits are restored.
- [x] Run `git diff --check` and review the scoped diff for unrelated changes.

## Implementation Result

- `RemoteMiniBarWindow::rebuildModMenu()` now builds each action label from `business.name`, replaces embedded newlines with spaces, and applies `simplified()`.
- Hosted-editor titles continue to use `business.text`; action IDs, checkability, selection synchronization, and popup behavior are unchanged.
- Restored the legacy `MiniBarWindow` source and popup-host documentation to their pre-task `fullName()` title behavior.
- Updated the helper IPC KnowledgeBase document to distinguish the MOD menu's `name` from the hosted-editor title's `text`.
- Static verification passed; no build or runtime verification was performed.
