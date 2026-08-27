# Digital Large Waveform Export Chunking Analysis

## Scope

Analyze why `DigitalModulator` fails when saving very large generated IQ data and define a bounded design for preserving asynchronous/generated results for large Digital waveforms.

This is a static-analysis task only. No build, run, or code change is included in this TaskLog.

## Verification Level

static

## Observation

- `src/plugins/analog/digitalmodulator.cpp` stores generated IQ data in `DigitalWaveformResult::iqData`, then copies the third-party API buffer with `result.iqData.resize(len)` and `memcpy(...)`.
- `GenerateDigitalModWaveform(...)` returns `short **iq` and `int32_t *lenOut`; the current public algorithm API does not expose a streaming callback or file sink.
- `lenOut` is used as an `int16` word count. A value such as `1073741824` requires about `2 GiB` for the `QVector<int16_t>` copy, in addition to the memory already owned by the algorithm buffer.
- `DigitalModulation::writeWaveformToWav(...)` also expects a complete `QVector<int16_t>` and writes it as one data block.
- Current Digital playback semantics are trim-only: large playback generation may pass a capped `SymbolLength`, while Save IQ is intended to export the full waveform.
- `Utils::WavHeader::toByteArray(...)` writes legacy RIFF/WAV fields and rejects total file size above the 32-bit RIFF limit.

## Inference

The failure at `result.iqData.resize(len)` is not primarily a Qt container issue. It is a peak-memory design issue:

1. The third-party generator has already allocated a full `iq` buffer.
2. The application then attempts to allocate a second full buffer in `QVector`.
3. Save IQ later requires the complete vector again for one-shot WAV writing.

For large exports, keeping the full result as an in-memory async result is the wrong ownership model.

## Success Criteria For A Future Fix

- Large Save IQ does not require a full-size `QVector<int16_t>` copy of the generated waveform.
- Playback/preview cache behavior stays unchanged: trimmed playback still uses the bounded `m_complex` cache.
- Save IQ remains a separate full-export intent and does not overwrite `m_complex`.
- Export writes data incrementally to disk and reports useful failure messages.
- Legacy WAV exports are rejected before generation when the expected data/header size cannot fit RIFF/WAV 32-bit limits, unless a future task adds RF64/W64.
- Static verification confirms all changed source files are included by `src/plugins/analog/CMakeLists.txt`.

## Recommended Design

Use a dedicated full-export path that writes generated chunks directly to disk.

1. Keep the normal async worker for preview/playback only.
2. Add a Save IQ export helper that accepts a parameter snapshot and output path.
3. Compute full symbol length as `1 << PN`, but split it into bounded `SymbolLength` chunks.
4. For each chunk:
   - call `GenerateDigitalModWaveform(...)` with the chunk symbol length,
   - write the returned `short *iq` directly to `QFile`,
   - release the generator object or otherwise release the library-owned buffer according to the API ownership contract,
   - do not copy the chunk into a long-lived `QVector`.
5. Patch the WAV header at the end if the final data length is known only after chunk generation; otherwise write a validated legacy header first.

## Open Risk

Chunking is only correct if repeated calls with the same profile and shorter `SymbolLength` can produce consecutive segments, or if the algorithm API can accept/derive a segment offset. The current API does not expose an offset. This must be verified against the third-party algorithm semantics before implementation.

If repeated calls always restart the PN/filter state from the beginning, chunked generation would duplicate the first segment and would not be a valid full export. In that case, the minimum viable fix is a safer failure path plus an API request for a streaming/chunk callback or offset-based generator.

## 2026-06-23 Minimal Implementation Plan

User direction: for Digital Save IQ, the third-party API has already allocated the full `iq` buffer, so write that buffer directly to disk and avoid copying it into `QVector`.

Implementation boundary:

- Change only the Digital Save IQ full-export path.
- Keep playback/preview generation and `m_complex` unchanged.
- Add a raw-pointer WAV writer that accepts `const int16_t *` and `wordCount`.
- Let the existing `QVector` writer delegate to the raw-pointer writer for the small-cache fast path.
- In `runFullWaveformExport(...)`, generate the full waveform through `GenerateDigitalModWaveform(...)`, then write `iq` directly before `GenSignalObjRelease(...)`.
- Write data in fixed-size chunks instead of one giant `QFile::write(...)`.

Static verification:

- Confirm `src/plugins/analog/CMakeLists.txt` includes `digitalmodulation.cpp` and `digitalmodulation.h`.
- Do not build unless explicitly requested.

## 2026-06-23 Playback Cache Follow-up Analysis

New observation:

- Even when the UI default trim option is enabled, the normal Digital background worker can still see a large `len` from `GenerateDigitalModWaveform(...)`.
- The currently exposed third-party API still returns `short **iq`; it does not expose a caller-provided output buffer such as `short *out, int32_t outCapacity`.
- Therefore the application cannot currently allocate its own 2 GiB buffer and ask the third-party API to fill it.

Design conclusion:

- For playback/download, do not allocate a 2 GiB application buffer. The playback cache should only keep the download-sized prefix.
- If the third-party API returns a large `iq` buffer, the worker can defensively copy only `MAXDOWNLOADSIZE / sizeof(int16_t)` words, even if `len` is larger.
- The better upstream fix is still to ensure the `SymbolLength` passed for download intent produces a library `len` inside the download cap, but the copy-side cap prevents a Qt `QVector` allocation failure if the estimate is wrong.
- If future third-party APIs add a caller-buffer or streaming callback variant, then the application can avoid both the third-party full allocation and the Qt full allocation. The current header does not provide that.

Minimal implementation:

- Before `result.iqData.resize(len)` in the normal Digital worker, compute `copyWords = min(len, MAXDOWNLOADSIZE / sizeof(qint16))`, rounded down to an even IQ word count.
- Copy only `copyWords` from the third-party `iq` pointer.
- Treat the generation as trimmed when either the request was explicitly trimmed or `copyWords < len`.

## 2026-06-23 Trim Flag Simplification

New observation:

- `DigitalModulator` currently has three trim-related booleans: `m_nextGenerationTrimmed`, `m_pendingGenerationTrimmed`, and `m_currentGenerationTrimmed`.
- The first two model a one-shot request, but the UI checkbox is a persistent policy: checked means use trimmed download generation, unchecked means do not use that default policy.
- `m_currentGenerationTrimmed` is only used to keep Save IQ from treating a trimmed playback cache as a complete waveform cache.
- Follow-up correction: the normal `DigitalModulator::workerLoop()` cache is only for preview/playback/download and should always generate a download-sized trimmed cache. The Digital panel checkbox should only control whether large-waveform confirmation is shown.

Implementation boundary:

- Remove the one-shot/pending/current trim booleans and the later persistent `m_trimmedGenerationEnabled` flag from `DigitalModulator`.
- Pass trimmed generation intent directly in `workerLoop()`; full waveform export remains on the separate Save IQ branch.
- Keep `m_trimLargeWaveformByDefault` in `DigitalModulation` only as a prompt policy.
- Derive "current cache is complete" in `DigitalModulation` by comparing cached byte count with the current full waveform estimated size instead of storing another trim-result flag.
- Keep the existing copy cap in the worker so a bad or unexpectedly large `len` still cannot force a giant `QVector` allocation.
- Remove the unused `m_sampleLength` member while keeping the public `sampleLength()` query.
- Make `sampleLength()` lock consistently because it participates in cache completeness decisions.

Static verification:

- Use `rg` to confirm the removed trim variables and one-shot API are no longer referenced.
- Do not build unless explicitly requested.
