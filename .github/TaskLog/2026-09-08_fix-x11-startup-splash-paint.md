# Fix X11 startup splash painting

## Scope

- Fix the standard SGStudio startup splash on the Linux X11/xcb runtime.
- Preserve the existing Win32 and Linux Wayland/Raspberry Pi behavior.
- Limit the source change to `src/app/main.cpp`; do not change packaging or image resources because the packaged JPEG plugin and embedded resource were verified present.

## Verification level

- `debug-run` equivalent runtime verification on the Ubuntu 18.04 x86_64 X11 VMware guest, using its packaged Release workflow.

## Observations

- The packaged splash resource is a valid 800x450 JPEG and the x86_64 package contains and loads `bin/imageformats/libqjpeg.so`.
- X11 creates an 800x450 SGStudio splash window, but captures at 0.4 s and 1.1 s show the underlying desktop instead of the image.
- Startup calls `processEvents()` only once immediately after `show()`, then later blocks the GUI thread with `QThread::msleep()` to satisfy the minimum splash duration.

## Inference

- The xcb window is mapped before its asynchronous expose/paint work is serviced. Plugin initialization and the final GUI-thread sleep prevent X11 from painting the splash before it is closed.

## Plan

1. Detect the active Qt `xcb` platform at runtime after `QApplication` construction.
2. On xcb only, immediately after `show()`, run the splash event loop for the full 1.5-second minimum before plugin loading begins. At this point the main window and its show timer do not exist, so X11 paint events can run without exposing the application window early.
3. Keep the painted splash visible while plugins initialize.
4. After plugin loading, let the existing main-window show timer run and use `QSplashScreen::finish(mainWindow)` so the splash closes only after the main window is exposed.
5. Keep Win32 and Wayland on their existing elapsed-time/sleep/close path.
5. Perform static diff/syntax review, sync the focused patch to the clean VMware source checkout, rebuild the x86_64 package once, and capture the splash window during startup.

## Success criteria

- The X11 splash capture shows the embedded blue SGStudio image rather than the desktop through a transparent window.
- The splash remains approximately 1.5 seconds and the main window starts normally.
- The platform branch is runtime-gated to `xcb`, leaving Win32 and Wayland on the pre-existing path.
- The x86_64 package build completes without errors.

## Risks

- The X11 path deliberately shows the splash for 1.5 seconds before plugin initialization, so its total duration is 1.5 seconds plus plugin initialization time. This is the direct way to satisfy both the minimum-visible-duration and main-window ordering constraints without letting the Core zero-delay show timer run early.

## Implementation

- Added an exact runtime `xcb` platform check.
- On xcb only, the splash repaints synchronously immediately after `show()`.
- The first verification revision used `QTimer` plus a local `QEventLoop` for the remaining delay. Follow-up observation showed that this processed `CorePlugin::extensionsInitialized()`'s queued zero-delay timer and allowed the main window to show before the splash closed.
- The corrected implementation retains the original `QThread::msleep()` minimum-duration wait on every platform. Only the synchronous xcb repaint is new.
- Follow-up field testing showed that repaint plus the original blocking wait returned to the transparent-frame behavior. The final design therefore gives xcb a dedicated 1.5-second event-loop phase before Core/MainWindow creation, then finishes the splash against the exposed main window.

## Verification results

- `git diff --check`: passed locally and on the VMware source checkout.
- Remote build command: `JOBS=4 bash scripts/build.sh ubuntu-x86_64 standard cn SGStudio --watermark off`.
- Remote build result: `BUILD_EXIT=0`; package and direct-launch dependency audits passed.
- Archive: `/home/harogic/Desktop/sgstudio/build/linux_ubuntu_x86_64/standard/cn/SGStudio.tar.gz`.
- The archive was extracted back to `/home/harogic/Desktop/sgstudio/build/linux_ubuntu_x86_64/standard/cn/SGStudio` for runtime verification.
- X11 capture at 0.4 s and 1.1 s shows the embedded blue splash; ImageMagick comparison with the source JPEG reported RMSE `26.004 (0.000396795)` at both points.
- At 0.25 s the 800x450 splash window is `IsViewable`; at 1.70 s it is `IsUnMapped`, while the 1280x800 SGStudio main window is present.

## Follow-up success criterion

- Before 1.5 s, the splash is viewable and the main window is not viewable.
- After the splash closes, the main window becomes viewable.
