# Arb Playback Rematerialization Fix

## Scope

- Fix HTRA Playback failing to replace an AWGN waveform when a loaded WAV/IQS file is enabled again under RF sweep.
- Reuse the existing `TxSessionService -> TxPipelineRuntime` materialization request flow.
- Change only the Arb Playback provider and its file-payload generator.
- Keep `ProgrammedArb` on its current non-runtime path.

## Verification Level

- `static`

## Observations

1. Ordinary/IQS Arb file playback already builds a `Core::PlaybackPayload` and participates in the unified Playback runtime.
2. After a successful download, releasing host storage preserves the Arb payload descriptor identity and word count through shared `PlaybackPayload` storage semantics.
3. Switching from Arb Playback to AWGN replaces the waveform resident on the device, so switching back requires payload rematerialization if the host storage was released.
4. `TxSessionService::onPlaybackPayloadMaterializationRequired()` dispatches that request only through `qobject_cast<IPlaybackBusiness *>` and `IPlaybackBusiness::requestGenerateData()`.
5. `ArbModulationOnly` is currently a Playback provider but still inherits `Core::IBusiness`, so the rematerialization request is dropped before reaching the Arb file generator.
6. `ArbModulationOnly::buildPlaybackExecutionContext()` intentionally treats a released-but-nonempty payload as ready, so normal request building must not proactively reload it.

## Root Cause

HTRA Arb file Playback does not participate in the `IPlaybackBusiness` rematerialization callback contract. When runtime detects that the selected Playback payload has no host storage and is no longer device-resident, the rematerialization request is dropped at the type boundary instead of re-entering the existing Arb file payload generation path.

## Design

1. Make `ArbModulationOnly` derive from `Core::IPlaybackBusiness`, matching its existing Playback-provider role.
2. Add a narrow `requestGenerateData()` override that handles only released file-payload rematerialization for `OrdinaryWav` and `IqsWav`.
3. Reuse `ArbDataGenerator`'s existing asynchronous worker path by exposing a guarded rematerialization entry that re-queues payload generation only when:
   - a file playback mode is active;
   - a file path exists;
   - the current payload descriptor is non-empty but has no host storage;
   - the current payload is the ready descriptor (`status == 1`); and
   - no newer generation request is already pending.
4. Leave `buildPlaybackExecutionContext()` unchanged so runtime can still reuse a released descriptor when the same payload is already device-resident.
5. Do not change `ProgrammedArb` routing or capability semantics.

## Success Criteria

- After a WAV/IQS Arb file has been downloaded, switching to AWGN and enabling Playback again rematerializes the file payload without requiring an extra Off/On cycle.
- The rematerialized Arb payload receives a new identity and can replace AWGN in the existing sweep Playback pipeline.
- A payload that still has host storage is not regenerated.
- `ProgrammedArb` behavior remains unchanged.

## Static Verification Checklist

- [x] Confirm Arb source/header remain in the active HTRA CMake target.
- [x] Confirm `ArbModulationOnly` now implements `IPlaybackBusiness::requestGenerateData()`.
- [x] Confirm the rematerialization path only targets Ordinary/IQS file payloads and does not touch `ProgrammedArb`.
- [x] Confirm no additional proactive reload was added to normal execution-context building.
- [x] Run `git diff --check`.

## Result

- Changed `ArbModulationOnly` from a generic `IBusiness` Playback provider into an `IPlaybackBusiness`, so `TxSessionService` can dispatch released-payload rematerialization to it.
- Added `ArbModulationOnly::requestGenerateData()` as a narrow bridge to `ArbDataGenerator::rematerializePlaybackPayload()`.
- Added guarded Arb file rematerialization that only re-queues generation for ready Ordinary/IQS payload descriptors whose host storage has been released.
- Left `ProgrammedArb` and normal `buildPlaybackExecutionContext()` behavior unchanged, preserving device-resident payload reuse.
- Static validation passed: edited Arb files report no diagnostics, HTRA CMake still includes the touched sources, and `git diff --check` completed without reporting patch issues.