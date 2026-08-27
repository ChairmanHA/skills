# MiniBar Restore Full Mode Plan

Date: 2026-07-02

## Scope

Add a normal `QPushButton` named `Restore` below the frequency/power sweep button in `MiniBarWindow`.

Clicking `Restore` should ask for confirmation:

> 切换为完整模式需要重启程序，是否要继续。

If the user chooses `No`, nothing else should happen and the minibar must remain usable.
If the user chooses `Yes`, SGStudio should close through the existing shutdown path and restart in full main-window mode.

## Static Evidence

Observation:

- `src/plugins/core/CMakeLists.txt` includes `minibarwindow.cpp/.h`, `mainwindow.cpp/.h`, and `coreplugin.cpp`.
- `MiniBarWindow::buildExpandedUi()` currently lays out `RF`, `Frequency`, `Level`, `MOD`, `Fre/Pwr Sweep`, then the collapse button.
- Recent Wayland minibar fixes require hosted panels/popups to keep MiniBarWindow-owned layer-shell policy. A new confirmation dialog must not re-enable old `ApplicationDeactivate` close/collapse behavior.
- `MessageDialog::execMessage()` exists as the narrow synchronous exception for MiniBar cases that need a stable modal result.
- `MessageDialog::showMessage()` closes visible `Qt::Popup` windows before opening asynchronously. That is useful in main-window flows but is not ideal for a minibar Restore click where `No` should leave current popup/overlay state undisturbed.
- MainWindow language/theme restart uses `MainWindowSettingsController::requestApplicationRestart()`: set `m_showExitDialog=false`, deactivate current business, set `APP/PendingRestart=true`, sync settings, then `qApp->closeAllWindows()`.
- `MainWindow::closeEvent()` sees `APP/PendingRestart=true`, sets `APP/Reboot=True`, suspends `TxSessionService`, disables the active business, and accepts close.
- `main.cpp` connects `QApplication::aboutToQuit` to `PluginManager::shutdown()`, so normal Qt application exit already runs plugin/device shutdown.
- After `app.exec()` returns, `main.cpp` checks `APP/PendingRestart`, clears it, and currently starts a detached process with the argument `reboot`.

Mismatch to keep in view:

- Current `src/plugins/core/coreplugin.cpp` only parses the `--ui-mode` `QCommandLineOption`; it does not contain the tolerant parser described in that historical TaskLog.
- Therefore this feature should start the next process with a canonical argument that current code definitely parses, such as `--ui-mode=main`.

## Assumptions

- "Restore" means returning from `--ui-mode=minibar` to the normal `MainWindow` UI mode; it does not mean restoring saved RF/frequency/sweep values.
- The existing `APP/PendingRestart` handshake is the correct shutdown/restart owner because it already lets `aboutToQuit -> PluginManager::shutdown()` close devices, business objects, scanners, and worker threads.
- The minibars's Wayland behavior should be preserved by keeping the confirmation dialog parented to `MiniBarWindow` and by not touching popup/overlay event-filter policy on cancel.

## Implementation Plan

1. Add `m_restoreButton` to `MiniBarWindow`.
   - Declare it as `QPushButton *m_restoreButton = nullptr` in `minibarwindow.h`.
   - Add private slot `onRestoreButtonClicked()`.
   - Create it in `buildExpandedUi()` after `m_sweepButton` and before `m_collapseButton`.
   - Use the existing ordinary toggle-button style helper or a small dedicated helper if the height needs to match product expectation. Keep it a plain `QPushButton` with text `tr("Restore")`.

2. Use a stable confirmation path.
   - In `onRestoreButtonClicked()`, defer the dialog with `QTimer::singleShot(0, this, ...)` so the button click finishes before entering a nested modal loop.
   - Construct `Controls::MessageDialog dialog(this)` or a heap/dialog guard parented to `this`.
   - Set title to `tr("Confirm")`.
   - Use `execMessage(tr("Switch to normal mode requires a restart. Continue?"), SG::MSG_QUESTION, QMessageBox::Yes | QMessageBox::No)`.
   - Explicitly set `WA_QuitOnClose=false` on the dialog before `execMessage()` because minibar/tool-host auxiliary top-level dialogs must never become an application quit trigger.

3. Cancel path.
   - If result is not `QMessageBox::Yes`, return immediately.
   - Do not call `closeTransientWidgets()`.
   - Do not change `m_state`, `m_leftButtonPressed`, `m_dragging`, RF/MOD/sweep state, or any settings key.
   - This keeps any currently valid minibar interaction path intact after the modal dialog closes.

4. Confirm path.
   - Add a small `MiniBarWindow::requestApplicationRestartToMainMode()` helper rather than duplicating logic inline.
   - If `TxSessionService::instance()` exists, call `setUpdatesSuspended(true)` before closing, matching `MainWindow::closeEvent()` shutdown intent for restart.
   - If `BusinessManager::instance()->activedBusiness()` exists, call `setActive(false)`, matching language/theme restart.
   - Set `APP/PendingRestart=true`.
   - Set a new restart-arguments setting such as `APP/PendingRestartArguments` to `QStringList{ "--ui-mode=main" }` or another unambiguous serialized value.
   - Set `APP/Reboot=True` only if preserving existing startup profile reload semantics is desired for minibars as well; otherwise leave it to the existing main-window close path. Preferred first pass: set it to `True` to match current restart semantics.
   - Sync settings and call `qApp->closeAllWindows()`.

5. Generalize the detached restart argument in `main.cpp`.
   - Add a constant for the new pending restart arguments key.
   - On startup, clear only stale restart state at the start as currently done for `APP/PendingRestart`; do not clear arguments after a process has just requested them until the post-`app.exec()` branch consumes them.
   - After `app.exec()` returns and `APP/PendingRestart` is true:
     - read the pending argument list;
     - clear `APP/PendingRestart` and the argument key;
     - call `QProcess::startDetached(QApplication::applicationFilePath(), args)`.
   - If no pending argument list exists, keep the current fallback `QStringList{ "reboot" }` so existing language/theme/update behavior is not broken.

6. Keep startup parsing change out of this feature unless verification proves it is required.
   - Since the new launcher can pass `--ui-mode=main`, the tolerant `--ui-mode = main` parser is not necessary to deliver Restore.
   - If product launch configuration also needs spaced-equals support, handle that as a separate parser cleanup because the current source contradicts older TaskLog notes.

## Success Criteria

- Expanded minibar shows a normal `Restore` button directly below `Fre/Pwr Sweep` and above the collapse button.
- Clicking `Restore` shows one modal confirmation dialog.
- Selecting `No` closes only the confirmation dialog; minibar remains visible/expanded and RF/frequency/level/MOD/sweep buttons continue to work.
- Selecting `Yes` exits through normal Qt shutdown, runs `PluginManager::shutdown()`, and restarts SGStudio with main-window UI mode.
- On Raspberry Pi Wayland, showing and canceling the confirmation dialog does not break layer-shell popup ownership, sweep popup reopening, soft keyboard editing, or the explicit expand/collapse-only minibar state rule.

## Verification Level

Static first.

Suggested checks after implementation:

1. `git diff --check -- src/app/main.cpp src/plugins/core/minibarwindow.cpp src/plugins/core/minibarwindow.h`
2. Static source check that the new button is added only to the expanded page and that cancel path has no side effects.
3. If runtime verification is requested, use the existing Debug build tree and test both:
   - `Restore -> No`: continue using minibar and open/close sweep + keyboard.
   - `Restore -> Yes`: process exits, logs show normal shutdown, then new process starts as `--ui-mode=main`.

## Implementation

Implemented in:

- `src/plugins/core/minibarwindow.h`
- `src/plugins/core/minibarwindow.cpp`
- `src/app/main.cpp`

MiniBar changes:

- Added a plain `QPushButton` Restore command to the expanded minibar layout directly below `Fre/Pwr Sweep` and above the collapse button.
- Added `onRestoreButtonClicked()`.
- The click handler defers the confirmation to the next event turn, creates a `Controls::MessageDialog` parented to `MiniBarWindow`, sets `WA_QuitOnClose=false`, prepares the question content, and then uses synchronous `exec()`.
- In layer-shell mode, the confirmation dialog is configured through `MiniBarWindow::configureLayerShellTransient(...)` before `exec()`, avoiding the default layer-shell top-level geometry path.
- Cancel path only returns from the dialog result check. It does not call `closeTransientWidgets()`, does not change minibar state, and does not write settings.
- Confirm path calls `requestApplicationRestartToMainMode()`, which suspends TX updates, deactivates the active business if present, writes the pending restart settings, and calls `qApp->closeAllWindows()`.

Restart handoff changes:

- Added `APP/PendingRestartArguments`.
- `main.cpp` now reads pending restart arguments after `app.exec()` returns.
- If no pending arguments exist, the old fallback `reboot` argument is preserved.
- Restore writes `QStringList{ "--ui-mode=main" }`, so the restarted process enters normal main-window mode through the current `--ui-mode` parser.

## Static Verification

Command:

```text
git diff --check -- src/app/main.cpp src/plugins/core/minibarwindow.cpp src/plugins/core/minibarwindow.h .github/TaskLog/2026-07-02_minibar_restore_full_mode_plan.md
```

Result:

- Passed.
- Git still reports existing LF-to-CRLF working-copy warnings for the touched C++ files.

Runtime verification was not run, per the repository default static-analysis workflow.

## 2026-07-03 Main Window Minibar Entry Plan

Scope:

- In normal `--ui-mode=main`, add an icon-only button in the title-bar action area, directly to the right of the screenshot button.
- The dark theme uses `src/plugins/core/resource/image/showpanel.png`.
- The light theme uses `src/plugins/core/resource/image/showpanel_light.png`.
- Clicking the button shows the same `Controls::MessageDialog` confirmation style used by the main-window language/theme restart paths.
- If the user chooses `No`, no state changes are made.
- If the user chooses `Yes`, SGStudio exits through the normal main-window close path and restarts with `--ui-mode=minibar`.

Static evidence:

- `MainWindow::registerDefaultActions()` creates `btnSingle`, `btnContinue`, and `btnScreenshot`, then places them in `outputModeWidget`.
- `TitleBar::setOutputModeWidget()` only hosts this widget and does not own title-bar business behavior.
- `configuration_files/standard_cn/theme.css` and `configuration_files/standard_cn/theme_light.css` style `QPushButton#btnScreenshot` with icon-only geometry and theme-specific icons for the standard Chinese profile.
- `src/plugins/core/core.qrc` already exposes screenshot icons under `/Core/Custom`.
- `main.cpp` already consumes `APP/PendingRestartArguments`, so the main-window button can request `QStringList{ "--ui-mode=minibar" }` without changing startup parsing.

Implementation plan:

1. Add `showpanel.png` and `showpanel_light.png` to `src/plugins/core/core.qrc` with stable `/Core/Custom` aliases.
2. Extend the screenshot button QSS in both themes so `btnShowPanel` shares the same size, padding, background, and pressed state.
3. In `MainWindow::registerDefaultActions()`, create `btnShowPanel` immediately after `btnScreenshot` and connect it to a new click handler.
4. Add `MainWindow::onShowPanelButtonClicked()`:
   - create a `Controls::MessageDialog` parented to `MainWindow`;
   - ask whether switching to minibar mode should continue;
   - on cancel, return without writing settings or closing windows.
5. Add `MainWindow::requestApplicationRestartToMinibarMode()`:
   - set `m_showExitDialog=false`;
   - suspend TX updates and deactivate the active business like existing restart flows;
   - write `APP/PendingRestart=true`, `APP/PendingRestartArguments=("--ui-mode=minibar")`, and `APP/Reboot=True`;
   - call `qApp->closeAllWindows()`, then post a quit request with a short event-loop fallback.

Success criteria:

- Main-window mode shows the new show-panel button immediately to the right of the screenshot button.
- Dark theme uses `showpanel.png`; light theme uses `showpanel_light.png`.
- Canceling the confirmation keeps the main window fully usable and does not alter restart settings.
- Confirming exits through the existing main-window close path and restarts into minibar mode.

Implementation:

- `MainWindow::registerDefaultActions()` now creates `btnShowPanel` immediately after `btnScreenshot` in the existing `outputModeWidget`.
- `btnShowPanel` opens a `Controls::MessageDialog` confirmation. The cancel branch only returns from the callback.
- The confirm branch writes `APP/PendingRestartArguments` as `QStringList{ "--ui-mode=minibar" }`, preserves `APP/Reboot=True`, closes all windows, posts a quit request, and keeps a 3 second event-loop fallback with a warning log.
- `src/plugins/core/core.qrc` now exposes `/Core/Custom/showpanel.png` and `/Core/Custom/showpanel_light.png`.
- `configuration_files/standard_cn/theme.css` and `configuration_files/standard_cn/theme_light.css` share the screenshot button geometry/pressed style with `btnShowPanel`; only the theme-specific icon path differs.

Style source correction:

- Per user direction, the title-bar `btnShowPanel` QSS belongs in the packaged profile source files under `configuration_files/standard_cn/`, not the generated/runtime `configuration/` copies.
- Move the dark/light `btnShowPanel` QSS into `configuration_files/standard_cn/theme.css` and `configuration_files/standard_cn/theme_light.css`.
- Keep `configuration/theme.css` and `configuration/theme_light.css` without `btnShowPanel` changes so those runtime copies are not the source of record for this change.

## Win32 Debug Cancel Crash

User field report:

- In Win32 debug mode, clicking `Restore` and then choosing `No` crashes at the `dialog.exec()` line.
- Qt Creator stack shows heap validation / delete path ending in `Controls::MessageDialog::~MessageDialog`.

Observation:

- `MessageDialogPrivate::initialize()` sets `WA_DeleteOnClose=true`.
- The Restore implementation used a stack object: `Controls::MessageDialog dialog(this)`.
- On button click, `Dialog::onButtonClicked()` calls `QDialog::done(id)`.
- With `WA_DeleteOnClose=true`, closing the dialog lets Qt delete the dialog object.

Inference:

- The cancel path makes Qt delete a stack-allocated `MessageDialog`, causing invalid heap/free validation failure in Win32 debug builds.
- The crash is not caused by restart settings or device shutdown; it happens before any `Yes` branch runs.

Fix:

- Explicitly set `WA_DeleteOnClose=false` on the stack-scoped Restore confirmation dialog before showing it.
- Keep `WA_QuitOnClose=false` as the minibar/tool-host application-exit guard.
- Leave the cancel path side-effect-free.

## Win32 Yes Leaves Background Process

User field report:

- In Win32 debug mode, clicking `Restore`, choosing `Yes`, and watching the UI disappear leaves `SGStudio.exe` running in Task Manager.

Observation:

- `MiniBarWindow` uses `Qt::Tool | Qt::FramelessWindowHint | Qt::WindowStaysOnTopHint`.
- `qApp->closeAllWindows()` closes visible top-level windows, but Qt's automatic application quit is tied to primary-window / `lastWindowClosed` semantics.
- A `Qt::Tool` minibar can close without causing `QApplication::exec()` to return.

Inference:

- The process is not necessarily stuck in device shutdown; the event loop may simply still be running because the minibar is not a primary main window.
- MainWindow language/theme restart gets automatic quit behavior from the real `QMainWindow`; minibar restore needs an explicit quit request.

Fix plan:

- After `qApp->closeAllWindows()`, post an explicit `QCoreApplication::quit()` request.
- Add a short timeout guard that calls `QCoreApplication::exit(0)` if the app is still running after the normal quit request.
- Keep the normal `aboutToQuit -> PluginManager::shutdown()` path as the first path; the timeout is only a fallback for the event loop not leaving.

Additional shutdown protection:

- `PluginManagerPrivate::shutdown()` waited forever for `AsynchronousShutdown` plugins.
- The current source has an asynchronous shutdown path in the Updater plugin when its startup online check thread is still running.
- Added a bounded asynchronous shutdown wait in `PluginManagerPrivate::shutdown()`:
  - normal `asynchronousShutdownFinished` still exits the wait immediately;
  - after timeout, unfinished plugin names are logged, the wait is released, and plugin delete proceeds.

Implementation:

- `MiniBarWindow::requestApplicationRestartToMainMode()` now calls `qApp->closeAllWindows()` and then posts `QCoreApplication::quit()`.
- The same path schedules a 3 second fallback that calls `QCoreApplication::exit(0)` if the application event loop has not left.
- `PluginManagerPrivate::shutdown()` now bounds asynchronous plugin shutdown waits to 10 seconds and logs unfinished plugin names before continuing.

Static verification:

```text
git diff --check -- src/plugins/core/minibarwindow.cpp src/libs/extensionsystem/pluginmanager.cpp .github/TaskLog/2026-07-02_minibar_restore_full_mode_plan.md
```

Result:

- Passed.
- Git still reports existing LF-to-CRLF working-copy warnings for touched source files.

## Raspberry Pi Main-Mode Dialog Inherits Layer-Shell Environment

User field report:

- Start SGStudio in `--ui-mode=minibar` on Raspberry Pi Wayland.
- Click `Restore` and confirm the restart to normal main-window mode.
- In the restarted main-window process, click the main close button.
- The exit confirmation `MessageDialog` appears at the screen top-left.

Observation:

- `CorePlugin::enableLayerShellForMiniBarIfNeeded()` only calls `LayerShellQt::Shell::useLayerShell()` when parsed startup mode is `MiniBar`.
- `LayerShellQt::Shell::useLayerShell()` enables Qt Wayland layer-shell through the process environment variable `QT_WAYLAND_SHELL_INTEGRATION=layer-shell`.
- `MiniBarWindow::requestApplicationRestartToMainMode()` writes `APP/PendingRestartArguments = ("--ui-mode=main")`.
- `main.cpp` consumes the pending restart and currently calls static `QProcess::startDetached(QApplication::applicationFilePath(), restartArguments)`.
- Static `QProcess::startDetached(...)` inherits the current process environment, including the layer-shell variable set during minibar mode.

Inference:

- The failure is not caused by the OS targeting the same process name.
- The restarted main-window process inherits a stale layer-shell environment from the previous minibar process.
- Because the child process is `--ui-mode=main`, `CorePlugin` does not explicitly configure main-window dialogs as layer-shell transients, so ordinary `MessageDialog` top-level windows can fall back to layer-shell default placement.

Implementation plan:

- Replace the static restart launch with a small helper in `src/app/main.cpp`.
- Build a `QProcessEnvironment` from the current environment, remove `QT_WAYLAND_SHELL_INTEGRATION`, and use it for the detached restart process.
- Keep `APP/PendingRestartArguments` behavior unchanged; `--ui-mode=minibar` restarts can still enable layer-shell again through `CorePlugin`.
- Do not change `MainWindow::closeEvent()` or generic `MessageDialog` geometry.

Success criteria:

- A minibar-to-main restart launches the next process without inherited `QT_WAYLAND_SHELL_INTEGRATION`.
- Main-window close confirmation uses normal Qt main-window dialog behavior instead of layer-shell default top-left placement.
- Main-window-to-minibar restart remains supported because the child process still receives `--ui-mode=minibar` and enables layer-shell itself.
- Existing fallback restart argument `reboot` is preserved.

Implementation:

- Updated `src/app/main.cpp`.
- Added a restart-launch helper that creates a `QProcessEnvironment` from the current process environment and removes `QT_WAYLAND_SHELL_INTEGRATION` before `startDetached(...)`.
- Replaced the static `QProcess::startDetached(...)` call in the pending-restart path with the helper.
- Left `APP/PendingRestartArguments` unchanged, so `--ui-mode=main`, `--ui-mode=minibar`, and the legacy `reboot` fallback still use the same settings handoff.
- Added the restart environment rule to `.github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md`.

Static verification:

```text
git diff --check -- src/app/main.cpp .github/TaskLog/2026-07-02_minibar_restore_full_mode_plan.md .github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md
```

Result:

- Passed for tracked source changes.
- Git still reports the existing LF-to-CRLF working-copy warning for `src/app/main.cpp`.
- In this working tree, `.github/TaskLog` and `.github/KnowledgeBase` are not reported as tracked paths by `git ls-files`, so their local edits do not appear in `git diff`.
