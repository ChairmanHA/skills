# File Dialog Header Style Bug

Date: 2026-07-06

## Scope

Investigate and fix the Raspberry Pi/Linux file dialog visual bug reported for SaveFile and LoadFile dialogs: the middle list header area appears white inside the otherwise dark custom file dialog.

## Evidence

- Remote host `192.168.3.179` is running SGStudio Release in main-window mode.
- Remote screenshot captured from the Wayland session shows `加载配置` open dialog with a white horizontal header row above the dark directory listing.
- The surrounding title bar, path controls, file rows, scroll bar, and buttons are dark, so this is not a missing icon/resource pack. It is the default `QHeaderView` palette/style showing through.
- The remote Qt 5.15.8 build-tree `configuration/theme.css` already contains the `Controls--FileWidget QHeaderView` / `QHeaderView::section` rules at the same offsets as the repository source, so QtCreator is not simply missing the file dialog stylesheet block.
- `src/libs/controls/CMakeLists.txt` includes `OpenFileDlg`, `SaveFileDlg`, `FileWidget`, `DirWidget`, and `DirTableView`, so the shared controls path is active code.

## Assumption

The same white strip appears in SaveFile and LoadFile because both dialogs share `FileWidget -> DirWidget -> DirTableView`. The existing stylesheet block is present, but on the remote Linux/Qt 5.15.8 runtime the custom-class-rooted selector is not covering every header paint path reliably. Adding a fallback rooted at the actual form object names and styling the corner/header sections should fix both without changing dialog behavior.

## Plan

- Inspect the current shared directory table implementation and UI setup before editing.
- Apply the smallest shared style fix in the file dialog QSS, covering the horizontal header and corner area with both class-based and objectName-based selectors.
- Keep file selection, sorting, model, and Open/Save behavior unchanged.
- Verify statically with diff review and `git diff --check`, then mirror the runtime QSS to the remote build-tree for a restart-based visual check.

## Success Criteria

- The file list header row no longer uses the default white background in either open/load or save dialogs.
- The fix applies through the shared `Controls` file dialog widgets.
- No runtime/build verification unless explicitly requested; this task is a static UI style fix after remote screenshot evidence.
