# Digital Default Trim Light Indicator Fix

## Scope

- Only adjust the local checkbox styling for `DigitalPanel::defaultTrimDownload`.
- Do not change shared/global checkbox styling.
- Target the light-theme inconsistency where the indicator shows an extra colored box.

## Local Hypothesis

The extra box is caused by the checkbox indicator still allowing native border/background painting in light theme. The local QSS sets only `image`, `width`, and `height`, so the platform indicator can bleed through behind the custom SVG.

## Root Cause Update

The actual mismatch comes from the global light-theme stylesheet, not from native checkbox fallback. `configuration/theme_light.css` applies a generic `QCheckBox::indicator` `border-image` to all checkboxes. The local `DigitalPanel` stylesheet uses `image` instead of `border-image`, so in light theme both properties participate in painting: the global light checkbox frame remains, and the local indicator image is drawn on top. Dark theme does not define the same generic checkbox rule, so the issue only appears in light theme.

## Planned Change

1. Keep the existing local theme-aware text color update path.
2. Override the light-theme global checkbox `border-image` in the local `DigitalPanel` stylesheet by switching the local indicator states to `border-image` as well.
3. Validate the touched source file with diagnostics only.

## Validation

- Run file diagnostics for `src/plugins/analog/digitalpanel.cpp`.