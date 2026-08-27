# MiniBar Sweep Panel Spacing Fix

## Scope

Restore the 5 px vertical spacing between the common Sweep row and the first
parameter row after the ListMode notice was introduced.

## Observation

- `RemoteSweepPanel` is included by `src/app/minibarhelper/CMakeLists.txt`.
- `m_pages` and `m_listModePlaceholderLabel` were added as two overlapping items
  in the same root-grid cell.
- The overlap was introduced with the ListMode notice and made the root row's
  spacing/layout behavior unreliable.

## Plan and success criteria

- Put the mutually exclusive parameter pages and ListMode notice in one
  zero-margin content container, leaving a single item in the root grid's second
  row.
- Keep existing ListMode visibility, compact-height resizing, and all Sweep
  behavior unchanged.
- Verify the focused diff and `git diff --check`; do not build or run.

## Verification level

`static`

## Result

- The root grid now has one parameter-area widget in its second row, so its
  existing `kPanelSpacing` is applied consistently between the common row and the
  first parameter row.
- The parameter area has zero margins and spacing; only the currently visible
  child contributes to its size, preserving the compact ListMode notice.
- Focused `git diff --check` passes. No build or runtime test was performed.
