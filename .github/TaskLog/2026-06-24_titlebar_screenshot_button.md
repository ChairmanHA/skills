# Main Window TitleBar Screenshot Button

## Scope

Add a screenshot entry to the main-window title bar using the icons already placed under `src/plugins/core/resource/image/`, and save captures into the runtime-root sibling `images/` directory.

Touched area:

- `src/plugins/core/mainwindow.cpp`
- `src/plugins/core/mainwindow.h`
- `src/plugins/core/core.qrc`
- `configuration_files/standard_cn/theme.css`
- `configuration_files/standard_cn/theme_light.css`
- `configuration_files/standard_en/theme.css`
- `configuration_files/standard_en/theme_light.css`
- `configuration_files/standard_ru/theme.css`
- `configuration_files/standard_ru/theme_light.css`
- `configuration_files/neutral_cn/theme.css`
- `configuration_files/neutral_cn/theme_light.css`
- `configuration_files/neutral_en/theme.css`
- `configuration_files/neutral_en/theme_light.css`
- `.github/KnowledgeBase/titlebar_menubar_outputmode_and_overflow_behavior.md`

## Verification Level

`static`

The request is for a UI wiring and file-save path change. No build or runtime verification was requested.

## Observations

- `MainWindow` currently creates `Single / Continue` inside an `outputModeWidget`, then hands that whole widget to `TitleBar::setOutputModeWidget()`.
- `TitleBar` already documents a boundary where it hosts right-of-menubar business controls, while `MainWindow` owns their creation, actions, and business semantics.
- The title-bar business controls are styled from `configuration/theme.css` and `configuration/theme_light.css` using stable object names such as `btnSingle` and `btnContinue`.
- `src/plugins/core/resource/image/` already contains two screenshot assets: `screenshot.png` and `scrreenshot_light.png`.
- Existing runtime-relative save flows commonly use `yyyyMMdd_HHmmss` timestamps, but the requested screenshot directory is specifically the sibling `images/` folder next to `bin/`.

## Inference

The smallest change is to keep the screenshot entry in the same `outputModeWidget` host area as the existing output-mode buttons. That preserves the current `TitleBar` ownership boundary and avoids introducing another title-bar-specific widget plumbing path.

Because the requested save directory is defined relative to `bin`, the path should be derived from `QCoreApplication::applicationDirPath()` rather than from the current working directory.

## Design

- Extend the existing `outputModeWidget` layout with:
  - a thin vertical separator
  - an icon-only screenshot button
- Add QRC aliases for the dark/light screenshot assets so code and QSS can use stable resource names even if the on-disk light icon filename stays misspelled.
- Add a `MainWindow` helper that:
  - grabs the current main-window widget contents
  - creates `<appDir>/../images`
  - saves `<timestamp>.png`
  - reports success/failure through the status bar and debug log
- Keep the feature generic to the main window only; do not add menu actions, dialogs, or extra persistence.

## Success Criteria

- The title bar shows a separator and screenshot icon after the existing output-mode controls.
- The screenshot icon follows the active dark/light theme.
- Clicking the button saves a PNG named like `20260624_171426.png`.
- The save target is the `images/` folder that sits beside `bin/`.
- The change keeps the current `TitleBar` vs `MainWindow` responsibility split intact.

## Static Verification

- Confirm the screenshot button is created inside `MainWindow`'s existing title-bar action widget.
- Confirm the screenshot icons are reachable through `core.qrc`.
- Confirm the save path is derived from `QCoreApplication::applicationDirPath()` and normalized to `../images`.
- Confirm the filename format is `yyyyMMdd_HHmmss.png`.

## 2026-06-24 Visual Tuning Update

- The screenshot icon should visually match the existing title-bar icon language on the dark title bar: slightly larger and white/light-gray rather than reddish.
- The screenshot action should save silently; keep debug logging, but remove status-bar success/failure feedback.
- Theme template edits must be applied in `configuration_files/*/theme*.css`, not in generated runtime files under `configuration/`.
- `standard_cn` has already been corrected and should be treated as the source-of-truth sample for the remaining variants.
