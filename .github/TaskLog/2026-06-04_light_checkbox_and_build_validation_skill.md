# Light Checkbox And Build Validation Skill

## Scope

- Restore a light-theme-specific checked checkbox icon for `DigitalPanel::defaultTrimDownload`.
- Keep shared checkbox resources in `src/libs/controls`, not in unrelated plugin qrc namespaces.
- Add a repo-local skill documenting the preferred SGStudio build validation workflow.

## Local Hypotheses

1. The preferred light-theme checkmark color is the previous light checkbox asset already used by the global light stylesheet, but that asset currently lives under an unrelated plugin qrc namespace.
2. The minimal repo-consistent fix is to copy the light checkbox assets into `Controls` shared resources and select them in `DigitalPanel` based on `ThemeManager`.
3. For this repo, code-validation build guidance should default to incremental target builds on the existing `build/cmake-win-debug` tree, escalating to the workspace Debug build task only when shared libs / app / packaging surfaces are touched.

## Planned Change

1. Add `checkbox_checked_light` and `checkbox_unchecked_light` resources to `src/libs/controls/controls.qrc`.
2. Add the corresponding SVG files under `src/libs/controls/resource/image/`.
3. Update `digitalpanel.cpp` to choose dark/light checkbox resources from `Controls` based on the current theme.
4. Create a workspace skill under `.github/skills/` that explains when to use plugin-target incremental build vs full Debug build, and how to monitor long-running builds asynchronously.

## Validation

- Run an incremental Debug build for the affected dependency chain using the existing build tree.
- Prefer the narrowest viable target build; if `Controls` is touched, allow the dependent plugin target build to rebuild it.