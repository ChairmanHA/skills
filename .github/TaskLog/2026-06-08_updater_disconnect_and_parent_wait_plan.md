# 2026-06-08 Updater Disconnect And Parent Wait Plan

## Goal

- 在当前活跃 updater 机制上补回一段最小可用的 Phase 1：
  - 更新前进入 `firmwareUpdateDisconnectMode`。
  - 显式等待 `currentDeviceDisconnectFinished()` 后再交给 maintenance。
  - 主程序改为正常退出；maintenance 先等待父进程自然结束，仅在超时后才兜底强杀。
- 保留上一轮已完成的 handoff ready marker 与 updater exitCode 门控。
- 暂不重建 `_bak` 的 install phase / manifest / update-session 闭环。

## Local Hypothesis

- 当前控制点仍然集中在 `src/plugins/updater/updatedialog.cpp` 与 `src/maintenance/progressdialog.cpp` / `src/maintenance/main.cpp`。
- 只要把 `_bak` 的 `setFirmwareUpdateDisconnectMode(true) -> setCurrentDevice(nullptr) -> currentDeviceDisconnectFinished()` 前半段迁入 `UpdateDialog`，再让 maintenance 使用 `--parent-pid` 等待父进程退出后才启动 updater，就能在不重建 `UpdateCoordinator` 的前提下完成这一步。

## Cheap Disproving Check

- 若当前 `UpdateDialog` 已进入 `firmwareUpdateDisconnectMode` 或 maintenance 已等待父进程退出，则该假设不成立。
- 当前检查结果：都还没有，因此假设成立。

## Minimal Change Plan

1. 在 `UpdateDialog` 中加入更新状态机：保存待更新 spec/args，进入 firmware update disconnect mode，等待 `currentDeviceDisconnectFinished()`。
2. 断链完成后再启动 maintenance；收到 ready marker 后不再 `taskkill`，改为关闭主窗口并走正常 shutdown。
3. 为 maintenance 增加 `--parent-pid` 解析与父进程等待；超时后才强杀并继续 updater。
4. 用现有 Debug build tree 做 Updater + maintenance 窄编译验证，并同步更新知识库文档。
