# 2026-08-18 Updater External File Lock Risk

## Scope

- Record the latent Win32 update risk caused by external applications opening files inside the installation root.
- Document the preferred remediation if field evidence shows the risk is real.
- Do not change updater or maintenance behavior in this task.

## Observation

- Maintenance currently closes Explorer windows located at or below the installation root.
- It does not detect or close Excel, text editors, IDEs, or other external applications that hold installation files open.
- Administrator privileges do not guarantee that directory rename, backup merge, or backup removal can bypass an incompatible Windows file-sharing handle.
- `configuration/Language.xlsx` and `configuration/Settings.ini` are concrete examples: an external application can keep them open while maintenance renames the installation root, restores runtime files, or deletes the backup.

## Decision

- Treat this as a known latent risk rather than implementing speculative process termination now.
- If the failure is reproduced in the field, add a pre-firmware-update occupancy check that lists external applications using installation files.
- The default remediation must ask the user to save and close those applications, then retry the check.
- Do not make force termination of Excel, editors, or other user applications the default because one process may own unrelated unsaved documents.

## Verification Level

- Static documentation review only.

## Success Criteria

- The active updater KnowledgeBase document distinguishes targeted Explorer-window closure from external application handle ownership.
- It identifies rename, runtime-file restoration, and backup deletion as separate failure points.
- It records that detection and user-directed closure must occur before firmware update if this risk is later implemented.
- The KnowledgeBase index reflects the newly documented Win32 file-lock boundary.

