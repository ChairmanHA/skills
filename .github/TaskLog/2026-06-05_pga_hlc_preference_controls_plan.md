# 2026-06-05 PGA HLC Preference Controls Plan

## Scope
- Platform: aarch64 Linux / PGA
- UI entry: PreferenceDialog
- New HLC-backed controls:
  - Brightness button
  - Auto brightness dimming switch
  - Vibration feedback switch
  - Fan mode switch (`0: Quiet`, `1: standard`)
- Keep existing PreferenceDialog entries aligned into two rows, three buttons per row.

## Local Evidence
- Current CPU temperature and battery polling already use `hlc` in `PgaRuntimeStatusService`.
- Brightness and vibration still go through legacy PGA hardware settings code:
  - brightness writes `/sys/class/backlight/backlight/brightness`
  - vibration only toggles a stored flag on `TouchEventFilter`
- `TouchEventFilter` is still globally installed and also owns lock-screen/touch-forwarding behavior, so only the vibration part should be modernized, not the whole filter.
- `PreferenceDialog` currently mixes four buttons in one row plus a separate brightness button, and MainWindow wires each behavior ad hoc.

## Falsifiable Hypothesis
- The correct ownership boundary is `IHardwareSettings` / `PGAHardwareSettings`: if HLC brightness, vibration trigger policy, and fan mode are implemented there, Core UI code can stay hardware-agnostic and only forward preference signals.
- Cheap disconfirming check: after extending `IHardwareSettings`, `PreferenceDialog` and `MainWindow` should only need signal/slot and state refresh changes; if they still require direct PGA-specific command code, the boundary choice is wrong.

## Implementation Plan
1. Extend `IHardwareSettings` with fan-mode capability and a one-shot vibration trigger API.
2. Rework `PGAHardwareSettings` to use `hlc` for brightness query/set, fan mode query/set, and persisted vibration enable state.
3. Simplify `TouchEventFilter` so touch begin triggers HLC haptic feedback through hardware settings when enabled, while preserving existing lock-screen and graph-forwarding behavior.
4. Reshape `PreferenceDialog` into a 2x3 grid and add a fan-mode button while keeping existing screen-lock and auto-mod controls.
5. Update `MainWindow` preference wiring and refresh paths to consume the unified hardware-settings state.
6. Run a focused Debug build for touched targets/files to catch interface and moc/ui regressions.

## Validation
- Preferred: focused Debug build on the touched plugin/library slice if practical.
- Fallback: repo Debug build when target-level build selection is not readily available in the current environment.

## Follow-up Delta
- Brightness entry should remain a plain `QPushButton`; brightness value belongs only in `BrightnessSettingDialog`, not in the preference button subtitle.
- `TouchEventFilter` no longer needs the old Windows two-finger graph forwarding path; keep only lock-screen interception plus Linux PGA haptic trigger.
- Platform intent should be explicit:
  - Win/Linux still install the global event filter for lock-screen behavior.
  - Linux-only sections should gate HLC-backed haptic triggering and other direct `hlc` command helpers.
- Preference fan mode should use `EnumTextButton` with `Quiet` / `Standard` options instead of a binary toggle button.
- `hlc --set-fan-mode` must be executed synchronously for PGA, with merged stdout/stderr parsing. If the output reports a forced fallback such as `mode : 1`, the cached state and UI writeback must follow the reported mode instead of the requested mode.