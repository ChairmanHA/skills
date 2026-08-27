# Minibar Optimistic Enabled Skill

## Scope

- Add a repository-local skill for minibar controls whose visual enabled/checked state follows remote business state.
- Cover top-level minibar buttons, paired collapsed/expanded buttons, Sweep-style switches, and modulation editor Enabled controls.
- Preserve navigation-only semantics for buttons such as the top-level MOD menu button.

## Design

- Keep main/property/business/runtime as the authoritative state owner.
- Require immediate local UI intent rendering for user-edited enabled state.
- Keep one request in flight per logical resource and coalesce later edits to the latest intent.
- Correlate request completion by resource; old RSP/EVT data may update the authoritative cache but must not overwrite active UI intent.
- Reconcile only when authoritative state differs, or on rejection/timeout/transport failure without a newer intent.
- Keep visual carrier/QSS decisions in the existing `labelbutton-style-workflow`; this skill owns asynchronous state timing and request correlation.

## Planned files

- `.github/skills/minibar-optimistic-enabled-workflow/SKILL.md`
- `.github/skills/minibar-optimistic-enabled-workflow/agents/openai.yaml`

## Success criteria

1. Skill metadata triggers for future minibar enabled-button additions and flicker/latency fixes.
2. Instructions explicitly cover modulation editor Enabled buttons.
3. Instructions distinguish navigation clicks from enabled-state edits.
4. Instructions include latest-intent serialization, resource-specific response handling, differential rendering, and failure reconciliation.
5. The standard skill validator passes.

## Verification

- Level: static documentation validation.
- Run `quick_validate.py` for the new skill.
- Inspect generated UI metadata and relative KnowledgeBase link.

