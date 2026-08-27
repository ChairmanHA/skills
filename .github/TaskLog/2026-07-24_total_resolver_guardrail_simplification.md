# Total Resolver Guardrail Simplification

> Superseded for PM and Multitone by
> `2026-07-24_pm_multitone_total_resolver_backoff.md`: both businesses now
> normalize to a valid profile and no longer retain failure rollback.

## Scope

- Treat the following active parameter-generated Playback resolvers as total
  functions for normalized profiles and supported device capability snapshots:
  - Digital Modulation, DSSS, OFDM
  - AM, FM, Pulse, Digital Ramp, AWGN
- Remove their business-layer last-resolved settings/plan caches, ordinary-edit
  rollback, generation-plan invalidation, and reset-on-resolution-failure
  branches.
- Retain failure recovery in PM and Multitone because their legal parameter
  spaces can contain combinations with no device-aligned generation plan.
- Preserve explicit device-switch compatibility behavior:
  - Digital/DSSS/OFDM still reset when the old profile is unsupported by the
    new device.
  - Pulse/Ramp/AWGN still disable Enabled when device-driven quantization
    changes the profile.
- Do not change waveform algorithms, sample-rate formulas, asynchronous
  generation, ARB, or Streaming.

## Evidence

- Digital non-FSK maps an unsupported `Rb * SPS` to the continuous sample-rate
  domain and writes back `Rb = Fs / SPS`. FSK scales Rb/deviation to the
  continuous maximum.
- DSSS similarly maps its requested sample rate and writes back Rb.
- OFDM clamps SampleRate and symbolCount; Pulse/Ramp/AWGN clamp or quantize
  their coupled parameters to current domain/capacity.
- AM and FM have a valid aligned sample-rate candidate throughout their
  normalized parameter ranges on all currently supported Playback domains.
- PM has a real legal no-plan case on model 132: `Rate = 10 MHz` and
  `PhaseDeviation = 2*pi` require an even aligned rate of 160 MHz, above the
  125 MHz maximum.
- Multitone can fail its exact-period tone-lattice check and has field-tested
  rollback behavior that must remain.

## Design

- A total resolver may still return `std::optional` internally because shared
  layout helpers validate arithmetic and capability contracts.
- At the business boundary, absence is an internal contract violation, not a
  recoverable user-state transition. Keep one assertion plus release-safe early
  return; do not mutate Enabled, restore settings, reset defaults, or clear a
  previous plan.
- Convert the eight total `resolveAndGenerate()` methods to `void`, retaining
  only adjustment flags where they represent successful device-driven
  quantization.
- PM and Multitone remain unchanged.

## Success Criteria

- Only PM and Multitone contain `m_lastResolvedSettings` or
  `rollbackAfterPlanFailure()` among active generated modulations.
- Only PM and Multitone have reset-on-terminal-plan-failure behavior.
- Digital/DSSS/OFDM device incompatibility prechecks remain intact.
- Pulse/Ramp/AWGN device adjustment still disables Enabled when parameters are
  rewritten.
- All ordinary edit and `setProfile()` paths for total resolvers commit a
  resolved profile/plan without maintaining rollback state.
- Static declaration/call audit and `git diff --check` pass.

## Verification Level

`static`. Do not build or run in this pass.

## Implementation Result

- Removed business-layer last-resolved settings/plan state and rollback helpers
  from Digital Modulation, DSSS, OFDM, AM, FM, Pulse, Digital Ramp, and AWGN.
- Converted those eight `resolveAndGenerate()` methods to `void`; an empty
  internal result now has one assertion and one release-safe early return.
- Removed their reset-on-resolution-failure branches.
- Preserved Digital/DSSS/OFDM device compatibility prechecks.
- Preserved successful device-driven adjustment disabling for OFDM, Pulse,
  Digital Ramp, and AWGN.
- PM and Multitone failure recovery is unchanged.

## Static Verification

- Static search confirms that `m_lastResolvedSettings`,
  `rollbackAfterPlanFailure()`, and `resetIfUnsupported` now occur only in PM
  and Multitone among active generated modulations.
- Declaration/call audit confirms the eight total resolvers use their simplified
  signatures and device adjustment calls retain the correct flag.
- `git diff --check` passes; only Windows line-ending conversion warnings are
  reported.
