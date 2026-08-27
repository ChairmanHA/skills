# ListMode Device Limits And File Row Count

## Goal

- Constrain direct ListMode cell editing with the current open device's TX frequency,
  level, and dwell-time capability ranges.
- After loading a file in the Insert/Fill dialog, constrain `Row Count` to the loaded
  file's total row count.

## Observations

- `FancyDevice` already queries and caches `tx_channel_capabilities` when the TX
  channel opens, but the shared `DeviceCapabilities` snapshot currently publishes
  only Playback, Streaming, and option capabilities.
- ListMode cell keyboards currently provide units, values, and steps without
  `minValue`/`maxValue`.
- The file-data path records the total parsed row count but does not update the
  numeric property's maximum, so the keyboard can accept a row count larger than
  the loaded data. `fillFileData()` later truncates it silently.

## Scope

- Extend the shared device capability snapshot with the TX carrier ranges needed by
  ListMode and populate them from the already validated H2 TX capability query.
- Resolve the latest snapshot each time a ListMode cell keyboard is opened and apply
  the matching frequency, level, or dwell bounds.
- Keep existing authoring rows unchanged when device capabilities change; only new
  direct edits are constrained.
- Set the file-data row-count maximum to the parsed file row count and restore the
  existing generated-data maximum when Auto Data Source is selected.
- Do not add an inferred MScan point-count capability or constrain adjacent ListMode
  point deltas with FScan/LScan step limits.

## Success Criteria

1. With a usable open-device capability snapshot, direct ListMode frequency, power,
   and dwell cell keyboards expose the corresponding H2 min/max range.
2. Without a usable snapshot, the existing keyboard behavior remains available and
   no guessed device limits are introduced.
3. Loading a file with `N` parsed rows makes the Row Count editor maximum `N` and
   normalizes its current value to `N`.
4. Returning to Auto Data Source restores the existing 20,000 generated-row limit.
5. Static checks cover CMake inclusion, all new capability fields, focused symbol
   references, and `git diff --check`.

## Verification Level

`static`

## Implementation Result

- Added `TxCarrierCapabilities` to the shared device capability snapshot and populated
  it from the cached H2 `freq_min/max`, `level_min/max`, and `dwelltime_min/max`
  values after a successful TX capability query.
- ListMode resolves the latest capability snapshot whenever a frequency, power, or
  dwell cell is opened. Valid ranges become the numeric keyboard min/max, and an
  existing out-of-range cell value is used as a clamped keyboard starting value.
- Existing table rows are not rewritten merely because the open device changes.
- `RowCount` now has an explicit 20,000 maximum for Auto Data Source. File loading
  replaces that maximum with the parsed file row count, and the editing-finished
  path uses the same source-dependent maximum.

## Static Verification

- Confirmed `devicecapabilities.h`, `editdialog.cpp`, and `listmodepanel.cpp` remain in
  the Core CMake target and `fancydevice.cpp` remains in the HTRA target.
- Confirmed all six published fields map directly to the corresponding checked-in H2
  capability fields and are consumed only by the matching ListMode column.
- Confirmed no positional aggregate initialization of `DeviceCapabilities` is present
  in the source tree.
- `git diff --check` passes for all edited source files; Git reports only the
  repository's existing LF-to-CRLF checkout warnings.
- No build or runtime test was performed, following the requested/static repository
  verification boundary.

## Follow-up: CommonPanel Center And Level Limits

### Goal

- Reuse the published TX carrier capability ranges for the CommonPanel `Center` and
  `Level` properties.
- Normalize retained values when a newly opened/switched device has a narrower
  frequency or level range.

### Design

- Subscribe the CommonPanel binding owner to the shared current-device capability
  snapshot and also apply the snapshot already available at panel construction.
- For a usable TX carrier snapshot, update the `Center` and `Level` property metadata
  before setting the current value again so `NumericProperty` performs its existing
  clamp and emits the normal value-change chain only when normalization is required.
- For an unavailable/invalid snapshot, restore the existing generic Center lower
  bound (`9 kHz`) and otherwise unbounded numeric limits instead of retaining the
  previous device's range.
- Do not add a second device query or duplicate H2 fields in CommonPanel.

### Success Criteria

1. Center editing uses the current device's `minimumFrequencyHz` and
   `maximumFrequencyHz`.
2. Level editing uses the current device's `minimumLevelDbm` and `maximumLevelDbm`.
3. A device capability change clamps retained out-of-range Center/Level values through
   the existing numeric-property update path.
4. No-device state does not retain stale limits from the previously open device.
5. Verification remains static and includes CMake membership and `git diff --check`.

### Implementation Result

- CommonPanel now applies the already published `TxCarrierCapabilities` snapshot to
  the shared `Center` and `Level` numeric-property metadata.
- The panel subscribes to subsequent capability changes and reapplies the current
  property values after updating both bounds, so the existing `NumericProperty`
  clamp and value-change flow normalizes only out-of-range values.
- Missing or invalid capabilities restore the original generic `Center >= 9 kHz`
  rule and otherwise unbounded finite-double limits, so a disconnected device does
  not leave stale device-specific bounds behind.

### Static Verification

- Confirmed `commonpanel.cpp` remains included in the Core CMake target.
- Confirmed Center consumes only the frequency range and Level consumes only the
  level range from the same snapshot already used by ListMode.
- Confirmed capability application runs both at binding construction and on
  `currentDeviceCapabilitiesChanged`.
- `git diff --check` passes for all edited source files with only the existing
  LF-to-CRLF checkout warnings. No build or runtime test was performed.

## Follow-up: Normalize The Complete ListMode Table

### Field Observation

- A restored/imported value such as `500 GHz` remains visible after a capable device
  opens because the first implementation applies min/max only when that individual
  cell keyboard is opened.
- Relying on per-cell interaction leaves invalid authoring/profile data in untouched
  rows even though the current device range is already known.

### Design

- Add one linear ListMode normalization function that reads a supplied/current usable
  TX carrier snapshot and clamps every valid row's frequency, level, and dwell time.
- Keep the visible model and the independent profile/downlink time model synchronized.
  In Global dwell mode, clamp both the saved per-row dwell values and the single
  displayed/global dwell value.
- Update only columns whose values actually change; do not reset or reconstruct the
  model, selection, range, or placeholder row.
- Suppress the manual Apply-button animation while capability-driven normalization is
  writing model values. After a capability change, emit the existing ListMode
  `argsChanged` signal only when normalization changed data, allowing the current
  StepSweep/runtime path to reconcile an enabled sweep.
- Reuse the same function after profile restore, auto insert/fill, file insert/fill,
  and top-level file load. Those authoring actions keep their existing explicit Apply
  semantics; normalization itself does not independently submit them.

### Success Criteria

1. Opening or switching to a device immediately clamps all valid ListMode rows to the
   device frequency, level, and dwell ranges without requiring cell clicks.
2. A table of roughly 20,000 rows is processed with bounded linear work and without a
   model reset.
3. Global and From List dwell modes retain their existing display/profile semantics.
4. Placeholder rows remain invalid placeholders and selected apply ranges are
   unchanged.
5. Data added while a device is already open is normalized through the same path.
6. Static verification covers all mutation entry points and `git diff --check`.

### Implementation Result

- ListMode now listens for current-device capability changes and normalizes every
  valid authoring row immediately when a usable TX carrier range becomes available.
- The normalization source is `m_timeModel`, which remains the profile/downlink
  owner. Frequency and power are synchronized into both models; per-row dwell values
  are clamped in `m_timeModel`, while `m_model` keeps either the clamped global dwell
  value or the clamped per-row dwell value according to the active mode.
- Existing non-finite values are normalized to the corresponding device minimum;
  finite out-of-range values are clamped to the nearest boundary.
- Only changed columns are updated, producing at most three consolidated updates per
  model. The placeholder row, selection, range, and model structure are untouched.
- Profile restore, placeholder-row completion, auto insert/fill, file insert/fill,
  and top-level file loading all invoke the same normalization path when a device is
  already open.
- Capability-driven changes suppress the manual Apply animation while writing and
  then use the existing `argsChanged` chain when actual table data changed.

### Static Verification

- Reviewed every ListMode row insertion/rebuild entry point and confirmed all paths
  that can introduce new external/generated values are normalized; dwell-mode-only
  rebuilds reuse already normalized model values.
- Confirmed the normalization method does not call model `clear()` or any insert,
  remove, or reset operation.
- Confirmed empty/placeholder-only tables do not emit a capability-driven apply.
- `git diff --check` passes for the edited ListMode files with only the repository's
  existing LF-to-CRLF checkout warnings. No build or runtime test was performed.
