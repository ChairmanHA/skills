日期：2026-03-13

## 问题
- 部分 HTRA 设备在 `device_list_usb()` 扫描阶段返回的 `device_info.model`、`uid_h32`、`uid_l64` 全为 0。
- 即使 scanner 已允许 `model == 0` 通过，这类设备仍未进入 `FancyDevice::open()`。

## 根因
- `HTRADeviceScanner` 只是把扫描结果发给 `DeviceManager`。
- `DeviceManagerPrivate::onDevicesDiscovered()` 当前严格以 `DeviceUID` 作为 reconcile 键，并直接忽略 `DeviceUID == 0` 的设备。
- 因此这类设备不会被 register，也不会被 `setCurrentDevice()` 触发 open。

## 本次最小临时修复
- 撤销运行期 synthetic UID 方案，不再把异常枚举结果伪装成正常设备。
- 在 `HTRADeviceScanner::scan()` 中检测“`device_list_usb()` 返回 `model/uid32/uid64` 全 0，但对该枚举序号执行 `device_open_usb()` 可正常打开”的异常设备。
- 一旦命中该条件：
	- 通过 `DeviceManager::setFirmwareUpdateDisconnectMode(true)` 立即停止后续发现与自动打开重试；
	- 通过全局 `MsgCritical` 弹出错误提示，要求用户关闭软件并对设备断电重插；
	- 当前扫描周期不再向上发出新的发现结果，避免前端持续“发现/重连”。

## 边界
- 这是故障熔断策略，不尝试继续使用该异常设备。
- 后续若底层修复 `device_list_usb()` 枚举字段，应删除该探测分支。

## 验证
- 编译 HTRA 相关改动。
- 插入异常设备后，确认前端只弹一次致命错误提示，且不再持续发现/重连。