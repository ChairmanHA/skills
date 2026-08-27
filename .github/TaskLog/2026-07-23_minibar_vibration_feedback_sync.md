# Minibar Vibration Feedback Sync

## Scope

- Mirror Main's authoritative vibration-feedback enable state into the minibar helper snapshot.
- Publish a fresh snapshot when the Main-side setting changes.
- Trigger the same PGA HLC feedback command for helper touch input when enabled.
- Do not initialize a second `PGAHardwareSettings` instance in the helper.

## Observation

- Main installs `Utils::TouchEventFilter` globally.
- On Linux, `TouchBegin` delegates to `IHardwareSettings::triggerVibrationFeedback()` when enabled.
- PGA implements that trigger with detached `hlc --trigger-feedback`.
- The helper already installs `RemoteMiniBarWindow` as an application event filter, but currently has no vibration setting or touch-feedback branch.
- Constructing `PGAHardwareSettings` in the helper would also repeat unrelated startup side effects such as volume initialization.

## Design

1. Add `ui.vibrationFeedbackEnabled` to the existing version-1 minibar snapshot.
2. Read it from Main's current hardware settings; unsupported/non-PGA hardware resolves to false.
3. Emit a change signal from `TouchEventFilter::setVibrationFeedbackEnable()` and let `RemoteMinibarService` schedule a snapshot.
4. Cache the boolean in `RemoteMiniBarWindow`.
5. On Linux `TouchBegin`, start detached `hlc --trigger-feedback` when the cached setting is enabled. Do not consume or otherwise alter the touch event.

## Success Criteria

- Main vibration setting ON causes one helper HLC trigger per `TouchBegin`.
- Main vibration setting OFF causes no helper HLC trigger.
- Changing the setting schedules a fresh authoritative snapshot.
- Non-Linux builds do not invoke HLC.
- Missing snapshot fields default safely to disabled.
- Existing touch routing, overlay dismissal, mouse handling, and Main feedback behavior remain unchanged.

## Verification Level

- `static`

## Verification Checklist

- [x] Confirm all modified sources remain included by active CMake targets.
- [x] Confirm snapshot producer/consumer key symmetry.
- [x] Confirm the helper feedback path is Linux-only and does not consume `TouchBegin`.
- [x] Confirm no helper hardware-settings initialization was added.
- [x] Run `git diff --check` and review the focused diff.

## Result

- Added `ui.vibrationFeedbackEnabled` to the existing minibar snapshot.
- Main publishes the current supported hardware setting and schedules a snapshot when it changes.
- Helper defaults the setting to disabled and runs detached `hlc --trigger-feedback` for Linux `TouchBegin` only when enabled.
- The helper continues using its existing application event filter and does not initialize hardware settings.
- Verification remained static per repository policy; no build or runtime execution was performed.
