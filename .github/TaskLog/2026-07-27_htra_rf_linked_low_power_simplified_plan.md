# HTRA RF 联动自动低功耗简化方案

## 1. 目标

在不拆分现有 TX 配置流水线、不引入 RF OFF 差分下发矩阵的前提下，
把 H2 `device_config_power_state()` 接入 RF 开关时序：

- 用户开启“自动低功耗”后：
  - RF ON 时确保设备为 `POWERON`；
  - RF OFF 时在现有 Mute 配置和 writeback 完成后进入 `POWEROFF`。
- 用户关闭“自动低功耗”后：
  - 立即确保设备为 `POWERON`；
  - 后续 RF OFF 不再触发 `POWEROFF`。
- 保留当前看似冗余的 clock、LO、port、FFM、trigger、output 和 query/writeback
  流程，不在本阶段实施配置域差分。
- Core-managed 流水线继续通过设备 I/O worker 串行调用；Streaming 保留现有
  sender owner thread 特例，不迁移到 core executor。两条路径继续由
  `FancyDevice::m_mutex` 保证 H2 调用互斥，UI 不直接等待 power-state API。

本方案只描述设计和实施边界，不修改代码。

## 2. 新的关键前提

本方案采用以下 H2 API 契约作为强前提：

1. 设备处于 `POWEROFF` 时，H2 控制面仍可接受 TX 配置调用。
2. `tx_config_ffm()` 在 `POWEROFF` 时不会真正驱动已经掉电的 RF/MCU/FPGA
   工作区，但仍会完成参数合法化或内部暂存。
3. 随后的 `tx_query_ffm()` 可以返回与 `POWERON` 时一致的规范化 center/level，
   因而现有 UI writeback 语义仍然成立。
4. `device_config_power_state(POWERON)` 成功返回后，现有完整
   `configuration()` 流水线可以立即恢复设备配置，不增加固定等待、ready
   轮询或重试。
5. 重复请求同一个 power state 是安全的；上层仍会通过软件缓存避免无意义重复调用。
6. `POWEROFF` 下实时状态 query 仍然有效。返回数值可能不准确，但温度 query
   继续作为设备在线保活探针；查询失败仍按现有逻辑判断设备断联。

其中第 2、3 条是本次简化成立的核心。如果实机证明 `tx_query_ffm()` 在
`POWEROFF` 时返回失败、旧值或零值，就必须撤回本方案，恢复
“休眠期间只保存 desired、上电后再做最终 writeback”的设计。

## 3. 当前实现证据

### 3.1 Mute 当前仍走完整流水线

`TxPipelineExecutor::applyMute()` 当前构造 Mute profile 后调用完整
`device->configuration(profile, &writeback)`。

HTRA `FancyDevice::configuration()` 的顺序保持为：

1. `applyCommonDeviceSettingsLocked()`
2. `applyModeConfigurationLocked()`
3. `fillWritebackProfileLocked()`

因此一次 Mute 当前仍会经过：

- `device_config_clock`
- `channel_config_lo_mode`
- `channel_config_port`
- `tx_config_ffm`
- `channel_config_trigger`
- `channel_config_trigger_out`
- `tx_config_output(OFF, OFF)`
- FFM、trigger、port、LO、clock query/writeback

本方案有意保留这条流水线。

### 3.2 串行异步执行基础已经存在

`TxPipelineRuntime` 当前已经具备：

- 设备 I/O worker 串行执行；
- in-flight 请求与 pending-latest 合并；
- generation/epoch 校验；
- 过期 completion/writeback 丢弃。

因此继续执行完整 Mute 配置虽然会占用设备 worker 时间，但不应再阻塞 UI 主线程。
本方案优化的是低功耗接入复杂度和状态正确性，不承诺减少底层 H2 调用数量。

### 3.3 Low Power 当前还是直接设备开关

当前 `Low Power` UI 直接映射：

- ON -> `POWEROFF`
- OFF -> `POWERON`

并由 `m_lowPowerEnabled` 缓存最近一次请求值。它还没有表达
“RF 关闭时自动休眠”的策略语义，也没有参与 TX 串行 runtime。

## 4. 方案结论

在新 API 前提成立时，这个简化方案可行，而且比“RF OFF 时逐项判断哪些字段
应该下发”更适合作为当前阶段的实现：

- 不改 `FancyDevice::applyCommonDeviceSettingsLocked()`。
- 不改 `FancyDevice::fillWritebackProfileLocked()` 的业务职责。
- 不增加 center/level 的 requested/confirmed 双状态。
- 不扩展频率、功率范围到 `DeviceCapabilities`。
- 不引入 applied-domain/dirty-domain 差分状态机。
- 不需要对 FFM 做 RF OFF 特判。
- 只增加一个自动低功耗策略状态，以及围绕完整配置流水线的 power-state 前后置动作。

需要接受的代价：

- RF OFF 的设备任务仍包含原有 set/query，物理完成时间不会明显缩短。
- 频繁修改参数时，设备 worker 仍会执行完整的 latest request。
- 该方案依赖 H2 对 `POWEROFF` 下 FFM writeback 的稳定契约。

由于 UI 卡顿已经由串行异步 executor 隔离，这些代价在当前阶段可以接受。

## 5. 状态定义

### 5.1 策略状态

新增或重新解释：

```text
autoLowPowerEnabled
```

它表示“RF OFF 时是否自动将设备切换到 POWEROFF”，而不是设备当前是否已经掉电。

### 5.2 软件推断的供电状态

H2 当前没有 power-state query，因此维护最小缓存：

```text
AssumedPowerState:
    Unknown
    PoweredOn
    PoweredOff
```

只有 `device_config_power_state()` 返回非错误结果后才更新缓存。

### 5.3 RF 状态来源

RF 意图继续由现有 pipeline 表达：

- `TxPipelineKind::Mute`：RF OFF
- 其他可执行 TX pipeline：RF ON

不再新增第二套 RF 布尔真相源。

## 6. Low Power UI 语义

建议把显示名称从 `Low Power` 调整为以下任一更准确的名称：

- `Auto Low Power`
- `RF Off Power Save`
- 中文界面使用“自动低功耗”

按钮行为：

### 6.1 用户开启自动低功耗

- 当前 pipeline 为 Mute：
  - UI 先更新线程安全的策略值；
  - 标记当前 desired request dirty 并强制 refresh；
  - 当前 Mute 在原有串行执行路径完成配置/writeback 后调用
    `device_config_power_state(POWEROFF)`。
- 当前 pipeline 非 Mute：
  - 只保存策略；
  - 设备按非 Mute 运行约束保持 `POWERON`；
  - 不因打开策略而中断当前 RF 输出。

### 6.2 用户关闭自动低功耗

- UI 先把线程安全策略值保存为关闭。
- 当前 pipeline 为 Mute 时，标记 desired request dirty 并强制 refresh；
  `FancyDevice::configuration(Mute)` 会先调用
  `device_config_power_state(POWERON)`，再执行现有完整配置/writeback。
- 当前 pipeline 非 Mute 时设备本来就应是 `POWERON`，不重启当前业务。
- RF 状态保持不变：
  - RF 原来为 OFF，仍保持 Mute；
  - RF 原来为 ON，仍保持当前业务流水线。
- Mute/休眠状态下不能让 runtime 因
  request 内容相同而直接判定“硬件已经匹配”。

### 6.3 UI 回显

按钮回显的是 `autoLowPowerEnabled`，不是 `AssumedPowerState`。

如需显示实际软件推断状态，应使用另一个只读状态，例如：

```text
Device Power: On / Sleep / Unknown
```

本阶段不要求新增这个只读 UI。

## 7. RF OFF 时序

当最新 request 解析为 `TxPipelineKind::Mute` 时：

```text
构建最新 Mute request
        |
        v
在设备 I/O worker 执行现有完整 configuration(Mute)
        |
        +-- clock / LO / port / FFM / trigger
        |
        +-- tx_config_output(OFF, OFF)
        |
        +-- 现有 query/writeback
        |
        v
完整 Mute 配置成功？
   |                 |
   | 否              | 是
   v                 v
报告现有错误      autoLowPowerEnabled？
                     |             |
                     | 否          | 是
                     v             v
                    结束      device_config_power_state(POWEROFF)
                                      |
                                      v
                          更新 AssumedPowerState
```

关键约束：

1. `POWEROFF` 必须位于现有 Mute 配置和 writeback 之后。
2. 不应先 `POWEROFF` 再依赖 `tx_config_output(OFF, OFF)` 关闭输出。
3. 如果 Mute 配置失败，不把完整 request 标记为成功。
4. 是否仍尝试 `POWEROFF` 作为 fail-safe，应根据 H2 错误类型单独决定；
   本阶段默认不增加第二套失败恢复路径。
5. RF OFF completion 只有在对应 generation 仍有效时才能回写 UI；
   继续复用现有 runtime 过滤规则。

## 8. RF ON 时序

当最新 request 从 Mute 进入非 Mute pipeline 时：

```text
取得最新非 Mute request
        |
        v
autoLowPowerEnabled 且 AssumedPowerState != PoweredOn？
   |                 |
   | 否              | 是
   v                 v
继续             device_config_power_state(POWERON)
                         |
                         v
                 更新 AssumedPowerState
                         |
                         v
执行现有完整 configuration()
        |
        +-- common settings / FFM
        +-- mode / waveform / stream / sweep
        +-- output / start / trigger
        +-- 现有 query/writeback
```

这里 `POWERON` 必须位于完整配置之前。这样即使设备在休眠期间收到过若干
只做校验/writeback 的配置请求，RF ON 时仍会通过最新完整 request 真正恢复硬件。

如果 Playback 波形仍在生成，继续遵循现有两阶段行为：

- 先完成能够执行的基础配置；
- RF 保持此前的关闭状态；
- 等最新波形物化后再下载和启动；
- 过期波形结果由现有 generation/latest-intent 规则丢弃。

## 9. RF OFF 期间的参数编辑

本方案不再对参数分类。

RF OFF 且自动低功耗开启时，参数变化仍然：

1. 构建完整 Mute request。
2. 进入现有串行 executor。
3. 调用现有配置和 writeback。
4. 由 H2 API 决定哪些调用只校验/暂存、哪些调用在掉电工作区不产生硬件动作。
5. 软件继续接收合法的 FFM writeback。
6. power-state 缓存发现已经是 `PoweredOff` 时，不重复调用 `POWEROFF`。

因此上层不需要维护：

- “这个字段 RF OFF 时是否允许下发”的白名单；
- FFM 的本地频率/功率裁剪算法；
- 休眠期间的 per-field dirty mask；
- 为 RF ON 单独拼装 deferred profile。

RF ON 时 pipeline 从 Mute 变为非 Mute，完整 request 必然不同，现有 runtime
会重新执行最新配置。

## 10. Runtime 与线程边界

### 10.1 power-state 调用必须串行化

不能继续让 Device Settings UI 在主线程直接调用
`configureLowPowerEnabled()`，否则可能出现：

- UI 等待 H2 API 或设备 mutex；
- RF ON 配置正在执行，UI 中途插入 `POWEROFF`；
- 旧任务完成后又把设备输出打开；
- power-state 缓存与 runtime 状态不一致。

最小修改不是新增一套独立 power job，而是：

1. UI 只更新 `autoLowPowerEnabled` 策略，不直接调用 H2 power API。
2. 当前为 Mute 时，策略变化后强制触发一次 TX request refresh；非 Mute
   特别是 Streaming 活跃时不重启当前业务。
3. power-state 前后置动作集中在 `FancyDevice::configuration()` 内，并与现有
   common/mode/writeback 共用 `m_mutex` 临界区。

这样：

- core-managed pipeline 仍由设备 I/O worker 调用 `configuration()`；
- Streaming 仍由其 sender owner thread 调用 `configuration()`；
- 两者最终进入同一个 HTRA 配置入口和同一把设备 mutex；
- UI 不会在两条设备执行路径中间插入一个无版本的 POWEROFF。

### 10.2 最小设备层挂点

建议只在 `FancyDevice::configuration()` 外围增加 power guard，内部现有配置细节
保持不变：

```text
lock m_mutex

if (!autoLowPowerEnabled || input.mode != Mute)
    ensurePowerStateLocked(POWERON)

applyCommonDeviceSettingsLocked(input)
applyModeConfigurationLocked(input)
fillWritebackProfileLocked(input, writeback)

if (autoLowPowerEnabled && input.mode == Mute)
    ensurePowerStateLocked(POWEROFF)
```

`ensurePowerStateLocked()` 只在目标状态与 `AssumedPowerState` 不同时调用
`device_config_power_state()`。成功后更新缓存；失败时进入 `Unknown` 或保留上一个
已确认状态，并返回本次配置失败。

这个挂点同时覆盖：

- Core-managed Mute/CW/Playback/Sweep；
- Streaming sender thread 的 `mode=STREAM` 基础配置；
- Streaming 参数变化导致的全量重配；
- Mute 下开启/关闭自动低功耗后的强制 refresh。

### 10.3 保留 latest-intent 规则

策略变化和 RF 变化需要遵循最新意图：

- RF OFF 后马上 RF ON：不得在最新 ON 配置完成后再执行旧 POWEROFF。
- RF ON 后马上 RF OFF：旧 ON completion 可以丢弃，但物理操作不能越过后续
  Mute/POWEROFF 的顺序。
- 关闭自动低功耗后排队的旧 POWEROFF 必须失效。

最小做法是让 power action 携带与 TX job 相同的 epoch/generation，或把 power
前后置动作直接放进对应 TX job，而不是使用独立的无版本 UI 命令。

### 10.4 相同 request 的重新应用

有一个需要显式处理的例外：

```text
Mute + Auto Low Power ON + PoweredOff
        |
用户关闭 Auto Low Power
        |
POWERON，但 TxApplyRequest 内容没有变化
```

此时 runtime 可能认为当前 Mute request 已经匹配 `m_hardwareRequest` 而跳过配置。
策略值不必进入 `TxApplyRequest`，但策略变化必须调用现有
`markDesiredStateDirty()`/强制 refresh 机制，确保当前 Mute 至少重新执行一次：

- 策略 ON：完整 Mute/writeback 后 POWEROFF；
- 策略 OFF：先 POWERON，再执行完整 Mute/writeback，并保持 PoweredOn。

## 11. FFM writeback 处理

在本方案的 API 前提下，继续保留：

```text
tx_config_ffm(center, level)
        |
        v
tx_query_ffm(&center, &level)
        |
        v
writeback 到 CommonDeviceProfile/UI
```

不增加本地 capability 裁剪，也不增加 pending UI。

但仍建议补齐一个与本方案无冲突的防御性检查：

- 必须检查 `tx_query_ffm()` 返回状态；
- query 失败时不能把初始化的 `0 Hz / 0 dBm` 写回 UI；
- 失败时保留 input 或最近一次有效值，并把本次 apply 作为失败或明确 warning。

这不是 RF OFF 差分设计，而是现有 writeback 的基本正确性要求。

## 12. 设备打开时序

当前设备 open 会根据 PGA 单口供电条件直接选择初始 `POWERON/POWEROFF`，随后还会
配置 fan 和 GPIO。若 `POWEROFF` 后这些调用对工作区无效，顺序需要调整为：

```text
device_open
    |
    v
确保 POWERON
    |
    v
完成 fan / GPIO / GNSS 等 open 初始化
    |
    v
建立初始 TX pipeline
    |
    v
若初始 RF OFF 且自动低功耗开启，
由 Mute job 在完整配置/writeback 后执行 POWEROFF
```

不要在 `FancyDevice::open()` 中提前进入 `POWEROFF`，然后继续执行依赖 MCU/FPGA
工作区的初始化。

PGA 单口供电判断可以继续决定 `autoLowPowerEnabled` 的默认值，但不应再直接决定
open 中段的最终物理 power state。

## 13. 实时状态轮询

按当前确认的 API 契约，`POWEROFF` 下继续保留现有实时查询：

- `device_query_temperature`
- `device_query_state`
- `device_query_supply`
- `device_query_gnss_info`

这些值在低功耗下可能不准确，但控制面仍在线。尤其
`device_query_temperature()` 继续作为设备保活探针；查询失败仍沿用现有设备断联
处理。本阶段不按 `AssumedPowerState` 暂停轮询，也不改变状态线程时序。

## 14. Device Settings 直接控制的边界

本阶段只保证 TX 主流水线和 FFM writeback 的简化语义。

RefOut、Fan、GPIO、GNSS 等直接设备控制不应因为 H2 的 FFM 特性而被推定为
在 `POWEROFF` 下同样有效。最简单的第一阶段策略是：

- 自动低功耗已使设备进入 `PoweredOff` 时，这些需要实时生效的控件禁用；
- UI 提示“请关闭自动低功耗或开启 RF 以唤醒设备”；
- 不为这些控件增加另一套 deferred/pending 状态。

如果产品后续要求休眠期间仍可编辑这些设置，再单独设计唤醒或暂存策略。

## 15. Streaming 边界

### 15.1 保留 Streaming 作为 legacy owner 特例

`FixedStream/SweepStream` 当前由 `TxSessionService` 统一裁决，但真正的设备配置、
生产和发送仍由 `StreamingBussiness` 持有：

- generator thread 读取并生产 IQ；
- sender thread 调用 `m_operator.configuration()`；
- sender thread 调用 `tx_send_stream`；
- FixedStream/SweepStream 同 business 内切换时，通过 bridged request
  设置 `m_profileChanged` 并重配，不 terminate/reactivate。

本阶段不把 Streaming 迁入 `TxPipelineRuntime/TxPipelineExecutor`，也不新建
Streaming 专用 power coordinator。它可以继续作为唯一特例，只要满足：

1. power-state 仍由 `FancyDevice::configuration()` 的统一 guard 管理；
2. 退出 Streaming 时必须先停止 sender，再允许 Mute/POWEROFF；
3. 启动或重配 Streaming 时必须先 POWERON，再配置 stream 和发送数据。

### 15.2 为什么统一设备层 guard 足够

Streaming sender thread 每次启动或 profileChanged 都会先调用：

```text
m_operator.configuration(mode=STREAM)
```

因此把 `ensurePowerStateLocked(POWERON)` 放在
`FancyDevice::configuration()` 的 common/mode 配置之前，会自然覆盖：

- 第一次进入 FixedStream；
- 第一次进入 SweepStream；
- Streaming sample rate 改动；
- Streaming 公共 center/level/clock/trigger 改动；
- FixedStream 与 SweepStream 的 session 内切换。

当设备已经是 `PoweredOn` 时，软件缓存会让这个 guard 成为 no-op，不会在每个
Streaming 参数变化时重复调用 POWERON。

SweepStream 的现有顺序继续保持：

```text
configuration(mode=STREAM)
        |
        +-- POWERON guard
        +-- FixedStream 基础配置
        |
        v
startStreamingFrequencySweep / LevelSweep / ListSweep
        |
        v
进入 tx_send_stream 循环
```

由于 sweep overlay 位于基础 `configuration(mode=STREAM)` 之后，不需要再给
每个 `startStreaming*Sweep()` 增加第二个 POWERON 调用。

### 15.3 Streaming -> RF OFF

当前 `TxSessionService` 从 legacy Streaming 切换到 core-managed Mute 时，已经按
以下顺序执行：

```text
currentBusiness->terminate()
        |
        v
StreamingBussiness::stopBusiness()
        |
        +-- generator setEnabled(false)
        +-- interrupt/清空发送队列
        +-- 等待 sender loop 退出当前发送循环
        |
        v
TxPipelineRuntime::requestApply(Mute)
        |
        v
FancyDevice::configuration(mode=MUTE)
        |
        +-- 现有完整 Mute 配置/writeback
        +-- 自动低功耗开启时 POWEROFF
```

这个顺序已经满足最重要的安全边界：POWEROFF 发生前，Streaming sender 已停止，
不会再有新的 `tx_send_stream` 与 Mute/POWEROFF 并行。

因此不需要在 `StreamingBussiness::stopBusiness()` 中直接调用：

- `tx_config_output(OFF, OFF)`；
- `device_config_power_state(POWEROFF)`；
- core runtime API。

这些动作继续由退出 Streaming 后的统一 Mute 流水线完成，避免形成第二套 RF OFF
语义。

### 15.4 RF ON -> Streaming

从 Mute 进入 FixedStream/SweepStream 时，保留现有桥接流程：

```text
等待 core apply idle
        |
        v
deactivate core runtime
        |
        v
setBridgedApplyRequest(stream request)
        |
        v
StreamingBussiness::active()
        |
        v
sender thread -> configuration(mode=STREAM)
        |
        +-- POWERON guard
        +-- 完整 stream 配置
        +-- output/start/trigger
        |
        v
generator/sender 开始持续送数
```

不在 `TxSessionService` 中单独插入 POWERON，也不让 UI 预先 POWERON。这样 power
动作和真正使用设备的 Streaming 配置仍在同一个设备 mutex 临界区内，避免
“已经 POWERON，但 Streaming 还没接管”这一额外中间态。

### 15.5 Streaming 运行中切换自动低功耗策略

Streaming 活跃意味着 RF ON，因此：

- 策略 OFF -> ON：只更新 `autoLowPowerEnabled`，保持 POWERON，不重启 session；
- 策略 ON -> OFF：只更新策略；设备本来就是 POWERON，不重启 session；
- 不因策略按钮变化调用 `StreamingBussiness::terminate()/active()`；
- 不清空 generator 队列；
- 不重新执行 stream 配置。

最新策略会在下一次真正进入 Mute 时由
`FancyDevice::configuration(mode=MUTE)` 读取并决定是否 POWEROFF。

策略字段可以保存在当前设备实例中并采用线程安全读写；它不必加入 bridged
`TxApplyRequest`，因为 Streaming 活跃期间策略变化没有即时硬件副作用。

### 15.6 Streaming 配置失败的必要保护

当前 sender loop 调用 `m_operator.configuration()` 后仍会继续执行 sweep overlay
并进入送数循环。引入 POWERON guard 后，必须保证：

- POWERON 或基础 stream configuration 失败时，不调用任何
  `startStreaming*Sweep()`；
- 不进入 `tx_send_stream` 循环；
- 保持 generator/sender 可被 stop 或下一次 refresh 唤醒；
- 通过现有 `deviceConfigurationEnd(..., errorStr)` 报告失败；
- 不把失败 session 标记为已经可以发送。

这是 Streaming 特例唯一建议增加的本地行为保护。它不改变成功路径，不引入
新的 restart/rearm 分类，也不迁移线程 owner。

当前 bridge 在 `targetBusiness->active()` 后、真实 sender configuration 完成前，
就会更新上层 `m_appliedPipeline`。本阶段不为此重做 Streaming completion 模型，
但不能拿这个上层 applied 标记推断物理供电状态。实际 power 判断只使用
`FancyDevice` 中由成功 power API 更新的 `AssumedPowerState`。

### 15.7 Streaming 与 core worker 的并发边界

Streaming sender thread 不是 `DeviceIoWorker`，因此“所有设备 API 都在同一线程”
并不是当前架构事实。最小方案继续接受两类 owner：

| 模式 | 设备操作 owner |
| :--- | :--- |
| Mute/CW/Playback/core Sweep | Device I/O worker |
| FixedStream/SweepStream | Streaming sender thread |

一致性依靠两层保证：

1. `TxSessionService` 在 core/legacy 切换时先 terminate 当前 owner，再启动目标
   owner；core apply in-flight 时延迟进入 legacy。
2. 所有 HTRA API 最终由 `FancyDevice::m_mutex` 串行化。

本阶段不为统一线程模型扩大改动范围。

需要保留一个已知边界：当前 `StreamingBussiness::stopBusiness()` 会等待 sender
退出当前发送循环，而且使用 busy-wait。这样可以保证 POWEROFF 不会与
`tx_send_stream` 并发，安全顺序是正确的，但 RF OFF 的 UI 路径最长仍可能等待
当前 `tx_send_stream` 返回。

最小实施阶段不借低功耗任务重构整个 stop 生命周期，但必须遵守：

- 不能为了缩短等待而跨线程强制 POWEROFF；
- 如果 `tx_send_stream` 可能无限阻塞，需要 H2 提供 cancel/timeout，不能由上层
  绕过 sender owner 强行解决；
- 实机测试应单独测量 Streaming RF OFF 的主线程等待时间；
- 如等待时间不可接受，后续只把 Streaming terminate 改为异步完成通知，再提交
  Mute，而不是迁移整个 Streaming pipeline。

### 15.8 Streaming 不需要做的改动

- 不把 FixedStream/SweepStream 改成 core-managed。
- 不把 IQ frame 塞进 `TxApplyRequest`。
- 不改变 generator/sender 双线程和队列。
- 不新增 streaming power thread。
- 不在每个 `tx_send_stream` 前查询或设置 POWERON。
- 不在 FixedStream/SweepStream session 内切换时 power cycle。
- 不借本任务实现 center/level live retune、RearmSession 或 HardReboot 分层。

## 16. 最小实施步骤

### 阶段 A：确认 H2 契约

- 实机验证 `POWEROFF` 下 `tx_config_ffm + tx_query_ffm`。
- 覆盖合法值、98 参数钳位、99 UNLEVEL 和 query 失败场景。
- 按已确认契约，`POWERON` 返回后立即配置，不增加等待。
- 确认重复 POWERON/POWEROFF 的幂等性。
- 验证掉电后的实时状态 query 继续承担保活职责。

### 阶段 B：策略语义和串行 power action

- 把 Low Power 改为 `autoLowPowerEnabled` 策略语义。
- 增加 `AssumedPowerState` 缓存。
- UI 策略编辑只更新线程安全策略状态，不直接调用 H2。
- 策略变化时强制 refresh 当前 Mute request；Streaming 活跃时不重启 session。

### 阶段 C：接入 TX 完整流水线

- 在 `FancyDevice::configuration()` 中为非 Mute/策略关闭的 Mute 配置前确保
  POWERON。
- 在同一函数中为策略开启的 Mute 完整配置/writeback 后执行 POWEROFF。
- 相同 power state 不重复调用。
- 保持 common/mode/writeback 细节不变。

### 阶段 D：open 与状态轮询

- open 初始化期间保持 POWERON。
- 初始 Mute job 决定最终是否 POWEROFF。
- PoweredOff 时继续执行现有温度/state/supply/GNSS query。
- 不改变温度 query 失败触发的断联判定。

### 阶段 E：Streaming

- 保留 legacy bridge、sender owner thread 和双线程队列。
- 检查基础 configuration 返回值；POWERON/config 失败后禁止 overlay/send。
- 验证 Streaming terminate 完成后才进入 Mute/POWEROFF。
- 验证 Mute -> Streaming 由统一 `configuration(mode=STREAM)` guard 唤醒。
- 验证快速 RF ON/OFF 和策略切换。

## 17. 验收标准

### 17.1 API 前提

- `POWEROFF` 下 FFM 合法值仍能得到正确 writeback。
- 98 钳位和 99 UNLEVEL 行为与 POWERON 下一致。
- FFM query 失败不会向 UI 写入零值。

### 17.2 功能

- 自动低功耗 OFF：
  - RF OFF/ON 不调用 `POWEROFF`；
  - 设备保持 POWERON。
- 自动低功耗 ON：
  - RF OFF 完整 Mute/writeback 后调用一次 POWEROFF；
  - RF ON 在完整配置前调用一次 POWERON；
  - RF ON 状态下修改普通参数不重复调用 POWERON；
  - RF OFF 状态下修改普通参数不重复调用 POWEROFF。
- 关闭自动低功耗：
  - 立即 POWERON；
  - RF 状态不改变；
  - 当前 Mute request 至少重新执行一次，不被 hardwareRequest 去重跳过。

### 17.3 时序和线程

- Core-managed power-state 调用发生在设备 I/O worker；Streaming 的 POWERON
  guard 发生在 sender owner thread。两者都必须位于
  `FancyDevice::configuration()` 的同一设备 mutex 临界区。
- 不存在 UI 主线程等待设备 mutex/H2 API 的路径。
- 快速执行 RF OFF -> ON 时，旧 POWEROFF 不会落在最新 ON 配置之后。
- 快速执行 RF ON -> OFF 时，最终设备一定为 Mute；策略开启时最终为 POWEROFF。

### 17.4 设备生命周期

- open 初始化不会在 POWEROFF 后继续调用必需的 fan/GPIO/GNSS 初始化。
- POWEROFF 后实时状态轮询继续运行。
- 温度 query 成功时设备保持在线；查询失败时仍按现有逻辑判定断联。
- Playback 波形和 Streaming 在掉电后的保留/重建行为有明确实机结论。

### 17.5 Streaming

- FixedStream/SweepStream 仍由当前 StreamingBussiness 和 sender thread 管理。
- Streaming -> Mute 时，最后一个 `tx_send_stream` 返回并退出 sender loop 后，
  才执行 Mute/POWEROFF。
- Mute -> Streaming 时，第一次 stream configuration 在任何 overlay/send 前
  完成 POWERON。
- FixedStream/SweepStream session 内切换不发生 power cycle。
- Streaming 参数变化不重复调用 POWERON。
- 自动低功耗策略在 Streaming 活跃期间切换，不清队列、不重启 session。
- POWERON 或基础 stream configuration 失败后，没有 sweep overlay 和
  `tx_send_stream` 调用。

## 18. 明确不在本阶段实施

- RF OFF 配置域差分。
- FFM 频率/功率 capability 扩展。
- requested/confirmed 双值 UI。
- 通用 deferred-setting 框架。
- 为所有 Device Settings 控件建立休眠期乐观更新。
- 删除现有 common/mode/query/writeback 调用。
- 把 Streaming 迁入 core-managed runtime。
- 改造 Streaming generator/sender 双线程与队列。
- 实现 Streaming live-retune/RearmSession/HardReboot 分层。

## 19. 最终判断

如果 H2 确实保证 `POWEROFF` 下 FFM 可以完成与正常状态一致的合法化和 writeback，
那么保留现有完整配置流水线、只在 RF 状态转换外围增加 power-state 动作，是当前
最简单且风险较低的方案。

该方案把复杂度限制在：

- 一个自动低功耗策略；
- 一个软件推断的 power state；
- POWERON 前置、POWEROFF 后置；
- `FancyDevice::configuration()` 内统一的 power guard；
- Streaming 保留 sender owner thread 这一项明确特例。

它不会减少现有 H2 set/query 数量，但能够避免重新设计整个流水线和 FFM UI
能力系统，也不要求为了低功耗把 Streaming 迁入 core executor。考虑到主线程
卡顿已经由串行异步 executor 解决，这个取舍合理。

## 20. 实施结果（2026-07-27）

本轮已按上述最小方案完成实现：

- `FancyDevice::open()` 始终先进入 `POWERON`，再执行 fan/GPIO 等初始化；初始
  Mute 任务决定最终是否进入 `POWEROFF`。
- `Low Power` 改为 `Auto Low Power` 策略语义，UI 只写原子策略值，不直接调用
  H2 power API，也不等待设备 mutex。
- `FancyDevice::configuration()` 在同一个设备锁内执行统一 power guard：
  非 Mute 配置前确保 `POWERON`；自动低功耗开启时，完整 Mute/writeback 成功后
  执行 `POWEROFF`。
- 同一 power state 通过 `AssumedPowerState` 去重；power API 失败时恢复为
  `Unknown`，本次配置返回失败。
- 按已确认契约，`POWERON` 成功返回后立即继续配置，没有增加 sleep、ready
  query 或 retry。
- Mute 下切换策略会使 runtime 失活并强制重放相同 Mute request，避免
  hardware-request 相等判断跳过策略变化；RF ON（含 Streaming）时只保存策略，
  不重启当前业务。
- `POWEROFF` 后清除设备层和 executor 层的波形驻留推断，下一次 Playback
  不会错误复用掉电前的 FPGA 驻留结论。
- `tx_query_ffm()` 失败时保留输入 center/level，避免把 `0 Hz / 0 dBm`
  回写到 UI。
- Streaming 继续保留 legacy sender owner；统一 guard 负责唤醒设备。
  基础配置或 sweep overlay 失败时不进入后续 overlay/send，等待 stop 或下一次
  reconfiguration 唤醒。
- 实时 temperature/state/supply/GNSS 查询未增加任何 PoweredOff 分支，温度
  keepalive 和现有断联判定保持不变。

本轮没有实现第 14 节所述 RefOut/Fan/GPIO/GNSS 控件禁用或 deferred-setting；
它们继续作为明确的后续边界，避免把本次 RF 主流水线改动扩展到独立设备控制。

验证结果：

- `git diff --check` 通过（仅有仓库现有行尾转换提示）。
- 在 VS 2022 x64 开发环境中执行
  `cmake --build build/Qt_5_15_9_msvc2022_64-Debug --target Core HTRA`
  通过；修改过的 Core/HTRA 源文件均已重新生成对象文件并完成目标链接。
- 未执行硬件联机运行；power API 时序、POWEROFF 下 FFM writeback 和 Streaming
  RF OFF 延迟仍需按第 17 节做实机验收。
