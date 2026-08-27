---
name: iqs-wav-playback-analysis
description: Use when analyzing HAROGIC IQS-WAV / .iq.wav files with magic bytes 8C 22 52 9B for SGS ARB/playback behavior, including identifying the file, extracting playback-needed sample rate and packet sizes, trimming data by TriggerRecord valid bytes, deciding AutoScale/PEP handling, and reasoning about cyclic playback discontinuities. Do not use for full spectrum-analyzer metadata extraction unrelated to SGS playback.
---

# IQS-WAV Playback Analysis

## Goal

Analyze a spectrum-analyzer IQS-WAV recording only far enough to decide how SGS should play it back.

Keep the scope narrow:

- Identify the private IQS-WAV format.
- Extract the IQ sample rate, packet slot size, per-packet valid byte counts, and effective Complex16 IQ payload.
- Decide SGS ARB playback scaling and caveats.
- Avoid expanding into full instrument metadata unless the user explicitly asks.

## Required Workflow

1. Read the task context.
   - If the user provides `IQS-WAV文件格式说明.md`, use it as the format authority.
   - If implementation behavior matters, inspect `src/libs/utils/iqswavreader.{h,cpp}` and the ARB caller before changing conclusions.
   - For implementation work, update the active TaskLog before editing code.

2. Recognize the file format by fixed layout and magic bytes.
   - `RIFF` at offset `0`, `WAVE` at offset `8`.
   - `fmt ` chunk at offset `12`.
   - `prof` chunk at offset `36`, normally payload size `356`.
   - Magic bytes at offset `46`: `8C 22 52 9B`.
   - Protocol version at offset `50`: `00 02`.
   - `trig` chunk at offset `400`.
   - `data` chunk at `400 + 25 * 1024 * 1024`; IQ payload starts 8 bytes after the `data` chunk header.

3. Parse `prof` correctly.
   - Read `ProfileStreamInfoLen` as big-endian `uint16` at offset `108`.
   - Read the MsgPack blob from offset `110` for that length.
   - Parse the MsgPack values sequentially.
   - Do not scan the whole `prof` area as fixed 9-byte `0xcb + 8-byte` fields; padding and mixed MsgPack types make that unreliable.
   - Playback-needed fields are:
     - IQ sample rate: field `44` when present; fall back to `fmt.SampleRate` if needed.
     - Packet samples: field `48`.
     - Packet data size: field `49`.
   - Optional consistency fields, when present, can include packet count, total valid complex samples, and total valid bytes. Treat them as checks, not as replacements for TriggerRecord trimming.

4. Parse TriggerRecord entries as big-endian records.
   - First TriggerRecord starts at offset `408`.
   - Compute the record length from the first record:
     - `TriggerInfoLen = BE uint16 @ +0`, normally `337`.
     - `DeviceStateLen = BE uint16 @ (2 + TriggerInfoLen)`, normally `52`.
     - `perTrigLen = 2 + TriggerInfoLen + 2 + DeviceStateLen + 4 + 4 + 4`, normally `405`.
   - Iterate by `perTrigLen` until the record header is invalid or reserved padding is reached.
   - Do not treat TriggerRecord as a 32-byte structure.
   - For SGS playback, the key field is `InPacketTriggeredDataSize = BE uint16 @ +10`.
   - `MaxPower_dBm` and `MaxIndex` can be logged for diagnostics, but do not use them to derive SGS DAC scaling.

5. Build the effective playback payload.
   - Use `PacketDataSize` as the packet slot stride in the `data` payload.
   - For each valid TriggerRecord, copy only the first `InPacketTriggeredDataSize` bytes from that packet slot.
   - Require valid bytes to be positive, no larger than `PacketDataSize`, and aligned to a Complex16 IQ pair.
   - Preserve a partial final packet if TriggerRecord marks it valid; discard only the padding after its valid bytes.
   - Example policy: if the 11th packet has `80` valid bytes, keep those 20 Complex16 samples and drop the rest of that slot.

## SGS Playback Rules

- Treat the extracted payload as raw Complex16 little-endian interleaved `I, Q`.
- For IQS-WAV ARB playback, the safe automatic behavior is:
  - force `AutoScale=true`;
  - lock IQScale at `100%`;
  - let the user adjust output through the common `Level/PEP` control.
- For unknown waveform type, preserve the original complex IQ trajectory. Do not convert to `I=Q` envelope form.
- Only apply AM/Pulse envelope-to-diagonal compensation when an external business context explicitly says the file is AM/Pulse:

```text
r[n] = sqrt(I[n]^2 + Q[n]^2)
I'[n] = Q'[n] = round(32767 * r[n] / max(r))
```

Without that external signal type knowledge, the transform destroys valid FM/PM/digital/multitone/AWGN phase information.

## Loop Continuity

IQS-WAV packet metadata defines data validity, not waveform periodicity.

- A random or stopped-at-arbitrary-time recording usually will not be cyclically continuous.
- Do not infer a QAM/OFDM/AM frame or symbol-cycle boundary from TriggerRecord alone.
- Do not automatically drop the last partial packet to chase continuity; keep valid bytes and discuss loop conditioning separately.
- If the user asks about spectral spreading in ARB loop playback, report it as a cyclic-boundary problem and separate these options:
  - generate a periodic waveform at the source;
  - search for an approximate loop cut point in IQ data;
  - apply explicit crossfade/windowing with EVM tradeoffs;
  - use streaming or a longer capture if cyclic replay is the wrong transport.

## Minimal Report

When reporting an IQS-WAV playback analysis, include only:

- format recognition result: magic, protocol, chunk offsets;
- sample rate used for playback;
- `PacketSamples`, `PacketDataSize`, packet count or parsed TriggerRecord count;
- valid bytes per packet, especially the final packet;
- total effective bytes and complex samples sent to SGS;
- padding discarded;
- scaling behavior: raw-complex AutoScale vs explicit AM/Pulse compensation;
- cyclic playback caveat if the user observed spectral spreading.

Avoid reporting DeviceState, GPS, edge arrays, RF state, temperature, or instrument `MaxPower_dBm` unless the user specifically asks for those fields.

## Common Pitfalls

- Do not use Python if the local `python.exe` is the Windows Store stub; use PowerShell byte reads or existing C++ helpers instead.
- Do not parse `prof` by visually scanning `0xcb` float64 markers.
- Do not compute packet count from a wrong `PacketDataSize`; first parse MsgPack field `49`, then cross-check `dataSize / PacketDataSize`.
- Do not include the 25 MiB `trig` reserved area or per-packet padding in waveform data.
- Do not use file name or header metadata to decide whether a recording is AM, Pulse, or digital modulation.
