# 2026-06-24 GPIO Dialog QSS And LabelButton Press Cleanup

## Scope

- Move the GPIO dialog's per-button `LabelButton::setStyleSheet(...)` styling into theme QSS.
- Remove the shared `LabelButton` press-time stylesheet override that was kept only to preserve that GPIO-local stylesheet path.
- Mirror the same scoped `GpioDialog LabelButton` QSS into the other theme source directories under `configuration_files/`.
- Keep the change minimal and avoid unrelated UI restyling.

## Verification Level

- `static`

## Observations

- `src/plugins/core/gpiodialog.cpp` is included by `src/plugins/core/CMakeLists.txt`.
- `src/libs/controls/labelbutton.cpp` is included by `src/libs/controls/CMakeLists.txt`.
- `GpioDialog` currently calls `button->setStyleSheet("LabelButton { ... background-color:#2D343A; ... }")` during button initialization.
- Repository-wide search shows this is the only remaining direct per-widget `setStyleSheet(...)` use on `LabelButton`.
- `LabelButton::mousePressEvent()` / `mouseReleaseEvent()` currently save and restore a widget-local stylesheet; `git show 107b1c27` shows the June 11 change was specifically to avoid losing externally injected width/font rules during press.
- Runtime theme loading uses `configuration/theme.css` and `configuration/theme_light.css`.
- User-provided build knowledge for this task says those runtime files are copied from `configuration_files/standard_cn/` and overwritten by the build flow, so `configuration_files/standard_cn/theme.css` and `configuration_files/standard_cn/theme_light.css` are treated as the source of truth here.
- Other theme source directories that also carry `theme.css` / `theme_light.css` are `standard_en`, `standard_ru`, `neutral_cn`, and `neutral_en`.

## Inferences

- Once the GPIO dialog stops injecting per-widget QSS, the shared `LabelButton` press override no longer has a remaining in-repo caller to protect.
- To let GPIO buttons use their normal `checked` visual state again, the dialog-local base background must move into theme QSS with matching scoped `:checked` rules so the scoped base rule does not override checked styling by selector specificity.

## Success Criteria

1. `gpiodialog.cpp` no longer sets a widget-local stylesheet on `LabelButton`.
2. `LabelButton` no longer overrides press/release events just to save and restore widget-local QSS.
3. `configuration_files/standard_cn/theme.css` and `configuration_files/standard_cn/theme_light.css` both contain scoped `GpioDialog LabelButton` styling with:
   - base dark background `#2D343A`
   - base light background `#D2CBC5`
   - width/font rules previously carried by the inline stylesheet
   - scoped `:checked` overrides so checked visuals are still available
4. The same scoped `GpioDialog LabelButton` rules are mirrored into every other theme source directory that ships both `theme.css` and `theme_light.css` under `configuration_files/`.

## Non-Goals

- No build or runtime verification in this task.
- No broader cleanup of unrelated theme drift between generated runtime copies and `configuration_files/standard_cn/`.
