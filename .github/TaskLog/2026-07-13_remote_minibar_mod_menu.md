# RemoteMiniBarWindow MOD menu

## Scope

- Add a display-only MOD `QMenu` to `RemoteMiniBarWindow`.
- Populate it from the main process's currently visible business entries, using each business's `fullName()`.
- Keep the list synchronized after business registration/unregistration and entry visibility changes, including license-driven visibility updates after device open.
- Reuse the existing Wayland layer-shell transient geometry/ownership path so the menu opens beside the MOD button and dismisses on outside click.
- Do not implement business selection or panel presentation in this task.

## Assumptions and evidence

- Observation: the helper process intentionally does not load business plugins and treats main as the sole state owner (`minibar_cs_helper_scpi_architecture.md`).
- Observation: analog license validation updates entries through `BusinessManager::setEntryBusinessesVisible(...)`.

- Assumption: the MOD list should follow the same business acceptance boundary as the legacy `MiniBarBusinessMenuHost`: provider-backed entries only, excluding `Streaming` and `Playback`. Temperary `Streaming` and `Playback` have added to list, waiting for a new 
file dialog to be implemented in the future.

- Assumption: `fullName()` is already translated in the main process and is therefore serialized as display text; the helper must not translate it again.

## Success criteria

- The expanded remote minibar contains a MOD button.
- Clicking it opens a `QMenu` containing one item per currently visible accepted business, in registration order, with `fullName()` text.
- A license-driven `setEntryBusinessesVisible(...)` call schedules and publishes a fresh snapshot; the helper replaces the menu contents from that snapshot.
- On Wayland, the popup uses the existing layer-shell transient positioning path; on all platforms its rectangle is clamped to the active screen.
- Clicking outside the native menu dismisses it using normal `QMenu` behavior, without collapsing the minibar as an unrelated interaction.

## Verification

- Level: static.
- Confirm all modified sources are included by their existing CMake targets.
- Inspect signal flow, snapshot schema use, popup ownership, and geometry paths.
- No build or runtime verification unless explicitly requested.

## 2026-07-13 Win32 outside-dismiss follow-up

- Field observation: after an outside click dismisses the MOD `QMenu`, Win32 can deliver `ApplicationDeactivate` after `QMenu::aboutToHide`; at that point `isVisible()` is already false, so the minibar mistakes the same interaction for an independent outside click and collapses.
- Fix boundary: hold a one-event-loop MOD-menu dismissal guard from `aboutToHide`, and consult it in both activation and owned-interaction checks.
- Wayland boundary: keep the same platform-neutral guard; do not change the existing layer-shell transient geometry or native `QMenu` outside-dismiss behavior.
- Success criterion: one outside click closes only the MOD menu; a later independent outside interaction may still collapse the expanded minibar normally.

## 2026-07-13 MOD button state preparation

- Align the MOD entry button with the RF button's checkable dual-line state styling.
- Keep its temporary state authoritative and fixed at translated `OFF`/unchecked until MOD business state is added to the snapshot.
- Opening or closing the menu must not leave the checkable button visually selected.

## 2026-07-13 MOD snapshot identity and state

- Replace the display-only string array with entries containing the main-process-lifetime-stable `IBusiness::uuid()` as `id` and translated `fullName()` as `text`; the UUID can be resolved by `IBusiness::find()` in the next request phase.
- Keep the state model intentionally narrow: `selectedBusinessId` identifies the selected modulation provider, and `enabled` mirrors the authoritative `Mod` property.
- Menu checkmarks follow `selectedBusinessId`; the MOD button checked/ON state follows only `enabled`.
- Do not add a separate current/browsed business or panel-visible state in this phase.

## 2026-07-13 Wayland outside-dismiss follow-up

- Raspberry Pi observation: the layer-shell top-level `QMenu` is positioned correctly, but the compositor does not provide a desktop-wide popup grab; clicks outside the menu are therefore not delivered to `QMenu`.
- On active layer-shell Wayland only, show a transparent fullscreen overlay host immediately below the still-native `QMenu`.
- Mouse/touch input landing on the overlay closes the menu and is consumed; `aboutToHide` always hides the overlay so no transparent input-blocking surface remains.
- Win32 keeps the existing native `QMenu::popup()` path unchanged.

## 2026-07-13 MOD panel and writeback phase

### Scope

- Extend the remote MOD IPC model from list-only to selectable provider entries with `name`, translated text, enabled state, and current profile data.
- Add helper-side hosted MOD parameter panels under `src/app/minibarhelper/`, reusing the existing minibars' popup/overlay pattern and Controls widgets instead of loading plugin panels into the helper process.
- Do not use `SwitchButton::setCompactMode()` for the new MOD panels.
- Do not surface waveform save/record/show-waveform actions in the helper; load/clear actions for Playback/Streaming remain in scope because they are authoring inputs, not waveform-save outputs.
- On menu item click, request main-process provider selection and show the corresponding helper panel. Include Playback and Streaming in the selectable list.
- On helper parameter changes, send a profile+enabled request back to main. Main remains the only owner of business profile application, waveform generation, TxSession refresh, and device download.
- PM and Multitone phase fields may be displayed/edited as degrees in the helper panel, but the request profile must keep the provider's persisted radians representation.

### Assumptions and evidence

- Observation: `SGStudioMiniBar` does not link `Core` or `HTRA`, so helper cannot safely host plugin-owned `Core::Panel` instances directly.
- Observation: HTRA businesses expose a stable `IBusiness::getProfile()` / `setProfile()` boundary; analog playback businesses regenerate waveform data from `setProfile()`, and `providerExecutionContextChanged()` already drives TxSession refresh in the main UI paths.
- Observation: `BusinessManager` entry facade can update the main entry host selection, while `TxSessionService` is the shared apply owner.
- Assumption: helper-side panels should be narrow authoring mirrors for the current profile keys, not a second implementation of HTRA modulation algorithms.

### Success criteria

- The remote MOD menu still follows the main process visible-entry list and now carries profile data for each entry.
- Clicking AM/FM/PM/Pulse/Digital Ramp/AWGN/Multitone/Playback/Streaming opens a helper-hosted parameter panel.
- The helper panel has an `Enabled` switch, does not use compact switch mode, and contains no save/record/show-waveform controls.
- Editing helper parameters sends a request to main; main validates the visible business, applies profile/enabled/selection state, updates `TxSessionService`, and returns a fresh snapshot.
- For playback-style generated data, asynchronous generator completion can trigger another TxSession refresh through `providerExecutionContextChanged()`.

### Verification

- Level: static.
- Confirm modified/new sources are included by existing CMake targets.
- Inspect protocol encode/decode paths, request routing, selection synchronization, and TxSession refresh path.
- No build/run unless explicitly requested.

## 2026-07-13 hosted MOD panel inside-click follow-up

### Observation and cause

- Win32 field observation: after a MOD business panel opens, clicking anywhere inside the panel closes it and also collapses the remote minibar.
- Field verification: ignoring `ApplicationDeactivate` while the MOD panel session is active did not change the failure. That hypothesis is rejected and the guard must be removed.
- Code comparison: the working in-process minibar creates its business popup as a normal top-level `Qt::Tool` dialog on Win32. It reparents that dialog into a fullscreen overlay only when layer-shell Wayland is active.
- Current remote mismatch: `RemoteMiniBarWindow` always creates the MOD dialog as a child of a fullscreen, transparent, `WA_ShowWithoutActivating` overlay, including on Win32. The Win32 panel therefore does not follow the proven in-process window/activation/ownership path.

### Fix boundary and success criteria

- Match the in-process host split: use a top-level tool-style MOD dialog directly on Win32; only reparent it into the fullscreen overlay when layer-shell Wayland is active.
- Preserve the existing overlay outside-click owner on Wayland. On Win32, preserve application-deactivate and application-wide mouse filtering for outside dismissal.
- Keep outside clicks closing only the active panel. A later independent outside interaction may still collapse the expanded minibar.
- Clicking any control or blank area inside the panel must leave both the panel and the expanded minibar visible on Win32 and Wayland.
- Do not change or extend the soft-keyboard path in this follow-up.

### Verification

- Level: static.
- Inspect the activation and pointer-event ordering and confirm the overlay remains the outside-dismiss owner.
- No build or runtime verification unless explicitly requested.

### Implementation result

- Removed the ineffective active-MOD `ApplicationDeactivate` bypass and restored the existing Win32 outside-deactivate dismissal path.
- `ensureModPanelHost()` now creates the MOD dialog with the same top-level `Qt::Tool` flags as the in-process business popup.
- The fullscreen `OverlayContainer` is now created only for active layer-shell Wayland; immediately before display, the MOD dialog is reparented into that overlay as a child widget.
- Win32 positions and shows the top-level dialog directly in screen coordinates. Wayland retains the fullscreen overlay, screen-local child geometry, and outside-input handler.

## 2026-07-13 hosted MOD panel UI parity follow-up

### Scope

- Match the hosted MOD panel container width and control height to `RemoteSweepPanel`; retain the same two-column layout and margins.
- Keep every hosted MOD `SwitchButton` in its default non-compact mode. Do not introduce `setCompactMode(true)`.
- Reuse the original business-panel source strings inside `tr()` wherever the same field already exists, so the helper's exact-string spreadsheet translator can resolve them.
- Disable step editing in every minibar-helper numeric soft keyboard, including center/level, sweep, and MOD fields.
- Align hosted Sweep/MOD panel `LabelButton` titles left and values right, matching `RemoteSweepPanel`.

### Success criteria

- The MOD popup uses the same 560-pixel target width and 60-pixel control height as the remote sweep popup, subject to screen clamping.
- No helper-side `SwitchButton` enables compact mode.
- Existing translated field names such as `Phase Deviation(deg)`, `Tone Count`, `File Name`, and `I/Q Scale(%)` use the same source text as their original panels.
- All helper numeric keyboard configurations set `stepEditable` to `false`.
- Hosted Sweep/MOD panel `LabelButton` instances do not retain centered title/value alignment.

### Verification

- Level: static.
- Inspect the helper source for popup width, field height, translation calls, alignment setters, compact-mode calls, and all `stepEditable` assignments.
- Do not build or run unless explicitly requested.

### Implementation result

- MOD popup target width now reuses the sweep popup's 560-pixel width; hosted MOD controls now use the sweep panel's 60-pixel height, 5-pixel spacing, and matching margins.
- Hosted Sweep/MOD panel `LabelButton` instances use left-aligned titles and right-aligned values.
- Main, sweep, and MOD numeric keyboards explicitly set `stepEditable` to `false`; the obsolete sweep step-editability API was removed.
- MOD labels and file actions now reuse the original panel translation source strings, including PM, Multitone, Playback, and Streaming fields.
- Removed the helper-only Multitone `FixedPhaseOffset` and `EvenCountCenterToneMask` controls because the original Multitone panel does not expose them; their profile values remain preserved during whole-profile writeback.
- Static scans found no helper-side compact-mode calls or editable step configurations; hosted Sweep/MOD panel LabelButtons use the requested left/right alignment.

## 2026-07-13 minibar alignment correction and Digital panel mapping

### Observation and scope

- User field feedback confirms that the minibar's own RF/Frequency/Level/Sweep/MOD information buttons must retain their original centered title/value layout; only hosted business panels use the Sweep-style left/right layout.
- `Digital Modulation` currently falls through `RemoteModPanel`'s generic profile-key renderer. Consequently, keys such as `SymbolRate`, `FilterType`, and `SequenceSeed` appear directly instead of the translated labels used by `digitalpanel.ui`.
- Add an explicit `Digital Mod` field specification using the original DigitalPanel titles and enum display names. Do not add save-waveform, show-waveform, or trim-download actions.

### Success criteria

- Minibar information buttons restore centered title and value alignment.
- The hosted Digital panel exposes the same authoring fields as `DigitalPanel`: Symbol Rate, Filter Type, Modulation Type, Filter Alpha, PN, Filter Length, Sequence Seed, Oversample, and FSK Deviation.
- Every Digital title and textual enum value is passed through `tr()` using an exact source key already present in `configuration/Language.xlsx`.
- Digital edits continue to submit the complete profile, preserving fields not represented by a visible helper control.

### Verification

- Level: static.
- Compare helper Digital keys against `DigitalProfile`, titles against `digitalpanel.ui`, and enum stored values against `DigitalModulator::saveSettings()`.
- Confirm the minibar's own `createInfoButton()` alignment is centered while hosted Sweep/MOD LabelButtons remain left/right.
- Do not build or run unless explicitly requested.

### Implementation result

- Restored centered title/value alignment in `RemoteMiniBarWindow::createInfoButton()`; hosted Sweep and MOD panel LabelButtons retain their left/right alignment.
- Added an explicit `Digital Mod` specification covering all nine `DigitalProfile` fields in the same two-column order as `digitalpanel.ui`.
- Digital filter, modulation, and oversample options display translated original labels while writing the exact enum keys/numeric values expected by `DigitalModulator::restoreSettings()`.
- Read-only inspection of `Language.xlsx` confirmed all nine Digital field-title source keys have Chinese mappings, so the workbook required no modification.
- Static diff validation passed; no build or runtime verification was performed.

## 2026-07-13 MOD numeric editor checked-state cleanup

### Observation and scope

- Observation: editable numeric `LabelButton` instances in `RemoteModPanel` are checkable, so clicking one automatically applies its checked editing style.
- Observation: `openModNumericKeyboard()` clears keyboard ownership on `finished` and `destroyed`, but does not clear the originating button's checked state; outside-click rejection therefore leaves the editing style behind.
- Assumption: "any LabelButton" refers to the hosted MOD panel's editable numeric fields; minibar Frequency/Level buttons are not checkable, and hosted Sweep fields already have an explicit editor-active reset path.
- Add a panel-owned numeric editor-active update matching `RemoteSweepPanel`, and call it for keyboard open plus both close paths.
- Do not change QSS or business/profile state.

### Success criteria

- Opening a MOD numeric keyboard checks only its originating field button.
- Accepting, rejecting by outside click, or destroying the keyboard restores the field button to unchecked/original styling.
- Programmatic checked-state changes continue through `LabelButton::toggled`, so both child labels refresh their `parentChecked` styling.

### Verification

- Level: static.
- Confirm the modified helper sources remain included by `src/app/minibarhelper/CMakeLists.txt`.
- Inspect open/finish/destroy callback symmetry and the `LabelButton` child-style refresh chain.
- Do not build or run unless explicitly requested.

### Implementation result

- Added `RemoteModPanel::setNumericEditorActive()` to keep editable numeric field selection exclusive and to restore all numeric buttons to unchecked when editing ends.
- `openModNumericKeyboard()` now marks the requested field active on open and clears it from both `finished` and `destroyed` cleanup paths.
- Static verification confirmed the sources are in the helper CMake target, `LabelButton::toggled` refreshes both child-label `parentChecked` properties, and `git diff --check` reports no patch errors.
- No build or runtime verification was performed.
