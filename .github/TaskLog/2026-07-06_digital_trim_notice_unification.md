# 2026-07-06 Digital trim notice unification

## Scope

Unify Digital Modulation large-waveform trim messaging so the unchecked `Default Trim Download` path uses an informational OK-only notice, matching the existing notice shown when the option is turned off after a large waveform already exists.

## Verification Level

static

## Assumptions And Evidence

- Observation: `DigitalModulation` handles PN and Oversample large-waveform edits through `handleLargeWaveformTrimRequest(...)`, which currently shows a Yes/Cancel confirmation when default trim is disabled.
- Observation: turning off `Default Trim Download` while the current waveform exceeds the download cap already calls `showTrimmedDownloadNotice(...)`, which is an informational OK-only dialog.
- Assumption: the requested behavior applies to Digital Modulation only; shared Ramp/OFDM large-waveform confirmation behavior should not change.

## Success Criteria

- PN/Oversample edits that exceed `MAXDOWNLOADSIZE` show the same OK-only informational text when default trim is disabled.
- After the OK-only notice, Digital still proceeds with the same trim generation request it used after confirmation before.
- The existing notice shown when toggling `Default Trim Download` off remains unchanged.
- Shared `AnalogPlaybackBusiness::showTrimmedDownloadPrompt(...)` behavior remains unchanged for other analog businesses.

## Static Check

- Inspect Digital large-waveform call sites and confirm no remaining Yes/Cancel prompt is used for Digital's default-trim-disabled PN/Oversample path.

## Implementation

- Added a Digital-specific large-waveform trim handler that preserves default-trim direct execution.
- When default trim is disabled, the handler now shows the existing OK-only `showTrimmedDownloadNotice(...)` text and starts trim generation from the notice dismissal callback.
- Existing trimmed-cache reuse also routes through the same OK-only notice instead of the shared Yes/Cancel prompt.
- Removed the old PN/Oversample rollback captures because there is no longer a Cancel branch in the Digital path.

## Verification Result

- `rg` confirmed `DigitalModulation` no longer calls `handleLargeWaveformTrimRequest(...)`, `showTrimmedDownloadPrompt(...)`, `MSG_QUESTION`, or the old `Do you want to continue?` text.
- `git diff --check -- src/plugins/analog/digitalmodulation.cpp src/plugins/analog/digitalmodulation.h .github/TaskLog/2026-07-06_digital_trim_notice_unification.md` passed, with only existing LF-to-CRLF working-copy warnings for the touched C++ files.
- No build or runtime run was performed; this task stayed at static verification level.

## Repository Note

- `.github/` is ignored by the current `.gitignore`, so this TaskLog is present locally but does not appear in ordinary `git status`.
