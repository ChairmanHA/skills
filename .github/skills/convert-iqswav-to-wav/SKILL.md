---
name: convert-iqswav-to-wav
description: Convert HAROGIC spectrum-analyzer IQS-WAV protocol 0x0002 recordings into compact ordinary stereo PCM16 IQ WAV files by removing the fixed roughly 25 MiB private prof/trig area and packet padding, retaining TriggerRecord-valid Complex16 I/Q, and applying SGStudio Streaming complex-peak AutoScale once. Use when asked to shrink, pre-autoscale, batch-convert, or replace recorded .iq.wav/.wav IQS files with Streaming-compatible ordinary WAV files. Do not use for arbitrary audio transcoding, DET WAV, or full spectrum-analyzer metadata extraction.
---

# Convert IQS-WAV To Ordinary WAV

Convert with the bundled deterministic script. Do not rewrite the binary parser ad hoc.

## Workflow

1. Confirm the output policy.
   - Default to a new sibling named `<stem>.ordinary.wav`.
   - Use an output directory to preserve original filenames without replacing inputs.
   - Use in-place replacement only when the user explicitly requests it and the source is recoverable from Git or a verified backup.

2. Analyze before converting.

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
     ".github/skills/convert-iqswav-to-wav/scripts/Convert-IqsWavToOrdinaryWav.ps1" `
     -InputPath "capture.iq.wav" -AnalyzeOnly
   ```

   For machine-readable output, add `-Json`.

3. Convert using one of these modes.

   Safe sibling output:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
     ".github/skills/convert-iqswav-to-wav/scripts/Convert-IqsWavToOrdinaryWav.ps1" `
     -InputPath "capture.iq.wav"
   ```

   Batch output with original filenames:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
     ".github/skills/convert-iqswav-to-wav/scripts/Convert-IqsWavToOrdinaryWav.ps1" `
     -InputPath "recordings" -OutputDirectory "converted"
   ```

   Explicit in-place replacement:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
     ".github/skills/convert-iqswav-to-wav/scripts/Convert-IqsWavToOrdinaryWav.ps1" `
     -InputPath "capture.iq.wav" -InPlace -Force
   ```

   Add `-Recurse` when a directory tree must be scanned. Add `-WhatIf` before a destructive or large batch conversion.

4. Review the report.
   - Confirm sample rate, packet count, valid bytes, padding discarded, complex samples, gain, output peak magnitude, output size, and SHA-256.
   - Confirm every output peak magnitude is at most `32767`.
   - Confirm the output has `DataOffset = 44` and `OrdinaryWav = true`.

5. Report what changed.
   - List input and output paths.
   - State whether originals were preserved or replaced.
   - Report valid complex samples, bytes removed, applied gain, and output hashes.
   - State that a non-100% Streaming IQScale still causes the ordinary WAV path to apply the user-requested scale.

## Conversion Contract

- Accept only RIFF/WAVE, PCM, two-channel, 16-bit IQS protocol `0x0002` with magic `8C 22 52 9B`.
- Parse MsgPack values sequentially from the declared profile length. Use field 44 for IQ sample rate, field 48 for packet samples, and field 49 for packet slot bytes.
- Compute TriggerRecord length from its embedded lengths. Use `InPacketTriggeredDataSize` at record offset `+10`.
- Copy valid bytes from each `PacketDataSize` slot in order. Preserve a partial final packet and drop only padding.
- Preserve little-endian interleaved `I,Q` phase trajectories. Never infer modulation type from filenames.
- Select the greatest finite TriggerRecord `MaxPower_dBm`, read its valid `MaxIndex` Complex16 sample, and calculate:

  ```text
  rMax = sqrt(I_peak^2 + Q_peak^2)
  gain = 32767 / rMax
  ```

- Scale each I/Q component with truncation toward zero and int16 saturation, matching current `StreamingDataGenerator`.
- Never apply AM/Pulse envelope-to-diagonal `I=Q` compensation without an explicit separate requirement.
- Write a 44-byte ordinary WAV header: PCM format 1, two channels, 16 bits, original IQ sample rate, block alignment 4, followed by one contiguous `data` payload.

## Failure Rules

- Refuse unsupported protocol versions, DET WAV, non-PCM16 data, malformed profile fields, invalid packet geometry, truncated payloads, unusable peak metadata, RIFF overflow, duplicate batch output paths, or accidental overwrites.
- Prepare and fully validate a same-directory temporary output before committing it.
- Delete only the converter's own uncommitted temporary file after failure.
- Do not silently fall back to raw packet slots or gain `1.0` when valid-byte or AutoScale metadata is unusable.

## Repository Context

When SGStudio implementation semantics may have changed, inspect:

- `src/libs/utils/iqswavreader.{h,cpp}`
- `src/plugins/htra/streamingdatagenerator.cpp`
- `.github/KnowledgeBase/IQS-WAV文件格式说明.md`

Use `iqs-wav-playback-analysis` instead when the request is only to diagnose playback, power semantics, or loop discontinuity without converting files.

