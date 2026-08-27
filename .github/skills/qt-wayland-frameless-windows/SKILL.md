---
name: qt-wayland-frameless-windows
description: Use when implementing, fixing, or reviewing Qt Widgets frameless modal dialogs or non-modal floating windows on Wayland, especially when windows must be draggable by mouse or touch, closable, constrained to the application, correctly parented, protected by a modal overlay, or opened from another hosted dialog without clipping. Includes a portable Qt5/Qt6 C++17 window kit that can be copied into other CMake workspaces.
---

# Qt Wayland Frameless Windows

Use a hosted child-widget model when a Wayland frameless window must support client-controlled movement. Keep window presentation, drag handling, and business lifetime as separate concerns.

Read [integration patterns](references/integration-patterns.md) before editing window code. It defines the parent/host rules, modal and floating variants, nested-dialog behavior, and integration examples.

## Workflow

1. Inspect the actual creation and presentation chain.
   - Find the constructor parent, later `setParent()`/`setWindowFlags()` calls, `show()`/`open()`/`exec()` usage, close-button connection, and CMake inclusion.
   - Determine whether the window is pre-created before its application host is first shown. Record its intended initial visibility and the single action that is allowed to present it.
   - Distinguish observed code from assumptions about compositor behavior.

2. Classify the window.
   - Use `ModalDialog` for an application-contained dialog that blocks its host.
   - Use `NonModalFloating` for a movable tool/palette that leaves its host interactive.
   - Keep native top-level behavior when the window does not require client-positioned dragging on Wayland.

3. Select the host explicitly.
   - Pass the application content/main-window host from the initial window construction.
   - Do not use the button, panel, page, or outer modal dialog as the hosted geometry parent.
   - Give nested hosted windows the same host so they are siblings and can be ordered with `raise()`.

4. Reuse or install the portable kit.
   - Prefer an equivalent project-local implementation if it already separates drag and presentation responsibilities.
   - Otherwise copy `assets/qt-wayland-window-kit/` into the target workspace:

```text
cmake -DDESTINATION=<workspace>/src/qt-wayland-window-kit \
      -P <skill>/scripts/install_window_kit.cmake
```

   - The installer accepts only a new or empty destination. Merge into an existing implementation manually.

5. Integrate handles and close behavior.
   - Install mouse dragging on the title-bar background.
   - Enable native Touch only on explicit label/handle widgets; never enable it on an ancestor that covers the close button.
   - Bind close to `QDialog::reject()` for dialogs and `QWidget::close()` for floating widgets.

6. Preserve platform behavior.
   - Convert to hosted `Qt::Widget` only when the Qt platform name contains `wayland`.
   - Preserve initial visibility across the conversion. If a top-level window was hidden, explicitly call `hide()` after it becomes a child so showing the host cannot reveal it.
   - Do not let construction or hosted conversion replace the existing explicit `show()`/`open()`/`present()` entry point.
   - Keep existing Win32/X11 top-level flags, modality, and presentation unless the task explicitly changes them.

7. Verify.
   - For every window pre-created during startup, show the host first and confirm the window stays hidden until its intended presentation action.
   - Check initial placement, full visibility, mouse drag, touch drag, boundary clamping, close/cancel, overlay restoration, host resize, nested modal ordering, and non-Wayland behavior.
   - Run static checks before build/runtime checks required by the target repository.

## Non-negotiable Invariants

- A hosted widget's Qt parent is its coordinate and clipping host, not merely its lifetime owner.
- Never construct an inner hosted dialog with the outer hosted dialog as parent and then rely on late reparenting.
- A modal overlay and its dialog must share the same host; raise overlay first and dialog second.
- A non-modal floating window must not create an input-blocking overlay.
- Hosted conversion must preserve an initially hidden window as explicitly hidden; the host's first `show()` must never become an accidental presentation path.
- Window construction, parent/flag conversion, and presentation are separate operations. Only the intended `show()`/`open()`/`present()` path may reveal the window.
- Dragging must use event coordinates, not polled cursor position.
- Do not install a consuming Touch filter on the close button or a title-bar ancestor that receives the close button's Touch sequence.
- Keep async callback/lifetime guards independent from geometry-parent fixes.

## Bundled Resources

- `assets/qt-wayland-window-kit/`: dependency-free Qt Widgets C++17 implementation.
- `references/integration-patterns.md`: decision table and copy-ready integration examples.
- `scripts/install_window_kit.cmake`: safe copier for a new or empty target directory.
