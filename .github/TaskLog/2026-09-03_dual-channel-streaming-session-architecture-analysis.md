# Dual-channel Streaming session architecture analysis

## Scope

- Explain what a Core-coordinated, independently long-lived Streaming session means in the current SGStudio architecture.
- Separate the current A/B topology of two independent H2 device sessions from a hypothetical single H2 device exposing two TX channels.
- Assess whether USB or ETH could support two concurrent realtime streams using only evidence available in the repository and the H2 API surface.
- Define the minimum software architecture and the API/firmware behavior that would be required to support two concurrent Streaming sessions safely.

## Verification level

`static`

No build, runtime test, hardware test, or code implementation is requested.

## Assumptions and evidence policy

- Current implementation facts come from active CMake inputs, current source, the checked-in H2 API v2.0.34 headers, and current KnowledgeBase documents.
- The current A/B ETH endpoints (`5000` / `5001`) are treated as two independent `FancyDevice` / H2 device sessions, not as two channels returned by one `device_open_*()` call.
- A single-device/two-channel design is hypothetical until hardware/API tests confirm channel count, concurrent `TX_REALTIME`, trigger independence, transport bandwidth, and error isolation.
- Transport feasibility will be expressed as a bandwidth budget and required contract, not inferred merely from the presence of `channel[]` parameters in the API.
- Observed behavior, source-backed conclusions, and proposed API assumptions will be labeled separately.

## Success criteria

- Identify every current singleton/global ownership boundary that prevents two concurrent Streaming sessions.
- Describe a target model in which Core owns routing, lifecycle arbitration, transition barriers, shared-device policy, and status, while each channel session owns continuous data production/sending.
- Give separate execution models for two independent devices and one device with two channels.
- Specify the API questions and expected behavior for concurrent streams, including handles, locking, start/stop, trigger, backpressure, cancellation, errors, and aggregate throughput.
- Provide a staged migration and hardware validation matrix without proposing speculative fallback behavior.

## Investigation plan

1. Confirm the current active source files through CMake and inspect DeviceManager, BusinessManager, TxSessionService, StreamingBussiness, StreamingDataGenerator, DeviceOperator, and FancyDevice ownership/routing.
2. Inspect `device_open_usb` / `device_open_eth`, returned `channel[]`, `tx_config_stream`, `tx_send_stream`, trigger, stop, and capability API declarations.
3. Reconcile current KnowledgeBase guidance with the superseded two-device TaskLogs and the current retained-device switching model.
4. Derive transport budgets for representative complex16 sample rates and identify the measurements required for USB and ETH.
5. Produce the architecture recommendation, API contract assumptions, risks, and staged verification plan.

## Static findings

### Current software boundary

- The active HTRA target includes `streamingbussiness`, `streamingdatagenerator`, and `fancydevice` through `src/plugins/htra/CMakeLists.txt`.
- `StreamingBussiness` is registered once and owns one generator, one queue, one sender thread, one bridged carrier plan, and one set of progress/lifecycle state.
- `BusinessManager` has one global `activedBusiness`; activating a new business terminates the previous one.
- `TxSessionService` owns one selected business, one carrier plan, one runtime, and one applied request.
- `DeviceOperator` resolves `DeviceManager::currentDevice()` on every configuration and realtime send. A second sender cannot safely use this path because changing the UI current device can redirect a later frame.
- `FancyDevice` stores the full channel array returned by H2 but selects one `primaryTxChannel`; all TX paths use it. Manual ETH deliberately requests one channel. USB requests the discovered count but still binds only the first usable TX channel.
- Current A/B ETH behavior is the superseding 2026-08-12 retained-handle design: port 5000 and port 5001 are two independent H2 device handles and UIDs, with one UI/current-device control focus. It explicitly stops Streaming before switching and does not support two concurrent streams.

### H2 API surface

- The header supports up to eight channels, reports `device_info.chn`, returns `channel[]`, assigns each channel a number/type/`max_streams`, and queries TX capabilities per channel.
- `device_open_usb` and `device_open_eth` accept a requested channel count and return a channel array. ETH multi-channel discovery is ambiguous because there is no separate output count; the only actual count is `device_info.chn`.
- TX configuration, trigger, start, stop, carrier, and realtime send APIs are channel-addressed.
- Device preset, clock, power, GNSS, trigger-out, open, and close are device-scoped.
- Although `channel.max_streams` exists, current `tx_config_stream`, trigger, query, and send documentation fixes or normalizes the stream index to stream 0. The supported first target is therefore one realtime stream per TX channel, not multiple streams on one channel.
- The current header does not define same-handle thread safety, buffer ownership after `tx_send_stream` returns, partial acceptance, bounded blocking/cancellation, per-stream underrun status, or aggregate simultaneous-channel bandwidth.

### Transport budget

For complex16 IQ, payload rate is `4 * sampleRate` bytes/s per stream before protocol overhead.

- Two streams at 7.8 MSps require about 62.4 MB/s / 499.2 Mb/s raw in aggregate.
- One 62.5 MSps stream requires 250 MB/s / 2 Gb/s raw.
- Two 62.5 MSps streams require 500 MB/s / 4 Gb/s raw.
- Therefore the presence of two channels cannot imply transport feasibility. Two 7.8 MSps streams may fit a healthy 1 GbE path, while one or two 62.5 MSps streams cannot fit 1 GbE. USB feasibility depends on negotiated generation, host-controller topology, driver efficiency, and the device/FPGA aggregate ingress limit.

## Proposed architecture boundary

### Target identity

Use a stable endpoint identity such as:

```text
TxEndpoint = { deviceSessionId/UID, channelNumber, streamNumber=0, openEpoch }
```

This represents both topologies without conflating them:

```text
Two independent devices:  A={uidA,ch0,s0}, B={uidB,ch0,s0}
One dual-channel device:   A={uidX,ch0,s0}, B={uidX,ch1,s0}
```

UID/revision validate a target; they must not be used as late-bound routing through a global current pointer.

### Ownership

- UI/business objects remain a single set of editors for the selected endpoint.
- Core owns a registry of per-endpoint desired/applied state and long-lived Streaming sessions.
- One `StreamingSession` exists per active endpoint. It owns its immutable settings snapshot, generator, bounded queue, sender/data-plane loop, progress, fault, and session generation; it does not own UI widgets.
- Core owns target routing, start/rearm/stop transitions, shared-device barriers, transport admission, result correlation, and application/device shutdown.
- A physical-device coordinator owns shared resources and the open handle. Channel-local control is routed to the selected channel; device-wide changes stop or barrier every affected channel.

### State machine and planes

```text
Stopped -> Starting -> Running -> Rearming -> Running
                    \-> Stopping -> Stopped
                    \-> Faulted
```

- Control plane: serialized configuration/start/stop and request correlation. Channel-local work may leave the other channel running only when the API contract guarantees isolation.
- Data plane: one independent bounded queue and sender per channel when the SDK is reentrant for same-handle sends. Long-running sends must not run in the single control worker.
- If the SDK is not reentrant, a single fair device data pump is acceptable only with a nonblocking/partial-send API. Serializing potentially blocking `tx_send_stream()` calls behind one mutex is not a reliable dual-stream design because it introduces head-of-line underrun.
- Close/reset/device-wide configuration uses a barrier: reject new work, stop all affected sessions, wait until senders leave the API call, stop channels, then close/reset and advance the epoch.

### Current classes that must change

1. Split physical-device/session ownership from TX-channel execution, or introduce an explicit channel endpoint abstraction backed by a shared device session.
2. Add `TxEndpoint` to apply commands, Streaming commands, status, progress, errors, and results.
3. Replace late `currentDevice()` lookup in `DeviceOperator`/executors with an endpoint-bound lease/token.
4. Replace singleton execution state in `TxSessionService` with per-endpoint contexts and generations. UI selection remains a view concern only.
5. Remove Streaming execution dependence on global `IBusiness::isActive()`. `StreamingBussiness` becomes an editor/provider adapter; Core's session registry is the runtime truth source.
6. Move `FancyDevice`'s primary-channel state (`mode`, sample rate, trigger, errors, sweep state, capabilities) into per-channel state. Retain device-wide clock/power/GNSS/lifecycle state above it.
7. Replace the single instance mutex with documented lock scopes: device lifecycle/shared control, channel control, and channel stream-send serialization. Exact concurrency follows the SDK contract, not assumptions.
8. Add aggregate transport admission by physical link/resource group. Reject unsupported rate combinations explicitly; do not silently lower sample rates.

## API/firmware contract required for dual Streaming

1. Open semantics: `chs` must be defined as exact request or output-array capacity; actual returned count and stable channel numbering must be unambiguous for USB and ETH.
2. Concurrency: document whether two threads may call `tx_send_stream(ch0,0,...)` and `tx_send_stream(ch1,0,...)` concurrently on the same device handle, and whether independent handles are reentrant.
3. Buffer contract: success must mean the complete buffer was copied/accepted; pointer lifetime after return, maximum/aligned point count, timeout, and warning semantics must be explicit. Otherwise add an extended send API returning accepted points.
4. Stop/cancel: `channel_stop(ch0)` must have a bounded way to stop/cancel channel 0 sending without stopping channel 1. A blocked send must be released or an explicit cancel API is required.
5. Isolation: channel 0 carrier/stream/trigger/output/start/stop operations must state exactly which channel 1 state they can disturb. Shared LO, clock, power, fan, GNSS, preset, and calibration operations must be identified as device-wide.
6. Capacity: expose both per-channel realtime limits and an aggregate simultaneous-channel rate/transport matrix. Per-channel maxima alone are insufficient.
7. Status: provide per-channel/per-stream queued depth, underrun, overflow, accepted/sent point counters, and fault scope; distinguish channel fault from device/bus disconnect.
8. Triggering: independent trigger behavior must be documented. Deterministic simultaneous/coherent start requires a group arm/start/trigger or timestamped-frame contract; two sequential `channel_start` calls are not a coherence guarantee.
9. Shared-device lifecycle: define whether device-level query/configuration calls are safe during any active realtime stream and which calls require all streams stopped.

## Initial product-level conclusion (superseded for short-term planning)

The following records the initial dual-Streaming analysis. The later explicit Level 1 product-direction decision supersedes its recommendation about which capability should be implemented first.

- Current SGStudio cannot safely run two Streaming sessions, regardless of whether the hardware could.
- Two independent A/B devices are the lower-risk first dual-stream target because their handles and instance locks are separate; remaining unknowns are SDK cross-handle reentrancy and shared physical-link bandwidth.
- One physical dual-channel device is supported by the H2 object model in principle, but needs a physical-device/channel split in SGStudio and stronger SDK contracts for same-handle concurrency, aggregate capacity, isolation, and synchronization.
- A Keysight-like two-channel product must define whether the requirement is merely two independent outputs, simultaneous start, or phase/sample-coherent MIMO. The current H2 interface can plausibly express independent channel control, but it does not by itself establish coherent group execution.

## KnowledgeBase integration (2026-09-03)

- Updated `KnowledgeBase/streaming_bridge_rearm_reboot_refactor_plan.md` as the existing durable architecture owner; no overlapping KnowledgeBase document was created.
- Preserved the 2026-03-31 hardware-backed decision to defer fixed-stream level live retune, and clarified that this deferral does not block endpoint-bound session extraction or multi-session coordination.
- Added the semantic-vs-legacy exception distinction, stable `TxEndpoint` identity, Core/session/device ownership, current A/B versus true dual-channel topology, H2 contract gaps, transport budget, session API behavior, Keysight capability-level comparison, and staged implementation/test order.
- Updated `KnowledgeBase/Index.md` so the existing document description exposes the new dual-Streaming scope.
- Verification remained static: reviewed document structure, terminology, links, and consistency with current source-backed findings; no source code, build, runtime, or hardware behavior was changed.

## Product-direction update: Level 1 Dual RF Channel first (2026-09-03)

### User-provided deployment facts

- The target device is connected to the computer through USB; realtime Streaming IQ uses that USB transport, in a form comparable to VSG60.
- The same device family can also be connected to the computer through Ethernet, whose available realtime I/O throughput is lower than the USB path in the current product context.
- At 62.5 MSps, one complex-int16 Streaming path carries 250 MB/s of raw IQ payload.
- At that load, the USB 3.0 link is already heavily occupied and useful operation of a second channel is difficult.
- Field behavior further confirms that while one channel is Streaming, downloading a Playback waveform or changing configuration on the peer channel affects the active stream. This is treated as a shared transport/SDK/device-control limitation, not dismissed because command payloads are small.

These are treated as authoritative product/deployment constraints. The earlier recommendation to prioritize two concurrent Streaming sessions is superseded for the short-term product plan.

### Revised scope and decision

1. Make **Level 1: Dual RF Channel** the short-term product target.
2. Define Level 1 as two simultaneously usable RF/vector endpoints for device-autonomous modes, with one shared host-realtime Streaming ingress and at most one active Streaming session per physical device, whether the active transport is USB or ETH.
3. Keep explicit per-channel identity/state and remove late `currentDevice()` routing, but model the realtime ingress as an exclusive physical-device resource with capacity one.
4. Specify the expected mixed-mode behavior (`Streaming + CW/Playback`) through a capability/concurrency matrix; do not infer it from channel count.
5. Preserve dual Streaming as a future Level 2 target that requires a higher-bandwidth or multi-transport hardware design plus SDK/firmware concurrency and isolation contracts.
6. Define the minimum Level 1 SDK contract for channel discovery/identity, per-channel configuration and status, shared realtime-ingress admission, mixed-mode combinations, bounded control latency, stop/fault isolation, device-wide barriers, and transport-specific capability reporting.

### Updated success criteria

- The KnowledgeBase must no longer describe dual Streaming on current A/B or a true dual-channel device as the preferred near-term implementation phase.
- It must explain why two 62.5 MSps streams require 500 MB/s raw before transport overhead and are not a credible USB 3.0 Gen1 target; the lower-throughput ETH path makes the same conclusion stronger and may not support even one 62.5 MSps uncompressed stream.
- It must describe the most plausible Level 1 hardware partition: two RF/DAC/output paths and device-resident playback/control state, sharing one USB/ETH transport controller and one routable realtime DMA/FIFO ingress.
- It must define deterministic API admission: a second Streaming start returns an explicit shared-resource conflict and never silently stops the first stream.
- It must preserve the field-backed conclusion that peer download/configuration affects an active stream even when the individual command payload is small; the baseline therefore keeps the peer channel in a preconfigured autonomous state during Streaming.
- For the current product baseline, peer-channel Playback download and configuration mutation while Streaming is active must be rejected with `RequiresQuiesce`/resource-conflict semantics. Reconfiguration uses an explicit stop-configure-rearm device transaction and reports the Streaming interruption/generation change.
- Index text and final recommendations must expose Level 1 as the short-term target and Level 2 as conditional future work.
- Transport capability must be runtime/profile driven (`USB` versus `ETH`, negotiated/measured bandwidth), not a fixed assumption that all connections expose the same Streaming sample-rate domain.

### Verification level

`static`

No source implementation, build, runtime test, USB measurement, or hardware change is included in this update.

### KnowledgeBase update completed

- Revised the durable design from “dual Streaming as the first multi-channel target” to “Level 1 Dual RF Channel first”.
- Added USB/ETH transport-specific capability, the 250 MB/s single-stream and 500 MB/s dual-stream raw budgets at 62.5 MSps, and the lower-throughput ETH constraint.
- Defined the likely Level 1 hardware partition: two device-autonomous RF/baseband output paths, device-resident Playback resources, and one shared/routable realtime DMA/FIFO ingress.
- Defined the Level 1 SDK/API contract: per-channel identity/state, simultaneous-output matrix, exclusive realtime-ingress lease, `ResourceBusy` for a second stream, streaming freeze window, `RequiresQuiesce` for peer mutation, explicit stop-configure-restart transaction, transport-specific rate profiles, and observable fault/state semantics.
- Updated the KnowledgeBase index description. Verification remained static.
