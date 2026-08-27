# File Dialog UDisk Mount Discovery Plan

## Problem
- On Raspberry Pi aarch64, inserted USB storage is mounted under /media/htra/<label>.
- The custom open/save dialogs populate external roots through RootListWidget::getUDiskPathes().
- The current implementation hard-codes a directory listing under /media/htra and relies on a name ignore list, which is fragile and does not model actual mounted volumes.

## Local Hypothesis
- OpenFileDlg.cpp and SaveFileDlg.cpp already rebuild the root list on each dialog init from RootListWidget::getUDiskPathes().
- If RootListWidget discovers mounted volumes under Linux media roots using QStorageInfo first, with a small directory fallback for /media/<user> style mount points, the dialogs will expose U-disk paths without changing dialog code.

## Cheap Check
- Confirm both dialogs call RootListWidget::getUDiskPathes() directly during init.
- Confirm the remote Raspberry Pi really mounts the USB volume at /media/htra/ESD-USB.

## Planned Change
- Rewrite RootListWidget::getUDiskPathes() around Linux mount discovery instead of a hard-coded ignore list.
- Prefer mounted volumes whose rootPath lives under /media/<user>, /run/media/<user>, or the generic media roots.
- Keep a directory scan fallback so the dialog still works when mount metadata is incomplete.
- Reuse the same discovered roots in getRelativePath() so relative display text stays consistent.
- Keep non-Linux behavior unchanged.

## Validation
- Run file diagnostics on the touched controls source files.
- Skip compilation because the user explicitly said no build is needed.