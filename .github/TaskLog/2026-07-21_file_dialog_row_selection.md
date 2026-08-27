# File dialog whole-row selection

## Scope

- Change the shared file-list table from cell selection to whole-row selection.
- Apply consistently to the Open and Save dialogs that both use `FileWidget -> DirWidget -> DirTableView`.
- Do not change single/multiple selection mode, sorting, filtering, directory activation, or file opening behavior.

## Evidence and design

- `src/libs/controls/DirWidget.ui` explicitly sets `selectionBehavior` to `QAbstractItemView::SelectItems`.
- `src/libs/controls/CMakeLists.txt` includes `DirWidget.ui` and the related file-dialog sources.
- `DirWidget::selectedPathes()` removes duplicate paths after reading selected indexes, so selecting every cell in a row still produces one path per selected row.
- Change only `selectionBehavior` to `QAbstractItemView::SelectRows`.

## Success criteria

- Clicking the filename or any other visible column selects the complete row.
- Existing single-selection and multi-selection configuration remains effective.
- Selected paths remain unique per selected row.
- Directory/file activation behavior remains unchanged.

## Verification

- Verification level: static.
- Parse the edited `.ui` file with Qt `uic`.
- Run `git diff --check` and inspect the focused diff.

## Result

- Pending.
