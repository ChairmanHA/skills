# MSCAN 实施方案与本轮落地记录（CW / Playback / Streaming）

## 目标

在当前 `TxApplyRequest -> TxPipelineRuntime` 主线架构下，补齐 `MSCAN` 对应的三条发射路径：

1. `SweepCw(MSCAN)`
2. `SweepPlayback(MSCAN)`
3. `SweepStream(MSCAN)`

要求遵守当前已经建立的架构边界：

- `TxOrchestrator` 只做裁决，不做设备配置。
- `TxPipelineRuntime` 只消费 request，不回读 UI / business 内部状态。
- `Streaming` 继续保留 session 语义，不强行塞进同步 Playback request 模型。
- `ListModePanel` 继续作为 MSCAN 前端建模 UI 真相源。

## 本轮实施结果（2026-04-01）

本轮实现已经按原定三阶段全部落地完成，覆盖：

1. UI 到 request 的 MScan 建模打通
2. CW MSCAN 与 Playback MSCAN 的 core runtime / device 支持
3. Streaming MSCAN 的 bridge session 分支

### 1. 执行模型与 request 承载

已在 `src/plugins/core/txpipelinestate.h` 增加：

1. `TxMScanPoint { frequency, level, dwellTime }`
2. `TxCarrierPlanContext::mscanPoints`

这样 `MScan` 不再依赖 UI 专属的 `ListModeWidgetProfile`，runtime / device 层只消费“本次实际要下发的点表”。

### 2. UI / orchestrator / request 链路

已完成以下改动：

1. `StepSweepPanel` 新增 `carrierPlanKind()`、`fillCarrierPlanContext()`、`applyCarrierPlanWriteback()`
2. `ListModePanel::argsChanged()` 已向上转发到 `StepSweepPanel::argsChanged()`
3. `MainWindow::updateOrchestrator()` 已正确把 `SweepType::List` 映射为 `CarrierPlanKind::MScan`
4. `MainWindow::buildApplyRequest()` 已改为统一从 `StepSweepPanel` 读取 FScan / LScan / MScan carrier context

当前行为是：`ListModePanel` 中的完整编辑表仍然是 authoring 真相源，进入 request 前才按 `rangeFrom / rangeTo` 裁剪为本次实际 apply 的点表。

### 3. Core runtime 已支持 CW / Playback MSCAN

已在 `src/plugins/core/txpipelineruntime.cpp` 完成：

1. `SweepCw(MSCAN)` 执行分支
2. `SweepPlayback(MSCAN)` 执行分支
3. `querySweepWriteback()` 的 `tx_query_mscan()` 分支
4. `canManageRequest()` 对 `SweepPlayback + MScan` 放行
5. request / writeback 日志增加 `mscanPoints`、requested count、writeback count、点表摘要

其中 `tx_query_mscan()` 的结果会回填到执行态 `TxCarrierPlanContext::mscanPoints`，仅用于日志和执行对账，不会覆盖 `ListModePanel` 当前编辑表。

### 4. HTRA 设备层已补齐 MSCAN 三种基带入口

已完成以下改动：

1. `IDevice` 新增 `startPlaybackListSweep()`、`startStreamingListSweep()`
2. `DeviceOperator` 新增 `startStreamingListSweep()` 包装
3. `FancyDevice` 新增统一的 `ListSweepConfig` 与 `startListSweepLocked()`
4. `FancyDevice::setListSweep()` 已改为复用公共 helper，而不是保留旧的 CW-only 内联流程
5. `FancyDevice::getListSweep()` 已按调用方提供的容量查询 `tx_query_mscan()`，并正确写回实际点数与缓存的 repeat

当前 `MScan` 在设备层仍统一使用 `TRIGGER_ACTION_SWEEP`，与现有 FScan / LScan 的 sweep 语义保持一致。

### 5. Streaming bridge 已支持 MSCAN overlay

已在 `src/plugins/htra/streamingbussiness.cpp` 增加 `CarrierPlanKind::MScan` 分支：

1. streaming session 仍由 `StreamingBussiness` 持有
2. 仅在 worker loop 的 overlay 阶段调用 `startStreamingListSweep()`
3. 不改变现有 sender thread / generator 的边界

这意味着 `SweepStream(MSCAN)` 仍然遵守当前 bridge 模型，而不是被硬塞进同步 runtime。

## 当前验证状态

截至本轮文档更新时：

1. Release 构建已通过，仓库现有 CMake Release 构建任务返回码为 0
2. 静态检查未发现与本轮 MSCAN 改动直接相关的新编译错误
3. 运行测试尚未执行，以下场景仍需人工联机验证：
   - `SweepCw(MSCAN)` 的整轮扫描行为
   - `SweepPlayback(MSCAN)` 的 Phase 1 / Phase 2 切换与重入
   - `SweepStream(MSCAN)` 的 session 内重配置行为
   - 设备 reopen 后的 cached request / bridge 重配恢复

因此当前结论是：代码链路与编译链路已打通，运行时行为仍待设备侧联调确认。

## 当前基线

### 已有能力

1. `CarrierPlanKind::MScan` 已存在于 `src/plugins/core/txpipelinestate.h`。
2. `StepSweepPanel` 已有第三个 `List` 页面，并嵌入 `src/plugins/core/listmodepanel.*`。
3. `ListModePanel::getProfile()` 已能输出完整的点表数据：
   - `freqList`
   - `powerList`
   - `timeList`
   - `rangeFrom`
   - `rangeTo`
4. `IDevice` / `DeviceOperator` / `FancyDevice` 已存在 `setListSweep()` / `getListSweep()` 包装。
5. H2 API 文档已经确认 `tx_config_mscan()` / `tx_query_mscan()` 可用，推荐顺序与现有 FScan / LScan 一致。

### 当前缺口

1. `MainWindow::updateOrchestrator()` 还不会把 `SweepType::List` 映射成 `CarrierPlanKind::MScan`。
2. `MainWindow::buildApplyRequest()` 目前只能从 `StepSweepPanel` 读取 FScan / LScan 参数，拿不到 MSCAN 点表。
3. `TxCarrierPlanContext` 还没有承载 MSCAN payload 的字段。
4. `TxPipelineRuntime::canManageRequest()` / `applySweepPlayback()` 目前明确只支持 `FScan / LScan`。
5. `StreamingBussiness` 当前只桥接 `FScan / LScan` overlay，没有 `MScan` 分支。
6. 现有 `FancyDevice::setListSweep()` 是 CW 专用语义，内部直接：
   - `tx_config_mscan`
   - `channel_config_trigger`
   - `tx_config_cw`
   - `tx_config_output`
   - `channel_start`
   - `channel_trigger_bus`
   这意味着它不能直接复用于 Playback / Streaming MSCAN。
7. `StepSweepPanel` 没有把子控件 `ListModePanel::argsChanged()` 往上转发，因此 list 页面 Apply 后 `MainWindow` 目前收不到参数变更。

## 核心设计

### 1. 统一 MSCAN 载波建模，不复制三套列表结构

建议在 `src/plugins/core/txpipelinestate.h` 新增独立点结构，例如：

- `TxMScanPoint`
  - `frequency`
  - `level`
  - `dwellTime`

然后扩展 `TxCarrierPlanContext`：

- `QVector<TxMScanPoint> mscanPoints`

不建议把 `ListModeWidgetProfile` 直接塞进 `TxCarrierPlanContext`，原因有二：

1. `dwellTimeMode`、`globalDwellTime` 是 UI 编辑语义，不是设备执行语义。
2. runtime 需要的是“本次实际要下发的离散点表”，不是整个编辑器状态对象。

### 2. 由 StepSweepPanel 负责把 UI 状态转换为 CarrierPlanContext

不建议继续让 `MainWindow` 自己判断 `Freq / Power / List` 并手工拼 `TxCarrierPlanContext`。

建议把这部分责任收敛到 `StepSweepPanel`：

1. 新增 `CarrierPlanKind carrierPlanKind() const`
2. 新增 `bool fillCarrierPlanContext(Core::TxCarrierPlanContext *carrier) const`
3. 新增 `void applyCarrierPlanWriteback(const Core::TxCarrierPlanContext &carrier)`

这样分工更干净：

- `MainWindow` 只管 orchestrator + request builder。
- `StepSweepPanel` 负责把 FScan / LScan / MScan 三种 UI 输入映射成统一 carrier context。

### 3. MSCAN request 中只放“本次实际 apply 的点表”

`ListModePanel` 当前同时保存：

- 完整表格数据
- `rangeFrom`
- `rangeTo`

建议 builder 在进入 request 前就把 range 截断完成，只把有效点表放进 `TxCarrierPlanContext::mscanPoints`。

理由：

1. runtime 不应继续理解 UI 的 1-based range 编辑语义。
2. request 应该表达“这次到底要下发哪几个点”，而不是“去 UI 里再切一次范围”。

这意味着 `fillCarrierPlanContext()` 内部做两件事：

1. 从 `ListModePanel::getProfile()` 取全量列表。
2. 按 `rangeFrom / rangeTo` 切成有效点表后填入 `mscanPoints`。

## 三条执行路径的落地方式

### A. SweepCw(MSCAN)

这条路径继续进入 `TxPipelineRuntime`，与当前 `SweepCw(FScan/LScan)` 同属 core-managed。

#### 需要修改

1. `MainWindow::updateOrchestrator()`
   - `SweepType::List -> CarrierPlanKind::MScan`
2. `TxPipelineRuntime::applySweepCw()`
   - 增加 `CarrierPlanKind::MScan` 分支
3. `TxPipelineRuntime::querySweepWriteback()`
   - 增加 `MScan` query 分支

#### 设备层建议

当前 `FancyDevice::setListSweep()` 虽然可用，但实现风格与 F/L scan helper 不一致，建议比照：

- `startFrequencySweepLocked()`
- `startLevelSweepLocked()`

新增统一 helper，例如：

- `ListSweepConfig`
- `startListSweepLocked(const ListSweepConfig &config, QString *errorMessage)`

配置步骤保持与现有 sweep helper 一致：

1. `tx_config_mscan()`
2. `channel_config_trigger(... TRIGGER_ACTION_SWEEP ...)`
3. `tx_config_cw()`
4. `tx_config_output()`
5. `channel_start()`
6. `channel_trigger_bus()`

然后 `IDevice::setListSweep()` 改为调用这个 helper 的 CW 版本。

### B. SweepPlayback(MSCAN)

这条路径也建议进入 `TxPipelineRuntime`，与现有 `SweepPlayback(FScan/LScan)` 保持同一套“两阶段 apply”模型。

#### Phase 1

沿用当前 `SweepPlayback` 行为：

1. `device->configuration(PlayFromRam)`
2. 若 provider payload 未 ready，只完成 Phase 1 并等待后续 re-apply

#### Phase 2

payload ready 后：

1. `uploadPlaybackWaveform()`
2. 进入 Playback MSCAN 启动接口

#### 新增设备接口

当前 `IDevice` 只有：

- `startPlaybackFrequencySweep()`
- `startPlaybackLevelSweep()`

需要新增：

- `startPlaybackListSweep(uint32_t startSeqNum, uint32_t endSeqNum, const QVector<TxMScanPoint> &points, int32_t repeat, QString *errorMessage)`

`FancyDevice` 内部应复用同一套 `startListSweepLocked()`，只是在 baseband 配置阶段改成：

1. 从 waveform cache 取单个 playback waveform
2. `tx_config_playback()`
3. `tx_config_output(... RF ON, MOD ON ...)`
4. `channel_start()`
5. `channel_trigger_bus()`

注意：本阶段的 `Playback MSCAN` 仍然是“一个 provider payload + 一个 MScan 载波计划”，不是“每个点切不同 waveform”。

这与现有 `SweepPlayback(FScan/LScan)` 的架构完全一致，也符合当前 provider 模型。

### C. SweepStream(MSCAN)

这条路径不建议强行迁入 `TxPipelineRuntime`。按照现有文档边界，`Streaming` 继续保留 legacy session，只桥接 carrier plan。

#### 建议做法

1. `TxOrchestrator` 继续裁决为 `SweepStream`
2. `MainWindow::buildApplyRequest()` 继续构建带 `CarrierPlanKind::MScan` 的 request
3. `MainWindow::applyResolvedPipeline()` 继续走 streaming bridge
4. `StreamingBussiness::setBridgedApplyRequest()` 继续只缓存 carrier context
5. `StreamingBussiness::workerLoop()` 新增 `CarrierPlanKind::MScan` overlay 分支

#### 新增设备接口

当前已有：

- `startStreamingFrequencySweep()`
- `startStreamingLevelSweep()`

需要新增：

- `startStreamingListSweep(double sampleRate, const QVector<TxMScanPoint> &points, int32_t repeat, QString *errorMessage)`

底层调用顺序应为：

1. `tx_config_mscan()`
2. `channel_config_trigger(... TRIGGER_ACTION_SWEEP ...)`
3. `tx_config_stream(sampleRate)`
4. `tx_config_output(... RF ON, MOD ON ...)`
5. `channel_start()`
6. `channel_trigger_bus()`

这与当前 `startStreamingFrequencySweep()` / `startStreamingLevelSweep()` 的结构一致，只是把 RF plan 换成 list plan。

## 写回与 UI 真相源策略

### FScan / LScan

继续保持现状：

- runtime query 成功后
- `MainWindow` 把 `TxCarrierPlanContext` 回写给 `StepSweepPanel`

### MScan

不建议在第一版把 `tx_query_mscan()` 结果直接覆写回 `ListModePanel` 编辑器。

原因：

1. request 内建议只保存“本次实际 apply 的有效子区间”。
2. `ListModePanel` 编辑器里可能还保留了未下发的其余行。
3. 若把 query 结果直接 `onProfileChanged()` 回去，会把完整编辑表破坏成“仅当前 apply 子集”。

因此第一版建议：

1. runtime 仍然执行 `tx_query_mscan()` 做日志对账。
2. 记录 `requested` / `writeback` 两类 MSCAN 日志。
3. 先不把 writeback destructively 写回 `ListModePanel`。

更明确一点，第一版代码行为定为：

1. runtime 在 `MScan` 成功下发后仍然调用 `tx_query_mscan()`。
2. query 结果会回填到 `TxCarrierPlanContext::mscanPoints`，仅用于日志和执行对账。
3. `MainWindow -> StepSweepPanel` 的 writeback 链路在 `FScan / LScan` 下继续回写 UI 真相源；在 `MScan` 下只维持当前 sweep 类型为 `List`，不覆盖 `ListModePanel` 的 authoring table。
4. query 容量使用 request 中本次实际下发的点数；若设备返回点数变化，只记录 `requested count` 与 `writeback count` 差异，不裁剪用户当前编辑表。

后续若确实需要显示“设备实际生效点表”，建议单独增加：

- applied snapshot
- 或只读状态视图

不要复用 authoring editor 作为 writeback 容器。

## Trigger 语义建议

用户提供的示例代码展示了 `MSCAN + TRIGGER_ACTION_HOP` 也是合法 API 组合，但当前仓库主线 sweep 语义一直是：

- `TRIGGER_ACTION_SWEEP`
- `TriggerCount` 作为 sweep 重复次数
- 驱动逻辑默认按“整轮扫频/扫表”理解

因此本次实施建议：

1. 第一阶段默认与现有 FScan / LScan 对齐，继续使用 `TRIGGER_ACTION_SWEEP`。
2. 若后续要支持“每次 BUS 触发只前进一步”的 HOP 模式，再单独补 `TriggerPolicy` 建模与 UI 入口。

原因：

1. 当前 UI 没有 `trigger.action` 配置位。
2. HOP 模式下每点 dwellTime 语义会被弱化甚至失效，和现有 ListModePanel 的主编辑语义不一致。
3. 先把 MSCAN 作为“第三种 sweep carrier plan”并入主线，风险最低。

## 推荐实施顺序

### Phase 1: UI -> request 建模打通

涉及文件：

- `src/plugins/core/txpipelinestate.h`
- `src/plugins/core/stepsweeppanel.h`
- `src/plugins/core/stepsweeppanel.cpp`
- `src/plugins/core/mainwindow.cpp`

任务：

1. 给 `TxCarrierPlanContext` 增加 `mscanPoints`
2. `StepSweepPanel` 新增 MScan-aware snapshot / writeback 方法
3. `StepSweepPanel` 转发 `ListModePanel::argsChanged()`
4. `MainWindow` 改为通过 `StepSweepPanel` 读取 carrier context
5. `updateOrchestrator()` 正确输出 `CarrierPlanKind::MScan`

完成标志：

- log 中已能看到 `Carrier:MScan`
- request 中已能看到有效点表数量
- 即使 runtime 还未执行，bridge / orchestrator 也已正确流转

### Phase 2: CW MSCAN 进入 core runtime

涉及文件：

- `src/plugins/core/txpipelineruntime.h`
- `src/plugins/core/txpipelineruntime.cpp`
- `src/plugins/core/idevice.h`
- `src/plugins/htra/fancydevice.h`
- `src/plugins/htra/fancydevice.cpp`

任务：

1. 抽象 `startListSweepLocked()`
2. `applySweepCw()` 支持 `MScan`
3. `querySweepWriteback()` 支持 `tx_query_mscan()`

完成标志：

- `SweepCw(MSCAN)` 可在 core-managed 路径下完成配置并重放 cached request

### Phase 3: Playback MSCAN 进入 core runtime

涉及文件：

- `src/plugins/core/idevice.h`
- `src/plugins/core/txpipelineruntime.cpp`
- `src/plugins/htra/fancydevice.h`
- `src/plugins/htra/fancydevice.cpp`

任务：

1. 新增 `startPlaybackListSweep()`
2. `TxPipelineRuntime::canManageRequest()` 放开 `SweepPlayback + MScan`
3. `applySweepPlayback()` 新增 `MScan` Phase 2 分支

完成标志：

- `SweepPlayback(MSCAN)` 与现有 F/L playback 一样支持：
  - payload not ready 的 Phase 1
  - payload ready 后的 Phase 2
  - 设备重开后的 cached request reapply

### Phase 4: Streaming MSCAN 进入 bridged session

涉及文件：

- `src/plugins/core/idevice.h`
- `src/plugins/htra/streamingbussiness.h`
- `src/plugins/htra/streamingbussiness.cpp`
- `src/plugins/htra/fancydevice.h`
- `src/plugins/htra/fancydevice.cpp`

任务：

1. 新增 `startStreamingListSweep()`
2. `StreamingBussiness::workerLoop()` 增加 `CarrierPlanKind::MScan`
3. 保持现有 session model，不改变 sender / generator 线程边界

完成标志：

- `SweepStream(MSCAN)` 能像现有 `SweepStream(FScan/LScan)` 一样，仅通过 bridge request 改变 carrier plan

## 风险与注意点

### 1. `tx_query_mscan()` 需要调用方提供容量

这和 `tx_query_fscan()` / `tx_query_lscan()` 不同，因此 runtime query MScan 时必须使用 request 中的有效点数作为输入容量。

建议：

1. 以 `mscanPoints.size()` 作为 query capacity
2. 若设备返回的实际点数小于请求点数，以返回值为准记录 writeback
3. 日志中明确打印 requested count 和 writeback count

### 2. Playback MSCAN 暂不支持“按点切换 waveform”

当前 provider 模型只提供一个 payload，因此本方案只支持：

- 单个 playback payload
- 叠加 MScan RF plan

若将来需要“多点多 waveform”，那已经不是纯 carrier plan，而是 carrier + waveform sequence 组合，需要单独建模。

### 3. Streaming 继续是 session，不做同步 runtime 化

这不是缺陷，而是当前架构的刻意边界。为了支持 Streaming MSCAN，只需要扩展已有 bridge，不应把 `StreamingDataGenerator` 和 sender thread 硬拆进 `TxPipelineRuntime`。

## 验证建议

### 静态验证

1. `TxOrchestrator` 日志出现 `Carrier:MScan`
2. `TxPipelineBridge` 日志正确区分：
   - `SweepCw(MSCAN)` -> core-managed
   - `SweepPlayback(MSCAN)` -> core-managed
   - `SweepStream(MSCAN)` -> legacy-managed bridge

### 运行验证

1. CW MSCAN
   - 配置多点频率/功率/驻留时间
   - 设备启动后能完成整轮扫描
2. Playback MSCAN
   - provider 未 ready 时只做 Phase 1
   - provider ready 后自动或再次 apply 进入 Phase 2
3. Streaming MSCAN
   - fixed stream -> mscan stream 切换时不重建 business，只在 sender thread 内重配
4. 设备 reopen
   - `SweepCw(MSCAN)` / `SweepPlayback(MSCAN)` 通过 cached request reapply
   - `SweepStream(MSCAN)` 通过现有 streaming session 重配链恢复

## 一句话结论

MSCAN 应当作为 `CarrierPlanKind` 的第三种 sweep 形态统一接入，而不是再造一个“ListMode 独立业务”。

推荐路线是：

1. 先补 `MScan` request 建模和 `StepSweepPanel` 快照职责。
2. 再把 `CW` 与 `Playback` MSCAN 纳入 `TxPipelineRuntime`。
3. 最后沿用现有 bridge，把 `Streaming MSCAN` 接入 `StreamingBussiness` 的 session overlay。

这样改动面最小，和现有主线架构最一致，也不会破坏已经稳定的 Playback / Streaming 边界。