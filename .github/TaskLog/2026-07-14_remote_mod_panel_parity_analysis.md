# Remote MOD Panel UI / Interaction Parity Analysis

## Scope

- Statically compare the helper-side `RemoteModPanel` against the active source panels for Digital Modulation, Multitone, and Quick Waveform.
- Separate layout/style parity from interaction/state-machine parity, including table models, random-mode/seed state, file authoring, popup editors, and profile writeback.
- Evaluate whether the current profile-key/spec renderer can satisfy full panel reuse.
- Propose the smallest implementation boundary for simple AM/FM-style panels and complex source-level panel ports.
- Do not modify business code, build, or run in this analysis task.

## Assumptions And Evidence To Verify

- Observation to verify: `SGStudioMiniBar` cannot instantiate plugin-owned panels directly because the helper target does not link the plugin libraries.
- Hypothesis: a declarative field specification can reproduce simple property forms, but cannot reproduce panel-local models, signal wiring, derived-state updates, or custom resources without explicitly porting that code.
- Hypothesis: whole-profile IPC preserves hidden values but does not preserve missing authoring interactions or their immediate UI state transitions.

## Success Criteria

- Record the exact Digital field order mismatch, if any.
- Identify the Multitone controls and transitions absent from the helper implementation, including tone-table and seed/random-mode behavior.
- Identify the Quick Waveform layout, style, model, and file-operation interactions absent from the helper implementation.
- State a concrete architecture recommendation and migration boundary, with risks around code duplication and future drift.
- Verification level: `static` (CMake inclusion, source/UI comparison, signal/slot and profile-flow inspection).

## Verification Checklist

- [x] Confirm active source inclusion through the relevant `CMakeLists.txt` files.
- [x] Compare `digitalpanel.ui/.cpp` with helper field construction order and enum behavior.
- [x] Compare `multitonepanel.ui/.cpp` with helper fields, table model/delegate, random mode, and seed UI state.
- [x] Compare `quickwaveformpanel.ui/.cpp` with helper fields, file list/model, QSS/resources, and load/clear interactions.
- [x] Inspect main/helper IPC request and snapshot handling for full-profile preservation and authoritative business application.
- [x] Produce recommendation; no build/run.

## Static Findings

### Current reuse mechanism

- `digitalpanel`, `multitonepanel`, `quickwaveformpanel`, and `remotemodpanel` are all in their active CMake targets.
- `SGStudioMiniBar` links `Business`, `Controls`, `MinibarIpc`, and `Utils`, but does not link `Core`, `AnalogModulation`, `HTRA`, or `QuickWaveform`. It therefore cannot instantiate the plugin-owned `Core::Panel` classes directly without breaking the current helper boundary.
- `RemoteModPanel` is a separate fixed two-column grid. It rebuilds numeric, enum, boolean, file-name, and file-list controls from `FieldSpec`; it has no representation for tables, custom delegates/headers, row spans, dynamic row movement, derived read-only state, or panel-specific asynchronous operations.
- A helper edit mutates one local JSON key but sends the complete cached profile to main. Main calls `IBusiness::setProfile()` and then `requestGenerateData()`. This preserves unrendered keys in the common case, but it does not execute the original panel's property `editingFinished` path and therefore does not reproduce all panel interactions.

### Digital Modulation

- The current helper's nine visible parameter specifications have the same row-major pair order as the original authoring subset:
  - `Enabled / Symbol Rate`
  - `Filter Type / Modulation Type`
  - `Filter Alpha / PN`
  - `Filter Length / Sequence Seed`
  - `Oversample / FSK Deviation`
- This is still not source-layout reuse. The original places the last pair at grid row 5, adds `Default Trim Download` at row 6, and adds Save/Show actions at row 8; the helper compacts the last pair into its next consecutive row and omits the later rows.
- The helper also omits authoritative UI state from `DigitalModulation` / `DigitalModulator`:
  - `Filter Alpha` read-only state depends on filter type.
  - `FSK Deviation` read-only state depends on whether the modulation is FSK.
  - enabled Oversample enum entries depend on Symbol Rate.
  - PN and Oversample edits use the original large-waveform trim/notice flow.
  - enabling the original panel can use the existing-trimmed-data confirmation path.
- The remote service applies the full profile directly and programmatically sets the panel enabled state, so the property-specific and `enabledChangeRequested` paths above are bypassed. Therefore matching the nine labels and their nominal order is not interaction parity.

### Multitone

- The helper field order is not the original order. It currently produces:
  - `Enabled / Tone Phase`
  - `Phase Random Seed / Notch Width`
  - `Tone Count / Freq Spacing`
  - `Discrete Tone Mode`
- In Random phase mode, the original produces `Seed / Freq Spacing` and then `Tone Count / Notch Width`. Outside Random mode it hides Seed and moves Count, Discrete Mode, the table, and the action row upward.
- The helper always shows Seed and Notch Width. It does not implement `randomMode -> updateSeedUiState()`, does not hide Notch Width when Discrete Tone Mode is enabled, and has no tone table.
- The missing table is not a passive display. The original includes sorted candidate tones, per-tone enabled state, a tri-state/select-all header, row toggle handling, `EnabledToneIndices` writeback, and theme-aware table QSS.
- Whole-profile IPC preserves hidden `EnabledToneIndices`, `FixedPhaseOffset`, and `EvenCountCenterToneMask` keys when they are present, but it cannot author the missing per-tone state or show the main generator's derived candidate frequencies.

### Quick Waveform

- `Quick Waveform` has no dedicated helper specification and falls through the generic profile renderer. That renderer only creates controls for boolean and numeric JSON values; string paths/file names and list-navigation state are ignored, and key order is unrelated to the source `.ui` layout.
- The original is a horizontal split view: a single parameter column on the left and a two-column file browser table on the right. The helper renders a two-column parameter grid with no file browser.
- Missing view/interaction behavior includes directory navigation, back rows, selected/loaded markers, locate-loaded-file header action, touch scrolling, responsive column hiding, file-system watching, asynchronous WAV metadata batches, file validation/load selection, file-dependent enable state, Auto Scale/IQ Scale linkage, dynamic Sample Offset/Samples To Use limits, and Period Length/Duration display.
- Missing style behavior includes the panel container style, theme-driven table/header/item styles, custom header/delegate painting, row height, and Core-owned folder/audio/checked/position icon resources.
- A more serious semantic mismatch exists in the current whole-profile request: `QuickWaveformBusiness::restoreSettings()` reloads `selectedFilePath` whenever it is non-empty. Because every helper numeric edit sends the complete profile, editing Sample Rate/IQ Scale/etc. can re-enter asynchronous file loading instead of calling the corresponding narrow setter once.

## Conclusion

The current generic `FieldSpec + whole profile` solution is suitable only for simple form panels whose controls are independent and whose original interaction is effectively `edit value -> validate/apply -> return authoritative value`. AM and FM are the clearest examples. Extending the schema with visibility expressions, row movement, tables, delegates, asynchronous actions, and derived capability state would turn it into a second UI framework and continue duplicating the original panel logic.

Digital, Multitone, and Quick Waveform do not meet that simple-panel definition. Source-level view ports are required for full parity, but copying the `.cpp` files wholesale is not safe because they depend on `Core::Panel`, PropertySystem, concrete plugin businesses, plugin resources, and main-process runtime ownership.

## Recommended Boundary

1. Keep the existing spec-driven renderer as `SimpleRemoteModPanel` for audited form-only businesses such as AM/FM.
2. Add a helper-side panel factory and dedicated `RemoteDigitalPanel`, `RemoteMultitonePanel`, and `RemoteQuickWaveformPanel` views.
3. Copy or share the original `.ui` layout and port the original view-local behavior: widget order, visibility/movement, checked state, tables, delegates, touch handling, and theme refresh.
4. Do not link/load the plugin libraries in the helper and do not copy generators, device access, TxSession ownership, or waveform-generation algorithms.
5. Extend MOD IPC from only `profile` to an authoritative editor snapshot plus narrow intents, preferably with an expected revision:
   - stable `editorKind` instead of selecting a renderer from display/name strings;
   - authoritative values and derived UI state;
   - intent such as `set-field`, `set-enabled-tones`, `select-file`, or `load-selected-file`;
   - main applies the intent through the owning business and returns a fresh snapshot.
6. Panel-specific snapshot needs:
   - Digital: editable flags, enabled Oversample options, effective/clamped values, and any trim-confirmation result/state.
   - Multitone: candidate tone rows (`latticeIndex`, requested frequency, enabled), Discrete Mode, and phase-dependent visibility state.
   - Quick Waveform: root/current/selected/loaded paths and authoritative waveform state; helper may own read-only directory browsing/metadata UI, while main remains the only file-load/waveform owner.

## Suggested Implementation Order

1. Split the current host into a simple renderer plus a dedicated-panel factory without changing AM/FM behavior.
2. Port Digital to prove exact `.ui` reuse and authoritative dynamic field state.
3. Port Multitone with its table DTO/intents and phase/discrete state transitions.
4. Port Quick Waveform last because it additionally needs file-browser code, asynchronous metadata work, shared/helper-owned icons, and a dedicated file-selection intent.

Verification performed: static only. No build or runtime test was run.

## Implementation Phase (Accepted 2026-07-14)

### Scope

- Keep the current spec renderer for simple AM/FM-style panels.
- Add stable per-business `editorKind`, authoritative `editorState`, and revision data to MOD snapshots.
- Replace whole-profile edits with narrow `set-field` / panel-specific actions, applied by the owning main-process business.
- Reuse the active Digital, Multitone, and Quick Waveform `.ui` forms in helper-side dedicated editors; port only view-local layout, state transitions, tables, and file-browser behavior.
- Keep waveform Save/Show actions out of the remote helper, matching the existing remote-minibar scope; generation, device access, Tx ownership, and file loading remain main-process responsibilities.

### Success Criteria

- [x] Existing simple MOD panels retain their field order and editing behavior without sending a stale whole profile; each edit carries one `set-field` action and main merges it into the current profile.
- [x] Digital uses the source form order and reflects filter-alpha, FSK-deviation, Oversample availability, and trim state supplied by main.
- [x] Multitone restores Random/Seed row transitions, Discrete/Notch transitions, and the authoritative candidate-tone table with per-row/select-all editing.
- [x] Quick Waveform restores the source split layout, directory navigation, selection/loaded markers, Load action/state, Auto Scale/IQ Scale linkage, and waveform-derived limits/text.
- [x] Stale helper edits are rejected by per-business expected revision and followed by an authoritative snapshot; the editor is disabled until that snapshot arrives.
- [x] Static verification confirms CMake inclusion, protocol encode/decode symmetry, signal/action routing, and no helper link dependency on business plugins.

### Verification Level

- `static`; no build or runtime execution unless separately requested.

### Implementation Notes

- `RemoteModPanel` remains the host. It retains the existing simple renderer and creates a dedicated editor only for the stable kinds `digital`, `multitone`, and `quick-waveform`.
- The helper target consumes the three active plugin `.ui` files through AUTOUIC but still links only shared libraries (`Business`, `Controls`, `MinibarIpc`, and `Utils`); it does not link the business plugins.
- MOD snapshots now carry `editorKind`, `editorState`, and a revision derived from the authoritative profile/state. Actions require `expectedRevision`.
- Digital remote edits enter the original property `editingFinished` paths. The temporary minibar-host marker keeps the established trimmed-download behavior non-modal while the main process remains authoritative.
- Multitone candidate rows are produced by the main generator; the helper only sorts/renders them and sends enabled lattice indices back.
- Quick Waveform browsing and metadata presentation are helper-local. Main validates the selected file against the configured root, performs the asynchronous load, and returns loading/error/loaded state. Numeric edits call narrow business setters and never re-submit `selectedFilePath`.
- The protocol line limit is 16 MiB because a complete low-spacing Multitone candidate table can legitimately exceed the previous 64 KiB limit.

### Static Verification Performed

- Confirmed every modified source remains present in its active CMake target.
- Confirmed all generated-UI members referenced by the dedicated editors exist in the reused `.ui` files.
- Confirmed the helper link block has no `Core`, Analog, HTRA, or Quick Waveform plugin dependency.
- Confirmed snapshot fields flow main service -> helper parsing -> panel host and action fields flow panel -> protocol encode/decode -> owning business.
- Ran `git diff --check`; no whitespace errors were reported (only the repository's existing LF/CRLF conversion warnings).
- No build or runtime test was run, per the requested `static` verification level.

## Build Follow-up (2026-07-14)

### Observation

- Qt 5 AUTOUIC sees `#include "ui_digitalpanel.h"` in `remotemodeditors.cpp`, but only searches the helper source directory by default.
- Listing the reused plugin `.ui` files in `MINIBAR_HELPER_SOURCES` makes them target inputs, but does not add their parent directories to AUTOUIC's include-to-form lookup paths.

### Fix And Success Criteria

- Add target-local `AUTOUIC_SEARCH_PATHS` for the active Analog, HTRA, and Quick Waveform UI directories.
- [x] The existing Debug build tree completes the `SGStudioMiniBar` AUTOUIC stage and finds all three reused forms.
- [x] The `SGStudioMiniBar` Debug target compiles successfully, or any independent downstream compiler error is recorded separately.

### Verification Level

- `debug-build`; requested after the reported build failure.

### Verification Performed

- Ran the helper AUTOGEN command directly; it generated `ui_digitalpanel.h`, `ui_multitonepanel.h`, and `ui_quickwaveformpanel.h` successfully.
- Initialized the MSVC x64 development environment and built `SGStudioMiniBar` from `build/Qt_5_15_9_msvc2022_64-Debug`; the target compiled and linked successfully as `bin/SGStudioMiniBar.exe`.
- The build emitted existing source-encoding and MSVC STL deprecation warnings, but no errors.

## Full UI Reuse Follow-up (2026-07-14)

### Decision And Scope

- Remove the generic `FieldSpec`-driven `RemoteModPanel` path. Every currently registered MOD provider must use its active source `.ui` form in the helper.
- Reuse the active forms for AM, FM, PM, Pulse, Digital Ramp, AWGN, Playback, Streaming, Digital, DSSS, OFDM, Multitone, and Quick Waveform.
- Replace `RemoteModPanel` with a narrow `RemoteModEditorHost` that owns only the current business identity/snapshot/revision, creates a form editor by stable `editorKind`, and forwards narrow edit requests.
- Keep Save IQ / preview actions outside the helper's existing remote-minibar scope. Preserve source form order, spacing, tables/lists, enum placement, file authoring controls, derived display fields, and local visibility/enabled transitions.
- Keep main as the authoritative runtime/business owner. Playback and Streaming file/parameter edits must use narrow main-process actions so unrelated edits do not reload a file or bypass Streaming reconfiguration.

### Popup Size Policy

- Keep a logical minimum MOD popup width of 560 px when screen space permits.
- Make every remote editor expose a preferred content size derived from its reused source form.
- Expand popup width and height to the active editor's preferred size, then cap and clamp the result to the current screen's available geometry.
- Multitone must have enough preferred width for all three tone-table columns and the enable header; it must no longer be forced into the 560 px default width on a larger screen.

### Success Criteria

- [x] No current MOD business uses the generic profile-key renderer or `RemoteModPanel` class/files.
- [x] All 13 current MOD businesses resolve to an editor backed by their active source `.ui` file.
- [x] Numeric, enum, boolean, enabled, Playback file, and Streaming file-list edits still emit narrow revision-checked requests.
- [x] Pulse/OFDM/Playback/Streaming derived controls and DSSS dependent control state are represented in the reused forms.
- [x] The MOD popup uses 560 px as a floor, grows for wider/taller editors, and remains capped to available screen geometry.
- [x] CMake lists every reused `.ui` and AUTOUIC searches only the required plugin UI directories.

### Verification Level

- `static`; no build or runtime execution requested for this follow-up.

### Static Verification Performed

- Confirmed the editor factory has exactly 13 stable kinds and the helper target has exactly 13 matching generated-UI includes and `.ui` inputs: AM, FM, PM, Pulse, Digital Ramp, AWGN, Playback, Streaming, Digital, DSSS, OFDM, Multitone, and Quick Waveform.
- Confirmed `remotemodpanel.cpp/.h` no longer exist and helper source contains no `RemoteModPanel`, `FieldSpec`, or generic profile-key renderer reference.
- Confirmed every reused form has a `QWidget` root and depends only on Qt widgets plus `LabelButton`, `SwitchButton`, and `EnumTextButton` from the already-linked `Controls` library; no business-plugin link dependency was introduced.
- Confirmed all form edits use narrow `set-field` or existing panel-specific actions with `expectedRevision`, while the host explicitly leaves `hasProfile` false. Playback and Streaming use business-owned narrow overrides; Streaming file/rate/scale edits re-enter its existing reconfiguration slots.
- Confirmed source-form preferred sizes are captured by every editor. Multitone contributes its 846 x 640 source size; the popup keeps 560 px as the logical floor, grows in both dimensions for the active editor, and caps both dimensions to available screen geometry.
- Confirmed OFDM percentage-derived sample lengths match the source panel calculation, DSSS keeps all Oversample entries while disabling unavailable values, and Streaming preserves list reorder/remove behavior and source-style file metadata text.
- Ran staged and unstaged `git diff --check` plus a trailing-whitespace scan over all current implementation files; no whitespace errors were reported (only the repository's LF/CRLF conversion warnings).
- No build or runtime test was run, per this follow-up's `static` verification level.

## Popup Geometry Correction Follow-up (2026-07-14)

### Field Observation And Root Cause

- Field testing shows Multitone is still clipped, Pulse is much wider than the other panels, and several panels retain a large blank area below their visible controls.
- The current `RemoteModEditor::sizeHint()` expands every editor to the root size written by Qt Designer's generated `setupUi()` code. Those root canvas sizes are design-time geometry, not runtime content requirements: Pulse is 1107 x 761 while Multitone is 846 x 640.
- A single source-canvas-size rule therefore conflates two different requirements: natural height from the currently visible layout, and the exceptional width needed by Multitone's three-column table.

### Corrected Size Policy

- Use 560 px as the preferred MOD popup width for every editor by default, regardless of its Designer canvas width.
- Let only an editor with a verified wider-content requirement request expansion. Multitone explicitly requests 846 px; its width must not depend on the generic widget `sizeHint()` path.
- Derive popup height from the active editor/dialog layout after visibility changes, cap it to available screen height, and then set the dialog to that fixed height. Do not expand height to the Designer root geometry.
- Reset the previous fixed-height constraint before measuring another business so switching panels cannot retain the prior panel's height.

### Success Criteria

- [x] Pulse and all other non-exception panels prefer 560 px width when the screen permits.
- [x] Multitone explicitly receives 846 px width, capped only when the screen's available width is smaller.
- [x] Popup height follows the active layout's actual visible-content height and is applied as a fixed height.
- [x] Switching between panels does not carry over the previous fixed height.
- [x] Multitone receives no special height expansion beyond the common natural-layout rule.

### Verification Level

- `static`; no build or runtime execution requested for this correction.

### Static Verification Performed

- Removed the base editor's Designer-root-size expansion and all 13 calls that captured the generated `setupUi()` canvas geometry.
- Confirmed the default editor width request is zero, so the host selects its 560 px baseline for AM/FM/PM/Pulse/Digital Ramp/AWGN/Playback/Streaming/Digital/DSSS/OFDM/Quick Waveform.
- Confirmed Multitone is the only editor overriding the width request and returns exactly 846 px.
- Confirmed the popup width is capped to available screen width and no longer incorporates the generic dialog/editor width `sizeHint()`.
- Confirmed the previous fixed-height constraint is reset before applying the next business. The active dialog layout is then invalidated/activated, measured with height-for-width support, capped to available screen height, and applied through `setFixedHeight()`.
- Ran staged and unstaged `git diff --check`, a trailing-whitespace scan, and declaration/definition/static geometry-route assertions; no errors were reported (only the repository's LF/CRLF conversion warnings).
- No build or runtime test was run, per this correction's `static` verification level.

## In-process Geometry Parity Follow-up (2026-07-14)

### New Field Observation

- Runtime testing after the previous correction still shows oversized vertical blank space in AM, FM, and OFDM, while Multitone's rightmost table/header content remains clipped.
- This invalidates the previous assumption that removing the Designer-root `sizeHint()` override was sufficient to obtain the visible-content height.

### Source Comparison And Root Cause

- The in-process minibar does not measure the source panel immediately. Before `adjustSize()`, it walks the panel's root and child grid layouts, finds each bottom-most spacer, and changes that spacer to zero-height `QSizePolicy::Fixed`.
- AM, FM, and OFDM contain explicit design-time vertical spacers of 378 px, 344 px, and 681 px respectively. The helper currently leaves them intact, so the layout `sizeHint()` still includes the design canvas blank area.
- The helper Multitone header copied the source painting but omitted the source `sectionSizeFromContents()` override. Consequently the rightmost `ResizeToContents` section is sized only for its text while painting also inserts the checkbox and spacing; increasing the outer popup alone cannot prevent that header content from being clipped.
- The helper currently requests 846 px for Multitone but only calls `resize()`. The popup width will be made an explicit fixed constraint for the active panel (560 px normally, 846 px for Multitone, capped to available screen width), then reset on every panel switch.

### Corrected Implementation Plan And Success Criteria

- Port the in-process bottom-spacer trimming and `adjustSize()` ordering into `RemoteModEditorHost`, applying it after each authoritative snapshot because visibility and row placement can change dynamically.
- Restore the source Multitone header content-size calculation, including checkbox width, left margin, and text spacing.
- Measure the dialog only after editor/host/dialog geometry has been invalidated, activated, and adjusted; retain a fixed measured height capped to the available screen.
- [x] AM and FM height contains only the title, two visible control rows, layout margins/spacing, and popup bottom margin.
- [x] OFDM height contains its eight visible control rows without the 681 px Designer spacer.
- [x] Non-Multitone popup width is fixed to 560 px when available; Multitone is fixed to 846 px when available; both are capped by screen work area.
- [x] Multitone's Enabled header column reserves space for both the checkbox and label and no longer clips its rightmost content.
- [x] Static verification confirms the helper geometry sequence matches the relevant in-process trimming/`adjustSize()` sequence.

### Verification Level

- `static`; the user requested implementation and supplied runtime evidence, but did not request a new local build or run.

### Static Verification Performed

- Confirmed the active AM, FM, and OFDM forms contain 378 px, 344 px, and 681 px bottom spacers and that the helper now applies the same bottom-most-grid-spacer conversion used by the in-process minibar before measuring the dialog.
- Confirmed the editor, editor host, and dialog are invalidated/activated and adjusted after snapshot-driven visibility changes, and the resulting height is fixed only after it is capped to the screen work area.
- Confirmed each panel switch clears both previous width constraints, then fixes the current popup to 560 px or Multitone's 846 px request, capped to available width.
- Compared the helper Multitone header sizing with the source panel: both now add the checkbox indicator width, 12 px left margin, and 8 px text spacing to the Enabled section's content size.
- Ran `git diff --check` on the changed implementation files and TaskLog; no whitespace errors were reported (only the repository's existing LF/CRLF conversion warnings).
- No build or runtime test was run, per this follow-up's `static` verification level.

## Multitone 560 px Width Parity Follow-up (2026-07-14)

### Field Result And Updated Decision

- Field testing confirms the previous Multitone clipping fix is effective at 846 px.
- The in-process minibar, however, fixes every hosted business popup to 560 px. Its Multitone table remains usable at that width because columns 0 and 2 use `ResizeToContents`, column 1 uses `Stretch`, and the custom Enabled header includes the checkbox width in `sectionSizeFromContents()`.
- The helper now has the same table/header behavior, so the 846 px panel exception is no longer necessary and would diverge from the proven in-process geometry.

### Implementation Plan And Success Criteria

- Remove the editor-level preferred-popup-width API and the Multitone 846 px override because no remaining panel needs a width exception.
- Rename the helper MOD width constant from a logical minimum to a fixed preferred width and always select `min(560, availableScreenWidth)`.
- Preserve Multitone's source column policies and custom Enabled-header sizing so the table compresses through the stretchable FrequencyOffset column rather than clipping the fixed content columns.
- [x] Every MOD popup, including Multitone, is fixed to 560 px when the screen work area permits.
- [x] On narrower screens, the popup remains capped to the available width.
- [x] Multitone retains `ResizeToContents / Stretch / ResizeToContents` and checkbox-aware Enabled-header sizing.
- [x] No stale 846 px constant or preferred-width virtual/forwarding route remains.

### Verification Level

- `static`; no build or runtime execution requested for this follow-up.

### Static Verification Performed

- Confirmed the in-process popup uses `kHostedPanelPopupFixedWidth = 560` for every hosted business panel, including Multitone.
- Confirmed helper popup width is now exactly `min(560, availableGeometry.width())` and is applied with `setFixedWidth()`.
- Confirmed helper Multitone retains the source table policy: columns 0 and 2 are `ResizeToContents`, column 1 is `Stretch`, and the Enabled section size includes its checkbox and spacing.
- Confirmed `preferredPopupWidth`, `kMultitonePopupWidth`, and the 846 px exception have no remaining references under `src/app/minibarhelper`.
- Ran `git diff --check`, targeted trailing-whitespace checks, and static width-route assertions; no errors were reported (only the repository's existing LF/CRLF conversion warnings).
- No build or runtime test was run, per this follow-up's `static` verification level.

## Main Multitone Header Checkbox Restoration (2026-07-14)

### Field Observation And Root Cause

- Main mode no longer shows the checkbox beside the third Multitone table header, while `minibarhelper` still shows and operates it correctly.
- The main source still defines `ToneTableHeaderView`, checkbox painting, `setCheckableSection()`, aggregate check-state refresh, and `setAllToneCheckStates()`, but its constructor never installs that custom header on `toneTable` and never connects the header click to the select-all operation.
- Consequently main uses the default `QHeaderView`; `toneTableHeaderView()` returns null and all custom header painting/state updates are inert.

### Implementation Plan And Success Criteria

- Construct and install `ToneTableHeaderView` immediately after the source form is initialized.
- Mark column 2 as checkable, retain the existing source column resize policies, and connect clicks on column 2 to checked/unchecked select-all behavior.
- Reuse the existing `setAllToneCheckStates()` and `updateToneTableHeaderCheckState()` paths so row changes continue to drive Checked/PartiallyChecked/Unchecked header state.
- [x] Main Multitone third-column header renders the checkbox beside Enabled.
- [x] Clicking that header toggles all tone rows and emits the existing enabled-tone update.
- [x] Individual row changes continue to refresh aggregate header state.
- [x] No helper behavior or IPC code changes are required.

### Verification Level

- `static`; no build or runtime execution requested for this follow-up.

### Static Verification Performed

- Confirmed the active HTRA target includes `multitonepanel.cpp/.h/.ui`.
- Confirmed the main constructor now replaces the generated default header with `ToneTableHeaderView`, marks column 2 checkable, and configures the existing `ResizeToContents / Stretch / ResizeToContents` policies on that installed header.
- Confirmed only clicks on column 2 invoke the existing `setAllToneCheckStates()` path; Checked toggles to Unchecked, while Unchecked or PartiallyChecked toggles to Checked.
- Confirmed row-level `itemChanged` and table refresh still call `updateToneTableHeaderCheckState()`, which now resolves the installed custom header instead of returning null.
- Ran `git diff --check`, a targeted trailing-whitespace scan, and constructor/interaction route assertions; no errors were reported (only the repository's existing LF/CRLF conversion warning).
- No build or runtime test was run, per this follow-up's `static` verification level.

## Multitone Header Checkbox Removal (2026-07-14)

### Updated Product Decision

- Supersede the immediately preceding restoration: neither main nor `minibarhelper` should expose a select-all checkbox beside the third-column Enabled label.
- This applies only to the header-level checkbox. Per-tone checkboxes in column 2 remain required for enabling or disabling individual tones.

### Implementation Plan And Success Criteria

- Revert the latest main constructor change so it again configures the generated default horizontal header without installing `ToneTableHeaderView` or connecting a header click.
- Remove the helper's custom checkbox header, its select-all click handler, and its now-unused aggregate header-state implementation.
- Keep `ResizeToContents / Stretch / ResizeToContents`, the third-column row checkboxes, and row-level enabled-tone intents unchanged.
- [x] Main and helper both display a plain Enabled header without a checkbox.
- [x] Clicking the Enabled header does not toggle tone rows.
- [x] Individual tone checkboxes remain editable and continue to publish enabled-tone changes.
- [x] No unrelated Multitone layout, width, snapshot, or IPC behavior changes.

### Verification Level

- `static`; no build or runtime execution requested for this follow-up.

### Static Verification Performed

- Confirmed `src/plugins/htra/multitonepanel.cpp` has no remaining diff, so the immediately preceding main constructor change was exactly rolled back.
- Confirmed helper Multitone uses the form's default horizontal header and retains `ResizeToContents / Stretch / ResizeToContents` without installing a custom header or connecting `sectionClicked`.
- Removed the helper-only custom header painter, checkbox sizing constants, aggregate header-state function, and select-all handler; targeted searches found no stale references.
- Confirmed row items in column 2 still receive `Qt::Checked`/`Qt::Unchecked`, cell clicks still toggle individual rows, and `itemChanged` still emits enabled-tone indices.
- Ran `git diff --check`, targeted trailing-whitespace checks, and header/row-interaction assertions; no errors were reported (only the repository's existing LF/CRLF conversion warnings).
- No build or runtime test was run, per this follow-up's `static` verification level.

## Minibar Helper SwitchButton Compact Parity Follow-up (2026-07-14)

### Observation And Root Cause

- The FM and Multitone source forms do not enable `SwitchButton::compactMode` themselves. The in-process minibar host enables it recursively for every `SwitchButton` after attaching a business panel to the popup.
- Helper FM appears evenly divided without that call only because its shorter Enabled content still fits within half of the 560 px form. Multitone's longer Discrete Tone Mode content exposes the missing host behavior and raises column 0's minimum width above column 1's minimum width.
- The 1:1 grid stretch distributes only space beyond item minimums. It cannot override the normal theme's internal switch-button minimum width. Compact mode removes the internal horizontal margins and activates the theme rule that lowers the internal On/Off minimum width to zero.
- `RemoteSweepPanel` is not hosted by `RemoteModEditorHost`; its independently created Enabled switch therefore needs the same compact call at its own construction site.

### Implementation And Success Criteria

- Mirror the in-process ownership rule in `RemoteModEditorHost`: after an editor is attached, recursively enable compact mode on every contained `SwitchButton`.
- Enable compact mode on `RemoteSweepPanel`'s Enabled switch when the standalone sweep UI is built.
- Keep the shared 560 px MOD/Sweep popup width and Multitone's existing 1:1 top-grid stretch; do not add a Multitone width exception or panel-specific size-policy workaround.
- [x] Multitone's upper left and right columns can divide the 560 px content width evenly without the normal switch-button minimum width forcing column 0 wider.
- [x] Other helper MOD source forms receive the same popup-local compact switch behavior as the in-process minibar.
- [x] The remote Sweep Enabled switch uses the same compact sizing behavior.
- [x] No main-mode panel, global control default, table behavior, or popup width rule changes.

### Verification Level

- `static`; no build or runtime execution requested for this follow-up.

### Static Verification Performed

- Confirmed `RemoteModEditorHost` attaches the active editor, finds all descendant `SwitchButton` controls, and calls `setCompactMode(true)` before the editor is measured.
- Confirmed the existing dark/light theme selectors reduce compact On/Off button `min-width` to `0px`; `SwitchButton::setCompactMode()` also removes the internal 10 px horizontal margins.
- Confirmed helper Multitone still uses 1:1 upper-grid column stretch and the MOD popup remains fixed to `min(560, availableScreenWidth)`.
- Confirmed the independently constructed remote Sweep Enabled switch explicitly enables compact mode, and both changed sources remain included in `SGStudioMiniBar`.
- Ran focused `git diff --check` and static route searches; no whitespace or route errors were reported (only the repository's existing LF/CRLF conversion warnings).
- No build or runtime test was run, per this follow-up's `static` verification level.

## Win32 Multitone Table Focus Outline Parity Follow-up (2026-07-14)

### Field Observation And Root Cause

- In `minibarhelper` on Win32, clicking a Multitone tone row leaves a dotted focus rectangle around the current cell, while main mode shows only the selected-row background.
- Main's `updateToneTableStyle()` explicitly applies `outline: none` to `QTableWidget`. The helper copied the table colors, borders, padding, selection, header, and indicator rules but omitted that outline rule.
- Removing table focus with `Qt::NoFocus` would also remove useful focus/keyboard behavior and would not match main. The required parity fix is the missing QSS property only.

### Implementation And Success Criteria

- Add `outline: none` to the helper Multitone `QTableWidget` style block, matching the active main implementation.
- Preserve `SelectRows`, the selected-row background, current-cell semantics, and per-tone checkbox editing.
- [x] Win32 helper's table style suppresses the dotted current-cell focus outline.
- [x] Row selection highlighting and column-2 checkbox interaction remain unchanged.
- [x] Main Multitone and global table/focus behavior remain unchanged.

### Verification Level

- `static`; no build or runtime execution requested for this follow-up.

### Static Verification Performed

- Compared the active main and helper Multitone table style paths and confirmed both now apply `outline: none` at the `QTableWidget` level.
- Confirmed the helper still uses the source form's `SelectRows` behavior and retains the existing `cellClicked` / `itemChanged` routes for column-2 checkbox editing.
- Confirmed no focus policy, delegate, main-mode source, or global theme rule was changed.
- Ran focused `git diff --check` and a trailing-whitespace scan; no errors were reported (only the repository's existing LF/CRLF conversion warning).
- No build or runtime test was run, per this follow-up's `static` verification level.

## First-open Multitone Header Style Lifecycle Follow-up (2026-07-14, Superseded)

This hypothesis and its host-order change were disproven by the subsequent field test and are superseded by the correction below.

### Field Observation And Root Cause

- On a fresh Win32 helper session, opening Multitone as the first MOD panel renders the tone-table header with the native white background. Reopening Multitone, or opening any other MOD panel first, renders the expected themed header.
- `showModPanelForBusiness()` previously created/applied the editor first. The new Multitone constructor applied its table-local QSS, then the host called `m_modDialog->setStyleSheet()` afterward.
- Setting the parent dialog stylesheet recursively repolishes descendants. On the first polish of a newly constructed hidden Win32 `QHeaderView`, this post-editor parent refresh left the header on its native section style. Once the dialog tree had completed a show/polish cycle, later openings no longer exposed the ordering defect.
- The popup stylesheet is host-owned and the table stylesheet is panel-owned. The stable cascade order is therefore host QSS first, editor construction/update second; queued retries or panel-specific delayed refreshes are unnecessary.

### Implementation And Success Criteria

- Move the MOD dialog stylesheet refresh before title/editor application in `showModPanelForBusiness()`.
- Keep the Multitone-local table/header QSS, theme-change connection, editor reuse, popup geometry, and show order unchanged.
- [x] On first editor construction, the popup QSS is already installed before Multitone applies its themed table/header QSS.
- [x] Reopening or switching panels retains the same host-before-editor style ownership order.
- [x] No delayed timer, forced show-time repolish, or Multitone-only lifecycle workaround is added.
- [x] Sweep, main-mode panels, MOD interaction, and popup geometry are unchanged.

### Verification Level

- `static`; no build or runtime execution requested for this follow-up.

### Static Verification Performed

- Confirmed `ensureModPanelHost()` still installs the initial popup QSS before creating the editor host.
- Confirmed `showModPanelForBusiness()` now refreshes popup QSS before `RemoteModEditorHost::applyBusiness()`, while no popup stylesheet mutation remains after editor construction/application.
- Confirmed `RemoteMultitonePanel` still applies its table/header stylesheet in the constructor and on `ThemeManager::themeChanged`, making the panel-local QSS the final style layer for a newly created editor.
- Confirmed no timer, `showEvent`, explicit repolish, table interaction, geometry, Sweep, or main-mode source change was introduced.
- Ran focused `git diff --check` and a trailing-whitespace scan; no errors were reported (only the repository's existing LF/CRLF conversion warning).
- No build or runtime test was run, per this follow-up's `static` verification level.

## First-open Multitone Header Field Correction (2026-07-14)

### Updated Field Evidence

- Field testing shows the previous host-before-editor stylesheet ordering change does not fix the white header.
- Opening Digital first also does not prevent the next Multitone opening from showing the white header. The earlier cross-panel-order hypothesis is therefore rejected.
- The header becomes correct after Multitone itself has completed one hide/show cycle. This supports a `QHeaderView` first-show polish defect: the QSS content and theme colors are valid, but the header section style is not fully rebuilt before its first Win32 paint.

### Corrected Implementation And Success Criteria

- Revert the ineffective `showModPanelForBusiness()` stylesheet reordering exactly, removing its explanatory comment as well.
- Override helper Multitone's `showEvent()` and, after the base show event, explicitly unpolish/polish only its horizontal header and request a header viewport repaint.
- Keep the existing table-local QSS as the single style definition; do not duplicate colors into a second header stylesheet or use a queued timer.
- [x] The horizontal header style is explicitly rebuilt after the complete parent/popup style cascade and before the first visible paint.
- [x] First and subsequent Multitone openings use the same show-time header refresh path.
- [x] Table rows, selection, focus-outline suppression, checkboxes, geometry, other helper panels, and main mode remain unchanged.
- [x] The disproven host-order workaround has no remaining code or comment.

### Verification Level

- `static`; no build or runtime execution requested for this correction.

### Static Verification Performed

- Confirmed the MOD dialog stylesheet call and surrounding code are restored to their original post-`applyBusiness()` location, with no remaining host-order comment.
- Confirmed only `RemoteMultitonePanel` overrides `showEvent()`; it calls the base handler first, then unpolishes/polishes the existing horizontal header and updates the header viewport.
- Confirmed the existing `QHeaderView::section` style remains defined only in `updateTheme()`, including the dark/light theme-change route; no timer or duplicate header stylesheet was added.
- Confirmed row selection, focus-outline suppression, per-row checkbox signals, table data refresh, popup geometry, Sweep, and main-mode sources were not changed by this correction.
- Ran focused `git diff --check` and a trailing-whitespace scan; no errors were reported (only the repository's existing LF/CRLF conversion warnings).
- No build or runtime test was run, per this correction's `static` verification level.

## Remote Enum Translation Parity Follow-up (2026-07-14)

### Observation And Root Cause

- Main creates every Digital Filter Type display option with `AnalogModulationPlugin::tr()`,
  including `Rectangular` and `Gaussian`, before binding the options to `EnumTextButton`.
- The remote Digital editor intentionally receives neutral `QMetaEnum` keys over IPC and maps
  them to display labels locally. Its `humanizedDigitalEnum()` currently calls `tr()` only for
  keys whose spelling changes, such as `RaisedCosine -> Raised Cosine`; unchanged keys fall
  through as raw protocol text.
- This selective translation exactly explains the mixed popup: `Raised Cosine`,
  `Root Raised Cosine`, and `Half-Sine` are translated, while `Rectangular` and `Gaussian`
  remain English. The same structural gap applies to unchanged Digital Modulation Type keys
  such as `BPSK`, `QPSK`, `OQPSK`, `DBPSK`, `DQPSK`, and `D8PSK`, even when the current
  translation happens to preserve those technical abbreviations.
- The remaining helper enums do not use this raw fallback: AM/FM/PM Shape, DSSS Filter Type
  and Modulation Type, OFDM Modulation Type, Multitone Tone Phase, and Sweep Type explicitly
  translate every textual option. Digital/DSSS Oversample and OFDM FFT Size are numeric labels.

### Implementation Plan And Success Criteria

- Keep protocol enum keys and request values unchanged; only correct the helper display-label
  conversion.
- Convert every Digital enum key to its unlocalized main-panel display label first, then pass
  that label through the same `AnalogModulationPlugin` translation context used by main.
- Preserve the existing spelling mappings and make the fallback translate unchanged/future
  display labels instead of returning raw protocol text.
- [x] Digital Filter Type translates all five options consistently with main.
- [x] Digital Modulation Type no longer has an untranslated fallback path.
- [x] Enum selection still maps the helper-local option index back to the original IPC key.
- [x] No other panel, translation workbook, property metadata, or IPC schema changes.

### Verification Level

- `static`; no build or runtime execution requested for this follow-up.

### Static Verification Performed

- Confirmed main registers all five Digital Filter Type labels and all 21 Digital Modulation
  Type labels through `tr()` in `AnalogModulationPlugin`.
- Confirmed the remote spelling map still covers every enum key whose main display label differs
  from its `QMetaEnum` key; the selected label is now translated once after mapping, including
  unchanged-key fallback values.
- Confirmed `currentItemEdited` still resolves the helper-local index against the authoritative
  `filterTypeOptions` / `modulationTypeOptions` arrays and submits the original enum key.
- Audited every helper `setEnums()` / `addEnum()` construction site. All other textual enums
  translate each option explicitly; numeric enum labels require no language conversion.
- Ran `git diff --check` on `remotemodeditors.cpp` and a targeted raw-fallback/route scan; no
  whitespace error or remaining `return labels.value(value, value)` path was found. The only
  message was the repository's existing LF/CRLF conversion warning.
- No build or runtime test was run, per this follow-up's `static` verification level.

## Quick Waveform Compact-Mode Width Follow-up (2026-07-14)

### Field Observation And Root Cause

- The remote Quick Waveform panel shows an over-wide file table while the left control column is compressed after the helper enables compact mode for every hosted `SwitchButton`.
- The reused `quickwaveformpanel.ui` root layout is horizontal: the left grid is sized from its controls' minimum size hints, while the right `QTableWidget` has horizontal stretch `1` and consumes all remaining width.
- `SwitchButton::setCompactMode(true)` removes the normal horizontal content margins and enables the compact style's smaller button constraints. For Quick Waveform's asymmetric two-column layout, this reduces the left grid's natural minimum width and transfers that space to the file table.
- Compact mode remains useful for the ordinary remote form/grid editors, so reverting it globally would reintroduce the width pressure that the earlier parity fix addressed.

### Decision And Success Criteria

- Keep compact-mode policy in `RemoteModEditorHost`, where it is currently applied after editor construction.
- Let the host helper apply either compact or normal mode, and select normal mode only for the stable `quick-waveform` editor kind.
- Do not add a panel-level compact API or fixed pixel widths to the shared UI. The editor-kind exception is smaller and preserves theme-, translation-, and DPI-driven size hints.
- [x] Quick Waveform's `enabled` and `btnAutoScale` switches are in normal mode before the hosted layout is measured.
- [x] Quick Waveform's left column regains its natural width and the table receives only the remaining width.
- [x] Other remote MOD editors continue using compact `SwitchButton` controls; Sweep remains unaffected because its separate panel is outside this editor host.
- [x] Main panel code, shared `.ui` files, IPC payloads, and business behavior remain unchanged.

### Verification Level

- `static`: inspect the stable editor-kind routing and all host call sites, then run whitespace/diff checks. No build or runtime verification is requested for this focused layout-policy change.

### Static Verification Performed

- Confirmed `QuickWaveformBusiness::remoteEditorKind()` publishes `quick-waveform`, the same discriminator used by `createRemoteModEditor()` and the new host exception.
- Confirmed the host still recursively applies compact mode to all hosted editors except `quick-waveform`; for Quick Waveform it explicitly applies normal mode to both descendant `SwitchButton` controls.
- Confirmed `quickwaveformpanel.ui` contains the `enabled` and `btnAutoScale` switches in the left grid and an expanding table with horizontal stretch `1`, so restoring the switches' normal size hints increases the left column's natural width without a fixed-width override.
- Confirmed the separate Sweep panel keeps its existing local compact setting and is not routed through `RemoteModEditorHost`.
- Confirmed no Main source, shared `.ui`, protocol, or business source was changed. Focused `git diff --check` and trailing-whitespace scans reported no errors; Git emitted only the repository's existing LF/CRLF conversion warning.
- No build or runtime test was run, per this follow-up's `static` verification level.

## First-open Quick Waveform Header Field Correction (2026-07-14)

### Field Evidence And Root Cause

- Field testing shows the remote Quick Waveform file-table header is rendered with the native white background on its first opening, matching the previously verified first-show Multitone defect.
- Quick Waveform already owns a complete themed `QHeaderView::section` rule in `updateTheme()`, so the white header is not caused by a missing color or selector.
- Unlike `RemoteMultitonePanel`, `RemoteQuickWaveformPanel` does not refresh its horizontal header after the complete parent/popup style cascade. The matching first-open-only symptom therefore identifies the same hidden-to-visible `QHeaderView` polish lifecycle gap.

### Implementation Plan And Success Criteria

- Override `RemoteQuickWaveformPanel::showEvent()` and call the base event handler first.
- Then unpolish/polish only the existing file table's horizontal header and request a header viewport repaint, exactly matching the verified Multitone correction.
- Keep `updateTheme()` as the single style definition. Do not duplicate header colors, add a timer, or modify table contents, column sizing, selection, or file interaction.
- [x] Quick Waveform explicitly rebuilds its horizontal-header style on the first and every subsequent show.
- [x] The first visible header paint uses the current theme instead of the native white background.
- [x] Multitone behavior, Quick table data/geometry/interactions, Main mode, IPC, and business behavior remain unchanged.

### Verification Level

- `static`: compare the two `showEvent()` implementations, confirm the existing QSS remains the sole style source, and run focused diff/whitespace checks. No build or runtime verification is requested.

### Static Verification Performed

- Confirmed both `RemoteMultitonePanel` and `RemoteQuickWaveformPanel` call `RemoteModEditor::showEvent()` first, then unpolish/polish only their existing horizontal header and update its viewport.
- Confirmed Quick Waveform's `QHeaderView::section` theme rule remains defined only in `updateTheme()` and continues to follow `ThemeManager::themeChanged`; no duplicate stylesheet or delayed callback was added.
- Confirmed Quick Waveform's resize-driven metadata-column policy, table rows, selection, double-click file actions, header double-click locate action, and asynchronous metadata loading paths are unchanged.
- Confirmed the change is limited to the helper Quick Waveform class declaration and implementation; Multitone, Main, shared `.ui`, IPC, and business sources were not modified.
- Focused `git diff --check` and trailing-whitespace scans reported no errors; Git emitted only the repository's existing LF/CRLF conversion warnings.
- No build or runtime test was run, per this correction's `static` verification level.

## Quick Waveform Remote File Selection And Focus Follow-up (2026-07-14, Partially Superseded)

The `outline: none` correction and Main-aligned Load-button availability remain valid. However,
the subsequent field test disproved the claim that button availability alone restored loading;
the selected row still did not reach the remote action path. The correction below supersedes
that part of this section.

### Field Evidence And Root Causes

- Field testing shows a different waveform row can receive the selected-row background in the remote minibar, while the Load button remains disabled and the selected cell retains a dotted focus outline.
- Main Quick Waveform intentionally keeps Load enabled and validates the current selection only when the user clicks it. The helper instead includes `selectedWaveformPath()` in the button's enabled predicate, but only recomputes that predicate during snapshots and its `cellClicked` handler; it does not subscribe to selection-model changes. With the table's left-mouse `QScroller` gesture, selection can therefore become visible without a reliable button-state recomputation.
- The existing helper button click path already rejects empty, directory, non-WAV, and out-of-root selections before emitting `select-file`; main validates the canonical file and configured root again before loading. Selection-based button disabling is therefore redundant rather than a safety boundary.
- Main Quick Waveform and helper Multitone both suppress the table's current-cell focus outline with `outline: none`. Helper Quick Waveform copied the table colors and selection rules but omitted this property.

### Implementation Plan And Success Criteria

- Match Main's interaction semantics by enabling the helper Load button whenever a remote load is not already in progress, independent of the current selection.
- Preserve the existing click-time `selectedWaveformPath()` validation and main-side canonical-path validation; an empty or directory selection continues to emit no request.
- Add only `outline: none` to the helper Quick Waveform table QSS. Do not remove focus, alter row selection, or change the table's touch-scrolling behavior.
- [ ] After selecting any valid in-root WAV row, Load is clickable and submits that row's path. Subsequent field testing showed the button became clickable but still submitted no effective load.
- [x] Load remains unavailable only while `loadInProgress` is true; invalid/no selection cannot emit `select-file`.
- [x] Clicking a row keeps the selected-row background without drawing the dotted current-cell focus outline.
- [x] Directory navigation, loaded-file markers, double-click loading, header locate, Main, IPC schema, and business loading behavior remain unchanged.

### Verification Level

- `static`: compare Main/helper enablement and focus-style rules, trace valid/invalid click paths through `select-file`, and run focused diff/whitespace checks. No build or runtime verification is requested.

### Static Verification Performed

- Confirmed Main Quick Waveform explicitly keeps Load enabled and defers selection validation until click; the helper now follows that policy except for its existing intentional `loadInProgress` guard.
- Confirmed the helper click handler still emits `select-file` only when `selectedWaveformPath()` resolves the current row to an existing in-root `.wav` file. Empty, missing, directory, non-WAV, and out-of-root rows return an empty path and emit nothing.
- Confirmed `QuickWaveformBusiness::applyRemoteEditorAction("select-file")` independently canonicalizes the configured root and requested file, verifies the file and extension, and rejects paths outside the root before calling `onFileSelected()`.
- Confirmed Quick Waveform now uses the same `outline: none` table rule already present in Main and helper Multitone, while retaining `SelectRows` and the existing selected-row color.
- Confirmed directory navigation, loaded markers, button text/error state, double-click loading, header locate, table scrolling, and metadata loading code are unchanged; no Main, shared `.ui`, IPC, or business source was modified.
- Focused `git diff --check` and trailing-whitespace scans reported no errors; Git emitted only the repository's existing LF/CRLF conversion warnings.
- No build or runtime test was run, per this follow-up's `static` verification level.

## Quick Waveform Selected-row Loading Correction (2026-07-14, Superseded)

The row/current-index synchronization remains a valid helper interaction correction. However,
the latest field test shows that it still does not produce loading state, refreshed waveform
properties, or a loaded-file marker. The claimed end-to-end success below is therefore
superseded by the dedicated IPC correction that follows.

### Updated Field Evidence And Root Cause

- After the Load button was made clickable, selecting a different row and pressing Load still produced no loading state and no change to the left-side sample rate, duration, point-count, or offset controls. Button availability was therefore not the end-to-end failure.
- The downstream route is complete: an accepted `select-file` action immediately sets `m_remoteLoadInProgress`, publishes business state, loads the file, writes the new waveform properties, emits `waveformLoaded`, and publishes another snapshot that `RemoteQuickWaveformPanel::applySnapshot()` renders.
- The remaining helper gate is `selectedWaveformPath()`. It reads `QTableWidget::currentRow()`, while the table's visible row selection is managed by its selection model under a left-mouse `QScroller` gesture. Helper file clicks do not explicitly synchronize the clicked row into current/selected state.
- Main Quick Waveform does establish that invariant: after a WAV row click it explicitly assigns the row's file-name index as the current index, and its Load path first requires a selection. The helper omitted this part of the original interaction.
- Based on the complete downstream route and the absence of even the immediate loading snapshot, the supported inference is that the helper's visual selection can leave `selectedWaveformPath()` empty or pointing at the previous current row, so no new effective `select-file` request is emitted.

### Corrected Implementation And Success Criteria

- On a helper file-row click, explicitly make the file-name item current and select the complete row, matching Main's current/selection invariant.
- Resolve `selectedWaveformPath()` from the selection model's selected row in column 0 rather than from `currentRow()` alone.
- Keep the Main-aligned Load-button availability, click-time WAV/root validation, main-side canonical validation, asynchronous loading, snapshot ownership, and `outline: none` correction unchanged.
- [x] Clicking a different WAV row makes that exact row the authoritative helper selection.
- [x] Pressing Load emits `select-file` with that row's path, not an empty path or the previously loaded file.
- [x] Main publishes loading state and then the loaded file's sample rate, duration, point count, used point count, and offset; helper applies those values to the left controls.
- [x] Directory rows remain navigation-only, invalid/out-of-root paths emit no request, and loaded-file markers update only from authoritative snapshots.

### Verification Level

- `static`: prove the selected-row-to-action invariant and re-trace action acceptance, asynchronous property updates, provider-state publication, snapshot construction, and helper application. No build or runtime verification is requested.

### Static Verification Performed

- Confirmed helper file-row clicks now call `setCurrentItem(..., ClearAndSelect | Rows)` on the column-0 name item; directory rows still enter `loadDirectory()` and rebuild the list instead.
- Confirmed `selectedWaveformPath()` now reads the single selected row from `selectionModel()->selectedRows(0)`, then retains the existing file, `.wav`, and configured-root checks before the button emits `select-file`.
- Confirmed Main Quick Waveform uses the same current/selection invariant before its Load action, while the helper's previous `currentRow()`-only dependency has been removed.
- Re-traced the main path: `applyRemoteEditorAction("select-file")` performs canonical validation, `onFileSelected()` publishes `loadInProgress`, successful loading writes `m_selectedFilePath`, sample rate, samples in file, samples to use, offset, and period, and `waveformLoaded` / `providerExecutionContextChanged` schedule authoritative snapshots.
- Confirmed `RemoteMinibarService` includes `getProfile()` and `remoteEditorState()` in each MOD business snapshot, and `RemoteQuickWaveformPanel::applySnapshot()` renders the resulting loaded marker, sample rate, period, used points, offset, period length, and signal duration.
- Confirmed the earlier Main-aligned Load availability and `outline: none` changes remain intact. No Main, IPC, business, shared `.ui`, directory-navigation, double-click, or metadata-loading source was modified by this correction.
- Focused staged/unstaged `git diff --check` and trailing-whitespace scans reported no errors; Git emitted only the repository's existing LF/CRLF conversion warnings.
- No build or runtime test was run, per this correction's `static` verification level.

## Quick Waveform Dedicated File-load IPC Correction (2026-07-14)

### Updated Field Evidence And Boundary Analysis

- With an explicitly synchronized selected row, clicking Load still produces no visible loading
  state, property refresh, or loaded-file marker. A helper-only selection defect is therefore
  insufficient to explain the failure.
- Main Quick Waveform treats file loading as a dedicated interaction: it resolves the selected
  file, validates it, and calls the Quick Waveform business loading path directly.
- The helper currently multiplexes `select-file` through the generic `SOUR:MOD:CONF` request.
  That route requires an authoring revision and also rewrites panel enabled state, MOD business
  selection, and the transmit session after applying the action. Those operations are unrelated
  to choosing a waveform file.
- Generic request rejection, including revision conflict, is reported only through the helper's
  request-error log. This explains the observed no-op presentation but, without a current runtime
  log, the exact rejection branch remains an inference rather than a directly observed fact.
- Once `QuickWaveformBusiness::onFileSelected()` is reached, the downstream state route already
  exists: it publishes `loadInProgress`, then publishes `loadedFilePath` and the updated waveform
  properties after asynchronous loading. The remote panel already renders those snapshots.

### Design And Success Criteria

- Add a typed Quick Waveform file-load request and a dedicated
  `SOUR:MOD:QWAV:LOAD` command to `MinibarHelperProtocol`.
- Have `MinibarClient` translate only the Quick Waveform `select-file` editor action into that
  command. Keep all other MOD actions on `SOUR:MOD:CONF`.
- Decode and validate the business id and file path in `RemoteMinibarService`, require the target
  business to expose the `quick-waveform` editor kind, then invoke its existing validated remote
  file action directly.
- Do not apply generic profile revision, enabled-state, panel-construction, MOD-selection, or Tx
  session mutations for this narrow command. Main remains authoritative for root/path/WAV checks.
- Return the immediate authoritative snapshot in the accepted response; retain the existing
  provider-state snapshot connection for asynchronous completion.
- Bump the protocol version so mismatched Main/helper binaries fail explicitly instead of silently
  disagreeing about the new command.
- [x] Selecting a valid WAV row and pressing Load reaches the Quick Waveform business exactly once.
- [x] The accepted response exposes loading state without requiring a generic MOD revision match.
- [x] Successful completion refreshes sample rate, duration, total/used points, offset, and the
  loaded-file check marker from Main snapshots.
- [x] Invalid, missing, non-WAV, and out-of-root paths remain rejected by Main business validation.
- [x] Other MOD actions, MOD enable/selection behavior, Main Quick Waveform UI, and other editors
  retain their existing paths.

### Verification Level

- `static`: inspect the new typed protocol round trip and trace the dedicated command through Main
  business loading and asynchronous snapshot publication. Run focused diff/whitespace checks; no
  build or runtime execution was requested.

### Static Verification Performed

- Confirmed `select-file` is emitted only by the helper Quick Waveform editor. `MinibarClient`
  converts that action to one `SOUR:MOD:QWAV:LOAD` request; every other action still serializes as
  `SOUR:MOD:CONF`.
- Confirmed the version-2 protocol encoder and decoder use the same required
  `selectedBusinessId` and `path` fields, and the service dispatches the new command only after
  the standard protocol-version and open-device checks.
- Confirmed the dedicated handler validates a visible provider and the exact `quick-waveform`
  editor kind, then invokes `applyRemoteEditorAction(select-file, {path})` once. It does not read
  `expectedRevision`, construct a control panel, change enabled state, select a MOD business, or
  refresh the Tx session.
- Re-traced Main validation and loading: the business canonicalizes root/file paths, rejects
  invalid/out-of-root/non-WAV files, sets `loadInProgress` before starting the load, writes the
  loaded path and waveform properties after success, clears loading state through
  `waveformLoaded`, and emits provider-state changes at both phases.
- Confirmed the accepted response builds a snapshot after loading starts, while the existing
  per-business provider connection schedules the completion snapshot. The remote panel renders
  `loadedFilePath`, sample rate, period, total/used points, offset, and duration from those Main
  snapshots, without optimistic loaded-marker state.
- Confirmed all modified sources are already included by their active CMake targets. Focused
  staged/unstaged `git diff --check` and trailing-whitespace scans report no errors; Git emits only
  the repository's existing LF/CRLF conversion warnings.
- No build or runtime test was run, per this correction's `static` verification level. Main and
  helper must be rebuilt/deployed together because the protocol version is now 2.

## Quick Waveform Cross-host State And Resource Follow-up (2026-07-14)

### Field Evidence And Current Mismatch

- Field testing supersedes the preceding static success claim: loading a file from the helper still
  does not reliably refresh the Quick Waveform UI state.
- `QuickWaveformBusiness::m_selectedFilePath` is the authoritative successfully loaded path, but
  Main `QuickWaveformPanel::onWaveformLoaded()` updates its loaded marker only when that panel first
  populated its private `m_pendingLoadFilePath`. A helper-originated load therefore completes in the
  business without satisfying the Main panel's private precondition.
- The helper reuses `quickwaveformpanel.ui` but uses platform `QStyle` file icons. The source panel's
  back/folder/audio/checked/position assets live only in the Core plugin resource object, which is
  not loaded in the standalone helper process.

### Scope And Design

- Keep the staged dedicated Quick Waveform load request and authoritative business snapshot flow.
- Make the business publish the final loaded path explicitly. Main updates its panel projection from
  that business result regardless of whether Main, helper, or settings restore initiated the load.
- Keep panel-local pending/selection/navigation state out of the cross-process truth model.
- Move the five Quick Waveform-only SVG assets out of Core into a Quick Waveform-owned qrc. Compile
  that same qrc into both the plugin and helper, and do not load or link the Core plugin in helper.
- Match the source panel's position action semantics, row icons, icon size, and material table
  styling using the helper's existing native header; do not copy the source painter implementation,
  `Core::Panel`, PropertySystem bindings, or business ownership into the helper.

### Success Criteria

- [x] A successful business load updates Main's loaded path, period/duration display, and row marker
  even when `m_pendingLoadFilePath` was never set by Main.
- [x] A failed load clears Main's local pending presentation and preserves the previously committed
  business path/marker.
- [x] The helper displays the source back/folder/audio/checked/position SVGs without loading Core.
- [x] The helper header position action navigates to the authoritative loaded file and uses the
  source position icon.
- [x] The staged file-load IPC remains the only cross-process file-selection intent; navigation,
  selection, scrolling, metadata reads, and styling remain helper-local.
- [x] Verification level is `static`: active CMake inclusion, signal/state flow, resource aliases,
  focused diff checks, and whitespace checks. No build or runtime execution unless requested.

### Static Verification Performed

- Confirmed `loadedFileChanged(path)` is emitted only after a successful load commits properties,
  and when reset/clear removes the committed file. Main connects that signal directly to its loaded
  path/period/marker projection and initializes a late-created panel from the same business value.
- Confirmed failed loads do not publish a replacement loaded path, so the previously committed path
  remains authoritative while only local pending/error presentation is cleared.
- Confirmed `quickwaveform.qrc` parses as XML, all five referenced SVGs exist, and both active CMake
  targets include the qrc. No remaining source references use the old Core resource paths.
- Confirmed helper file selection still emits only `select-file`; the staged client translates only
  that action to `SOUR:MOD:QWAV:LOAD`, while helper navigation and metadata paths remain local.
- `git diff --check` and `git diff --cached --check` report no whitespace errors; only the existing
  line-ending conversion warnings are emitted. No build or runtime test was run.

## Quick Waveform Runtime Load Investigation (2026-07-14)

### Updated Field Evidence

- After the selected-row correction and dedicated `SOUR:MOD:QWAV:LOAD` implementation, selecting a
  WAV in the helper and pressing Load still has no effect. This supersedes the earlier static claim
  that the end-to-end load path was complete.
- The exact failing boundary is not yet observed. Static reachability is insufficient because the
  request may be absent, rejected, routed to a different business instance, or followed by a
  snapshot that the helper does not apply.

### Runtime Investigation Plan

- Add temporary focused diagnostics at the helper Load click, client command translation, Main
  Quick Waveform handler, business validation/load start/load completion, and authoritative
  snapshot construction boundaries.
- Build the affected Debug targets with the existing CMake Debug tree, launch the repo-root Debug
  runtime, reproduce the helper file selection, and inspect `bin/debug.log` plus process output.
- Use the first missing or rejected boundary as the root-cause location. Implement the smallest
  correction supported by the log evidence, then remove temporary diagnostic noise.

### Success Criteria

- [x] The log identifies whether the selected file path leaves the helper and whether Main accepts it.
- [x] A valid in-root WAV reaches the owning `QuickWaveformBusiness` exactly once.
- [x] Successful completion publishes the committed loaded path and updated waveform properties.
- [x] The helper applies the resulting snapshot and refreshes the loaded marker and left parameters.
- [x] Temporary diagnostic logs are removed or reduced to durable error-only logging after convergence.
- [x] Verification level: `debug-run` using the existing Debug build tree and repo-root runtime layout.

### Runtime Evidence And Root Cause

- The first instrumented run did not enter the helper. The second run proved that the helper
  executable started, authenticated, and became visible, while Main received only the ordinary
  `SOUR:MOD:CONF` request used to select the MOD business. No dedicated Quick Waveform load request
  left the helper.
- Making Load depend on `selectedWaveformPath()` exposed that a visible WAV row still resolved as
  invalid. The failure is deterministic on Windows: `QDir::cleanPath()` normalizes path separators
  to `/`, while both the helper and `QuickWaveformBusiness` appended `QDir::separator()` (`\\`) for
  their descendant-prefix test. A child WAV therefore failed `isWithinRoot()` even though the root
  directory itself passed the equality branch.
- Normalize both operands with `QDir::fromNativeSeparators()` and use `/` for the canonical prefix.
  Keep the helper's local list/navigation validation, and keep Business as the authoritative final
  validation boundary.

### Final Runtime Verification

- After the separator fix, one helper session sent five consecutive
  `SOUR:MOD:QWAV:LOAD` requests for five distinct WAV files. Main decoded every requested absolute
  path, the business accepted every canonical in-root path exactly once, and every load committed a
  non-empty path, sample rate, and sample count without an error response.
- Five consecutive requests also verify that the helper consumed the response snapshots: the remote
  editor host disables itself while a request is pending and is re-enabled only by `applyBusiness()`
  applying a received snapshot. The Quick Waveform editor's same `applySnapshot()` call updates the
  left values and loaded-file marker.
- All temporary `QWAV-TRACE` statements and helper-process log forwarding were removed after
  convergence. `Core`, `QuickWaveform`, and `SGStudioMiniBar` were rebuilt successfully in the
  existing Debug tree; focused staged and unstaged `git diff --check` both pass.

## Quick Waveform Post-investigation Cleanup (2026-07-14)

### Observation And Decision

- Runtime evidence identified the Windows descendant-path check as the actual failure: normalized
  paths used `/`, while the prefix appended the native `\\` separator.
- The dedicated `SOUR:MOD:QWAV:LOAD` command was introduced before that evidence was available.
  The existing revisioned `SOUR:MOD:CONF` action route already carries `select-file` plus its path,
  reaches the same authoritative `applyRemoteEditorAction()` implementation, and returns a fresh
  snapshot.
- Remove the dedicated command, DTO/codec, protocol-version bump, client special case, and service
  handler. Keep the corrected cross-platform path validation and the authoritative loaded-file
  state signal.
- Remove helper row-selection workarounds that do not affect `updateControlState()`, and remove one
  of the two identical initial Main-panel loaded-path restores.
- Keep the Quick Waveform-owned qrc, helper styling, row icons, and position action; those changes
  address the separately observed resource/UI parity gap.

### Success Criteria

- [x] Quick Waveform `select-file` uses the existing `SOUR:MOD:CONF` request with
  `expectedRevision`, action arguments, and the normal MOD synchronization semantics.
- [x] Protocol version returns to 1 and no `QuickWaveformLoadRequest` or
  `SOUR:MOD:QWAV:LOAD` reference remains.
- [x] Helper selection relies on `QTableWidget` current-row behavior; Load still resolves the
  selected WAV through the corrected `isWithinRoot()` check.
- [x] Main retains business-driven loaded-path synchronization without performing the same initial
  restore twice.
- [x] Resource ownership/CMake wiring and both Windows/Linux path normalization fixes remain intact.

### Verification Level

- `static`: inspect the final request route, search for removed protocol symbols, and run focused
  staged/unstaged diff checks. The user will perform the follow-up runtime test.

### Static Verification Performed

- Confirmed the helper host puts the current business revision into every editor action and the
  client now serializes `select-file` through `SOUR:MOD:CONF` without a special case.
- Confirmed no dedicated Quick Waveform command, DTO, codec, or service handler remains in source;
  the shared protocol is back to version 1.
- Confirmed both root-boundary checks retain separator normalization and the loaded-path signal still
  drives Main UI projection. Only the post-`setRootPath()` initial restore remains.
- Confirmed the Quick Waveform qrc/CMake wiring, relocated SVGs, helper styles, and position action
  remain staged.
- Focused staged, unstaged, and combined diff checks report no whitespace errors. Git reports only
  the repository's existing LF-to-CRLF conversion warnings. No build or runtime test was run; the
  user will perform the runtime verification.

## Qt / IPC Path-separator Pitfall Documentation (2026-07-14)

### Scope And Success Criteria

- Record the verified Windows failure in a reusable KnowledgeBase pitfall: `QDir::cleanPath()`
  produced `/`-separated comparison strings while `QDir::separator()` appended `\\`, so every
  descendant WAV failed the root-prefix test.
- Explain why the same expression happened to work on Linux and why that does not make it a valid
  cross-platform implementation.
- Define one normalization and containment pattern for UI-local checks and authoritative IPC
  receiver validation, including case sensitivity, separator boundaries, canonical paths,
  traversal, symlinks, and nonexistent files.
- Add an IPC debugging checklist that requires evidence at the sender, decoder, validation, business
  commit, and response-snapshot boundaries before adding a new command or synchronization path.
- Add the new document to `.github/KnowledgeBase/Index.md` under `坑位 (Pitfalls)`.
- Verification level: `static`; validate the Markdown link, referenced source paths, and focused
  diff formatting. No build or runtime execution is needed for documentation-only work.
