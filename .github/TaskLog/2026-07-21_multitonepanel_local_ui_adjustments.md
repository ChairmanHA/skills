# 2026-07-21 MultitonePanel 本地 UI 调整

## Scope

- 只修改 `src/plugins/htra/multitonepanel.{h,cpp,ui}`。
- 不修改 minibar helper、IPC、generator 算法或其他业务层代码。

## Verification Level

- static

## Observation

- `MultitonePanel` 当前在 `setDiscreteKeepToneMode(true/false)` 内同时负责：切换离散模式按钮状态、隐藏/显示 `Notch Width`、以及启用/禁用 tone table。
- `MultitoneModulation` 构造阶段已经调用 `m_generator->setDiscreteKeepToneMode(true)`，并通过 `syncPanelFromGenerator()` 把 generator 当前状态回推给 panel。
- tone table 当前通过 `cellClicked` 只切换单个 `QTableWidgetItem` 的勾选状态，没有保留“点击前的多选行集合”。
- tone table 当前样式使用左对齐 header 和 item padding/border，行内复选框尺寸为 `18x18`，且 item bottom border 形成分割线。

## Inference

- 由于离散模式已经由 modulation/generator 默认置为 `true`，panel 可以直接隐藏“离散音模式”与 `Notch Width` 控件，并保持表格始终可用，而不需要扩散到其他 owner。
- 若要支持“多选后改其中一个勾选状态，同步整组选中行”，需要在 selection 变化前缓存点击命中的多选行，再把本次状态写回整组，否则普通点击可能先把 selection 收缩到单行。

## Success Criteria

1. tone table 三列内容居中显示，分割线移除，使能列勾选框视觉尺寸放大约 15%。
2. panel 默认按离散模式呈现，隐藏 `Discrete Tone Mode` 和 `Notch Width`，且不再因该模式切换禁用 tone table。
3. 当用户在 tone table 中已有多行选中，并切换其中一行的使能状态时，整组选中行同步到同一状态。
4. 改动保持在 multitone panel 本地；只做静态检查，不跑构建。

## 2026-07-21 Follow-up

- 用户补充要求：只去掉数据区列与列之间的竖向分割线，保留表格其余分割线。
- 用户现场截图显示第三列使能态视觉上像出现了双 checkbox。
- 本轮 follow-up 继续限制在 `multitonepanel.cpp`：恢复横向分割线，并把第三列底层绘制改成单一 checkbox 输出，避免委托基类与自绘叠加。

## 2026-07-21 Checkbox and Minibar Parity Follow-up

### Scope update

- Keep the existing staged main-panel interaction changes, but remove the second checkbox paint path from the tone Enabled cells.
- Synchronize the same Multitone editor presentation and row-selection interaction into `src/app/minibarhelper/remotemodeditors.{h,cpp}`.
- Do not change IPC payloads, generator behavior, or business/runtime ownership.

### Observation

- The staged main panel installs a custom item delegate while the checked item still owns `Qt::CheckStateRole` and the table stylesheet still sizes `QTableWidget::indicator`; this leaves two checkbox rendering mechanisms active.
- The helper's `RemoteMultitonePanel` still has the old Discrete Tone Mode/Notch Width visibility, left-aligned table, 18 px indicator, and single-row-only toggle behavior.

### Success criteria

1. Each Enabled data cell is rendered through only the standard item-view indicator path, sized at 21 px; no custom checkbox primitive is layered over it.
2. Main and helper Multitone editors both hide Discrete Tone Mode and Notch Width, keep the tone table enabled, center all three columns, retain horizontal row separators, and use the same larger checkbox size.
3. In both editors, clicking the Enabled cell of an already multi-selected row applies the clicked row's target state to the full selected row set and emits one logical update.
4. Verification remains static unless the user separately requests a build or runtime check.

### Static verification result

- `git diff --check HEAD` passed; only the repository's existing LF-to-CRLF working-copy warnings were reported.
- Confirmed both modified implementations are included by their active CMake targets: `HTRA` and `SGStudioMiniBar`.
- Confirmed the custom `ToneCheckStateDelegate`/`setItemDelegateForColumn` path is absent, while both editors retain the 21 px standard item indicator and matching multi-selection toggle flow.

## 2026-07-21 Native Indicator Scale Follow-up

### Qt source finding

- `QStyledItemDelegate::paint()` copies the supplied option and calls `initStyleOption()` again. Because the model index still contains `Qt::CheckStateRole`, Qt re-adds `QStyleOptionViewItem::HasCheckIndicator`; clearing the flag before calling the base implementation cannot suppress the standard indicator and caused the previous duplicate rendering.
- `QCommonStyle` obtains item-view checkbox layout from `PM_IndicatorWidth/PM_IndicatorHeight`, while the platform style draws `PE_IndicatorItemViewItemCheck`. A QSS width/height rule changes the style geometry path but does not reliably scale the native checkbox artwork.

### Revised design and success criteria

1. Install a column delegate in both main and helper editors.
2. Initialize the item option once, draw `CE_ItemViewItem` directly with `HasCheckIndicator` removed, then draw exactly one `PE_IndicatorItemViewItemCheck` primitive.
3. Scale that primitive around its center by exactly `1.15` in both axes, preserving the active platform/theme appearance.
4. Remove the ineffective fixed 21 px QSS indicator rule so there is only one size owner.
5. Keep verification static.

### Static verification result

- `git diff --check HEAD` passed; only the existing line-ending warnings were reported.
- Both delegates use `painter->scale(1.15, 1.15)` and draw one `PE_IndicatorItemViewItemCheck` after a direct indicator-free `CE_ItemViewItem` draw.
- No `QTableWidget::indicator` size rule or call to `QStyledItemDelegate::paint()` remains in either Multitone implementation.

## 2026-07-21 Visual Size and Column Alignment Follow-up

- Field screenshot shows the 1.15 scale is still visually subtle and the checkbox center is left of the Enabled header centerline.
- Increase the native indicator transform to `1.50` in both editors.
- Move the unscaled indicator rectangle to `option.rect.center()` before applying the transform, so scaling preserves exact cell/header center alignment.
- Static success check: both implementations use the same scale and center source; no independent offset remains.

## 2026-07-21 SVG Resource Indicator Follow-up

- Field verification shows painter scaling makes the native indicator visibly blurry at 1.50x.
- Native-style redraw at an enlarged rect is not guaranteed to be consistent across Windows/Fusion/Wayland styles, so use the same checked/unchecked dark/light SVG resources as `RemoteDigitalPanel::defaultTrimDownload`.
- Draw one SVG-backed `QIcon` into an actual centered `24x24` logical-pixel rectangle; remove painter transforms and platform indicator primitive drawing.
- Keep the indicator-free `CE_ItemViewItem` background draw so the resource icon remains the only checkbox output.

### Static verification result

- Both main and helper delegates use the same four Controls SVG resources and a centered `24x24` target rectangle.
- No `painter->scale()`, `PE_IndicatorItemViewItemCheck`, or scaled-delegate path remains.
- `git diff --check HEAD` passed; only existing line-ending warnings were reported.
