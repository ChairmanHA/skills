# SaveFile Dialog Wayland Modal Parent Fix

Date: 2026-07-01

## Scope

- Keep the existing `PxSaveFileDlg` UI and behavior.
- Align its lifetime and parent ownership with the Wayland modal-dialog fix already applied to `PxOpenFileDlg`.
- Limit code changes to the Controls save-file dialog and the Linux custom save-file entry point.

## Observations

- `PxSaveFileDlg` is compiled into the `Controls` target.
- The current constructor always uses `Utils::GUIContext::instance()->getMainWindow()` as parent.
- On Linux, `Controls::getSaveFilePath(...)` receives a `QWidget *parent` but currently constructs `PxSaveFileDlg dialog;`, so a modal caller cannot become the dialog owner.
- `PxSaveFileDlg::show(...)` invokes the external callback directly from the `Dialog::finished` callback path.
- Recent Raspberry Pi Wayland testing showed the same callback timing pattern can crash when a modal dialog opens a custom file dialog.

## Assumption

The likely failure mode is not the save dialog UI itself, but an unstable modal/top-level ownership and callback reentrancy boundary under Wayland. The conservative fix is to parent the dialog to the active caller and defer async callbacks by one event-loop turn.

## Plan

1. Add an optional parent argument to `PxSaveFileDlg`. Done.
2. Use that parent when constructing the Linux custom save dialog from `getSaveFilePath(...)`. Done.
3. Defer the async `show(...)` callback with `QTimer::singleShot(0, ...)` and protect it with `QPointer`. Done.
4. Defer the overwrite-confirmation callback before it closes the save dialog. Done.
5. Run static diff checks only; no build or runtime test unless requested.

## Success Criteria

- Linux save dialogs opened from a modal parent are owned by that parent instead of always falling back to the main window.
- Existing callers that use the default constructor or singleton remain source-compatible.
- Async callback code no longer runs directly inside the dialog finished stack.
- `git diff --check` passes for the changed files.

## Verification

- Static inclusion check: `SaveFileDlg.cpp`, `SaveFileDlg.h`, and `filedialogutils.cpp` are listed in `src/libs/controls/CMakeLists.txt`.
- Static caller check: `getSaveFilePath(...)` callers already pass useful parent widgets in the common panel paths.
- Diff hygiene: `git -c safe.directory=D:/development/vsg2.0 diff --check -- src/libs/controls/SaveFileDlg.h src/libs/controls/SaveFileDlg.cpp src/libs/controls/filedialogutils.cpp` passed.
- Build/run: not run, per the repository default static-analysis workflow.
