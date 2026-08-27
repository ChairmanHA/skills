# Streaming 模式隐藏无效温度显示

日期：2026-03-20

## 背景
- HTRA 设备在 Streaming 模式下占用数据通道，无法执行实时温度查询。
- 当前 `FancyDevice::getRealTimeStatus()` 在流模式下未写入温度字段，前端收到默认值 `0.0` 后会显示为 `0.0℃`，与真实语义不符。
- `DeviceInfoWidget` 同时显示温度数值与 `℃` 单位，需要在“温度无效”时整组隐藏，而不是显示误导性的占位值。

## 本次目标
- 在实时状态层引入“温度无效”的显式哨兵值，供设备层和 UI 共享。
- 让 HTRA 流模式在 `getRealTimeStatus()` 中返回该哨兵值，明确表达“当前无法查询温度”。
- 让 `DeviceInfoWidget` 在收到该哨兵值时使用空白占位字符替代温度文本与摄氏度单位，保留布局宽度，避免切换到流模式时发生位移抖动；同时按当前字体为 `℃` 预留足够宽度，避免单位被裁切；恢复有效温度后自动重新显示。

## 计划
1. 在 `core/idevice.h` 中定义共享的无效温度哨兵值常量。
2. 修改 `FancyDevice::getRealTimeStatus()`，在流模式分支回填无效温度哨兵值。
3. 修改 `DeviceInfoWidget` 的温度显示逻辑，基于哨兵值隐藏/恢复温度与单位。
4. 运行编辑器错误检查，确认本次改动未引入新的静态错误。

## 追加调整：AboutDialog 实时供电信息
- HTRA API 已提供 `device_query_supply(void** device, supply_info* supplyInfo_o)`，返回 RF/USB 端口的电压与电流。
- 需要将供电信息纳入设备实时状态链路，与温度一样由 `DeviceManager` 定期轮询并经 `deviceRealTimeStatusUpdated` 分发。
- `AboutDialog` 需要在显示后持续实时更新 `RFPwrValueLabel` / `USBPwrValueLabel`，显示格式为 `10.990V 0.800A 8.792W`。
- Streaming 模式下数据通道被占用，供电信息与温度一样视为不可查询，AboutDialog 中应显示为空字符串而不是保留旧值。

## 目前测试出来有的问题
- `RFPwrValueLabel` / `USBPwrValueLabel`，显示格式为 `10.990V 0.800A 8.792W`。show出来的时候也是按照这个宽度设置的，如果后续更新了数据，比如 8.792W 变成了18.792W，就会被裁切掉一部分单位。需要预留足够宽度，避免被裁切掉，不要在运行过程中动态伸缩，造成抖动。