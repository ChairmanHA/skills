# MiniBar USB 扫描、自动连接与显隐联动实施方案

## 目标

在 `--ui-mode=minibar` 模式下，把设备发现与窗口显隐联动收敛成一套稳定行为：

1. 启动后持续轮询 USB 设备。
2. 发现至少一个可自动发现的 USB 设备时，minibar 正常显示。
3. 没有任何可自动发现的 USB 设备时，minibar 自动隐藏。
4. 当前设备中途断开时，如果还有别的 USB 设备，自动切到其它设备且 minibar 不闪隐。
5. 当前设备中途断开且没有其它 USB 设备时，minibar 自动隐藏。
6. 不破坏现有 HTRA scanner、DeviceManager 自动选择、以及 parked USB 句柄复用语义。

## 2026-04-30 简化收敛结论

基于当前仓库实现，现阶段建议优先采用更小的方案，而不是继续为 `DeviceManager` 新增一套 device presence API。

### 为什么这个简化方案可行

当前链路已经满足几个关键前提：

1. HTRA scanner 会持续扫描 USB。
2. `DeviceManager` 会在没有当前设备时自动选择第一台已发现设备。
3. 当前 USB 断开后，`DeviceManager` 会注销旧设备并重启 scanner。
4. 多台 USB 存在时，旧设备断开后，后续扫描会把新的 fallback 设备重新选成 current，并继续尝试 open。

因此，minibar 不必先关心“是否存在已发现设备”，只关心“当前是否已经有打开成功的 USB 设备”即可。

### 当前推荐方案

只考虑 USB，不考虑 ETH，并按下面的最小逻辑实现：

1. minibar 完成启动后，不立即决定显隐，而是延时 2 秒检查一次当前设备。
2. 若 `DeviceManager::currentDevice()` 是 USB 且 `isOpen() == true`，则显示 minibar。
3. 若 2 秒后仍没有已打开的 USB 设备，则保持隐藏。
4. 监听 `DeviceManager::currentDeviceOpenStateChanged(bool)`。
5. 收到 `true` 时，立即取消隐藏延时，并显示 minibar。
6. 收到 `false` 时，不立即隐藏，而是启动或重置一个 2 秒单次定时器。
7. 这个 2 秒定时器到期后再次检查：如果仍没有已打开的 USB 设备，则隐藏 minibar；如果新的 USB 设备已经打开，则不隐藏。

### 这个方案的边界与取舍

这套方案的前提是：

- 我们接受“只有设备真正 open 成功后，minibar 才显示”。

这意味着：

1. 如果设备已经被扫描到，但仍在重试 open，minibar 可能暂时保持隐藏。
2. 如果断开后下一台设备在 2 秒内完成 open，minibar 不会闪隐。
3. 如果断开后下一台设备超过 2 秒才 open，minibar 会先隐藏，等 open 成功后再显示。

对当前阶段来说，这是可以接受的简化；如果后续觉得“设备明明在，但窗口先隐藏再回来”的体验不能接受，再回到下面那套 presence 方案即可。

### 这次可以省掉的东西

采用这个简化方案后，本阶段可以先不做：

1. 不给 `DeviceManager` 新增 `hasScannerManagedDevices()`。
2. 不给 `DeviceManager` 新增 presence change signal。
3. 不在 `onDevicesDiscovered()`、`registerDevice()`、`unregisterDevice()` 里额外维护 UI presence 状态。

### 仍然必须改的点

即使采用简化方案，下面两点仍然必须改：

1. `MiniBarWindow::initialize()` 不能再固定 `setState(State::Collapsed)`。
2. `CorePlugin::extensionsInitialized()` 里的 minibar 分支不能再无条件 `show/raise/activateWindow`。

原因很直接：如果这两处不改，minibar 还是会在“无设备”时被启动路径硬显示出来，2 秒延时逻辑就失效了。

### 最小实现落点

按这个简化方案，建议把改动收敛到 3 个点：

1. `MiniBarWindow` 内新增一个 2 秒单次定时器，负责启动首判和断开后的延时隐藏。
2. `MiniBarWindow` 连接 `DeviceManager::currentDeviceOpenStateChanged(bool)`，只根据“当前 USB 是否已打开”决定显隐。
3. `CorePlugin` 改为把 minibar 的首显控制权交给 `MiniBarWindow`，而不是自己直接 show。

### Hidden 语义

在这个简化方案里，`Hidden` 应明确收敛成“当前没有已打开的 USB 设备，因此系统隐藏”。

因此建议：

1. 保留 `Expanded -> Collapsed` 的 Esc 行为。
2. 去掉 `Collapsed -> Hidden` 的 Esc 行为。

## 当前实现现状

### 已经具备的基础能力

1. `HTRA::Plugin::initialize()` 已经会创建 scanner、注册到 `DeviceManager`，并立即启动扫描。
2. `DeviceManagerPrivate::onDevicesDiscovered()` 已经会按 scanner snapshot 做设备收敛，并在 `currentDevice == nullptr` 时自动选择第一台设备。
3. `DeviceManagerPrivate::handleDeviceDisconnected()` 已经会在运行时断开后注销当前设备，并重启 scanner。
4. `MiniBarWindow::initialize()` 已经会补齐 `DeviceManager`、`BusinessManager`、`MainWindowDeviceController`、`MainWindowSettingsController` 等基础启动基座。

### 当前与需求的差距

1. `MiniBarWindow::initialize()` 现在固定 `setState(Collapsed)`，不看设备是否存在。
2. `CorePlugin::extensionsInitialized()` 现在会无条件 `show/raise/activateWindow` minibar。
3. `MiniBarWindow` 当前没有订阅任何设备存在性变化信号，也没有“无设备时强制隐藏”的逻辑。
4. 当前 `Hidden` 既被当成原型期的本地交互状态，又将被需求拿来表示“无设备时系统隐藏”，语义会冲突。

## 设计原则

### 一、不要把 USB 扫描/自动连接搬进 MiniBarWindow

扫描与自动连接的归属仍应留在 `DeviceManager + scanner`：

1. scanner 负责周期性发现设备。
2. `DeviceManager` 负责设备列表收敛、自动选择、断开清理、状态轮询。
3. `MiniBarWindow` 只负责根据“当前是否存在可自动发现设备”决定窗口显隐。

否则会把本应全局唯一的设备状态机分裂成 “底层一套 + UI 一套”，后续很容易出现主窗、minibar、业务层三方状态不一致。

### 二、显隐依据应当是“是否存在 scanner 管理的 USB 设备”，而不是“当前设备是否已 open 成功”

推荐把显隐条件定义为：

- `DeviceManager::allDevices()` 中是否存在 `managedByScannerSnapshot() == true` 的设备。

原因：

1. 这样可以覆盖“设备已被扫描到，但仍在自动 open 重试”的阶段，避免窗口先隐藏再显示。
2. 当前设备断开但列表里还有其它 USB 设备时，窗口不应因为 `currentDeviceChanged(nullptr)` 的瞬时空档而闪隐。
3. 手动 ETH 设备不属于本需求的“USB 自动扫描/自动连接”范围，不应该驱动 minibar 显示。

## 推荐改造方案

### 1. 在 DeviceManager 暴露“可自动发现设备存在性”能力

建议在 `DeviceManager` 增加一组轻量接口：

1. `static bool hasScannerManagedDevices();`
2. 一个设备清单变化信号，例如：
   - `void deviceInventoryChanged();`
   - 或者更窄一点：`void scannerManagedDevicePresenceChanged(bool hasDevices);`

推荐优先使用“存在性变化信号”，因为 minibar 只关心“有/无”，不关心完整列表。

### 2. 在 DeviceManager 内部统一计算并发出 presence 变化

建议在这些路径后统一重新计算 `hasScannerManagedDevices()`：

1. `registerDevice()`
2. `unregisterDevice()`
3. `DeviceManagerPrivate::onDevicesDiscovered()` 完成增删收敛后

实现要求：

1. 只在 presence 真正变化时发信号，避免每次扫描都打扰 UI。
2. 过滤条件只看 `managedByScannerSnapshot()`，不要把手动 ETH 算进去。
3. 不要改变现有 `registerDevice()` 的“无 current 时自动 setCurrentDevice()` 逻辑。

### 3. MiniBarWindow 引入“设备可见性门控”而不是直接把业务逻辑塞进现有 m_state

当前三态是：`Hidden / Collapsed / Expanded`。

为满足新需求，建议把它改成“两层语义”：

1. 交互态：`Collapsed / Expanded`
2. 设备门控态：`hasScannerManagedDevice`

最终可见态由两者共同决定：

1. 当 `hasScannerManagedDevice == false` 时，强制 `Hidden`
2. 当 `hasScannerManagedDevice == true` 时，恢复到一个可见交互态

### 4. 明确 Hidden 的新语义，避免和原型期 Esc 行为冲突

当前 `Esc` 在 `Collapsed` 态会把 minibar 直接切到 `Hidden`。这和“有设备就正常显示”的新需求冲突。

推荐处理方式：

1. 在设备联动模式下，把 `Hidden` 重新定义为“系统因无设备而隐藏”。
2. 去掉 `Collapsed -> Hidden` 的 Esc 快捷路径。
3. 保留 `Expanded -> Collapsed` 的 Esc 行为。

这是本次需求里必须明确的一点，否则会出现“设备其实还在，但用户前一次按了 Esc，窗口永远不再出来”的歧义。

### 5. MiniBarWindow 启动时不要再无条件进入 Collapsed

`MiniBarWindow::initialize()` 建议改为：

1. 完成全局 bootstrap。
2. 连接 DeviceManager 的 presence 变化信号。
3. 读取本地位置与本地交互偏好。
4. 立即根据 `hasScannerManagedDevices()` 决定初始显示：
   - 有设备：进入 `Collapsed`
   - 无设备：进入 `Hidden`

若担心恢复 `Expanded` 会导致热插拔后突然弹出大面板，建议统一恢复到 `Collapsed`，不要自动恢复到 `Expanded`。

### 6. CorePlugin 不应再强制 show minibar

当前 `CorePlugin::extensionsInitialized()` 对 minibar 走的是无条件 show。

这会直接绕过设备门控。

建议改成：

1. `CorePlugin` 只保留 main 模式下的现有 show 路径。
2. minibar 模式下不直接 `show()`。
3. 改为调用 `MiniBarWindow` 的一个轻量同步入口，例如：
   - `syncVisibilityFromDeviceState()`
   - 或 `finalizeStartupVisibility()`

把首显决策留在 `MiniBarWindow` 内部，避免 CorePlugin 和 MiniBarWindow 各自维护一套首显逻辑。

### 7. 建议为 scanner 增加一次“启动即扫描”优化

当前 `HTRADeviceScanner::start()` 只是启动线程和定时器，首次有效扫描会落在一个 interval 之后。

如果 minibar 按新需求启动时默认隐藏，那么首屏会出现最多约 1 秒的“设备已经在，但窗口没出来”的等待。

推荐优化：

1. scanner 启动后立即触发一次扫描。
2. 后续仍保持现有 1 秒轮询周期。

这不是功能正确性的前提，但对 minibar 模式的体感很重要。

## 具体文件修改建议

### 一、`src/plugins/core/devicemanager.h`

建议新增：

1. `static bool hasScannerManagedDevices();`
2. `signals:` 下新增 presence 变化信号。

如果后续还有别的轻 UI 也要跟设备有无联动，这个接口会直接复用。

### 二、`src/plugins/core/devicemanager_p.h`

建议新增一个缓存字段，例如：

1. `bool lastScannerManagedPresence = false;`

用于只在 presence 变化时发信号，避免 UI 每次扫描都刷新。

### 三、`src/plugins/core/devicemanager.cpp`

建议新增一个内部 helper，例如：

1. `computeScannerManagedPresenceLocked()`
2. `publishScannerManagedPresenceIfChanged()`

并在以下路径调用：

1. `registerDevice()` 完成注册后
2. `unregisterDevice()` 完成注销后
3. `onDevicesDiscovered()` 完成收敛后

注意点：

1. 发信号要放在锁外。
2. presence 变化通知不能替代现有 `currentDeviceChanged/currentDeviceOpenStateChanged`，只能补充。
3. 不能破坏当前 USB parked / reconnect / delayed -8 的逻辑。

### 四、`src/plugins/core/minibarwindow.h`

建议新增成员：

1. `bool m_hasScannerManagedDevice = false;`
2. 一个“可见交互态”字段，例如 `State m_visibleState = State::Collapsed;`
3. 设备显隐同步函数声明，例如：
   - `void bindDeviceVisibility();`
   - `void syncVisibilityFromDevicePresence();`
   - `bool shouldShowForDevices() const;`

### 五、`src/plugins/core/minibarwindow.cpp`

建议调整：

1. `initialize()` 不再直接 `setState(State::Collapsed)`。
2. 增加对 `DeviceManager` presence 信号的连接。
3. 新增一个统一的显隐同步入口：
   - `presence == false` 时隐藏
   - `presence == true` 时显示 `Collapsed`
4. `keyPressEvent()` 去掉 `Collapsed -> Hidden` 的 Esc 分支。

如果希望进一步减少闪隐，可以在 presence 不变时不重复触发 `show/hide`。

### 六、`src/plugins/core/coreplugin.cpp`

建议修改 `extensionsInitialized()` 的 minibar 分支：

1. 删除无条件 `show/raise/activateWindow`
2. 改为仅通知 minibar 执行一次“按设备状态首显同步”

这样首显逻辑只保留在一处。

### 七、`src/plugins/htra/htradevicescanner.cpp`

建议做一个小优化：

1. scanner 启动后立刻做一次扫描
2. 保持后续定时扫描不变

这部分建议单独做成一个很小的可回退切片，不和 MiniBarWindow 状态机改动混在一起。

## 实施顺序建议

### Phase 1: 先补底层 presence 能力

目标：让 UI 能可靠拿到“当前是否存在 scanner 管理设备”的事实，而不是靠猜。

步骤：

1. 给 `DeviceManager` 增加 presence 计算与变化信号。
2. 保证 register / unregister / reconcile 后都会刷新 presence。
3. 静态检查确认不影响 currentDevice 自动选择。

### Phase 2: 再改 MiniBarWindow 状态机

目标：把现有“原型交互 Hidden”升级成“设备门控 Hidden”。

步骤：

1. 引入设备门控字段。
2. `initialize()` 改成按 presence 决定首态。
3. 删除 `Collapsed -> Hidden` 的 Esc 路径。
4. 只在 `presence == true` 时允许显示窗口。

### Phase 3: 收口 CorePlugin 首显路径

目标：防止 CorePlugin 抢先把本应隐藏的 minibar show 出来。

步骤：

1. 删除 minibar 分支的无条件 show。
2. 保留 main 模式现状不变。
3. 首显完全由 MiniBarWindow 内部同步逻辑决定。

### Phase 4: 再做 scanner 启动即扫描优化

目标：把“启动时最多等一个周期”的体验问题降到最低。

这一步建议在前 3 步稳定后再做，避免把功能正确性和体验优化混在一起调试。

## 验证矩阵

### 启动场景

1. 启动 minibar，未插 USB：窗口不出现。
2. 启动 minibar，插 1 台 USB：窗口自动出现，且 currentDevice 自动选中该设备。
3. 启动 minibar，插 2 台 USB：窗口自动出现，且自动选择第一台可用设备。

### 运行时插拔

1. 启动时无设备，运行中插入 1 台 USB：窗口自动出现。
2. 当前设备拔掉且没有其它 USB：窗口自动隐藏。
3. 当前设备拔掉但还有其它 USB：窗口保持可见，自动切到其它设备，不应先隐藏再显示。
4. 所有 USB 拔掉后再次插回：窗口重新出现。

### 异常与边界

1. scanner 因 USB API 锁竞争而临时跳过一轮时，窗口不应误隐藏。
2. open 失败但设备仍在枚举中时，窗口不应因为“尚未 open 成功”而误隐藏。
3. firmware update disconnect mode 打开时，不应触发新的自动连接回归。
4. 手动 ETH 连接不应驱动 minibar 显示。

## 不建议本次一起做的事情

1. 不要在本次需求里重构 `MainWindowDeviceController`。
2. 不要同时把 `DeviceInfoWidget` 正式接进 minibar。
3. 不要在同一批改动里改 scanner interval、UI 样式、设备详情布局。
4. 不要把“有设备就显示”实现成轮询 QWidget 自己查设备，这会把本应由 DeviceManager 持有的全局事实再次分叉。

## 结论

这次需求的正确落点不是“给 minibar 加一个定时器自己查设备”，而是：

1. 保持 `HTRA scanner + DeviceManager` 继续负责设备发现与自动连接。
2. 给 `DeviceManager` 补一个明确的“scanner 管理设备 presence”输出。
3. 让 `MiniBarWindow` 仅根据这个 presence 做显隐门控。
4. 去掉 `CorePlugin` 对 minibar 的无条件 show。

这样改完后，minibar 模式下的 USB 设备扫描、自动连接、断开后隐藏、存在其它设备时保持显示，都会落在同一套一致的状态机里。