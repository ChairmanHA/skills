# Digital Default Trim Checkbox Style Fix

## Scope

- Only adjust the `DigitalPanel` checkbox presentation for `Default Trim Download`.
- Keep the style local in code.
- Fix three visible issues together: indicator too small, left offset, and light-theme text becoming invisible.

## Local Hypothesis

The incorrect appearance is caused by the inline stylesheet in `digitalpanel.cpp`, not by the `.ui` layout:

- `padding-left: 18px` shifts the whole checkbox content right.
- `color: #ffffff` forces white text even in light theme.
- `indicator` size `18x18` is visually too small next to adjacent 60px-high controls.

Using the current `ThemeManager` text color in the inline style, removing the extra left padding, and enlarging the indicator should fix the issue without touching layout structure or global QSS.

## Planned Change

1. Import `ThemeManager` in `digitalpanel.cpp`.
2. Replace the hard-coded checkbox stylesheet with a small local lambda that:
   - pulls `ThemeManager::textColor()`
   - sets the checkbox text color from the active theme
   - removes the extra left padding
   - enlarges the indicator
3. Reapply the style on `themeChanged` so runtime theme switching stays correct.

## Validation

- Run file diagnostics on the touched source file after editing.

## Follow-up Finding

- Qt `QCheckBox::indicator` 的 `width/height` 本身是支持的，问题不在于 Qt 不支持改大小。
- 当前 `DigitalPanel` 在暗色主题下仍主要走 native indicator 绘制；仅设置 subcontrol 的宽高，只会放大 indicator 所占空间，不一定放大 native 勾选图形本身。
- 要让视觉尺寸稳定生效，需要像 Qt 文档示例那样一起接管 `QCheckBox::indicator:checked/:unchecked` 的图像绘制，避免继续依赖 native indicator 尺寸。