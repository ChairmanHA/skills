# 2026-04-10 Restart After Full Shutdown Plan

## Goal

- 修复切换语言、主题、显示模式后重启时，旧进程尚未完全退出就拉起新实例，导致手工 ETH 设备无法重新连接的问题。
- 保持现有重启后自动恢复 Profile.json 的启动语义不变。

## Observed Facts

- 手工 ETH 断开后重新连接正常，说明单纯的 `device_close()` 路径已经基本成立。
- 调试时，`FancyDevice::close()` 里的 ETH `device_close(&device)` 断点会命中，但新的程序实例仍会立即启动。
- 当前三个重启入口（语言、主题、显示模式）都在 UI 回调里执行：
  - `qApp->closeAllWindows();`
  - `QProcess::startDetached(qApp->applicationFilePath(), {"reboot"});`
- `PluginManager::shutdown()` 挂在 `QApplication::aboutToQuit` 上，说明真正的插件析构、设备释放、线程退出都发生在“窗口关闭之后、事件循环退出之前”。

## Root Cause

- 当前实现把“请求退出”和“启动新实例”放在同一个 UI 回调里同步执行。
- `closeAllWindows()` 只会触发窗口关闭流程与 `MainWindow::closeEvent()`，并不等于当前进程已经完成 `aboutToQuit -> PluginManager::shutdown -> CorePlugin/MainWindow/DeviceManager/FancyDevice` 的完整析构链。
- 因此即使旧实例里的 ETH `device_close()` 已经进入执行，新的实例仍可能在旧进程尚未完全释放 SDK/transport 状态时启动并尝试 `device_open_eth(...)`，形成跨进程时序竞争。
- 用户补充的断点现象恰好说明：问题不是“close 分支完全没走到”，而是“新进程启动得太早”。

## Plan

- 把“是否需要重启”的决定保留在 settings controller 中，但移除这里的 `QProcess::startDetached(...)`。
- 新增一个统一的“请求应用重启”辅助函数：
  - 写入 `APP/Reboot=True`
  - 写入新的待重启标记，例如 `APP/PendingRestart=True`
  - 调用 `qApp->closeAllWindows()` 进入正常退出链路
- 在 `main()` 中改为：
  - `int exitCode = app.exec();`
  - 事件循环返回后再读取 `APP/PendingRestart`
  - 若为真，先清除该标记并 `sync()`，再 `QProcess::startDetached(...)`
  - 最后 `return exitCode;`
- 这样可以保证新实例只会在旧实例完成 Qt 事件循环、插件 shutdown、对象析构之后才启动。

## Scope

- 修改 `src/plugins/core/mainwindowsettingscontroller.h/.cpp`
- 修改 `src/app/main.cpp`
- 不改 HTRA ETH open/close 语义，不改 DeviceManager 设备切换逻辑。

## Validation

- 对改动文件做静态错误检查。
- 如环境允许，构建 Debug 或至少检查 Core / app 相关编译错误。