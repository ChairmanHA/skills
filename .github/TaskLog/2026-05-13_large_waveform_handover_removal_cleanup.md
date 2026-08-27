# Large Waveform Handover Removal Cleanup

## Goal

- Remove the obsolete Generate & Stream / handover implementation now that the new large-waveform flow has converged to trim-only behavior.
- Keep current Digital large-waveform behavior unchanged.
- Update KnowledgeBase and repo memory so documentation no longer claims the common Analog layer still supports handover.

## Local Hypothesis

- The remaining handover path is dead code: only `AnalogPlaybackBusiness` itself and one stale `DigitalModulation::onModulatorStatusChanged()` branch still reference it.
- No included modulation business still calls `requestGenerateAndStream()` or uses the optional stream button in `showLargeWaveformPrompt()`.
- Therefore the safe root fix is to delete the handover-specific API/state/storage and keep only the trim request path.

## Cheap Disconfirming Check

- Search for `requestGenerateAndStream|hasPendingHandover|onHandoverDataReady|handoverDataRequested|saveToTempWavFile|executeHandoverToStreaming|HandoverSnapshot` under `src/**`.
- If matches exist outside `analogplaybackbusiness.*` and the stale Digital branch, stop and narrow the owning behavior before deleting.

## Planned Changes

1. Refactor `AnalogPlaybackBusiness` from mixed handover/trim state to trim-only request state.
2. Remove the stale handover branch and type naming from `DigitalModulation`.
3. Update large-waveform KnowledgeBase text so it describes the current trim-only reality instead of the removed Generate & Stream design.
4. Update any nearby docs that still describe `cancelPendingHandover()` as handling handover.
5. Record the cleanup outcome in repo memory.

## Validation

- Run targeted searches after the edit to confirm there are no remaining source references to the removed handover API.
- Run file diagnostics on the touched source files.
- Do not build; user will compile and test locally.
