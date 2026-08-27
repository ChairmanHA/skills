# PM And Multitone Total Resolver Backoff

## Scope

- Convert PM and Multitone from recoverable plan-failure workflows into total
  business resolvers for normalized profiles and currently supported Playback
  capability domains.
- PM preserves phase deviation and other waveform semantics, and lowers `Rate`
  only when the requested rate cannot form a valid even-period sample-rate
  plan.
- Multitone preserves `Count` and tone selection first, and lowers
  `FreqSpacing`; it lowers `Count` only when the spacing required by the
  current continuous sample-rate ceiling would fall below 1 kHz.
- Remove last-resolved settings caches, ordinary-edit rollback, default reset
  after device resolution failure, and terminal plan invalidation from both
  businesses.
- Do not change waveform synthesis, asynchronous generation, rematerialization,
  center-offset semantics, ARB, or Streaming.

## Evidence

- PM requires an even samples-per-period value `n >= 6` and
  `n > 2 * (1 + PhaseDeviation)`. For any normalized phase deviation, choosing
  the minimum such `n` and writing back
  `Rate = continuousMaximumSampleRate / n` produces a supported exact-period
  plan. For model 132 and `PhaseDeviation = 2*pi`, this maps 10 MHz to
  7.8125 MHz with `n = 16`.
- Multitone's full candidate lattice has a bandwidth requirement proportional
  to `Count * FreqSpacing`. At fixed `Count`, reducing `FreqSpacing` to the
  maximum value supported by the continuous sample-rate ceiling preserves the
  topology and produces an exact rational period. If that value is below the
  1 kHz product minimum, reducing `Count` at 1 kHz restores feasibility.
- Hiding tones only removes active frequencies and therefore cannot make the
  full-lattice fallback harder to satisfy.
- Current supported Playback capability profiles all contain a non-empty
  continuous sample-rate range and enough payload capacity for these
  minimum-period plans.

## Design

### PM

1. Try the normalized requested profile unchanged.
2. If it has no plan, compute the smallest even samples-per-period count that
   satisfies the strict PM bandwidth inequality.
3. Write back the smaller of the requested Rate and the current highest
   continuous sample rate divided by that count.
4. Resolve once with the adjusted profile and submit the plan.

### Multitone

1. Try the normalized requested profile unchanged, including an exact 400 MSPS
   point when the lattice genuinely supports it.
2. If it has no plan, keep `Count` and limit `FreqSpacing` using the highest
   continuous sample rate.
3. If that spacing would be below 1 kHz, set spacing to 1 kHz and reduce
   `Count` to the largest full lattice supported by that continuous ceiling.
4. Resolve the adjusted profile and submit the plan.

At the business boundary, an empty result after this normalization is an
internal capability/arithmetic contract violation: retain one assertion and a
release-safe return, but do not restore old settings, reset defaults, clear a
plan, or report a recoverable edit failure.

Ordinary user/profile edits keep Enabled unchanged. Device-driven parameter
writeback disables Enabled, matching the successful-adjustment policy used by
the other total resolvers. Explicit reset still disables Enabled and restores
defaults.

## Success Criteria

- PM `Rate = 10 MHz`, `PhaseDeviation = 2*pi` on a 125 MSPS device writes Rate
  back to 7.8125 MHz and submits a 16-sample-period plan.
- The same PM profile on a 200 MSPS continuous device remains at 10 MHz with a
  valid plan.
- Multitone first preserves Count and lowers FreqSpacing. Count changes only
  when the supported spacing at that Count is below 1 kHz.
- Tone hiding and discrete tone selection do not invoke rollback or restore a
  prior profile.
- Device-driven PM/Multitone adjustment disables Enabled; ordinary edits do
  not.
- PM and Multitone contain no `m_lastResolvedSettings`,
  `rollbackAfterPlanFailure`, `resetAfterDevicePlanFailure`, or
  `resetIfUnsupported`.
- Remote Multitone editor actions no longer expose a recoverable plan-failure
  branch.
- Declaration/call audit and `git diff --check` pass.

## Verification Level

`static`. Do not build or run in this pass.

## Implementation Result

- PM now tries the requested profile first, then derives the smallest even
  samples-per-period count satisfying the strict phase-modulation bandwidth
  inequality and writes back only Rate.
- PM ordinary edits keep Enabled; device-driven Rate writeback disables
  Enabled.
- Multitone now tries the requested profile first, preserving exact supported
  domain-boundary periods such as `Count=10/11, FreqSpacing=10 MHz @
  125 MSPS`.
- If the requested Multitone profile has no plan, it preserves Count and lowers
  FreqSpacing to the highest full-lattice value supported by the continuous
  sample-rate ceiling. Count is lowered only when that spacing would be below
  1 kHz.
- Multitone ordinary edits keep Enabled; device-driven Count/FreqSpacing
  writeback disables Enabled.
- Removed both businesses' last-resolved settings state, rollback helpers,
  device-failure default reset, plan invalidation, and recoverable remote plan
  failure response.
- Rematerialization and asynchronous latest-profile generation behavior remain
  unchanged.

## Static Verification

- PM arithmetic audit:
  - `PhaseDeviation = 2*pi` requires 16 samples per period.
  - model 132 therefore limits Rate to `125 MHz / 16 = 7.8125 MHz`.
  - model 122 permits the requested 10 MHz at 160 MSPS.
- Multitone arithmetic audit with the active even-count center mask:
  - `Count=10, FreqSpacing=10 MHz` requires exactly 125 MSPS and is preserved.
  - `Count=11, FreqSpacing=10 MHz` also requires exactly 125 MSPS and is
    preserved.
  - `Count=12, FreqSpacing=10 MHz` requires 150 MSPS on model 132, so Count is
    preserved and FreqSpacing is written back to about 8.333333 MHz.
- Static search confirms PM and Multitone no longer contain
  `m_lastResolvedSettings`, `rollbackAfterPlanFailure`,
  `resetAfterDevicePlanFailure`, `resetIfUnsupported`, boolean
  `resolveAndGenerate()`, or remote `if (!resolveAndGenerate())` handling.
- HTRA CMake includes both business and generator source files.
- `git diff --check` passes; only the repository's Windows line-ending
  conversion warnings are reported.
- No build or runtime execution was performed.
