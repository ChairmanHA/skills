# 多实例 USB Ownership / Startup Gate 当前实现

## 1. 结论先说

当前仓库里真正生效的多实例防干扰机制是下面这条链路：

- 启动时先把当前进程注册进 `InstanceStateRegistry`，初始状态为 `Idle`
- 若系统里已经存在另一个 `Idle` session，则当前实例直接退出
- 若允许继续启动，则 startup 只会从 **free USB** 集合中自动附着设备
- 若当前没有 free USB，则实例直接以 **no-current Idle** 进入主窗口，不再弹 startup ETH dialog
- 手工 ETH 仍然保留，但它只是主窗口内的普通连接入口。

跨实例自动协调的对象只有 scanner 管理的 USB。手工 ETH 依然按用户输入 `IP + Port` 走普通连接链路，不做跨进程 endpoint 仲裁。

历史设计背景、测试用例和未收口项可结合 [../TaskLog/2026-05-25_multi_instance_device_ownership_refactor_plan.md](../TaskLog/2026-05-25_multi_instance_device_ownership_refactor_plan.md) 一起看；若 TaskLog 与本文有冲突，以本文描述的“当前代码实际行为”为准。

---

## 2. 代码锚点

### 2.1 启动期实例准入：`src/app/main.cpp`

启动时先执行：

- `CurrentSessionRegistration::initialize()`
- `InstanceStateRegistry::initializeCurrentSession()`

随后：

- 把 `otherInstanceCount` 写进 `qApp` property
- 把 `kSuppressStartupUsbAutoSelectProperty` 设成 `otherInstanceCount > 0`
- 若 `InstanceStateRegistry::hasOtherIdleSession()` 为真，则直接退出

这里的语义不是“已有任意其他实例就拒绝启动”，而是“已有其他 **空闲实例** 才拒绝启动”。

### 2.2 启动期 USB 选择：`src/plugins/core/mainwindow.cpp`

`MainWindow::evaluateStartupDeviceGate()` 是 startup gate 的真正实现点。

它的行为分三支：

1. `otherInstanceCount <= 0`
   - 调用 `DeviceManager::setStartupAutoSelectSuppressed(false)`
   - 正常进入主流程

2. 有其他实例，且存在 free USB
   - `availableStartupUsbDevices()` 先取 `DeviceManager::allDevices()`
   - 用 `InstanceStateRegistry::otherOwnedUsbKeys()` 过滤掉已被其他 session 持有的 USB
   - 再按 `DeviceUID` 升序稳定排序
   - 选第一台 `freeStartupUsbDevices.first()` 作为 startup target
   - 调用 `DeviceManager::setCurrentDevice(...)`
   - 再调用 `DeviceManager::setStartupAutoSelectSuppressed(false, false)`

3. 有其他实例，但当前没有 free USB
   - 直接 `completeExtensionsInitialization()`
   - 不再弹 startup ETH dialog
   - 不会在 startup 阶段强行抢设备
   - 当前实现还会继续保留 `startupAutoSelectSuppressed == true`，使这个 secondary idle instance 在后续运行期也不会自动附着后来重新出现的 USB

### 2.3 运行期自动附着：`src/plugins/core/devicemanager.cpp`

运行期自动附着统一通过 `firstAutoSelectableScannerDevice()` 决定候选设备。

当前逻辑：

- 只看 `managedByScannerSnapshot()` 为真的设备
- 用 `InstanceStateRegistry::otherOwnedUsbKeys()` 过滤其他实例已持有的 USB
- 若存在 `preferredReconnectUsbKey`，先尝试命中这台保留 USB
- 若未命中，返回第一台 free scanner USB
- 但当 `preferredReconnectUsbKey != 0` 且 `InstanceStateRegistry::hasOtherAttachContenderSession()` 为真时，会返回 `nullptr`，禁止降级去抢别的 free USB

这套 chooser 会被以下路径复用：

- `DeviceManager::registerDevice()` 的即时 auto-select
- `DeviceManager::setStartupAutoSelectSuppressed(false, allowFallbackSelection)` 的 fallback 选择
- `DeviceManagerPrivate::onDevicesDiscovered()` 的运行期 fallback

选择候选与执行切换是两个边界：

- `preferredReconnectUsbKey == 0` 表示启动期首次自动选择，仍由 `DeviceManager` 直接设置 current，首台设备使用启动 UI seed。
- `preferredReconnectUsbKey != 0` 表示运行期 USB 断连恢复。上述三个入口都只发出 `runtimeFallbackSelectionRequested(device)`，由 `DeviceRuntimeProfileCoordinator` 在 pipeline suspension 内切换 current、恢复目标 UID profile，再执行最终 refresh。
- 运行期 fallback 不得直接调用 `setCurrentDevice()`；否则目标设备会先接收断连源设备残留在 UI 中的 profile，并可能在后续切换时把错误状态保存到目标 UID 文件。

### 2.4 跨进程 owner 状态：`src/libs/utils/instancestateregistry.*`

`InstanceStateRegistry` 维护每个 session 的：

- `SessionState`
  - `Idle`
  - `HoldingEth`
  - `HoldingUsb`
  - `ReconnectingUsb`
- `ownedUsbKeys`

当前 session 状态更新规则：

- 只要 `ownedUsbKeys` 非空，就会被规范化为 `HoldingUsb`
- 否则若当前 `currentDevice` 是已打开 ETH，则为 `HoldingEth`
- 否则回到 `Idle`

`otherOwnedUsbKeys()` 返回所有其他 session 的 USB owner 并集。

`hasOtherIdleSession()` 的判定是：

- 其他 session 的 state 为 `Idle`
- 且它的 `ownedUsbKeys` 为空

`hasOtherAttachContenderSession()` 的判定是：

- 其他 session 为“空的 Idle”
- 或 state 为 `ReconnectingUsb`

### 2.5 UI 层 owner 反馈：`src/plugins/core/mainwindowdevicecontroller.cpp`

主窗口 `Device -> USB Connect` 菜单在 `aboutToShow` 时：

- 读取 `InstanceStateRegistry::otherOwnedUsbKeys()`
- 继续展示所有 scanner 发现的 USB
- 但把被其他实例持有的设备 action 设为 disabled

手工 ETH 入口仍然是同一个菜单里的 `ETH Connect`，但它只负责普通人工连接，不再参与 startup gate。

---

## 3. 当前语义边界

### 3.1 自动协调只覆盖 USB

- startup free-device 选择只看 USB/scanner 设备
- runtime fallback 也只看 USB/scanner 设备
- 手工 ETH 不参与 `otherOwnedUsbKeys()` owner 协调

### 3.2 USB switch-away 保留 handle 与 ownership

当前 `DeviceManager::setCurrentDevice()` 对任意已打开旧设备切到非空目标都设置 `keepOldOpen=true`。`DeviceIoWorker::switchDevice()` 因此不会调用旧 USB 的 `closeForSwitch()`，也不会执行 `device_preset()` / `device_close()`；已经下发的普通 Playback 可以继续由设备自主输出。

这不是恢复旧的 `UsbSessionState::Parked`：旧 `FancyDevice` 仍保持真实 open，切回时直接复用。UI、Business 和状态轮询仍只围绕唯一 `currentDevice` 工作；Streaming sender 在切换前正常停止。

ownership 必须跟随真实 handle：

- retained USB UID 继续保留在当前 session 的 `ownedUsbKeys`
- 其他实例仍将该 USB 视为 occupied
- 只有显式断开、物理注销、固件更新关闭或应用退出真实释放 handle 后，才移除 ownership

manual ETH 使用相同 retain 判定，但不参与 USB owner 协调。同 IP 的 5000/5001 只影响标题栏 A/B 显示。

### 3.3 手工 ETH 只保留“主窗口内人工连接”语义

当前手工 ETH 的职责是：

- 主窗口内输入 endpoint
- 尽量复用同一 `IP + Port` 的已打开设备
- 把 open 失败回写到 `EthConnectDialog`

它不再承担：

- startup 时“无 free USB 就弹 ETH gate”
- 跨实例同 endpoint 冲突仲裁

---

## 4. 当前代码与历史设计的差异

### 4.1 startup ETH gate 已退出主流程

历史设计里曾存在“无空余 USB 时弹 ETH dialog，连接成功后再进入软件”的路径。

当前代码已经不是这条语义：

- 无 free USB 时直接进入 no-current Idle 主窗口
- 不再强制用户先做 ETH 连接

### 4.2 `ReconnectingUsb` 仍在枚举里，但当前没有写入路径

当前代码里：

- `SessionState::ReconnectingUsb` 仍然保留在 `InstanceStateRegistry`
- `hasOtherAttachContenderSession()` 也仍把它当作 attach contender

但仓库当前没有任何把 session state 设置成 `ReconnectingUsb` 的调用点。

因此在现状下，这个状态更多是“为后续设计预留的枚举”，而不是已经落地的运行期事实。

### 4.3 startup chooser 与 runtime chooser 仍未完全统一

当前两条路径都已经受 `otherOwnedUsbKeys()` 过滤，但目标选择顺序仍分叉：

- startup：`availableStartupUsbDevices()` 先收集 free USB，再按 `DeviceUID` 升序排序
- runtime：`firstAutoSelectableScannerDevice()` 先按 `registeredDevice` 当前顺序扫描，再结合 `preferredReconnectUsbKey` 优先命中

这意味着在“同时存在多台 free USB”的场景下，冷启动和运行期断联恢复可能选到不同设备。

### 4.4 当前有意保留的保守策略

当前 `MainWindow::evaluateStartupDeviceGate()` 在“无 free USB”分支里只执行了 `completeExtensionsInitialization()`，没有调用 `DeviceManager::setStartupAutoSelectSuppressed(false, false)`。

单看这段代码，表面后果确实是：该实例后续会一直保持 `startupAutoSelectSuppressed == true`，从而失去“未来有 free USB 时再自动附着”的机会。

但结合当前 reconnect 实现，这更像是**有意保留的保护策略**，不是单纯遗漏：

- 当前设备断联并被 `unregisterDevice()` 清理时，原 owner 实例会把 `preferredReconnectUsbKey` 记成刚断开的 USB UID
- `firstAutoSelectableScannerDevice()` 在扫描到同 UID 的 free USB 时，会优先把这台设备还给原 owner；这个“命中 preferred reconnect”的返回发生在 `hasOtherAttachContenderSession()` 检查之前
- 而另一个“启动时无 free USB、后来进入 no-current Idle 主窗口”的 secondary instance，如果继续保持 `startupAutoSelectSuppressed == true`，就不会在 `registerDevice()` / `onDevicesDiscovered()` 里自动参与这次重连竞争

这正好规避了当前仓库仍未彻底解决的一个 race：

- 两个实例
- 只有一台 USB 设备
- secondary instance 启动时因无 free USB 而进入空闲主窗口
- 唯一这台设备被拔掉再插回

如果此时把 secondary instance 的 `startupAutoSelectSuppressed` 提前清掉，那么：

- 原 owner 实例会因 `preferredReconnectUsbKey` 尝试抢回这台 USB
- secondary idle instance 也会因为“现在看到了一台 free USB”而自动附着

两边都可能在 ownership 重新建立前对同一台物理设备发起自动连接，最终出现“两个实例试图控制同一台机器”的违背设计现象。

所以当前代码的真实取舍是：**优先避免双实例自动抢连同一台回插 USB**，哪怕代价是 secondary idle instance 不再具备“后来有 free USB 时自动附着”的能力。

这仍然是与 2026-05-25 设计稿不一致的地方，但它现在属于“带明确规避目的的实现偏差”，不是无意遗漏。

---

## 5. 维护建议

若后续继续维护这块逻辑，优先关注三件事：

1. startup 与 runtime chooser 是否要收敛成统一 helper
2. `ReconnectingUsb` 是继续落地，还是从 registry 语义里删掉
3. 若将来还想恢复“secondary idle instance 对后来 free USB 的自动附着”，必须先补齐更强的跨进程 reservation / claim 机制，再讨论是否清掉 `startupAutoSelectSuppressed`

关联文档：

- [device_discovery_architecture.md](device_discovery_architecture.md)
- [manual_eth_connect_temporary_design.md](manual_eth_connect_temporary_design.md)
- [htra_multi_device_stageA_design_and_debug.md](htra_multi_device_stageA_design_and_debug.md)
