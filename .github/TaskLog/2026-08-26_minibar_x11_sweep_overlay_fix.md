# MiniBar X11 Sweep Overlay Fix

## Scope

Prevent the entire screen from turning black when the helper opens the Sweep
panel on Linux AArch64 X11.

## Observation

- The field failure occurs on X11 when Sweep opens; the MOD AM editor renders
  normally on the same target.
- Sweep always creates and shows a screen-sized top-level widget with
  `WA_TranslucentBackground` and `WA_NoSystemBackground`.
- MOD uses that fullscreen overlay only for LayerShell Wayland. On non-Wayland
  platforms its editor is a normal frameless `Qt::Tool` dialog.
- An X11 server/window manager without a working compositor or ARGB transparency
  path can render the transparent fullscreen Sweep host as black.

## Design

- Keep the existing Sweep overlay path on LayerShell Wayland and Win32.
- On Linux non-Wayland platforms, create Sweep as a normal frameless `Qt::Tool`
  dialog owned by the MiniBar, matching the proven MOD editor path.
- Present the X11 dialog directly in global screen coordinates and do not create
  or show the fullscreen transparent host.
- Keep the existing Sweep button/outside-click session handling; without an X11
  overlay, clicking the Sweep button or another MiniBar area can close the panel.

## Success criteria

1. Linux X11 does not create or show `miniBarHelperSweepOverlayHost`.
2. Sweep opens at the same anchored popup rectangle as before, without a
   screen-sized transparent window.
3. LayerShell Wayland keeps its fullscreen outside-click catcher and hosted Sweep
   child geometry.
4. Win32 behavior remains unchanged.
5. MOD behavior and Sweep business/IPC behavior remain unchanged.

## Verification level

`static`

## Result

- `useSweepOverlayHost()` now keeps the overlay on Win32 and selects it on Linux
  only when the Qt platform is Wayland (`xcb` therefore takes the direct path).
- Linux X11 creates `miniBarHelperSweepPopup` as a normal frameless `Qt::Tool`,
  positions it with the already calculated global popup rectangle, and never
  creates or shows `miniBarHelperSweepOverlayHost`.
- Wayland and Win32 retain the existing hosted overlay presentation and outside
  input handling.
- `isSweepPanelVisible()` now follows the actual dialog, covering both hosted and
  top-level presentation models.
- The current MiniBar platform-boundary KnowledgeBase document records the X11
  policy.
- The source remains included by the helper CMake target, focused branch searches
  are consistent, and `git diff --check` passes with only existing line-ending
  warnings. No build or runtime test was performed.

## Follow-up: X11 outside-click close

### Boundary

- A non-overlay X11 tool window cannot consume a click delivered to the desktop
  or another application without an X11 pointer grab/native event path.
- It can preserve the user-visible close behavior through Qt's
  `ApplicationDeactivate`: the clicked target receives the click normally and the
  Sweep panel closes when the helper loses activation.
- Clicks elsewhere inside the helper process already pass through the existing
  application mouse-event filter and close Sweep.
- Wayland must not use application deactivation as outside-click evidence because
  switching among the helper's layer surfaces can also deactivate a surface.

### Plan and success criteria

- Reuse the existing Win32 `ApplicationDeactivate` close path for Linux
  non-Wayland only.
- Preserve the enum-popup and numeric-keyboard guards before closing Sweep.
- Keep Wayland overlay ownership and input routing unchanged.
- Confirm the platform predicate, focused event path, and `git diff --check`
  statically; do not build or run.

### Result

- Added a narrow deactivation policy: true for Win32 and Linux non-Wayland,
  false for LayerShell Wayland and other platforms.
- The existing application-wide event filter now closes X11 Sweep on
  `ApplicationDeactivate`, while retaining the enum-popup and numeric-keyboard
  guards.
- In-process outside mouse presses continue to use the existing object/geometry
  classification and close path.
- No X11 pointer grab or native event filter was added, so the external target
  still receives its click normally.
- Focused static inspection and `git diff --check` pass with only existing
  line-ending warnings. No build or runtime test was performed.
