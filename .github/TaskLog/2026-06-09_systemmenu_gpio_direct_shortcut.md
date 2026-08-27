# System Menu GPIO Direct Shortcut

## Goal

- 在 System menu 中新增独立的 GPIO item。
- 点击后弹出一个 GPIO dialog，布局参考 PreferenceDialog：最多两行、每行四个 LabelButton。
- 每个按钮代表一个 GPIO 口，标题为 `GPIO1` ~ `GPIO8`，状态文案为 `On / Off`。
- 不走 CommonDeviceProfile、TxApplyRequest、Profile.json 保存/恢复链路，直接通过当前设备快捷接口下发。

## Design

- 在 `Core::IDevice` 增加可选 GPIO 快捷接口：
  - `queryGpioState(quint16 *stateBits, int *gpioCount)`
  - `configureGpioState(int index, bool high)`
- `HTRA::FancyDevice` 直接封装 `device_gpio_setbits()` / `device_gpio_resetbits()`，并在设备实例内缓存最近一次写入的 GPIO 位图。
- GPIO 数量不从 profile 推导，而是在设备 open 后依据 `hardware_version` 计算：
  - `eio_type == 0`: 0 路，不支持 GPIO
  - `eio_type == 0x60`: 8 路
  - 其他非 0: 4 路
- 由于当前 H2 API 没有 GPIO 查询接口，UI 回显使用设备实例缓存，不做硬件真实回读。
- `MainWindow` 持有 GPIO dialog 与 menu action，直接监听 `DeviceManager::currentDeviceChanged` 与 `currentDeviceOpenStateChanged` 刷新启用状态与按钮数量。
- 当设备未打开或无 GPIO 能力时，System menu 的 GPIO item 直接禁用。

## Current Boundary

- GPIO 状态当前只保证本次设备会话内的软件侧回显；切换设备、断开设备、重新 open 后，UI 默认回到全低电平。
- 本次不新增配置持久化，不把 GPIO 状态写入 `Settings.ini` 或 `Profile.json`。
- 本次不新增独立设备设置页入口，只在 System menu 提供对话框入口。