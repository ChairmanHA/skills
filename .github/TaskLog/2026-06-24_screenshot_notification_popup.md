# Screenshot Notification Popup

## Scope

Add a reusable semi-transparent notification popup in `src/libs/controls/`, then use it from the main-window screenshot action to show the completed screenshot path.

Touched area:

- `src/libs/controls/notificationpopup.h`
- `src/libs/controls/notificationpopup.cpp`
- `src/libs/controls/CMakeLists.txt`
- `src/plugins/core/mainwindow.cpp`
- `src/plugins/core/mainwindow.h`

## Verification Level

`static`

The repository default is static analysis unless build/run verification is explicitly requested.

## Observations

- `Controls::PopupWidget` is a menu-like `Qt::Popup`; it closes on focus/click semantics that are not suitable for selectable notification text.
- `Controls::MessageDialog` already uses selectable text for copyable messages, but it is modal/dialog-oriented and too heavy for screenshot completion feedback.
- The screenshot action already saves to `<applicationDir>/../images/yyyyMMdd_HHmmss.png` and logs the path.
- The desired anchor is inside `MainWindow`, visually below `CommonPanel` near the upper-left content area.

## Design

- Implement `Controls::NotificationPopup` as a child `QFrame`, not a top-level popup window.
- The popup owns:
  - a title label
  - a read-only selectable text edit for details/path
  - a close button
  - slide-in / slide-out `geometry` animations
  - a timer that starts after expansion
- The popup keeps itself visible while hovered, so users can select and copy the path.
- `MainWindow` keeps one popup instance and reuses it for repeated screenshots.
- `MainWindow` computes the final position from `m_commonPanel` geometry and places the popup just below it.

## Success Criteria

- Screenshot completion shows a semi-transparent notification below `CommonPanel`.
- The notification slides in over 500 ms.
- If the mouse is not over the popup, it closes after 3 seconds from expansion.
- If the mouse is over the popup, it stays visible until the mouse leaves or the close button is clicked.
- The path text is selectable and copyable.
- The popup is wide enough for a normal screenshot path/name and avoids the narrow wrapping issue shown in the reference screenshot.

## Static Verification

- Confirmed `notificationpopup.cpp/.h` are registered in `src/libs/controls/CMakeLists.txt`.
- Confirmed `MainWindow` creates one reusable notification instance and calls it only after `screenshot.save(filePath)` succeeds.
- Confirmed notification placement is derived from `m_commonPanel` geometry.
- Confirmed resize handling repositions a visible, non-animating notification.
- Confirmed no `configuration/` CSS file changes are needed for this component.

## 2026-06-24 Style And Encoding Follow-Up

- Use Unicode escapes for the Chinese screenshot-complete title so the runtime text does not depend on source-file encoding.
- Center the title across the popup header.
- Reduce the close button size.
- Show only one selectable line containing the screenshot path relative to the executable directory.
