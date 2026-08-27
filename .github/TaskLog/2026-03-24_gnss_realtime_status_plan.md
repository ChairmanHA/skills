# GNSS 实时状态接入计划

## 目标

- 将 GNSS 实时信息接入现有 `DeviceManager -> DeviceRealTimeStatus -> UI` 链路。
- 查询策略与温度、供电一致：仅在设备已打开且**非 Streaming 模式**时轮询。
- 暂不处理 GNSS 配置写入，只做只读实时展示。
- HTRA 设备默认视为带 GNSS 标配，不再把“是否支持 GNSS”建立在一次查询是否成功之上。

## 设计

### 1. 设备层

- 在 `FancyDevice::updateRealTimeStatus()` 中追加 `device_query_gnss_info()`。
- 当前收敛后的断联规则：温度查询失败即可视为设备已失联，并统一走断联清理。
- 若温度查询成功但供电或 GNSS 查询失败：
  - 不把该失败升级为全局 `errorCode`，避免引入新的运行时错误弹窗噪音。
  - 清空当前缓存的供电或 GNSS 动态值，UI 视为“当前无有效数据”。

### 2. 状态结构回填

- `FancyDevice::getRealTimeStatus()` 在非 Streaming 分支回填：
  - `latitude / longitude / altitude`
  - `GNSS_LockState`
  - `SatsNum / SNR_Max / SNR_Min / SNR_Avg`
  - 通过 `device_epoch_to_utc()` 把 `ns_sinceepoch` 转成 `year/month/day/hour/minute/second`
- Streaming 分支显式清空 GNSS 动态字段，避免沿用旧值。

### 3. 前端状态总线

- `MainWindow::onDeviceRealTimeStatusUpdated()` 补写 `DeviceRealTimeStatus` property。
- 设备断开时也同步写回空状态，保证依赖 property 的界面不会残留旧值。

### 4. UI

- GPS 插件对话框改为只读显示实时状态，不接旧项目中的外接 GPS / PPS / 串口业务。
- 现有 Core 内置 `gpsinfodialog` 也一并修正明显显示问题：
  - 纬度错误使用了经度字段。
  - 时间显示固定 `+8` 不正确。
  - 断开时不应保留旧值。

## 非目标

- 不实现 GNSS 设置下发。
- 不实现 constellation 配置回写。
- 不调整 Streaming 业务语义，只保持“Streaming 下不查 GNSS”。
