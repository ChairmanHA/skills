# 设备发现/连接/断开告警日志补充方案

日期：2026-05-14

## 背景
- 当前设备发现、打开、断开链路已有完整状态机，但关键生命周期节点缺少统一 `qWarning()` 输出。
- `HTRADeviceScanner` 每秒扫描一次；如果按扫描周期直接记日志，会造成日志噪声，掩盖真正的设备变化。
- 运行期断开目前只有笼统的 `device disconnected`，不利于区分“用户主动切换导致旧设备关闭”和“状态查询/保活判定断开”。

## 目标
- 仅在设备集合发生变化时输出“发现新设备 / 移除设备”日志。
- 在设备成功打开时输出“设备已连接”日志。
- 在运行期断开时输出带来源和错误码的日志，优先说明是否来自状态轮询判定。
- 若是用户主动切换当前设备，补一条单独日志说明旧设备是因切换而关闭，而不是异常断开。

## 设计
1. 在 `DeviceManagerPrivate::onDevicesDiscovered()` 复用现有 `devicesToAdd/devicesToRemove` 对账结果打印日志；无增删时不输出。
2. 在 `DeviceIoWorker::switchDevice()` 里，当存在 `oldDevice != newDevice` 时输出“因切换关闭旧设备”的日志。
3. 在 `DeviceManagerPrivate::onDeviceSwitchFinished()` 里，当最新请求成功打开设备时输出一次“设备已连接”日志。
4. 将运行期断开处理改为携带 `DeviceRealTimeStatus` 与来源字符串，日志里带上 `connected/errorCode/warnningCode`，用于区分温度/保活查询触发的断开判定。

## 风险控制
- 不在 scanner 每次轮询处直接打日志，避免每秒刷屏。
- 不新增复杂跨线程状态，仅复用现有 requestId、对账结果和 runtime status。
- 不改变现有 open/retry/disconnect 行为，只增强可观测性。