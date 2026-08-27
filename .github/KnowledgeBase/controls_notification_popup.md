# Controls Notification Popup

## Purpose

`Controls::NotificationPopup` is a lightweight, non-modal notification widget for short user feedback that should not interrupt the workflow.

The first use case is screenshot completion feedback in the main window.

## Current Behavior

- The popup is a child `QFrame`, not a top-level `Qt::Popup`.
- It slides in from the left over 500 ms.
- It starts a 3-second auto-close timer after expansion.
- Hovering the popup stops auto-close so the user can select and copy text.
- Leaving the popup restarts auto-close.
- The close button dismisses it immediately.
- The message body uses selectable read-only text.
- Screenshot notifications show a single-line path relative to the executable directory.

## Ownership Boundary

- `Controls::NotificationPopup` owns presentation, animation, hover pause, and close behavior.
- The caller owns message text and placement.
- `Core::MainWindow` positions the screenshot notification below `CommonPanel` and passes the saved screenshot path.
- `Core::MainWindow` formats screenshot text with Unicode escapes for Chinese labels, avoiding source-encoding dependent runtime text.

## When To Use

Use this for transient, non-blocking completion messages where the user may still need to copy a path or short detail.

Do not use it for errors requiring explicit acknowledgement; those should remain `Controls::MessageDialog`.
