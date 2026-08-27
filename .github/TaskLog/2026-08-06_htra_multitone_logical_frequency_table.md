# HTRA Multitone Logical Frequency Table

## Scope

- Keep the generated IQ/baseband frequencies unchanged.
- When `EvenCountCenterToneMask` is active for an even Count, display each tone relative to the user-visible logical RF center by adding `FreqSpacing / 2` to the actual baseband frequency.
- Keep odd Count and non-shifted even Count display behavior unchanged.
- Apply the same display semantics to the main Multitone panel and the remote minibar Multitone panel.
- Verification level: `static`; do not configure, build, or run.

## Observations

- The current shifted even lattice is `{-m*spacing, ..., 0, ..., +(m-1)*spacing}` in IQ baseband coordinates.
- FixedPlayback offsets the physical device center by `+FreqSpacing/2` while preserving the logical center in UI writeback.
- The table currently displays `MultitoneCandidateToneInfo::requestedFrequency`, which is the actual IQ/baseband coordinate, so an even shifted lattice appears asymmetric around zero.
- `latticeIndex` owns enabled-tone selection; changing display frequency must not change tone identity or generated waveform frequency.

## Design

- Preserve `requestedFrequency` as the actual generator/baseband frequency.
- Add a separate `displayFrequency` to candidate tone UI information.
- For shifted even Count, calculate `displayFrequency = requestedFrequency + FreqSpacing/2`; otherwise use `requestedFrequency` unchanged.
- Sort and format the main table by `displayFrequency`.
- Publish both actual and display frequencies in the remote editor snapshot; the helper sorts and formats by display frequency, with requested frequency as a compatibility fallback.

## Success Criteria

1. Even shifted Count displays a symmetric half-spacing lattice around zero.
2. Odd Count display is unchanged.
3. Candidate `latticeIndex`, enabled state, generator bins, sample-rate requirements, IQ data, and device center offset are unchanged.
4. Main and remote Multitone tables display the same offsets.

## Implementation Result

- Added `displayFrequency` to candidate tone UI information while preserving `requestedFrequency` as the actual IQ/baseband coordinate.
- `candidateTones()` adds `FreqSpacing / 2` only when the hidden even-count center mask is active.
- The main table now sorts and formats `displayFrequency`.
- The main process publishes both actual and display frequencies to the minibar; the helper sorts and formats the display value and falls back to requested frequency for compatibility with an older snapshot.
- Updated the current Multitone KnowledgeBase document with the logical-center table semantics.

## Static Verification Result

- Confirmed Count=4, Spacing=1 MHz maps actual `[-2,-1,0,+1] MHz` to displayed `[-1.5,-0.5,+0.5,+1.5] MHz`.
- Confirmed odd Count display receives zero offset and remains unchanged.
- Confirmed `displayFrequency` is referenced only by candidate UI information, main/remote table display, and remote snapshot transport; it does not enter sample-rate requirements, bin resolution, phase assignment, or waveform synthesis.
- `git diff --check` passed; only existing line-ending conversion warnings were reported.
- Per repository policy and the requested static verification level, no configure, build, or runtime test was performed.
