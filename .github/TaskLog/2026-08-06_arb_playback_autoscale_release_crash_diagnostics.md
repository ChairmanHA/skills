# ARB Playback AutoScale Release Crash Diagnostics

## Scope

- Analyze the Release-only crash seen when ARB Playback is enabled and AutoScale is toggled repeatedly.
- Treat the asynchronous TX apply path and the shared `PlaybackPayload` storage optimization as the primary investigation boundary.
- Add focused Release-visible diagnostics only; do not change apply, payload-release, or SDK-call behavior in this task.

## Verification Level

- `static`
- The user will reproduce the crash with a Release build and return `bin/debug.log` for the next investigation step.

## Observations

1. `ArbPanel` publishes an `Arb_AutoScale` edit, and `ArbModulationOnly` forwards it to `ArbDataGenerator::setAutoScale()`.
2. An accepted AutoScale change calls `ArbDataGenerator::requestGenerateData()`.
3. `requestGenerateData()` calls `m_playbackPayload.releaseStorage()` before waking the waveform-generation worker.
4. `PlaybackPayload` copies share one `PlaybackPayloadStorage`. Its public contract explicitly says `releaseStorage()` releases the backing storage for every copy.
5. The asynchronous coordinator copies the payload through the desired request, in-flight job, queued metatype argument, and device-thread executor request; those copies therefore refer to the same storage and payload identity.
6. `TxPipelineExecutor::uploadPlaybackWaveform()` currently calls `constData()`, then passes that bare pointer to `IDevice::downloadDataSequence()` without holding `PlaybackPayloadStorage::m_mutex` for the duration of the SDK call.
7. `PlaybackPayloadStorage::release()` can consequently set `m_data` to null and free the owned `QVector<int16_t>` on the GUI thread while the device I/O thread is still reading the previously returned pointer.
8. Before the asynchronous executor was introduced, device configuration blocked the GUI thread. Repeated clicks accumulated as UI events but could not concurrently free the active SDK input buffer.

## Primary Inference

The highest-confidence failure mechanism is a use-after-free across the GUI and device I/O threads:

```text
device I/O thread                         GUI thread
-----------------                         ----------
payload = sharedPayload.constData()
downloadDataSequence(payload, ...)
                                          AutoScale edit
                                          requestGenerateData()
                                          sharedPayload.releaseStorage()
                                          free(payload)
SDK continues reading payload -> crash
```

Release-only behavior is consistent with a timing-sensitive lifetime bug: optimized execution and shorter UI/device scheduling gaps make the overlap more likely, while Debug timing masks it.

## Alternative Hypotheses Kept Visible

- The vendor SDK could retain the supplied pointer after `downloadDataSequence()` returns despite the current synchronous-call contract. A crash after the logged SDK return would support this alternative.
- A stale queued request or completion could be accepted out of order. Existing epoch/generation logs should distinguish this; the coordinator already filters stale completions.
- The ARB generation worker could publish stale data. Its current `m_profileChanged`/capability-generation checks make this less likely, but request/build/drop/publish diagnostics will verify it.

## Diagnostic Design

Add one correlated log vocabulary:

- `[ArbAutoScale]`: accepted AutoScale intent, thread, old/new value, current payload identity and address.
- `[ArbGenerator]`: generation revision requested, build started, stale result dropped, and payload published.
- `[PlaybackPayloadLifetime]`: shared-storage release begin/end, storage address, data address, payload identity, word count, reason, and thread.
- `[TxPipelineExecutor]`: SDK download begin/end with the exact data pointer, payload identity, word count, and thread.
- Existing `[TxApplyQueue]` logs provide epoch/generation/payload identity correlation.

Do not log IQ contents, file contents, or per-sample data.

## Success Criteria

1. Every accepted AutoScale toggle can be correlated to a generator revision.
2. Every generated payload can be correlated by `payloadId` to a `[TxApplyQueue] dispatch` line.
3. The log identifies the exact pointer passed to `downloadDataSequence()`.
4. If storage is released during that call, the same `payloadId` and data address appear between `download begin` and `download end`; if the process crashes first, `download begin` followed by a cross-thread `release begin/end` is the decisive last sequence.
5. If the SDK returns before the crash, the `download end` line distinguishes a possible vendor-SDK retained-pointer contract violation from the in-call use-after-free.

## Static Verification Checklist

- [x] Re-read each modified source file after editing.
- [x] Confirm all new formatting arguments and integer/pointer casts match the format string.
- [x] Confirm diagnostics do not dereference a pointer after storage release.
- [x] Confirm no synchronization, queueing, payload ownership, or device behavior changes.
- [x] Inspect focused diff and whitespace errors.

## Implementation Result

- Added an ARB generation revision used only for request/build/drop/publish correlation.
- Added shared-storage release begin/end diagnostics with one lifetime event number and an explicit caller reason.
- Added exact SDK download begin/end pointer diagnostics.
- Extended desired/pending/dispatch queue logs with payload identity, storage state, pointer, replaced payload identity, and in-flight payload identity.
- Confirmed all modified sources are included by the active Core and HTRA `CMakeLists.txt` files.
- `git diff --check` passes; only the repository's existing LF-to-CRLF checkout warnings are reported.
- No build or runtime verification was performed, per the requested Release reproduction handoff and repository static-analysis default.

## Release Reproduction Evidence

The user reproduced the crash with the diagnostic Release build. The final log establishes the cross-thread use-after-free directly:

```text
15:07:16.143 device thread 0x5e50:
  download begin payloadId=38 data=0x248704ad058 hasStorage=yes

15:07:16.143 GUI thread 0x8604:
  AutoScale accepted for payloadId=38 data=0x248704ad058
  release begin/end payloadId=38 data=0x248704ad058 -> null

no matching download end; process terminates
```

This rules out stale completion writeback as the crash origin. The current request queue correctly marks superseded generations stale, but queue serialization cannot protect a buffer whose shared descriptor allows another thread to destroy the storage during the SDK call.

## Confirmed Root Cause

`PlaybackPayloadStorage::constData()` locks only while returning the address. The lock is no longer held when `TxPipelineExecutor` calls the synchronous device SDK. `PlaybackPayload::releaseStorage()` mutates the one storage object shared by producer state, desired requests, pending requests, in-flight requests, queued metatype copies, and executor state, so a producer edit can free the device thread's active input pointer.

The same contract affects every Playback provider, not only ARB. Any provider that releases its previous payload while an asynchronous download or Save IQ reader uses `constData()` has the same race.

## General Fix Design

1. Split the shared descriptor from the owned data block.
   - `PlaybackPayloadStorage` remains the shared descriptor used by all request copies.
   - A ref-counted data block owns the `QVector<int16_t>` or external pointer/release callback and the large-payload reservation.
2. `withStorageView()` acquires a local read lease to the data block under the descriptor mutex, then releases the mutex before executing the reader.
3. `releaseStorage()` atomically detaches the descriptor from the data block and returns without waiting for readers.
   - New readers immediately observe no host storage.
   - Existing readers retain the block until their SDK/file/analysis callback returns.
   - Physical storage, external generator ownership, and the >125 MiB reservation are released by the final lease.
4. Remove the public bare-pointer `PlaybackPayload::constData()` API and migrate every repository consumer to `withStorageView()` or a shared helper built on it.
5. Use one lease for each complete operation: device download, Save IQ write, metrics calculation, preview copy, or spectrum analysis.
6. Remove the temporary high-volume diagnostics after the confirmed race is fixed; retain normal queue and download-result logs.

This preserves asynchronous UI responsiveness: unlike holding the descriptor mutex for the entire SDK call, an AutoScale edit does not wait for device I/O.

## Fix Success Criteria

1. `releaseStorage()` returns promptly while an existing `withStorageView()` reader is active, but the reader's pointer remains valid until it returns.
2. No source outside `playbackpayload.cpp` calls `PlaybackPayload::constData()`; the public method is removed so future unsafe consumers fail at compile time.
3. Device download holds one read lease across the complete `downloadDataSequence()` call.
4. Digital/DSSS/OFDM Save IQ, generated-waveform metrics, spectrum analysis, and preview copying hold leases across their complete reads.
5. External `adoptExternal()` callbacks and large-payload reservations remain alive until the last read lease ends.
6. ARB, Quick Waveform, Analog Digital/DSSS/OFDM, HTRA generated Playback, and Multitone all inherit the same core fix without local waits or retries.
7. Focused static search, `git diff --check`, and the repository-approved build verification pass.
