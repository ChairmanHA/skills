# Win32 Minibar Taskbar Button

## Scope

- Show the helper-owned minibar as a taskbar window on Win32.
- Preserve its frameless, always-on-top, draggable, collapsed/expanded behavior.
- Keep Sweep, MOD, keyboard, menu, and overlay auxiliary windows taskbar-free.
- Reuse the active packet's existing application icon resources for the helper.
- Leave Linux X11 and Wayland window roles unchanged.

Verification level: `static`. The user will perform Win32 runtime testing.

## Observation

- `RemoteMiniBarWindow` is an unparented top-level widget, but its window type is
  explicitly `Qt::Tool`; on Win32 that role does not receive a taskbar button.
- Always-on-top behavior is independently requested with
  `Qt::WindowStaysOnTopHint`, so it does not require the main minibar window to
  remain a tool window.
- Helper-owned Sweep/MOD and keyboard windows define their own `Qt::Tool` roles;
  changing only `RemoteMiniBarWindow` does not change those auxiliary windows.
- The helper target does not currently compile the packet-specific application
  `.rc/.qrc` resources and does not set `QApplication::windowIcon()`.

## Implementation Plan

1. On Win32, give only `RemoteMiniBarWindow` the `Qt::Window` role while retaining
   `Qt::FramelessWindowHint`, `Qt::WindowSystemMenuHint`, and
   `Qt::WindowStaysOnTopHint`; retain `Qt::Tool` on other platforms. The system
   menu capability lets Win32 associate the window icon with the taskbar button
   without adding a native title bar.
2. Give the minibar window the helper application's branded title.
3. Compile the selected packet's Windows icon resource and Qt icon resource into
   the helper target.
4. Set the helper application icon when the selected packet provides a non-null
   `:/App/Image/app_icon` resource; keep the neutral packet's existing no-icon
   policy.
5. Record the Win32 taskbar role in the existing minibar architecture document.

## Success Criteria

- Win32 minibar mode has one taskbar button for the helper minibar.
- The taskbar button uses the packet-specific icon when that packet supplies one.
- The minibar remains frameless and always on top.
- Sweep/MOD/menu/keyboard windows do not create additional taskbar buttons.
- Restore and helper hide/show continue through the existing IPC lifecycle.
- Linux X11 and Wayland code paths are unchanged.

## Static Verification Checklist

- [x] The Win32-only main-window role is `Qt::Window`.
- [x] Non-Win32 main-window role remains `Qt::Tool`.
- [x] Auxiliary `Qt::Tool` window construction is unchanged.
- [x] Packet-specific icon resources are added only to the Win32 helper target.
- [x] A missing/null packet icon remains a supported no-op.
- [x] Diff contains no IPC, state-machine, geometry, or business changes.

## Implementation Result

- `RemoteMiniBarWindow` now uses the normal taskbar-capable Win32 window role,
  while all non-Win32 and auxiliary tool-window roles remain unchanged.
- The helper compiles the selected packet's existing `.rc/.qrc` resources and
  applies `:/App/Image/app_icon` when it exists.
- `git diff --check` completed without whitespace errors. Git only reported the
  repository's existing LF-to-CRLF checkout warning for the three edited source
  files.
- No build or runtime launch was performed; Win32 behavior is left for the
  requested user test.
