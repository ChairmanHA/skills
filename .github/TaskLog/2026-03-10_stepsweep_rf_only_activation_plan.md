# StepSweep RF-only Activation Plan

## Background

- Current business activation routing is centralized in `MainWindow::selectBusiness2Work()`.
- The existing routing treats all non-CW left-panel businesses as modulation businesses, so they require both `RF` and `Mod` to be enabled before activation.
- `StepSweepBusiness` currently mixes analog sweep and an obsolete digital/playback branch.

## Goal

- Remove the obsolete StepSweep digital branch from code.
- Introduce a minimal business-level activation prerequisite declaration.
- Keep `BusinessManager` responsible only for active-business lifecycle, without embedding StepSweep-specific rules.

## Design

### 1. Simplify StepSweep to analog-only

- Remove StepSweep digital UI/property binding code from `StepSweepPanel`.
- Remove digital profile/property handling from `StepSweepBusiness`.
- Keep the business focused on analog frequency/power sweep device configuration.

### 2. Add activation prerequisite to IBusiness

- Add a small enum on `IBusiness` to describe activation prerequisites.
- Default prerequisite remains `RequireRfAndMod` so existing businesses preserve current behavior.
- `StepSweepBusiness` overrides the prerequisite to `RequireRfOnly`.

### 3. Update activation routing only in MainWindow

- `MainWindow::selectBusiness2Work()` continues to be the single place that maps global UI state to the concrete active business.
- If `RF` is disabled, route to `Mute`.
- If a selected business exists and its prerequisite is satisfied, activate it.
- Otherwise preserve existing fallback semantics: `CW` when only `RF` is enabled, `Mute` when `RF` and `Mod` are enabled but no eligible business is selected. 

## Impact

- Minimal surface area: `IBusiness`, `MainWindow`, `StepSweepBusiness`, `StepSweepPanel`, and StepSweep plugin property registration.
- No changes to `BusinessManager` state model.
- No business-name special-casing in UI routing.

## Follow-up

- When StepSweep writes sweep parameters to hardware, read back the effective parameters from the same abstraction layer and only overwrite UI after both write and query succeed.
- Avoid mixing `DeviceOperator` and raw `IDevice *` in the same business flow unless the operator layer does not expose the required API yet.