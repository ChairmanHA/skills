# SystemClockOut Delayed Readback Plan

## Goal

- 保留 `RefOut` 直连 `IDevice::configureSystemClockOut()` 的快捷路径。
- 避免设置后立即 `device_query_clock()` 读到旧的 `out_o == OFF`，把 UI/运行时镜像值错误回写回去。
- 改为设置成功后先保留用户目标值，等待 5 秒，再做一次真实 query 并回写 property / 按钮状态。

## Local hypothesis

- 当前问题不是 `device_config_clock()` 没下发，而是 `DeviceSettingPanel::applySystemClockOut()` 在设置成功后立刻 `refreshSystemClockOut()`。
- `FancyDevice::querySystemClockOut()` 会直接调用 `device_query_clock()`；在硬件状态尚未稳定时，`out_o` 仍返回 `OFF`，于是把刚设置的 `ON` 覆盖掉。

## Discriminating check

- 工作区内 `querySystemClockOut()` / `configureSystemClockOut()` 的直接调用只出现在 `DeviceSettingPanel` 的 `RefOut` 快捷链路里。
- 因此先在 UI 快捷路径做“延迟回读 + 等待期屏蔽即时 query”即可验证假设，不必先扩散到 `IDevice`/`FancyDevice` 层。

## Design

- 在 `DeviceSettingPanel` 增加 `QTimer` 单次定时器和 `pending` 状态。
- 设置成功后：
  - 立即把按钮和 `SystemClockOut` property 镜像到目标值。
  - 启动 5 秒定时器。
  - 等待期间 `refreshSystemClockOut()` 不再向设备 query，而是保持 pending 值，避免被旧值冲掉。
- 定时器到期后清除 pending，再执行真实 `querySystemClockOut()`，将设备实际状态回写到 property / UI。
- 设备切换或 open state 变化时取消 pending，避免旧设备的延迟回读影响当前设备。