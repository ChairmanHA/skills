# Minibar MOD / Business 菜单两阶段实施计划

## 结论

当前把 minibar 的 `MOD / provider` 接入拆成两步是合理的，而且现有实现已经证明这个顺序是对的：

1. 第一大步已经完成：`business 注册 -> minibar 菜单模型 -> license 驱动动态更新`
2. 下一步应进入第二大步的前置收口：先明确 `entry selected/current` 与真正 apply provider 的关系，再打通 `点击菜单项 -> 打开对应 compact panel`

原因很直接：

1. `MiniBarBusinessMenuHost` 已经替换硬编码 provider menu，并且 2026-05-08 联机测试表明 menu list 能随插件注册与 analog license 结果动态更新。
2. analog 许可证显隐当前直接走 `BusinessManager` 的 entry facade，不需要 minibar 再复制一份 license 规则。
3. 当前剩余风险已经从“菜单从哪里来”收敛为“entry 选择是否参与发射”和“某个菜单项如何映射到 compact panel”。

因此，当前文档的重点不再是论证 phase 1 值不值得做，而是明确：phase 1/2 已经完成到什么边界，phase 3/4 下一步应该如何接着做。

## 当前代码判断

### 1. Minibar 当前状态

`MiniBarWindow` 当前已经完成入口层接线：

1. `buildUi()` 中创建 `m_providerMenu`，并由 `MiniBarBusinessMenuHost` 托管。
2. menu item 不再硬编码，而是来自 business registration，且按 `providerKind() != None` 过滤。
3. `onModProviderButtonClicked()` 现在只负责弹出动态 `QMenu`。
4. selected/current、fallback、按钮副标题与本地恢复状态已经收口到 `MiniBarBusinessMenuHost + m_selectedProvider`。
5. 当前仍未接 compact panel，也还没有把 selected business 注入 `TxSessionService`。

这说明 minibar 的 `MOD` 已经从本地占位按钮升级为真实业务入口，但当前仍停留在“入口层”。

### 2. MainWindow / FancyTabWidget 当前状态

主窗口侧已经有可复用的 business UI 规则：

1. `BusinessManager::attachBusinessEntryHost(IBusinessEntryHost *)` 与 `registerBusiness()` 已能把新 business 追加到抽象 UI host
2. `FancyTabWidget` 作为当前 main-mode host 实现，通过 `addBusiness()` / `rebuildListWidget()` 把注册顺序转成真实列表 item
3. `FancyTabWidget::setBusinessesVisibleInList()` 已支持“不改变注册顺序、只改变可见性”的收口语义

这部分已经不是猜想，而是现成能力。

### 3. Analog license 当前状态

analog 已经有一套成熟的“设备打开后，根据许可证动态更新业务列表”的逻辑：

1. `AnalogModulationPlugin` 持有 `m_licenseControlledBusinesses`
2. 监听 `DeviceParamsManager::licenseValidationStateChanged`
3. 在 `Licensed / Unlicensed` 落定后，通过 `BusinessManager::setEntryBusinessesVisible(...)` 更新当前激活的 entry host
4. 当当前页会被隐藏时，只做 page fallback，不自动启用别的 provider

这条链说明 minibar 第一阶段最应该复用的是“business 可见性规则”和“license 三态时机”，而不是复制一份 analog 私有判断。

### 4. 当前架构基线（2026-05-08 更新）

在继续做 minibar 之前，主模式这侧已经完成了两层重要收口：

1. `BusinessUiBridge` 与旧 entry bridge 已删除，当前共享 UI 入口抽象是 `IBusinessEntryHost + BusinessManager(entry facade)`
2. `BusinessManager` 的 public host 边界已经改为 `IBusinessEntryHost`，不再泄漏 `FancyTabWidget *`
3. `FancyTabWidget` 已退回 main-mode 内部 host 实现，而不是外部 plugin 依赖的 exported 类型

因此，minibar 下一步最自然的落点不是再造一层并行 bridge，而是补一个 menu-backed host，让它直接复用当前这套 host 语义。

## 实施原则

### 1. 菜单来源必须从硬编码切到 business 注册结果

minibar 的菜单项不能继续由 `MiniBarWindow` 写死字符串，而应来自共享的 business 注册表。

### 2. visibility 与 enabled 必须分离

第一阶段要沿用 FancyTabWidget 现有经验：

1. 某个 business 是否出现在菜单里，是 `visible` 语义
2. 某个 business 当前是否可交互，是 `enabled` 语义
3. 当前是否被用户选为 active/apply provider，是 `selected/current` 语义

这三者不能混在一个字段里。

### 3. license 更新只在结果落定时改菜单

对 analog license 要继续沿用已有三态原则：

1. `Unknown` 不等于失败
2. `Unknown` 阶段不应抖动菜单
3. 只有 `Licensed / Unlicensed` 最终结果才能驱动菜单显隐

### 4. 第一阶段不直接承诺“已能发射”

第一阶段的完成标准应是：

1. 菜单项能正确映射 business 注册结果
2. 菜单项能随 analog license 结果动态变化
3. 当前选中项、fallback 和按钮文案语义正确

不要把 compact panel 打开或 provider apply 绑进这个阶段，否则边界会重新混乱。

## 分阶段计划

## 阶段 0：收口模型与命名

目标：先把 `provider` 这个 UI 文案和内部真实对象关系定义清楚。

建议定义：

1. minibar 对外按钮仍可显示 `Mod`
2. 菜单项内部以 `IBusiness*` 或稳定 business key 为主标识
3. `m_selectedProvider` 这种纯文本状态要逐步退化为显示缓存，而不是真实语义 owner

本阶段输出：

1. 明确 menu item 的数据结构
2. 明确如何从 item 找回 `IBusiness*`
3. 明确按钮副标题显示规则，是取 `business->name()` 还是更适合的 compact title

## 阶段 1：给 minibar 建立 business-backed 菜单模型（已完成）

目标：把 `MiniBarWindow` 的硬编码 menu 改成基于 business 注册结果生成。

当前落地：

1. 在 core/minibar 层补一个薄的 menu-backed `IBusinessEntryHost` 实现，例如 `MiniBarBusinessMenuHost`，职责仅包括：
   - 从 `BusinessManager::attachBusinessEntryHost(...)` / `registerBusiness()` 接收业务注册结果
   - 维护 item 与 `IBusiness*` 的映射
   - 根据 visible/selected/current 状态刷新 `QMenu`
   - 把当前 business 文案回写到 minibar 按钮副标题
2. `MiniBarWindow` 已不再自己写死 `AM / FM / PM / Pulse`
3. `MiniBarWindow` 当前只持有并消费这个 host，负责弹菜单和显示当前副标题

建议不要直接让 `MiniBarWindow` 去扫 `BusinessManager::allBusiness` 并临时拼菜单；那样 phase 2 很快会把 host、selection、fallback 逻辑重新塞回窗口类里。

本阶段当前状态：

1. 新注册的 analog business 已能出现在 minibar 菜单中
2. 菜单顺序保持 business 注册顺序
3. 没有 UI host 时仍按 no-op 处理；minibar 自己就是 host/consumer

## 阶段 2：把 license 驱动接到 minibar 菜单可见性（已完成）

目标：让 analog 许可证像 MainWindow/FancyTabWidget 一样驱动 minibar 菜单项显隐。

这里当前已经按统一入口落地，不需要把 analog plugin 的 license 逻辑复制进 `MiniBarWindow`：

1. minibar mode 下 `MiniBarBusinessMenuHost` 已 attach 到 `BusinessManager`
2. analog plugin 当前继续只通过 `BusinessManager` 的 entry facade 更新 `selected/current/visible/fallback`
3. 因而 minibar 没有重新引入一套 host 外的并行 bridge

如果想尽量小改，第一版也可以先在 `MiniBarWindow` 内部监听 `DeviceParamsManager::licenseValidationStateChanged`，但要注意：

1. 这只能作为过渡方案
2. 许可证规则的 owner 仍应留在 analog plugin/runtime，而不是永久塞进 minibar

本阶段必须复用的产品规则：

1. `Unknown` 不改菜单
2. `Licensed` 显示受控 analog item
3. `Unlicensed` 隐藏受控 analog item
4. 如果当前选中项被隐藏，只做当前页/当前显示 fallback，不自动启用别的 provider

本阶段当前状态：

1. 插上设备并 open 后，analog 许可证结果已能驱动 minibar 菜单变化
2. `Unknown` 状态仍保持不改菜单的三态原则
3. 被隐藏项如果正好是当前显示项，按钮文案和当前引用已按 fallback 语义回退

## 阶段 3：补齐 minibar 自身的 current/selected 语义

目标：在不打开 compact panel 的前提下，先把“当前菜单选择到底表示什么”定义清楚。

建议拆成两个状态：

1. `currentBusiness`：当前按钮文案/当前菜单高亮项
2. `selectedBusiness`：真正参与当前发射上下文的业务

如果当前阶段还没有接入完整 panel，建议先让这两者临时保持一致，但实现上不要只留一个字符串字段。

本阶段需要明确：

1. minibar 启动时默认选哪个 business
2. 当前 business 因 license 被隐藏后，fallback 到谁
3. fallback 是否优先 `Playback / Streaming / Mute`，还是只在 analog 集合内部回退

这里建议优先沿用 analog 现有经验：

1. 只 fallback 当前显示页
2. 不自动启用另一个业务

## 阶段 4：为 compact panel 接入准备统一入口接口

目标：在真正弹 panel 前，先把“某个 business 对应哪个 compact widget”收口成接口。

建议不要在 `onProviderSelected()` 里写一堆 `if (name == "AM")`。应先定义诸如：

1. `MiniBarBusinessPanelProvider`
2. 或 `IBusinessCompactPresenter`

它至少回答两个问题：

1. 这个 business 在 minibar 下是否有 compact panel
2. 如果有，如何创建/复用该 panel widget

这样 phase 2 真正开始做 UI 时，就不会把名字判断和 widget 生命周期全堆进 `MiniBarWindow`。

## 阶段 5：逐个 business 接入 compact panel

这才是第二大步真正开始的位置。

建议顺序：

1. 先选一个已经适合 compact 化的 analog panel 做样板
2. 复用已有 panel，补 `setCompactMode(true)`，而不是复制迷你版实现
3. 每接一个 business，就补齐该 business 的 popup ownership、argsChanged、enabledChanged、关闭时状态保持

这里应复用 `StepSweepPanel` 已经验证过的模式：

1. minibar 做 popup host
2. 原 panel 保持业务语义 owner
3. compact mode 只改布局与局部样式，不改业务逻辑

## 阶段 6：把 compact panel 选择接到共享 runtime

当某个 business 的 compact panel 接进来后，再决定它如何影响共享发射链：

1. 是否需要写共享 property
2. 是否需要驱动 `TxSessionService::requestRefresh()`
3. 是否需要提供 provider execution context

这一步必须按 business 类型分开评估，不建议在第一轮计划里承诺“一次全接完”。

## 建议的最小实现顺序

如果目标是降低返工，我建议按下面的最小切片推进：

1. 已完成：把 minibar 硬编码 menu 改成 business-backed menu
2. 已完成：让 analog license 能动态增删这些 menu item
3. 已完成：把当前按钮副标题从纯字符串改成由当前 business 决定
4. 下一步：定义 compact presenter 接口，但先不给具体 business 接 panel
5. 下一步：选一个 analog business 做首个 compact panel 样板

这样做的好处是每一步都可验证，而且每一步都不会推翻前一步的数据结构。

## 下一步建议切片

如果要马上进入下一步实现，我建议直接做下面这个最小切片：

1. 显式定义 minibar 的 `currentBusiness / selectedBusiness / apply-selected business` 三者关系
2. 决定 `MiniBarWindow` 什么时候把 selected business 注入 `TxSessionService`
3. 定义 `MiniBarBusinessPanelProvider` 或 `IBusinessCompactPresenter`
4. 选一个 analog business 做首个 compact panel 样板

## 风险点

### 1. BusinessManager 当前没有 unregister 收口

这意味着如果要做“运行时重建菜单模型”或未来 plugin unload，需要提前留意生命周期对称性。

### 2. compact panel 不是所有业务都能直接复用

需要接受一个现实：有些 business 很适合像 `StepSweepPanel` 那样补 compact mode，有些则可能需要额外 presenter/adapter，不能假设所有 panel 都能零成本塞进 popup。

## 验证口径

## 2026-05-08 mod 菜单交互缺陷收口

本轮只聚焦 `MOD` 按钮与其 `QMenu` / compact panel 的交互一致性，不扩展业务范围。

### 现象

1. 未使能任何 mod 时，点击 `AM` 会弹出 compact panel，但关闭后再次展开 menu，`AM` 已经出现选定勾选符号。
2. 已使能 `AM` 后，再从 menu 点击 `FM` 仅浏览其 compact panel；此时若不使能直接关闭，再回点 `AM`，panel 退化成一个很小的点，且 `Mod` 按钮高亮丢失。

### 本地根因判断

1. `MiniBarBusinessMenuHost` 当前把 `QAction` 设为 checkable，但 `onActionTriggered()` 只更新 `currentBusiness` 并发出 `businessTriggered()`；Qt 会在点击时自动把 action 勾上，而 host 没有立刻把勾选状态拉回 `selectedBusiness` 语义。
2. `MiniBarWindow::showBusinessPanelPopup()` 在切换不同 business panel 时会把旧 panel `hide()` 并脱离 popup；后续重新把该 panel 挂回 popup 时，没有显式 `show()`，导致 popup 按隐藏子控件的最小尺寸收缩成“小点”。

### 本轮修正边界

1. 菜单勾选只允许反映 `selectedBusiness`，浏览 `currentBusiness` 不得污染勾选态。
2. business popup 复用 panel 时必须恢复其可见性，并在每次展示前刷新布局尺寸。
3. 不修改 `InfoButton` 基类；不在本轮扩展 compact panel 样式范围。

### 本轮最终 UI 收尾

在上述语义修正稳定后，又补了两条 UI 交互一致性收口：

1. `Mod` 的 `QMenu` 弹出方向改为与 compact business panel 一致，统一按“水平向左展开，右边与 minibar 右边对齐”的规则定位，避免 menu 和 panel 分别从左右两侧弹出。
2. `m_modProviderButton` 虽然仍为 checkable，用于表达 `appliedBusiness` 是否存在，但点击按钮展开 menu 不应把高亮语义临时翻掉；因此在 menu 打开前和 `aboutToHide` 时都显式回刷 `updateModProviderButtonText()`，保证“已使能时高亮始终跟随真实 applied 状态，而不是跟随按钮点击态”。
3. compact business popup 顶部补一个只属于 minibar host 的标题 label，文本取 `IBusiness::fullName()`；这样能让用户明确当前看到的是哪个调制模式的配置页，同时不需要修改 `Panel` 基类或业务 panel 本体，因此 main-mode 下的标题与布局完全不受影响。

第一大步当前已经按下面口径完成验证，仍暂不等于完整调制下发：

1. minibar 启动后 menu item 来源不再是硬编码
2. analog business 注册后能自动反映到 menu
3. 设备 open 成功后，analog license 结果能正确隐藏/恢复受控 item
4. `Unknown` 阶段 menu 不闪烁
5. 当前显示项被隐藏时，fallback 与按钮文案正确

第二大步开始后，再按单个 compact panel 增量验证：

1. 点击 item 能弹出正确 compact panel
2. popup ownership 正确，不会触发 minibar outside-click 误折叠
3. compact panel 编辑能保持原业务语义
4. 与 `TxSessionService` 的接线只影响该 business 自己，不破坏 `RF / Sweep` 已有链路

## 最终建议
“business-backed 菜单模型 + analog license 驱动菜单显隐 + current/fallback 语义”已经完成，把下一步目标明确收口为“selected/current/apply 语义 + compact panel presenter 接口”；然后再逐个把 compact panel 挂上去。

这样第二步做出来的 panel 才是挂在稳定入口上的，而不是继续绑定在一个临时占位菜单上。

## 2026-05-08 二次收口

基于本轮讨论，第二大步不再是泛泛地“把 compact panel 挂上去”，而是明确按下面三个切片推进。

### 切片 A：先改状态模型与按钮样式

目标：先把 minibar 的 `Mod` 按钮从“菜单当前项”语义改成“当前是否真的存在已 apply 的 mod provider”语义。

本轮确认的产品语义：

1. 当没有任何 mod provider 真正使能时，`m_modProviderButton` 只显示 `Mod`，不显示副标题。
2. 这个状态下按钮不高亮；点击后可以展开 menu，但 menu 内不应有勾选符号。
3. 点击 menu item 的语义是“浏览 / 打开该 business 的 compact panel”，不是“立即 apply 该 provider”。
4. 只有当某个 business 在 compact panel 内被真正 enable 后，按钮才高亮，副标题才更新为该 business 名称。
5. 只有在上述真正 enable 之后，再次展开 menu 时，对应 item 才应该显示勾选符号。

据此，minibar 内部状态至少要拆成三层：

1. `currentBusiness`：当前用户刚点击、正准备查看 compact panel 的业务。
2. `selectedBusiness`：当前已经被用户确认启用、将参与发射链的业务。
3. `appliedBusiness`：派生态，不单独存字段；当 `selectedBusiness != nullptr` 且共享 `Mod` 属性为 true 时成立。

按钮视觉语义对应关系：

1. `checked` 只表达 `appliedBusiness` 是否存在。
2. 副标题只表达 `appliedBusiness` 的显示名。
3. `currentBusiness` 不应直接驱动按钮高亮，也不应直接驱动 menu 的勾选状态。

额外实现约束：

1. 不能继续让 `MiniBarBusinessMenuHost` 在初始化时自动选中第一个 visible business。
2. 不能继续让 menu action triggered 直接等同于 `selectedBusiness = currentBusiness`。
3. `LabelButton` 仅把 `labelText` 置空还不够；若需要“真正只显示一行 Mod”的视觉，需补显式的副标题可见性语义或等价布局收口。

### 切片 B：补 `MiniBarBusinessPanelPresenter` 接口

目标：把“某个 business 在 minibar 下如何弹出 compact panel”从 `MiniBarWindow` 中抽离出来，避免后续出现按 business 名字写 if/else 的分支树。

建议接口责任：

1. 根据 `IBusiness *` 判断该业务是否支持 minibar compact presenter。
2. 为给定 business 创建或复用 compact panel widget。
3. 将 panel 放入 minibar popup 容器，并负责 popup 生命周期对齐。
4. 在需要时，把 panel 的 enabled 状态变化回写为 `selectedBusiness` 变化。

接口边界：

1. `MiniBarWindow` 只负责：menu 触发、popup host、按钮文本与 TxSession 刷新。
2. presenter 负责：business 到 panel 的映射，以及 panel 级 popup 内容组织。
3. business 自己仍是业务语义 owner；presenter 只是 minibar 下的 UI 适配层。

### 切片 C：先接简单 analog compact mod 样板

目标：先以 `AM / FM / Pulse / Ramp` 这类简单 analog business 做首批样板，直接复用原 panel，走类似 `StepSweepPanel` 的 compact 语义。

当前确认的范围：

1. 简单 analog panel 优先复用现有 panel，不重新做一套小窗版。
2. compact 语义先做最小收口：去掉或隐藏 `Save / Record` 类按钮，压紧边距与高度即可。
3. `Digital Mod / OFDM / Playback / Streaming` 这类复杂业务暂不在本轮实现范围内，后续再评估是完全复用、阉割复用还是单独设计。

样板完成标准：

1. 点击 `AM / FM / Pulse / Ramp` 菜单项能弹出对应 compact panel。
2. 在 panel 中 enable 后，minibar `Mod` 按钮高亮，并显示对应副标题。
3. 该 enable 能正确驱动 `TxSessionService::setSelectedBusiness(...) + requestRefresh()`，形成真实 apply。
4. disable 后按钮恢复为“只显示 Mod、无副标题、无勾选”的空态。
5. 该链路不改变 analog 现有 license 三态规则，也不绕开原有 business / panel owner 关系。

### 建议落地顺序

本轮代码按下面顺序推进，避免把按钮语义、popup owner、business enable 三件事一次糊在一起：

1. 先改 `MiniBarBusinessMenuHost + MiniBarWindow` 的状态模型与按钮样式。
2. 再补 presenter 接口，但初始只接 placeholder / infrastructure。
3. 最后把 `AM / FM / Pulse / Ramp` 接成第一批 compact sample。

## 2026-05-08 当前联调结论

经过当前轮次的本地联调，minibar `Mod` 入口的基础交互已达到可继续扩展的稳定状态。

### 当前已确认正常的行为

1. `Mod` 按钮空态、已 apply 态、menu 勾选态三者语义已经一致。
2. 点击 menu item 仅执行“浏览 / 打开 compact panel”，不会误写成 selected/apply。
3. 已 apply 某个 business 后，再浏览其他 business 并关闭，不会破坏原有高亮、按钮副标题与 menu 勾选。
4. business popup 复用已稳定，不再出现 panel 缩成小点的问题。
5. `Mod` 的 menu 与 compact panel 统一按“向左展开，右边与 minibar 右边对齐”的方式弹出。
6. compact business popup 顶部标题已由 minibar host 承担，能明确标识当前业务，同时不影响 main-mode panel。

### 当前阶段结论

这意味着后续再接更复杂的业务时，不必继续怀疑入口层、popup host、selected/current/apply 语义本身；下一阶段的主要风险将转移到“复杂业务自身怎样 compact 化”以及“哪些复杂页面应该复用、裁剪还是拆专用 presenter”。

## 后续复杂界面 compact 模式安排

对于 `AWGN / Digital Mod / OFDM / Playback / Streaming` 这类复杂业务，不建议简单复制 `AM / FM / Pulse / Ramp` 的做法，原因是这些页面通常同时具备：

1. 纵向参数区更长，原 panel 直接塞进 popup 会导致高度失控。
2. 有 tab、group、预览区、图表区、文件区或多步操作区，不适合无差别整体压缩。
3. 某些页面的“使能”只是其中一步，真正关键的是参数预览、上下文检查或文件状态。

因此，后续建议按下面三类来安排，而不是统一一刀切：

### A 类：可直接复用原 panel 的复杂页

适用条件：

1. 原 panel 虽然元素较多，但核心操作仍集中在一屏内。
2. 只要压缩边距、隐藏次要按钮、收起非关键说明文本，就能在 popup 内完成主要配置。

建议做法：

1. 继续走现有 `Panel::supportsCompactMode() + setCompactMode(true)` 路径。
2. 由业务 panel 自己决定隐藏哪些非关键区域。
3. presenter 只负责 popup host，不介入业务布局细节。

### B 类：需要“裁剪版 compact 容器”的复杂页

适用条件：

1. 原 panel 包含预览图、文件列表、诊断区、批量操作区等大块次级区域。
2. 用户在 minibar 场景下真正高频使用的只是其中少量关键参数与 enable/apply 操作。

建议做法：

1. 不直接把整个原 panel 塞进 popup。
2. presenter 提供一个 compact container，只嵌入原 panel 的关键子区域，或通过 adapter 暴露一组关键控件。
3. 原 panel 仍保留 main-mode 的完整布局；compact container 只是 minibar 专用的裁剪视图。

这里的关键原则是：复用业务语义 owner，不强迫 main-mode panel 为 minibar 妥协布局。

### C 类：需要独立 compact presenter 的复杂页

适用条件：

1. 原页面本质上是工作流式界面，而不是单页参数表单。
2. 页面依赖多步校验、预览、异步加载、文件解析、图表交互或设备状态联动。
3. 直接 compact 化会让 popup 同时承担过多语义，既不好用，也难维护。

建议做法：

1. 不复用原 panel 的 QWidget 布局。
2. 单独为 minibar 定义 presenter/adapter，提供一个更小的“快捷配置面板”。
3. 这个快捷面板只覆盖用户在 minibar 场景下最常用的 20% 操作；剩余复杂配置仍回到 main-mode 完成。

## 后续实施建议

后续复杂业务的 compact 化建议不要按业务名字凭感觉逐个硬上，而是先做一轮盘点，把每个业务归入上面三类之一。建议顺序如下：

1. 先列出复杂业务清单，例如 `AWGN / Digital Mod / OFDM / Playback / Streaming`。
2. 对每个业务判断：是 A 类直接复用、B 类裁剪容器、还是 C 类独立 presenter。
3. 优先选一个“中等复杂度但不是最复杂”的业务做下一轮样板，不要一开始就上最重的页面。
4. 每接一个复杂业务，都单独验证 popup 尺寸、可编辑性、enable/apply 语义以及是否需要回退到 main-mode 完整配置。

## 推荐下一步

如果下一轮要继续推进，我建议不要直接开始写复杂页面代码，而是先补一份“复杂业务 compact 分类表”，把每个候选业务明确归类为 A/B/C，再决定谁做下一批样板。这样可以避免后面一边做一边改策略。

## 2026-05-08 全量 provider 复用接入补充

本轮目标从“继续做复杂业务 compact 分类”临时切到“先把 minibar 下剩余 mod provider panel 全部打通”，并接受一个刻意简化的实现边界：

1. 优先复用原有 `controlPanel()`，不要求剩余 provider 先补完整 compact-mode 适配。
2. 对 presenter 的准入语义，先从“`panel->supportsCompactMode()` 才允许进入 minibar”放宽为“只要 business 提供 `controlPanel()` 就允许复用”。
3. `preparePanel()` 仍可对已支持 compact mode 的 panel 调 `setCompactMode(true)`；不支持 compact mode 的 panel 保持原布局直接复用。
4. 为减少分支，本轮统一恢复 compact 场景下的 `Save IQ Data` 按钮，不再继续隐藏。

### 本轮局部根因判断

当前剩余 provider 无法在 minibar 中打开 panel 的最小根因，不是业务未注册，而是 `DefaultMiniBarBusinessPanelPresenter` 的 `supportsBusiness()` 过严：

1. `MiniBarBusinessMenuHost` 只按 `providerKind() != None` 决定菜单项是否出现。
2. `showBusinessPanelPopup()` 真正决定“点菜单后能否打开 panel”的入口是 presenter。
3. 当前 presenter 只有在 `business->controlPanel()` 存在且 `panel->supportsCompactMode()` 为真时才返回 panel。
4. 现状里只有 `AM / FM / Pulse / Ramp` 明确声明了 compact mode；但 `AWGN / Digital / DSSS / OFDM / Playback / Streaming` 都已有原始 `controlPanel()`。

因此，本轮最小修复不是给所有剩余业务先补一套完整 compact presenter，而是先修正 presenter 的复用准入边界，让 minibar 能直接消费这些现有 panel。

### 本轮实施边界

1. 修改 `DefaultMiniBarBusinessPanelPresenter`：只要有 `controlPanel()` 就支持复用。
2. 保留对已支持 compact mode 的 panel 的 `setCompactMode(true)` 调用，不强迫未支持业务实现 compact 接口。
3. 修改 analog compact helper，不再在 compact 模式下隐藏 record/save 按钮，使简单 analog 样板与后续复用 provider 的按钮语义一致。
4. 本轮不新增复杂业务专用 presenter，不改 `MiniBarWindow` 的 popup owner 和 selected/apply 语义。

### 预期结果

完成后，minibar 模式下除当前已有的 `AM / FM / Pulse / Ramp` 外，其余已注册的 mod provider 也都应能通过原 panel 打开：

1. `AWGN`
2. `Digital Mod`
3. `DSSS`
4. `OFDM`
5. `Playback`
6. `Streaming`

后续如果某个复杂 panel 的尺寸、布局或交互仍不适合 popup，再单独回到 A/B/C 分类去做第二轮 compact 化收口。

## 2026-05-08 全量复用后的最小 compact 收口

在“先全部复用原 panel 打通”落地后，新的局部问题已经明确：

1. 某些剩余 provider 虽然已经能在 minibar 中弹出原 panel，但顶部 `SwitchButton` 仍保持主界面尺寸。
2. 某些 panel 的 `verticalSpacer` 仍按主界面高度参与布局，导致 popup 内部出现大块无意义留白。
3. 这类问题不需要立刻进入完整复杂界面重构，也不应该把本轮目标重新抬升为“所有复杂 panel 都做完整 compact 版”。

因此，本轮补一个更小、更稳定的 UI 收口：

1. 对 `AWGN / Digital / DSSS / OFDM / Playback / Streaming` 至少补齐 `supportsCompactMode() / setCompactMode()`。
2. `setCompactMode()` 的最低要求不是重做整页布局，而是：
   - 把 `enabled` 对应的 `SwitchButton` 切到 compact 模式
   - 把面板内主 `verticalSpacer` 压缩为 0 高度或 fixed
3. 其余复杂区块先保持原样，避免本轮再次扩大范围。

也就是说，本轮目标从“全部 provider 可打开”进一步收口为“全部 provider 至少具备 minibar 可接受的顶部开关与底部留白表现”；完整复杂布局优化仍留到下一轮分类处理中。

## 2026-05-08 数字调制局部收口

本轮继续只处理 `Digital Mod` 这一页的两个具体问题，不扩散到其他 business：

1. compact 模式下纵向仍有明显留白。
2. 点击 `显示波形预览` 打开弹窗后，关闭该弹窗会导致整个程序退出。

### 局部根因判断

对第一个问题，当前证据已经足够明确：

1. `DigitalPanel` 在 compact 模式里虽然已经压了 `verticalSpacer_2` 和 `enabled` switch。
2. 但同一行的 `constellationHost` 仍保留 `minimumHeight=120` 并继续参与布局。
3. 该 host 原本是给旧的内嵌星座图区预留位置，当前 panel 自身已不再把星座图挂回这块区域。

因此，这块空白不应再靠继续压 spacer 解决，而应直接删除 `constellationHost` 这块历史残留占位。

对第二个问题，当前更合理的本地解释是：

1. minibar business panel 宿主本身是 `Qt::Popup`。
2. `DigitalSpectrumDialog` 作为普通 `QDialog` 打开时，会成为当前唯一的普通顶层窗口。
3. 关闭它时，Qt 仍可能把它当成触发 `quitOnLastWindowClosed` 的窗口，从而带出整进程退出。

这类问题可局部修，不需要退化为“compact 模式直接去掉波形预览按钮”。

### 本轮修正边界

1. 从 `DigitalPanel.ui` 中直接移除 `constellationHost` 占位区域。
2. 保留 `DigitalSpectrumDialog` 功能，但对该 dialog 显式关闭 `WA_QuitOnClose` 语义，避免关闭预览时触发应用退出。
3. 本轮不删除 `显示波形预览` 按钮；若后续仍发现 popup owner 交互问题，再单独评估是否在 compact 下隐藏。

## 2026-05-08 compact 对齐统一

在前几轮把各个 mod provider panel 的 compact 开口补齐后，按钮文字对齐语义仍不一致：

1. `StepSweepPanel` 的 compact 模式里，`LabelButton` 的 `text` 和 `labelText` 都按居中显示。
2. 其余 mod provider panel 目前主要只压了 switch 和 spacer，`LabelButton / EnumTextButton` 仍保留主界面的左标题 + 右数值布局。

这轮不再逐页散改，而是统一收口到 compact helper：

1. 在 analog / htra 的 compact helper 内统一遍历 `LabelButton`。
2. compact 模式时把 `textAlignment` 与 `labelAlignment` 都切到 `Qt::AlignCenter | Qt::AlignVCenter`。
3. 非 compact 模式恢复为常规的 `text=左对齐`、`label=右对齐` 语义。

因为 `EnumTextButton` 继承自 `LabelButton`，所以这次统一 helper 就能一并覆盖普通 label button 和枚举按钮，避免后续继续逐个 panel 补丁式修改。

## 2026-05-08 minibar 嵌套文件对话框退出问题

本轮又暴露出一个更底层的 minibar 窗口协议问题：

1. `Playback` / `Streaming` 在 compact panel 内需要弹出文件选择对话框。
2. 当前 `Controls::getOpenPath()` / `getSaveFilePath()` 在 Windows 下直接走 `QFileDialog` 的静态便捷 API，调用点又经常不给 parent。
3. minibar 自身是 `Qt::Tool` 顶层，而不是主窗口语义；这意味着如果再弹出一个无 parent、默认 `WA_QuitOnClose=true` 的顶层辅助对话框，关闭它时就可能把整个应用一并带走。

因此这里不能只在 `Playback` / `Streaming` 局部打补丁，而应把规则收口到 controls 公共封装：

1. Windows 下不要继续直接用 `QFileDialog::getOpenFileName/getSaveFileName/getExistingDirectory` 这类静态便捷入口。
2. 改为显式创建 `QFileDialog` 实例。
3. 对该实例显式关闭 `WA_QuitOnClose`。
4. 若调用者没传 parent，则回退绑定到当前 active window，避免在 minibar 模式下生成孤立顶层对话框。
5. `Playback` / `Streaming` 当前调用点再额外显式传入 `this`，把 owner 语义写清楚。

这个问题应沉淀为 minibar/轻量 host 的通用排障基线：

1. 只要 host 本身不是主窗口语义（例如 `Qt::Tool`、overlay、popup host），就不能假设“随手 new 一个无 parent dialog”是安全的。
2. 遇到“辅助弹窗一关闭整应用退出”时，优先检查 parent ownership 和 `WA_QuitOnClose`，而不是先怀疑业务逻辑。

进一步看，minibar 当前还有一个紧邻问题：

1. `MOD` 菜单点开后的 business panel popup 目前本身也是 `Qt::Popup`。
2. 这意味着即便文件对话框不再触发整应用退出，只要 panel 内部继续拉起模态文件对话框，business popup 也会因为失焦被 Qt 自动收掉。

因此这块还需要补一层 host 协议修正：

1. business panel popup 不能继续用 `Qt::Popup`，而要改成由 minibar 手动管理关闭时机的轻量 tool/dialog 窗口。
2. minibar 的 outside-click owned area 判断，除了 menu / activePopup / keyboard popup，还要把“由 minibar 业务 panel 拉起的 owned modal dialog”也视为 host 自己的一部分。
3. 这样 `Save`/`Load` 期间文件对话框可以稳定覆盖在 panel 之上，关闭文件对话框后原 panel 仍保持打开，不需要再做一次自动重弹。

## 2026-05-09 main / minibar panel 模式隔离重构

本轮目标从继续扩 business compact 范围，收口为先修正“main 正常模式”和“minibar compact 模式”之间的宿主职责边界。

### 当前局部根因

当前 `SwitchButton` 左侧 label 在 main 模式丢失，不应继续按单个 panel 补丁修，而应先承认展示模式 owner 混乱：

1. `FancyTabWidget` 当前在 `createContentWidget()` 内递归对 hosted page 的 `SwitchButton` 直接执行 `setCompactMode(true)`。
2. `DefaultMiniBarBusinessPanelPresenter` 当前也会在 minibar 侧对同一业务 `controlPanel()` 实例执行 `setCompactMode(true)`。
3. 多数 business 的 `controlPanel()` 返回的都是长期持有的单例 panel，而不是为不同宿主分配独立副本。

因此，main 和 minibar 当前并不是“两个彼此隔离的展示模式”，而是“两个宿主都在直接改同一批 panel/widget 的显示状态”。

### 本轮重构原则

1. 展示模式必须由宿主统一设置，不允许 `FancyTabWidget`、`MiniBarBusinessPanelPresenter` 各自散落一套子控件级规则。
2. main 宿主只表达 `Normal`，minibar 宿主只表达 `Compact`。
3. `SwitchButton` 的 compact 只能是 compact 展示语义的一部分，不能再被 main 模式拿来当宽度修复手段。
4. 业务 panel 自己保留 `setCompactMode(bool)` 实现，但宿主侧通过统一入口切换，而不是直接假设只改 `SwitchButton` 就够了。

### 本轮最小实施边界

1. 提供一个共享的 panel display mode 入口，负责把 `Normal / Compact` 语义落到 panel 与其内部 `SwitchButton`。
2. `FancyTabWidget` 改为显式应用 `Normal`，不再无条件把 hosted page 压成 compact。
3. `DefaultMiniBarBusinessPanelPresenter` 改为显式应用 `Compact`，必要时在 panel 脱离 minibar popup 时恢复到 `Normal`。
4. 本轮不扩展新的业务 compact 样式规则；只修复模式隔离和 main 下 label 丢失问题。

### 预期结果

1. main 模式恢复正常的 `SwitchButton` 左侧 label。
2. minibar 模式继续保留 compact 开关和紧凑布局。
3. 宿主切换时，展示模式的 owner 明确为 host，而不是散落在各页内部或 `FancyTabWidget` 局部 helper 中。

## 2026-05-09 minibar 局部 QSS 越界污染

本轮联调又确认了一条独立于 compactMode 的样式污染链：`MiniBarWindow` 构造函数里通过 `setStyleSheet()` 写死的 `LabelButton { ... }`、`LabelButton QLabel#textLabel { ... }`、`LabelButton QLabel#infoLabel { ... }` 选择器范围过大，会直接命中被挂进 minibar business popup 的业务 panel 内部控件。

### 当前局部根因

1. business panel popup 是 `MiniBarWindow` 的子孙窗口，panel 在显示期间会被 reparent 到 `m_businessPanelPopup`。
2. `MiniBarWindow` 自身设置的 stylesheet 会向其子孙控件级联。
3. 现有选择器写成裸 `LabelButton`，而 `EnumTextButton` 又继承自 `LabelButton`，所以 business panel 里的参数按钮、枚举按钮都会一起吃到 minibar 的深色紧凑按钮皮肤。

因此，即便当前 `MiniBarBusinessPanelPresenter` 临时被改成 `PanelDisplayMode::Normal`，业务 panel 内部的 `LabelButton/EnumTextButton` 仍然会因为这层父级 stylesheet 被渲染成 minibar 风格，看起来像“模式没有恢复”。

### 本轮修复边界

1. 不再继续扩大 `MiniBarWindow` 局部 QSS 的覆盖范围。
2. 给 minibar 自己创建的按钮打稳定 objectName，仅让局部 QSS 命中这些按钮。
3. business panel 继续走全局主题 QSS 与 panel 自己的 compact/helper 样式，不再被 `MiniBarWindow` 的按钮皮肤覆盖。