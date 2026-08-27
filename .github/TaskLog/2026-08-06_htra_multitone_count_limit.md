# HTRA Multitone Count Product Limit

## Scope

- Limit HTRA Multitone `Count` to the product range `2..1024`.
- Keep `FreqSpacing` at the existing minimum of `1 kHz`.
- Apply the same range to the main editor, restored profiles, remote minibar editing, and device-capability writeback.
- Preserve the current device adjustment order: keep Count and reduce FreqSpacing first; reduce Count only when `1 kHz` still cannot form a valid plan.
- Keep the current `QTableWidget`; 1024 rows have been field-tested by the user and are accepted for this product boundary.
- Verification level: `static`; do not configure, build, or run.

## Observations

- The main `Multitone_Count` property currently defines only a minimum of `2` and no maximum.
- Generator profile sanitization currently clamps Count only to `>= 2`, so restored profiles can bypass a UI-only maximum.
- The device capability helper can currently report a Count maximum much larger than the intended product limit.
- The remote minibar editor consumes that capability maximum but currently exposes local minima of `1` for Count and `1 Hz` for FreqSpacing.
- The current generator and business already clamp FreqSpacing to `>= 1 kHz` and already prefer reducing FreqSpacing before Count when resolving device capability conflicts.

## Design

- Define shared HTRA Multitone product constants for minimum/maximum Count and minimum FreqSpacing.
- Use those constants in generator sanitization and `setCount()` so every profile entry path is canonicalized.
- Apply the same constants to main property metadata and capability-derived Count metadata.
- Cap the capability-derived maximum at the product maximum without changing the current Count/Spacing fallback order.
- Align the remote minibar numeric editor minima with the main product ranges; its maximum continues to come from the main process capability snapshot.
- Update the existing Multitone KnowledgeBase documents to distinguish the `1024` product cap from the device bandwidth constraint.

## Success Criteria

1. Main UI metadata reports `Count` range `2..1024` and `FreqSpacing >= 1 kHz`.
2. Generator profiles, including restored presets, cannot retain Count outside `2..1024`.
3. Remote editor reports Count minimum `2`, maximum no greater than `1024`, and FreqSpacing minimum `1 kHz`.
4. Device capability resolution still tries the original combination first, then preserves Count while lowering FreqSpacing, and only lowers Count if `1 kHz` remains invalid.
5. Existing odd/even lattice, tone-selection remapping, generation, and Enabled behavior are otherwise unchanged.

## Implementation Result

- Added shared Multitone product constants: Count `2..1024` and minimum FreqSpacing `1 kHz`.
- Main property metadata now exposes the Count maximum and keeps the existing FreqSpacing minimum.
- Generator profile sanitization and `setCount()` clamp Count at both ends, so preset restore and remote property writeback cannot retain a value above `1024`.
- Capability-derived Count maxima are capped at `1024`; the existing plan-first, Spacing-backoff, then Count-backoff flow is unchanged.
- Minibar remote numeric editing now exposes Count minimum `2` and FreqSpacing minimum `1 kHz`; its Count maximum remains supplied by the capability-aware main process and is now never above `1024`.
- Updated the current Multitone parameter, capability-policy, and product-decision KnowledgeBase documents with the new product limit.

## Static Verification Result

- Confirmed the shared constants, profile clamp, main metadata maximum, capability maximum, and remote editor minima are all present in currently built source files.
- Confirmed no stale `kMinFreqSpacingHz`, `Count >= 2`, or old 100001/160001/320001 product-limit examples remain in the relevant HTRA source and current KnowledgeBase documents.
- `git diff --check` passed; only existing line-ending conversion warnings were reported.
- Per repository policy and the requested static verification level, no configure, build, or runtime test was performed.
