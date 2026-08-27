# open 阶段 -8 延时升级方案

日期：2026-03-25

## 背景
- 当前 HTRA 设备在只接 USB、未接电源时，`device_list_usb()` 仍可枚举到设备。
- 用户点击连接后，`device_open_usb()` 可能先返回 `-8`，`DeviceManager` 立即弹出 `Device Open Failed`。
- 现有架构下，status timer 会继续 retry-open；用户补上电源后，设备通常可自动打开，旧弹窗也会被关闭。
- 问题不在“无法恢复”，而在“对短暂可恢复的 open 阶段 -8 提示过早”。

## 目标
- 仅对 open 阶段的 `-8` 引入 8 秒延时升级。
- 8 秒内若 retry-open 成功，则不弹窗。
- 8 秒后仍未成功，再沿用现有 `Device Open Failed` 弹窗链路。
- 其他 open 失败错误保持立即提示。
- 不修改扫描层职责，不把提示策略下沉到 UI 层。

## 设计
1. `FancyDevice::open()` 在失败时保留原始 `m_errorCode`，供上层识别具体失败码。
2. `DeviceManagerPrivate` 增加单次 request 级别的 delayed-open-failure 状态：
   - `delayedOpenFailedRequestId`
   - `delayedOpenFailedDevice`
   - `delayedOpenFailedMessage`
   - 单次 `QTimer`
3. `onDeviceSwitchFinished()` 中：
   - 若失败码为 `-8`，且本 request 尚未提示，则启动一次 8 秒单次定时器。
   - 若同一 request 后续仍是 `-8`，不重复启动，不滑动窗口。
   - 若同一 request 后续变成其他错误，取消延时并立即按现有链路提示。
   - 若成功 open / 切换设备 / 当前设备被注销，则取消延时状态。
4. 延时定时器触发时再次校验：
   - requestId 未变化
   - currentDevice 仍是原设备
   - 设备仍未 open
   满足时才真正 `postMessage(Device Open Failed, ...)`。

## 风险控制
- 保持 `requestId` 去重语义不变，避免旧请求的延时消息污染新请求。
- 不碰 `deviceRealTimeStatusUpdated` 的运行态错误弹窗逻辑。
- 不改变 scanner “只发现、不 open” 的架构边界。

## 2026-03-25 Follow-up
- 为 `DeviceManager` 核心环节补充英文注释，覆盖：scanner 注册、current 切换、I/O worker、status retry-open、快照对账、延时升级超时回调。
- 将重复出现的 delayed-open-failure 状态清理逻辑合并为私有 helper，降低后续维护时的遗漏风险。
- 同步更新知识库，明确 `-8` 在 open 阶段采用 8 秒 grace period，成功重试则不弹窗。