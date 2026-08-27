# FancyTabWidget Grouped Layout Plan

## Background

- Current left-side business entry area is a single `QListWidget` in `IconMode`, with two columns driven by item width and widget width.
- Business entries are appended in registration order by `BusinessManager::registerBusiness()` -> `FancyTabWidget::addBusiness()`.
- `StepSweepBusiness` is semantically different from modulation businesses and should appear alone in the first row: left cell occupied, right cell empty.
- The user does not want an explicit separator line. The visual grouping should come from layout order only.

## Goal

- Keep using a single `QListWidget`.
- Render the first group as `AnalogGroup`, currently containing `StepSweepBusiness` only.
- Ensure the first modulation business starts on the next row by inserting a placeholder item when the sweep group size is odd.
- Avoid coupling UI order to plugin load order.

## Non-goals

- No extra `QFrame` or painted separator line.
- No refactor to two independent list widgets.
- No business-name special-casing inside selection, activation, save, or restore paths.

## Proposed Design

### 1. Add a lightweight UI group concept to `IBusiness`

- Introduce a small enum in `Core::IBusiness`, for example:
  - `AnalogGroup`
  - `ModulationGroup`
- Add a virtual accessor such as `businessUiGroup()`.
- Default return value remains `ModulationGroup` so existing businesses keep current behavior.
- `StepSweepBusiness` overrides the accessor to return `AnalogGroup`.

Rationale:

- The grouping rule belongs to UI metadata of the business, not to plugin dependency order.
- This keeps the design extensible if more non-modulation businesses are added later.

### 2. Keep one `QListWidget`, but rebuild it in grouped order

- `FancyTabWidget` should no longer treat visual order as plain append-only state.
- After adding a business, rebuild the list contents from the internal business/widget mappings.
- Rebuild order:
  1. All `AnalogGroup` items, preserving registration order within the group.
  2. If analog group count is odd, append one placeholder item.
  3. All `ModulationGroup` items, preserving registration order within the group.

This guarantees:

- `Freq Level Sweep` is the first item.
- The right cell of the first row stays empty.
- Modulation entries always start from the second row.

### 3. Represent the empty cell as a real placeholder item

- Insert a dedicated dummy `QListWidgetItem` for the empty slot.
- The placeholder must be fully non-interactive:
  - no selection
  - no enable state
  - no user role business binding
  - no business/widget mapping
- The placeholder should have the same size hint as normal items so grid alignment remains stable.

Recommended marker:

- Use a dedicated item data role such as `ListWidgetItemTypeRole`.
- Values can distinguish `BusinessItem` and `PlaceholderItem`.

### 4. Delegate must render placeholder as blank cell

- Update `ListviewDelegate::paint()` to detect placeholder items and skip all background/text rendering.
- Update `sizeHint()` to keep the placeholder cell size consistent with regular business items.

Rationale:

- If placeholder cells are painted like normal items, the empty slot will look broken instead of intentionally reserved.
- Leaving the cell visually blank is enough to convey the grouping.

### 5. Selection and lookup must ignore placeholders

- `FancyTabWidget::getItemByName()` must only return business items.
- `selectedBusiness()`, `currentWidgetBusiness()`, and current-item change handling must tolerate placeholders safely.
- If a placeholder somehow becomes current, the code should defensively clear selection or ignore it.

Recommended defensive rules:

- `onCurrentItemChanged()` should verify that the current item has a mapped `TabWidget` before switching stacked pages.
- Placeholder items should never be inserted into `m_item2Widget`, `m_item2Business`, or `m_widget2Item`.

### 6. Rebuild logic should preserve current page and active selection

- Rebuilding the list will change item instances and order, so it must restore state after repopulation.
- Preserve two notions separately:
  - current widget business
  - selected business
- After rebuilding:
  - restore current item from the previously visible business if it still exists
  - restore selected highlight state from the currently selected business if it still exists

This avoids regressions in:

- initialization flow
- config restore flow in `MainWindow`
- later UI refreshes if more businesses are registered dynamically

## Implementation Outline

### Files expected to change

- `plugins/core/ibusiness.h`
- `plugins/sweep/stepsweepbusiness.h`
- `plugins/core/fancytabwidget.h`
- `plugins/core/fancytabwidget.cpp`

### Main code changes

1. Extend `IBusiness` with a UI group enum and virtual accessor.
2. Override the accessor in `StepSweepBusiness`.
3. In `FancyTabWidget`, separate business registration state from visual list population.
4. Add placeholder item support and item-type role constants.
5. Add a helper that rebuilds the list in grouped order.
6. Harden selection/current-item logic to ignore non-business items.

## Complexity Assessment

- UI concept and business metadata: low complexity.
- QListWidget rebuild and state restore: medium complexity.
- Delegate placeholder rendering: low complexity.
- Overall: low-to-medium complexity, with the main risk concentrated in preserving selection/current-page behavior during rebuild.

## Risks

- Rebuilding the list can break config restore if `getItemByName()` or current-item restoration accidentally matches placeholders.
- Current selection logic mixes visual current item and active business highlighting; placeholder handling must not interfere with either path.
- Any code assuming every `QListWidgetItem` maps to a business/widget must be updated accordingly.

## Validation Plan

- Start app and verify `Freq Level Sweep` is the first item and the top-right cell is blank.
- Verify modulation items begin from the second row.
- Verify clicking business items still switches the stacked page correctly.
- Verify placeholder cell is not selectable and does not affect current page.
- Verify enable/selected highlight still works for business items.
- Verify config save/restore still restores:
  - active business
  - current widget business

## Follow-up

- If a second sweep-like business is added later, it will automatically occupy the top row right cell without any further UI logic changes.
- If future UX needs a visible section gap, one extra full blank row can be inserted by appending two placeholders between groups.