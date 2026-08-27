# Raspberry Pi TitleBar Touch Runtime Debug

## Scope

- Connect to `192.168.3.215` and work in `~/Desktop/SGSProject` on the already selected branch.
- Instrument the MainWindow title menu input path to capture native touch,
  synthesized/system mouse, popup visibility, target object, action, and event order.
- Build and launch one instrumented SGStudio instance, let the user reproduce by
  repeatedly touching File, then inspect the persisted runtime log.
- Replace diagnostics with the smallest evidence-backed fix and rebuild.

## Success criteria

- Runtime evidence identifies which object receives touch and which later event
  reopens File.
- Repeated File touch closes the active File menu without opening it again.
- First touch opens File, touching another top-level menu switches normally,
  menu actions and physical mouse remain unaffected.
- Temporary high-volume diagnostics are removed after convergence.

## Verification level

- `debug-run` on Raspberry Pi

## Runtime evidence

- Target: Raspberry Pi Debian 12 / aarch64, Qt 5.15.8, native Wayland session.
- When File was already open, every repeated touch produced this order:
  1. `QMenu::aboutToHide` was emitted and the popup disappeared.
  2. `TouchBegin` then reached the title `QMenuBar`; at that point
     `QApplication::activePopupWidget()` was already null.
  3. Because the touch was not consumed, Qt synthesized a left mouse press
     (`Qt::MouseEventSynthesizedByQt`) and `QMenuBar` opened File again.
- The previous active-popup guard could therefore work for the physical mouse
  path but could not observe the popup during the Wayland touch path.

## Implemented boundary

- `TitleBar` tracks `aboutToHide` for top-level title menus, including actions
  added after the menubar is installed.
- The hidden menu is remembered only through the current event-loop turn.
- If the following `TouchBegin` hits that same top-level title, `TitleBar`
  consumes the complete touch sequence and its possible compatibility mouse
  sequence. A different title, menu item, or non-menubar widget is unaffected.
- The existing active-popup / physical-mouse fallback remains intact.
- Temporary high-volume event diagnostics were removed before the final build.

## Verification result

- Remote Release build tree: `build/pi-menu-trace`.
- Incremental build completed successfully (`20/20`, exit 0).
- The user repeatedly touched File on the Raspberry Pi and confirmed the menu
  now closes normally without reopening.
- The application exited normally after the test; final remote source diff is
  limited to `titlebar.cpp` and `titlebar.h`, and `git diff --check` passes.
