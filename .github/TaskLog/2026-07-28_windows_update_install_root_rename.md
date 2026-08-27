# Windows Update Install-Root Rename

Date: 2026-07-28

## Scope

- Fix the confirmed Win32 maintenance failure when the installation root cannot be renamed to its `_bak` sibling.
- Preserve the existing whole-directory backup/copy/rollback transaction.
- Do not restore the previous global `taskkill /IM explorer.exe` behavior.
- Keep the post-update restart path dependent on the surviving unelevated Explorer shell.

## Verification Level

Debug-build for the `maintenance` target, because the fix adds Win32 COM calls on a confirmed runtime failure path.

No full application build or runtime update rehearsal is planned.

## Observation

The field failure was:

```text
重命名备份失败: C:/Users/jsl/Desktop/SGStudio -> C:/Users/jsl/Desktop/SGStudio_bak
```

At diagnosis time, Windows Shell automation reported two Explorer windows whose locations were direct descendants of the target installation root:

- `C:\Users\jsl\Desktop\SGStudio\bin`
- `C:\Users\jsl\Desktop\SGStudio\plugin`

The target ACL grants the current user full control and no `_bak` sibling existed, so an open Shell folder handle is the concrete leading cause. This evidence supersedes the earlier assumption that all Explorer interaction was unrelated to the rename.

## Design

1. Before starting `CopyThread` on Windows, enumerate Shell windows through the existing `IShellWindows` COM integration already used by maintenance.
2. Close only filesystem Explorer windows whose current path equals the install root or is a descendant of it.
3. Leave the desktop Shell process and every unrelated Explorer window running.
4. Keep `CopyThread`'s existing three rename attempts so asynchronous Shell-window shutdown has time to release handles.
5. Do not add a global Explorer-kill fallback. A remaining rename failure continues to stop the transaction before the original installation is changed.
6. Keep the existing Explorer-based unelevated application restart unchanged.

## Success Criteria

- Explorer windows opened at the install root or below it receive `Quit()` before the root rename begins.
- Unrelated Explorer windows and the desktop process are not closed or terminated.
- No `taskkill /IM explorer.exe` or Explorer process restart is introduced.
- Linux behavior is unchanged.
- The original rename retry, rollback, and non-administrator restart paths remain intact.
