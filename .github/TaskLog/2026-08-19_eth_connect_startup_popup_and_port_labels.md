# ETH Connect startup popup and port labels

Date: 2026-08-19  
Status: implemented; static verification complete  
Verification level: static

## Scope

- Display `port 1` beside the fixed 5000 row and `port 2` beside the fixed 5001 row.
- After extensions initialization completes and the main window is shown, open the existing ETH Connect dialog once automatically.
- Do not auto-submit, select, or modify endpoint values beyond the dialog's existing defaults; the user still clicks `Connect`.

## Implementation boundary

- `EthConnectDialog` changes only the two form-row labels.
- `MainWindowDeviceController::openEthConnectDialog()` becomes callable by the startup owner while remaining the existing menu action handler.
- `MainWindow::completeExtensionsInitialization()` queues one zero-delay call after `show()` and startup release-note handling so all controller dependencies are ready.

## Success criteria

- The two visible rows read `port 1` and `port 2` while still showing 5000 and 5001.
- A normal startup creates and shows one ETH Connect dialog without connecting automatically.
- Repeated startup initialization cannot create duplicate dialogs because the existing controller guard raises an existing dialog.
- The existing menu action and connection/transaction behavior remain unchanged.
- All touched files remain listed in their CMake source list and `git diff --check` passes.

No build or runtime test is planned unless explicitly requested, following the repository-default static workflow.

## Implementation result

Implemented on 2026-08-19. The labels are fixed in `EthConnectDialog`, and `MainWindow::completeExtensionsInitialization()` queues the existing dialog after startup initialization. The dialog opens without submitting a connection request.

Static verification confirms the startup call, existing duplicate-dialog guard, unchanged port defaults, CMake inclusion, and `git diff --check`.
