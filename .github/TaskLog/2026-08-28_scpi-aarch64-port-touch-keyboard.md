# SCPI aarch64 port TouchNumKeyboard

## Scope

- Make both `Control Port` and `Data Port` use `Controls::TouchNumKeyboard` on Linux aarch64.
- Keep the behavior independent of the runtime Qt platform plugin, so both X11/xcb and Wayland use the same numeric keyboard.
- Preserve the existing legacy `Controls::Keyboard` behavior for eligible non-x86_64 Tablet builds.
- Do not change SCPI server start/stop behavior or broader soft-keyboard architecture.

## Observed Current State

- `SCPIDialog` installs its event filter only on `ui->lineEdit` (`Control Port`).
- The filter calls `Controls::Keyboard::showPopup()` only for that first field.
- `ui->lineEdit_2` (`Data Port`) therefore has no software-keyboard entry path.

## Plan

1. Install the same port-editor event filter on both line edits when a software keyboard is required.
2. On Linux aarch64, create a `TouchNumKeyboard` with integer port validation (`0..65535`) and the selected line edit's current value.
3. Write the numeric result back only when the keyboard is accepted; cancellation keeps the original text.
4. On other Tablet builds, keep using the existing `Controls::Keyboard` for the selected field.
5. Guard against opening a second numeric keyboard while the first one is active.

## Success Criteria

- Linux aarch64 X11 and Wayland builds open `TouchNumKeyboard` from either port field.
- Accepting edits updates only the selected field; cancelling changes nothing.
- Eligible non-x86_64 Tablet builds can open the existing legacy keyboard from both fields.
- Desktop builds that are neither Linux aarch64 nor Tablet retain normal physical-keyboard editing.
- Static diff and source inclusion checks pass.

## Verification Level

- `static`
- Runtime verification remains required on Linux aarch64 X11 and Wayland targets.

## Implementation Result

- Both SCPI port line edits now install the same event filter whenever the port software keyboard is enabled.
- Linux aarch64 selects `TouchNumKeyboard` at compile time without inspecting the runtime Qt platform plugin.
- The shared numeric-keyboard builder supplies the existing X11 top-level tool or Wayland managed-overlay presentation automatically.
- The selected line edit is used as the keyboard anchor and lifetime trigger; its focus is cleared before presentation so Wayland does not independently request a system input panel.
- The active keyboard is guarded with `QPointer`, and only an Accepted result writes the clamped integer port back to the selected line edit.
- SCPI now links `Business` explicitly because the public `PropertyBindingHelper::prepareNumericKeyBoard()` assembly path is used.

## Static Verification

- Confirmed `src/plugins/scpi/CMakeLists.txt` includes `scpidialog.cpp`, `scpidialog.h`, and links `Controls` plus `Business`.
- Confirmed the aarch64 branch is guarded by `Q_OS_LINUX && __aarch64__`, with no X11/Wayland runtime branch.
- Confirmed eligible non-x86_64 Tablet behavior retains `Controls::Keyboard` and covers both port fields.
- `git diff --check` passes apart from the repository's expected LF-to-CRLF working-tree warnings.
