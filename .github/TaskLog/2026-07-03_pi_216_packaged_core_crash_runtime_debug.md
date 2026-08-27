# Raspberry Pi 192.168.3.216 Packaged Core Crash Runtime Debug

Date: 2026-07-03

## Scope

Investigate and fix a packaged SGStudio runtime crash on a second Raspberry Pi:

- Host: `192.168.3.216`
- User: `htra`
- Package path: `~/Desktop/SGStudio`

## Correction: Normal-User Launch

User correction:

- The target verification must be direct normal-user launch from `~/Desktop/SGStudio/bin`, without `sudo`.

Observation:

- A previous `sudo` launch of `/home/htra/Desktop/SGStudio/bin/SGStudio` left `/home/htra/Desktop/SGStudio/bin/debug.log` owned by `root:root` with mode `0644`.
- Direct launch as `htra` reproduced:
  - `Failed to open log file /home/htra/Desktop/SGStudio/bin/debug.log: Permission denied`
  - repeated `Failed to open log file for logging: Permission denied`

Follow-up fix plan:

- Restore ownership of package-generated runtime files under `~/Desktop/SGStudio` to `htra:htra`.
- Change `/software/app2.sh` so the application itself is launched as `htra`, not through `sudo`; keep only the pre-launch cleanup capable of killing older root-owned SGStudio processes.
- Re-verify direct launch from `~/Desktop/SGStudio/bin` as normal user.

Follow-up fix applied:

- Repaired `/home/htra/Desktop/SGStudio` ownership back to `htra:htra`; `bin/debug.log` is now writable by `htra`.
- Rewrote `/software/app2.sh` so it launches `./SGStudio --ui-mode=main` as the current desktop user, with no `sudo` in the application start command.
- Added `scripts/launch_sgstudio_pi.sh` and copied it to `/home/htra/Desktop/SGStudio/launch_sgstudio_pi.sh` for explicit normal-user launches:
  - `./launch_sgstudio_pi.sh --ui-mode=main`
  - `./launch_sgstudio_pi.sh --ui-mode=minibar`

Final normal-user verification:

- Direct binary launch from `~/Desktop/SGStudio/bin` with no arguments was verified as user `htra`; the process was `./SGStudio`, not `sudo ... SGStudio`.
- `./launch_sgstudio_pi.sh --ui-mode=main` and `./launch_sgstudio_pi.sh --ui-mode=minibar` were both verified as user `htra`; minibar mode logged `Enabled layer-shell integration for Wayland minibar mode`.


## Correction: Root-Owned Qt Instance Semaphore

User correction:

- Direct command-line launch still printed `Failed to initialize instance state registry: "QSharedMemoryPrivate::initKey: unable to set key on lock"`.

Observation:

- `InstanceStateRegistry` uses `QSharedMemory("HAROGIC.SGStudio.InstanceStateRegistry")`.
- `ipcs -s` showed a SysV semaphore for that Qt shared-memory lock:
  - key `0x510208d1`
  - semid `1`
  - owner `root`
  - perms `600`
- This root-owned semaphore was a leftover from the earlier sudo/root launch path. A normal `htra` process could not open the lock, so Qt reported the `QSharedMemoryPrivate::initKey` error.

Fix applied:

- Removed the stale root semaphore with `sudo ipcrm -s 1`.
- Re-ran `~/Desktop/SGStudio/bin/SGStudio` directly as `htra`.

Verification:

- The `Failed to initialize instance state registry` message disappeared.
- `ipcs -s` now shows key `0x510208d1` owned by `htra`, which is the expected normal-user state.
- The direct launch log again reached `Load Core OK`, `Initialize Core OK`, `Start Core OK`, and `DelayedInitialize Finished`.
- Updated `scripts/launch_sgstudio_pi.sh` to warn if this root-owned semaphore is detected in the future, without using sudo to start SGStudio.

## Correction: SSH Shell Missing Display Environment

User correction:

- Running exactly `./SGStudio` from `~/Desktop/SGStudio/bin` still aborts.

Observation:

- The user's plain command-line environment over SSH had:
  - `XDG_RUNTIME_DIR=/run/user/1000`
  - `DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus`
  - `XDG_SESSION_TYPE=tty`
  - no `DISPLAY`
  - no `WAYLAND_DISPLAY`
  - no `QT_QPA_PLATFORM`
- Plain `./SGStudio` aborted with:
  - `qt.qpa.xcb: could not connect to display`
  - `Could not load the Qt platform plugin "xcb" ...`
- The successful Codex-controlled launches differed because they explicitly passed:
  - `WAYLAND_DISPLAY=wayland-0`
  - `DISPLAY=:0`
  - `QT_QPA_PLATFORM=wayland`
  - `XDG_SESSION_TYPE=wayland`

Inference:

- The remaining abort is not a Core-plugin crash. Qt is choosing `xcb` from an SSH/tty environment with no display variables, then aborting before the main window can initialize.
- On Raspberry Pi Wayland, if an active desktop socket exists under `XDG_RUNTIME_DIR`, SGStudio can safely default to Wayland before `QApplication` is constructed.

Final decision:

- Do not modify SGStudio source or replace the SGStudio binary.
- Keep the fix as an external launcher script only.

Script placement:

- Put `launch_sgstudio_pi.sh` at the SGStudio package root, next to `bin/`, for example:
  - `/home/htra/Desktop/SGStudio/launch_sgstudio_pi.sh`
- For command-line launches on machines whose SSH shell lacks desktop variables:
  - `cd /home/htra/Desktop/SGStudio`
  - `./launch_sgstudio_pi.sh --ui-mode=main`
  - `./launch_sgstudio_pi.sh --ui-mode=minibar`
- For desktop shortcuts on these machines, point the `.desktop` `Exec=` command to this script instead of launching `bin/SGStudio` directly.

Verification:

- Verified the script from a deliberately stripped SSH environment with no `DISPLAY`, no `WAYLAND_DISPLAY`, and no `QT_QPA_PLATFORM`.
- The script supplied the Wayland desktop environment and SGStudio reached `Load Core OK`, `Initialize Core OK`, `Start Core OK`, and `DelayedInitialize Finished`.

## SAStudio Covers Minibar Layering

User observation:

- Start SGStudio in `--ui-mode=minibar` through the launcher script.
- Click the remote desktop SAStudio shortcut.
- The SGStudio minibar disappears.
- When SAStudio shows its close-confirmation modal dialog, the minibar appears and remains interactive.
- After the SAStudio modal dialog closes, the minibar disappears again.

Observation:

- Current `MiniBarWindow` layer-shell policy uses `LayerTop` for the minibar base surface.
- `configureLayerShellTransient(...)` also uses `LayerTop` for minibar-owned popup surfaces and overlay hosts.
- The described behavior means the minibar process/window is still alive; visibility changes with another application's normal/modal surface stacking.

Remote desktop launch evidence:

- Graphical session `1` on `192.168.3.216` is `lightdm-autologin`, `Desktop=LXDE-pi-labwc`, `Type=wayland`, `Remote=no`.
- The systemd user desktop environment contains `WAYLAND_DISPLAY=wayland-0`, `DISPLAY=:0`, `XDG_CURRENT_DESKTOP=labwc:wlroots`, and `XDG_SESSION_TYPE=wayland`.
- `/home/htra/Desktop/SAStudio4.desktop` launches `sh "/software/app.sh"`.
- `/software/app.sh` sets `QT_QPA_PLATFORM=wayland`, so the shortcut is not intentionally launching SAStudio as an X11/XCB Qt client.
- `/software/app.sh` does run `sudo -S -E env ... "$APP_PATH"` without `-u htra`, so SAStudio is launched as `root` while borrowing the `htra` desktop runtime variables.

Inference:

- On this Raspberry Pi compositor/SAStudio combination, `LayerTop` is not sufficient to keep the minibar above SAStudio's application window. SAStudio's modal dialog changes stacking/activation enough for the minibar to become visible temporarily.
- This is a compositor layer ordering issue, not a minibar visibility-state or device-open condition.
- The root-owned SAStudio launch is a separate operational risk because it can leave root-owned logs, runtime files, or IPC objects in a user desktop session. It is not, by itself, the Wayland stacking rule that decides whether the minibar is above or below SAStudio.

Fix plan:

- Change only the Wayland layer-shell layer used by minibar-owned surfaces from `LayerTop` to `LayerOverlay`.
- Keep anchors, margins, exclusive zone, keyboard interactivity, and Win32/X11/non-layer-shell paths unchanged.
- Apply the same layer choice to owned popup/overlay surfaces so popups are not left below SAStudio while the minibar base is above it.

Success criteria:

- In `--ui-mode=minibar`, launching/clicking SAStudio must not cover the minibar.
- SAStudio close-confirmation modal should no longer be the only condition that reveals the minibar.
- Minibar-owned popup interactions should remain unchanged.

Superseded change applied:

- `src/plugins/core/minibarwindow.cpp` now uses a single minibar layer-shell layer helper that returns `LayerOverlay`.
- The helper is used by both the minibar base surface and minibar-owned layer-shell transient/overlay surfaces.

Verification:

- Static diff check passed for `src/plugins/core/minibarwindow.cpp`.
- Runtime verification still requires rebuilding/repackaging SGStudio and testing on the Raspberry Pi compositor.

## SAStudio Covers Freq/Level Soft Keyboard

User observation:

- With SAStudio running, the minibar remains visible and interactive.
- The minibar `Freq` / `Level` buttons no longer appear to open the soft keyboard.
- Soft keyboards opened from `StepSweepPanel` and hosted business panels still work.
- Without SAStudio running, the minibar `Freq` / `Level` soft keyboard works normally.

Observation:

- `Freq` / `Level` on the minibar create `TouchNumKeyboard` directly from widgets in the minibar base surface.
- On Wayland, when the base minibar surface is too small to contain the keyboard, `TouchNumKeyboard` creates a top-level `TouchNumKeyboardScreenOverlayHost`.
- `TouchNumKeyboard` lives in `Controls`, which does not link `LayerShellQt`; therefore this screen overlay host is not explicitly configured by the keyboard itself.
- Hosted business/sweep panels are already carried by `MiniBarWindow`'s layer-shell popup overlay host, which explains why their soft keyboard path can keep working while the base minibar path fails.

Inference:

- With SAStudio present, the direct minibar keyboard overlay host is created but can be stacked below SAStudio because it lacks the same explicit `LayerOverlay` role as the minibar and hosted popup overlay surfaces.
- This is another layer-shell top-level surface policy gap, not a numeric property binding or adapter problem.

Fix plan:

- Keep `Controls::TouchNumKeyboard` independent of `LayerShellQt`.
- In `MiniBarWindow::eventFilter(...)`, detect the minibar-owned `TouchNumKeyboardScreenOverlayHost` when it is shown.
- Configure that host as a full-screen layer-shell transient using the same `LayerOverlay`, anchors, exclusive zone, and keyboard interactivity policy as other minibar-owned overlay surfaces.

Success criteria:

- With SAStudio running, clicking minibar `Freq` / `Level` shows the soft keyboard above SAStudio.
- Hosted business and sweep soft keyboard behavior remains unchanged.

Change applied:

- `src/plugins/core/minibarwindow.cpp` now detects minibar-owned `TouchNumKeyboardScreenOverlayHost` widgets on `Show`.
- The host is configured as a full-screen layer-shell transient with the shared minibar `LayerOverlay` policy.
- `Controls::TouchNumKeyboard` remains independent of LayerShellQt.

Verification:

- Static diff check passed for `src/plugins/core/minibarwindow.cpp`.
- Windows Debug incremental build passed:
  - `$env:CL='/FS'; cmake --build build/cmake-win-debug --config Debug --target Core -- /m:1 /p:CL_MPCount=1 /p:UseMultiToolTask=false`
- Runtime verification still requires rebuilding/repackaging SGStudio and testing on the Raspberry Pi with SAStudio running.

Field result and rollback:

- User field test showed the `TouchNumKeyboardScreenOverlayHost` layer-shell configuration did not fix the problem.
- Repeated clicks on minibar `Freq` / `Level` still did not show a keyboard while SAStudio was running.
- When SAStudio later showed its close-confirmation modal dialog, all previously opened keyboards appeared together.
- This proves the previous patch was not useful and also exposes an additional bug: repeated clicks can create stacked invisible keyboard instances.
- The `TouchNumKeyboardScreenOverlayHost` event-filter patch was removed from `src/plugins/core/minibarwindow.cpp`; the earlier verified minibar `LayerOverlay` change is retained.

Updated inference:

- The failure is not solved by retroactively reconfiguring the keyboard screen overlay host after `Show`.
- The direct minibar `Freq` / `Level` path needs a single-session keyboard owner: if a minibar numeric keyboard is already alive, another click must close/replace/activate that existing session instead of creating another hidden instance.
- A valid fix must also ensure the single current keyboard is shown through a host that participates in the same owned overlay model as other working minibar popups.

Updated fix plan:

- Add `MiniBarWindow` ownership for the direct minibar numeric keyboard session.
- Before opening a new direct `Freq` / `Level` keyboard, close and delete any previous direct minibar numeric keyboard.
- For layer-shell minibar mode, open direct minibar numeric keyboards inside the existing `MiniBarWindow` full-screen popup overlay host instead of allowing each keyboard to create its own independent screen overlay host.
- Keep hosted business/sweep panel keyboard behavior unchanged.

Updated success criteria:

- With SAStudio running, one click on minibar `Freq` / `Level` shows exactly one soft keyboard.
- Repeated clicks do not accumulate multiple hidden keyboards.
- Opening the SAStudio close-confirmation modal no longer reveals a backlog of old SGStudio keyboards.
- Sweep/business soft keyboards remain working.

Implementation:

- Removed the failed `TouchNumKeyboardScreenOverlayHost` `Show` event layer-shell patch from `MiniBarWindow`.
- Added `Controls::TouchNumKeyboard::setOverlayHost(QWidget *)` so a host can be supplied by an owning layer-shell-aware component without making `Controls` depend on LayerShellQt.
- `TouchNumKeyboard::resolveOverlayHost()` now prefers the explicitly supplied overlay host while still using the original anchor widget for positioning.
- `MiniBarWindow` now tracks `m_directNumericKeyboard` for direct minibar `Freq` / `Level` editing.
- Before opening a direct minibar numeric keyboard, `MiniBarWindow` closes and schedules deletion of any previous direct numeric keyboard.
- In layer-shell mode, `MiniBarWindow` supplies its already configured full-screen popup overlay host to the direct numeric keyboard.

Verification:

- `git diff --check` passed for the changed minibar/keyboard files; Git only reported existing LF-to-CRLF conversion warnings.
- Windows Debug incremental build passed for `Core`:
  - `$env:CL='/FS'; cmake --build build/cmake-win-debug --config Debug --target Core -- /m:1 /p:CL_MPCount=1 /p:UseMultiToolTask=false`
- Raspberry Pi layer-shell runtime verification is still required: rebuild/repackage with `build_pi`, start minibar, start SAStudio, then click `Freq` / `Level` repeatedly and confirm only one keyboard appears and no backlog appears when the SAStudio close-confirmation dialog opens.

