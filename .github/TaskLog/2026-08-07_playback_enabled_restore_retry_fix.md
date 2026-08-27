# Playback Enabled Restore Retry Fix

## Scope

- Fix startup restore of enabled state for Arb Playback after its file/profile is restored asynchronously or capability-late.
- Cover the same delayed-readiness pattern for Quick Waveform without changing runtime profile schema.
- Keep the save/load file format and the existing `loadRuntimeProfileFile()` order unchanged.

## Verification Level

- `debug-build`

## Observations

1. Runtime profile loading restores business profiles before it restores `activedBussiness`, and provider-local restore may still complete later.
2. `FancyTabWidget::setBusinessSelected()` only delegates to `TabWidget::setSelected(true)` once.
3. `TabWidget::setSelected(true)` currently trusts the panel's immediate `isBtnEnabledChecked()` result as final.
4. `ArbPanel::setBtnEnabledChecked(true)` refuses to check the switch when no file is currently loaded, via `checked && hasLoadedFile()`.
5. After the recent Arb startup fix, a capability-late restore can make the first startup selection request arrive before the Arb file has been replayed, so that first check attempt becomes a no-op.
6. Once the file/profile becomes available later, the provider emits `providerExecutionContextChanged()`, but the UI host never retries the saved selection intent.
7. AM/FM/AWGN do not show the bug because their restore path materializes a ready provider state synchronously enough that the first `setBusinessSelected(true)` succeeds.

## Root Cause

The enabled-state restore bug is a host-side missed retry. Startup restore records the intent that Arb Playback should be enabled, but `TabWidget` discards that intent if the panel cannot accept selection in that exact moment. Delayed-readiness file providers later become selectable, yet no second selection attempt is issued.

## Design

1. Teach `TabWidget` to remember the last non-user selection intent from the host.
2. When a business emits `providerExecutionContextChanged()`, retry applying a pending selected state if the panel was previously unable to accept it.
3. Keep the retry narrow:
   - only for pending selected state;
   - stop retrying once the panel really becomes checked; and
   - do not change persistence format or provider semantics.
4. Let this generic UI-host retry cover both Arb and Quick Waveform delayed-readiness restores.

## Success Criteria

- Startup restore re-checks Arb Playback automatically once its restored file/profile becomes selectable.
- The recovered selection updates both the panel switch state and FancyTabWidget's selected business state.
- Quick Waveform follows the same delayed-readiness retry path if it ever misses the first restore-time selection.
- AM/FM/AWGN behavior remains unchanged.

## Verification Checklist

- [x] Confirm retry logic lives in the UI host layer rather than the runtime-profile format.
- [x] Confirm pending selection is retried from `providerExecutionContextChanged()`.
- [x] Confirm the touched core UI files still build in the existing Debug tree.
- [x] Run `git diff --check`.

## Result

- Root cause is a missed host-side retry, not a missing saved enabled flag. Startup restore already persists `activedBussiness`, but `TabWidget::setSelected(true)` previously treated a provider's immediate panel state as final even when the panel was only temporarily unable to accept selection.
- Arb exposes the bug because its restored file/provider readiness can arrive after the first startup `setBusinessSelected(true)` call; AM/FM/AWGN do not because their restore path is synchronously ready enough that the first selection succeeds.
- Added a generic pending-selection retry in the `FancyTabWidget` host layer so delayed-readiness providers can accept the saved enabled state once they emit `providerExecutionContextChanged()`.
- This same retry path also covers Quick Waveform if its post-restore file/payload readiness arrives after the first selection attempt.
- Validation passed: focused diagnostics report no new errors, the existing Debug build tree successfully linked `Core`, and `git diff --check` completed without reporting patch issues.