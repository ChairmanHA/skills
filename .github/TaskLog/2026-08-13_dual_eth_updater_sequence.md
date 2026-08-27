
 2026-08-13 Dual ETH Updater Sequence

## Scope

- Support dual ETH firmware update for one device IP with ports 5000 and 5001.
- Remove the single-target updater argument assumption.
- Remove the "only disconnect current device" assumption before handing control to maintenance.
- Keep the existing maintenance handoff, package copy, and restart model.

## Implementation Plan

1. Build a firmware-update target list before disconnecting devices.
2. Include the current ETH endpoint first, then the co-resident peer endpoint if registered.
3. Ask Core to close every target device that is open before maintenance starts.
4. Pass explicit multi-target arguments to maintenance while preserving compatibility with the old single-target form.
5. In maintenance, run the external updater once per target in order; only after all targets succeed, continue to DLL/package copy and restart.

## Verification Level

- Static only. The user will compile and test manually.

## Success Criteria

- Existing single USB / single ETH update arguments remain supported.
- Dual ETH 5000/5001 uses one maintenance transaction and two sequential updater executions.
- Copy/restart starts only after every updater execution exits normally with code 0.