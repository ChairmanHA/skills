# Digital Large Waveform Checkbox Implementation

## Scope

- Focus only on Digital Modulation.
- Add a panel-level checkbox for the default large-waveform download policy.
- Wire the checkbox state through the business layer so the user can choose between:
  - default trim-and-download directly
  - prompt before trim-and-download
- Do not implement the large-waveform save progress flow in this change.

## Local Hypothesis

The current large-waveform decision point is already local to `DigitalModulation` on the PN and Oversample edit paths. A minimal, correct implementation is to keep the existing trim request backend and add one UI-driven branch:

- checked: call `requestGenerateAndTrim(...)` directly
- unchecked: show a Digital-specific confirmation prompt, then call `requestGenerateAndTrim(...)` only after confirm

This should preserve the existing generate -> status changed -> download flow without changing other modulation types.

## Planned Changes

1. Add a `QCheckBox` to `digitalpanel.ui` and style it locally to match the current dark panel rhythm.
2. Expose checkbox read/write and change notification from `DigitalPanel`.
3. Store the preference in `DigitalModulation` and use it in the PN / Oversample large-waveform branches.
4. Replace the Digital large-waveform prompt path with a Digital-specific trim confirmation dialog that removes the stream option and leaves cancel as the only alternate action.
5. When trim-and-download is chosen, compute a capped `SymbolLength` from `MAXDOWNLOADSIZE / (samplesPerSymbol * 4)` and generate only that download segment instead of the full `2^PN` sequence.
6. Build Debug to validate the touched slice.

## Non-Goals

- No full-waveform export rework.
- No progress dialog for save.
- No changes to Ramp / OFDM / other modulation large-waveform prompts.

## Follow-up

- Remove the `SystemBusyStatus` updates from the large-waveform trim / handover path so DeviceInfoWidget no longer shows the transient `Generating Waveform...` warning during these requests.
- Fix the trim-complete path so default large-waveform trimming does not auto-enable Digital modulation when the business is currently inactive; only refresh device download if the business is already active.
- If Digital already holds trimmed waveform data and the user later turns off the default trim checkbox, require the same confirmation prompt before keeping or re-enabling that trimmed playback, but reuse the existing cached data instead of regenerating it.
- Update KnowledgeBase documentation to summarize the current Digital-specific large-waveform behavior: what changed relative to the common Generate & Stream flow, how the Digital prompt logic now works, and how trimmed generation derives `SymbolLength` from `MAXDOWNLOADSIZE` including the FSK-specific size model.
- Add a leadership-facing Markdown summary that separates delivered behavior, pending save-path work, and the current Digital large-waveform flow in a single page with Mermaid flowchart.
- Replace the leadership flowchart with a simpler UI-only SVG so the report page focuses on visible user decisions instead of backend generation steps.
- Extend the leadership summary with the target Save IQ flow: reuse the cache only when it already represents the full waveform, otherwise show a fake progress dialog and regenerate the full `SymbolLength = 2^PN` waveform before saving.
- Add a higher-level Digital modulation interaction-flow document that describes the whole business from the two user intents (playback vs save), including async regeneration, enable-driven auto download, large-waveform trim interaction, and the target Save IQ full-waveform flow.
- Convert the high-level Digital interaction SVG into Mermaid, embed the Mermaid source in the Markdown doc, and keep a standalone `.mmd` source alongside it so the flow can be imported and edited in draw.io without rebuilding SVG by hand.
- Reduce Digital large-waveform memory pressure by removing the panel-side full-waveform preview cache, switching the preview path to pass only a small `int16` fragment, and letting Digital playback download reuse raw `int16` IQ data instead of converting `int16 -> float -> int16` on every apply.
- Update KnowledgeBase documentation to record the new Digital playback/preview memory-management hard constraints: one raw `int16` main cache, preview only keeps a small fragment, and Digital mainline download/save paths must not regress to full-waveform preview caches or meaningless `int16 <-> float` round trips.
- Refine the Digital memory-management documentation so the constraints match the real download path: still forbid full-size preview caches and meaningless full-waveform `int16 <-> float` round trips, but explicitly allow the bounded short-waveform padding copy required by `IPlaybackBusiness::normalizePlaybackPayloadForDownload(...)`.
- Remove the obsolete direct playback apply path from the current Analog provider base: `buildPlaybackExecutionContext -> TxSessionService -> TxPipelineRuntime` is now the only active download/configuration path for included Analog modulation providers, so the old `IPlaybackBusiness::deviceThread` and `AnalogPlaybackBusiness::onDeviceProfileChanged()` flow should be deleted instead of left as dead parallel infrastructure.