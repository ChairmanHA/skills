# Digital Checkbox Use Controls Resources

## Scope

- Only change `DigitalPanel` checkbox indicator resource paths.
- Stop depending on updater plugin resources for `Default Trim Download`.
- Keep the existing local stylesheet structure and theme text-color update path.

## Local Hypothesis

- `DigitalPanel` should use resources owned by `Controls`, not `Updater`.
- Replacing `:/Custom/Image/checkbox_*` with `:/Controls/Image/checkbox_*` removes the cross-plugin runtime dependency without affecting the local QCheckBox sizing logic.

## Planned Change

1. Update `digitalpanel.cpp` to point the checked and unchecked indicator URLs to `:/Controls/Image/checkbox_checked` and `:/Controls/Image/checkbox_unchecked`.
2. Remove the now-unused light-theme resource branch while preserving theme-driven text color updates.

## Validation

- Run file diagnostics on the touched source file after editing.