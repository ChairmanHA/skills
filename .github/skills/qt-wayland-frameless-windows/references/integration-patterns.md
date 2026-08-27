# Qt Wayland Frameless Window Integration Patterns

## Decision Table

| Requirement | Wayland presentation | Geometry parent | Overlay | Presentation call |
| --- | --- | --- | --- | --- |
| Frameless modal dialog | Hosted `Qt::Widget` | Explicit application host | Yes | `WaylandWindowController::present()` |
| Non-modal movable float | Hosted `Qt::Widget` | Explicit application host | No | `WaylandWindowController::present()` |
| Nested hosted dialog | Hosted sibling | Same host as outer dialog | Inner overlay | Inner controller `present()` |
| Native/system dialog | Native top-level | Logical transient parent | Compositor/Qt | Existing API |
| Win32/X11 frameless dialog | Existing top-level behavior | Existing logical parent | Existing modality | Controller preserves it |

## Parent and Ownership Rules

For a hosted child, Qt parent defines:

- coordinate system;
- clipping boundary;
- stacking group;
- QObject lifetime ownership.

Choose the host before constructing the window. Prefer an application content widget that fills the usable client area. Do not pass a narrow panel or an outer dialog.

If business lifetime must be owned elsewhere, keep an explicit `QPointer`, delete on `finished()`/`closed`, or use `Qt::WA_DeleteOnClose`. Do not corrupt geometry parent selection to express business ownership.

## Initial Visibility and Startup Construction

A top-level `QDialog` is normally hidden when constructed. Converting it to a hosted `Qt::Widget` before the application host is first shown changes it into a child; without an explicit hidden state, Qt can show that child together with its host.

Before changing flags or parent, record whether the window is visible. After hosted conversion, explicitly call `hide()` when it was initially hidden:

```cpp
const bool wasHidden = !window->isVisible();
window->setParent(applicationHost, Qt::Widget);
if (wasHidden) {
    window->hide();
}
```

This is required even when `isVisible()` is still false immediately after `setParent()`: the explicit `hide()` prevents the host's later `show()` from becoming an accidental presentation path. Pre-creation is allowed, but only the intended menu, signal, or controller call may invoke `show()`, `open()`, or `present()`.

For an inner dialog:

```text
applicationHost
├── outerModalOverlay
├── outerDialog
├── innerModalOverlay
└── innerDialog
```

Do not create:

```text
outerDialog
└── innerDialog   # clipped to outerDialog
```

## Add the Portable Kit

Copy from this skill:

```text
cmake -DDESTINATION=/absolute/workspace/src/qt-wayland-window-kit \
      -P /absolute/skill/scripts/install_window_kit.cmake
```

Then add to the target project:

```cmake
find_package(QT NAMES Qt6 Qt5 REQUIRED COMPONENTS Core Gui Widgets)
find_package(Qt${QT_VERSION_MAJOR} REQUIRED COMPONENTS Core Gui Widgets)

add_subdirectory(src/qt-wayland-window-kit)
target_link_libraries(MyTarget PRIVATE
    QtWaylandWindowKit::QtWaylandWindowKit
)
```

## Modal Frameless Dialog

Construct the dialog with the application host from the beginning:

```cpp
#include <qt-wayland-window-kit/waylandwindowcontroller.h>

auto *dialog = new MyDialog(applicationHost);
dialog->setAttribute(Qt::WA_DeleteOnClose);

auto *presentation = new QtWaylandWindowKit::WaylandWindowController(
    dialog,
    applicationHost,
    QtWaylandWindowKit::WaylandWindowController::Mode::ModalDialog);

presentation->addDragHandle(dialog->titleBar(), false);
presentation->addDragHandle(dialog->titleLabel(), true);
presentation->bindCloseButton(dialog->closeButton());

connect(dialog, &QDialog::finished, owner, [guard = QPointer<MyDialog>(dialog)](int result) {
    if (!guard) {
        return;
    }
    // Copy required values here; defer re-entrant business work if necessary.
});

presentation->present();
```

The title bar handles mouse or platform-synthesized mouse events. Only the title label explicitly accepts raw Touch; the close button keeps its own input sequence.

## Non-modal Floating Window

```cpp
auto *floating = new MyFloatingWidget(applicationHost);
floating->setAttribute(Qt::WA_DeleteOnClose);

// MyFloatingWidget should keep its existing Qt::Tool/frameless top-level
// flags on non-Wayland platforms; the controller changes them only on Wayland.

auto *presentation = new QtWaylandWindowKit::WaylandWindowController(
    floating,
    applicationHost,
    QtWaylandWindowKit::WaylandWindowController::Mode::NonModalFloating);

presentation->addDragHandle(floating->titleBar(), false);
presentation->addDragHandle(floating->titleLabel(), true);
presentation->bindCloseButton(floating->closeButton());

// Call this only from the intended menu/signal action. If the controller is
// created during startup, the floating window remains explicitly hidden until here.
presentation->present();
```

This mode never creates an overlay, so the host remains interactive.

## Opening an Inner Modal

Use the same application host, not the outer dialog:

```cpp
void OuterDialog::openInner()
{
    QWidget *host = m_applicationHost;
    auto *inner = new InnerDialog(host);
    inner->setAttribute(Qt::WA_DeleteOnClose);

    auto *presentation = new QtWaylandWindowKit::WaylandWindowController(
        inner,
        host,
        QtWaylandWindowKit::WaylandWindowController::Mode::ModalDialog);

    presentation->addDragHandle(inner->titleBar(), false);
    presentation->addDragHandle(inner->titleLabel(), true);
    presentation->bindCloseButton(inner->closeButton());
    presentation->present();
}
```

## Existing-Code Adaptation

If the target repository already has drag or overlay helpers:

1. Keep a generic drag controller responsible only for pointer/touch movement and clamping.
2. Keep a Wayland presentation controller responsible only for hosted flags, explicit host, overlay, stacking, and resize.
3. Compose the drag controller inside the presentation path.
4. Remove duplicate title-bar movement only after verifying non-Wayland callers still have a drag path.
5. When flags or parent are changed during construction, preserve an initially hidden state with an explicit `hide()` after conversion.

## Verification Matrix

Test at least:

- modal first show and close;
- host first show while modal/non-modal windows are pre-created: those windows remain hidden;
- modal mouse and Touch drag;
- close button by mouse and Touch;
- drag to all four host edges;
- host resize while visible;
- nested modal open, cancel, accept, and outer-dialog recovery;
- non-modal float while interacting with host;
- repeated show/hide without stale overlays;
- Win32/X11 behavior unchanged;
- object deletion while a deferred callback is pending.
