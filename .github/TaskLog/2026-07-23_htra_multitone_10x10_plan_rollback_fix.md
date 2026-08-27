# HTRA Multitone 10x10 Plan And Rollback Fix

## Scope

- Fix HTRA Multitone planning for `Count = 10`, `FreqSpacing = 10 MHz` on the
  model-132 `125 MS/s` continuous playback domain.
- Preserve the existing even-count center-tone workaround and its
  `FreqSpacing / 2` FixedPlayback center offset.
- Prevent a newly edited profile from reusing a payload generated for an older
  profile when generation-plan resolution fails.
- Do not change the TX serial executor, device writeback, or Streaming paths.

## Evidence

Observed in `build/Qt_5_15_9_msvc2022_64-Debug/bin/debug.log`:

- Current Multitone execution context requested a `+5 MHz` center offset, which
  proves the live profile used `FreqSpacing = 10 MHz`.
- The downloaded payload was still `13 MS/s`, `130000` complex samples. Those
  values match the previously valid `Count = 10`, `FreqSpacing = 1 MHz` plan,
  not the requested 10 MHz spacing.
- Only one TX generation was dispatched; there was no concurrent device apply,
  stale writeback, or repeated center-offset configuration.

Static cause:

- With the even-count center mask, the baseband lattice is `-50 ... +40 MHz`.
- The occupied-band lower bound is `125 MS/s`.
- The current aligned-rate policy asks for the next integer multiple of
  `10 MHz`, namely `130 MS/s`, which is outside the device domain.
- Plan resolution then returns false without invalidating or rolling back the
  already-ready old payload.

## Design

1. Prefer the existing integer-multiple aligned sample-rate policy.
2. If no aligned rate exists, select the lowest device-supported rate at or
   above the occupied-band lower bound.
3. Continue using the existing rational exact-period sample-count solver. For
   `125 MS/s` and a `10 MHz` fundamental step, `10/125 = 2/25`, so a sample
   count divisible by 25 represents every tone exactly.
4. Cache the full last successfully resolved Multitone settings.
5. If final plan resolution fails, synchronously restore those settings and
   refresh the property/panel state. If no resolved settings exist yet,
   invalidate the generation plan so no stale payload can remain ready.
6. Log aligned-rate fallback, successful plans, and rollback decisions with a
   focused `[MultitonePlan]` prefix.

## Success Criteria

- `Count = 9`, `FreqSpacing = 10 MHz` remains valid and selects `100 MS/s`.
- `Count = 10`, `FreqSpacing = 10 MHz`, even-count center mask enabled, selects
  `125 MS/s` and generates a fresh payload for the requested profile.
- `Count = 11`, `FreqSpacing = 10 MHz` also selects `125 MS/s` instead of
  retaining an older waveform.
- Any genuinely unsatisfied plan restores the last resolved Count, spacing,
  tone-selection, and center-mask settings before returning.
- A failed plan can never expose a ready payload together with a different
  current profile.
- The existing Debug HTRA target builds successfully.

## Verification Level

`debug-build`, followed by user-operated `debug-run` frequency-domain
verification on the connected device.

