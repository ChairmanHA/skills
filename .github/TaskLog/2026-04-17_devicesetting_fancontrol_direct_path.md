# Device Settings Fan Control Direct Path

## Goal

- 在 Device Settings 页面右侧区域新增独立的 Fan Control group。
- group 内提供一个 EnumTextButton：Fan Mode，选项固定为 `On / Off / Auto`。
- 不走 CommonDeviceProfile、TxApplyRequest、主业务 profile 保存/恢复链路，直接通过当前设备快捷接口下发。

## Design

- 在 `Core::IDevice` 增加可选风扇控制接口：`queryFanMode()` / `configureFanMode()`，默认返回不支持。
- `HTRA::FancyDevice` 直接封装 `device_set_fan()`，并在设备实例内缓存最近一次设置的模式。
- 由于当前 H2 API 只有 `set fan`，没有 `query fan`，UI 侧回显使用设备实例缓存，不做硬件真实回读。
- `DeviceSettingPanel` 直接监听 `DeviceManager::currentDeviceChanged` 与 `currentDeviceOpenStateChanged`，刷新 Fan Mode 按钮状态。
- 用户在页面中改动 Fan Mode 后，立即调用当前设备的 `configureFanMode()`；这条链路与波形下载、公共配置保存无关。

## Current Boundary

- `Auto` 模式阈值当前固定为 `50.0 C`，本次不开放阈值编辑 UI。
- 风扇模式当前不参与 `CommonDeviceProfile`，也不进入 `Profile.json` 保存/恢复。
- 因底层 API 无返回值，本次实现无法做设备级成功确认，只能在软件侧缓存并回显最近一次设置值。