# Quick Waveform Keyboard And Playback Slice Fix

## Scope

- Make every Quick Waveform numeric popup use the same unpolluted soft-keyboard theme as the `Period` (`点数`) editor.
- Make `Period`, `Samples To Use`, and `Sample Offset` affect the playback payload with the same complex-sample semantics as ordinary ARB playback.
- Correct ordinary WAV loading so one complex sample always contributes both interleaved `I` and `Q` words.
- Keep IQS-WAV valid-byte extraction, forced AutoScale behavior, shared playback padding, and the runtime/device apply flow unchanged.

## Observations

- `QuickWaveformPanel::updateStyle()` applies an unscoped local `QPushButton` rule to Sample Rate, I/Q Scale, Samples To Use, and Sample Offset, but not to Period.
- `TouchNumKeyboard` is parented to the triggering button, so those local rules cascade into the keyboard's child buttons. Period has no such local rule and therefore uses the correct global keyboard theme.
- `QuickWaveformBusiness::buildPlaybackExecutionContext()` currently converts and sends the complete `m_wavData`; it does not read `sampleOffset`, `samplesToUse`, or `period`.
- Ordinary WAV `WavHeader::sampleCount()` is a complex-sample count, but Quick Waveform currently maps only `sampleCount * sizeof(int16_t)` bytes. For stereo Complex16 WAV this loads only half of the interleaved I/Q words.
- ARB ordinary-WAV playback treats all three slice parameters as complex-sample counts: offset chooses the source start, samples-to-use chooses the copied valid region, and period chooses the final zero-padded payload length.

## Inference

- The keyboard defect is stylesheet inheritance from the four trigger buttons, not a `TouchNumKeyboard` layout or metadata defect.
- The waveform defect is caused by both a missing slice/zero-fill stage and an inconsistent complex-sample-to-word conversion in ordinary WAV loading.

## Success Criteria

1. Sample Rate, I/Q Scale, Samples To Use, and Sample Offset no longer propagate a local generic `QPushButton` rule into their soft keyboards; their keyboard appearance follows the same theme path as Period.
2. For a loaded waveform with `N` complex samples, ordinary WAV and IQS-WAV both retain `2 * N` interleaved `int16`-equivalent words before playback shaping.
3. The runtime payload contains exactly `period` complex samples before shared minimum-download padding.
4. The first `samplesToUse` complex samples of that payload come from source complex sample `sampleOffset`; the remaining period is zero-filled.
5. `samplesToUse` remains clamped to `min(samplesInFile - sampleOffset, period)`.
6. AutoScale/IQ Scale is applied consistently with ARB semantics, and IQS-WAV remains forced to AutoScale/100%.
7. Power metrics are calculated from the shaped payload before shared download padding.

## Verification Level

- `static`

## Static Verification Checklist

- [x] Confirm Quick Waveform sources are included by `src/plugins/quickwaveform/CMakeLists.txt` and the plugin directory is included by `src/plugins/CMakeLists.txt`.
- [x] Read the current version of every source file changed by this task.
- [x] Confirm no unscoped per-trigger `QPushButton` stylesheet remains on the four affected LabelButtons.
- [x] Confirm ordinary WAV byte/word/sample arithmetic preserves complete Complex16 pairs.
- [x] Compare the final slice, scaling, zero-fill, power-metric, and shared-padding order with ARB ordinary-WAV playback.
- [x] Run `git diff --check` and review the final diff for unrelated changes.

## Implementation Result

- Removed the four local generic `QPushButton` rules from `QuickWaveformPanel`; all five numeric LabelButtons now use the same global theme chain, and trigger-local QSS no longer cascades into `TouchNumKeyboard`.
- Ordinary WAV loading now requires stereo 16-bit Complex IQ, rejects payloads beyond the 100 MiB playback limit, and loads all `2 * sampleCount` interleaved words.
- `buildPlaybackExecutionContext()` now applies ARB-equivalent AutoScale/IQ Scale semantics, copies the requested complex-sample slice from `sampleOffset`, zero-fills to `period`, calculates power metrics against the period denominator, and only then applies shared playback minimum-length normalization.
- Connected Quick Waveform AutoScale edits to the business setter so a UI change rebuilds the selected provider execution context; IQS-WAV continues to force AutoScale and IQ Scale 100%.
- Static verification passed. The maximum shaped payload is 52,428,800 words (100 MiB), which remains within the `QVector`/`int` indexing used here; slice bounds guarantee `sourceWordOffset + activeWordCount <= data.size()`; `git diff --check` reports no whitespace errors.
