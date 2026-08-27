# Remote Updater exec Permission Denied Investigation

## Scope

Investigate and fix the firmware updater launch failure reported on target `192.168.3.179` when running SGStudio from the target machine and clicking the default firmware update path.

Reported user-visible error:

- `Failed to start updater: execvp: Permission denied`

## Verification Level

`debug-run` on the remote Linux target, focused on static/runtime filesystem state and the updater launch boundary. Build only if the deployed layout cannot be corrected by runtime/package permissions.

## Observations

- Local source path: `ProgressDialog::startUpdaterProcess()` starts the updater via `QProcess` using `args.first()` as the program path.
- Local source path: default update uses `PacketSpec::createNativeInfoPacket()`, which resolves the updater under the current runtime root's `updater/` directory.
- Remote login succeeds for `htra@192.168.3.179`.
- Remote host reports Linux/aarch64 on `raspberrypi`.
- `/home/htra/SGSProject/SGStudio` was not present during initial probe.
- `~/Desktop/SGSProject` was present during initial probe.
- Remote `bin/debug.log` showed the active runtime root as `/home/htra/Desktop/SGSProject/SGStudio`.
- The update handoff passed `/home/htra/Desktop/SGSProject/SGStudio/updater/Updater_Linux-2.0.24` to maintenance.
- Before the remote fix, that updater file existed but was mode `-rw-r--r--`; `SGStudio` and `maintenance` were already executable.
- The updater path is on a `rw,noatime` mount, with no `noexec` mount option observed.

## Assumptions

- The `execvp: Permission denied` came from the OS refusing to execute the resolved updater file because it lacked executable permission.
- The user's path likely refers to a deployed SGStudio runtime under the home/Desktop project tree, not necessarily the exact missing `/home/htra/SGSProject/SGStudio` path.

## Changes

- Remote runtime fix: set `/home/htra/Desktop/SGSProject/SGStudio/updater/Updater_*` to mode `755`.
- Source/package fix: update `updater_files/CMakeLists.txt` so Linux copied `Updater_*` files are chmodded executable in the build `updater/` directory.
- Git mode fix: mark the tracked Linux updater binaries under `updater_files/linux_aarch64/` and `updater_files/linux_x86/` as executable.

## Verification

- Remote `test -x /home/htra/Desktop/SGSProject/SGStudio/updater/Updater_Linux-2.0.24` returned success.
- Remote `ls -l` now reports `-rwxr-xr-x` for `Updater_Linux-2.0.24`.
- Remote mount check for the updater path reports `rw,noatime`; no `noexec` option was present.
- The stale maintenance process from the failed attempt was stopped.
- SGStudio was restarted on the remote GUI session and remained running as `/home/htra/Desktop/SGSProject/SGStudio/bin/SGStudio`.
- Final process check showed only `SGStudio`; no `maintenance` or `Updater_Linux*` process remained.
- Real firmware update was not launched from automation, to avoid changing device firmware outside the user's GUI-driven action.

## Success Criteria

- Identify the exact updater path passed to maintenance or the updater candidate selected by `PacketSpec`.
- Confirm whether the updater file exists and why `execvp` rejects it.
- Apply the smallest remote fix that makes the updater executable if this is a deployment permission issue.
- Verify the updater binary can be executed far enough to pass the OS exec boundary without `Permission denied`.
- If a packaging/source change is required, document and implement it after confirming the remote runtime state.
