# DSSS / Digital Remote Oversample Options Merge Fix

## Status

- Task type: merge regression fix
- Verification level: Debug target build
- Owning target: `AnalogModulation`

## Evidence

1. `DsssModulation::remoteEditorState()` and
   `DigitalModulation::remoteEditorState()` call the obsolete algorithm-layer
   `oversampleOptions()` API.
2. Commit `e61b9aca` intentionally removed that API, its signal/property, and
   device capability storage from both modulators so the algorithm layer only
   consumes a resolved generation plan.
3. The current business classes already own device-aware option filtering in
   `refreshOversampleOptions()`.
4. Merge commit `3ac3935b` retained remote-editor callers from another branch
   while retaining the decoupled modulator APIs, producing the compile error.

## Scope

- `src/plugins/analog/dsssmodulation.h/.cpp`
- `src/plugins/analog/digitalmodulation.h/.cpp`

Do not restore device capability APIs in `DsssModulator` or
`DigitalModulator`.

## Implementation

1. Add a private business-layer `availableOversampleOptions()` helper to DSSS
   and Digital.
2. Compute enabled values from the existing enum candidates, current business
   profile, and current Playback capability snapshot.
3. Reuse the helper for both local property option refresh and remote editor
   state serialization.
4. Remove all remaining calls to modulator-layer `oversampleOptions()`.

## Success Criteria

1. No `DsssModulator::oversampleOptions()` or
   `DigitalModulator::oversampleOptions()` call remains.
2. Local UI and remote editor publish the same enabled oversample values.
3. Modulator headers remain free of device capability option APIs.
4. `git diff --check` passes.
5. Existing Debug build tree successfully builds target `AnalogModulation`.

