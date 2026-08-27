# Light Checkbox Geometry Align To Dark

## Scope

- Only adjust the light-theme checkbox SVG resources under `src/libs/controls/resource/image/`.
- Keep `DigitalPanel` QSS size unchanged.
- Make light checkbox geometry match dark checkbox geometry; only colors stay light-theme specific.

## Local Hypothesis

The oversized appearance is caused by the current light SVG geometry itself, not by `QCheckBox::indicator` width/height. The current light assets use a much larger outer border and thicker checkmark path than the dark assets.

## Planned Change

1. Rebuild `checkbox_checked_light.svg` using the same border and check geometry as dark.
2. Rebuild `checkbox_unchecked_light.svg` using the same border geometry as dark.
3. Preserve light-theme colors: brown border and blue checkmark.

## Validation

- Do a focused diff review after editing the two SVG files.