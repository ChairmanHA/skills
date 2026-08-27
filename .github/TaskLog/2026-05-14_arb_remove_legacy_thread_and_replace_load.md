# 2026-05-14 Arb remove legacy thread and replace load

## Background
- Arb playback now has a core-managed/provider path for ordinary WAV playback.
- ProgrammedArb bridge logic remains in ArbModulationOnly, but the old device-thread lifecycle hooks are still present.
- ArbPanel UI already removed the unload button, while cpp/h still contain unload-specific behavior.

## Goal
1. Remove the obsolete legacy device-thread start/stop/profile-change path from ArbModulationOnly cleanly.
2. Change ArbPanel load behavior to replace the current file directly without showing an "already loaded" prompt.
3. Remove stale unload-button wiring and keep panel state updates driven by Arb_FileName/property changes.

## Local hypothesis
- The remaining deviceThread/startBusiness/stopBusiness/onDeviceProfileChanged path is dead legacy for the currently included playback architecture and can be reduced to no-op overrides without affecting provider-based runtime behavior.
- ArbPanel can treat every load action as selecting a replacement file by directly assigning Arb_FileName, and property-driven UI refresh is already sufficient.

## Planned changes
- Update ArbModulationOnly header/source to remove QThread ownership and related helper methods that were only used by the legacy path.
- Keep ProgrammedArb bridge/configuration/download helpers only if still referenced by the remaining bridge entry points; otherwise drop unreachable triggers.
- Update ArbPanel to remove unload-specific public slot API and button wiring, and make loadFile replace any existing file silently.
- Verify with file-level diagnostics on the touched files only.

## Validation
- Run file diagnostics for ArbModulationOnly/ArbPanel sources and header after edits.
