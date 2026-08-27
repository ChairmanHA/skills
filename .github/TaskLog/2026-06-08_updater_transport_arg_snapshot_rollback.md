# 2026-06-08 Updater Transport Arg Snapshot Rollback

## Goal

- 按当前设备更新语义收敛 updater 参数：
  - ETH 只依赖 `Interface=ETH` 与 `IP/Port`。
  - USB 才依赖 `Interface=USB` 与 `DeviceNum`。
- 回退上一轮把 `deviceNumber()` 无条件带入所有 transport 的修改，保持当前 updater 入口简单。
- 保留已完成的关键时序保证：参数仍在断链前构造并缓存到 `m_pendingMaintenanceArgs`。

## Local Hypothesis

- 上一轮 `UpdaterTransportSnapshot` helper 的核心收益只是“显式表达断链前快照”，但它同时把 ETH 场景也带上了 `deviceNumber()`，偏离了当前 updater 语义。
- 直接恢复 `buildMaintenanceArguments()` 中“ETH 用 IP/Port；USB 用 DeviceNum”的分支表达，可以在不影响断链前缓存这一事实的前提下保持实现更简单。

## Cheap Disproving Check

- 若当前代码是在断链后重新读取 `currentDevice` 才生成参数，则不能回退到简单分支写法。
- 当前检查结果：参数仍在断链前调用 `buildMaintenanceArguments()` 并保存到 `m_pendingMaintenanceArgs`，所以可以安全回退。

## Minimal Change Plan

1. 删除 `UpdaterTransportSnapshot` helper 与相关调用。
2. 恢复 `buildMaintenanceArguments()` 的简洁分支语义：ETH 只带 IP/Port，USB 才带真实 DeviceNum。
3. 增量编译 `Updater` 验证。
