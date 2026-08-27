# Updater Local File Dialog Paths

## Scope

- Adjust `UpdateDialog::loadLocalFile()` so the local update package picker opens from the user's Desktop on Win32 by default.
- Preserve the Linux/aarch64 local update picker default at the current user's Home directory.
- Adjust the Linux custom file dialog root so users can navigate back to `/` instead of being stopped at Home.
- Keep root selection correct when `/` overlaps mounted Flash paths by matching the longest applicable root.

## Assumptions And Evidence

- `src/plugins/updater/updatedialog.cpp` is part of the active updater target according to `src/plugins/updater/CMakeLists.txt`.
- `src/libs/controls/OpenFileDlg.cpp`, `src/libs/controls/SaveFileDlg.cpp`, and `src/libs/controls/DirWidget.cpp` are part of the active controls library according to `src/libs/controls/CMakeLists.txt`.
- Current `PxOpenFileDlg::init()` ignores its `localDir` argument and starts from `../data/`, which prevents updater from controlling the initial Linux directory.
- Current Linux local root resolution collapses paths under Home to Home, so the back button cannot navigate above Home in those cases.
- When the local root becomes `/`, root matching must prefer longer mounted paths so Flash aliases still win under `/media` or `/run/media`.

## Success Criteria

- On Win32, opening updater local file selection with no previous selection starts from Desktop, with Home as fallback.
- On Linux/aarch64, updater local file selection starts from Home when there is no previous selection.
- On Linux, the custom file dialog's local root is `/`, so the back button can navigate to the Linux root directory.
- On Linux, mounted Flash roots still display/select correctly because they are more specific than `/`.
- Existing previous-selection behavior is kept: after a successful selection, reopening starts from the selected file/path context.

## Verification

- Verification level: static.
- Read the edited code and run IDE linter diagnostics for changed files.
