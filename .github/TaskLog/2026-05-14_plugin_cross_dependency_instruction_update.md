# Plugin Cross Dependency Instruction Update

## Scope

- Update repository instructions to forbid unstable cross-plugin runtime dependencies.
- Persist the lesson in repository memory so future work avoids the same pattern.

## Local Hypothesis

- In this plugin architecture, plugin load order and even plugin load success can differ across machines because deployment completeness and environment differ.
- Therefore a plugin must not depend on another unrelated plugin's qrc resources, singleton initialization, or other runtime side effects unless there is an explicit plugin dependency and shared ownership contract.

## Planned Change

1. Add a hard rule to `.github/copilot-instructions.md` near the runtime plugin architecture guidance.
2. Extend the existing repo memory note from the `DigitalPanel -> Updater` incident into a general rule plus concrete example.

## Validation

- Run diagnostics on the touched instruction file after editing.
- Re-read the updated memory note to confirm the rule is recorded.