# 2026-06-30 FixedPlayback Writeback Emit Simplification

## Scope

- Simplify `TxPipelineRuntime::applyFixedPlayback()` writeback emission.
- Keep the change limited to FixedPlayback runtime flow.
- Do not modify `PropertyBindingManager`.

## Evidence

- `applyFixedPlayback()` previously emitted `deviceConfigurationDone(writeback, {})` once immediately after base device configuration and once again after waveform upload plus `triggerStart()`.
- Both emissions are delivered to the same receivers, which call `CommonDeviceProfile::setProfile(profile)` and do not distinguish Phase 1 from Phase 2.
- When `provider.hasPlaybackPayload()` is true, the first success emit is immediately followed by a second success emit with the final playback fields, so it only adds duplicate UI/profile churn.
- When `provider.hasPlaybackPayload()` is false, Phase 2 will not run; keeping a Phase 1 writeback still reports the successful base configuration.

## Design

1. Keep Phase 1 writeback only in the `!provider.hasPlaybackPayload()` early-return branch.
2. For the normal ready-payload path, emit only once after waveform upload and `triggerStart()` succeed.
3. Preserve the current temporary multitone logical-center writeback override.

## Success Criteria

- FixedPlayback ready-payload success path emits `deviceConfigurationDone` once.
- FixedPlayback no-payload path still emits the base configuration writeback once before returning true.
- No shared property binding code changes are required.

## Verification Level

static

- Inspect the FixedPlayback flow.
- Run `git diff --check` on touched files.
