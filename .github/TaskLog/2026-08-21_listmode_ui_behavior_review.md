# ListMode UI Behavior Review

## Scope

Review three reported ListMode behaviors:

1. Changing the dwell-time mode resets the selected end row.
2. The device currently writes back only about 4000 applied MScan points even though the editor can contain more rows.
3. A reported Load File flow shows a Save dialog before the file-selection dialog.

Verification level: `static`

## Observations

- `ListModePanel::onBtnDwellTimeModeChanged()` rebuilds `m_model` when switching to `FromList`.
- Re-inserting those existing rows emits `rowsInserted`, and `ListModePanel::onRowsInserted()` treats that internal rebuild as a user append and unconditionally sets `rangeTo` to the total row count.
- The authoring table and the applied MScan subset are intentionally separate. `StepSweepPanel::fillCarrierPlanContext()` sends only `rangeFrom..rangeTo`, while `ListModePanel::applyPreviewSubsetWriteback()` updates only the corresponding subset and preserves rows outside it.
- The user confirmed that the UI table itself remains complete. When the device writes back about 4000 points, the program narrows the effective `rangeTo` relative to the selected `rangeFrom`; it does not remove authoring rows.
- The checked-in H2 header uses an `int16_t` point-count parameter but does not document a 4000-point limit. The current HTRA wrapper therefore uses `32767` as its structural limit. The reported 4000-point result is field evidence of a narrower device/API implementation limit and should not be replaced with a guessed compile-time rule without an API/capability contract.
- `EditDialog::onBtnLoadFileClicked()` calls only `getOpenPath()`; it has no Save path. The exact Save-then-Open sequence exists in `ListModePanel::loadFile()` when the current table differs from its last loaded/exported snapshot and the user chooses to export before loading.

## Inference

- The dwell-mode end-row reset is a definite UI bug caused by internal model reconstruction.
- Keeping more than 4000 editable rows is useful and consistent with the current authoring/applied-state split. Silent device-side truncation is acceptable only as a temporary behavior; the long-term UI should expose the device limit and applied count explicitly.
- The reported Save-then-Open sequence most likely refers to the top-level ListMode Load File action, not the Load File button inside `EditDialog`. Changing or removing its unsaved-content confirmation is a product/UX choice, not a safe bug fix to infer from the current evidence.

## Success Criteria

1. Switching between `Global` and `From List` preserves `rangeFrom` and `rangeTo` when the table row count is unchanged.
2. Genuine user insertions continue to extend `rangeTo` to the new last row.
3. The full authoring table remains intact when device preview/writeback returns fewer points.
4. No guessed 4000-point hard limit is introduced without a documented capability or confirmed API contract.
5. The two Load File entry points and their distinct behavior are documented clearly; no data-loss safeguard is removed without confirmation.

## Plan

1. Suppress only the `onRowsInserted()` range-update slot while `onBtnDwellTimeModeChanged()` reconstructs the visible model.
2. Reconnect the existing slot immediately after reconstruction and leave ordinary insertion behavior unchanged.
3. Perform focused static inspection and `git diff --check`; do not build or run.

## Implementation Result

- While switching from `Global` to `From List`, the visible model now disconnects only the `rowsInserted -> onRowsInserted` bookkeeping connection for the duration of the internal rebuild.
- The table model still emits its normal reset/insert signals to the view, while the user-selected apply range is no longer overwritten.
- The connection is restored immediately after the placeholder row is recreated, so real insert/fill/load operations keep their existing end-row extension behavior.
- No change was made to the approximately 4000-point writeback behavior. The complete authoring table remains available, and only the effective end row is narrowed from the selected start row.
- No change was made to Load File yet: static inspection proves that `EditDialog` opens only an Open dialog, while the top-level `ListModePanel` deliberately offers export-before-load. The reported entry point must be distinguished before changing this data-loss safeguard.

## Static Verification

- Confirmed `src/plugins/core/listmodepanel.cpp` is included by `src/plugins/core/CMakeLists.txt`.
- Confirmed ordinary insertions still reach `onRowsInserted()` after the mode-switch reconstruction completes.
- Confirmed the mode-switch path does not change the number of valid rows, so preserving the existing range remains valid.
- `git diff --check -- src/plugins/core/listmodepanel.cpp` passes with only the repository's existing LF-to-CRLF checkout warning.
- No build or runtime test was performed.
