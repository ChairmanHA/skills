# UI 无关的公共服务启动重构方案

## 当前设计摘要（简版）

如果只关心当前已经收敛出的设计，而不关心每一刀的历史细节，可以直接看本节。

### 当前已经成立的结构

1. `CoreRuntimeServices` 已经成为共享运行时 owner，负责在 UI 分流之前统一初始化 property schema、`ActionManager`、`CommonDeviceProfile`、`DeviceManager`、`BusinessManager`、runtime bridges 和默认运行时状态。
2. `DeviceRuntimeBridge` 已把设备切换 / 打开 / 实时状态的主同步链从 `MainWindowDeviceController` 中抽离出来，负责维护 `CommonDeviceProfile`、`DeviceInfo`、`DeviceRealTimeStatus`，并对外发出共享的 `deviceConnected` 语义。
3. `BusinessManager` 已不再依赖 `FancyTabWidget` 先创建；`MainWindow` 只是在 UI 起来后 attach 自己的 business UI host。
4. `BusinessUiBridge` 负责暴露“可选 UI host”能力，供插件在存在 `FancyTabWidget` 时操作业务列表，不存在时自动 no-op。
5. `MainWindowDeviceController` 当前已经基本收缩为 UI-only；它不再承担后台同步主链，主要保留状态展示、菜单、弹窗和 warning UI。

### 当前设计意味着什么

1. 就运行时 bootstrap 而言，`MainWindow` 和 `MiniBarWindow` 的差异已经不再决定公共服务是否启动。
2. 当前阶段要让 minibar 真正可用，核心已经不是继续抽象 `MainWindow` 全部 controller，而是让各插件的 UI 代码停止依赖 `MainWindow::instance()` / `FancyTabWidget::instance()`，改为连接共享 runtime signal 和可选 UI bridge。
3. 以当前重构结果来看，真正的基础是：
   - 步骤 1：共享运行时启动收口到 `CoreRuntimeServices`
   - 步骤 3：设备状态同步抽离到 `DeviceRuntimeBridge`
4. 在这两个基础成立后，后续工作会更多表现为“插件 UI 改写与信号接线”，而不是继续为 core 启动链补洞。

### 当前建议的验证口径

1. 证书 / 许可证鉴权路径可以视为本轮重构的代表性 smoke test。
2. 这条路径同时覆盖了设备打开、`DeviceRuntimeBridge` 同步、设备参数刷新、Analog license gating、`BusinessUiBridge` 列表控制，以及“有 UI host / 无 UI host”两种分支。
3. 如果这条路径稳定，则基本可以说明本轮“服务先于 UI、插件改接共享信号”的主链没有被破坏。

以下内容保留分阶段分析与实现记录，供后续追溯。

## 目标

把 `DeviceManager`、`BusinessManager`、scanner/factory 注册承载点、设备状态同步等“公共运行时服务”从具体 UI 模式中解耦出来，使它们满足以下约束：

1. 是否走 `MainWindow`、`MiniBarWindow`，不影响公共服务是否创建。
2. 依赖 `Core` 的其他插件在 `initialize()` 阶段可以稳定注册 scanner、factory、business，而不依赖某个 UI 先构造完成。
3. UI 只负责“展示、交互、菜单、弹窗、页面导航”，不再承担“必须先 new 出窗口才能让后台服务活起来”的职责。
4. 后续新增第三种 UI 模式，或者做更轻量的 host / headless 启动时，不需要再次复制一份启动 bootstrap。

## 当前实现现状

### 1. 已经在 CorePlugin 中共享的部分

当前 `CorePlugin` 构造阶段已经承担了部分共享初始化：

1. 全局 property schema 的一大部分创建。
2. `ActionManager` 创建。
3. `CommonDeviceProfile` 创建。
4. 当前已经补进了 `SystemBusyStatus` 这类共享 property。

这部分位于：

1. `src/plugins/core/coreplugin.cpp`

这说明仓库已经部分接受了“公共状态不应依赖窗口”的方向，但还没有彻底收口。

### 2. MainWindow 仍然承担了大量运行时启动职责

当前 `MainWindow` 构造函数里不仅创建 UI，还创建或触发了多项公共服务与共享状态：

1. `new DeviceManager(this)`
2. `new MainWindowDeviceController(this, this)`，并立刻连接 `DeviceManager` 信号
3. `m_deviceController->initializeDeviceMenu()`
4. `m_fancyTabWidget = new FancyTabWidget`
5. `new BusinessManager(this)`
6. `m_deviceController->setupSystemBusyStatusBinding()`
7. `BusinessManager::muteBusiness()->active()`

这部分位于：

1. `src/plugins/core/mainwindow.cpp`

这里的问题不是“MainWindow 不该有控制器”，而是“公共服务创建、共享状态初始化、UI 控制器绑定”现在混在同一个构造函数里，导致窗口本身变成了运行时 bootstrap 的宿主。

### 3. MiniBarWindow 复制了一份精简 bootstrap

`MiniBarWindow::initializeGlobalBootstrap()` 当前又复制了一套较轻的启动链：

1. 条件创建 `DeviceManager`
2. 条件创建 `BusinessManager`
3. 创建 `MainWindowDeviceController(nullptr, this)`
4. 初始化 device menu
5. 创建 `MainWindowSettingsController(nullptr, this)`
6. 补默认 `mute` 业务激活

这部分位于：

1. `src/plugins/core/minibarwindow.cpp`

这说明当前已经出现了明显的“同一套公共服务，在两个 UI 分支里各自兜底创建”的结构性重复。

### 4. Device scanner / manual ETH factory 本身不依赖 UI，但依赖 DeviceManager 先就绪

以 HTRA 插件为例，其 `initialize()` 里会执行：

1. `Core::DeviceManager::registerScanner(m_scanner)`
2. `Core::DeviceManager::registerManualEthDeviceFactory(this)`
3. `m_scanner->start()`

同时还会向 `BusinessManager` 注册 business。

这部分位于：

1. `src/plugins/htra/plugin.cpp`
2. `src/plugins/htra/htra.json`

因为 `HTRA` 的 plugin metadata 依赖 `Core`，所以只要 `Core` 在自己的 `initialize()` 阶段先把共享 manager 准备好，scanner/factory 注册就天然可以变成 UI 无关。

### 5. 一部分“服务类”自身仍然带着 UI 假设

这才是需要特别处理的点。

#### 5.1 BusinessManager 当前在构造时捕获 FancyTabWidget 单例

`BusinessManager` 构造函数里会：

1. `m_widget = Internal::FancyTabWidget::instance()`
2. 在 `selectBusinessPageRequested` / `selectBusinessRequested` 中直接操作 `m_widget`
3. `registerBusiness()` 时如果 `m_widget` 存在就立刻 `m_widget->addBusiness(business)`

这部分位于：

1. `src/plugins/core/businessmanager.cpp`

这意味着：

1. `BusinessManager` 不是纯后台服务，它在内部缓存了 UI host。
2. 如果把 `BusinessManager` 提前到 UI 创建之前，`m_widget` 很可能在构造时是空的。
3. 即使后续 `FancyTabWidget` 被创建，当前实现也没有显式 attach / replay 机制把已注册 business 回灌到 UI。

这是当前最关键的“服务依赖 UI 实例化”的案例之一。

#### 5.2 MainWindowDeviceController 混合了运行时同步和 UI 展示职责

`MainWindowDeviceController` 当前同时承担两类职责：

1. 运行时同步职责
   - 监听 `DeviceManager::currentDeviceChanged`
   - 监听 `currentDeviceOpenStateChanged`
   - 监听 `deviceRealTimeStatusUpdated`
   - 更新 `CommonDeviceProfile` feature specs
   - 更新 `DeviceInfo` / `DeviceRealTimeStatus` property
2. UI 职责
   - 拥有 `DeviceInfoWidget`
   - 维护 USB/ETH 连接菜单
   - 弹设备错误对话框
   - 把 `SystemBusyStatus` 绑定为 warning 文案展示

这部分位于：

1. `src/plugins/core/mainwindowdevicecontroller.cpp`

这类混合职责会导致一个现实问题：如果没有先构造某个 UI 控制器，设备状态同步就可能不完整；但如果为了后台同步去构造这个控制器，又会顺手带出 widget、dialog、menu 等 UI 语义。

#### 5.3 部分业务插件直接依赖 MainWindow / FancyTabWidget 单例

以 Analog 插件为例：

1. `initialize()` 里直接取 `Core::Internal::MainWindow::instance()`
2. 通过 `MainWindow::deviceConnected` 同步设备参数
3. 后续又直接取 `Core::Internal::FancyTabWidget::instance()` 做业务可见性控制

这部分位于：

1. `src/plugins/analog/analogmodulationplugin.cpp`

这说明问题不只是 manager 创建点不对，还有一层“插件自身把 UI singleton 当成服务入口” 的耦合。

### 6. 已经比较健康的部分

`ActionManager` 已经开始体现较合理的方向：

1. 优先找 `MainWindow::instance()`
2. 如果没有，则回退到 `Utils::GUIContext::instance()->getMainWindow()`

这部分位于：

1. `src/plugins/core/actionmanager.cpp`

这类模式值得保留：共享服务本身不要求某个具体窗口类存在，只要求存在“当前 GUI host”或者允许无 host 运行。

## 当前结构的核心问题

### 1. 公共服务创建点仍然跟 UI 分支绑定

虽然 `DeviceManager` / `BusinessManager` 本身不要求一定依赖 `MainWindow`，但它们的创建点目前分散在 `MainWindow` 和 `MiniBarWindow` 内部。这样一来：

1. 任何新 UI 模式都必须再抄一遍 bootstrap。
2. 某个分支少抄一步，就会出现“服务启动不完全”。
3. 问题表现会很隐蔽，因为 dependent plugin 仍然能加载，只是注册或信号链局部失效。

### 2. 服务对象内部缓存了 UI 单例，阻止其提前启动

最典型的是 `BusinessManager`。即使把它移动到 `CorePlugin` 提前创建，当前实现也会因为构造时抓不到 `FancyTabWidget::instance()` 而留下不完整状态。

### 3. 运行时同步职责和 UI 展示职责未分层

以设备侧为例，真正需要尽早启动的是：

1. scanner 注册与发现
2. 当前设备切换
3. 设备打开状态传播
4. `CommonDeviceProfile` / `DeviceInfo` / `DeviceRealTimeStatus` 更新

这些都属于运行时同步，不应该要求先有状态栏 widget 或弹窗控制器。

### 4. 插件把 UI singleton 当作公共服务入口

这会导致：

1. `MainWindow` 模式下功能看似正常。
2. `MiniBar` 模式下功能只能“部分工作”或静默降级。
3. 未来如果做 UI 热切换、延迟 attach、无主窗口启动，问题会持续放大。

### 5. 当前启动顺序是“隐式约定”，不是“显式契约”

仓库里现在靠的是“Core 先加载、MainWindow 先构造、某些插件再 initialize”这种事实顺序，而不是一个明确的 service bootstrap contract。这让后续改动非常容易产生回归。

## 重构目标架构

建议把启动结构明确分成四层。

### 第一层：共享运行时服务层

由 Core 统一创建并长期持有，完全独立于具体 UI 模式。

建议包含：

1. property schema 初始化
2. `ActionManager`
3. `CommonDeviceProfile`
4. `DeviceManager`
5. `BusinessManager`
6. 设备运行时同步桥接对象
7. 默认运行时状态初始化，例如默认 `mute` 激活

这层的 owner 应该是 `CorePlugin` 或 `CorePlugin` 持有的独立 bootstrap/service-host 对象，而不是 `MainWindow` 或 `MiniBarWindow`。

### 第二层：UI Host 层

只负责创建并展示某种 UI 模式。

例如：

1. `MainWindow`
2. `MiniBarWindow`
3. 未来可能的第三种 host

这一层只决定“展示形态”，不负责创建公共 manager。

### 第三层：UI Adapter / Controller 层

只在对应 UI 存在时才 attach。

例如：

1. device 菜单控制器
2. `DeviceInfoWidget` 展示控制器
3. `FancyTabWidget` 业务列表绑定器
4. 设置菜单/主题/语言控制器

这些 adapter 可以依赖共享服务层，但共享服务层不应该反过来依赖它们。

### 第四层：插件扩展提供者层

依赖 `Core` 的插件只通过共享服务层注册自己的能力：

1. scanner
2. manual ETH factory
3. business
4. 其他 provider

这层不应该默认拿 `MainWindow::instance()` 或 `FancyTabWidget::instance()` 作为运行时入口。

## 推荐的重构落点

## 方案总览

建议新增一个明确的共享启动对象，例如：

1. `CoreRuntimeServices`
2. 或 `CoreBootstrap`
3. 或 `CoreSharedServiceHost`

本文以下统一称为 `CoreRuntimeServices`。

它由 `CorePlugin` 持有，职责如下：

1. `initializePropertySchema()`
2. `initializeSharedManagers()`
3. `initializeRuntimeBridges()`
4. `initializeDefaultRuntimeState()`

### 1. 把共享 manager 的创建统一收口到 CorePlugin

第一步不要急着做大拆分，先把创建点统一收口。

建议改法：

1. 在 `CorePlugin::initialize()` 的最前面调用 `CoreRuntimeServices::initializeOnce()`。
2. 该步骤内统一保证以下对象存在：
   - `ActionManager`
   - `CommonDeviceProfile`
   - `DeviceManager`
   - `BusinessManager`
   - 共享 property schema
3. 之后再分流创建 `MainWindow` 或 `MiniBarWindow`。
4. 从 `MainWindow`、`MiniBarWindow` 中删除 `new DeviceManager` / `new BusinessManager` 这类共享 manager 创建代码。

这样可以先解决“manager 创建点依赖 UI 模式”的根问题。

### 2. 把默认运行时状态从窗口中拿出来

当前默认 `mute` 激活散落在窗口路径中。这个动作应归入共享运行时基线。

建议改法：

1. 在 `CoreRuntimeServices::initializeDefaultRuntimeState()` 中统一执行默认 `mute` 激活。
2. 保持逻辑为“仅当当前没有 active business 时才补默认状态”。
3. 从 `MainWindow` 和 `MiniBarWindow` 中删掉默认 `mute` 激活动作。

### 3. 抽出 Device 运行时桥接层，避免 MainWindowDeviceController 同时承担后台同步和 UI 展示

建议新增一个纯运行时对象，例如：

1. `DeviceRuntimeBridge`
2. 或 `CoreDeviceSessionBridge`

职责只包含：

1. 监听 `DeviceManager` 的设备切换/打开/实时状态信号
2. 更新 `CommonDeviceProfile` feature specs
3. 更新 `DeviceInfo` property
4. 更新 `DeviceRealTimeStatus` property
5. 对外发出一个 UI 无关的设备已就绪信号，例如 `deviceReady(const QSharedPointer<Core::DeviceInfo> &)`

而 `MainWindowDeviceController` 收缩为 UI-only 控制器：

1. `DeviceInfoWidget` 展示
2. USB/ETH Connect 菜单
3. 设备错误对话框
4. `SystemBusyStatus` 到 warning UI 的绑定

这样即使没有任何主窗口，设备发现和状态同步仍然是完整的。

### 4. 重构 BusinessManager，使其不在构造时抓取 FancyTabWidget

这是第二个核心点。

建议最小可行改法不是马上引入复杂接口，而是先把 UI host attach 显式化：

1. `BusinessManager` 内部不再在构造函数里缓存 `FancyTabWidget::instance()`。
2. 新增显式方法，例如：
   - `attachBusinessUiHost(FancyTabWidget *widget)`
   - `detachBusinessUiHost()`
   - `replayBusinessesToUiHost()`
3. `registerBusiness()` 永远先只修改内部 registry。
4. 如果当前存在 UI host，则同步把 business 加到 UI。
5. 如果 UI host 在 business 注册之后才 attach，则 attach 时重放已有业务列表。

这样有几个直接收益：

1. `BusinessManager` 可以在 UI 之前稳定启动。
2. `MainWindow` 模式下仍然能看到完整 business 列表。
3. `MiniBar` 模式下可以不 attach `FancyTabWidget`，而不影响后台 business 注册完整性。

### 5. 把插件对 MainWindow/FancyTabWidget 的直接依赖迁移到运行时信号或可选 UI facade

这一步是把“manager 已经 UI 无关”推进到“dependent plugin 也不再把 UI singleton 当服务入口”。

先处理会影响服务完整性的路径。

#### 5.1 Analog 插件

当前 Analog 插件有两类依赖：

1. 通过 `MainWindow::deviceConnected` 同步设备参数
2. 通过 `FancyTabWidget::instance()` 控制业务列表可见性

建议改法：

1. 设备参数同步改监听 `DeviceRuntimeBridge::deviceReady(...)` 或 `DeviceManager::currentDeviceOpenStateChanged + DeviceInfo property`。
2. 业务列表可见性控制不要直接抓 `FancyTabWidget::instance()`，而是：
   - 要么由 `BusinessManager` 新增“business visibility policy”接口
   - 要么由 `MainWindow` attach 一个单独的 UI facade，再把这类 UI-only 能力暴露为可选服务

#### 5.2 GPS 等只需要父窗口的插件

这类不涉及后台服务完整性，但应顺手消除对 `MainWindow::instance()` 的硬绑定。

建议改法：

1. 需要 parent widget 时，优先走 `Utils::GUIContext::instance()->getMainWindow()`。
2. 允许 parent 为空时优雅降级。

### 6. 把 UI 模式切换收敛为“只选 host，不选服务”

最终启动顺序建议为：

1. `CorePlugin::initialize()`
2. `CoreRuntimeServices::initializeOnce()`
3. 根据 `--ui-mode` 选择创建 `MainWindow` 或 `MiniBarWindow`
4. UI host 在构造期间 attach 自己需要的 UI controller / UI bridge
5. 依赖 `Core` 的插件在其 `initialize()` 中注册 scanner、factory、business
6. 如果 UI host 晚于某些注册发生，依靠 attach/replay 机制补齐 UI 表现

这时 UI 模式差异只影响：

1. 展示方式
2. 交互方式
3. 哪些 UI adapter 被 attach

而不会再影响：

1. manager 是否存在
2. scanner 是否能注册
3. business 是否能注册
4. 设备状态 property 是否完整更新

## 分阶段实施步骤

建议分五个阶段做，避免一次性大改。

### 阶段 A：共享启动收口，不改行为语义

目标：先把 manager 创建点和默认状态初始化从窗口里拿出来。

步骤：

1. 新增 `CoreRuntimeServices` 文件并接入 `src/plugins/core/CMakeLists.txt`。
2. 把 `CorePlugin` 当前构造阶段的 property / `ActionManager` / `CommonDeviceProfile` 初始化迁到 `CoreRuntimeServices`。
3. 在 `CorePlugin::initialize()` 最前面调用 `initializeOnce()`。
4. 把 `DeviceManager`、`BusinessManager` 创建迁入 `CoreRuntimeServices`。
5. 把默认 `mute` 激活迁入 `CoreRuntimeServices`。
6. 删除 `MainWindow`、`MiniBarWindow` 中重复的 manager 创建。

阶段 A 完成后，至少应保证：

1. 两种 UI 模式都通过同一份 shared bootstrap 起服务。
2. 不再存在“窗口里各自兜底 new manager”的重复代码。

### 阶段 B：抽离 Device 运行时桥接

目标：让设备发现、状态同步、property 写回不再依赖 UI controller。

步骤：

1. 新增 `DeviceRuntimeBridge`。
2. 从 `MainWindowDeviceController` 迁出对 `CommonDeviceProfile`、`DeviceInfo`、`DeviceRealTimeStatus` 的同步逻辑。
3. `MainWindowDeviceController` 保留菜单、弹窗、widget 展示。
4. 如果 minibar 不需要这些 UI 组件，则可完全不创建 `MainWindowDeviceController`，或只创建更轻的 device menu adapter。

阶段 B 完成后，设备侧共享运行时就已经基本与 UI 解耦。

### 阶段 C：重构 BusinessManager 的 UI attach 机制

目标：让 `BusinessManager` 可以先启动，UI 之后再 attach。

步骤：

1. 移除构造时对 `FancyTabWidget::instance()` 的硬缓存。
2. 增加 attach/detach/replay 接口。
3. `MainWindow` 在创建 `FancyTabWidget` 后显式 attach。
4. 把 `selectBusinessRequested` / `selectBusinessPageRequested` 的处理收口到 attach 的 UI host。
5. 对无 UI host 场景做空实现或延迟处理，不允许因无 UI host 导致后台注册失败。

阶段 C 是这次重构真正的分水岭。没有这一层，`BusinessManager` 仍然只是“创建点提前了”，但内部语义仍然依赖 UI。

### 阶段 D：迁移插件侧的 UI singleton 依赖

目标：让 dependent plugin 不再把 `MainWindow` 当服务入口。

步骤：

1. 全仓审计 `MainWindow::instance()`、`FancyTabWidget::instance()` 的使用点。
2. 先迁移影响服务完整性的点：
   - Analog 设备参数同步
   - business 可见性门控
3. 再迁移只涉及父窗口获取的点，例如 GPS 对话框 parent。

阶段 D 完成后，UI 模式变化就不会再导致 dependent plugin 的后台逻辑不完整。

### 阶段 E：清理与验证

步骤：

1. 删除 `MiniBarWindow::initializeGlobalBootstrap()` 中已经被 shared bootstrap 吸收的逻辑。
2. 在关键路径补充 `qDebug()` / `qWarning()` / `Q_ASSERT`，确认 shared service 先于 dependent plugin 初始化。
3. 清理重复连接和重复 property 创建。

## 推荐的验证口径

### 1. 启动时序验证

需要确认：

1. `CoreRuntimeServices::initializeOnce()` 早于 HTRA / Analog 等依赖 `Core` 的插件 `initialize()`。
2. 两种 UI 模式下，`DeviceManager::instance()`、`BusinessManager::instance()` 在 dependent plugin initialize 时都已存在。

### 2. Device 侧验证

需要确认：

1. `HTRA` scanner 在 `MainWindow` 和 `MiniBar` 两种模式下都能注册并启动。
2. manual ETH factory 在两种模式下都能注册。
3. 打开设备后，`DeviceInfo`、`DeviceRealTimeStatus`、`CommonDeviceProfile` 在无 `MainWindow` 前提下仍可完整更新。

### 3. Business 侧验证

需要确认：

1. `BusinessManager` 在 UI attach 前后都能正确接受 business 注册。
2. `MainWindow` 模式下 business 列表能够完整回放到 `FancyTabWidget`。
3. `MiniBar` 模式下即使没有 `FancyTabWidget`，business 注册和默认 active state 仍然完整。

### 4. 插件侧验证

需要确认：

1. Analog 插件在 `MiniBar` 模式下不再因为 `MainWindow::instance()` 为空而遗漏设备参数同步。
2. 依赖 UI parent 的对话框路径在无 `MainWindow` 直接实例时能优雅退化或改走 `GUIContext`。

## 本次重构建议的边界

### 本次建议纳入

1. shared manager 启动收口
2. device runtime bridge 抽离
3. `BusinessManager` 的 UI attach 化
4. dependent plugin 中会影响服务完整性的 UI singleton 迁移

### 本次建议暂不纳入

1. `TxOrchestrator` / `TxPipelineRuntime` 完整下沉为 UI 无关服务
2. MainWindow 全部 controller 的抽象统一
3. 所有 UI plugin API 的彻底重写

原因是这三项会显著扩大范围，而当前核心目标只是先把“公共服务是否完整启动”与“UI 是否先实例化”解耦。

补充判断：

1. 就当前阶段而言，`MainWindow` 全部 controller 的统一抽象并不是 minibar 可用性的前置条件。
2. 当前更关键的是共享运行时收口是否成立、设备运行时桥接是否成立，以及插件 UI 是否改为连接这些共享信号与可选 UI bridge。

## 结论

当前仓库里真正的问题不只是“`DeviceManager` / `BusinessManager` 创建点在窗口里”，而是存在三层耦合：

1. 公共 manager 的创建点耦合在 UI host 中。
2. 某些服务类内部直接缓存 UI singleton。
3. dependent plugin 也把 UI singleton 当成服务入口。

因此重构不能只做“把 `new DeviceManager` 挪到 `CorePlugin`”这么浅的一步。推荐路径是：

1. 先把共享启动统一收口到 `CorePlugin` 持有的 `CoreRuntimeServices`。
2. 再把 device 同步桥和 business UI attach 机制拆开。
3. 最后迁移 dependent plugin 的 UI singleton 依赖。

这样才能真正保证：UI 只是 UI，服务就是服务；启动哪种 UI，不会再决定后台服务是否完整启动。

## 2026-05-06 第一实现切片：BusinessManager 与 FancyTabWidget 解耦

本轮先只做最关键、范围最小的一刀，不同时推进 shared manager owner 下沉。

### 目标

1. `BusinessManager` 不再在构造时抓取 `FancyTabWidget::instance()`。
2. `MainWindow` 模式下，`FancyTabWidget` 创建后显式 attach 到 `BusinessManager`。
3. `MiniBar` 模式下，不 attach 任何 `FancyTabWidget`，但 `BusinessManager` 仍可正常初始化并接受插件注册的 business。
4. 保持 `MainWindow` 模式下插件 business 列表行为不变，包括 analog 插件无许可证时业务仍从列表中隐藏。

### 约束

1. 只需要新增：
   - `attachBusinessUiHost(FancyTabWidget *widget)`
   - `detachBusinessUiHost()`
2. attach 时必须支持“UI 晚于 manager 创建”的回放。
3. 回放对象只能是“通过 `BusinessManager::registerBusiness()` 注册的业务”。
4. 不能把 `MuteBusiness`、`ContinuesWaveBusiness` 这类 built-in business 错误重放到 `FancyTabWidget` 列表里，因为当前主界面并不会把它们显示为普通业务入口。

### 预计改动点

1. `src/plugins/core/businessmanager.h`
2. `src/plugins/core/businessmanager.cpp`
3. `src/plugins/core/businessmanager_p.h`
4. `src/plugins/core/mainwindow.cpp`

### 切片后的预期

1. `MainWindow` 模式：
   - `BusinessManager` attach 到 `FancyTabWidget`
   - 插件 `initialize()` 中注册的 business 正常进入 UI 列表
   - analog 插件继续通过 `FancyTabWidget::instance()` 做许可证可见性控制
2. `MiniBar` 模式：
   - `BusinessManager` 不持有任何 `FancyTabWidget`
   - 插件 business 仍注册到 manager 内部
   - 不因为缺少 `FancyTabWidget` 而导致启动期 business 注册失败

### 当前验证结果

1. `MainWindow` 模式下，`FancyTabWidget` 的 list UI 状态与改动前保持一致，说明 attach/replay 主链已成立。
2. `MainWindow` 模式下，analog 插件无许可证时业务隐藏行为已按预期通过验证。
3. `MiniBar` 模式下，`BusinessManager` 可正常初始化且未绑定 `FancyTabWidget`，启动链通过验证。

## 2026-05-06 第二实现切片：共享 manager 创建收口与默认运行时状态上移

本轮落实上文第 1、2 项，目标是在不改变 `MainWindow` 功能的前提下，把共享 manager 的 owner 从窗口内迁到 `CorePlugin` 持有的 shared bootstrap。

### 目标

1. 新增 `CoreRuntimeServices`，由 `CorePlugin` 持有。
2. `CoreRuntimeServices::initializeOnce()` 统一初始化：
   - 共享 property schema
   - `ActionManager`
   - `CommonDeviceProfile`
   - `DeviceManager`
   - `BusinessManager`
3. `CorePlugin::initialize()` 在创建 `MainWindow` / `MiniBarWindow` 之前先完成 shared bootstrap。
4. 默认 `mute` 激活从窗口构造函数迁移到 `CorePlugin` 共享启动路径中。
5. 删除 `MainWindow` / `MiniBarWindow` 内部对 `DeviceManager`、`BusinessManager` 和默认 `mute` 状态的重复创建或兜底逻辑。

### 风险点

1. `MainWindow` 之前是在构造阶段后半段才激活默认 `mute`，若上移过早，可能导致它错过 `currentActivedBusinessChanged` 的初始通知。
2. 为避免这一点，默认 `mute` 激活不放进 `initializeOnce()`，而是在 `CorePlugin::initialize()` 中完成窗口实例化和 `initialize()` 之后，再调用 `initializeDefaultRuntimeState()`。
3. 这样既保持 shared owner 收口，也保留 `MainWindow` 观察到初始 active business 变化的机会。

### 预计改动点

1. `src/plugins/core/coreruntimeservices.h`
2. `src/plugins/core/coreruntimeservices.cpp`
3. `src/plugins/core/coreplugin.h`
4. `src/plugins/core/coreplugin.cpp`
5. `src/plugins/core/actionmanager.h`
6. `src/plugins/core/mainwindow.cpp`
7. `src/plugins/core/minibarwindow.cpp`
8. `src/plugins/core/CMakeLists.txt`

### 切片后的预期

1. `MainWindow` 模式：
   - 共享 manager 在 `CorePlugin::initialize()` 前半段完成创建
   - `MainWindow` 只消费这些 manager，不再拥有它们
   - 默认业务状态仍然正确建立，现有功能保持不变
2. `MiniBar` 模式：
   - 不再在本地 bootstrap 中兜底创建 `DeviceManager` / `BusinessManager`
   - 直接复用 `CorePlugin` 持有的 shared runtime services

### 当前结果

1. `CoreRuntimeServices` 已接入 `CorePlugin`，并负责共享 property schema、`ActionManager`、`CommonDeviceProfile`、`DeviceManager`、`BusinessManager` 的初始化。
2. 默认 `mute` 激活已从 `MainWindow` / `MiniBarWindow` 内部迁移到 `CorePlugin::initialize()` 的共享启动路径中。
3. `MainWindow` 与 `MiniBarWindow` 内部对 `DeviceManager`、`BusinessManager` 和默认 `mute` 状态的重复创建已删除。
4. `MainWindow` 模式功能由人工验证通过；`Core` target 已完成 Debug 编译验证通过。

## 2026-05-06 第三实现切片：收缩 MainWindowDeviceController

本轮只做 device 侧的职责拆分，不同时推进 dependent plugin 迁移。

### 目标

1. 新增共享的 `DeviceRuntimeBridge`，由 `CoreRuntimeServices` 统一创建并初始化。
2. 把 `MainWindowDeviceController` 中下列后台同步职责迁入 bridge：
   - `currentDeviceChanged` 对 `CommonDeviceProfile` feature specs 的刷新
   - `DeviceInfo` property 写回
   - `currentDeviceOpenStateChanged` 对空状态和就绪状态的 property 同步
   - `deviceRealTimeStatusUpdated` 对 `DeviceRealTimeStatus` property 的写回
   - 对外发出兼容现有 `MainWindow::deviceConnected` 语义的共享信号
3. `MainWindowDeviceController` 只保留 UI-only 职责：
   - `DeviceInfoWidget` 展示
   - USB/ETH Connect 菜单
   - 设备错误对话框和 warning UI
   - `SystemBusyStatus` 到 warning 文案的绑定
4. `MainWindow` 改为直接监听 `DeviceRuntimeBridge`，继续向外转发 `deviceConnected`，保持 Analog 等现有插件兼容。

### 边界

1. 本轮不改 `MainWindow::deviceConnected` 的对外接口。
2. 本轮不改 Analog 插件等 dependent plugin 的连接方式。
3. 本轮不把 `MainWindowDeviceController` 从 `MiniBarWindow` 中移除，只收缩其职责。
4. 本轮不做编译验证，由后续人工编译运行验证行为。

### 预计改动点

1. `src/plugins/core/deviceruntimebridge.h`
2. `src/plugins/core/deviceruntimebridge.cpp`
3. `src/plugins/core/coreruntimeservices.h`
4. `src/plugins/core/coreruntimeservices.cpp`
5. `src/plugins/core/mainwindow.cpp`
6. `src/plugins/core/mainwindowdevicecontroller.h`
7. `src/plugins/core/mainwindowdevicecontroller.cpp`
8. `src/plugins/core/CMakeLists.txt`

### 当前结果

1. `DeviceRuntimeBridge` 已接入 `CoreRuntimeServices`，并统一监听 `DeviceManager` 的设备切换、打开状态和实时状态信号。
2. `CommonDeviceProfile`、`DeviceInfo` property、`DeviceRealTimeStatus` property 的后台同步已从 `MainWindowDeviceController` 迁出到 bridge。
3. `MainWindow` 已改为直接转发 `DeviceRuntimeBridge::deviceConnected`，对外仍保持原有 `MainWindow::deviceConnected` 接口。
4. `MainWindowDeviceController` 已收缩为 UI-only：保留 `DeviceInfoWidget`、错误/警告 UI、USB/ETH 菜单和 `SystemBusyStatus` 绑定。
5. 本轮未做编译；对新改 core 文件执行了静态错误检查，未发现代码级报错，剩余报错为仓库现有的 cpptools Qt includePath 噪声。

## 2026-05-06 第 4、5 步与阶段 C、D 完备性检查

本节只核对“是否已经做完”，不再扩大实现范围。

### 第 4 步 / 阶段 C：`BusinessManager` 的 UI attach 机制

结论：主目标已完成，但从生命周期收口角度看仍有一个尾项，当前状态应定义为“基本完成，未完全封口”。

已完成项：

1. `BusinessManager` 已不在构造时抓取 `FancyTabWidget::instance()`。
2. 已提供显式 `attachBusinessUiHost()` / `detachBusinessUiHost()`。
3. `MainWindow` 已在创建 `FancyTabWidget` 后执行 attach，并在析构时 detach。
4. attach 时会回放 `uiRegisteredBusinesses`，因此 UI 晚于 manager 创建的场景已成立。
5. `selectBusinessRequested` / `selectBusinessPageRequested` 在无 UI host 时已安全 no-op，不会阻塞后台 business 注册。

未完全封口项：

1. `BusinessManager::unregisterBusiness()` 仍为 `TODO`，而 `IBusiness` 析构路径已经会调用它；这意味着当前 attach/replay 主链虽然可用，但 business 生命周期的移除分支尚未补齐。
2. 当前 UI host 仍然直接建模为 `FancyTabWidget *`，功能上已满足本阶段目标，但还不是更抽象的 UI facade。

结论口径：

1. 若按本轮计划的最小目标定义，第 4 步 / 阶段 C 已达到可交付状态。
2. 若按“生命周期也必须对称闭环”的标准定义，则还差 `unregisterBusiness()` 收口这一小项。

### 第 5 步 / 阶段 D：迁移 dependent plugin 的 UI singleton 依赖

结论：对本轮明确纳入的 `Analog` / `GPS` 而言，阶段 D 已完成；如果按“全仓所有插件 UI API 全部重写”来定义，则这一步仍然不是终点。

当前判断：

1. `AnalogModulationPlugin` 已不再直接依赖 `MainWindow::instance()` 或 `FancyTabWidget::instance()`。
2. `GPS` 插件已不再直接依赖 `MainWindow::instance()` 作为对话框 parent。
3. 因此，作为“dependent plugin 不再把 UI singleton 当服务入口”的首批关键样本，这一阶段已经验证成立。

剩余工作已从“修 core 启动链”转为“按同样模式继续改其他插件 UI 层”：

1. 设备就绪类依赖应改接 `DeviceRuntimeBridge`。
2. 业务列表操作类依赖应改接可选 UI bridge。
3. 纯 parent widget 获取应改走 `Utils::GUIContext`。

## 2026-05-06 第四实现切片：迁移 Analog / GPS 的 UI singleton 依赖

本轮只处理已经明确列出的两个 dependent plugin：`Analog` 和 `GPS`。

### 目标

1. `AnalogModulationPlugin` 不再直接依赖 `MainWindow::instance()` 作为设备就绪信号入口。
2. `AnalogModulationPlugin` 不再直接依赖 `FancyTabWidget::instance()` 操作业务列表可见性。
3. `GPS` 插件不再直接依赖 `MainWindow::instance()` 作为 `GpsInfoDialog` 的 parent。
4. `MainWindow` 模式下保持现有 Analog 许可证门控行为；`MiniBar` 模式下没有 UI host 时自然 no-op，不影响后台逻辑。

### 最小实现方案

1. 设备就绪信号统一改由 `DeviceRuntimeBridge` 提供，`AnalogModulationPlugin` 直接监听 bridge。
2. 新增一个 Core 侧的薄 `BusinessUiBridge`，只暴露 Analog 当前需要的 UI 能力：
   - attach / detach `FancyTabWidget`
   - 查询 `selectedBusiness()` / `currentWidgetBusiness()`
   - `setBusinessSelected()`
   - `setBusinessesVisibleInList()`
   - `firstVisibleBusiness()`
   - `setCurrentWidgetByBusiness()`
   - `uiHostChanged` 通知
3. `MainWindow` 在创建 / 销毁 `FancyTabWidget` 时同步 attach / detach `BusinessUiBridge`。
4. `GPS` 插件改为通过 `Utils::GUIContext::instance()->getMainWindow()` 获取 parent widget。

### 边界

1. 本轮不迁移所有 `MainWindow::instance()` / `FancyTabWidget::instance()` 使用点，只处理 `Analog` 和 `GPS`。
2. 本轮不重写 Analog 的 license policy，只替换其服务入口与 UI host 获取方式。
3. 本轮不做编译验证，由后续人工编译运行确认 MainWindow / MiniBar 行为。

### 当前结果

1. `Core` 新增了 `BusinessUiBridge`，由 `CoreRuntimeServices` 统一创建，并由 `MainWindow` 在 `FancyTabWidget` 创建/销毁时 attach / detach。
2. `AnalogModulationPlugin` 的设备参数同步已从 `MainWindow::deviceConnected` 迁移到 `DeviceRuntimeBridge::deviceConnected`。
3. `AnalogModulationPlugin` 的许可证业务列表可见性控制已从 `FancyTabWidget::instance()` 迁移到 `BusinessUiBridge`。
4. `GPS` 插件已改为通过 `Utils::GUIContext::instance()->getMainWindow()` 获取 `GpsInfoDialog` 的 parent。
5. 已重新搜索 `src/plugins/analog/**` 与 `src/plugins/gps/**`，`MainWindow::instance()` / `FancyTabWidget::instance()` 的直接依赖均已清零。
6. 本轮未做编译；对新增和改动的 core / analog / gps 文件执行了静态错误检查，未发现代码级报错，剩余问题仍为仓库现有的 cpptools Qt includePath 噪声。
7. 用户已对证书 / 许可证鉴权路径完成验证；该路径覆盖设备打开、设备参数同步、Analog 鉴权与业务列表门控，可作为本轮重构的代表性回归检查。