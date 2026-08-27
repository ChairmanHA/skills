## 背景

- 当前 `GpsInfoDialog::applyGnssConfig()` 只绑定在 `antenna / xpps_onoff / xpps` 的用户编辑信号上，不会在设备 open 时自动调用。
- `FancyDevice::open()` 目前只做 `device_query_gnss()` 与 `device_query_gnss_info()`，并没有“首轮按默认策略 `device_config_gnss()`”这一步。
- UI 侧已有设备值回显链：`currentDeviceOpenStateChanged(true)`、`currentDeviceChanged`、以及对话框首次构造的初始状态都会走 `refreshGnssConfig() -> queryGnssSettings() -> 回写控件`。

## 局部假设

- 当前缺的不是“open 后 UI 回写”，而是“设备 open 时第一次 GNSS configure”。
- 若需求是 `pps_en` 默认应为 1，则最稳妥的落点应在 `FancyDevice::open()` 的 GNSS bootstrap 阶段：先 query 当前硬件值，必要时仅把 `pps_en` 补到 1，并对设备接受后的值再次 query 回本地缓存。
- 一旦这一步补齐，现有的 `refreshGnssConfig()` 链即可把 open 后的真实设备值写回前端 UI，无需把 open 职责塞进 GPS 对话框。

## 便宜校验

- 检查 `gpsdialog.cpp` 中 `applyGnssConfig()` 的调用点，确认是否只来自用户编辑信号。
- 检查 `FancyDevice::open()` 是否仅 query GNSS 而未 config。
- 检查 `GpsInfoDialog` 是否已经在设备 open / currentDevice 变化时调用 `refreshGnssConfig()`。

## 计划

- 在 `FancyDevice::open()` 的 GNSS bootstrap 中新增一次“首轮默认配置”逻辑：query 成功后若 `pps_en != 1`，则按当前设备参数为基础把 `pps_en` 设为 1 并下发，再 query 一次确认本地缓存。
- 保持 UI 侧继续通过 `refreshGnssConfig()` 做只读回写，不让 `applyGnssConfig()` 在 open 时承担设备初始化职责。
- 更新 GNSS 知识库，明确区分“设备 open 时的 GNSS bootstrap configure”与“对话框 query 后回显”的两条链。