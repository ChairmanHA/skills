# TxExecutionContext / TxSessionService 现状与 Provider 迁移下一步

> 2026-07-23 更新：core-managed TX 已改为串行异步执行。`TxSessionService`
> 仍在主线程完成 resolve 和不可变 request 快照；新的主线程
> `TxPipelineRuntime` 只维护 desired / in-flight / pending-latest /
> applied 状态及 epoch/generation 过滤；设备侧完整执行序列由
> `DeviceIoWorker` 持有的 `TxPipelineExecutor` 在
> `DeviceStatusUpdateThread` 上运行。设备生命周期、状态轮询和 core TX
> 配置因此处于同一串行 I/O 域。旧结果不会回写新 UI 意图，最终成功结果才会
> 更新 applied/RMS/窗口标题。当前仍保留完整 H2 set/query 序列，差分执行和
> Streaming 异步停止/重配属于下一阶段。波形生成期间不额外强制 Mute 或改变
> RF 意图，但保留“先做 Playback 基础配置、ready 后再下载/启动”的两阶段语义；
> payload 下载同步返回仍是共享 host storage 的释放边界。

> 2026-07-16 更新：legacy in-process `MiniBarWindow`、其 entry host 和 panel
> presenter 已删除。下文点名这些类型的详细实现与验证记录属于历史证据；当前
> minibar 通过 `MinibarHelperController + RemoteMinibarService + MinibarIpc` 接入
> 同一个 `TxSessionService` owner，当前架构见
> `minibar_cs_helper_scpi_architecture.md`。

## 当前设计的边界

当前代码已经把“裁决输入”和“执行输入”明确拆开，而且 `MSCAN` 已作为第三种 `CarrierPlanKind` 并入统一 request 模型。

在此基础上，发射 apply owner 也已经下沉到共享 `TxSessionService`：

1. `CorePlugin` 会先初始化 `CoreRuntimeServices`，再创建唯一的 `MainWindow`
2. `TxSessionService` 负责统一 resolve / build request / apply
3. `MainWindow` 只保留 UI host 注入与 writeback/UI 逻辑
4. `RemoteMinibarService` 把 helper 的 `RF / Center / Level / Sweep / MOD` typed
   intent 映射回 main 的现有 owner；helper 不复制 runtime，也不加载业务 panel

### 1. 裁决层：`TxPipelineSnapshot`

位置：`src/plugins/core/txpipelinestate.h`

职责只有一个：回答“现在应该跑哪条 pipeline”。

当前包含：

- `rfEnabled`
- `modEnabled`
- `carrierPlan`
- `basebandProvider`
- `providerCapabilities`
- `providerDebugName`
- `selectedBusinessName`

它不负责承载具体频点、电平、扫频参数、采样率、IQ 数据。

### 2. 执行层：`TxExecutionContext`

位置：`src/plugins/core/txpipelinestate.h`

当前拆成三块：

1. `TxCommonSettings`
   - `center`
   - `level`
   - `triggerCount`
   - `triggerSource`
   - `refClockSource`
   - `refClockFrequency`
   - `systemClockOut`

2. `TxCarrierPlanContext`
   - `kind`
   - `fscanStartFreq / fscanStopFreq / fscanFreqStep`
   - `lscanStartLevel / lscanStopLevel / lscanLevelStep`
   - `dwellTime`
   - `mscanPoints`

3. `TxProviderExecutionContext`
   - `providerKind`
   - `providerDebugName`
   - `sampleRate`
   - `fixedPlaybackCenterOffsetHz`
   - `playbackIqInterleaved`
   - `powerMetrics`
   - `dataReady`
   - `providerParticipating`
   - `errorMessage`

`playbackIqInterleaved` 当前不是按值保存的 `QVector<int16_t>`，而是 `Core::PlaybackPayload` immutable identity/view handle：

- request、session 和 runtime 之间复制的是轻量句柄，不复制完整 IQ 数据；
- equality 比较 payload identity、view offset 和 word count，不做整 payload 逐字比较；
- 发布后只暴露 `const int16_t *`，所有缩放、截取和补齐都必须在 builder 的唯一可写阶段完成；
- `hasPlaybackPayload()` 表达逻辑描述仍有效，`hasPlaybackPayloadStorage()` 单独表达 host 连续内存是否仍在。这允许同步下载返回后释放 host storage，同时保留设备驻留复用所需的 identity/长度。

其中 `mscanPoints` 由 `QVector<TxMScanPoint>` 表达，单点结构为：

- `frequency`
- `level`
- `dwellTime`

这样 runtime / device 层消费的是“本次实际要下发的点表”，不再依赖 UI 专属的 `ListModeWidgetProfile`。Streaming 的 session 执行上下文仍未并入这里。

### 3. 统一 apply 入口：`TxApplyRequest`

位置：`src/plugins/core/txpipelinestate.h`

当前结构：

- `deviceUid`
- `capabilityRevision`
- `pipeline`
- `context`

runtime 现在拿到的是“已裁决的 pipeline + 同一时刻采集的执行上下文 + 对应设备能力版本”，而不是若干零散参数。

## Provider Kind 口径更新

当前代码里，`BasebandProviderKind` 已进一步收敛为：

- `None`
- `Playback`
- `Streaming`

这里的 `Playback` 指的是“一次性 IQ 数据下发”这一执行语义，不再按数据来源拆分 provider kind。

也就是说：

1. 从文件读取波形后下载，是 `Playback`。
2. 从算法同步生成 IQ 后下载，是 `Playback`。
3. 从算法异步生成 IQ，ready 后再下载，仍然是 `Playback`。

因此本文后文提到 AM / FM / 数字调制 / 多音这类 modulation business 时，指的是 legacy 业务来源或 UI 页面，不代表新的 provider kind；在 core 裁决和执行语义里，它们统一属于 `Playback`。

## 当前代码里的实际调用链

### 1. `TxSessionService::updateOrchestrator()` 负责收集共享裁决输入

位置：`src/plugins/core/txsessionservice.cpp`

当前做的事情：

1. 从 PropertySystem 读取 `RF` / `MOD`。
2. 从 `TxSessionService::setCarrierPlan()` 读取当前 UI host 注入的 `CarrierPlanKind`。
3. 从 `TxSessionService::setSelectedBusiness()` 读取当前 UI host 注入的 `IBusiness`，并进一步提取：
   - `providerKind()`
   - `providerCapabilities()`
   - `providerDebugName()`
4. 交给 `TxOrchestrator::resolve()` 计算 `TxPipelineKind`。

这意味着 orchestrator 仍然只做裁决，不做设备配置；差别在于这些输入现在不再由 `MainWindow` 私有 owner 采集，而是由共享 service 在每次 refresh 时重新快照。

当前两个 UI host 的注入边界分别是：

1. `MainWindow::syncTxSessionState()`
   - 注入 selected business
   - 在 sweep enabled 时注入 `carrierPlanKind()`，否则回退到 `Fixed`
2. `MiniBarWindow`
   - 当前已 attach `MiniBarBusinessMenuHost` 作为 entry host
   - 菜单项来自 business registration，并通过 selected/current/visible/fallback 语义维护 `Mod` 按钮副标题与回退
   - analog license gating 已可直接驱动 minibar 菜单显隐
   - 当前仍不注入 provider/business 到 `TxSessionService`
   - 会在 sweep enabled 时注入 `carrierPlanKind()`，并把 `StepSweepPanel::fillCarrierPlanContext()` 作为 carrier context provider
   - 因而它当前代码上的共享 apply 主线仍覆盖 `Mute / FixedCw / SweepCw`；`MOD / provider` 当前已完成入口层，但 provider execution context 仍未接入

### 2. `StepSweepPanel` 负责统一载波计划建模

位置：`src/plugins/core/stepsweeppanel.h`
位置：`src/plugins/core/stepsweeppanel.cpp`

当前 `StepSweepPanel` 已收敛为 sweep UI 对外边界，负责：

1. `carrierPlanKind()`：把 `Freq / Power / List` 映射成 `FScan / LScan / MScan`
2. `fillCarrierPlanContext()`：把 UI 输入转换为统一 `TxCarrierPlanContext`
3. `applyCarrierPlanWriteback()`：把 runtime 返回的 sweep 生效值回写给 UI

其中 `MScan` 的 request 构造行为是：

1. 仍然以 `ListModePanel` 作为 authoring 真相源
2. 进入 request 前，按 `rangeFrom / rangeTo` 截出本次实际 apply 的有效点表
3. 仅把有效点表写入 `mscanPoints`

另外，`StepSweepPanel::argsChanged` 当前已经加上 enabled 态门控：

1. sweep 面板 disabled 时，切换 sweep type 或 list 页面点击 apply 只更新本地 UI，不触发共享 apply 主线
2. sweep 面板 enabled 时，参数变化才向上冒泡并参与裁决
3. `enabledChanged` 本身仍直接参与裁决，因为它就是 pipeline 输入的一部分

2026-05-14 之后，这条 `StepSweepPanel::setCompactMode(true)` 路径已经删除。当前 sweep popup 的边界是：

1. `StepSweepPanel` 按 normal host 直接挂入 minibar sweep popup，不再保留自己的 panel-level compact 实现
2. sweep 仍复用同一套 property binding、preview、writeback 与 enabled gating
3. 仓库当前唯一保留下来的 compact 语义，只剩 `SwitchButton::compactMode` 这个基础属性；它不再代表“整页 compact panel”，只代表单个 `SwitchButton` 的局部收紧状态

### 3. `TxSessionService::buildApplyRequest()` 负责收集执行输入

位置：`src/plugins/core/txsessionservice.cpp`

当前 builder 会一次性快照：

1. `CommonDeviceProfile` 公共参数，写入 `TxCommonSettings`
2. 对 `Fixed` 计划，直接写入 `CarrierPlanKind::Fixed`
3. 对 sweep 计划，通过 `setCarrierPlanContextProvider(...)` 注入的 UI callback 获取 `TxCarrierPlanContext`
4. 对 `FixedPlayback / SweepPlayback`，从当前 selected business 构造 `TxProviderExecutionContext`

这一步的核心价值是：runtime 不再自己回头读 UI / profile / business 内部状态。

### 4. `TxSessionService::applyResolvedPipeline()` 负责桥接分流

位置：`src/plugins/core/txsessionservice.cpp`

当前代码分成两条路径：

1. `core-managed`
   - `Mute`
   - `FixedCw`
   - `SweepCw`
   - `FixedPlayback`（两阶段：基础配置立即执行，payload ready 后再下发/触发）
   - `SweepPlayback(FScan/LScan/MScan)`（两阶段：基础配置立即执行，payload ready 后再下发/触发）

2. `legacy-managed`
   - 尚未参与 core execution context 的 Playback provider
   - `FixedStream`
   - `SweepStream`

其中 `SweepStream(MSCAN)` 仍然走 legacy-managed bridge，但 bridge request 已经能承载 `CarrierPlanKind::MScan`，并在 `StreamingBussiness` 内完成 overlay。

和之前不同的是，当前桥接分流已经不归 `MainWindow` 私有；`MainWindow` 与 `MiniBarWindow` 都只是 shared service 的 client。

因此现在的 host 分工是：

1. `TxSessionService`
   - 决定 pipeline
   - 构造 request
   - 执行 core-managed / legacy-managed 分流
2. `MainWindow`
   - 注入 selected business / sweep context
   - 接收 `deviceConfigurationDone` / `sweepConfigurationDone` 做 writeback
   - 维护标题、availability、页面高亮等 UI 逻辑
3. `MiniBarWindow`
   - 共享驱动 `RF / Center / Level`
   - 通过 normal `StepSweepPanel` 承载 sweep popup 与 carrier context 注入
   - 不复制一份 runtime owner

当前还有一个刻意保留的发布边界：

1. `MSCAN` 代码链路已接入 request / runtime / device / streaming bridge
2. 但由于设备联调条件暂不可用，`CorePlugin` 当前临时隐藏了 `StepSweep_SweepType` 的 `List` 入口
3. 也就是说，当前日常 UI 默认不会生成 `MScan` request，但实现代码保留，不回退

### 5. `TxPipelineRuntime` 只消费 request，不再回读外部状态

位置：`src/plugins/core/txpipelineruntime.h`
位置：`src/plugins/core/txpipelineruntime.cpp`

当前 runtime 主要做这些事：

1. `requestApply()` 缓存完整 `TxApplyRequest`
2. `fillDeviceProfile()` 从 `m_request.context.common` 填充设备 profile
3. `applySweepCw()` 从 `m_request.context.carrier` 读取 `FScan / LScan / MScan` 参数
4. `applyFixedPlayback()` 先执行 Playback Phase 1（基础配置），payload ready 后再执行下载/触发
5. `applySweepPlayback()` 执行 `Playback + FScan / LScan / MScan`
6. `querySweepWriteback()` 在 sweep 成功后立即查询设备实际 sweep 参数
7. `sweepConfigurationDone(...)` 把 sweep 生效值回写给 `StepSweepPanel`
8. `requestApply()` 在执行前校验 request 的设备 UID、capability revision，以及 Playback 采样率/容量
9. 新 payload 通过同步 `downloadDataSequence()` 下发；SDK 返回后无论成功或失败都立即释放共享 host storage
10. 下载成功后只保留 payload identity、word count、设备 UID 和 capability revision；相同 payload 再次 apply 时复用设备已驻留波形，不要求业务重新持有125/1000 MiB内存
11. 设备 UID、capability revision 或 payload identity 变化时，设备驻留记录失效并重新下载
12. 成功的 common profile writeback 会先把设备确认的 `Level` 收敛到 runtime 缓存的 `m_request.context.common.level`，再发布 `applyStateChanged(...)`

因此 `TxApplyRequest` 在进入 runtime 时是 requested snapshot；设备成功回写后，`currentRequest()` /
core-managed 的 `TxSessionService::appliedRequest()` 是已吸收设备确认 Level 的 effective snapshot。
这一步不回读 UI/profile，也不触发第二次设备配置。CW RMS 直接使用 effective Level，Playback RMS
使用 effective Level 加既有 waveform RMS offset。

这就是当前设计最重要的收敛点：

- **设备 reopen 不再重放缓存 request；能力同步收口后由 `TxSessionService` 重建带新 revision 的 request**

### 6. 共享触发、UI host 注入与设备 writeback 的驱动关系

位置：`src/plugins/core/coreplugin.cpp`
位置：`src/plugins/core/mainwindow.cpp`
位置：`src/plugins/core/minibarwindow.cpp`
位置：`src/plugins/core/txsessionservice.cpp`

当前关键驱动链路如下：

1. `CorePlugin::initialize()`
   - 先执行 `CoreRuntimeServices::initializeOnce()`
   - 再按 `--ui-mode` 分流创建 `MainWindow` 或 `MiniBarWindow`

2. 共享最小触发已经收口到 `TxSessionService`
   - `RF.editingFinished -> requestRefresh()`
   - `MOD.editingFinished -> requestRefresh()`
   - `CommonDeviceProfile::profileChanged -> requestRefresh()`

3. `MainWindow` 仍负责注入 UI 专属状态
   - `syncTxSessionState()` 写入 selected business / carrier plan
   - `setCarrierPlanContextProvider(...)` 提供 sweep context
   - `selected business`、provider execution context、sweep enabled/args 变化时调用 `refreshTxSession()`
   - 与共享 property/profile 重复的 RF/MOD/CommonProfile refresh 接线已移除，避免在 mainwindow 模式下双触发

4. `MiniBarWindow` 当前直接消费共享 property
   - `RF` 点击写回共享 property，并发 `editingFinished`
   - `Center` / `Level` 通过 `PropertyBindingManager::bindButtonToProperty` + `prepareNumericKeyBoard(...)` 编辑
   - `Center` / `Level` 提交后会走 property `editingFinished` / `externMapping()->groupChanged()` / `CommonDeviceProfile::profileChanged` / `TxSessionService::requestRefresh()` 这条共享链
   - `Sweep` 通过 `ensureSweepPanel()` 创建 `StepSweepPanel`，直接复用同一套 sweep UI 逻辑
   - `enabledChanged / argsChanged` 会触发 `refreshTxSession()`，`fillCarrierPlanContext()` 作为 `setCarrierPlanContextProvider(...)` 的数据源
   - `MOD / provider` 菜单已接入真实 business entry host：菜单来源于 business 注册，analog license 可动态更新 list
   - 当前仍未把 selected business 注入 `TxSessionService`
   - 仓库已删除 panel-level compact 接口；当前 business popup 里只保留 host 侧 helper，在 panel 已挂入 popup 并安装样式后，遍历其中的 `SwitchButton` 调 `setCompactMode(true)`，用于触发 popup-local 的局部 QSS 收紧

5. `TxPipelineRuntime::deviceConfigurationDone -> CommonDeviceProfile::setProfile()`
   - 设备返回的 common profile 被回写到共享 profile 真相源
   - runtime 会在发布该信号前先把设备确认的 `level` 写入当前 effective request，保证随后发布的 RMS 与 Level 使用同一次 writeback

6. `TxPipelineRuntime::sweepConfigurationDone -> StepSweepPanel::applyCarrierPlanWriteback(...)`
   - runtime 只在 sweep 真正成功后才查询并回写 sweep 生效值

7. minibar 的软键盘 host 边界已经过实测验证
   - `prepareNumericKeyBoard(property, triggerObj)` 在 minibar 模式下不依赖 `MainWindow`
   - 真实需要注意的是 host 自己的 outside-click / collapse 逻辑必须把 popup descendant 视为 owned area，否则点键盘时会把 minibar 提前折叠

这里仍然保留两个关键边界：

1. `SweepPlayback` 存在 Phase 1 / Phase 2 两次 common profile writeback，因此 sweep 真值 query 不能放在 `MainWindow` 收到任意 `deviceConfigurationDone(...)` 后盲查
2. `MScan` 的 writeback 结果不会 destructively 覆盖 `ListModePanel` 当前编辑表；query 结果只用于日志对账，并保持 sweep 类型为 `List`

## 当前日志口径（简化版）

当前日志只需要按三类看：

1. `[TxOrchestrator]`
   - 看裁决结果
   - 回答“当前应该进入哪条 pipeline，为什么 fallback”

2. `[TxPipelineBridge]`
   - 看桥接路由
   - 回答“当前是走 core-managed 还是 legacy-managed”

3. `[TxPipelineRuntime]`
   - 看真正的设备执行
   - sweep 相关日志已明确区分：
     - `requested`：本次 request 想下发什么参数
     - `writeback`：设备实际 query 回来的 sweep 参数
     - `mscanPoints / requestedPoints / writebackPoints`：`MScan` 对账摘要

4. 设备重开时关注：
   - DeviceManager 发布新的 capability revision
   - BusinessManager 完成同步 reconcile
   - `[TxPipelineRuntime] requestApply uid=... capabilityRevision=...`
   - request 中的 UID/revision 必须与当前设备快照一致

## 当前已实现范围

1. `TxPipelineSnapshot / TxPipelineKind / CarrierPlanKind / BasebandProviderKind` 已稳定落地。
2. `TxCommonSettings / TxCarrierPlanContext / TxProviderExecutionContext / TxApplyRequest` 已落地，runtime 统一消费完整 request。
3. `TxCarrierPlanContext` 已新增 `mscanPoints`，`TxMScanPoint` 已成为统一的 `MScan` 执行载体。
4. `StepSweepPanel` 已统一承载 `FScan / LScan / MScan` 的 snapshot / writeback 责任，`ListModePanel` 保持 authoring 真相源。
5. `Mute / FixedCw / SweepCw` 已不再依赖旧 `MuteBusiness / ContinuesWaveBusiness` 执行设备配置。
6. `SweepCw` 的代码链路已支持 `FScan / LScan / MScan`。
7. `FixedPlayback` 已切到两阶段 apply：
   - Phase 1：基础 Playback 配置
   - Phase 2：payload ready 后下载并触发
8. `SweepPlayback(FScan/LScan/MScan)` 已进入 core runtime，并复用同一套两阶段 apply。
9. `StreamingBussiness` 已支持 `SweepStream(MSCAN)` 的 bridge overlay，仍保持 session 语义，不强行并入同步 runtime。
10. 设备层已新增 `startPlaybackListSweep()`、`startStreamingListSweep()`，`FancyDevice` 已收敛出统一 `ListSweepConfig` / `startListSweepLocked()`。
11. sweep 参数 writeback 已补齐：
    - `FScan / LScan` 会回写 sweep UI 真相源
    - `MScan` 会 query 设备结果并写回执行态 `mscanPoints`，仅用于日志和执行对账
12. provider 已支持“参与 core 执行模型但 payload 尚未 ready”的语义，AM 这类异步 prepare Playback 可稳定进入 Phase 1 / Phase 2 流程。
13. 所有 `AnalogPlaybackBusiness` 子类（AM / FM / DigitalMod / AWGN / Pulse / OFDM / DSSS / Ramp）已统一迁入 `buildPlaybackExecutionContext`。
14. `Analog::MultitoneModulation`（直接继承 `IPlaybackBusiness`）已完成 per-class `buildPlaybackExecutionContext` 迁移。
15. 设备重开后不再调用 cached `reapply()`；`TxSessionService` 在 business capability reconcile 完成后重新 snapshot 并构建 request。
16. 当前发布入口上，`MSCAN` 仍处于“代码已接入、UI 暂时隐藏”的保守开放状态。
17. `TxSessionService` 已成为共享发射 owner，`MainWindow` 不再私有持有 resolve / build request / apply 主线。
18. `MainWindow` 当前已收缩为 shared service client，只保留 selected business / sweep context 注入与 writeback/UI 逻辑。
19. `MiniBarWindow` 已接入共享 `RF / Center / Level / Sweep`，其中 `Center / Level` 通过现有软键盘体系编辑，`Sweep` 通过 normal `StepSweepPanel` 复用同一套 carrier context / preview / writeback 逻辑，不再依赖 `MainWindow` 存在。
20. 2026-05-07 已在 Debug 运行时验证 minibar 的 `RF / Center / Level` 共享闭环：
   - `RF OFF -> ON` 进入 `FixedCw`
   - `Frequency -> 2GHz` 后日志中 `center=2000000000`
   - `Level -> 10dBm` 后日志中 `level=10.0`
   - `RF ON -> OFF` 回到 `Mute`
21. 当前代码层面，minibar 的 `Sweep` 已不再属于“未接线”状态：
   - `MiniBarWindow::ensureSweepPanel()` 创建 `StepSweepPanel`
   - `StepSweepPanel` 直接按 normal host 形态挂到 sweep popup
   - `enabledChanged / argsChanged` 触发 `refreshTxSession()`
   - `setCarrierPlanContextProvider(...)` 直接复用 `fillCarrierPlanContext(...)`
22. 当前代码层面，整个仓库已删除 panel-level compact 扩散链：
   - `Core::Panel` 不再提供 `PanelDisplayMode` / `applyPanelDisplayMode()` / `supportsCompactMode()`
   - analog / htra panel 不再保留各自的 `setCompactMode()` override 和 helper
   - `StepSweepPanel` 也不再保留自己的 compact 实现
   - 剩余的 compact 只存在于 `SwitchButton` 基础控件层，以及 `MiniBarWindow` 的 popup-local helper 中
23. 根据 2026-05-08 联机测试，minibar 的 `MOD / provider` 菜单模型已完成第一阶段入口收口：
   - `MiniBarBusinessMenuHost` 已 attach 到 `BusinessManager`
   - 菜单项来源于 business registration，而不是硬编码字符串
   - analog license 结果已可动态更新 menu list
   - `m_selectedProvider` 已退化为本地恢复缓存，按钮文案以当前 entry/fallback 为准
24. Playback payload 已从按值 `QVector<int16_t>` 收敛为 `PlaybackPayload` identity/view；request equality、缓存和 runtime apply 均不再复制或逐字比较完整波形。
25. Quick Waveform 与 HTRA ARB 的 Ordinary/IQS WAV 已直接填充最终 `int16_t` payload，并按当前设备125/1000 MiB能力校验；同步下载返回后释放host storage。
26. HTRA `ArbModulationOnly` 的 Ordinary/IQS 模式已迁入统一 request/runtime；ProgrammedArb 仍保持不参与 provider execution context 的边界。

## 当前仍未完成的范围

HTRA 插件当前 active 范围只保留 Arb / Streaming：

- HTRA FM / AM 已有 Analog 插件平替，相关旧文件已从 active code 中清理。
- HTRA ListMode 旧插件已不再注册；当前 `MScan` 前端入口统一收敛到 Core 的 `StepSweepPanel + ListModePanel`。
- HTRA Multitone 当前不在本轮重构范围内。

当前仍未完成的事项主要是：

1. `MSCAN` 的设备联调尚未完成，`SweepCw(MSCAN)`、`SweepPlayback(MSCAN)`、`SweepStream(MSCAN)` 和 reopen reapply 仍待联机验证。
2. `CorePlugin` 中的 `List` sweep 入口仍暂时隐藏，待设备验证可用后再恢复。
3. `FixedStream / SweepStream` 还没有 session 化进入 `TxExecutionContext`；当前仍不应为了兼容 Streaming 去污染同步 Playback request 模型。
4. HTRA `ProgrammedArb` 仍未迁入统一 request/runtime；它当前只保留文件解析、模式识别和 capability/UI 路由语义。Ordinary/IQS WAV 已迁入统一 Playback request，不再由业务层直接配置设备。
5. 若后续需要展示“设备实际生效的 `MScan` 点表”，应新增只读 applied snapshot / status view，而不是复用 `ListModePanel` 编辑器。
6. minibar 当前主要剩余的是 `MOD / provider` 的 provider execution context 注入；菜单模型和 analog license 驱动的 list 更新已经完成，`Sweep` 也已通过 normal `StepSweepPanel` 接入共享 session。当前不再把“给业务 panel 补 compact 接口”视作后续方向。

## 当前最重要的设计边界

1. runtime 只消费 `TxApplyRequest`，不回头散读 UI / profile / business 内部状态。
2. `StepSweepPanel` 是 `FScan / LScan / MScan` 的统一前端边界；`MainWindow` 不再自己手工拼 sweep 参数。
3. sweep 参数变化只有在 sweep 面板 enabled 时才参与 orchestrator；disabled 状态下的局部编辑只更新本地 UI。
4. `SweepPlayback` 的 common writeback 与实际 sweep writeback 不是同一个阶段。
5. sweep 真值 query 必须收敛在 runtime 内部，只能在 sweep 成功后进行。
6. `MScan` 的 authoring table 不作为 writeback 容器，避免把完整编辑表破坏成当前 apply 子集。
7. provider 的职责继续收敛到：参数编辑、数据准备、ready/error 状态反馈；设备执行权继续由 runtime 接管。
8. `Streaming` 继续保持 session bridge 语义，不强行并入同步 Playback runtime。
9. 若要让 analog panel 兼容 minibar，优先维持单一业务真相源与 host 侧最小注入；当前已明确不再恢复 panel-level compact 扩散链。
10. 对1000 MiB级payload，进程内最多允许一个超过125 MiB的Core-owned/adopted storage；SDK同步返回是host storage的释放边界，不允许request缓存延长其生命周期。
11. requested Level 与设备 writeback Level 必须在 runtime apply-result 边界收敛；UI 不自行修正 RMS，也不因显示回写再次发起配置。

## 下一步建议

1. 等设备联调条件恢复后，先重新开放 `CorePlugin` 的 `List` sweep 入口，再按 `CW / Playback / Streaming / reopen` 四类场景做联机验证。
2. 若要展示设备实际生效的 `MScan` 点表，单独补 applied snapshot 或只读状态视图，不要覆盖 `ListModePanel` 当前编辑表。
3. 仅在 Playback 家族边界稳定后，再单独推进 `Streaming` 的 session 化，不与同步 Playback 混做。
4. 若继续收敛 HTRA 插件，下一步只需要单独处理 `ProgrammedArb` 与 `Streaming` 两条特殊路径；Ordinary/IQS ARB 已完成统一 Playback 接入。
5. 若继续扩展 minibar 或其它轻量 host，优先采用 host 侧最小适配，例如 popup-local 样式或局部控件状态注入；不要重新恢复 panel-level compact mode 扩散链，也不要复制迷你面板。

## 一句话结论

当前 `TxExecutionContext` 主线已经完成 Analog Playback provider 以及 HTRA Ordinary/IQS ARB 的迁移，并把发射 apply owner 收口到共享 `TxSessionService`；文件 Playback 使用 immutable payload handle，1000 MiB级波形在request/runtime间不复制，SDK同步返回后释放host storage并可按identity复用设备驻留波形。`MiniBarWindow` 已验证打通 `RF / Center / Level` 共享闭环，`Sweep` 也已接入同一 shared session；当前仍需单独处理的是 ProgrammedArb、Streaming、minibar provider execution context 注入，以及处于“代码保留、UI 暂时隐藏、待设备联调恢复”状态的 `MSCAN`。
