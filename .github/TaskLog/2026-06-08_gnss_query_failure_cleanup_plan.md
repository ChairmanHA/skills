# 2026-06-08 GNSS Query Failure Cleanup

## Problem

- `device_query_gnss()` 当前因底层 API 问题持续返回 `-8`，导致所有“先 `device_config_gnss()` 再 `queryGnssSettings()` 回读确认”的链路都无法成立。
- 现有代码中仍然保留了多处基于 `queryGnssSettings()` 的保存、刷新、确认逻辑，这些逻辑在当前设备环境下已退化为无效分支或早退。
- 需要把 GNSS 配置路径收敛为“本地缓存 + 明确下发”，同时区分真正冗余的 query 回写链路与仍然必要的默认值下发入口。

## Local Analysis

- `MainWindow::currentDeviceOpenStateChanged -> CommonDeviceProfile::applyGnssSettingsToCurrentDevice()` 不是冗余：在 `StartSetting=Default` 时没有 settings 文件加载，默认 GNSS 只有这条 open 后下发路径能真正打到设备。
- 真正冗余的是：
  - `saveSettingsFile()` 保存前主动 `refreshGnssSettingsFromCurrentDevice()`；
  - `applyGnssSettingsToCurrentDevice()` 里的 `queryGnssSettings()` 确认写回；
  - `GpsInfoDialog::refreshGnssConfig()` 对设备 query 的硬依赖；
  - `GpsInfoDialog::applyGnssConfig()` 在 configure 成功后的 query 确认分支；
  - `FancyDevice::open()` 中依赖 `device_query_gnss()` 的 GNSS bootstrap / confirm 日志块。

## Fix Plan

- 删除 `CommonDeviceProfile` 中仅服务于 query 回写的冗余状态与方法：移除保存前 query 刷新，以及 apply 后 query 确认。
- 保留 `applyGnssSettingsToCurrentDevice()` 作为“把缓存/default/load/preset 的 GNSS 下发到当前设备”的单一入口。
- 让 `GpsInfoDialog` 的配置区 UI 改为从 `CommonDeviceProfile::gnssSettings()` 同步，而不是从 `queryGnssSettings()` 拉取；用户编辑成功后直接更新缓存与本地 UI。
- 删除 `FancyDevice::open()` 里基于 `device_query_gnss()` 的 GNSS bootstrap 逻辑，避免无意义的失败日志和重复路径。

## Expected Result

- GNSS 的设置保存、恢复、preset、默认启动都依赖本地缓存与明确下发，不再被 `device_query_gnss()` 的 `-8` 失败拖垮。
- GPS 对话框配置区不再依赖无效 query 才能显示最新设置。
- 保留下发默认 GNSS 到设备所必需的 open 后 apply 入口，其余 query 回写冗余代码删除。