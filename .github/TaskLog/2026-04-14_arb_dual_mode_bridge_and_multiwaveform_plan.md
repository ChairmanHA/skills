# ARB 双态拆分、Bridge 接入与多波形 Playback MSCAN 实施方案

## 目标

分三步把 HTRA ARB 业务收敛到清晰且可维护的形态：

1. **[已完成]** 把 ARB 内部拆成"普通 wav 回放"和"自定义 arb 程序化回放"两态。
2. **[已完成]** 让自定义 arb 走类似 Streaming 的 bridge 路径，但语义上仍归属于 Playback provider。
3. **[已完成]** 在设备层补齐"多 waveform + playback list sweep"能力（sampleRate 暂时统一从 UI 读取）。

## 设计原则

1. 普通 wav 继续向统一 Playback 主线靠拢。
2. 自定义 arb 与外部 StepSweepPanel 解耦，不参与统一 carrier plan 建模。
3. 自定义 arb 不伪装成 Streaming，但执行权走 bridge 模式。
4. runtime 不为兼容自定义 arb 而污染现有单 payload `TxProviderExecutionContext`。
5. 多 waveform playback list sweep 能力下沉到 device / operator / legacy bridge 层。

## 目标形态

ARB 是一个双态 provider：

1. **OrdinaryWav** — 普通 Playback provider，与 AM/FM 等行为一致，支持外部 StepSweepPanel。
2. **ProgrammedArb** — 文件内自带程序化扫描和波形序列的特殊 Playback session，与外部 sweep 解耦，通过 bridge + 多 waveform playback list sweep 落地。

---

## Phase 1：ARB 双态拆分 + MainWindow sweep 互斥 ✅ 已完成

### 实现概述

在 ARB 业务内部引入显式文件模式枚举，基于已解析文件结果判定；MainWindow 根据 provider capability 动态协调 sweep 可用性；OrdinaryWav 通过 `buildPlaybackExecutionContext()` 参与 core runtime Playback 主线。

### 改动文件与内容

#### `src/plugins/htra/arbdatagenerator.h`

- 新增 `ArbFileMode` 枚举（`None` / `OrdinaryWav` / `ProgrammedArb`），定义在 `Arb` namespace 中
- `ArbDataGenerator` 新增：
  - `ArbFileMode arbFileMode() const` — 线程安全的只读 getter
  - `void arbFileModeChanged(Arb::ArbFileMode)` — 模式变化信号
  - `ArbFileMode m_arbFileMode` — 成员变量

#### `src/plugins/htra/arbdatagenerator.cpp`

- `handleFile()` 中基于 `readREPChunk()` 结果判定模式：
  - `sections.empty()` → `OrdinaryWav`
  - `!sections.empty()` → `ProgrammedArb`
  - 空文件名 → `None`
  - 模式变化时 emit `arbFileModeChanged`
- `restoreSettings()` 中增加：
  - `m_fileChanged = true`（修复了原先 restore 不触发文件重读的问题）
  - 主动检测文件模式并 emit 信号，确保从 JSON 恢复后模式正确
- `arbFileMode()` getter 实现（锁保护读取 `m_arbFileMode`）

#### `src/plugins/htra/arbmodulation.h`

- `providerCapabilities()` 从内联常量改为声明式（实现移入 cpp）
- 新增 `ArbFileMode arbFileMode() const`
- 新增 `buildPlaybackExecutionContext()` override
- `mutex` 改为 `mutable` 以支持 const 方法中的锁

#### `src/plugins/htra/arbmodulation.cpp`

- **`providerCapabilities()`** 动态返回：
  - `ProgrammedArb` → `{true, false, false}`（不支持外部 sweep）
  - `OrdinaryWav / None` → `{true, true, false}`（与普通 Playback 一致）
- **`buildPlaybackExecutionContext()`**：
  - `OrdinaryWav`：`providerParticipating = true`，提供单波形 payload（sampleRate + IQ data）给 core runtime
  - `ProgrammedArb`：`providerParticipating = false`，不参与 core runtime
  - `None`：`providerParticipating = false`
- **`onArbGeneratorStatusChanged()`**：新增 `emit providerExecutionContextChanged()` 驱动 orchestrator 感知数据就绪变化
- **构造函数**：连接 `arbFileModeChanged` → 日志输出 + `providerExecutionContextChanged()`

#### `src/plugins/core/mainwindow.cpp`

- **`updateSweepAndBusinessAvailability()`**：
  - 检查当前选中 business 的 `providerKind()` 和 `providerSupportsSweepCarrier()`
  - 当选中的 Playback provider 不支持 sweep（即 ProgrammedArb）时：
    - 通过 `QScopedValueRollback<bool>(m_suspendingPipelineUpdates, true)` 事务化关闭 sweep 使能，防止中间态 orchestrator 抖动
    - 调用 `m_commonPanel->setSweepButtonEnabled(false)` 禁用 sweep 按钮
  - 其他情况：sweep 按钮保持可用
- **`providerExecutionContextChanged` 连接**：
  - 改为同时调用 `updateSweepAndBusinessAvailability()` + `updateOrchestrator()`
  - 确保文件模式切换时 sweep 可用性同步更新

### 关键机制

1. **MainWindow 不依赖 ARB 头文件**：仅通过 `IBusiness::providerSupportsSweepCarrier()` 和 `providerKind()` 判断，不需要 include ARB 特有类型。
2. **事务化 sweep 关闭**：使用 `QScopedValueRollback` 挂起 `m_suspendingPipelineUpdates`，避免 `setBtnEnabledChecked(false)` → `enabledChanged` → `updateOrchestrator` 的中间态。
3. **Orchestrator 自然失效**：即使 MainWindow 没有关闭 sweep，`TxOrchestrator::resolve()` 也会因 `!capabilities.supportsSweepCarrier` 将 pipeline 解析为 `Mute`，形成双重保护。

### 已验证行为

1. 普通 wav + FixedPlayback：正常播放 ✅
2. 普通 wav + 外部 Sweep：sweep 正常参与 ✅
3. .arb 文件被文件类型过滤阻止加载（MSCAN 底层 API 尚未实现，暂不暴露）✅
4. 模式切换不产生 pipeline 抖动日志 ✅

---

## Phase 2：ProgrammedArb bridge request 缓存化 ✅ 已完成

> 前置条件：Phase 1 已完成。

### 实现概述

ProgrammedArb 通过 `setBridgedApplyRequest()` 接收 MainWindow 的 `TxApplyRequest` 桥接快照，只缓存 `context.common` 用于设备配置。MainWindow 的 `applyResolvedPipeline()` 识别 "Playback bridge provider"（`providerParticipating == false`）并走与 Streaming 相同的 bridge 路径。

### 改动文件与内容

#### `src/plugins/htra/arbmodulation.h`

- 新增 `setBridgedApplyRequest()` override
- 新增 `fillDeviceProfileFromBridgedRequest()` 私有方法
- 新增成员：`std::mutex m_bridgeMutex`、`TxCommonSettings m_bridgedCommon`、`std::atomic<bool> m_hasBridgedRequest`
- 增加 `<atomic>`、`<mutex>`、`<core/txpipelinestate.h>` include

#### `src/plugins/htra/arbmodulation.cpp`

- **`setBridgedApplyRequest()`**：锁保护缓存 `request.context.common`，设置 `m_hasBridgedRequest = true`，已激活时触发 `onDeviceProfileChanged()`
- **`fillDeviceProfileFromBridgedRequest()`**：与 `TxPipelineRuntime::fillDeviceProfile()` 逻辑一致，从 `m_bridgedCommon` 填充 `IDevice::Profile`
- **`configurationDevice()`** 双路径：
  - ProgrammedArb + 有 bridge → `fillDeviceProfileFromBridgedRequest()`
  - OrdinaryWav / None → `getCurrentProfile()`（传统回读 UI）

#### `src/plugins/core/mainwindow.cpp`

- **`applyResolvedPipeline()`** legacy-managed 分支重构：
  - 引入 `isPlaybackBridgeProvider` 判定：`FixedPlayback/SweepPlayback` + `!providerParticipating`
  - 合并为统一的 `isBridgeProvider = isStreamingPipeline || isPlaybackBridgeProvider`
  - bridge provider 在 requestChanged 时调用 `setBridgedApplyRequest(request)`
  - bridge provider 已激活时不 terminate/reactivate，只更新 request
  - 首次激活时在 `active()` 前发送 bridge request（确保 `startBusiness()` 能读到缓存）

### 关键机制

1. **MainWindow 不引入新的 ARB 判定逻辑**：仅依赖已有的 `providerParticipating` 标志，不需要知道 ArbFileMode
2. **bridge 路径与 Streaming 一致**：`requestChanged → setBridgedApplyRequest()` + 已激活时 skip terminate/reactivate
3. **首次激活时预填充 bridge**：`active()` 前调用 `setBridgedApplyRequest()`，确保设备线程启动时 `m_hasBridgedRequest == true`
4. **ArbModulationOnly 执行态只服务 ProgrammedArb**：OrdinaryWav 仅作为 data provider 参与 core-managed Playback runtime，不再在本业务线程里做 legacy 配置和启动

---

## Phase 3：多 waveform Playback MSCAN 通路 ✅ 已完成

> 前置条件：Phase 2 已完成 + 底层 MSCAN API (`tx_config_mscan` / 多 waveform `tx_config_playback`) 就绪。

### 实现概述

sampleRate 暂时和普通 wav 一样，从界面读取，不从文件读取。后续如有需求再改。

DeviceOperator 新增 `startPlaybackListSweep()` wrapper；FancyDevice 的 `startListSweepLocked()` Playback 分支从单 waveform 改为多 waveform；`downloadDataAndStart()` 实现双路径下载+启动。

### 改动文件与内容

#### `src/plugins/core/deviceoperator.h`

- 新增 `startPlaybackListSweep(startSeqNum, endSeqNum, points, repeat, errorString)` 方法及文档注释

#### `src/plugins/core/deviceoperator.cpp`

- 新增 `startPlaybackListSweep()` 实现：isActive 检查 → 获取设备 → 委托 `IDevice::startPlaybackListSweep()`

#### `src/plugins/htra/fancydevice.cpp`

- **`startListSweepLocked()` Playback 分支重写**：
  - 校验 `waveformCount == points.size()`
  - 从 `m_waveformCache[startSeqNum..endSeqNum]` 收集 `waveformIds[]`
  - `waveformRepeats[]` 全部设为 -1（由设备自行管理循环）
  - `waveformSampleRates[]` 全部使用 `m_sampleRate`（UI 统一采样率）
  - 调用 `tx_config_playback(&channels[0], ids, repeats, rates, count)` 多 waveform 配置

#### `src/plugins/htra/arbmodulation.cpp`

- **`downloadDataAndStart()` 完整实现**：
  - **OrdinaryWav 路径**：`downloadDataSeq(data, samples, sampleRate, seqNum=0)` + `triggerStart(-1, 0, 0)`（单波形回放）
  - **ProgrammedArb 路径**：
    1. 循环下载每段 waveform 为 seqNum 0,1,2...
    2. 构建 `QVector<TxMScanPoint>`：从每段 `dataForDownload` 提取 `freq/powdBm`，`dwellTime = (sampleCount / sampleRate) * repetition`
    3. 调用 `startPlaybackListSweep(0, segmentCount-1, points, -1)` 启动多波形 MSCAN

### 关键机制

1. **点表与 waveform 一一对应**：`points.size()` 必须等于 `endSeqNum - startSeqNum + 1`，不匹配则报错
2. **dwellTime 推导**：`dwellTime = waveformDuration × repetition`，其中 `waveformDuration = sampleCount / sampleRate`
3. **playbackRepeat = -1**：由设备自行管理循环，停留时间由 `dwellTime` 控制
4. **统一 sampleRate**：目前所有 waveform 使用 UI 侧同一 sampleRate，后续可扩展为 per-segment
5. **普通 wav 不复用这条执行通路**：multi-waveform MSCAN 仅属于 ProgrammedArb，OrdinaryWav 继续留在 core-managed 单 payload 播放模型中

### 三层语义模型（ProgrammedArb 内部结构）

1. **波形层** `ArbWaveformDescriptor` — `iqData` / `sampleRate` / `playbackRepeat`
2. **点表层** `ArbProgramPoint` — `frequency` / `level` / `dwellTime` / `waveformIndex`
3. **会话层** `ProgrammedArbDescriptor` — `waveforms[]` / `points[]`

---

## 架构约束（整个方案期间不可违反）

1. **不要把 ProgrammedArb 塞回 StepSweepPanel** — 扫描程序来自文件内部。
2. **不要污染 TxProviderExecutionContext** — 保持单 payload 模型干净。
3. **不要让 MainWindow 承担 ARB 文件语义解析** — 只知道 mode，不知道 rep/hop 细节。
4. **RF / MOD 自动拉起只在显式启用时** — 不在文件加载时触发。
5. **ProgrammedArb reopen 依赖 cached request** — 不重新从 UI 组装。
# ARB 双态拆分、Bridge 接入与多波形 Playback MSCAN 实施方案

## 目标

围绕当前仓库已经建立的主线架构，分三步把 HTRA ARB 业务收敛到清晰且可维护的形态：

1. 把 ARB 内部拆成“普通 wav 回放”和“自定义 arb 程序化回放”两态。
2. 让自定义 arb 走类似 Streaming 的 bridge 路径，但语义上仍归属于 Playback provider。
3. 在设备层补齐“多 waveform + playback list sweep”能力，把 H2 API 示例中的设备语义完整映射到 SGStudio。

本方案只做实施设计，不修改代码。

## 当前基线

### 已有主线边界

当前主线已经稳定在以下边界：

1. `TxOrchestrator` 只做裁决。
2. `MainWindow::buildApplyRequest()` 负责快照执行参数。
3. `TxPipelineRuntime` 只消费 request，不再回读 UI / business 内部状态。
4. `StepSweepPanel` 是统一的外部 sweep UI 边界。
5. `Streaming` 仍保持 bridge session 模型，不强行塞进同步 runtime。

### 与 ARB 直接相关的现状

1. `ArbModulationOnly` 当前仍是 legacy business 风格，内部自己持有 `ArbDataGenerator`、自己配置设备、自己下载数据。
2. ARB 代码仍在当前 CMake 工程中编译，但插件入口当前没有重新注册 `ArbModulationOnly`。
3. 普通 wav 最终会收敛成单一 `sampleRate + IQ payload`，天然适合当前 `TxProviderExecutionContext`。
4. 当前自定义 arb 会生成 `std::vector<Utils::dataForDownload>`，但这个结构目前只显式携带：
   - 独立 waveform 数据
   - 文件展开阶段使用的 `repetition`
   - 关联的 `freq`
   - 关联的 `powdBm`
   它还不能清晰表达：
   - 每段 waveform 自己的 `sampleRate`但是如果统一采样率, 目前的接口针对单一samplerate其实是足够的。
   - 设备侧 `tx_config_playback()` 使用的 `repeat[]`
   - 点位停留时间 `dwell[]`
5. 当前 `TxProviderExecutionContext` 只有：
   - `sampleRate`
   - `playbackIqInterleaved`
   - `dataReady`
   - 其他 provider 元信息
   它只能表达“单波形 Playback”，不能表达“多 waveform + 点表关联”。
6. 当前 `FancyDevice::downloadDataWithRFCmd()` 已能下载多 waveform 并缓存 waveformId，但 `startListSweepLocked()` 在 Playback 模式下仍限制为单 waveform。
7. 当前 `DeviceOperator` 只暴露了 streaming sweep bridge 接口，没有 playback sweep bridge 接口。

### 关键矛盾

当前 ARB 实际上混合了两种完全不同的执行语义：

1. 普通 wav：本质是“单一 Playback payload”。
2. 自定义 arb：本质是“文件内自带程序化波形序列和点表语义的 Playback session”。

它们不应该继续共享同一套执行模型。

## 总体设计结论

### 设计原则

1. 普通 wav 继续向统一 Playback 主线靠拢。
2. 自定义 arb 与外部 StepSweepPanel 解耦，不参与统一 carrier plan 建模。
3. 自定义 arb 不伪装成 Streaming，但执行权走 bridge 模式。
4. runtime 不为兼容自定义 arb 而污染现有单 payload `TxProviderExecutionContext`。
5. 多 waveform playback list sweep 能力下沉到 device / operator / legacy bridge 层，不强行塞进当前同步 runtime。

### 目标形态

最终 ARB 应该变成一个双态 provider：

1. `WavPlaybackMode`
   - 输入：普通 wav
   - 裁决语义：普通 Playback provider
   - 执行路径：优先接入现有 `FixedPlayback`
   - Sweep 语义：只允许使用外部 StepSweepPanel；如果用户打开 sweep，则行为与 AM / FM 等普通 Playback 一致

2. `ProgrammedArbMode`
   - 输入：带 `rep/hop` chunk 的自定义 arb
   - 裁决语义：Playback provider，但不参与 StepSweepPanel sweep 裁决
   - 执行路径：legacy-managed bridge
   - Sweep 语义：点表来自 arb 文件内部；进入该模式时应主动关闭外部 sweep 使能

### 结构修正（基于 H2 MSCAN + Playback 示例）

结合 H2 API 和示例程序，自定义 arb 的执行态不应继续沿用“单一 `sampleRate + repetition`”的扁平模型，而应拆成三层语义：

1. 波形层 `ArbWaveformDescriptor`
   - `iqData`
   - `sampleRate`
   - `playbackRepeat`
   - 语义：对应一次 `tx_config_playback()` 中的一个 waveform 槽位
2. 点表层 `ArbProgramPoint`
   - `frequency`
   - `level`
   - `dwellTime` 暂时按照samplerate和波形时长推导，未来可考虑暴露给作者输入
   - `waveformIndex`
   - 语义：对应一次 `tx_config_mscan()` 中的一个扫描点
3. 会话层 `ProgrammedArbDescriptor`
   - `waveforms[]`
   - `points[]`
   - 语义：一次完整的“Playback + MSCAN”程序

这里需要明确区分三个容易混淆的字段：

1. 文件展开阶段的 `repetition`
   - 来自 `rep` chunk
   - 用于表达作者想要的逻辑重复次数
   - 本质是 authoring / compile-time 语义
2. 设备 playback 阶段的 `playbackRepeat`
   - 对应 `tx_config_playback()` 的 `repeat[]`
   - 本质是 runtime 的波形循环次数
3. 扫描点位的 `dwellTime`
   - 对应 `tx_config_mscan()` 的 `dwell[]`
   - 本质是 RF 停留时间

因此，“各段波形有自己的 sampleRate”这个判断是正确的；但“重复次数可以直接变成 `#sym:dwell`”并不完全正确。更准确的说法是：

1. `dwellTime` 不应取代 `playbackRepeat`，因为两者职责不同。
2. 在示例语义下，`playbackRepeat` 完全可以统一设为 `-1`，由 `dwellTime` 决定每个点停留多久。
3. 如果未来编辑器想提供 `#sym:dwell` 这类更高层输入，它应作为 authoring 字段，在 apply 前编译为：
   - waveform 长度
   - waveform sampleRate
   - 设备 `playbackRepeat`
   而不是直接替代 `dwellTime`。

同时还要纳入设备 RAM 有限这一条新的设计优先级：

1. 如果某段逻辑 repetition 可以通过 `tx_config_playback()` 的 `repeat[]` 无损表达，就不应优先展开成更多 IQ 数据。
2. ProgrammedArb 的默认目标应是“最小下载、最大设备侧复用”，而不是“先完全展开再上传”。
3. 这意味着 `rep` chunk 中的 repetition，在后续 ProgrammedArb 设计里不应默认再解释为“必须展开后的字节数”。

## Phase 1：ARB 双态拆分 + MainWindow sweep 互斥

    已经完成

## Phase 2：ArbModulationOnly bridge request 缓存化

### 目标

让自定义 arb 不再通过 legacy business 自己散读外部状态，而是像 Streaming 一样接收 `TxApplyRequest` 的桥接快照，只消费 common snapshot。

### 设计要点

#### 1. 只 bridge common，不 bridge 外部 carrier

ProgrammedArb 的 carrier plan 来源是文件内部，不是 `StepSweepPanel`。因此其 bridge request 的使用范围必须收敛到：

1. `context.common`
2. provider 自身的文件和数据状态

不应去消费：

1. `context.carrier`
2. `StepSweepPanel` 当前状态
3. `CommonDeviceProfile` 之外的其他外部对象

#### 2. ArbModulationOnly 增加 request cache

建议沿用 Streaming bridge 的套路，在 `ArbModulationOnly` 内新增：

1. 最近一次桥接的 `TxApplyRequest`
2. 一个“是否已有 bridge request”的标记
3. 一个解析后的 `TxCommonSettings` 快照缓存

之后它的设备配置入口改为优先使用缓存 request，而不是 `getCurrentProfile()` 临时回读。

#### 3. MainWindow 路由策略

建议把 ProgrammedArb 明确定义为“legacy-managed Playback bridge provider”。

也就是说：

1. orchestrator 仍 resolve 到 `FixedPlayback`
2. `buildPlaybackExecutionContext()` 对 ProgrammedArb 不参与当前 core runtime payload 模型
3. `TxPipelineRuntime::canManageRequest()` 返回 false
4. `MainWindow::applyResolvedPipeline()` 像处理 streaming 那样，把 request bridge 给 ARB business，然后走 legacy activation

这样有几个好处：

1. 不需要污染当前 `TxProviderExecutionContext` 单 payload 模型
2. 不需要让 runtime 为多 waveform arb 特判
3. reopen / reapply 的行为也能通过 cached request 重新进入 ARB bridge session

#### 4. ArbModulationOnly 的执行阶段拆分

建议把 ProgrammedArb 执行也收敛为两阶段：

1. Phase 1：使用 bridged common request 配置设备公共项和触发项
2. Phase 2：在数据 ready 后下载多 waveform，并按 `waveforms[] + points[]` 启动文件内定义的 playback list sweep

普通 wav 则可以继续保留原有 legacy 行为，或在后续再迁到 core runtime。Phase 2 这个阶段先只针对 ProgrammedArb bridge 收敛。

这里建议把 ProgrammedArb 的 provider 编译流程显式拆为：

1. `authoring normalize`
   - 解析文件中的逻辑 repetition、窗口、hop 分组，假设文件中已经有了波形的sampleRate
2. `waveform compile`
   - 尽量生成最小基础 waveform 列表
3. `repeat fold`
   - 把可折叠的逻辑 repetition 映射为 `playbackRepeat[]`
4. `timing resolve`
   - 根据 `sampleRate`、waveform 时长和业务需求生成或校验 `dwellTime[]`
5. `device emit`
   - 生成 `tx_config_playback()` 和 `tx_config_mscan()` 所需的最终数组

#### 5. providerExecutionContextChanged 的职责

对 ProgrammedArb，`providerExecutionContextChanged()` 不再用于向 core runtime 提供 payload，而更多用于触发 MainWindow 重新 resolve / bridge / apply。也就是说：

1. 文件变化
2. 数据 ready 变化
3. ARB mode 变化

都应该能驱动 MainWindow 重新进入 `buildApplyRequest()` 和 `applyResolvedPipeline()`。

### Phase 2 需要改动的文件

1. `src/plugins/htra/arbmodulation.h/.cpp`
   - 新增 `setBridgedApplyRequest()`
   - 新增 request cache
   - 把设备配置从外部散读改成消费 bridged common snapshot
2. `src/plugins/core/mainwindow.cpp`
   - 为 ProgrammedArb 补齐类似 Streaming 的 bridge 路由
   - 只在 ProgrammedArb 模式下走 legacy-managed bridge
3. 如有必要，`src/plugins/core/ibusiness.h`
   - 复用已有 `setBridgedApplyRequest()`，无需新接口

### Phase 2 验收标准

1. ProgrammedArb 在 apply / reapply 时不再回头读 `StepSweepPanel`。
2. 设备重开后的重放行为来自 cached request，而不是 UI 散读。
3. ProgrammedArb 的公共设备参数变化可以通过 bridge request 正常生效。
4. 普通 wav 路径不被这次 bridge 化改坏。

## Phase 3：DeviceOperator + FancyDevice 多 waveform playback list sweep 通路

### 目标

把 H2 API 示例里的语义完整映射到 SGStudio：

1. `tx_config_mscan()` 配置点表
2. `tx_config_playback()` 配置多个 waveform id、repeat、sampleRate
3. `tx_config_output(STATE_ON, STATE_ON)` 打开 RF 和 MOD
4. `channel_start()` + `channel_trigger_bus()` 启动整个循环扫描

### 当前缺口

当前设备层虽然已经能：

1. 下载多 waveform 到设备
2. 缓存每个 seq 对应的 waveformId
3. 启动单 waveform 的 playback list sweep

但还不能把“点表数组”和“waveform 数组”一一对应起来。

### 设计要点

#### 1. 先扩展 operator 接口，而不是让 ARB 直接下钻到 IDevice

建议在 `DeviceOperator` 中新增 playback sweep wrapper，保持 ARB 和 Streaming 一样都通过 operator 层访问设备：

1. `startPlaybackFrequencySweep(...)`
2. `startPlaybackLevelSweep(...)`
3. `startPlaybackListSweep(...)`

虽然当前 ARB 核心目标是 list sweep，但三组接口保持对称性更利于后续维护。

#### 2. 设备接口需要支持“序号范围”之外的显式多 waveform 语义

当前 `startPlaybackListSweep(startSeqNum, endSeqNum, points, repeat)` 这个签名默认假设：

1. waveform 来自缓存中的连续序号段
2. 但最终只支持单 waveform

要完整映射示例语义，有两种做法：

1. 最小改动方案：
   - 保持 `startSeqNum/endSeqNum`
   - 约定 `points.size()` 必须等于 `endSeqNum - startSeqNum + 1`
   - `FancyDevice` 从缓存中按序取出对应 waveformId 列表
2. 彻底显式方案：
   - 新增 `QVector<uint32_t> waveformSeqNums`
   - API 显式表达“每个点对应哪个 waveform sequence”

本轮建议先采用最小改动方案，因为 ARB 的 `downloadDataWithRFCmd()` 本身已经按顺序把所有段落放进 cache，天然适合“连续序号段”和点表一一对应。

但需要补一条执行约束：

1. 若多个逻辑 repetition 已经被折叠为同一个 waveform + `playbackRepeat`，则 `points[]` 不应再机械要求与“展开前的 rep 次数”一一对应。
2. `points[]` 只需要和最终 device-visible waveform 槽位数量对齐。

#### 3. FancyDevice::startListSweepLocked 要拆成三种 baseband 分支

当前已经有：

1. CW branch
2. Streaming branch
3. Playback branch

但 Playback branch 目前只允许单 waveform。应改为：

1. 检查点表非空
2. 检查 `points.size()` 与 waveform 段数匹配
3. 根据 `startSeqNum .. endSeqNum` 从 `m_waveformCache` 收集：
   - `waveformId[]`
   - `repeat[]`
   - `sampleRate[]`
4. 调用 `tx_config_playback(..., n)`，其中 `n = 点数/波形数`
5. 调用 `tx_config_output(channels, STATE_ON, STATE_ON)`
6. `channel_start()`
7. `channel_trigger_bus()`

这才是真正对应示例里的执行语义。

#### 4. 点表与 waveform 的配对规则

需要在实现前明确规则，建议固定为：

1. 一个 `TxMScanPoint` 对应一个 waveform 段
2. 顺序按 `downloadDataWithRFCmd()` 产生的 `dataForDownload` 顺序对齐
3. 若点数与 waveform 数不一致，则直接报错，不做隐式补齐或截断

这样规则简单、可解释、易调试。

这里的“waveform 段”应理解为“最终下发到设备 RAM 的 waveform 槽位”，而不是“作者输入文件中的每个 rep 展开片段”。

#### 5. sampleRate / repeat / dwell 语义修正

根据示例与 H2 API，ProgrammedArb 的设备执行态应明确支持“每段 waveform 自己的 sampleRate”。因此本轮设计需要修正为：

1. `tx_config_playback()` 的 `sample_rate[]` 应从 `waveforms[]` 逐段提供，而不是复用一个全局 `Arb_SampleRate`。
2. `playbackRepeat[]` 应是每段 waveform 的独立运行时循环次数，而且在 RAM 受限的发射机上，它应优先承担“逻辑 repetition 映射”的职责。
3. `dwell[]` 继续只表达每个扫描点的停留时间，不承载 waveform 循环语义；虽然可以输入，但是目前完全依靠 `sampleRate + waveformDuration + playbackRepeat` 推导或校验。

这意味着后续 ProgrammedArb provider 的内部结构，应该优先把 `sampleRate` 和 `playbackRepeat` 绑定到 waveform，而不是绑定到整个文件或整个点表。

### Phase 3 需要改动的文件

1. `src/plugins/core/deviceoperator.h/.cpp`
   - 新增 playback sweep wrapper
2. `src/plugins/core/idevice.h`
   - 若现有签名不足以表达多 waveform 对齐，需要扩展注释或签名约束
3. `src/plugins/htra/fancydevice.h/.cpp`
   - 改造 playback list sweep 分支，支持多 waveform
   - 校验 point-count 与 waveform-count 一致
4. `src/plugins/htra/arbmodulation.cpp`
   - bridge 执行阶段改为通过 operator 走新的 playback list sweep 通路

### Phase 3 验收标准

1. ProgrammedArb 能把文件生成的多 waveform 全部下载到设备。
2. Playback MSCAN 启动时使用的是多 waveform `tx_config_playback(..., n)`，不是单 waveform 退化路径。
3. `tx_config_output` 在 Playback MSCAN 下明确使用 `RF ON + MOD ON`。
4. 点表与 waveform 数量不匹配时，日志和错误信息清晰可见。
5. 多 waveform 各自的 `sampleRate[]` 和 `playbackRepeat[]` 被正确送入 `tx_config_playback()`。
6. 相同内容的逻辑 repetition 被优先折叠为 API `repeat[]`，而不是先展开占满设备 RAM。


## 风险与注意事项

### 1. 不要把 ProgrammedArb 塞回 StepSweepPanel

这是本轮最重要的架构约束。ProgrammedArb 的扫描程序来自文件内部，外部 StepSweepPanel 只会制造双重真相源。

### 2. 不要为了兼容 ProgrammedArb 污染当前 TxProviderExecutionContext

当前 context 对普通 Playback 很干净，没必要为了一个特殊 legacy provider 加入多 waveform / 点表 / 文件内 hop overlay 等复杂结构。

### 3. 不要让 MainWindow 承担 ARB 文件语义解析

MainWindow 只应知道“这个 business 当前是 OrdinaryWav 还是 ProgrammedArb”，不应知道 rep/hop chunk 的细节。

### 4. 注意 RF / MOD 自动拉起时机

自动打开 RF / MOD 只能发生在显式启用阶段，不能在文件加载、参数编辑等纯编辑态动作中触发。

### 5. 设备 reopen 语义要和 bridge request 保持一致

ProgrammedArb 一旦 bridge 化，就必须保证 reopen reapply 依赖 cached request，而不是重新从 UI 组装执行态。

## 验证建议

### 静态验证

1. 普通 wav 和自定义 arb 的 mode 识别是否唯一且稳定。
2. `updateSweepAndBusinessAvailability()` 是否只做 UI/互斥，不误触发设备操作。
3. ProgrammedArb bridge 后，代码中是否仍残留外部 sweep 读取路径。
4. `sampleRate / playbackRepeat / dwellTime` 三种语义是否已经彻底分离，没有再共用同一个字段名或状态。

### 联机验证

建议至少覆盖以下场景：

1. 普通 wav + FixedPlayback
2. 普通 wav + 外部 FScan
3. 普通 wav + 外部 MScan
4. 自定义 arb + 进入 ARB 模式时自动关闭 sweep
5. 自定义 arb + 多 waveform playback list sweep 启动
6. 自定义 arb + 设备断开后 reopen reapply
7. 自定义 arb + 不同 waveform 使用不同 sampleRate 的点表执行

### 日志建议

建议新增或统一以下日志口径：

1. `[ArbMode] mode=OrdinaryWav / ProgrammedArb`
2. `[ArbBridge] apply cached common request ...`
3. `[ArbBridge] playback list sweep points=%d waveforms=%d`
4. `[FancyDevice] tx_config_playback(n=%d) for playback mscan`

## 一句话结论

本轮不应该把 ARB 继续当成“一个特殊一点的 Playback”。正确做法是：

1. 普通 wav 归并到统一 Playback 主线。
2. 自定义 arb 作为“文件内自带程序化扫描和波形序列的特殊 Playback session”，与外部 sweep 解耦，并通过 bridge + 多 waveform + per-waveform sampleRate 的 playback list sweep 落地。