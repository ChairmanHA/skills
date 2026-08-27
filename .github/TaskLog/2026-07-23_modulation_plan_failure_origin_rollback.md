# Modulation Plan Failure Origin-Aware Rollback

> Superseded in part by
> `2026-07-24_total_resolver_guardrail_simplification.md`: ordinary-edit
> rollback is now retained only for PM and Multitone. The other eight active
> generated modulations treat resolution as a total business operation.

## Scope

- Cover every active parameter-generated Playback modulation registered by the
  Analog and HTRA plugins:
  - Digital Modulation, DSSS, OFDM
  - AM, FM, PM, Pulse, Multitone, Digital Ramp, AWGN
- Distinguish generation-plan failure caused by an ordinary profile mutation
  from failure encountered while applying a new device capability snapshot.
- Do not change ARB file playback or Streaming. They do not use the
  parameter-profile-to-generation-plan workflow covered by this task.
- Do not change the waveform algorithms or sample-rate formulas except where a
  small consistency fix is required by rollback.

## Evidence

- Multitone already caches its last successfully resolved settings and restores
  them when an ordinary plan resolution fails.
- Digital, DSSS, OFDM, AM, FM, PM, and Pulse currently use the same
  `resetIfUnsupported` default for both UI/profile edits and device capability
  changes. An ordinary invalid edit can therefore disable modulation and reset
  the complete profile.
- Ramp and AWGN write back successfully adjusted profiles, but a terminal
  resolver failure only returns `false`; they do not restore the last resolved
  profile.
- A resolver failure that returns before `setGenerationPlan()` can leave the
  previous generated result associated with a newly mutated live profile.

## Required Behavior

### Ordinary user/profile mutation

1. Resolve the edited profile against the current device capabilities.
2. On success, commit any resolver writeback, cache the complete resolved
   settings and, where payload metadata can diverge from the visible profile,
   the exact plan, then generate.
3. On terminal plan failure, preserve the modulation Enabled state, restore the
   complete last successfully resolved settings, and ensure the generated
   result again belongs to that restored profile.
4. If initialization has no last resolved profile, invalidate the generation
   plan so no ready payload can be exposed for an unresolved profile.

Ordinary mutations include local property editing, remote editor actions,
`setProfile()`, and explicit rematerialization that re-enters resolution.

### Device capability change

1. Apply the new capability snapshot.
2. If the current profile can still be resolved, retain the existing
   business-specific successful adjustment/writeback policy.
3. If it cannot be resolved, disable modulation and restore the complete
   business default profile, then resolve/generate that default under the new
   device.
4. If even the default profile cannot be resolved, invalidate the generation
   plan and discard any last-resolved cache inherited from the previous device.

### Explicit reset

- Disable modulation, restore defaults, and resolve the default profile.
- Do not restore the pre-reset last-resolved profile if the default itself
  cannot be resolved.

## Implementation Boundaries

- Keep the existing asynchronous generator/latest-profile discard behavior.
- Reuse each modulator's existing `saveSettings()`, `restoreSettings()`,
  `resetSettings()`, and `setGenerationPlan()` APIs.
- Keep successful parameter quantization semantics for Pulse, Ramp, AWGN,
  OFDM capacity adjustment, and Multitone device-domain adjustment.
- Keep device-switch silent reset semantics; do not add dialogs.
- Preserve Multitone's tested ordinary-edit rollback behavior while adding the
  separate device-failure default-reset path.

## Success Criteria

- An unresolvable ordinary edit in every covered modulation restores the full
  most recently resolved profile and does not turn Enabled off.
- An unresolvable `setProfile()` or remote edit follows the same rollback rule
  as local UI editing.
- A device capability change that makes the current profile unresolvable turns
  Enabled off and resolves the complete default profile for that device.
- A failed default resolution leaves no ready payload or valid cached plan from
  the previous profile/device.
- Successfully resolved automatic writeback remains unchanged.
- Multitone continues to pass its current Count/FreqSpacing/tone-selection
  rollback behavior.
- Static search shows no active generated modulation using default
  reset-on-failure semantics for ordinary edits.

## Verification Level

`static` for this implementation pass. No build or runtime execution unless
separately requested.

## Implementation Result

- Added last-resolved full-profile rollback to Digital Modulation, DSSS, OFDM,
  AM, FM, PM, Pulse, Digital Ramp, and AWGN; preserved Multitone's existing
  ordinary-edit rollback.
- Routed only explicit reset and device-capability application through the
  disable-and-default recovery path.
- Added a device-failure default-reset path to Multitone without changing its
  tested Count/FreqSpacing/tone-selection edit behavior.
- Digital Modulation, DSSS, and OFDM retain the last exact generation plan and
  rematerialize it when restored sample metadata does not match the currently
  ready payload.
- Digital Modulation's PN and Oversample preflight editors now reject only the
  attempted field value instead of invoking the full reset path.
- Removed Digital Modulation's spurious non-FSK deviation writeback error while
  retaining the editability guard for external callers.
- ARB and Streaming remain unchanged.

## Static Verification

- Confirmed from plugin CMake/registration that the ten covered businesses are
  the active parameter-generated Playback modulations.
- Audited every `resolveAndGenerate()` call: ordinary edits use rollback
  semantics; reset and `applyDeviceCapabilities()` opt in to reset-on-failure.
- Confirmed there is no covered header whose ordinary-call default is
  reset-on-failure.
- `git diff --check` passes; only the repository's existing Windows line-ending
  conversion warnings are reported.
