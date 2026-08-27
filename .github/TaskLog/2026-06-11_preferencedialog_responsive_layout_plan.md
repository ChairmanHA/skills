# 2026-06-11 PreferenceDialog Responsive Layout Plan

## Scope
- Target: `PreferenceDialog` on PGA-capable runtime.
- Goal: remove oversized horizontal gaps caused by fixed dialog width while keeping the existing 3-column layout when six options are visible.
- Desired sizing: left/right margins 5px, inter-button spacing 5px, button minimum footprint 110x65.

## Local Evidence
- `preferencedialog.ui` currently gives the content widget a `minimumSize` width of 520 and an initial geometry width of 620.
- All six entries are placed in a `QGridLayout` with `Expanding` button size policies, so any extra width is distributed across three columns.
- `PreferenceDialog::showEvent()` only calls `adjustSize()`, which means the layout keeps honoring the oversized content width instead of collapsing to the visible controls.

## Falsifiable Hypothesis
- If visible preference buttons are reflowed sequentially into up to three columns and the content widget size is recomputed from visible column/row counts, the dialog will collapse to a compact width without breaking PGA's 2x3 layout.
- Cheap disconfirming check: for six visible buttons, the content area should resolve to `350x145` before the outer dialog adds title-bar/frame overhead.

## Implementation Plan
1. Remove the UI-level fixed minimum width and expanding layout defaults that force wide empty gaps.
2. Give each preference button a compact fixed footprint, then re-add visible buttons to the grid in sequence so hidden entries do not leave empty columns.
3. Recompute content width and height from visible columns/rows before showing the dialog.

## Validation
- Run file-scoped diagnostics on the touched files after editing.

## Follow-up Delta
- Win32 runtime screenshot shows the inner grid already compact but the outer frameless `Controls::Dialog` still remains wider, leaving the content centered inside a larger shell.
- Local fix direction: for `PreferenceDialog`, compute the final dialog size explicitly from title bar + content area (+ button box if present) and apply it before `Controls::Dialog::showEvent()` runs, instead of relying on `adjustSize()` alone.
- Root cause refinement: all runtime theme variants currently declare `PreferenceDialog { min-width: 455px; }`, so shrinking the content area below that width cannot reduce the outer shell on Win32.
- Updated fix direction: keep the compact button grid calculation, but on Win32 convert the theme-enforced surplus width into an explicit right-side spacer column so the extra width is intentionally consumed inside the content layout instead of showing up as unexplained outer whitespace.