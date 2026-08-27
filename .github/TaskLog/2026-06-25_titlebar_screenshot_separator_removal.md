# 2026-06-25 titlebar screenshot separator removal

## Scope

- Remove the code-created titlebar divider before the screenshot button.
- Keep `Single`, `Continue`, and `Screenshot` in the existing titlebar output-mode action widget.
- Do not modify QSS/style files in either `configuration/` or `configuration_files/`.

## Evidence

- `MainWindow::registerDefaultActions()` creates `btnSingle`, `btnContinue`, a `QFrame#titleBarDivider`, and `btnScreenshot` inside `outputModeWidget`.
- `QFrame` is only included in `mainwindow.cpp` for `titleBarDivider`.
- The user will clean up style rules manually, so code should not remove or edit QSS selectors.

## Design

- Use one `QHBoxLayout` spacing value to give the three titlebar action buttons equal horizontal spacing.
- Remove the extra manual `addSpacing(...)` calls around the divider.
- Remove the `QFrame#titleBarDivider` widget creation and insertion.
- Remove the now-unused `#include <QFrame>`.

## Verification Level

static

## Success Criteria

- `mainwindow.cpp` no longer references `titleBarDivider`.
- `mainwindow.cpp` no longer includes `QFrame`.
- `btnSingle`, `btnContinue`, and `btnScreenshot` remain added to `outputLayout` in that order.
- No style files are modified.

## Implementation Notes

- Removed the `QFrame#titleBarDivider` creation and insertion from `MainWindow::registerDefaultActions()`.
- Removed the manual spacing around the divider.
- Set the `outputModeWidget` `QHBoxLayout` spacing to a single uniform value for `Single`, `Continue`, and `Screenshot`.
- Removed the now-unused `#include <QFrame>`.
- Updated the TitleBar KnowledgeBase addendum to record the no-separator layout baseline.

## Verification

- `rg -n "QFrame|titleBarDivider|outputLayout->setSpacing|outputLayout->addWidget\\(btnSingle\\)|outputLayout->addWidget\\(btnContinue\\)|outputLayout->addWidget\\(btnScreenshot\\)|outputLayout->addSpacing" src/plugins/core/mainwindow.cpp`
- `git diff --check -- src/plugins/core/mainwindow.cpp .github/KnowledgeBase/titlebar_menubar_outputmode_and_overflow_behavior.md .github/TaskLog/2026-06-25_titlebar_screenshot_separator_removal.md`
- `git status --short -- src/plugins/core/mainwindow.cpp configuration configuration_files .github/KnowledgeBase/titlebar_menubar_outputmode_and_overflow_behavior.md .github/TaskLog/2026-06-25_titlebar_screenshot_separator_removal.md`

Result: static checks passed. `git diff --check` reported only line-ending normalization warnings for `mainwindow.cpp`. No style file diff was introduced by this task.
