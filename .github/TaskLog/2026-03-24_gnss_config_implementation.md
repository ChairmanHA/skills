# GNSS 配置全流程实现方案

## 目标
建立中立 `Core::GnssSettings` 结构体，打通 GPS 插件 → Core 抽象接口 → FancyDevice → H2 API 的配置通路；
所有 GNSS 设备方法加锁保护线程安全；Streaming 模式下自动禁用配置。

## 中立结构体 `Core::GnssSettings`

```cpp
struct GnssSettings {
    uint8_t antenna = 0;          // 0 = internal, 1 = external
    bool    xppsEnabled = false;
    double  xppsFrequencyHz = 1.0; // [0.25, 10 000 000] Hz
    double  ppsDelay = 0.0;        // 秒
};
```

## IDevice 新增接口

```cpp
virtual bool queryGnssSettings(GnssSettings *settings) { return false; }
virtual bool configureGnssSettings(const GnssSettings &settings) { return false; }
```
默认返回 false，表示设备不支持 GNSS。

## FancyDevice 实现

- `queryGnssSettings`: QMutexLocker(m_mutex) + Streaming-mode 拒绝 → device_query_gnss → 映射到 GnssSettings
- `configureGnssSettings`: QMutexLocker(m_mutex) + Streaming-mode 拒绝 → 映射到 gnss_setting → device_config_gnss

## DeviceRealTimeStatus 补充

- 新增 `uint64_t gnssNsSinceEpoch{0}` 字段（原始纳秒时间戳）
- `getRealTimeStatus` 非 Streaming 分支填充 `GNSSAntennaState = gnssSettgins.antenna`，`gnssNsSinceEpoch = gnss.ns_sinceepoch`

## GPS Dialog 改造

### 控件分类
| 类型 | 控件 | 数据源 |
|------|------|--------|
| 配置 | antenna, xpps_onoff, xpps | queryGnssSettings / configureGnssSettings |
| 显示 | noticeLabel, lon/lat/alt, satNum, snr_*, date/time | DeviceRealTimeStatus |
| 本地 | timeFormatComboBox | 纯 UI |

### 配置控件交互
- antenna ComboBox activated → configureGnssSettings
- xpps_onoff statusChanged → configureGnssSettings
- xpps QLineEdit editingFinished → configureGnssSettings（带 QDoubleValidator 0.25~10MHz）

### 配置刷新时机
- 对话框构造时（设备已连接）
- DeviceManager::currentDeviceOpenStateChanged(true)
- DeviceManager::currentDeviceChanged

### Streaming 自动禁用
- BusinessManager::currentActivedBusinessChanged → 检查 name()=="Streaming"
- 禁用 antenna / xpps_onoff / xpps / xpps_label
- 对话框构造时也检查当前 business

### 时间显示
- 使用 gnssNsSinceEpoch 代替分离的 year/month/day/hour/minute/second 字段（更精确，单一数据源）

## 线程安全
- FancyDevice 所有 GNSS 方法均持有 m_mutex
- GPS Dialog 运行在主线程，通过 DeviceManager::currentDevice() 跨线程调用 IDevice 方法，由 m_mutex 保护
