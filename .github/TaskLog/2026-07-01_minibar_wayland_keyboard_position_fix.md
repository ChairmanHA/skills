# Minibar Wayland Keyboard Position Fix

Date: 2026-07-01

## Scope

Fix the initial popup position of `TouchNumKeyboard` when opened from `MiniBarWindow` under Linux Wayland layer-shell mode.

## Verification Level

static

No build or runtime verification is planned unless explicitly requested.

## Observations

1. Main-window mode positions the keyboard near the clicked button.
2. Wayland minibar mode uses the `OverlayInScreenHost` path because the minibar host is too small to contain the keyboard.
3. `TouchNumKeyboard::initialOverlayPosition()` currently relies on `anchorWidget->mapToGlobal(...)` and `parent->mapFromGlobal(...)`.
4. Under layer-shell surfaces, regular QWidget global coordinate mapping is not a reliable source of the compositor-applied visual position.
5. `MiniBarWindow` already keeps the intended visual top-left in `m_collapsedTopLeft` and applies it to layer-shell margins.

## Inference

The keyboard appears near the far left because the screen-overlay path is deriving its anchor from unreliable Wayland/layer-shell global mapping rather than from the minibar's stored visual position.

## Success Criteria

1. Main-window and Win32/non-Wayland top-level keyboard positioning remain unchanged.
2. Wayland minibar small-host keyboard positioning uses the minibar's stored visual top-left when available.
3. The keyboard opens to the left of a minibar on the right side of the screen, or to the right of a minibar on the left side.
4. The final position remains clamped inside the current overlay/screen bounds.
5. Changes stay localized to `MiniBarWindow` and `TouchNumKeyboard`.

## Implementation Plan

1. Add private dynamic property names for a layer-shell visual top-left and visual size.
2. In `MiniBarWindow::applyLayerShellGeometry(...)`, update those properties from the same top-left/size used to compute margins.
3. In `TouchNumKeyboard::initialOverlayPosition()`, when using `OverlayInScreenHost`, try to build a visual anchor rect from:
   - owner window dynamic visual top-left/size properties,
   - anchor widget coordinates relative to that owner window.
4. Choose left or right of the owner/anchor based on available horizontal space and screen half.
5. Fall back to the existing global-map path when the visual properties are absent or invalid.

## Implementation Update

Files changed:

1. `src/plugins/core/minibarwindow.cpp`
2. `src/libs/controls/touchnumkeyboard.cpp`
3. `.github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md`

Implemented:

1. `MiniBarWindow::applyLayerShellGeometry(...)` now writes the layer-shell visual top-left and size to dynamic properties before the unchanged-margin guard.
2. `TouchNumKeyboard::initialOverlayPosition()` now has an `OverlayInScreenHost`-only path that reads those dynamic properties, reconstructs the visual anchor rect, and places the keyboard beside the minibar.
3. The side selection prefers the side away from the screen edge:
   - right-side minibar -> keyboard on the left,
   - left-side minibar -> keyboard on the right.
4. The final position is still clamped to the screen/overlay bounds.
5. Main-window mode and top-level fallback positioning remain on the existing path.
6. The KnowledgeBase now records why layer-shell minibar keyboard positioning should not rely on regular global QWidget mapping.

Verification:

1. `git diff --check -- src/libs/controls/touchnumkeyboard.cpp src/plugins/core/minibarwindow.cpp .github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md`

Build/runtime verification not run per repository default workflow.

## Documentation Follow-Up

User requested that the key discovery be made explicit in the durable document:

1. Under Wayland + layer-shell, the minibar's true screen position comes from compositor-applied layer-shell margins.
2. Regular QWidget `mapToGlobal(...)` / `mapFromGlobal(...)` is not a reliable source of the final visual position for layer-shell surfaces.

Documentation update:

1. Promoted this finding to an explicit "important finding" section inside `.github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md`.
2. Added a maintenance rule: use the minibar's layer-shell visual state for Wayland layer-shell positioning decisions, while leaving main-window / Win32 / X11 fallback on normal QWidget global mapping.
