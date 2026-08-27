# ListMode Go-To And Row Drag Selection Fix

## Scope

1. Close the Go To numeric keyboard immediately after the `First` or `Last` shortcut performs its navigation.
2. Remove mouse-drag scrolling from the ListMode table so the row-number column supports native click-to-select-row and drag-to-select-contiguous-rows behavior. Scrolling remains available through the vertical scrollbar.

Verification level: `static`

## Observations

- The `First` and `Last` adapter callbacks call `goToFirst()` / `goToLast()` but never finish the `TouchNumKeyboard` dialog.
- The table viewport globally registers `QScroller::LeftMouseButtonGesture` with a one-second mouse press delay.
- `eventFilter()` tries to switch between scrolling and selection based on the pressed column, but ungrabs the gesture only after a 100 ms timer. A drag can therefore already be claimed by `QScroller` before the row-number path disables it.
- The same gesture delay explains why a single row-number click may not select immediately while a double click appears to work.
- The table already uses `SelectRows + ContiguousSelection`, which provides the requested row click/drag selection once the competing scroller and custom suppression logic are removed.

## Success Criteria

1. Pressing `First` navigates to the first row and closes the Go To keyboard.
2. Pressing `Last` navigates to the last row and closes the Go To keyboard.
3. A single click in column 0 selects the complete row.
4. Pressing in column 0 and dragging selects a contiguous row range; dragging beyond the viewport edge automatically scrolls to continue the selection.
5. Clicking an editable data cell continues to open its numeric keyboard and select its row.
6. Ordinary table-content dragging does not scroll; scrolling remains available through the vertical scrollbar and selection-edge auto-scroll.

## Plan

1. Accept the Go To keyboard from both shortcut callbacks after setting the corresponding target value.
2. Remove the viewport `QScroller` gesture and its delayed column-switching event filter.
3. Keep the table's native `SelectRows + ContiguousSelection` configuration.
4. Remove now-unused scroller/selection plumbing and perform focused static checks.

Temporary update: the user corrected the scrolling requirement after the initial implementation. Native selection auto-scroll must remain enabled when a row-number drag leaves the table viewport; only `QScroller` content dragging stays removed.

## Implementation Result

- The table viewport no longer installs the ListMode event filter that dynamically changed selection mode and re-grabbed the left-mouse scroller.
- The vertical scrollbar no longer installs the corresponding scroller-stop event filter.
- The constructor no longer creates or registers a `QScroller` mouse gesture. Native `SelectRows + ContiguousSelection` owns mouse press/drag selection, and native view auto-scroll remains enabled so selection can continue beyond the viewport edge.
- The `First` shortcut sets target row 1, performs the existing first-row navigation, and accepts the keyboard.
- The `Last` shortcut now stores the actual last-row target, performs the existing last-row navigation, and accepts the keyboard.

## Static Verification

- Confirmed no active call installs `ListModePanel::eventFilter()` on the table viewport or scrollbar.
- Confirmed no active call registers `QScroller::LeftMouseButtonGesture` for the table viewport during construction.
- Confirmed the table remains configured with `SelectRows` and `ContiguousSelection`.
- Confirmed selection-driven auto-scroll is explicitly enabled without restoring `QScroller`.
- Confirmed data-cell clicks remain connected to `popupNumKeyBoard()`; column 0 remains excluded by that slot.
- Confirmed both shortcut callbacks reach `TouchNumKeyboard::accept()`, after which the existing `finished` handler unchecks the Go To button and schedules keyboard deletion.
- No build or runtime test was performed.
