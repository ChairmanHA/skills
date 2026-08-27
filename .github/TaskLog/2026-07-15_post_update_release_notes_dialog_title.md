# Post-update Release Notes Dialog Title

## Scope

- Fix the release-notes dialog shown after maintenance restarts SGStudio with `--UpdateCompleted`.
- Set the dialog's visible custom title-bar text to exactly `Update`.
- Keep the release-note content, size, startup condition, and lifecycle unchanged.

## Observation And Inference

- Observation: `MainWindow::completeExtensionsInitialization()` creates `ReleaseNotesDialog` when `--UpdateCompleted` is present.
- Observation: `ReleaseNotesDialog` derives from `Controls::Dialog`, whose visible frameless title bar is updated through `setTitle()`.
- Observation: the dialog currently calls inherited `QDialog::setWindowTitle()`, while `Controls::Dialog::windowTitleChanged` does not forward that value to its custom title label.
- Inference: replacing that call with `setTitle(tr("Update"))` is the smallest change that makes the visible title appear.

## Success Criteria

1. The post-update release-notes dialog displays `Update` in its custom title bar.
2. The dialog continues to show the same release-note content after `--UpdateCompleted` startup.
3. No unrelated dialog behavior or updater flow changes.

## Verification Level

- `static`
- Confirm the edited source remains included by `src/plugins/core/CMakeLists.txt`.
- Inspect the final diff and relevant title API usage; do not build or run unless explicitly requested.

## Verification Result

- `ReleaseNotesDialog` now calls `setTitle(tr("Update"))`.
- `MainWindow::completeExtensionsInitialization()` still creates this dialog for `--UpdateCompleted`.
- `src/plugins/core/CMakeLists.txt` still includes `releasenotesdialog.cpp`.
- Focused `git diff --check` passed; no build or runtime check was performed.
