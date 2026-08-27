# Quick Waveform Playback Rematerialization Fix

## Scope

- Fix Quick Waveform failing to replace an AWGN waveform when it is enabled again under RF sweep.
- Keep the existing `TxSessionService -> TxPipelineRuntime` provider-selection and device-resident payload reuse model.
- Change only the Quick Waveform provider's missing rematerialization behavior; do not alter sweep, AWGN, or runtime arbitration.

## Verification Level

- `static`

## Observations

1. A successful Playback download releases the shared host-side IQ storage while preserving the payload identity and word count.
2. Switching from Quick Waveform to AWGN replaces the waveform resident on the device.
3. Switching back to Quick Waveform therefore makes the runtime request payload rematerialization because the old Quick Waveform descriptor has no storage and is no longer device-resident.
4. `TxSessionService` dispatches that request through the virtual `IPlaybackBusiness::requestGenerateData()` API.
5. AM/FM/AWGN and other generated Playback providers override that API, but `QuickWaveformBusiness` currently declares a same-named Qt signal with no receiver and has no implementation.
6. `PlaybackPayload::isEmpty()` checks the retained word count, not host storage presence, so the released Quick Waveform descriptor does not trigger the separate empty-payload reload path in `buildPlaybackExecutionContext()`.

## Root Cause

The Quick Waveform provider never rematerializes its file payload after runtime storage release. The runtime correctly detects that the device now contains AWGN and asks the selected Quick Waveform provider to recreate its payload, but virtual dispatch reaches an unhandled Qt signal instead of file loading.

## Design

1. Replace the obsolete Quick Waveform `requestGenerateData` signal declaration with an override of `IPlaybackBusiness::requestGenerateData()`.
2. When called, reload the committed file only if:
   - a file path is available;
   - no load/reload is already pending; and
   - the current payload no longer has host storage.
3. Reuse the existing asynchronous `onFileSelected()` loading path so capability validation, IQS-WAV parsing, power metrics, AutoScale policy, and provider-context publication remain unchanged.
4. Do not proactively reload merely because a descriptor lacks storage during normal request building; the runtime must remain free to reuse the same device-resident payload without allocating the large host buffer again.

## Success Criteria

- After Quick Waveform has been downloaded, switching to AWGN and enabling Quick Waveform again starts file rematerialization without an extra Off/On cycle.
- The rematerialized payload receives a new identity, is published through `providerExecutionContextChanged()`, and can replace AWGN in the existing sweep Playback pipeline.
- Repeated rematerialization requests do not start duplicate loads.
- A payload that still has host storage is not reloaded.
- Existing IQS-WAV parsing/scaling and device-resident reuse behavior are unchanged.

## Static Verification Checklist

- [x] Confirm Quick Waveform source remains included by its active CMake target.
- [x] Confirm `QuickWaveformBusiness::requestGenerateData()` overrides the base virtual API and is no longer a signal.
- [x] Confirm the implementation reuses `onFileSelected()` and guards storage/load state.
- [x] Search for duplicate or unhandled Quick Waveform `requestGenerateData` declarations.
- [x] Inspect the focused diff and run `git diff --check`.

## Result

- Converted the obsolete Quick Waveform `requestGenerateData` signal into the `IPlaybackBusiness` virtual override consumed by `TxSessionService`.
- Added guarded rematerialization through the existing asynchronous file loader when the committed payload descriptor has lost its host storage.
- Kept a storage-present payload and an already pending load unchanged, preventing unnecessary allocation and duplicate reloads.
- Confirmed the modified source/header remain in the QuickWaveform CMake target and no second Quick Waveform declaration/definition exists.
- `git diff --check` passes; only the repository's existing LF-to-CRLF checkout warnings are reported.
- Verification remained static per repository policy; no build or runtime execution was performed.
