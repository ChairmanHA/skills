# 手工 ETH 连接临时方案与后续 API 演进边界

## 1. 背景与定位

当前仓库的设备主链路仍以“USB 扫描快照 + `currentDevice` 打开/关闭调度”为核心。

手工 ETH 接入的目标不是重做整套设备架构，而是在不破坏现有 USB 发现链路的前提下，为 HTRA 增加一个最小可用的手工连接入口：

- `Device` 菜单使用统一 `Device List` 展示 USB 与已成功连接的 ETH
- 独立 `ETH Connect` 只负责手工创建/打开 ETH endpoint
- 用户手工输入一个共享 IP，在固定显示的 5000/5001 两行中勾选需要连接的端口；默认两项都勾选
- 成功后仍复用现有 `DeviceManager -> BusinessManager -> DeviceOperator` 主链路

因此，这个方案本质上是一个“**manual-managed target 对接既有 open/runtime 主链路**”的过渡实现，而不是最终的统一设备发现模型。

---

## 2. 当前实现如何分层

### 2.1 UI / Controller 层

入口在 `Core::Internal::MainWindowDeviceController`：

- `Device List` 从 `DeviceManager::allDevices()` 展示 USB 与所有已取得 UID 的 manual ETH；后续设备选择统一从该列表进入 profile coordinator
- `ETH Connect` 打开 `EthConnectDialog`，承担一个共享 IP 下一个或两个 manual ETH endpoint 的创建/首次打开；普通构建不会在启动时自动弹出该窗口，只有启用编译选项 `SGS_ENABLE_STARTUP_ETH_CONNECT_DIALOG=ON` 的特殊构建才会弹出；5000/5001 始终按此顺序显示，标签为 `port 1` / `port 2`，每行尾部复选框决定是否纳入批次，默认两项都勾选
- `EthConnectDialog` 负责采集 `IP Address` 和显示顺序内的端口列表，并根据候选本机接口数量切换展示方式：未找到候选接口时直接显示 `Not found`；只有一张候选接口时用单个 QLabel 展示，只有多张候选接口时才显示下拉框列出候选项。候选项会按“Physical Ethernet > USB Ethernet > Other Network Link”排序，显示文本形如 `Physical Ethernet: 192.168.1.1`；Windows 下仍会显式排除 VMware 等虚拟网卡地址，Linux fallback 则通过接口名与 sysfs 路径做启发式分类。idle 时的软警告不再绑定到当前下拉选中项，而是基于全部检测到的本机接口整体判断：若未找到任何候选接口，或所有候选接口都不与目标 IP 同网段，则提示用户确保主机存在与设备同网段或当前路由可达目标 IP 的链路；若任意候选接口已与目标 IP 同网段，则不显示提示。本机接口枚举和同网段判断只提供展示与软提示，不参与 `Connect` 可用性；只要目标 IPv4 合法且至少勾选一个端口，用户就可以发起连接。这样做是因为当前 SDK / API 实际不会把 UI 中的候选项绑定成真实出接口，最终仍由系统路由决定
- 弹窗通过 `runAsync()` 以异步模态方式打开，避免连接期间继续操作 MainWindow 配置。idle/失败状态可以使用标题栏关闭或 `Cancel` 退出；批次活动期间输入、端口选择和 `Connect` 均禁用，`Cancel` 禁用、标题栏关闭隐藏，`reject()` 也直接忽略，因此用户不能中途取消连接
- 控制器把单端口也视为长度 1 的短批次。端口严格按显示顺序逐个通过 `DeviceRuntimeProfileCoordinator` 切换，等待每次结果与协调器 terminal 双锁存后才启动下一行；每一行独立提交，成功 endpoint 立即保留，失败的新建 endpoint 只在该次切换终止后做局部移除，不再恢复批前 current 或删除其他已成功 endpoint
- open、ping 预检、单次 switch/profile rollback 和超时的中间全局错误按活动批次计数抑制，不再依赖 `pendingEthDevice` 的瞬时生命周期。watchdog 是活动批次唯一的 abort 入口；abort 后必须等 source/no-current 的 DeviceManager 生命周期结果真正返回，协调器才解除 pipeline suspension，控制器随后才能清理失败 endpoint。任一端口成功后弹窗在全部请求行尝试完成后关闭并进入正常使用；只有全部端口失败才在弹窗内显示一次汇总
- 控制器在发起新连接前，会先按 `IP + Port` 查找是否已经存在一个已打开的手工 ETH 设备；若命中，则直接复用现有 `IDevice` 对象，不再再次进入 `device_open_eth(...)`
- 若某一行命中的设备同时就是当前 `currentDevice` 且已打开，该行直接计为成功，不再调用 `DeviceManager::setCurrentDevice(...)`；双端口批次仍继续尝试下一行
- 不同 IP 的 manual ETH 可以分别创建并进入 Device List；同 IP 的 5000/5001 配对只用于标题栏 A/B，A 固定对应 5000、B 固定对应 5001

#### 启动时的 ETH 恢复与弹窗策略

启动连接策略由 `src/app/main.cpp` 在 `PluginManager::loadPlugins()` 之前计算一次，并通过应用属性 `SGStudio.RestoreLastDeviceConnection` 传给 HTRA plugin。该属性在本次进程生命周期内不再修改。

`restoreLastDeviceConnection` 满足以下任一条件时为 true：

- `APP/Reboot=True`，表示语言、主题、显示模式等需要连续性恢复的应用重启；
- 命令行包含 `--UpdateCompleted`，表示更新完成后的重启；
- `APP/StartSetting=Last`，表示普通启动选择上次运行配置。

启动 ETH 恢复还必须同时满足上次成功传输类型为 ETH、endpoint 地址合法、端口为 5000/5001、当前没有设备且没有其他实例。恢复只创建并尝试上次成功的一个 ETH endpoint，不自动创建共享机箱的另一个 A/B 端口。恢复失败时注销临时 manual ETH，后续 USB 发现仍按既有 scanner 和 `DeviceRuntimeProfileCoordinator` fallback 路径处理。

普通 `Default` 或 `User` 启动不会因为 `Settings.ini` 中残留上次 ETH endpoint 就主动创建 ETH；此时由 USB scanner 自动选择 USB 设备，没有 USB 时保持无 current。上次成功传输为 USB 时，所有启动模式也继续由 USB scanner 处理。

启动 ETH 自动恢复与 ETH Connect 弹窗是两个独立行为。`SGS_ENABLE_STARTUP_ETH_CONNECT_DIALOG` 默认关闭，只控制特殊构建是否在启动完成后弹出手工连接窗口；它不改变上述恢复策略，也不改变 Device List、A/B 或 profile coordinator 的切换语义。

这层复杂的原因，是要把“用户输入一个端点”映射到当前已有的“选中一个 `IDevice*` 并让 `DeviceManager::setCurrentDevice()` 串行 close/open”模型里。

### 2.2 Device 创建层

当前用 `Core::IManualEthDeviceFactory` 作为插件到 Core 的窄接口：

- Core 不直接依赖 HTRA 驱动类型
- HTRA plugin 通过 `Plugin::createManualEthDevice(...)` 创建 `FancyDevice`
- `FancyDevice::configureManualEthTarget(...)` 写入 `IP/Port/Timeout` 并切换到 `EthManual` transport
- `IDevice::ethEndpoint(...)` 作为一个很窄的查询 seam 暴露当前 ETH endpoint，供 `MainWindowDeviceController` 在创建新设备前做 endpoint 去重与复用

这一步的价值，是把“手工端点如何变成具体驱动对象”的复杂度限制在插件层，而没有把 HTRA 细节扩散到 `DeviceManager`。

### 2.3 Driver 层

当前没有新建独立 `HTRAEthDevice`，而是在 `FancyDevice` 内部增加：

- `TransportKind::UsbDiscovered`
- `TransportKind::EthManual`

`FancyDevice::open()` 根据 transport 选择：

- USB 路径：`device_open_usb(...)`
- ETH 路径：`device_open_eth(...)`

同时，当前 `FancyDevice` 在 ETH 路径上还额外承担两条明确语义：

- `closeForSwitch()` 真正被调用时，ETH 走完整释放；正常切到非空目标时由 `DeviceManager` 通用 retain 策略跳过该函数
- `updateRealTimeStatus()` 在检测到 ETH 已断开时，仍会执行一次 `device_close()` 释放底层资源，而不是沿用旧的“断开后析构跳过 close”语义

这样做的目的，是最大化复用现有能力：

- `configuration()`
- 实时状态查询
- GNSS / trigger / reference clock 能力
- 错误翻译
- 关闭 / reset / writeback 语义

### 2.4 Manager 层

`DeviceManager` 仍然是唯一的打开/关闭编排者：

- 仍然维护全局 `currentDevice`
- 仍然只在 I/O worker 里串行执行“旧设备切换清理 + 新设备 open”
- 仍然用 `currentDeviceOpenStateChanged(false -> true/false)` 驱动 UI 和 Business

这点很关键：**手工 ETH 并没有绕开 DeviceManager，而只是把“设备对象的来源”从 scanner 扩展到了 manual factory。**

### 2.5 切换语义（按设备生命周期选择 retain / release）

`DeviceManager` 在“从一个当前设备切到另一个当前设备”时先计算 `keepOldOpen`：

1. 任意旧设备已打开且目标非空：保留旧 handle，使已下发 Playback 继续自主输出。
2. 显式切到 `nullptr`：调用 `close()` 真实释放当前设备。

对当前 HTRA 手工 ETH 方案，这意味着：

- USB retain 不使用历史 parked/probe 状态机，设备对象保持 `isOpen()==true`，切回时直接复用。
- retained USB ownership 不释放，其他实例不能 claim 仍打开的设备。
- manual ETH 与 USB 使用相同 retain 判定；正常切换时，同 IP 的 5000/5001 只决定标题栏是否显示 A/B，不改变 retain 行为。
- Streaming sender 在 profile/device 切换前停止；retain 只保证设备自主运行的普通 Playback 不被 preset。

共享机箱失联是上述普通切换语义的例外：

- 同 IP 的 5000/5001 被视为同一个网络故障域。current manual ETH 的状态轮询确认断联后，`DeviceManager` 会注销 current 以及同 IP 下全部 manual ETH endpoint。
- 切换到 retained 非 current ETH 前的异步 ping 若明确失败或超时，也会注销同 IP 下全部非 current 5000/5001 endpoint；同 IP current 始终保留。
- 注销组时先统一调用 `markConnectionLost()`，使异步析构跳过 `device_preset()`、只释放旧 handle；UID profile 文件保持不变。
- 注销后不自动恢复 ETH。网络恢复时必须通过 `ETH Connect` 重新创建 endpoint 并执行 `device_open_eth()`。
- 如果 current ETH 注销后自动选择到 USB，切换仍必须经过 `DeviceRuntimeProfileCoordinator` 完成目标 UID profile restore。

---

## 3. 为什么需要 `managedByScannerSnapshot()`

当前 USB 扫描链路的 reconcile 语义是：

- `uid == 0` 的注册设备会被清理
- “最新扫描快照里不存在”的注册设备会被视为物理消失并移除

这对 USB 发现设备是对的，但对手工 ETH 是错的：

- 手工 ETH 不是扫描快照产物
- 下一轮 USB scan 不应把它误删

因此当前引入了 `IDevice::managedByScannerSnapshot()`：

- USB 发现设备返回 `true`
- 手工 ETH 目标返回 `false`

这相当于在现有架构里人为补了一个“设备来源”维度，让 manual-managed device 暂时能和 scanner-managed device 共存。

这个点看起来复杂，但它实际上也是后续真正抽象“设备来源 / 设备描述符”的前置 seam。

---

## 4. 为什么这个临时方案会显得复杂

复杂度主要来自“新来源设备”要塞进“旧的 scanner/currentDevice 模型”里，而不是来自业务主链路本身。

当前额外引入的复杂度集中在三处：

1. **设备来源区分**
   - scanner-managed
   - manual-managed

2. **设备创建 seam**
   - `IManualEthDeviceFactory`
   - `configureManualEthTarget(...)`
   - `ethEndpoint(...)`

3. **弹窗局部失败处理**
   - ETH Connect 的中间错误不走全局错误框；全部失败时在弹窗内汇总并允许重试，部分成功时直接关闭弹窗并保留成功 endpoint

4. **endpoint 级复用与 no-op 防卡死处理**
   - 同一 `IP + Port` 再次连接时复用已打开设备，而不是重复 `device_open_eth(...)`
   - 若复用结果恰好就是当前已打开设备，需要在 UI 层直接关闭弹窗，不能再走一次 `setCurrentDevice(same_open_device)`

这些复杂度都属于“入口编排复杂度”，不是配置、业务、状态轮询、运行时等核心域复杂度。

---

## 5. 未来若 API 升级为抽象“发现 + 打开”，重构风险在哪里

结论先说：**会有重构，但大概率不是推倒重来，而是把当前临时 seam 收敛为正式抽象。**

### 5.1 大概率可以保留不动的层

以下层次当前已经和 transport/source 解耦，未来通常不用重写：

- `DeviceManager` 的 `currentDevice` + I/O worker 串行 open/close 模型
- `currentDeviceOpenStateChanged(bool)` 驱动 UI / Business 的契约
- `BusinessManager -> DeviceOperator -> IDevice::configuration()` 这条配置主链路
- `CommonDeviceProfile`、实时状态、DeviceInfoWidget 的大部分逻辑

这些层只关心“当前设备能否被打开、打开后如何配置”，并不关心它来自 USB 扫描还是 ETH 手工输入。

### 5.2 未来最可能收敛/替换的层

若 SDK 将来支持统一的“设备描述符 / 发现结果 / 打开参数”模型，当前临时方案最可能重构的是：

1. `IManualEthDeviceFactory`
   - 可能升级成更通用的 `IDeviceEndpointFactory` / `IDeviceOpener` / `IDeviceDescriptorFactory`

2. `managedByScannerSnapshot()`
   - 可能被更正式的“设备来源 metadata”替代，例如：
     - `DiscoveredByScanner`
     - `ManualEndpoint`
     - `RemoteDescriptor`

3. `configureManualEthTarget(...)` / `ethEndpoint(...)`
   - 可能从 `IDevice` 上移走，改成“先有 endpoint/descriptor，再 create/open device”

4. `EthConnectDialog`
   - 未来可能不再是 ETH 特例，而是统一为“设备端点输入 / 远端设备连接”入口的一种 UI 形态

### 5.3 为什么当前实现反而有利于后续演进

这次临时实现虽然看起来复杂，但它已经把未来最可能变化的边界显式化了：

- 用 `IManualEthDeviceFactory` 隔开了“Core 不知道具体驱动如何创建”
- 用 `managedByScannerSnapshot()` 显式暴露了“设备来源”问题
- 用 `TransportKind` 把 USB / ETH 差异限制在驱动 open 层

因此将来真要升级，不是把一堆隐式耦合拆开，而是把**已经显式存在的 seam 抽象得更正规**。

从重构风险上看，这比“现在先把 ETH 混进 USB scanner 里凑合跑”要安全得多。

---

## 6. 下一步 UI 交互优化：弹窗存活期间的 1 秒级轮询刷新

当前 `EthConnectDialog` 的本机接口状态只会在弹窗初始化、用户修改目标 `IP/Port`、以及本机候选接口下拉框切换时重新计算。对最常见的直连场景，这意味着：

- 弹窗打开时若显示 `Not found`
- 用户随后插入网线、启用 USB 有线网卡或等待 DHCP / 静态 IP 配置生效
- 界面不会自动更新，必须关闭并重新打开弹窗，才能看到 `Not found -> Physical Ethernet: 192.168.1.1` 这类变化

这个交互不是功能性错误，但体验上偏生硬。对手工 ETH 连接入口，更自然的行为应当是：**弹窗打开后，本机链路状态在有限频率下自动刷新。**

### 6.1 推荐方案

优先采用“**弹窗存活期间的 1 秒级轮询刷新**”，而不是先上系统级网络事件订阅。

推荐边界如下：

1. 仅在 `EthConnectDialog` 打开期间轮询
   - 弹窗 `showEvent` / 初始化后启动定时器
   - 弹窗关闭、析构或 `finished` 后立即停止

2. 轮询频率控制在约 `1000 ms`
   - 不需要更高频率；本机插线、DHCP 生效、网卡 Up/Down 都不是毫秒级交互
   - `1000 ms` 足以让用户感知为“自动更新”，同时避免无意义的高频重算

3. 每轮只做“本机接口重新枚举”
   - Windows：本质上是重复执行一次 `GetAdaptersAddresses(...)`
   - Linux：本质上是重复执行一次 `QNetworkInterface::allInterfaces()`，并读取有限的 sysfs 路径/接口状态
   - 不额外做 `ping`、ARP 探测、socket 尝试连接或其它主动网络 I/O

4. 仅在快照变化时刷新 UI
   - 对比上一轮和当前轮的本机候选接口集合
   - 只有当候选项、候选顺序、显示文本、`Not found` / 单候选 / 多候选状态发生变化时，才真正调用 UI 更新
   - 这样可以避免 1 秒一次的无差别 `setText` / `setVisible` 造成闪烁或无效 repaint

5. `connecting` 状态暂停刷新
   - 用户点击 `Connect` 后，弹窗进入连接中状态，此时应冻结本机接口显示，避免连接过程里 UI 还在因网卡瞬时变化而抖动
   - 连接失败退回 idle 后，再恢复轮询即可

### 6.2 预期用户体验

采用该方案后，最直接的交互收益是：

1. 弹窗刚打开时是 `Not found`
2. 用户插上网线或启用有线网卡
3. 约 1 秒内自动切换为本机 IP 显示，并同步更新提示文案；`Connect` 的可用状态不依赖本机接口枚举结果

反向路径同样成立：

1. 弹窗打开时已显示本机 IP
2. 用户拔掉网线或关闭本地有线接口
3. 约 1 秒内自动退回 `Not found` 并更新提示文案，但不禁用 `Connect`

这会明显优于当前“必须关窗重开才能看到状态变化”的使用感受。

### 6.3 复杂度评估

这个优化的实现复杂度属于**低到中等**，因为它复用的仍是现有本机接口收集逻辑，而不是引入一套新的平台监听框架。

预计主要改动集中在 `EthConnectDialog`：

1. 增加一个 `QTimer`
2. 保存上一轮本机接口快照
3. 定时触发一次“重新枚举 -> 比较快照 -> 必要时刷新 UI”
4. 在连接中暂停、弹窗关闭时停止

整个改动基本可以限制在 Core 插件内完成，不需要改动 `DeviceManager`、`BusinessManager` 或驱动层接口。

### 6.4 推荐实现要点

若后续开始落代码，建议按以下顺序实施：

1. 在 `EthConnectDialog` 内增加定时器和快照字段
2. 弹窗初始化时先立即刷新一次，再启动 1 秒定时器
3. 定时器只在 idle 状态工作，`connecting` 时暂停
4. 快照无变化时不刷新 UI
5. 快照变化时统一调用现有 `updateIdleStatus()` 与 `updateConnectButtonState()` 收敛界面状态

这样做可以以最小实现代价获得最明显的交互提升。

## 7. 后续 API 演进的建议目标

如果后续 SDK 真能提供统一的发现/打开抽象，工程侧更推荐收敛到下面这个方向：

1. **Device Descriptor First**
   - UI 和 scanner 都先产出统一的 descriptor / endpoint
   - `DeviceManager` 接受 descriptor，再决定创建/打开哪个 driver instance

2. **Source-aware Reconcile**
   - reconcile 不再只看 UID，而是同时看来源类别与生命周期策略

3. **Open Result as Structured Data**
   - open 失败应提供结构化错误：
     - transport category
     - raw code
     - user-facing message
     - retryability

4. **UI 入口统一化**
   - `USB Connect` / `ETH Connect` 最终可以都是“连接源”的不同实现，而不是两套完全不同的后端语义

---

## 8. 当前方案的工程判断

当前手工 ETH 方案可以接受，前提是把它明确认定为：

- 过渡期方案
- 入口复杂度局部增加
- 业务主链路不变
- 后续可向统一 descriptor/open 模型平滑迁移
- 当前已补上 endpoint 级去重复用，以及 ETH 断开/切换时的明确释放语义，因此“重复打开同一 endpoint 导致二次 `device_open_eth(...)` 报错”不再是这条方案的已知结构性缺口

所以，对“后续 API 更新到抽象设备发现打开会不会有重构问题”的回答是：

- **会有，但主要是入口建模和设备来源建模的重构，不是核心业务链路重写。**
- **当前这版实现已经把最需要演进的边界显式化，属于可收敛的临时复杂度，而不是未来必炸的技术债。**

---

## 9. 相关文档

- [device_discovery_architecture.md](device_discovery_architecture.md)
- [device_open_ui_config_flow.md](device_open_ui_config_flow.md)
- [device_status_ui_feedback.md](device_status_ui_feedback.md)
