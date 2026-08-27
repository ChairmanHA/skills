# Updater Wayland modal open-file dialog fix

## Scope

- Fix the Raspberry Pi Wayland crash when `UpdateDialog` is modal and the local update file dialog finishes.
- Preserve the existing `PxOpenFileDlg` UI.
- Keep the change focused on dialog parent/lifetime/callback ordering.

## Assumptions And Evidence

- User field evidence: the same update package parses on Win32, but crashes on the Raspberry Pi Wayland desktop after the Linux `PxOpenFileDlg` callback calls `m_localFile->loadFile(fileName)`.
- Observation: Win32 uses native `QFileDialog`; Linux uses `Controls::PxOpenFileDlg::instance()` from `UpdateDialog::loadLocalFile()`.
- Observation: `PxOpenFileDlg::instance()` is a singleton parented to the main window, not to the currently modal `UpdateDialog`.
- Observation: `Controls::Dialog::runAsync()` uses `open()`, and its callback runs synchronously from the dialog `finished(int)` handling path.
- Assumption: on Wayland, a helper dialog opened from a modal dialog should be parented to that modal dialog, and business work that changes the parent dialog should run after the child dialog close/finished path unwinds.

## Design

- Let `PxOpenFileDlg` be constructed with an explicit parent while preserving the old default main-window parent behavior.
- In `UpdateDialog`, create a short-lived `PxOpenFileDlg` parented to `this` instead of using the singleton.
- Copy the selected path in the file dialog callback, schedule file loading with `QTimer::singleShot(0, this, ...)`, and delete the file dialog later.
- Re-enable the browse button after the child dialog has finished.

## Verification Level

- static

## Success Criteria

- The file dialog opened from modal `UpdateDialog` has `UpdateDialog` as parent on Linux/Wayland.
- `m_localFile->loadFile(fileName)` is no longer called directly inside the child dialog `finished(int)` callback.
- Existing synchronous `getOpenPath()` and singleton callers keep compiling against the default constructor and `instance()`.

## Implementation Notes

- `PxOpenFileDlg` now accepts an optional explicit parent and still falls back to the main window when none is supplied.
- Linux `UpdateDialog::loadLocalFile()` now creates a short-lived `PxOpenFileDlg(this)` instead of using the global singleton.
- The selected file path is copied while the child dialog is still alive, then `m_localFile->loadFile(fileName)` is posted with `QTimer::singleShot(0, this, ...)`.
- The browse button is disabled while the child file dialog is active and re-enabled after the deferred callback returns to `UpdateDialog`.

## Static Verification

- `git -c safe.directory=D:/development/vsg2.0 diff --check -- src/libs/controls/OpenFileDlg.h src/libs/controls/OpenFileDlg.cpp src/plugins/updater/updatedialog.cpp` passed.
- `rg -n "PxOpenFileDlg::instance\\(|PxOpenFileDlg dialog|new Controls::PxOpenFileDlg|PxOpenFileDlg\\(" src/libs/controls src/plugins/updater -S` shows only the existing synchronous stack use, the retained singleton factory, and the new updater-owned instance.
- Build/run verification was not performed in this pass.
