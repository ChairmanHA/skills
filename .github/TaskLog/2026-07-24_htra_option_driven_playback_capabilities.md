# HTRA Option-Driven Playback Capabilities

## Scope

- Replace HTRA's model-based Playback capability table with the physical
  `OPTION_BW_320M_TX` option reported by `device_query_options()`.
- Query device options once during a successful `FancyDevice::open()` and use
  the same result for RF-port and Playback capability resolution.
- Preserve the user's current extended-device capacity change as exactly
  `1000 * 1024 * 1024` bytes.
- Keep Streaming on its existing independent
  `[DATA_SAMPLE_RATE_MIN, STREAMING_SAMPLE_RATE_MAX]` domain.
- Update the existing capability architecture documents so they describe the
  option-driven behavior rather than model-derived behavior.
- Do not change individual waveform resolvers, payload ownership, runtime
  validation, device switching, or UI behavior.

## Observation

- `FancyDevice::open()` already calls `device_query_options()` after the device
  handle is opened, but currently consumes only the medium-power option.
- `OPTION_BW_320M_TX` is now defined by the staged H2 API v2.0.28 headers as
  option `51`.
- `htradevicecapabilityresolver.cpp` currently maps model 132 to the baseline
  domain and models 122/123/150/151 to the extended domain.
- The current working-tree edit has already changed the extended payload
  capacity from 996 MiB to 1000 MiB.
- Existing KnowledgeBase documents explicitly state that the model table
  should be replaced when the API exposes a physical capability query.

## Design

1. During a successful device open, scan all returned option IDs without
   stopping after the first recognized option.
2. Set RF high-power availability from `OPTION_MEDIUM_POWER`.
3. Set extended Playback availability from `OPTION_BW_320M_TX`.
4. Resolve HTRA Playback capabilities from that boolean only:
   - option present:
     `[DATA_SAMPLE_RATE_MIN, 200e6] U {400e6}`, 1000 MiB;
   - option absent:
     `[DATA_SAMPLE_RATE_MIN, 125e6]`, 125 MiB.
5. If the option query fails, log the failure and publish the conservative
   no-option baseline. A query failure must not invent extended capability.
6. Remove model/hardware parameters and the unsupported-model branch from the
   HTRA resolver; any HTRA device successfully opened by the API receives one
   of the two option-derived profiles.

## Success Criteria

- Models 122, 132, and other successfully opened HTRA models use the same
  option-driven capability decision.
- `OPTION_BW_320M_TX` present produces a continuous maximum of 200 MSPS, an
  isolated 400 MSPS point, and `1000 * 1024 * 1024` maximum Playback bytes.
- The option absent or the query failing produces a continuous maximum of
  125 MSPS, no isolated point, and `125 * 1024 * 1024` maximum Playback bytes.
- The options loop can recognize both medium-power and bandwidth options in
  one response.
- Streaming capability remains unchanged at 62.5 MSPS maximum.
- Resolver declarations and call sites agree, stale model constants are
  removed, relevant KnowledgeBase baselines are updated, and
  `git diff --check` passes.

## Verification Level

`static`. Do not build or run in this pass.

## Implementation Result

- `FancyDevice::open()` now performs one `device_query_options()` call and
  scans all returned option IDs.
- The same response drives:
  - RF High Power availability from `OPTION_MEDIUM_POWER`;
  - extended Playback capability from `OPTION_BW_320M_TX`.
- The options loop no longer exits after finding the power option, so both
  capabilities can be recognized in one device response.
- A failed option query logs its status and selects the conservative baseline.
- The HTRA resolver now accepts only the bandwidth-option result. Its model and
  hardware-version table, known-model constants, and unsupported-model branch
  were removed.
- The two published Playback profiles are:
  - no bandwidth option: `[DATA_SAMPLE_RATE_MIN, 125e6]`, 125 MiB;
  - bandwidth option: `[DATA_SAMPLE_RATE_MIN, 200e6] U {400e6}`, 1000 MiB.
- Streaming remains `[DATA_SAMPLE_RATE_MIN, STREAMING_SAMPLE_RATE_MAX]`.
- The existing capability, waveform-constraint, product-decision, large
  payload, H2 API, device-open, ARB, Digital, Multitone, Ramp, RF-port, and
  execution-context KnowledgeBase documents now use the option-driven source
  and 1000 MiB extended capacity.

## Static Verification

- Resolver declaration, definition, and only call site all use
  `bool has320MTransmitBandwidthOption`.
- Static search finds one `device_query_options()` call in active HTRA source,
  with comparisons against both `OPTION_MEDIUM_POWER` and
  `OPTION_BW_320M_TX`.
- Static search finds no stale `kModelF60*`, `kModelCurrent`, `kModelF400*`, or
  `Unsupported HTRA model` logic in the HTRA plugin.
- HTRA CMake includes `fancydevice.cpp`,
  `htradevicecapabilityresolver.cpp`, and their headers.
- Extended-capacity arithmetic:
  - bytes: `1000 * 1024 * 1024 = 1,048,576,000`;
  - complex samples: `1,048,576,000 / 4 = 262,144,000`;
  - representative periods: 0.65536 s at 400 MSPS, 1.31072 s at
    200 MSPS, 2.097152 s at 125 MSPS, and 1342.17728 s at
    `DATA_SAMPLE_RATE_MIN`.
- Static search finds no stale 996 MiB capacity or its derived sample/period
  values in active KnowledgeBase documents.
- Working-tree and staged `git diff --check` both pass. Only expected Windows
  line-ending conversion warnings are reported.
- No build or runtime execution was performed.
