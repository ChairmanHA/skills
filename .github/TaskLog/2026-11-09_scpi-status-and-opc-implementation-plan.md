# SCPI 改进分阶段实施方案：IEEE 488.2 状态模型与真实 `*OPC?`/`*WAI`

- 日期：2026-11-09
- 来源：[测试机制分析 §10](2026-11-09_test-automation-analysis.md)；[2026-09-08 OPC/WAI 评审草案](2026-09-08_scpi-operation-completion-design.md)；[2026-09-17 移除 SCPI worker](2026-09-17_remove-scpi-worker-and-batch-dialog-log.md)；KB [SCPI 接入审阅](../KnowledgeBase/scpi_review_and_integration_plan.md) §2/§3。
- 状态：**方案，未实施**。本文件为 static 分析，各阶段的验证级别分别注明。

## 0. 已确认决策与默认假设

| 编号 | 内容 | 状态 |
| --- | --- | --- |
| D1 | `*IDN?` 保持现有 `DeviceID:...,FPGA:...` 格式，不改为 IEEE 488.2 的 4 字段 | **用户已确认** |
| D2 | 无可用设备（且没有设备切换在进行）时，`*OPC?` 立即返回 1，`*WAI` 立即放行，`*OPC` 立即置 ESR OPC；同时置 `STAT:QUES` 自定义位 9 “NotConnected” | **用户已确认** |
| D3 | 异步失败不区分来源（SCPI/UI/Minibar），只要 SCPI 服务在运行就进入错误队列。依据：所有连接共用一个设备级错误队列 | 默认，待确认 |
| D4 | 保持单一解析器 + 全局 FIFO；屏障等待期间所有连接的命令都会排队。文档写明这是全局串行限制 | 默认，待确认 |
| D5 | 完成判定 = 屏障之后 Core 第一次进入“终态空闲”。不按命令来源追踪“被 UI 新意图替换”，也不为此单独报错。这是对 09-08 草案“替换即结算为中断并报错”的简化，见 §2.3 | 默认，待确认 |
| D6 | `*ERR?` 的格式与行为保持不变，作为兼容别名；新增标准 `SYST:ERR?` | 默认 |
| D7 | 错误队列容量从 10 扩到 32（`parser.cpp kScpiErrorQueueData`），以容纳异步错误 | 默认，待确认 |
| D8 | 未连接时 `*IDN?` 仍不响应（handler 返回空，libscpi 推入 -200），保持现状 | 默认，可另议 |
| D9 | `*OPC?` 只保证“SGStudio 已完成且 H2 API 调用全部返回”，**不保证 RF 已稳定**。h2_api 没有稳定或完成的确认接口，需向厂商索取 | 写入手册 |

与旧文档不一致的地方（均为观察到的事实）：

- 09-08 草案假设有一个 SCPI worker 线程在等待 ticket。09-17 已移除 worker，`Parser::pushCommand` 在 GUI 线程同步执行（`parser.cpp:104` 注释），因此本方案改为**主线程暂停 FIFO 并延迟应答**。
- KB 审阅 §4 写的是“`*RST` 可能被模态框阻塞”。但当前 `MainWindow::preset()`（`mainwindow.cpp:1437`）没有模态确认框，只会同步调用 `device->resetDevice()`，并可能引发轮询器观察到的短暂断连。该节在 Phase 7 更新。

## 1. 目标契约（写入用户手册）

1. **重叠命令**：所有设置命令按 IEEE 488.2 的 overlapped 命令处理。命令被接受即返回，设备执行是异步的。
2. **pending operation**：在当前可用设备上，已接受但还没有到达终态的操作。终态包括成功、失败、被设备断开终止。
3. `*WAI`：等 pending 清空后才执行后续命令，没有响应。
4. `*OPC?`：pending 清空后输出 `1`。**同一程序消息的全部响应在最后一起返回**，格式与现在完全相同。
5. `*OPC`：不阻塞；pending 清空时置 ESR bit0。`*CLS`、`*RST` 会取消已挂起的 `*OPC`。
6. **失败也算完成**：失败照样结束等待，失败原因进入错误队列，ESR 的 EXE/DDE 位自动置位。服务端不设超时，等待上限由客户端超时决定。
7. **无设备**：按 D2 处理。
8. **覆盖范围**：Phase 3 起覆盖 core-managed 管线（Mute/FixedCw/SweepCw/FixedPlayback/SweepPlayback，含 F/L/MScan）、配置加载和设备切换；Streaming 与 Playback 桥接 provider 在 Phase 5 之前只能等到 Core 侧完成，手册中明确写出这一点。
9. **连续运行**：连续扫描和 Streaming 的“完成”表示“配置已下发并启动”，不表示一轮结束。

## 2. 架构与职责

| 层 | 负责 | 不负责 |
| --- | --- | --- |
| `src/libs/scpi`（协议层） | IEEE 488.2 寄存器与公共命令、`SYST:ERR`、`STATus` 子系统、程序消息切分、屏障挂起/恢复、`*OPC` 挂起状态 | 不判断业务何时完成，不依赖 Core |
| `src/plugins/core`（TxSessionService 为所有者） | 唯一定义“pending 原因”和“异步失败”，从现有权威字段推导，不复制状态 | 不知道 SCPI 错误码和寄存器位 |
| `src/plugins/scpi`（适配层） | 把 Core 信号转成 `ScpiServer` 调用；设备状态映射到 QUES/OPER 位；`*OPT?` 等产品命令 | 不写业务规则 |
| `src/plugins/htra` 中的 Streaming（provider） | Phase 5 提供桥接执行状态 | — |

### 2.1 新增 `ScpiServer` 接口（草案，主线程调用）

```cpp
// Product-defined pending source for *OPC/*OPC?/*WAI. Must already reflect an
// intent accepted by a setter when that setter's handler returns.
void setPendingOperationSource(std::function<bool()> hasPendingOperations);
// Call whenever the pending state may have changed. Resumes barriers through a
// queued continuation and completes an armed *OPC; never parses re-entrantly.
void notifyOperationStateChanged();
// Condition registers; events latch on positive transitions (libscpi default).
void setQuestionableCondition(quint16 mask, quint16 bits);
void setOperationCondition(quint16 mask, quint16 bits);
// Asynchronous error into the shared queue; ESR follows libscpi's code ranges.
void pushError(int code, const std::string &info);
// Product action for *RST; the protocol layer cancels *OPC state first.
void setResetHandler(ContextCallback handler);
```

`*CLS`、`*WAI`、`*RST`、`*OPC`、`*OPC?` 改由协议层注册，`ScpiRegister::init()` 删除对应注册。原因：产品条目先于 `Parser::init()` 注册，模式冲突时产品条目会先命中，所以两边不能同时注册。

### 2.2 Core 新增（草案）

```cpp
// txsessionservice.h
enum class TxPendingReason : quint32 {
    RefreshDirty        = 1u << 0, // accepted intent not yet built into a request
    ProfileTransaction  = 1u << 1, // load/preset pipeline-update suspension
    DeviceSwitch        = 1u << 2,
    CapabilityReconcile = 1u << 3,
    ApplyActive         = 1u << 4, // in-flight, queued latest or desired snapshot pending
    PayloadPending      = 1u << 5, // provider generating or payload materialization
    MScanApplyPending   = 1u << 6, // submitted revision not yet applied
    LegacyTransition    = 1u << 7,
    BridgeProvider      = 1u << 8, // Phase 5
};
Q_DECLARE_FLAGS(TxPendingReasons, TxPendingReason)

// Main thread. Empty when no usable device and no switch is in progress (D2).
// Derived on demand from authoritative fields; reflects an accepted intent
// before the call that accepted it returns.
TxPendingReasons pendingOperationReasons() const;

signals:
    void operationStateChanged();
    void operationFailed(Core::TxOperationFailureKind kind, const QString &message);
```

`TxOperationFailureKind { DeviceApply, CarrierPlan, Payload, Stream }` 只用来区分映射到哪个 SCPI 错误码，Core 本身不出现 SCPI 码。

### 2.3 为什么不用 ticket 追踪“被替换”（D5）

- SGStudio 对所有来源统一做 latest-intent 合并。只要不新增来源字段，就分不清“SCPI 目标被 UI 替换了”还是“被合并了”。
- 只要求“屏障之后第一次进入终态空闲”就不会假完成：Playback 的 Phase 1 或 materialization 期间属于 `PayloadPending`，不算空闲。
- 代价：本地 UI 或 Minibar 持续操作可能让等待延长。真实仪器在远程状态下会锁定前面板，SGStudio 不锁，这一点写入手册。

## 3. 阶段划分与依赖

```text
P1 状态/错误命令 ──┬──> P3 *OPC?/*WAI ──> P5 Streaming/桥接 ──> P7 文档/脚本迁移
P2 Core 完成状态 ──┤        │
                   └──> P4 异步错误入队
P1 ──> P6 QUES/OPER 设备条件（可与 P2～P4 并行）
P8 运维类改进（独立，可选）
```

| 阶段 | 估算（人日） | 里程碑 |
| --- | --- | --- |
| P1 | 2 | M1：标准状态与错误命令可用，日常使用立即受益 |
| P2 | 4～6 | — |
| P3 | 3～4 | M2：CW/Playback/扫描/配置加载具备真实 OPC（约第 2.5 周） |
| P4 | 1～2 | — |
| P5 | 4～5 | M3：Streaming 覆盖完整 |
| P6 | 3～5 | — |
| P7 | 2～3 | 文档、脚本与 KB 收口（约第 5 周） |

## 4. P1：IEEE 488.2 状态与错误命令

**改动**

1. `src/libs/scpi/src/parser/parser.cpp`，在 `Parser::init()` 中注册，handler 直接调用 libscpi 已编译的实现，写入输出后返回空 `QVariant`（`helper.cpp callResult2SCPI` 对无效值不会再输出）：
   - `*ESR?`、`*ESE`、`*ESE?`、`*STB?`、`*SRE`、`*SRE?`；
   - `*CLS`（`SCPI_CoreCls`：清错误队列和所有事件寄存器；P3 起同时取消 `*OPC`）；
   - `SYSTem:ERRor[:NEXT]?`（`SCPI_SystemErrorNextQ`，标准格式 `code,"text;info"`，带上 `reportError` 写入的详细原因）；
   - `SYSTem:ERRor:COUNt?`、`SYSTem:ERRor:ALL?`（循环弹出，逗号拼接）、`SYSTem:VERSion?`（`1999.0`）；
   - `STATus:OPERation[:EVENt]?`、`:CONDition?`、`:ENABle`、`:ENABle?`，以及 `STATus:QUEStionable` 的同组命令，`STATus:PRESet`。
2. **libscpi 的一个缺陷，在包装层修复，不改第三方源码**：`SCPI_RegSet` 处理 ENAB 类寄存器时直接返回（`ieee488.c` 的 `SCPI_REG_CLASS_ENAB` 分支），所以 `*ESE`/`STAT:*:ENAB` 修改后，STB 汇总位要等下次事件变化才更新。包装层在设置 enable 后，按 `event & enable` 重新计算 STB 的 ESB/QES/OPS 位，再写 STB，让 SRQ/MSS 逻辑保持一致。
3. `ScpiServer`/`ScpiEngine`/`Parser` 新增 `setQuestionableCondition`、`setOperationCondition`、`pushError`（§2.1）。
4. `src/plugins/scpi`：
   - 删除 `*CLS` 注册和 `onClear`；
   - **NotConnected 位（D2）**：连接 `DeviceManager::currentDeviceCapabilitiesChanged`、`currentDeviceOpenStateChanged`；服务启动时初始化一次；条件为 `!currentDeviceCapabilities().usable()` 且没有设备切换在进行时置 QUES bit 9。
   - `*OPT?`：读取 `DeviceManager::currentDeviceCapabilities().device.options`，`known` 且非空时返回逗号分隔的 `effectiveOptionCodes` 十进制码，否则返回 `0`。选件名称映射需要 htra 拥有的 `OPTION_*` 宏，SCPI 不 include `h2_api.h`；需要名称时另行讨论。
   - **错误码细化**：`PRPOERTY_NULL_CHECK` 的只读分支改为推 `-221 SettingsConflict`（附属性名），原来只是 `qDebug` 后由 libscpi 统一推 -200；`PANEL_NULL_CHECK` 的业务不可见或不存在分支推 `-241 HardwareMissing`（附业务名）。
5. 错误队列容量按 D7 改为 32。

**寄存器位（P1 先定义，后续阶段逐步接线）**

| 寄存器 | 位 | 含义 | 接线阶段 |
| --- | --- | --- | --- |
| ESR | 0 OPC / 2 QYE / 3 DDE / 4 EXE / 5 CME | IEEE 488.2；错误位由 libscpi 按错误码区间自动设置 | P1 / P3 |
| STB | 2 错误队列非空 / 3 QES / 5 ESB / 6 MSS / 7 OPS | libscpi 维护；MAV 未实现，raw socket 下不需要 | P1 |
| QUES | 3 POWer | UNLEVEL | P6 |
| QUES | 5 FREQuency | 本振/时钟失锁、外参考失锁 | P6 |
| QUES | 8 CALibration | 默认或缺失校准文件（需先找到来源） | P6 |
| QUES | 9 NotConnected（自定义） | 无可用设备 | **P1** |
| QUES | 14 Command Warning | 参数被 API 钳位（98），只产生事件脉冲 | P6 |
| OPER | 1 SETTling | 有 pending | P3 |
| OPER | 3 SWEeping | F/L/MScan 生效且 RF 打开 | P6 |
| OPER | 8 Generating（自定义） | `PayloadPending` | P3 |
| OPER | 9 Applying（自定义） | `ApplyActive` 或 `RefreshDirty` | P3 |
| OPER | 10 Streaming（自定义） | Streaming 运行中 | P5 |
| OPER | 11 DeviceSwitching（自定义） | `DeviceSwitch` | P3 |

只支持正跳变锁存（libscpi 的 `USE_CUSTOM_REGISTERS 0`，没有 PTR/NTR 命令），写入手册。WAIT for TRIGger（OPER bit 5）需要“已 arm 等待”的状态源，暂不定义。

**验证（debug-run，大部分不需要设备）**

- 新建 `scripts/test_scpi_ieee488.py`（只用标准库，复用 foundation 的 `ScpiSession`）：
  - 制造 `-113` 后 ESR=32，制造 `-224` 后 ESR=16，`*ESR?` 读后清零；
  - `*ESE 32` 后 `*STB?` 的 bit5 立即反映（验证上面的包装修复）；
  - `*CLS` 清空错误队列和事件寄存器；
  - `SYST:ERR?` 带引号且包含详细原因；`SYST:ERR:COUN?`、`:ALL?` 正确；
  - 未连接时 `STAT:QUES:COND?` bit 9 = 1，连接设备后为 0；`STAT:QUES?` 锁存后读出即清；
  - `*OPT?` 在有设备和无设备两种情况下的返回。
- 回归：`test_scpi_foundation.py`、`test_scpi_error_queue.py` 不改即可通过。error_queue 若断言了容量 10，按 D7 同步修改该断言。

**成功判据**：上述脚本通过；`*IDN?`、`*ERR?`、多查询响应格式与改动前逐字节一致。

## 5. P2：Core 完成状态

**改动**

1. 在 `TxSessionService` 中实现 `pendingOperationReasons()`，每个原因都从现有字段推导：

   | 原因 | 来源 |
   | --- | --- |
   | `RefreshDirty` | `m_refreshDirty`（`requestRefresh()` 中同步置位，`txsessionservice.cpp:495`） |
   | `ProfileTransaction` | `m_updatesSuspended`（LOAD 与 Preset 通过 `beginPipelineUpdateSuspension` 设置，`mainwindow.cpp:1413`） |
   | `CapabilityReconcile` | `m_deviceCapabilityReconcileInProgress` |
   | `DeviceSwitch` | `DeviceRuntimeProfileCoordinator::switchProgressChanged(bool)` |
   | `ApplyActive` | `TxPipelineRuntime` 新增 `hasPendingWork()`，即 `m_inFlight \|\| m_hasPendingLatest \|\| m_desiredSnapshotPending` |
   | `PayloadPending` | 有效管线为 Playback、provider participating、`!dataReady`、`errorMessage` 为空；或 runtime `m_materializationPendingPayloadIdentity != 0`（新增访问器） |
   | `MScanApplyPending` | 载波计划为 MScan，且 `submitted().submissionRevision != applied().submissionRevision`，且该提交没有失败 |
   | `LegacyTransition` | `m_legacyTransitionPending` |

   设备不可用（`!currentDeviceCapabilities().usable()`）且没有设备切换在进行时，返回空集合（D2）。只有一个必要的派生缓存 `m_lastPublishedReasons`：变化时才发 `operationStateChanged()`。

2. **重新求值并发出信号的时机**：`requestRefresh`、`processPendingRefresh` 返回后、`setUpdatesSuspended`、能力 reconcile 开始/结束/就绪、runtime 的 `applyQueueIdle`/`applyStateChanged`/`deviceConfigurationDone`/`sweepConfigurationDone`/`playbackPayloadMaterializationRequired`、`requestApply` 返回后、provider 的 `providerExecutionContextChanged`、MScan 的 `submittedChanged`/`appliedChanged`、`switchProgressChanged`、`DeviceManager::currentDeviceCapabilitiesChanged`。
3. **`operationFailed`**：
   - session 订阅 runtime `applyStateChanged(request, false)`，限当前设备、非 stale。09-08 草案指出 session 目前忽略这个失败事件。
   - `sweepConfigurationDone` 带错误时。
   - 有效 provider 的 `errorMessage` 从空变为非空时。
   - 只发送不重试，遵守 runtime 现有“不自动重试失败意图”的规则。
4. **审计（主要成本）**：逐一核对每类 SCPI setter 和每个 provider（Multitone、AWGN、AM、FM、PM、Pulse、Ramp、Digital、DSSS、OFDM、Playback/ARB、Quick Waveform、Streaming），确认“setter 返回前 pending 已可见”。也就是 `setPropValue` → `editingFinished`（同线程直连）→ 业务 → `requestRefresh()` 或 provider 生成标记，全程没有 queued/定时器跳转。
   - 发现跳转时，在跳转的源头同步标记 pending（所有者修复），不在协议层加“让出一轮事件循环”之类的补丁。
   - 同时列出 `providerParticipating == false` 的 Playback 桥接 provider。
   - 审计结果以表格形式追加到本文件。
5. **诊断**：新增日志类别 `sgs.tx.operation`（默认关闭），记录原因集合的每次变化，便于 P3 排查“假完成”和“挂起”。

**验证（debug-run，需要设备）**：打开日志类别，分别在 GUI 和 SCPI 下操作 CW、1024 音、AWGN 100 MHz/100 ms、G-2、F/L/MScan、LOAD 全部夹具、切换设备、断开设备、Preset、退出，确认原因集合最终归零、不会在 UI 进入 Applied/Failed 之前归零、无设备时为空。

**成功判据**：审计表没有遗留未处理的 queued 跳转；验证矩阵中没有“原因提前归零”或“长期不归零”。

## 6. P3：真实 `*OPC?`/`*WAI`/`*OPC`

**改动（协议层 `parser.cpp/.h`）**

1. **程序消息切分**：在每个头部为 `*WAI` 或 `*OPC?` 的单元之后切开（不区分大小写，只在引号和定长块之外的 `;` 处切）。
   - 扫描规则与 `transport_tcp.cpp takeNextCommand` 相同：`"`/`'` 字符串含双写转义，`#<n><len>` 为定长块，`#H/#B/#Q` 是数值前缀而不是块。把这段扫描逻辑提取到 `helper` 中，transport 和 parser 共用。
   - 语义不变的依据：libscpi 不会把公共命令之后的单元与前一个头部拼接路径（`utils.c:693`），所以切开后从根路径解析与原来完全等价。
2. **消息状态**：`{segments, next, output, isByteArray, callback}`。每段 `SCPI_Parse` 的输出去掉末尾 CRLF，非空段之间用 `;` 连接，最后补 CRLF 一次性回调。结果与原来整行解析的字节完全一致（libscpi 在同一次 parse 内也是用 `;` 分隔查询并只写一次 CRLF）。
3. **屏障**：一段以屏障结尾时，如果 `hasPendingOperations()` 为真，则置 `mBarrierActive`，保留当前消息和队列并退出 drain。`*OPC?` 立即写入 `1`，但因为整条消息的响应被扣住，实际在完成后才发出。
4. **恢复**：`notifyOperationStateChanged()` 先更新 OPER bit1/8/9/11。如果有屏障且已无 pending，就通过 `QTimer::singleShot(0, ...)` 恢复 drain，绝不在 Core 信号栈内重入解析。原有 `mDraining` 嵌套保护保留。
5. `*OPC`：没有 pending 时立即 `SCPI_RegSetBits(ESR, ESR_OPC)`，否则置 `mOpcArmed`，通知时完成。`*CLS`、`*RST` 取消挂起。
6. `*RST`：协议层先取消 OPC 状态，再调用产品通过 `setResetHandler` 注册的 handler（即现有 `onReset` 的逻辑）。
7. `stop()`：清除屏障、丢弃当前消息（不应答）、取消 `*OPC`，与现有“停服丢弃排队命令”保持一致。客户端断开时，已扣住的响应因 `QPointer` 自然丢弃；屏障继续存在直到完成或停服（D4）。

**改动（适配层 `src/plugins/scpi`）**：

- 设置 pending 源为 `pendingOperationReasons() != 0`；
- 把 `operationStateChanged` 连到 `notifyOperationStateChanged()`，并按原因映射 OPER 自定义位；
- 删除 `*WAI` 的注册和 `onWait`，`*RST` 改为通过 `setResetHandler` 提供；
- 关闭阶段：`aboutToShutdown` 已调用 `stop()`。`MainWindow::closeEvent` 会把 `updatesSuspended` 永久置真，从这时到 stop 之间屏障不会被释放，依靠 stop 丢弃。

**验证（debug-run，Windows + 树莓派）**

- 协议（无需设备）：
  - `FREQ?;*OPC?;POW?`、foundation 中 2500 个 `*OPC?` 的大消息、CRLF 测试，响应与改动前逐字节一致；
  - 无设备时 `*OPC?` 立即返回 1，QUES bit 9 = 1。
- 设备矩阵：
  - 对 CW、1024 音、AWGN、G-2 Playback、F/L/MScan（含 `LIST:LEAR;FREQ:MODE LIST;*OPC?`）、LOAD 全部夹具、`*RST;*OPC?`、运行中拔线，各做 10 轮；
  - 判据：`*OPC?` 的返回时刻**不早于** `sgs.tx.operation` 日志中原因归零的时刻和 UI 进入 Applied/Failed 的时刻，且最多晚一个事件循环；没有挂起；`MTON:COUN 1024;*WAI;MTON:COUN?` 的顺序正确。
  - 退出时有屏障在等待：不死锁。
- 回归：foundation、error_queue 照常通过。`test_scpi_listmode_learned.py` 的 `send_checked` 现在会等待 LEARn/apply 完成，需要放宽超时；`wait_until_running` 可以保留。

**风险**

| 风险 | 缓解 |
| --- | --- |
| 审计遗漏导致假完成 | P2 审计 + 日志对比矩阵 |
| 漏发信号导致挂起 | 所有输入点都重新求值；日志能定位卡在哪个原因上。不加服务端超时，避免把失败伪装成完成 |
| `*RST` 后短暂断连，设备暂时不可用，`*OPC?` 被 D2 提前放行 | 确认 preset 的断连窗口是否落在 `DeviceSwitch`/`CapabilityReconcile` 内；如果不在，由设备 reset 的所有者在窗口内保持 pending。在矩阵中专门验证 |
| 客户端超时短，长时间生成时超时 | 发布说明；推荐用 `*OPC` + 轮询 `*ESR?` 处理长操作 |

## 7. P4：异步失败进入错误队列

**改动（`src/plugins/scpi`）**：服务运行期间，把 `operationFailed(kind, message)` 转成 `pushError`。

| kind | 错误码 | 说明 |
| --- | --- | --- |
| `DeviceApply` | -300 | DDE，info 中附 API 文本 |
| `CarrierPlan` | 复用现有 `scpiErrorForMScan` 映射；无法映射时用 -300 | — |
| `Payload` | -200 | 生成失败、容量或采样率不满足 |
| `Stream` | -300 | — |

ESR 由 libscpi 自动置位。来源不区分（D3）。

**相关的所有者修复（另开 TaskLog，不在本方案内）**：[2026-11-06 TaskLog §6](2026-11-06_even-multitone-ffm-download-race-analysis.md) 的“必修”项，即 `tx_download_waveform` 返回 +10 被 `warningAsSuccess` 吞掉。不修这一处，下载超时永远到不了错误队列。

**验证**：构造 Playback 容量超限、非法采样率、下载中拔线、MScan apply 失败等场景，检查 `SYST:ERR?` 的文本和 ESR 位，并确认 `*OPC?` 照常返回 1。

## 8. P5：Streaming 与 Playback 桥接 provider

**改动**

1. Core 的 `IBusiness` 新增 `virtual bool bridgedApplyPending() const { return false; }`，状态变化时复用现有的 `providerExecutionContextChanged()` 信号通知。这是仓库内部的插件接口，只有 htra 等内部插件实现。
2. `StreamingBussiness`（`src/plugins/htra/streamingbussiness.h`）：
   - 从 `setBridgedApplyRequest`/`active()` 开始，到设备配置完成且第一块数据被接受为止，返回 pending；
   - `terminate()` 到发送线程空闲为止，返回 pending；
   - 失败时由 session 发出 `operationFailed(Stream, ...)`；
   - 运行中置 OPER bit 10。
3. `TxSessionService` 在有效管线走 legacy/桥接路径时，加入 `BridgeProvider` 原因。目前 `applyResolvedPipeline` 在 `active()` 之后立即调用 `publishAppliedRequest`（`txsessionservice.cpp:1000-1040`），不能作为完成依据。
4. 采样率确认提示（M6）：确认 SCPI 路径是否会弹模态框。如果会，`*OPC?` 将等待人工操作，需要为 SCPI 定义非交互策略（另议）。

**验证**：M-1、M-2 加载，启用/停用，改采样率；Streaming 中执行 `*OPC?`；拔线（只拔非流模式的数据口，遵守 M1 的安全警示）。

## 9. P6：QUES/OPER 设备条件接线

**改动（`src/plugins/scpi`，数据源都已存在）**

- QUES bit 3：`DeviceManager::currentDevice()->txLevelUnlevelActive()`，在 `deviceRealTimeStatusUpdated` 和 `deviceConfigurationDone` 后刷新。
- QUES bit 5：`DeviceRealTimeStatus.activeWarnings` 中出现 15～19，或 `referenceClock` 在 External 下 `!locked`；按 `deviceUid` 过滤陈旧状态。
- QUES bit 14：API 返回 98 时产生事件脉冲（条件置位后立即清除，事件被锁存）。需要 FancyDevice 暴露一个“最近操作被钳位”的通知；目前 98 只记日志（`fancydevice.cpp:1804` 等），属于所有者改动。
- QUES bit 8：42/44 目前不进入 `activeWarnings`（`fancydevice.cpp:1177-1181`），需要先找到来源；没有来源就不实现。
- OPER bit 3：`appliedPipeline()` 为 SweepCw/SweepPlayback/SweepStream 且 RF 打开。

**验证**：J2 拔外参考，QUES:COND bit 5 置位并锁存；O1 在标准口输出高电平，bit 3 置位；设置 `STAT:QUES:ENAB 40` 后 `*STB?` 的 bit 3 跟随变化。

## 10. P7：文档、脚本与 KB

- **手册**：在 `scripts/README_scpi_gui_baseline.md` 中新增“同步与状态”一节，内容包括契约 §1、寄存器表、D2/D4/D5/D9、兼容性变化（§11）。
- **KB**：更新 `scpi_review_and_integration_plan.md` 的 §2/§3/§4（§4 修正为“当前无模态”）；新建 `scpi_status_model_and_operation_completion.md`，并更新 `Index.md` 的 E-scpi 分区。
- **脚本**：
  - 公共会话类新增 `opc()`；
  - baseline 的 `checkpoint` 改为 `send` → `*OPC?` → 查询，`--wait-ms` 只作为旧构建的显式回退；
  - 用 `STAT:OPER:COND?` 是否被识别（-113）探测新构建；
  - listmode 专项放宽超时。

## 11. 兼容性变化（发布说明）

| 变化 | 影响 |
| --- | --- |
| `*OPC?`/`*WAI` 可能阻塞到完成 | 客户端超时过短的脚本，在长时间生成或大文件下载时会超时 |
| `*CLS` 额外清空事件寄存器并取消 `*OPC` | 符合标准 |
| 只读属性写入：-200 改为 -221；业务不可用：-200 改为 -241 | 依赖 -200 的脚本需要调整（现有 baseline 的 G9 本来就期望 -221） |
| 新增命令 | `*ESR? *ESE(?) *STB? *SRE(?) *OPC *OPT? SYST:ERR[:NEXT]?/:COUN?/:ALL? SYST:VERS? STAT:OPER/QUES:…? STAT:PRES` |
| 不变 | `*IDN?`（D1）、`*ERR?`（D6）、多查询响应格式、所有现有命令的参数语义 |

## 12. P8：运维类改进（独立、可选，不阻塞以上阶段）

- 启动参数 `--scpi[=port]`，持久化“启动时开启”和端口（KB §6）。
- 选择监听地址（默认值待产品决定）；`SYST:TEST:*` 只在显式测试模式下注册（KB §5，分析 TaskLog P13）。
- 把 `SYST:TEST:LOAD/SAVE` 正式化为 `MMEMory:LOAD:STATe`/`MMEMory:STORe:STATe`。
- SCPI 越界策略（-222 还是夹值），待决策。

## 13. 整体成功判据

1. P1～P4 完成后，baseline 的 basic/scans/files/repeat/rms 改用 `*OPC?` 同步、不使用固定等待，在 Windows Debug 和树莓派上各连续通过 10 轮，结果不比固定等待版本差。
2. 验证矩阵中 `*OPC?` 从未早于 Core 终态返回，也没有出现挂起。
3. 协议回归与新增 `test_scpi_ieee488.py` 全部通过；§11 列为“不变”的行为逐字节一致。
