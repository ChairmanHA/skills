# ETH Dialog System Keyboard Plan

- Date: 2026-06-08
- Target: src/plugins/core/ethconnectdialog.cpp, src/plugins/core/ethconnectdialog.h

## Context

- User only wants IP and port text entry to use the system keyboard on aarch64 Raspberry Pi.
- Win32 should remain unchanged and may rely on a physical keyboard.
- Remote probe on 192.168.3.179 shows labwc + wf-panel-pi Wayland session with squeekboard installed and running.
- Current EthConnectDialog uses plain QLineEdit fields and has no software input panel request path.
- Existing repo code only opens custom keyboards for special numeric/unit inputs; there is no generic QInputMethod trigger path to reuse here.

## Local Hypothesis

- On Linux/Wayland, tapping the IP or port QLineEdit does not show squeekboard because the dialog never explicitly requests the software input panel after focus is established.

## Plan

- Add a Linux-only event filter on the IP and port line edits.
- On focus-in or pointer press, defer a software input panel request until focus is stable, then call QGuiApplication::inputMethod()->show().
- Keep the change local to EthConnectDialog so unit-entry dialogs and Win32 behavior are unaffected.
- Add input method hints suited for IP and port entry.

## Validation

- Run a narrow error check on the touched files.
- If clean, optionally run a narrow Debug build target for the core plugin if needed.

## Follow-up 2

- User additionally requires Linux/X11 environments without a system keyboard, such as rk3588, to remain non-crashing.
- Save/Open file flows on Raspberry Pi should stop surfacing the custom Controls::Keyboard panel and instead follow the same system-keyboard request model.
- The most local owning abstraction is Controls::Keyboard::showPopup because current custom text-keyboard callers route through it, including PxSaveFileDlg and InputDialog.
- Plan: keep the Keyboard class compiled, but on Linux make showPopup request the software input panel on the target QLineEdit with deferred focus-stable delivery and null-safe guards; keep non-Linux behavior unchanged.
- Update PxSaveFileDlg's event filter from mouse-release custom-popup timing to the same focus/pointer-trigger style used by EthConnectDialog.
- Validate with a narrow Controls target incremental build.

## Follow-up 3

- Runtime inspection on Raspberry Pi confirmed SGStudio is using the Qt Wayland platform plugin, not xcb/XWayland.
- Squeekboard can be forced visible through DBus service `sm.puri.OSK0`, and screenshots confirm the keyboard is not hidden behind the application window.
- While focus is inside the ETH IP field, `sm.puri.OSK0.Visible` stays `false` unless forced manually, so the current `QInputMethod::show()` path is insufficient on this compositor/session combination.
- Plan: add a Linux-only DBus fallback in the shared Controls::Keyboard path to call `sm.puri.OSK0.SetVisible(true/false)` when requesting or dismissing the system keyboard, and let EthConnectDialog delegate to that shared path while hiding the keyboard when the dialog closes.