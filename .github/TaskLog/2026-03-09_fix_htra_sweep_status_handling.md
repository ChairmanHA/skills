# 修正 HTRA 扫描状态处理与上层调用

日期：2026-03-09

## 背景
- `FancyDevice::setFrequencySweep()` 基本符合 H2 API FSCAN 示例顺序。
- `FancyDevice::setLevelSweep()` 未正确传播底层返回码，且触发结构体未完整初始化。
- Sweep 上层业务对扫描返回值基本未处理，失败后仍继续查询/回写 UI。

## 本次目标
- 将 `setLevelSweep()` 调整为与 `setFrequencySweep()` 一致的调用与错误处理风格。
- 为 FSCAN/LSCAN 增加本地 `repeat` 缓存，避免查询接口固定回写 `-1`。
- 修正 sweep 上层对扫描接口的调用与返回值处理，避免错误被吞掉。

## 计划
1. 在 `FancyDevice` 中增加频率扫描/功率扫描的 `repeat` 缓存字段。
2. 重写 `setLevelSweep()` 的顺序与返回值传播，补齐 trigger 初始化。
3. 调整 `getFrequencySweep()` / `getLevelSweep()` 使用本地缓存回写 `repeat`。
4. 修正 `DeviceOperator` 与 sweep 业务层对扫描返回码和错误消息的处理。
5. 运行编辑器错误检查，确认本次修改未引入新的编译问题。

## 追加检查：温度链路
- 检查 `FancyDevice::updateRealTimeStatus()` -> `getRealTimeStatus()` -> `DeviceManager::deviceRealTimeStatusUpdated` -> `DeviceInfoWidget::updateDeviceRealTimeStatus()` 的温度传递链路。
- 确认当前实现中温度类型为 `float -> double -> QString::number(..., 'f', 1)`，不存在项目内将负温度翻转为正温度的显式转换。
- 在底层查询点、状态转发点、前端显示点增加 `qWarning()`，打印当前温度与连接状态，便于联调核对。