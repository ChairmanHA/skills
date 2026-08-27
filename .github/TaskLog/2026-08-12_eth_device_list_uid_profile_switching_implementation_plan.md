# 双 ETH 常驻 Open、指针切换与 UID Profile 实施方案

日期：2026-08-12  
状态：Phase 0 已通过，Phase 1–5 已一次性实施完成，待用户实机验收  
本轮验证级别：`static`  
实施后验证级别：`static`（实机验收由用户执行）  
范围：一次性实施 Phase 1–4；按用户要求不编译，由静态检查和后续用户实机验证闭环  

2026-08-12 实施前确认：

- 5000/5001 的 `uid_h32/uid_l64` 不同，可直接使用完整 UID 作为 profile key；本轮不启用 UID+port fallback；
- 既有 Save 功能的 Playback profile 已由用户确认可用，本轮按完整覆盖实施；
- H2 已实测允许同进程同时持有 5000/5001 两个 handle，A 失去 current 身份后仍持续输出；
- 用户要求 Phase 1–4 一次性实施，不再设置中间交付门禁；本轮仍按依赖顺序编码，并在完成后统一静态审计。

优先级说明：本文取代此前“切换时关闭旧 ETH、切回时重新 open”的方案。此前关于 ETH pair 双工作区、双 Streaming 或 H2 多通道的 TaskLog 仅作历史记录，不得作为本次实现依据。

## 1. 最终需求边界

本期只针对同一 IP、不同端口的两台手工 ETH 设备，当前产品端口固定为 `5000` 和 `5001`。两台设备各自拥有独立 H2 device handle、独立 `FancyDevice` 和独立 UID。

SGStudio 保留单控制焦点架构，但允许两个 handle 常驻：

```text
DeviceManager::currentDevice()
    = 当前 UI 正在查看、编辑和下发配置的唯一设备

registered ETH A (IP:5000)
    = 生命周期内保持 open，可在后台维持最后一次 CW / Playback / Sweep 输出

registered ETH B (IP:5001)
    = 生命周期内保持 open，可在后台维持最后一次 CW / Playback / Sweep 输出
```

明确规则：

1. 第一次依次连接 A、B 后，两台设备都保持 open；正常切换只改变 current 指针，不调用旧设备的 `close()`、`closeForSwitch()`、`device_preset()` 或 `device_close()`。
2. CW、普通 Playback、多波形 Playback、FScan/LScan/MScan 切到后台后继续维持设备端输出。
3. Streaming 是唯一例外：切走前必须同步停止主机数据发送，确认 sender 已退出发送区后才能改变 current 指针。
4. 切回目标设备时允许全量重新配置，并允许重新生成/重新下载波形；目标设备可能出现一次可接受的重配 glitch。
5. 两台设备只在应用正常退出、对象销毁或真实断线释放失效 handle 时关闭；正常 A/B 切换绝不关闭。
6. 后台设备不做温度、GNSS、供电或 health 轮询，不对后台设备发任何配置命令。
7. 当前 ETH 掉线或一次 open 失败后不自动重连；保留已成功设备的列表条目，下一次 open 只能由用户点击 Device List 触发。
8. 不支持两路 Streaming 并行，不提供同时编辑两台设备的 UI，不引入 H2 多通道概念。
9. USB scanner、ownership 和原有 USB 生命周期不改；USB/ETH 混插及跨介质切换不属于本期产品验收。

## 2. 成功标准

实施完成后必须同时满足：

1. 用户手动连接 `IP:5000` 成功后 A 进入 Device List；再连接 `IP:5001` 时 A 不关闭，B 完成 open 后也进入列表。
2. 两台设备建立成功后反复切换，日志中不得出现由切换触发的 `device_open_eth`、`device_preset`、`device_close` 或 `closeForSwitch`。
3. A 正在 CW/Playback/Sweep 输出时切到 B，频谱仪确认 A 连续输出；软件日志或 `isOpen()` 不能替代射频实测。
4. B 配置并输出后切回 A，B 同样继续输出；A 恢复 A 自己的 profile 并只向 A 全量下发。
5. A 处于 Streaming 时切 B，A sender 必须在 current 指针变为 B 之前停止；不得有任何尾部 buffer 被发往 B。
6. A、B 的 Common、Trigger Out、全部 baseband 业务、Sweep、选中业务和当前页面按设备身份保存，往返切换不串台。
7. 第二台设备首次没有 profile 时使用 factory default，不能继承第一台仍显示在 UI 中的配置。
8. 当前 ETH 掉线后不 unregister、不启动 timer retry；用户点击条目时只 open 一次，失败后继续等待下一次点击。
9. 应用退出时两个仍持有的 ETH handle 都执行一次最终关闭，退出后两台设备停止输出。

## 3. 已核实的代码事实与对参考方案的修正

### 3.1 支持常驻双 handle 的现有基础

- [devicemanager.cpp](../../src/plugins/core/devicemanager.cpp) 的 `DeviceIoWorker::switchDevice()` 中，真正关闭旧设备的入口是 `oldDevice->closeForSwitch()`；目标已经 open 时，`newDevice->isOpen()` 会直接得到成功，不重复 open。
- `DeviceManager` 本来就持有 `registeredDevice` 列表和唯一 `currentDevice`，因此不需要新增“双设备管理器”或 H2 多通道模型。
- `TxPipelineRuntime::deactivate()` 与 `TxPipelineExecutor::deactivate()` 只清除软件 epoch、request 和 resident 假设，不调用 H2 停止、Mute、preset 或 close。保留这一步可以强制切回目标后重新构建完整 request。
- `FancyDevice` 的 waveform cache 是实例成员。A/B 两个实例天然拥有两套设备侧 waveform identity，不会因 current 指针切换而合并。
- 状态 timer 只轮询 `currentDevice`；旧设备留在后台后，不会继续收到温度、供电和 GNSS 查询。
- HTRA 的 SDK mutex 是 `FancyDevice` static 全局锁；首次打开第二个 handle以及正常当前设备配置仍会串行进入 SDK。
- `FancyDevice::~FancyDevice()` 调用 `close()`；`DeviceManagerPrivate::~DeviceManagerPrivate()` 在线程停止后 `qDeleteAll(registeredDevice)`，所以只要 A/B 不被中途 unregister，现有退出析构链即可关闭两个 handle。

### 3.2 切换前必须主动停止 Streaming，不能只依赖 open-state=false

`DeviceManager::setCurrentDevice()` 会先把 `currentDevice` 改成目标，再发布 `currentDeviceOpenStateChanged(false)`。如果等该信号才停止 Streaming，Streaming sender 可能在 current 指针已指向 B 后再次通过 `DeviceOperator` 取到 B，形成串台发送。

因此顺序必须固定为：

```text
仍然 current=A
    -> suspend Tx pipeline
    -> 将当前 active legacy business setActive(false)
       Streaming::stopBusiness() 在这里同步等待 sender 退出
    -> 确认 Streaming 停止完成
    -> 才允许 DeviceManager::setCurrentDevice(B)
```

对普通业务，这个失活动作不会停止旧设备硬件输出：

- CW 的 `ContinuesWaveBusiness::stopBusiness()` 为空；
- core-managed Playback/CW/Sweep 的 executor deactivate 只清软件状态；
- Playback business 没有向旧设备发送 stop/mute 的切走动作；
- Streaming 的 `stopBusiness()` 停止 generator/sender，正是本需求要求的唯一停止项。

不能在切换路径中构造或下发 Mute request，也不能调用旧设备 `configuration(Mute)`。

### 3.3 open=true 之前后的自动动作必须加门禁

目标 open/指针切换成功后，现有 `currentDeviceOpenStateChanged(true)` 观察者会立即执行：

- `BusinessManager` 可能重新 `startBusiness()`；
- `MainWindow` 会调用 `CommonDeviceProfile::applyGnssSettingsToCurrentDevice()`；
- capability reconcile 会更新业务的设备能力。

为防止 A 的 UI 状态在 B profile 恢复前写入 B：

1. 切换前把当前 active business 真正设为 inactive，使 `BusinessManager` 的 open=true 回调无业务可重启；
2. MainWindow 的 GNSS open=true 回调在 coordinator 切换期间只记录 pending，不立即应用；
3. capability reconcile 保留，因为目标 profile 必须在 B 的能力范围下恢复；
4. B profile 完成恢复后，由 coordinator 应用一次 GNSS，并释放 pipeline 触发一次完整业务下发。

不建议为了消除 UI 闪烁而跳过 open-state=false 或 unavailable capability 发布；这两步仍承担旧 request 失效和目标 capability revision 切换职责。若闪烁明显，仅在 View 层显示 `Switching`，不要改变 DeviceManager 信号语义。

### 3.4 Playback profile 并不存在参考方案所说的结构性空洞

`AnalogPlaybackBusiness` 本身是抽象公共基类，未实现 `getProfile()/setProfile()` 不能据此判断 Playback 不可保存。当前注册的具体 HTRA/Analog 调制业务、Arb、Quick Waveform 和 Streaming 均有各自的 profile 实现；`BusinessManager::toJson()` 保存的是这些具体注册实例。

实施前仍需做一次 profile round-trip 审计，但它是验证项，不是当前方案的架构阻塞项。重点检查文件路径、选中项和生成参数，不保存 IQ 大块数据本身。

## 4. 共存 ETH 对的精确定义

第一版不增加泛化的多设备设置项，直接使用窄范围 predicate，减少实施面：

```text
isCoResidentEthPair(old, next):
    old != nullptr
    next != nullptr
    old != next
    old 和 next 均为 manual ETH（ethEndpoint() == true）
    两者 IP 完全相同
    两者 port 不同
    两个 port 的集合必须为 {5000, 5001}
```

只在 `old->isOpen()` 且 predicate 成立时设置 `keepOldOpen=true`。以下场景一律保持原 close 行为：

- USB -> USB；
- USB -> ETH 或 ETH -> USB；
- 任意设备 -> `nullptr`；
- 同一对象人工 reconnect；
- 不满足同 IP、5000/5001 配对约束的 manual ETH。

如果这一代码分支还要同时发布到不需要双 ETH 常驻的标准产品，再把 predicate 外层接到现有产品配置，而不是先引入通用 `[Device]` 新设置。当前 P0 目标是尽快闭环固定产品。

## 5. DeviceManager 最小改造

### 5.1 worker 参数

`DeviceIoWorker::switchDevice()` 增加 `bool keepOldOpen`：

```cpp
void switchDevice(quint64 requestId,
                  Core::IDevice *oldDevice,
                  Core::IDevice *newDevice,
                  bool keepOldOpen);
```

关闭判断改为：

```cpp
if (!keepOldOpen
    && oldDevice
    && oldDevice != newDevice
    && oldDevice->isOpen()) {
    // 完整保留原 closeForSwitch()/close() 逻辑
}
```

其余逻辑保持：

- worker 开始时停止当前 status timer；
- executor `deactivate()` 保留；
- 目标未 open 时执行一次 `open()`；
- 目标已经 open 时直接返回成功；
- result 仍通过现有 requestId/current pointer 做 stale rejection。

所有 `switchDevice()` 调用点必须显式传值：

- UI A/B 切换：按 predicate 传 `keepOldOpen`；
- same-device reconnect、timer retry、disconnect/null：传 `false`。

### 5.2 正常 A/B 切换的设备状态

跳过 `closeForSwitch()` 后，不得手工修改旧设备：

- `m_isOpen` 保持 true；
- `m_deviceInfo.connected` 保持 true；
- H2 handle、channel、waveform cache、mode、RF/MOD 和 power state 全部保留；
- 旧设备不再被轮询，但仍在 `registeredDevice` 中等待切回或退出销毁。

### 5.3 manual ETH 禁止自动 retry

当前 status timer 同时承担轮询与 retry-open。必须把 manual ETH 与 USB 分开：

```text
onDeviceSwitchFinished:
    manual ETH open 成功  -> 启动当前设备 status polling
    manual ETH open 失败  -> 不启动 polling/retry timer
    USB                   -> 保持原逻辑

onStatusUpdateTimeout:
    current manual ETH && !isOpen()
        -> stop timer / return，不调用 switchDevice(device, device)
```

manual ETH 的 `-8` open 失败可以保留一次错误提示延时，但不得在延时窗口内重新 open。

### 5.4 当前 ETH 运行中掉线

保留用户之前确认的人工恢复语义：

1. `FancyDevice::updateRealTimeStatus()` 检测真实 bus disconnect 后释放失效 handle并把 `isOpen` 设为 false；
2. `DeviceManager` 停止 status timer，发布 unavailable capability 和 open-state=false；
3. 不 unregister、不 delete、不 restart scanner、不 queue open；
4. 已成功设备保留 UID、endpoint 和 Device List 条目；
5. 用户再次点击该条目时，same-device closed 分支只发起一次 open；失败后再次静默等待用户操作。

后台 ETH 没有轮询，因此后台物理断线无法即时发现。切回时若对象仍报告 open，第一次目标配置/状态查询可能才暴露失效；此后按上述人工恢复语义处理。本期不增加 TCP probe 或后台 health timer。

## 6. 切换 Coordinator

新增 `DeviceRuntimeProfileCoordinator`，只负责控制焦点切换、profile 和软件下发门禁，不拥有 H2 handle。

最小状态足够：

```text
boundDevice / boundProfileKey
pendingSource / pendingTarget
switchToken
switchInProgress
firstSuccessfulEthBound
```

Device List 在 `switchInProgress` 时拒绝重复点击；`switchToken + pendingTarget + DeviceManager::currentDevice()` 用于丢弃迟到回调，不建立双工作区或复杂产品状态机。

### 6.1 首台 ETH

1. 用户通过 ETH Connect 连接第一台设备。
2. 第一次 open 成功后取得完整 UID。
3. 当前启动 UI 作为首台设备的 authoring seed，不先加载历史 per-device profile。
4. 保持现有一次业务应用，然后原子写入首台初始 profile。
5. 将该设备设为 `boundDevice`。

### 6.2 连接第二台 ETH

```text
current=A，A 已 open/正在输出
    1. capture + QSaveFile 保存 A profile
    2. beginPipelineSuspension()
    3. active legacy business setActive(false)
       - 若为 Streaming，同步等待 sender 停止
    4. requestSwitch(A -> B)，keepOldOpen=true
       - current 指针变为 B
       - A 不 close，继续原硬件输出
       - B 尚未 open，所以 worker 只 open B
    5. B open 成功，发布 B capability
    6. 按 B profile key restore；不存在则 factory default
    7. apply GNSS once for B
    8. commit boundDevice=B
    9. endPipelineSuspension() + 单次 refresh
       - B 全量配置/必要时下载波形
       - A 始终未收到命令
```

如果 B 首次 open 失败：

- 不关闭 A；
- B provisional 对象从列表/registry 移除；
- current 指针切回仍然 open 的 A，不能 open/close A；
- UI 继续使用已经保存的 A working copy；
- 结束 suspension，恢复 A 的控制焦点；
- B 以后只能由用户再次通过 ETH Connect 发起一次新 open。

### 6.3 两台都已 open 后切换

```text
保存源 profile
    -> suspend pipeline
    -> stop active legacy runtime（Streaming barrier）
    -> setCurrentDevice(target)
    -> worker skip old close
    -> target isOpen == true，不 open
    -> publish target capability
    -> restore target profile/default
    -> apply target GNSS once
    -> resume + one refresh
```

这一流程只改变软件控制焦点。源设备的普通 RF 输出继续，目标设备在最终 refresh 时接受全量配置。

### 6.4 保存失败与 restore 失败

- 源 profile 保存失败：在改变 current 指针前报错并中止，源设备和 UI 均不变；
- 目标 profile 不存在：非首台使用 factory default；
- JSON 损坏或 metadata 不匹配：不沿用源 UI，恢复 factory default并警告；
- restore 已开始后不得触发任何中间 pipeline apply；
- 恢复结束后只按最终状态刷新一次。

## 7. Pipeline 门禁

### 7.1 计数式 suspension

当前 `MainWindow::runWithoutPipelineUpdates()` 只适合同步、不可嵌套。改成 depth 计数：

```text
beginPipelineUpdateSuspension()
    depth 0 -> 1：TxSessionService::setUpdatesSuspended(true)

endPipelineUpdateSuspension()
    depth 1 -> 0：TxSessionService::setUpdatesSuspended(false)
```

同步 `runWithoutPipelineUpdates()` 继续作为 RAII wrapper。Coordinator 跨 worker 往返持有外层 suspension；profile restore 的内层 suspension 退出时不得提前恢复。

### 7.2 目标只应用一次

`setUpdatesSuspended(true)` 会使 runtime/executor invalidate，但不会操作源设备。恢复 profile 期间产生的多个 `profileChanged` 只标记 dirty。最终：

1. 先同步 TxSession 的 selected business 和 carrier plan；
2. 解除最外层 suspension；
3. 调用一次 `selectBusiness2Work()/requestRefresh()`；
4. 利用现有 queued refresh 合并，日志中目标只出现一轮最终 apply。

必须验证 stale A apply/writeback 因 epoch、device UID 或 capability revision 被拒绝，不能在 B profile restore 后覆盖 UI。

### 7.3 Streaming 硬门禁

在 `setCurrentDevice()` 前执行：

```cpp
if (auto *active = BusinessManager::activedBusiness()) {
    active->setActive(false);
}
```

不需要为此给 `BusinessManager` 增加新公开 API；现有 `activedBusiness()` 与 `IBusiness::setActive(false)` 已足够。Streaming 的 `stopBusiness()` 是同步 barrier，返回后才允许更换 current 指针。

## 8. Device List 与 ETH 对象生命周期

### 8.1 endpoint 精确复用

`resolveManualEthDevice()` 改为：

1. 遍历已登记 manual ETH；只要 `{ip, port}` 完全匹配就复用，不论 open/closed/current/background；
2. 没有匹配才由 factory 新建；
3. 删除“选择任意已关闭 ETH，再调用 `configureManualEthTarget()` 改成新 endpoint”的逻辑；
4. 已有 UID 的对象永远不得被改配成另一端口；
5. controller 校验同一产品只接受同 IP 的 5000/5001，拒绝第三个 endpoint。

### 8.2 provisional 与 remembered 条目分开

不能再以“manual ETH 且 closed”判断是否应该 unregister。Controller 单独记录 `m_provisionalEthDevice`：

- 新建且从未 open 成功的对象是 provisional；
- 第一次 open 成功后立即清除 provisional 标记，成为 remembered；
- provisional 失败/取消可以 unregister；
- 已取得 UID 的 remembered ETH 即使一次 reconnect 失败，也必须保留在列表。

这避免 `onEthConnectDialogFinished()` 误删曾经成功连接的设备。

### 8.3 菜单呈现

`m_usbConnectMenu` 改为 `m_deviceConnectMenu`，文案使用 `Device List`。菜单打开时从 `allDevices()` 重建：

- current 设备：checked，显示 `[Current]`；
- 非 current 且 `isOpen()` 的 ETH：显示 `[Open]`；
- remembered 但 closed 的 ETH：显示 `[Offline]`，仍可点击人工 open；
- provisional/UID 0 ETH：不显示；
- 文案包含 endpoint 和 UID 短码，例如 `ETH 192.168.1.100:5000 [A1B2C3D4] [Open]`。

不要显示 `Transmitting`：后台 `isOpen()` 只能证明 handle 存在，不能可靠证明 RF/MOD 当前正在发射。可在菜单说明中提示“Open 设备可能继续保持上次输出”。

QAction data 继续保存进程内 `IDevice::uuid()`，不新增持久化设备目录。

## 9. UID Profile

### 9.1 Profile key

首选完整 H2 身份：

```text
DeviceProfileKey = { DeviceUID32, DeviceUID }
../configuration/device_profiles/h2_<UID32-8hex>_<UID64-16hex>.json
```

UID 在 JSON 中必须使用固定宽度十六进制字符串，避免 64 bit 值经 JSON double 丢精度。

Phase 0 先记录 5000/5001 的完整 UID：

- UID 不同：直接使用上述 key；
- UID 完全相同：UID 不能区分两块设备。因为本产品 endpoint 固定，可退化为 `{full UID, port}`，文件名追加 `_p5000/_p5001`；必须在 metadata 写明 fallback 原因并输出 warning；
- UID 为零：不得创建 profile，连接视为身份不可用。

### 9.2 Schema

复用当前 v4 内容和历史字段拼写：

- `common`；
- `bussiness`；
- `sweep`；
- `activedBussiness`；
- `currentWidgetBusiness`。

增加可选 `deviceContext`：完整 UID、IP、port、保存时间与 profile schema。Trigger Out 已在 `common` 中，必须随设备恢复。

System Clock Out、Low Power 和 Fan 当前不属于既有 Save 按钮的 v4 保存范围，本期不借此任务扩展；Low Power 仍保存在各 `FancyDevice` 实例的本次会话状态中。

### 9.3 Persistence helper

[runtimeprofilepersistence.cpp](../../src/plugins/core/runtimeprofilepersistence.cpp) 拆成：

1. `captureRuntimeProfile()`：只读取当前 authoring state，返回 `QJsonObject`；
2. `readAndValidateRuntimeProfile()`：完整解析与 metadata 校验，不修改 UI；
3. `restoreRuntimeProfileState()`：在 suspension 内 reset/restore Common、全部具体 Business、Sweep、selected/current page；不自动应用 GNSS，不自动 refresh；
4. `writeRuntimeProfileAtomic()`：使用 `QSaveFile::commit()`。

现有 File -> Save/Load 继续复用这些 helper并保持用户行为。

### 9.4 保存内容边界

保存 authoring state 和数据源路径，不保存：

- H2 handle/channel；
- 正在运行的线程；
- device waveform ID；
- executor resident state；
- 大块 IQ 样本本身。

切回设备后，业务按参数和源文件重新构建 payload；可复用目标 `FancyDevice` 自己仍有效的 cache，但正确性不得依赖跨设备 cache。

## 10. Low Power 与中间 Mute 防护

为了避免 ETH 首次 open 或 profile 恢复的中间状态导致设备断电：

1. `FancyDevice::open()` 对 `TransportKind::EthManual` 明确初始化 `m_autoLowPowerEnabled=false`，不再从 `Plugin::usbPortOnly()` 间接推导；USB 初始化规则不变；
2. restore 全程处于 pipeline suspension，中间 reset/Mute 状态不得下发；
3. 最终 profile 本身若是 Mute 且用户随后明确启用了 Low Power，允许目标设备按正常策略进入低功耗；
4. 后台源设备切走时不得下发 Mute，因此不会因切换触发 POWER_OFF 或清空源设备 waveform cache。

同时修正 ETH `closeForSwitch()` 中“preset + device_close”的误导日志；当前该分支实际未调用 preset。常驻 pair 正常切换后不应再进入该日志。

## 11. 退出关闭

不新增第二套 shutdown 逻辑：

1. `MainWindow::closeEvent()` 在最终接受退出前保存当前 bound 设备的 UID profile；后台设备已在上次切走时保存；
2. 停止 TxSession 和当前 Streaming；
3. `CorePlugin::aboutToShutdown()` 停 status timer；
4. `DeviceManagerPrivate` 停 worker thread后删除全部 `registeredDevice`；
5. 两个 `FancyDevice` 析构分别调用 `close()`，完成各自 preset/close。

验收必须看到两个 endpoint 各一次最终 close。不得因为运行时断线或 dialog cleanup 误删 remembered ETH，否则退出关闭链会漏掉该对象。

## 12. 文件级改动清单

### 新增

- `src/plugins/core/deviceruntimeprofilecoordinator.h`
- `src/plugins/core/deviceruntimeprofilecoordinator.cpp`

职责：profile key/path、首台 seed、保存/恢复、切换门禁、Streaming stop barrier、pending target 与迟到回调拒绝。

### 修改

- [src/plugins/core/CMakeLists.txt](../../src/plugins/core/CMakeLists.txt)：加入 coordinator。
- [devicemanager_p.h](../../src/plugins/core/devicemanager_p.h)：worker slot 增加 `keepOldOpen`。
- [devicemanager.h](../../src/plugins/core/devicemanager.h) / [devicemanager.cpp](../../src/plugins/core/devicemanager.cpp)：co-resident pair predicate、worker 参数、manual ETH 无 auto-retry、断线保留对象。
- [mainwindowdevicecontroller.h](../../src/plugins/core/mainwindowdevicecontroller.h) / [mainwindowdevicecontroller.cpp](../../src/plugins/core/mainwindowdevicecontroller.cpp)：Device List、精确 endpoint 复用、provisional tracking、所有 ETH 选择交给 coordinator。
- [mainwindow.h](../../src/plugins/core/mainwindow.h) / [mainwindow.cpp](../../src/plugins/core/mainwindow.cpp)：持有 coordinator、计数式 suspension、GNSS 切换门禁、退出保存。
- [runtimeprofilepersistence.h](../../src/plugins/core/runtimeprofilepersistence.h) / [runtimeprofilepersistence.cpp](../../src/plugins/core/runtimeprofilepersistence.cpp)：capture/read/restore/write 拆分、metadata、`QSaveFile`。
- [fancydevice.cpp](../../src/plugins/htra/fancydevice.cpp)：manual ETH 默认关闭 auto-low-power。

### 不需要修改

- `BusinessManager`：已有 active business getter 和 `setActive(false)`；
- 各具体 baseband 业务的 profile 实现；
- H2 API、单 endpoint/单 channel[0] 的 `FancyDevice` 模型；
- `TxApplyRequest` 的单目标模型；
- USB scanner、USB ownership 和 USB disconnect/reconnect；
- 现有 registered-device 析构关闭机制。

## 13. 最快可实施顺序

### Phase 0：三个阻塞实测

在正式重构前先用最小诊断改动验证：

1. 记录 5000/5001 的 `uid_h32 + uid_l64`，决定纯 UID key 还是 UID+port fallback；
2. 临时仅跳过 A->B 的 `closeForSwitch()`，确认 H2 DLL 允许同进程持有两个 handle；
3. A 输出 CW/Playback，打开 B 后用频谱仪确认 A 连续输出；再分别配置 B，确认两个 handle 可顺序调用且互不停止。

任一 handle 共存或输出连续性失败，立即停止后续实施，不能用 profile/UI 代码掩盖底层限制。

### Phase 1：常驻 handle 最小闭环

只改：

1. `isCoResidentEthPair()`；
2. worker `keepOldOpen`；
3. `resolveManualEthDevice()` 精确 endpoint 复用；
4. provisional 与 remembered 分离；
5. manual ETH 禁止 timer retry；
6. ETH auto-low-power 初始值。

先不接 per-device profile，只用 CW/Playback 验证两台都 open、A/B 切换无 close/open、退出双 close。Streaming 必须等 Phase 2 的“停流后再换指针”门禁完成，不能在 Phase 1 提前测试或交付。

### Phase 2：安全指针切换

1. 建 coordinator 骨架；
2. pipeline suspension；
3. 在改变 current 前同步 `setActive(false)`，完成 Streaming barrier；
4. GNSS open=true 门禁；
5. 目标 capability ready 后仅做一次最终 refresh。

门禁：Streaming 不串台，普通 Playback/CW/Sweep 切走不停。

### Phase 3：Device List

1. Device List 同时显示两个成功 ETH；
2. `[Current] / [Open] / [Offline]`；
3. 点击 closed remembered 条目只 open 一次；
4. 切换期间禁用重复点击。

### Phase 4：UID Profile

1. persistence helper 拆分；
2. `QSaveFile`；
3. full UID 或 UID+port key；
4. coordinator 保存源、恢复目标、首台 seed、第二台 default；
5. Trigger Out、Sweep、全部具体业务和页面选择 round-trip。

### Phase 5：失败与退出收尾

1. 第二台首次 open 失败时无损回到仍 open 的源设备；
2. remembered 设备 reconnect 失败不被删除；
3. 当前 ETH 掉线后停止 timer、无 auto-retry；
4. 退出保存当前 profile并关闭两个 handle；
5. 静态确认 USB 分支未改变。

## 14. 实机验收矩阵

### 14.1 Handle 与输出连续性

1. A open 后连接 B：日志只有 B 的一次 `device_open_eth`，没有 A close/preset。
2. A Fixed CW -> 切 B：频谱仪确认 A 连续。
3. A Fixed Playback -> 切 B：A 波形连续。
4. A FScan/LScan/MScan CW 与 Playback -> 切 B：A 扫描继续。
5. B 配置不同输出 -> 切 A：B 继续，A 按 A profile 全量重配。
6. 两台都建立后切换 50 次：切换区间没有任何 open/close/preset；SDK 调用目标始终是 current UID。

### 14.2 Streaming

1. A Streaming -> 切 B：先看到 A sender stop barrier 完成，再看到 current=A->B；B 不收到 A 的 stream buffer。
2. B 原本在后台 Playback 时，A Streaming 切 B：A 停流，B 的旧 Playback 在最终重配前保持。
3. 切回 A 后，Streaming 只按 A profile重新启动，不自动保留切走前 sender 线程。

### 14.3 Profile

1. A/B 使用明显不同的 Center、Level、RF/MOD、Trigger In/Out、业务类型和 Sweep 参数；往返 20 次不串台。
2. 第二台无文件时为 default；首台仍使用启动 UI seed。
3. CW、普通 Playback、多波形 Playback、Realtime Streaming，以及 Fixed/FScan/LScan/MScan 均完成 profile round-trip。
4. MScan ListMode 参数、selected business、current page 和 Quick Waveform/调制参数恢复。
5. 损坏 B 文件后切 B：使用 default并警告，绝不沿用 A UI。

### 14.4 断线与人工恢复

1. 当前 A 拔线：A 条目保留，timer 停止；连续观察多个周期无 `device_open_eth`。
2. 恢复网络但不点击：仍不连接；点击 A 一次只 open 一次。
3. open 失败：不 retry；再次点击才产生下一次 open。
4. 后台设备拔线：软件允许延迟发现；切回并发生首个失败后进入 Offline/人工恢复流程。

### 14.5 退出

1. A/B 均 open，正常退出；日志中 5000、5001 各一次 ETH final close。
2. 退出后频谱仪确认两台都停止输出。
3. 下次启动手动重新连接，不自动恢复 endpoint catalog；首台仍按启动 UI seed，第二台及后续切换可使用已有 UID profile。

## 15. 已知限制

- Device List 的 `[Open]` 不等于确认正在发射；后台 RF/MOD/错误状态没有实时查询。
- 后台设备的温度、unlevel、GNSS、供电和物理断线在切回前可能不可见。
- UI 同时只表达一个 current 设备；后台设备只能保持最后一次已下发的硬件状态。
- 不支持双 Streaming。Streaming 切走即停，切回后按 profile 重新启动。
- 切回后的全量配置可能使目标输出产生一次短暂 glitch或从波形起点重新播放。
- Profile 不持久化 IQ payload、waveform ID、handle 或线程；源文件缺失时仍由业务报错。
- Fan、System Clock Out、Low Power 暂不进入 UID profile；Low Power 仅为本次设备对象会话状态。
- 后台设备物理掉线可能延迟到切回后的首个 H2 调用才发现。
- 本期不持久化 ETH endpoint 列表；应用重启后仍需手动连接 5000/5001。
- USB/ETH 同时存在时的控制焦点和 profile 行为不属于本期验收。

## 16. 最终实施结论

- 本方案是“两台独立 ETH 设备常驻 open + 单 current 控制焦点”，不是 H2 多通道，也不是两套并行 UI。
- 核心底层改动是 co-resident A/B 切换跳过旧设备 close；正常切换不得向源设备发任何 H2 命令。
- Streaming 必须在 current 指针改变前同步停止；这是防止数据串到目标设备的硬性顺序。
- 普通 CW/Playback/Sweep 切走后依靠设备自身继续运行，后台不轮询、不重配。
- UID profile 解决的是唯一 UI working copy在 A/B 间的状态归属；切回目标后再全量构建和下发。
- manual ETH 失败/断线均无自动重连；已成功对象留在 Device List，只响应用户下一次点击。
- 两个常驻 handle 由现有 registered-device 析构链在应用退出时统一关闭。

## 17. 2026-08-12 实施记录

本轮已按用户确认的一次性交付要求完成 Phase 1–5：

- `DeviceManager` 仅对同 IP 的 5000/5001 手工 ETH 配对启用 `keepOldOpen`，worker 保留 executor 失效动作但跳过源设备 close；
- manual ETH open 失败和当前设备运行时掉线均停止 timer，不再自动 retry、不 unregister 已成功对象；
- Device List 显示成功建立过 UID 的 USB/ETH 设备，ETH 区分 `Current / Open / Offline`，closed remembered 条目点击只触发一次 open；
- `resolveManualEthDevice()` 改为 endpoint 精确复用，不再重配任意 closed 对象；首次失败的 provisional 对象与已成功 remembered 对象分离清理；
- 新增 `DeviceRuntimeProfileCoordinator`，固定执行“保存源 -> suspend -> 停止 active runtime/Streaming barrier -> 切 current -> 恢复目标 -> GNSS -> 单次 refresh”；
- runtime profile 已拆分 capture/read/restore/write，写入使用 `QSaveFile`，per-device 文件以完整 96-bit UID 命名并写入 `deviceContext`；
- 首台成功 ETH 使用启动 UI seed；非首台无 profile、损坏 profile 或 UID metadata 不匹配时恢复 factory default，不继承源 UI；
- pipeline suspension 改为可嵌套计数；退出前保存当前 bound UID profile；manual ETH open 时明确关闭 auto-low-power 默认策略；
- 新增源设备回滚恢复：第二台首次 open 失败时切回仍 open 的源设备，并从刚保存的源 UID profile 恢复 UI 后再统一 refresh。

本轮按用户要求未执行编译或运行。已完成的静态检查：

- `git diff --check` 通过；
- 所有 `DeviceIoWorker::switchDevice()` 直接调用均已传入 `keepOldOpen`；
- 新增 coordinator 源文件已由 Core `CMakeLists.txt` 纳入；
- Device List 路径中不再调用 `configureManualEthTarget()` 重配已有对象；
- coordinator 和 Device List 切换路径不存在 `closeForSwitch`、`device_preset` 或 `device_close` 调用；
- USB scanner、ownership、auto-select、disconnect/retry 主分支未改写，USB 选择仍直接走原 `setCurrentDevice()`。
