# GPS ComboBox Popup Anchor

## Scope

- Make the shared `Controls::ComboBox` popup use the same ListMode placement
  contract as `EnumTextButton`.
- Cover both `timeFormatComboBox` and `antenna` in `GpsDialog` without adding
  GPS-specific positioning code.

## Evidence

- `src/plugins/gps/gpsinfodialog.ui` declares both controls as
  `Controls::ComboBox`.
- `src/libs/controls/combobox.cpp` currently repositions the private popup
  container only after `QComboBox::showPopup()` returns.
- That is too late for a newly mapped Wayland popup, and the aarch64-only
  main-window bottom clamp can move the popup upward across its anchor.
- Both source files are included by their active CMake targets.

## Success criteria

- The popup left edge aligns with the ComboBox left edge and its top edge is
  the ComboBox bottom edge.
- The popup stays below the control whenever the target screen has room;
  only insufficient screen space may place it above, matching
  `EnumTextButton`.
- Horizontal placement is clamped to the current screen available geometry.
- Geometry is applied during the popup Show event, before first native mapping,
  rather than only after `showPopup()` returns.
- The rule remains shared by all `Controls::ComboBox` users.

## Verification

- Level: static.
- Confirm active CMake inclusion, inspect the popup event/geometry order, run
  `git diff --check`, and leave runtime validation to the normal UI test pass.

## Implementation result

- `Controls::ComboBox::showPopup()` installs an event filter on the private
  popup container before calling the Qt base implementation.
- The popup geometry is now applied synchronously on `QEvent::Show`, after Qt
  has calculated the list height but before first native mapping.
- Placement uses the ComboBox bottom-left as the screen reference and mirrors
  `EnumTextButton` ListMode screen clamping.
- Removed the aarch64 main-window-bottom correction that could move the popup
  upward across the triggering control.
- No GPS-specific code or UI layout was changed.
