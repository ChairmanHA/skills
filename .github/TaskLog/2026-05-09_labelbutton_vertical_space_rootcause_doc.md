# LabelButton Vertical Space Root Cause Doc Plan

## Goal

- Write a KnowledgeBase document explaining why `LabelButton` lower text appears unable to gain extra bottom space in minibar.
- Compare minibar against main-mode `CommonPanel` so the explanation is grounded in the actual QSS and layout differences in this repository.

## Local Hypothesis

- The root cause is structural in `InfoButton`, not a missing one-line alignment flag in `LabelButton`.
- `InfoButton` splits the button into two equal-height rows with zero inter-row spacing and zero layout margins, so the lower row already extends to the button bottom.
- `QLabel` padding / contentsMargins only redistribute space inside that lower row; they cannot create new outer space below the row.
- `CommonPanel` looks fine because it has a larger vertical budget and only uses one-sided QSS padding to pull the two labels toward the center, not because it has a different base layout.

## Evidence To Capture

- `src/libs/controls/infobutton.cpp`: two-row `QGridLayout`, zero spacing, zero margins, `rowStretch(0)=rowStretch(1)=1`.
- `src/libs/controls/labelbutton.cpp`: `infoLabel` is just a `QLabel` replacing `infoWidget`; no extra spacer/container exists.
- `src/plugins/core/commonpanel.cpp`: main-mode buttons center both labels and do not add extra label content margins in code.
- `configuration/theme.css`: `CommonPanel` only adds `padding-top: 5px` to `textLabel` and `padding-bottom: 5px` to `infoLabel`.
- `src/plugins/core/minibarwindow.cpp`: minibar uses a shorter fixed button height and extra local insets/gutters, so the same two-row structure has less slack.

## Output Boundary

- Add one new KnowledgeBase markdown file under `.github/KnowledgeBase/`.
- Update `.github/KnowledgeBase/Index.md` with a concise summary entry.
- Do not change runtime code in this pass.

## Runtime Follow-up

- User chose the simplest runtime fix: increase minibar primary button height directly instead of continuing to tune label padding.
- Align `MiniBarWindow` primary button height with main-mode/CommonPanel's 60px vertical budget while keeping the current symmetric title/detail insets unchanged.
- Keep the change local to `src/plugins/core/minibarwindow.cpp` and avoid touching shared `LabelButton` / `InfoButton` code.

## Cleanup Follow-up

- After the height increase proved sufficient, the temporary minibar-local title/detail inset constants and `setTextMargins` / `setLabelMargins` calls are no longer needed.
- Remove those temporary adjustments to restore a cleaner style path while preserving the validated 60px primary button height.