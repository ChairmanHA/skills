# 设备状态前端反馈机制

本文档说明当前工程里“设备状态如何反馈到前端”的完整机制，重点覆盖：

- 打开失败时的 error 弹窗链路
- 运行中 warning 在状态栏的滚动播放链路
- 实时查询状态里的 RLO 小指示链路
- `STATUS_WARNING_UNLEVEL (99)` 为何不再进入滚动 warning
- 各条链路各自的去重策略
- `STATUS_ERROR_BUSOPENFAILED (-1)` 是否有特殊处理

---

## 1. 总览

当前前端反馈分成五条独立链路：

1. **打开设备失败链路**
   - 入口是 `IDevice::open()` 返回失败。
   - 由 `DeviceManager` 转成全局 `systemMessage`。
   - `MainWindowDeviceController` 收到后弹 `MessageDialog`。

2. **设备已打开后的实时状态链路**
   - 入口是 `DeviceManager` 的定时状态轮询。
   - 由 `deviceRealTimeStatusUpdated(status)` 发到前端。
   - `MainWindowDeviceController` 负责：
     - error -> 弹窗
     - warning -> 送入 `DeviceInfoWidget` 的 warning 队列滚动播放

3. **实时状态里的 RLO 小指示链路**
   - 入口同样是 `deviceRealTimeStatusUpdated(status)`。
   - `DeviceInfoWidget::updateDeviceRealTimeStatus()` 直接读取原始 `warnningCode`。
   - 若 warning code 是 `15/16/17/18`，则在状态栏右侧显示很小的 `RLOxx` 指示。
   - 其中 RLO15 在当前参考源为 External 时还会进入“状态型滚动 warning”链路。

4. **配置结果型局部状态链路（Level UnLevel）**
   - 入口不是实时轮询，而是 `tx_config_ffm()` 的局部返回结果。
   - HTRA 设备把 `STATUS_WARNING_UNLEVEL (99)` 收敛为 `txLevelUnlevelActive()`。
   - 主窗口把它同步到 `CommonPanel` 的 `Level` 按钮，显示为右上角 `UNLEVEL` badge。
   - 该状态不进入状态栏滚动 warning。

5. **业务级许可证失败链路（Analog）**
   - 入口是设备 open 成功后的 `deviceConnected(deviceInfo)`。
   - 由 Analog 插件内部执行设备参数许可证校验。
这些链路共享“设备状态/可用性”语义，但**状态 owner、展示位置、去重策略彼此独立**。

### 1.1 C/S Minibar 例外

以上“弹 `MessageDialog`”描述默认指 MainWindow 可见的普通 UI 路径。当前 Minibar
是独立 `SGStudioMiniBar` helper，运行期间不展示这些主进程消息框：

1. `MinibarHelperController` 在隐藏 MainWindow 前开启全局 popup prompt
   suppression，恢复或异常回退时解除。
2. open failure、runtime error 或 license failure 即使在 main 中创建了
   `MessageDialog`，其展示 API 也会立即拒绝或丢弃并释放对象；不会通过 IPC 转发给
   helper，也不会在恢复 MainWindow 后补弹。
3. 标准回调版 `showMessage()` 在 suppression 时同步回调
   `QDialog::Rejected`。实时 error 链因此仍会清除 `m_errorMsgActive` 并记录
   `m_lastErrorCode`，避免隐藏期间同一错误被反复处理或恢复后补弹。
4. helper 请求自身的失败通过结构化 RSP 和权威 snapshot 回滚/刷新，不属于本文件的
   MainWindow 弹窗链。

这套边界没有 Win32/Linux 分支，Win32 与 aarch64 行为相同。

---

## 2. 打开失败 -> error 弹窗

### 2.1 调用链

1. `DeviceManager::setCurrentDevice(device)`
   - 更新当前设备
   - 停止状态轮询
   - 把 close/open 投递到 I/O 线程执行

2. `DeviceIoWorker::switchDevice(...)`
   - 关闭旧设备
   - 调用 `newDevice->open(&openError)` 打开新设备

3. `FancyDevice::open(QString *errorMessage)`
   - 调用底层 `device_open_usb(...)`
   - 若返回 `< 0`，认为打开失败
   - 通过 `translateErrorCode(result, lvl)` 生成用户可见文案

4. `DeviceManagerPrivate::onDeviceSwitchFinished(...)`
   - 如果打开失败且 `errorMessage` 非空：
     - 对大多数错误，立即 `postMessage(Core::MsgError, tr("Device Open Failed"), errorMessage)`
     - 对 open 阶段的 `-8`，先进入 8 秒 grace period，期间继续 retry-open；若超时后仍未成功，再升级为 `Device Open Failed`

5. `MainWindowDeviceController` 监听 `DeviceManager::systemMessage`
   - 对 `Device Open Failed` 创建 `Controls::MessageDialog`
   - MainWindow 可见时以 error 风格显示文本
   - C/S Minibar 可见时由全局 suppression 直接丢弃，不转发给 helper

### 2.2 去重机制

打开失败弹窗的去重分两层。

#### 第一层：按连接请求去重

`DeviceManager::setCurrentDevice()` 每次都会生成新的 `requestId`，并重置 `openFailedMessageShownRequestId`。

在 `onDeviceSwitchFinished()` 中：

- 只有当本次 `requestId` 还没展示过失败消息时，才会 `postMessage("Device Open Failed", ...)`
- 同一次连接请求内，后续自动 retry-open 失败不会重复弹窗
- 若本次失败码是 open 阶段的 `-8`，则先延时 8 秒；8 秒内成功打开不会弹窗，超时后最多仍只弹一次

因此，**同一次点击 Connect 触发的持续失败，只会首弹一次**。

#### 第二层：前端只保留一个 Device Open Failed 弹窗句柄

`MainWindow` 内有 `m_deviceOpenErrorDialog`：

- 如果新的 `Device Open Failed` 到来时旧弹窗还在，先关旧弹窗再开新弹窗
- 设备一旦成功打开，`currentDeviceOpenStateChanged(true)` 会主动关闭该弹窗并清空句柄

这一层主要解决的是“同类弹窗实例管理”，不是核心去重来源。

---

## 3. 实时状态 -> error 弹窗 / warning 滚动

### 3.1 调用链

1. `DeviceManager::startStatusUpdates()` 启动状态轮询定时器
2. `DeviceManagerPrivate::onStatusUpdateTimeout()` 周期执行：
   - 若当前设备未 open，则走 retry-open 分支，不发实时状态
   - 若当前设备已 open，则调用：
     - `device->updateRealTimeStatus()`
     - `device->getRealTimeStatus(status)`
   - 然后 `emit deviceRealTimeStatusUpdated(status)`

3. `MainWindowDeviceController::onDeviceRealTimeStatusUpdated(const DeviceRealTimeStatus &status)`
   - 先做当前设备 UID 过滤，避免设备切换后的陈旧状态污染新 UI
   - 先把原始状态刷新到 `DeviceInfoWidget`
   - 再分别处理 errorCode、状态型 warning 和普通 warnningCode

这里顺序很重要：

- `DeviceInfoWidget` 会先看到原始 `warnningCode`
- `MainWindowDeviceController` 随后才会根据状态型规则或 `warningCategory()` 判断它是否进入滚动 warning 队列

因此，某些被分类为 `Ignorable` 的 warning 仍然可以在 `DeviceInfoWidget` 内部触发局部 UI，例如 `RLO` 小指示；也可以由 controller 显式提升为带生命周期的状态型滚动 warning，例如 External 下的 RLO15。

---

## 补充：业务级许可证失败 -> error 弹窗（Analog）

这条链路不属于 `DeviceManager::systemMessage`，而属于 Analog 插件自己的业务级反馈。

### 调用链

1. `DeviceRuntimeBridge::onDeviceOpenStateChanged(true)`
   - 发出 `deviceConnected(deviceInfo)`

2. `AnalogModulationPlugin::initialize()`
   - 监听 `DeviceRuntimeBridge::deviceConnected`
   - 从 `deviceInfo` 提取 `model/uid_h32/uid_l64`
   - 调用 `DeviceParamsManager::onDeviceConnected(params)`

3. `DeviceParamsManager::onDeviceConnected(...)`
   - 执行 `checkDeviceLicense(...)`
   - 若失败，发出 `licenseValidationFailed(message)`

4. `AnalogModulationPlugin`
   - 监听 `licenseValidationFailed(...)`
   - 直接创建 `Controls::MessageDialog`

### 当前语义

- 这条链路只在 `FixedLic=false` 时有意义。
- 它表示“设备已经连通，但当前设备不允许 Analog 波形生成”，不是设备 open 失败。
- 当前代码中的弹窗文案字面量是：

```text
License validation failed, waveform generation unavailable.
```

### 与 `Device Open Failed` 的区别

- 不走 `DeviceManager::systemMessage`
- 不占用 `m_deviceOpenErrorDialog`
- 不属于 open-phase transport failure，而属于业务级 capability / license gate

更多实现细节见 [analog_device_license_gating.md](analog_device_license_gating.md)。

---

## 4. 实时 error 的前端去重

`MainWindowDeviceController` 使用两个成员做去重：

- `m_errorMsgActive`
  - 当前是否已有 error 弹窗正在显示
- `m_lastErrorCode`
  - 上一次已经确认展示过的错误码

触发条件：

- `status.errorCode != 0`
- `m_errorMsgActive == false`
- `m_lastErrorCode != status.errorCode`

弹窗关闭回调中：

- `m_errorMsgActive = false`
- `m_lastErrorCode = status.errorCode`

C/S Minibar suppression 会以 `QDialog::Rejected` 同步调用同一个标准回调，因此虽然
没有可见弹窗，上述去重状态仍然完成更新。

### 4.1 语义

这意味着：

- 同一个错误码在已经展示并关闭后，不会因为后续轮询再次出现而重复弹窗
- 切换到新设备或新设备成功打开时，`m_lastErrorCode` 会被重置为 0
- 去重粒度是“错误码”，不是消息文本

### 4.2 与打开失败链路的边界

实时状态 error 弹窗只针对“**已经 open 的设备**”。

如果设备尚未打开，状态线程不会分发 `deviceRealTimeStatusUpdated(status)`，而是直接进入 retry-open 逻辑。因此 open 阶段的错误通常不会走这里。

---

## 5. warning 的滚动播放机制

warning 不走 `MessageDialog`，而是进入状态栏控件 `DeviceInfoWidget` 的队列机制。

### 5.1 MainWindow 侧过滤与去重

`MainWindowDeviceController::onDeviceRealTimeStatusUpdated()` 对 warning 的处理规则是：

1. 先处理带生命周期的状态型 warning，例如 External 参考源下的 RLO15。
2. 普通 warning 仅在 `status.warnningCode != 0` 时处理。
3. 若 `m_lastWarningCode == status.warnningCode`，则忽略，避免重复入队。
4. 调用 `device->warningCategory(status.warnningCode)` 做分类。
5. 只有 `WarningCategory::General` 才会进入前端滚动显示。
6. 用 `translateShortStrOfWarningCode()` 生成短文案。
7. 调用 `m_deviceInfoWidget->addWarnningMessage(warningStr)`。

若本次 `warnningCode == 0`，则 `m_lastWarningCode` 被重置为 0。

这里有两条重要边界：

1. 编辑态 preview 的参数钳位 warning 不属于 `DeviceRealTimeStatus::warnningCode`
2. 例如 StepSweep 的 `tx_test_*` 归一化 warning，应在 preview 链路内被视为“归一化成功”，而不是状态栏 warning
3. 只有真实运行态通过设备状态轮询/配置路径上送的 warning，才允许进入 `DeviceInfoWidget`
4. `STATUS_WARNING_UNLEVEL (99)` 当前也不再属于状态栏滚动 warning，它已经转为 `CommonPanel Level` 的局部 UI 状态

更完整的 preview 边界见 [sweep_preview_validation_boundary.md](sweep_preview_validation_boundary.md)。

### 5.1.1 External RefClock Unlock 状态型 warning

External 参考源下的 RLO15 使用状态型 warning，而不是普通一次性 warning：

1. HTRA 设备层仍复用 `device_query_state()` 的实时轮询结果，不额外把 `device_query_clock()` 接入高频状态轮询。
2. 当 `status.warnningCode == STATUS_WARNING_SYSCLK_UNLOCKED (15)` 且当前 `RefClockSource == External` 时，`MainWindowDeviceController` 向 `DeviceInfoWidget` 增加滚动文案：`External reference clock not locked.`
3. 当 RLO15 消失、切回 Internal、断开设备或切换设备时，controller 调用 `removeWarnningMessage()` 移除该文案。
4. 该机制使用独立布尔状态去重，不依赖 `m_lastWarningCode`，因此可以表达“出现时持续滚动，消失时立即消失”的状态语义。

`device_query_clock()` 仍保留给显式参考时钟查询、Device Settings 刷新和配置 writeback 路径；只有当实测证明 `device_query_state()` 不能可靠反馈锁定状态时，才应考虑把 clock snapshot 作为设备实时状态的窄字段扩展。

### 5.2 DeviceInfoWidget 的队列与循环播放

`DeviceInfoWidget` 内部维护：

- `QQueue<QString> m_messageQueue`
- `QTimer *m_warningTimer`
- `bool m_warningLoop`

`MainWindow` 构造时默认开启：

- `m_deviceInfoWidget->setWarningLoopMode(true)`

#### addWarnningMessage() 的去重

向队列追加 warning 前，会做两层文本去重：

1. 如果队列里已经有完全相同的字符串，不重复入队
2. 如果当前正在显示的文本里已经包含该字符串，不重复入队

#### showNextWarningMessage() 的播放规则

- 每次从队列头取一条 warning 显示到 `warnningStatus`
- 文本格式为 `Warning : <message>`
- 定时器每 3000ms 播放下一条
- 若 `m_warningLoop == true`，刚展示过的消息会重新入队尾，形成循环播放

#### clear / remove 语义

- `clearWarningQueue()` 会清空队列并清空当前显示文本
- `removeWarnningMessage()` 会从队列中删掉指定文案；如果删的是当前正在显示的文案，则立刻切换下一条

### 5.3 RLO 小指示与滚动 warning

`DeviceInfoWidget` 内部存在一条独立的 RLO 指示链路：

1. `updateDeviceRealTimeStatus(status)` 直接读取原始 `status.warnningCode`
2. 若 warning code 是 `15/16/17/18`，则把当前 RLO warning code 保存下来
3. `updateRloIndicatorVisibility()` 决定是否显示右侧很小的 `RLOxx` 文本

这条链路的关键点是：

- RLO 不依赖 `addWarnningMessage()`
- RLO 小指示不等于 warning 队列
- RLO 的显隐由每次实时状态刷新直接覆盖，因此 warning 消失后会自然隐藏
- 当前只有 External 参考源下的 RLO15 会被 controller 额外映射成可自动移除的滚动 warning

这也是为什么 `FancyDevice::warningCategory()` 把 RLO 类 warning 归为 `Ignorable` 后，RLO 小指示依旧可以工作。

### 5.4 当前滚动队列边界

当前 `DeviceInfoWidget` 的 warning 区只承载设备相关 warning 与系统短警告文案，不再额外挂接 `SystemBusyStatus -> Generating Waveform...` 这条旧的大波形忙碌提示链路。

也就是说，前台 warning 区现在不再承担“大波形生成中”的通用 busy 展示职责。

当前 HTRA 侧被允许进入滚动 warning 的主要是 `WarningCategory::General`：

- `STATUS_WARNING_BUSTIMEOUT`
- `STATUS_WARNING_TXLEVEL_FILEDEFAULT`
- `STATUS_WARNING_IQCAL_FILEDEFAULT`

而下列 warning 虽然可能出现在原始 `warnningCode` 中，但默认不按普通 warning 进入滚动队列：

- `STATUS_WARNING_SYSCLK_UNLOCKED`
- `STATUS_WARNING_ADDACLK_UNLOCKED`
- `STATUS_WARNING_LOREF_UNLOCKED`
- `STATUS_WARNING_RFLO_UNLOCKED`
- `STATUS_WARNING_UNLEVEL`
- `STATUS_WARNING_PARAMOUTRANGE`

其中：

- `15/16/17/18` 仍会驱动 `RLOxx` 小指示
- External 参考源下的 `15 / STATUS_WARNING_SYSCLK_UNLOCKED` 会额外驱动滚动文案 `External reference clock not locked.`
- `99 / STATUS_WARNING_UNLEVEL` 已改为 `CommonPanel Level` 的局部 badge 状态

更多 `Level` 按钮的本地 badge 处理见 [commonpanel_level_unlevel_badge_ui.md](commonpanel_level_unlevel_badge_ui.md)。

---

## 6. `STATUS_ERROR_BUSOPENFAILED (-1)` 是否有特殊处理

结论：**没有单独的前端机制特殊处理**。
因为-1的设备，根本不会在list_device_usb里被发现到，所以也就根本不会去尝试连接它。

`-1` 的特殊处理主要体现在 `FancyDevice::translateErrorCode()` 中，提供了专门的用户文案；以及在 `FancyDevice::open()` 中，`-1` 会像其他 `< 0` 错误一样直接导致 open 失败，并进入前述的 error 弹窗链路。
当前只存在两点与 `-1` 相关的行为：

1. 在 `FancyDevice::translateErrorCode()` 中，`-1` 被翻译为专门的用户文案
2. 在 `FancyDevice::open()` 中，`-1` 会像其他 `< 0` 错误一样直接导致 open 失败

补充（2026-04-09，手工 ETH 临时方案）：

- SDK 头文件中的 `eth_setting` 虽然存在 `eth_errorcode` 字段，但 `device_open_eth(void** device, eth_setting settings, ...)` 使用的是按值 `settings`，当前上层不能把该字段当成可回写的稳定错误输出。
- 因此手工 ETH open 当前仍以 `device_open_eth(...)` 的返回值作为唯一稳定结果来源；若拿不到更细粒度的 transport 错误细节，就只能沿用 `STATUS_ERROR_BUSOPENFAILED (-1)` 这类通用 open-failure 语义做用户提示兜底。
- 这属于“错误细节可观测性受限”，不是 `DeviceManager` / `BusinessManager` 主链路的架构问题。
- 详见 [manual_eth_connect_temporary_design.md](manual_eth_connect_temporary_design.md)。


## 6.1 `STATUS_ERROR_DISCONNECT (-8)` 在 open 阶段的特殊处理

结论：**有，仅限 open 阶段。**

当前实现把 `-8` 分成两类语义：

1. **open 阶段的 `-8`**
   - 被视为“可能短暂可恢复”的失败（典型场景是 USB 已枚举，但设备电源稍后才接上）
   - `DeviceManager` 不会立即弹窗，而是启动 8 秒单次 grace period
   - grace period 内仍由 status timer 持续 retry-open
   - 若 8 秒内成功打开，则不会出现 `Device Open Failed`
   - 若 8 秒后仍失败，才升级为正常 error 弹窗

2. **运行态的 `-8`**
   - 仍被视为真实断连
   - 由状态轮询直接走 disconnect / unregister / restart scanner 流程

因此，`-8` 的特殊处理不是前端层面的，而是 `DeviceManager` 在 open-failure 编排中的策略分支。

---

## 7. 当前机制的设计结论

### 7.1 error 为什么用弹窗

- MainWindow 可见时，open 失败需要显式打断用户操作，提示连接失败
- MainWindow 可见时，已 open 设备的运行时错误也被视为需要用户注意的异常事件
- C/S Minibar 运行期间不以隐藏 MainWindow 的 modal prompt 打断 helper；错误状态按
  suppression、RSP 和 snapshot 边界处理

### 7.2 warning 为什么用滚动播放

- warning 往往不是阻断式错误
- 状态栏滚动比频繁弹窗更适合持续性或可忽略告警
- `warningCategory()` 允许设备层决定某些 warning 是否值得展示给用户

---

## 8. PGA 新增平台实时状态链路（2026-05）

为支持 PGA 的 `CPU` 温度与电池显示，状态栏现在增加了一条“平台态”并行链路；它与本文件前文描述的 `DeviceRealTimeStatus` 链路并存，而不是替换关系。

### 8.1 新增链路

1. `MainWindowDeviceController` 在 PGA 平台创建 `PgaRuntimeStatusService`（独立线程）。
2. `PgaRuntimeStatusService` 调用 `hlc --get-cpu-temp` / `hlc -b`，解析为 `PlatformRuntimeStatus`。
3. service 发出 `platformStatusUpdated(const PlatformRuntimeStatus &)`。
4. `MainWindowDeviceController::handlePlatformRuntimeStatusUpdated(...)` 转发到 `DeviceInfoWidget::updatePlatformRuntimeStatus(...)`。

### 8.2 复用链路

以下旧链路保持原样复用：

1. `DeviceManager` 的设备状态轮询与 `deviceRealTimeStatusUpdated(status)` 发布。
2. `MainWindowDeviceController::onDeviceRealTimeStatusUpdated(...)` 的 error/warning/RLO 处理。
3. `DeviceInfoWidget::updateDeviceRealTimeStatus(...)` 的设备态渲染（RFU 温度、吞吐、RLO）。
4. `updateDeviceInfo()/resetDisplayedDeviceState()` 的连接态与版本信息更新。

### 8.3 边界结论

- `DeviceInfoWidget` 当前保留两条 runtime 输入：
   - `updateDeviceRealTimeStatus(const DeviceRealTimeStatus &)`（设备态）
   - `updatePlatformRuntimeStatus(const PlatformRuntimeStatus &)`（平台态）
- `hlc` 查询 owner 已收敛到 `PgaRuntimeStatusService`，不再由 Widget 或 `IHardwareSettings` 承担。
- 非 PGA 平台不会创建该服务，因此不会引入额外线程或命令调用。

更多实现细节与参数语义见 [pga_runtime_realtime_status_chain.md](pga_runtime_realtime_status_chain.md)。

---

## 9. About 页 RF/USB 供电显示（2026-07）

HTRA 的供电信息继续复用本文件第 3 节描述的设备实时状态链路，不建立新的轮询：

1. `FancyDevice::updateRealTimeStatus()` 在温度保活成功后调用
   `device_query_supply()`。
2. `FancyDevice::getRealTimeStatus()` 把 `rf_voltage/rf_current` 和
   `usb_voltage/usb_current` 写入 `DeviceRealTimeStatus`。
3. `DeviceManager` 给快照附加设备 UID 后发布
   `deviceRealTimeStatusUpdated(status)`。
4. `AboutDialog` 过滤断连状态和陈旧 UID，并分别显示：
   - `RF Port: voltage / current / voltage * current`
   - `USB Port: voltage / current / voltage * current`

显示值使用三位小数，单位为 `V/A/W`。无设备、Streaming 当前不提供 supply
快照或 `device_query_supply()` 失败时显示 `--`。设备层在查询失败时写入
`InvalidDeviceSupplyValue`，不能再用清零后的 `supply_info{}` 冒充真实
`0V/0A/0W`。

这两行主要用于观察自动低功耗前后的供电趋势。`POWEROFF` 下 query 仍按当前
API 契约继续执行，但返回值可能不够精确，因此不把它当作计量级功率结论，也不参与
设备在线/断联判定；温度 query 仍是现有保活入口。

---

## 10. 相关文档

- `messagedialog_design.md`
  - 解释 `Controls::MessageDialog` 组件本身的 API 和异步回调模型
- `htra_multi_device_stageA_design_and_debug.md`
  - 解释 current device 切换、I/O 线程串行 open/close、open 失败自动重试等架构背景
