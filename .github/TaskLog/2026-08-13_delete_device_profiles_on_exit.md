# Exit cleanup for device-switch profiles

日期：2026-08-13  
状态：已完成  
验证级别：`static`

## Scope

在应用退出时删除设备切换过程中写入的 `configuration/device_profiles` 下的 JSON profile 文件，保留目录本身。退出清理必须发生在现有插件 shutdown 完成之后，避免退出前的 profile 保存被清理逻辑之后再次写回。

## Evidence and assumption

- `DeviceRuntimeProfileCoordinator` 使用 `../configuration/device_profiles/h2_<uidHigh>_<uidLow>.json` 保存设备 profile。
- `MainWindow::closeEvent()` 在正常退出确认后保存当前设备 profile。
- `QApplication::aboutToQuit` 当前已连接 `PluginManager::shutdown`，因此清理连接追加在该连接之后。
- 本次只清理该目录下的 `.json` 文件，不递归删除目录或其他类型文件。

## Success criteria

- 应用退出时，`configuration/device_profiles` 下由设备切换产生的 JSON profile 文件被删除。
- `configuration/device_profiles` 目录保留，其他配置文件不受影响。
- 修改保持在应用退出入口，设备切换和 profile 读写流程不变。
- 已通过 `git diff --check` 和静态引用检查；按仓库规则未编译、未运行。
