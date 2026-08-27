# Auto Mod On Preference Plan

## Background

- Current pipeline selection remains snapshot-driven in Core: `MainWindow::updateOrchestrator()` collects `RF`, `Mod`, carrier plan, and selected business provider info, then `TxOrchestrator` resolves the pipeline.
- Settings restore does not persist or replay a pipeline directly. It restores UI/business/common authoring state under suspended updates, then runs one `selectBusiness2Work()` pass to rebuild the snapshot and re-resolve the pipeline.
- Therefore the new behavior should be treated as a UI interaction preference, not as execution state, request state, or business profile state.

## Verified Scope

- `providerKind() != None` is not strictly equivalent to "traditional modulation".
- Today it includes:
  - all `IPlaybackBusiness` descendants in the Analog plugin
  - `StreamingBussiness` in the HTRA plugin
  - `ArbModulationOnly` in HTRA code, although it is currently not registered in `plugin.cpp`
- All of the above live on the right-side business list when registered, but `Streaming` is semantically a business mode rather than a classic modulation page.

## Requirement Interpretation

- Add a new preference entry in the Preference dialog.
- Default value is On.
- When the preference is On, user-initiated enabling of a business that requires baseband should automatically turn `Mod` on.
- Manual user shutdown of `Mod` must remain authoritative and must not be auto-reverted.
- Programmatic state restore, preset reset, startup restore, and cached pipeline reapply must not be reinterpreted as user intent.

## Recommended Scope Rule

- Use `providerKind() != BasebandProviderKind::None` as the initial scope rule.
- This is the smallest and most architecture-aligned rule because it reuses the existing Core provider classification instead of introducing a new business taxonomy.
- Consequence: the preference will also apply to `Streaming`, and to `Arb` if that business is re-enabled later.

## If Product Semantics Must Exclude Streaming

- If the real product meaning is strictly "traditional modulation pages only", then `providerKind() != None` is too broad.
- In that case, add a small business-level policy API on `IBusiness`, for example a boolean or enum describing whether the business participates in the Auto Mod On preference.
- Default implementation can still derive from `providerKind() != None`, while `StreamingBussiness` can explicitly opt out.
- This adds a small amount of surface area but keeps the scope decision explicit and future-proof.

## Recommended Design

### 1. Persist the preference in global UI settings

- Store the value in `ExtensionSystem::PluginManager::globalSettings()`.
- Keep it under the same UI-preference layer as theme, startup mode, and other non-profile UI settings.
- Recommended key shape: `APP/AutoEnableModOnBusinessEnable` or another `APP/`-scoped name consistent with existing Core usage.
- Default to `true` when the key is absent.

Why:

- This behavior is not part of waveform authoring, common device profile, or execution request semantics.
- It should not enter `Profile.json`, `TxPipelineSnapshot`, `TxExecutionContext`, or business profile payloads.

### 2. Extend Preference dialog only as UI surface

- Add one new toggle entry to `PreferenceDialog` with text such as `Auto Mod On`.
- Extend the dialog update API so MainWindow can pass the current stored value when opening or refreshing the dialog.
- Add one new signal from the dialog for preference changes.
- MainWindow remains the owner of reading and writing the actual persisted setting.

Why:

- This matches the current preference pattern: dialog is only a view and signal source; MainWindow applies side effects and persistence.

### 3. Add a user-only business-enable signal in FancyTabWidget path

- Do not hook the feature to `currentSelectedBusinessChanged`.
- That signal is too broad because it is also used by restore flows and other programmatic selection updates.
- Instead, add a new small signal in the FancyTabWidget / TabWidget path that only fires when a panel emits `enabledChanged(true)`.
- This signal should carry the business pointer and represent "the panel was actively enabled from the panel interaction path".

Why this is the right layer:

- The existing selection chain is already: panel `enabledChanged` -> `TabWidget::setSelected` -> `FancyTabWidget` selected business -> `MainWindow::selectBusiness2Work()`.
- The new preference is about augmenting user interaction before the snapshot is resolved, so the right place is this UI routing edge, not orchestrator/runtime.

Why this is reliably user-oriented:

- `SwitchButton::setStatus()` changes UI state silently.
- `SwitchButton::onButtonClicked()` is what emits `statusChanged()`.
- Most programmatic `setBtnEnabledChecked()` calls therefore do not emit `enabledChanged()`.
- This naturally separates restore-time state replay from true panel interaction.

### 4. Let MainWindow turn on Mod through the existing property path

- MainWindow listens to the new user-enable business signal.
- On receipt, MainWindow checks:
  - preference is enabled
  - business is non-null
  - business provider kind is not `None`
  - current `Mod` property is Off
- If all are true, MainWindow updates the `Mod` property through the same property edit path semantics used by the Common panel.
- After that, the existing routing continues unchanged:
  - selected business stays selected
  - `selectBusiness2Work()` runs
  - `updateOrchestrator()` rebuilds snapshot
  - pipeline resolves normally

Important:

- Do not call `applyResolvedPipeline()` directly from the preference handler.
- Do not call device APIs.
- Do not mutate `TxPipelineSnapshot` or `TxApplyRequest`.

### 5. Keep manual Mod Off authoritative

- The feature should only run on the new user-enable business signal.
- It should not be triggered from snapshot rebuilds, provider updates, settings restore, or pipeline reapply.
- Therefore if the user later manually turns `Mod` Off, nothing should automatically turn it back On until the user explicitly enables a business again.

### 6. Keep settings save/load semantics unchanged

- `Profile.json` save/load remains exactly as it is.
- Existing fields under `common`, `bussiness`, `sweep`, and selected/current widget business stay untouched.
- The new preference lives only in global settings, not in per-profile snapshots.

Why:

- Loading a profile should restore the authored state, not inject a fresh UI interaction side effect.

## Minimal File Impact

- Core only:
  - `src/plugins/core/preferencedialog.h`
  - `src/plugins/core/preferencedialog.cpp`
  - `src/plugins/core/preferencedialog.ui`
  - `src/plugins/core/fancytabwidget.h`
  - `src/plugins/core/fancytabwidget.cpp`
  - `src/plugins/core/mainwindow.cpp`
  - optionally `src/plugins/core/mainwindow.h` if a small helper is added there
- No changes required in `TxOrchestrator`, `TxPipelineState`, `TxPipelineRuntime`, `CommonDeviceProfile`, or business plugin implementations for the recommended scope rule.

## Edge Cases

### Generate And Trim path in Analog playback

- Analog code currently contains a path that programmatically sets the panel enabled and then explicitly emits `enabledChanged(true)` after data becomes ready.
- This path originates from a user action, so applying Auto Mod On there is usually acceptable.
- If product later decides that only direct switch clicks should count, this one path will need an explicit suppression flag or a more precise signal taxonomy.

### Hidden Arb business

- `ArbModulationOnly` currently has `providerKind() == Playback`, but the registration line is commented out.
- Under the recommended scope rule, if Arb is re-enabled later it will automatically participate in the preference.
- This is likely desirable if the real semantic is "any baseband business".

### Streaming business

- `StreamingBussiness` currently has `providerKind() == Streaming` and lives in the same right-side business selection model.
- Under the recommended scope rule it will also auto-enable `Mod`.
- If that is not desired, the scope rule must be refined from provider-based to an explicit business-level policy.

## Validation Matrix

1. Preference default

- Fresh settings should show the new preference as On.

2. Manual enable with preference On

- Start from `RF=On`, `Mod=Off`.
- Enable AM, FM, Pulse, Digital, DSSS, OFDM, Ramp.
- Result: `Mod` becomes On and pipeline resolves to the corresponding provider path.

3. Manual shutdown of Mod

- With a provider business already enabled, manually turn `Mod` Off from the Common panel.
- Result: no automatic reopen happens.

4. Re-enable after manual shutdown

- With preference still On and `Mod=Off`, explicitly enable a provider business again.
- Result: `Mod` turns On again.

5. Preference Off

- Start from `RF=On`, `Mod=Off`.
- Enable a provider business.
- Result: `Mod` remains Off and existing fallback behavior is preserved.

6. Settings restore

- Load a saved profile with an active business and varying `Mod` states.
- Result: restore should replay the saved state only; it must not re-interpret restore as a fresh user enable action.

7. Preset/reset flow

- Run preset/reset and confirm no unexpected `Mod` auto-enable happens during programmatic UI replay.

8. Streaming scope check

- If keeping provider-based scope, explicitly verify Streaming page behavior matches product expectation.

## Recommendation Summary

- Treat the feature strictly as a Core UI preference.
- Keep the existing snapshot and pipeline architecture unchanged.
- Trigger only from a new user-only business-enable signal in the FancyTabWidget path.
- Use `providerKind() != None` as the smallest initial scope rule.
- If product semantics later prove narrower than provider-based scope, add a small explicit business policy API instead of pushing special cases into MainWindow.