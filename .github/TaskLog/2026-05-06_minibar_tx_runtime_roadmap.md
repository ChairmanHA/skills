# Minibar 基础发射链路路线图

## 结论

如果下一步的目标是先打通 minibar 模式下最基础的三个入口：

1. `RF` 开关
2. `freq` 设置
3. `level` 设置

那么把 `TxOrchestrator` / `TxPipelineRuntime` 完整下沉为 UI 无关服务是必要条件，但不是充分条件。

真正需要下沉的，不只是这两个类本身，而是当前仍然挂在 `MainWindow` 上的整条 apply 主线：

1. `selectBusiness2Work()`
2. `updateOrchestrator()`
3. `buildApplyRequest()`
4. `applyResolvedPipeline()`

只有把这条主线一起收口为共享服务，再给 minibar 加一层很薄的 UI 适配，minibar 的基础发射链路才会真正闭环。

## 当前现状

### 已经具备的基础

当前共享运行时基础已经成立：

1. `CoreRuntimeServices` 已经负责 shared bootstrap。
2. `DeviceRuntimeBridge` 已经接管设备状态同步主链。
3. `BusinessManager` / `BusinessUiBridge` 已经与 `FancyTabWidget` 单例解耦。
4. dependent plugin 已经开始改接共享 signal / bridge，而不是直接抓 `MainWindow::instance()`。

这意味着“公共服务是否启动”已经不再依赖 `MainWindow` 还是 `MiniBarWindow`。

### 当前真正卡住 minibar 的点

当前卡点不在 shared bootstrap，而在“发射控制链仍然是 UI-owned”。

`MainWindow` 里当前仍然持有并驱动：

1. `TxOrchestrator`
2. `TxPipelineRuntime`
3. request 构造与 apply 调度主线

对应代码点在：

1. `src/plugins/core/mainwindow.cpp` 中的 `selectBusiness2Work()`
2. `src/plugins/core/mainwindow.cpp` 中的 `updateOrchestrator()`
3. `src/plugins/core/mainwindow.cpp` 中的 `buildApplyRequest()`
4. `src/plugins/core/mainwindow.cpp` 中的 `applyResolvedPipeline()`

与此同时：

1. `CommonPanel` 已经具备 `Center` / `Level` 的 property 绑定，以及 `RF` / `Mod` 的 property 写回逻辑。
2. `MiniBarWindow` 当前的 `RF` 仍然只是本地状态翻转。
3. `MiniBarWindow` 当前的 `freq` / `level` 仍然只是 placeholder popup。

所以现在并不是“只差把 orchestrator new 到别处”，而是“还缺一条共享 apply 主线，以及一个 minibar 薄适配层”。

## 建议目标

下一阶段不要一开始就追求“让 minibar 覆盖 MainWindow 的全部调制能力”。

建议先只打通最小闭环：

1. `RF OFF -> Mute`
2. `RF ON -> FixedCw`
3. `Center` 修改后可触发重新 apply
4. `Level` 修改后可触发重新 apply
5. 设备重连后可按缓存 request reapply

也就是说，第一阶段只做 `CW-only` 的 minibar 基础发射链。

## 建议分阶段路线

### 阶段 1：抽出共享的发射会话服务

新增一个 UI 无关的共享服务，名称可以是：

1. `TxSessionService`
2. 或 `TxExecutionService`

建议由 `CoreRuntimeServices` 持有。

它内部统一持有：

1. `TxOrchestrator`
2. `TxPipelineRuntime`
3. 已应用 request 的缓存状态
4. legacy / core-managed 切换逻辑

这一阶段的核心不是重新设计 pipeline 语义，而是把现有 `MainWindow` 中已经验证过的逻辑平移出来。

最直接的迁移来源就是：

1. `legacyActivationTargetFor()`
2. `buildApplyRequest()`
3. `applyResolvedPipeline()`
4. `updateOrchestrator()`

### 阶段 2：把服务输入面收窄成 UI 无关状态

`TxSessionService` 不应依赖 `MainWindow`、`CommonPanel` 或 `FancyTabWidget` 本身。

第一版只需要接收最小输入：

1. `RF` 是否开启
2. `MOD` 是否开启
3. 当前 carrier plan
4. 当前 selected business
5. 当前公共 profile 快照
6. 一次“请重新 resolve 并 apply”的触发

这样 `MainWindow` 和 `MiniBarWindow` 的关系就会变成：

1. UI 负责改 property / 选择业务 / 请求 apply
2. service 负责 resolve / build request / apply / reapply

### 阶段 3：先让 MainWindow 切到共享服务

不要直接先改 minibar。

更稳的顺序是先让 `MainWindow` 自己改为通过 `TxSessionService` 工作，保证当前行为不变。

这一步的意义是：

1. 先把行为回归风险锁定在已有完整 UI 上。
2. 先确认 service 抽离没有破坏现有 `CommonPanel`、business 选择、设备重连行为。
3. 让 minibar 后续只做“接共享服务”，不承担首次验证压力。

做到这一步后，`MainWindow` 中关于发射链路的角色应该收缩为：

1. 组装 UI
2. 订阅 UI 事件
3. 把事件转发给共享服务

### 阶段 4：给 minibar 增加薄控制适配层

在共享服务成立后，再给 minibar 增加一个极薄的控制适配层，例如：

1. `MiniBarTxControlsAdapter`

它只做三件事：

1. 把 `RF` 按钮改为写回 `RF` property，而不是维护本地 `m_rfEnabled`
2. 把 `freq` 按钮绑定到 `Center` property，并复用现有数字键盘
3. 把 `level` 按钮绑定到 `Level` property，并复用现有数字键盘

这层不应直接持有设备对象，也不应自己决定 pipeline。

### 阶段 5：让共享服务监听 property 变化并触发 apply

第一版只需要接住最小事件集合：

1. `RF` 的 `editingFinished`
2. `Center` 的 `editingFinished`
3. `Level` 的 `editingFinished`
4. `CommonDeviceProfile::profileChanged`
5. 设备重连后的 `reapply`

这样 minibar 只要能改 property，就能驱动共享发射主线。

### 阶段 6：把 minibar 显示值改成 property 驱动

当前 minibar 上的以下状态仍是本地 UI 状态：

1. `RF ON/OFF`
2. `freq` 文案
3. `level` 文案

这一阶段应统一改为由 property 驱动显示。这样可以保证：

1. `MainWindow` 与 minibar 看到的是同一份状态
2. 设备重连或外部修改后，minibar 文案能自动跟上
3. 本地缓存状态不会与真实 runtime 状态漂移

### 阶段 7：只做最小闭环验证

第一轮验证不碰 `MOD`、provider、sweep，只验证最基础 CW 主线：

1. 无设备时 minibar 可打开、可编辑、无崩溃
2. 打开设备后 `RF ON` 能进入 `FixedCw`
3. 修改 `Center` 能触发重新 apply
4. 修改 `Level` 能触发重新 apply
5. `RF OFF` 能回到 `Mute`
6. 设备重连后会按缓存 request 自动 reapply

如果这六项成立，就说明 minibar 已经从“展示壳”变成“最小可操作壳”。

### 阶段 8：再进入第二阶段能力扩展

基础 `RF` / `freq` / `level` 打通后，再考虑：

1. `MOD` 按钮
2. provider 选择
3. selected business 来源收口
4. sweep / `StepSweepPanel` 轻量入口
5. 其他 dependent plugin 的 minibar UI 接线

这一步不应该反过来阻塞第一阶段。

## 为什么这条路线更稳

这条路线的关键优点是先把复杂度压回 core，再让 minibar 只承载最小 UI 责任：

1. 共享服务负责真正的状态裁决与设备 apply
2. `MainWindow` 先成为共享服务的第一个消费者，承担首轮行为回归验证
3. minibar 只负责最小输入与显示，不承担发射逻辑 owner 的职责

这样可以避免再次出现“为了让 minibar 先跑起来，在窗口里临时复制一条本地发射链”的结构性回退。

## 后续建议补的文档

本轮路线图落地后，建议择机更新以下文档：

1. `.github/KnowledgeBase/device_open_ui_config_flow.md`
2. `.github/KnowledgeBase/device_status_ui_feedback.md`
3. `.github/KnowledgeBase/analog_device_license_gating.md`
4. `.github/KnowledgeBase/gnss_plugin_integration_summary.md`
5. `.github/KnowledgeBase/device_discovery_architecture.md`
6. `.github/KnowledgeBase/plugin_metadata_and_loading_architecture.md`
7. `.github/KnowledgeBase/ui_independent_runtime_and_minibar_design.md`

其中最优先的是最后一篇，因为它最适合作为当前设计的长期总览入口。
