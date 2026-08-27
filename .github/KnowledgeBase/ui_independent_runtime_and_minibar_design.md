# UI 无关运行时与 Minibar 设计说明

> 现状说明（2026-07-16）：legacy in-process `MiniBarWindow` 及其私有 menu/panel
> host 已删除。本文中点名这些类型的段落只保留为运行时解耦、entry host 和轻量
> UI 复用设计史；当前产品路径与 typed IPC 实现见
> `minibar_cs_helper_scpi_architecture.md`。

## 删除后的保留边界

1. `CorePlugin` 只创建 `MainWindow`；minibar 由独立 `SGStudioMiniBar` helper 承载。
2. `CoreRuntimeServices`、`DeviceRuntimeBridge`、`BusinessManager`、
   `IBusinessEntryHost` 和 `TxSessionService` 仍是可复用的 UI 无关边界。
3. helper 通过 `MinibarHelperController + RemoteMinibarService + MinibarIpc` 消费
   main 的权威状态，不成为第二个 runtime owner。
4. `LabelButton`、`SwitchButton::compactMode`、`OverlayContainer`、数字键盘和
   LayerShellQt host 方案保留在共享控件或 helper 中；不保留未被调用的 legacy
   window/menu/presenter 类。
5. main 中的 remote editor 请求仍可短暂标记业务 panel 为 minibar 上下文，用于
   复用无交互生成/错误处理路径；这不是重新挂载 in-process panel。

本文档记录当前已经落地的“UI 无关运行时”设计，以及它对 minibar 模式的直接意义。

## 设计目标

1. 公共运行时服务不依赖某个具体 UI 先实例化。
2. 插件的后台逻辑不再把 `MainWindow::instance()` 或 `FancyTabWidget::instance()` 当成服务入口。
3. UI 模式只影响展示与交互，不再决定 scanner、business、device status sync 是否完整启动。

## 当前分层

### 1. 共享运行时层

当前由 `CoreRuntimeServices` 统一持有并初始化：

1. property schema
2. `ActionManager`
3. `CommonDeviceProfile`
4. `DeviceManager`
5. `BusinessManager`
6. `DeviceRuntimeBridge`
7. `TxSessionService`
8. 默认运行时状态，例如默认 `mute`

这一层先于 `MainWindow` 创建，并独立于 helper 进程的显隐与生命周期。

其中 `TxSessionService` 当前已经成为共享发射 owner，负责：

1. 监听共享 `RF / MOD / CommonDeviceProfile`
2. 统一 resolve pipeline
3. 构造 `TxApplyRequest`
4. 执行 core-managed / legacy-managed 分流
5. 在设备能力 reconcile 完成后重建带 UID/capability revision 的 request；reopen 不重放旧缓存 request

### 2. 设备运行时桥接层

当前由 `DeviceRuntimeBridge` 承担设备侧的共享同步职责：

1. 监听 `currentDeviceOpenStateChanged`
2. 监听 `deviceRealTimeStatusUpdated`
3. 在 `openState == false` 时更新 `CommonDeviceProfile` feature specs 并清空运行时 property
4. 在 `openState == true` 时更新 `DeviceInfo` property
5. 更新 `DeviceRealTimeStatus` property
6. 发出共享的 `deviceConnected` 语义

这意味着“设备是否切换、是否打开、property 是否写回”已经不再依赖 `MainWindowDeviceController`。

### 3. 业务管理与可选 UI host 层

当前已经收口为 `BusinessManager` 单 owner：

1. `BusinessManager`
   - 负责 business 注册、active state、业务切换主线
   - 已不再要求 `FancyTabWidget` 在构造前存在
   - public host 边界已经收口到 `IBusinessEntryHost`
   - 当前只保留 `requestSelectBusiness(...)` 这条真实使用的业务选择请求入口
   - 同时承担当前激活 `IBusinessEntryHost` 的 owner 与 entry facade
   - 对外通过 `hasEntryHost()`、`selectedEntryBusiness()`、`currentEntryBusiness()`、`setEntryBusinessSelected()`、`setEntryBusinessesVisible()`、`firstVisibleEntryBusiness()`、`setCurrentEntryBusiness()` 和 `entryHostChanged` 暴露入口宿主语义

当前 `MainWindow` 会在创建 `FancyTabWidget` 后，把它 attach 给 `BusinessManager`；`FancyTabWidget` 只是 `IBusinessEntryHost` 的一个实现。没有 entry host 时，这一层自然降级为 no-op。

### entry 语义补充

这里的 `entry` 不是某个 business 本体，也不是当前真正处于发射执行态的 business；它指的是“当前 UI host 暴露给用户的 business 入口状态”。

对主窗口来说，entry 是 `FancyTabWidget` 里的列表项 + 对应 page；对 minibar 来说，entry 是 `MiniBarBusinessMenuHost` 管理的 provider menu action + `Mod` 按钮副标题。

当前这层语义只回答下面几件事：

1. 哪些 business 已注册到当前 host，顺序是什么。
2. 哪些 entry 当前 visible。
3. 哪个 entry 当前是 current，也就是当前显示页 / 当前按钮文案对应的业务。
4. 哪个 entry 当前是 selected，也就是当前 host 记录的用户选择。

它明确不直接回答下面这些问题：

1. 谁是 `BusinessManager::activedBusiness()`。
2. `TxSessionService` 当前是否已经把这个 business 当成 provider execution context。
3. 某个 business 是否已经在 minibar 下接好了 popup host 侧的最小适配。

这也是为什么当前代码里会同时存在两条状态线：

1. `BusinessManager` 一边维护 `activedBusiness()` 这条业务生命周期主线，一边维护当前激活 `IBusinessEntryHost` 的 facade。
2. `FancyTabWidget` 自己区分 `selectedBusiness()` 和 `currentBusiness()`；`MainWindow` 再把 `selectedBusiness()` 注入 `TxSessionService`。
3. 已删除的 `MiniBarBusinessMenuHost` 曾验证同一接口可承载非 tab 入口；当前 helper
   不 attach 第二个 entry host，而是由 `RemoteMinibarService` 把过滤后的 business
   snapshot 和用户 intent 映射回 main 的唯一 owner。

### 4. UI host 层

当前 main/helper 的定位已经收口：

1. `MainWindow` 是 main 进程唯一 UI host，并 attach `FancyTabWidget` entry host。
2. `MainWindow` 向 `TxSessionService` 注入 selected business / sweep context。
3. `RemoteMinibarService` 把权威 snapshot 和 typed intent 连接到 helper；helper 不
   链接 Core plugin，也不复制 runtime owner。
4. `RemoteMiniBarWindow` 已承载 `RF / Center / Level / Sweep / MOD` 交互。

## 当前对插件的接线规则

后续插件若要兼容 minibar，推荐按下面的规则接线：

1. 设备就绪 / 设备参数同步：连接 `DeviceRuntimeBridge`
2. 业务列表显隐、选择、回退：连接 `BusinessManager` 的 entry facade / `entryHostChanged`
3. 对话框 parent widget：使用 `Utils::GUIContext::instance()->getMainWindow()`
4. 不再把 `MainWindow::instance()` / `FancyTabWidget::instance()` 当作后台服务入口
5. 若现有 analog panel 需要进入 minibar 或其它轻量 host，优先做 host 侧的最小适配，而不是复制一套迷你业务逻辑，也不要重新扩散 panel-level compact 接口

## 已完成的插件样本

### Analog

`AnalogModulationPlugin` 当前已经完成两类迁移：

1. 设备参数同步从 `MainWindow::deviceConnected` 改为 `DeviceRuntimeBridge::deviceConnected`
2. 许可证业务列表显隐从 `FancyTabWidget::instance()` 改为 `BusinessManager` 的 entry facade

这说明“插件后台逻辑连接共享 runtime signal，可选 UI 行为通过 `BusinessManager` 的 entry facade 暴露”这一模式已经可行。

### GPS

`GPS` 插件当前已经把 `GpsInfoDialog` 的 parent 获取改为 `Utils::GUIContext::instance()->getMainWindow()`，不再直接依赖 `MainWindow::instance()`。

## 对 minibar 模式的直接意义

当前判断是：minibar 可用性的主要阻塞已经不再是 core 启动链；共享 runtime 与最小发射闭环已经成立，剩余问题主要是更完整的业务 UI 面是否继续下沉。

换句话说，当前阶段真正关键的是：

1. 共享运行时启动收口是否成立
2. 设备运行时桥接是否成立
3. `TxSessionService` 是否成为唯一 apply owner
4. 插件 UI 是否改为连接共享 signal / entry facade

`MainWindow` 全部 controller 的进一步统一抽象，并不是 minibar 可用的前置条件。

## 历史 in-process 验证记录

截至 2026-05-08，以下链路已经在 Debug 运行时完成真实验证或联机确认：

1. `CorePlugin` 在 `--ui-mode minibar` 下不会实例化 `MainWindow`，但 `CoreRuntimeServices`、`DeviceManager`、`BusinessManager`、`TxSessionService` 仍会完整启动。
2. `RF` 按钮已经改为共享 property 驱动：
   - 点击后写回 `RF` property
   - 触发 `TxSessionService::requestRefresh()`
   - 已验证日志中进入 `FixedCw`，再次关闭后回到 `Mute`
3. `Frequency` / `Level` 已通过共享 property 与软键盘打通：
   - `PropertyBindingManager::bindButtonToProperty(...)`
   - `prepareNumericKeyBoard(...)`
   - 已验证提交 `2GHz` / `10dBm` 后，`TxPipelineRuntime` 日志中的 `center` / `level` 跟随变化
4. minibar host 自己的窗口协议已补齐 popup ownership：
   - 当 provider menu 或 `TouchNumKeyboard` popup 打开时，outside-click collapse 逻辑会把这些 popup descendant 视为 owned area
   - 避免“键盘一弹出就把 minibar 折叠掉”
5. `MOD / provider` 菜单已切到 business-backed entry host：
   - `MiniBarBusinessMenuHost` 已通过 `BusinessManager::attachBusinessEntryHost(...)` 接入 minibar
   - menu item 来源于 business 注册结果，而不是本地硬编码字符串
   - 根据 2026-05-08 联机测试，menu list 已能随插件注册结果与 analog license 结果动态更新
6. `Mod` 按钮副标题已由当前 entry/fallback 驱动：
   - `currentBusinessText()` 优先反映当前 current entry
   - `m_selectedProvider` 只保留为本地恢复缓存，不再充当真实语义 owner

## 保留下来的 compact 设计边界

4. 仓库当前唯一保留下来的 compact 语义，只剩 `SwitchButton::compactMode` 这个基础控件级能力。

legacy in-process minibar 当时的做法如下，现只作为“host 侧局部适配”设计参考：

1. `MiniBarWindow` 通过 popup 承载 `StepSweepPanel`，直接复用同一套 sweep 参数编辑逻辑，不再单独做 compact sweep panel。
2. business popup 若需要收紧顶部开关，只在 host 侧通过一个局部 helper 遍历 hosted panel 里的 `SwitchButton`，调用 `setCompactMode(true)`。
3. 能纯靠 QSS 表达的按钮最小宽度、padding、label margin，继续放在主题样式；QSS 做不了的内部 layout margin 收紧和显式 repolish，保留在 `SwitchButton::setCompactMode()`。
4. 这条路径说明：后续若要把其它 analog panel 下沉到 minibar 或其它轻量 host，优先继续走 host 侧最小适配，而不是恢复 panel-level compact 或复制一份“迷你业务实现”。

## 历史尾项

当前仍有一个明确的生命周期尾项：

1. `BusinessManager::unregisterBusiness()` 还需要补齐，才能让 business 生命周期收口完全对称。

同时，`BusinessManager` 里只剩当前仍在使用的 `requestSelectBusiness(...)` 选择主线；旧的 page-only `selectBusinessPageRequested` 已在本轮清理删除。

这不影响当前 UI 无关运行时主线的成立，但属于后续应补的工程性收口项。

另外，minibar 当前虽然已经补齐 `MOD / provider` 的入口层，但仍保留两类明确尾项：

1. `MOD / provider` 当前只完成了 business-backed menu model、license 驱动显隐、current/fallback 与本地恢复；它还没有把 selected business 注入 `TxSessionService`。
2. `sweep` 已通过 normal `StepSweepPanel` 接入 minibar；business popup 如需局部收紧，只保留 host 侧 `SwitchButton` helper，不再恢复 panel-level compact。
