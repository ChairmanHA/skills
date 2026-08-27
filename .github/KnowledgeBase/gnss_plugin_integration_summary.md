# GNSS 插件实现总结

## 目标

本次改动的目标是把 GNSS 能力从原先 Core 内部对话框迁移为独立 GPS 插件，并打通以下链路：

1. 菜单入口：System 菜单中的 `GNSS` 动作打开独立对话框。
2. 实时显示：设备层采集 GNSS 状态，Core 将 `DeviceRealTimeStatus` 发布到 `PropertyManager`，GPS 插件订阅显示。
3. 配置写回：前端 `antenna`、`XPPS Out`、`XPPS` 通过 `IDevice` 新增的中立接口落到 H2 `device_config_gnss()`。
4. Streaming 保护：Streaming 模式下前端自动禁用 GNSS 配置，设备层同时拒绝 GNSS query/configure。
5. 时间显示：设备层保留原始 `gnssNsSinceEpoch`，同时下沉完成 UTC 年/月/日/时/分/秒/毫秒换算；GPS 插件只消费 `DeviceRealTimeStatus`，不直接依赖 H2。

## 主要改动

## 插件化拆分

- 新增 `src/plugins/gps` 插件目录与 `GPSPlugin`。
- Core 中旧的 `gpsinfodialog.*` 被移除，不再由 `MainWindow` 直接持有 GNSS 对话框。
- `plugin.cpp` 在 `MENU_SYSTEM` 下注册 `GNSS` 菜单动作，按需懒创建对话框。

## 设备状态发布

- `MainWindow::onDeviceRealTimeStatusUpdated()` 在更新设备状态面板之前，将 `DeviceRealTimeStatus` 写入 `PropertyManager` 的 `DeviceRealTimeStatus` 属性。
- 当当前设备为空时，`MainWindow` 会主动写入一个空的 `DeviceRealTimeStatus`，避免 GPS 插件停留在陈旧显示。
- GPS 插件不再 `#include <h2_api.h>`，也不再链接 `HTRA::HTRA`；底层时间转换边界保留在设备插件内部。

## 中立 GNSS 配置接口

在 `Core::IDevice` 中新增：

```cpp
struct GnssSettings {
    uint8_t antenna = 0;
    bool    xppsEnabled = false;
    double  xppsFrequencyHz = 1.0;
    double  ppsDelay = 0.0;
};

virtual bool queryGnssSettings(GnssSettings *settings) { return false; }
virtual bool configureGnssSettings(const GnssSettings &settings) { return false; }
```

目的：

- 让 GPS 插件只依赖 `Core::IDevice`，不直接依赖 `gnss_setting`。
- 把 H2 设备私有结构隔离在 HTRA 插件内部。
- 后续若有其他设备实现 GNSS，只需实现同名接口即可复用对话框。

## FancyDevice 实现

- `queryGnssSettings()`：加锁后调用 `device_query_gnss()`，回填 `GnssSettings`。
- `configureGnssSettings()`：加锁后把 `GnssSettings` 映射回 `gnss_setting`，调用 `device_config_gnss()`。
- `open()`：不再主动做 GNSS bootstrap configure。GNSS 默认值/保存恢复/preset 都由 `CommonDeviceProfile` 持有缓存，并在设备 open 后通过单独的 GNSS apply 路径下发。
- `getRealTimeStatus()`：填充 GNSS 经纬度、卫星数、SNR、天线状态、`gnssNsSinceEpoch`，以及已换算好的 UTC 年/月/日/时/分/秒/毫秒字段。
- `updateRealTimeStatus()`：遵循“温度查询失败即可认定断联”的新规则；供电/GNSS 查询失败仅清空各自缓存。

## GNSS 与 CommonDeviceProfile 的边界

GNSS 现在确实放进了 `CommonDeviceProfile`，但它承担的是“缓存 owner”，不是“统一 runtime 配置 owner”。这里要刻意区分两条链：

### 1. 普通 CommonProfile/runtime 配置链

- 代表字段：`RefClockSource`、`RefClockFrequency`、`RfPort`、`LoMode`、`Trigger*`
- 路径：Property -> `CommonDeviceProfile` -> `IBusiness::getCurrentProfile()` / `TxSessionService` / `TxPipelineRuntime` -> `IDevice::Profile` -> `FancyDevice::configuration()`
- 语义：这些字段属于发射 runtime 环境的一部分，会跟随业务 apply 一起下发。

### 2. GNSS 持久化/默认值链

- 代表字段：`antenna`、`xppsEnabled`、`xppsFrequencyHz`、`ppsDelay`
- 路径：`CommonDeviceProfile` 只负责保存默认值、save/load settings、preset 后的缓存；真正下发设备时不经过 `IDevice::Profile`，而是单独调用 `IDevice::configureGnssSettings()`。
- 调用入口分两类：
    - 用户在 `GpsInfoDialog` 中编辑时，直接走 `applyGnssConfig() -> configureGnssSettings()`；
    - 正常启动 / load / preset 时，走 `CommonDeviceProfile::applyGnssSettingsToCurrentDevice()`。

这样设计的原因是：GNSS 配置不属于当前 `TxPipelineRuntime` 负责的发射业务 runtime 组合；它更像设备侧的持久化辅助设置，但又必须参与默认值、保存恢复和 preset，所以适合把缓存 owner 放在 `CommonDeviceProfile`，而不把字段硬塞进 `IDevice::Profile` 的统一业务 apply 链。

## GPS 对话框行为

### 显示面

- 订阅 `DeviceRealTimeStatus` 属性并刷新经纬度、海拔、卫星数、SNR、日期时间。
- 时间格式支持 Local / UTC 切换。

### 配置面

- `antenna`、`xpps_onoff`、`xpps` 发生编辑后调用 `applyGnssConfig()`。
- 写回前先通过 `queryGnssSettings()` 保留当前 `ppsDelay`，因为当前 UI 没有 `ppsDelay` 控件。
- 写回成功后立即再查询一次设备实际值，并用 `QSignalBlocker` 回写控件，保证前端展示的是设备最终接受的值。

### 生命周期/模式联动

- `currentDeviceOpenStateChanged` 时同步连接态，其中 `true` 时刷新 GNSS 配置。
- `BusinessManager::currentActivedBusinessChanged` 检测到 `Streaming` 时禁用配置控件。
- 每次 GNSS 实时状态刷新或 XPPS 配置写回后，都会重刷一次 TriggerSource feature specs；若 XPPS 因 `pps_en=0` 或失锁而失效，公共 `TriggerSource` 会默认退化到 `BUS`。

这里要区分两条链：

1. 设备默认/恢复链：`CommonDeviceProfile` 持有 GNSS 缓存，并在设备 open、load settings、preset 后通过 `applyGnssSettingsToCurrentDevice()` 单独下发。
2. UI 回显链：`GpsInfoDialog` 不会在 open 时自动调用 `applyGnssConfig()`；它只会在设备 openState 变化、对话框 show，或者用户手动编辑后，走 `queryGnssSettings()` / `configureGnssSettings()` 与设备同步。

因此“默认/preset/load 驱动的 GNSS 设备下发”和“前端 UI 显示设备实际接受值”是两条相邻但分离的链路。

这里的退化策略是“只降级，不自动恢复”：当 XPPS 重新可用时，系统不会偷偷把当前触发源从 `BUS` 自动切回 `XPPS`，避免改写用户当前意图。

## 当前剩余不足

以下问题是在当前实现基础上仍建议尽快处理的部分。

### 1. GPS 对话框缺少 GNSS 配置失败的前端反馈

位置：`src/plugins/gps/gpsdialog.cpp`

当前 `applyGnssConfig()` 在以下情况下都只是静默返回：

- `queryGnssSettings()` 失败
- `configureGnssSettings()` 失败
- 配置后确认查询失败

这会造成用户侧症状是“点了没反应”，尤其是在：

- 设备刚断开但 UI 尚未完全刷新
- 设备不支持 GNSS
- 设备参数被底层拒绝

建议：

- 最少给出 `noticeLabel` 级别的失败提示。
- 更稳妥的做法是通过 `DeviceManager::postMessage()` 进入统一消息系统。

### 2. `xpps` 的显示精度被固定成 2 位小数，和输入能力不一致

位置：`src/plugins/gps/gpsdialog.cpp`

当前 `QDoubleValidator` 允许 9 位小数，但：

```cpp
QString::number(value, 'f', 2)
```

在 `refreshGnssConfig()` 与配置确认回写时都只保留了 2 位小数。

结果：

- 对 `0.25 Hz` 以上的低频段问题不大；
- 但对更高精度的 XPPS 频率配置，前端会把设备真实值截短显示，用户再次提交时可能造成不必要的二次改写。

建议：

- 显示精度与校验精度至少部分对齐，比如 6 到 9 位；
- 或按量级动态格式化，避免高频值显示过长、低频值又丢精度。
