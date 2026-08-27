# 2026-05-14 Arb wav padding and capability doc sync

## Background
- ArbModulationOnly was recently simplified: the legacy device thread path was removed and ArbPanel load behavior changed to replace the current file directly.
- The existing KnowledgeBase Arb summary still describes the removed ProgrammedArb device-thread bridge path and old Load/Unload UI behavior.
- The user wants confirmation that ordinary WAV playback still uses the same short-waveform padding rule as IPlaybackBusiness, and wants the current capability-model decision recorded.

## Local hypothesis
- OrdinaryWav still reaches the same short-waveform padding rule because ArbDataGenerator calls Core::IPlaybackBusiness::normalizePlaybackPayloadForDownload() before ArbModulationOnly snapshots provider payload for runtime.
- The right documentation target is the existing KnowledgeBase Arb summary, not a new doc, because the requested content is a state correction of the current Arb architecture.

## Evidence to verify
- Check Core::IPlaybackBusiness::normalizePlaybackPayloadForDownload() for the canonical padding rule.
- Check ArbDataGenerator ordinary-wav handling for a direct call into that helper.
- Check ArbModulationOnly buildPlaybackExecutionContext() to confirm it forwards the generated IQ payload into the core-managed runtime path.
- Update the existing Arb KnowledgeBase doc so it matches the current code: no deviceThread path, ordinary WAV padding is shared with IPlaybackBusiness, load replaces current file, and ProgrammedArb capability flags remain intentionally unchanged until runtime integration.

## Planned edits
- Update .github/KnowledgeBase/arb_mode_summary.md to reflect the current implementation boundary and the deferred capability-model change.
- Leave code untouched unless the verification step reveals a contradiction.

## Validation
- Run file diagnostics for the edited markdown document.
- No build/run requested.
