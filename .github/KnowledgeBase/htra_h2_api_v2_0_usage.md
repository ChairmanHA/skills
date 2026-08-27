# HTRA H2 API v2.0 使用与软件建模指南

本文档面向本仓库当前集成的 HTRA H2 API，即 `3rdParty/h2_api/include/h2_api.h` 与 `3rdParty/h2_api/include/h2_typedef.h` 中定义的 API v2.0.28。本文不只解释“怎么调函数”，还要说明这些函数组合背后的业务语义，以及软件为什么必须围绕 API 能力而不是围绕页面名字设计。

核心原则只有一句话：软件是 API 能力的展示层，不是另起一套真相源。UI、business、provider、runtime、orchestrator，都应该是对 API 能力边界的组织、约束和可视化，而不是脱离 API 再造概念。

> 约定：本文默认以单个 TX 通道 `ch[0]` 为例。多通道场景可把相同配置按 `channel[]` 数组批量作用到多个通道。

---

## 1. 先从 API 看业务，而不是先从 UI 看业务

如果只看 SGStudio 页面，容易把“CW 页”“Playback 页”“Streaming 页”“Sweep 页”误当成系统的一阶业务状态；但从 H2 API 看，设备真正暴露出来的是下面几组能力：

1. 设备会话能力：枚举、供电路由准备、打开、关闭、复位、查询版本与运行状态。
2. 设备级公共配置：参考时钟、GNSS 配置、GNSS 实时信息、供电、温度。
3. RF 载波计划：固定载波，或 FSCAN / LSCAN / MSCAN 这类扫描计划。
4. 基带来源：无基带（CW）、RAM Playback、实时 Stream。
5. 执行控制：触发源、触发动作、输出开关、启动、停止、总线触发、触发输出。

这意味着软件真正应该表达的不是“用户打开了哪个页签”，而是“当前设备运行在什么发射流水线”。页面只是参数编辑器，真正的运行态应由 API 能力组合推导出来。

---

## 2. H2 API 的对象模型

### 2.1 device 是设备会话

- `device_list_usb(device_info* devInfo, uint32_t* count)` 负责枚举当前 USB 设备。
- `device_powered_by_usb(uint8_t dnum)` / `device_powered_by_eth(eth_setting settings)` 是 open 前的供电路由辅助；SGStudio 当前在 HTRA 插件里仅对定义了 `HTRA_HAS_HLC_USB_POWER_ROUTING` 的 Linux ARM 目标启用这条路径，典型场景是 aarch64 / 树莓派环境下基于 `hlc` 的 USB 供电探测。Windows 桌面默认跳过这一步。
- `device_open_usb(void** device, uint8_t dnum, channel ch_o[], uint16_t* chn, device_info* dinfo)` 打开设备，并返回通道数组与设备信息。
- `device_close(void** device)` 关闭设备。
- `device_preset(void** device)` 做设备级复位，通常用于收尾或回到可预测状态。
- `device_query_state(void** device)` 可作为设备运行状态探针；它返回的仍然是 H2 状态码语义，而不是单纯布尔值。
- `device_query_options(void** device, uint32_t* opt_o, uint32_t* num_o)` 在 open 后读取本机实际生效选件。SGStudio 当前同时消费 `OPTION_MEDIUM_POWER` 与 `OPTION_BW_320M_TX`，不能再按 model 推测这两项物理能力。

`OPTION_BW_320M_TX` 当前直接决定 Playback capability：

- 返回该选件：采样率为 `[DATA_SAMPLE_RATE_MIN, 200 MSPS] U {400 MSPS}`，最大 payload 为 `1000 * 1024 * 1024` Bytes。
- 未返回该选件：采样率为 `[DATA_SAMPLE_RATE_MIN, 125 MSPS]`，最大 payload 为 `125 * 1024 * 1024` Bytes。
- 查询失败：软件记录 warning 并使用无选件基线，不推测扩展能力。

该判据适用于 model 122、132 以及 H2 API 能成功打开的其他 HTRA 型号；Streaming 仍使用软件链路独立的 62.5 MSPS 上限。

这里的 `device` 不是抽象配置对象，而是“本次硬件会话”的句柄。任何下载波形、GNSS 查询、参考时钟配置，最终都依赖这个会话句柄存在且有效。

### 2.2 channel 是设备内部的可执行发射对象

- `channel` 由 `device_open_usb()` 一次性返回。
- 后续绝大多数 TX 配置都作用在 `channel*` 上，例如：
  - `tx_config_ffm`
  - `tx_config_fscan`
  - `tx_config_lscan`
  - `tx_config_mscan`
  - `tx_config_cw`
  - `tx_config_playback`
  - `tx_config_stream`
  - `channel_config_trigger`
  - `channel_start`
  - `channel_stop`
  - `channel_bus_trigger`

这说明软件架构上应把“设备打开”和“通道配置”视为两层：

1. 设备层负责会话、能力、全局设置。
2. 通道层负责真正的发射运行态。

### 2.3 state 不是业务模式，而是开关量

`state` 只有 `STATE_OFF` 和 `STATE_ON` 两个值，典型用于：

- `tx_config_output(channel* ch, state rfout, state mod)`
- `trigger_out.enable`
- `gnss_info.locked`
- `device_config_clock(..., state out)` 的系统时钟输出开关

因此 RF 开关、MOD 开关、GNSS 锁定、TRG OUT 打开与否，本质上都是 API 原生布尔态，不应被 UI 语义污染。

### 2.4 返回码是业务流程控制的一部分

`h2_typedef.h` 当前约定：

- `STATUS_NOERROR = 0`：成功。
- 小于 0：错误，表示当前链路应立即中止，例如 `STATUS_ERROR_DISCONNECT = -8`。
- 大于 0：警告，典型如参数越界被钳位、超时、默认校准文件生效。

推荐的软件处理策略：

1. `< 0`：立即停止当前配置链路，必要时把设备标记为不可继续使用。
2. `> 0`：继续执行，但必须保留 warning 语义，不要吞掉。
3. 不要把负值压平成 `false` 或 `0` 再回写上层，否则软件会丢失关键设备语义。

---

## 3. 五个能力域，决定软件应该怎么分层

### 3.1 能力域 A：设备会话域

对应 API：

- `device_list_usb`
- `device_powered_by_usb`
- `device_powered_by_eth`
- `device_open_usb`
- `device_close`
- `device_preset`
- `device_query_state`
- `device_query_apiverion`
- `device_query_options`

这一层回答的问题是：

- 当前有哪些设备。
- 打开的到底是哪一台。
- 当前会话是否仍然有效。
- 设备关闭前是否需要复位收尾。

软件含义：这一层应由 DeviceManager / FancyDevice / session owner 持有，不能散落到各业务页自己偷偷 reopen。

### 3.2 能力域 B：设备级公共配置域

对应 API：

- `device_config_clock`
- `device_query_clock`
- `device_reset_clock_monitor`
- `device_config_gnss`
- `device_query_gnss`
- `device_query_gnss_info`
- `device_config_fan`
- `device_config_power_state`
- `device_query_temperature`
- `device_query_supply`

补充说明：`channel_config_lo_mode()` / `channel_query_lo_mode()` 虽然挂在 `channel*` 上，但语义上仍属于通道级公共硬件配置，而不是某个页面私有状态。

这一层回答的问题是：

- 参考时钟源是谁，外参频率是多少，系统时钟输出是否打开。
- GNSS 是否启用 PPS，PPS 频率与延时如何设置。
- 当前设备温度、电源、GNSS 锁定状态如何。
- 当前设备是否启用低功耗状态。

软件含义：这些参数是 pipeline-agnostic common settings。它们不属于某个 playback 页面，也不属于某个 sweep 页面，而是所有发射流水线共享的设备公共上下文。

### 3.3 能力域 C：RF 载波计划域

对应 API：

- `tx_config_ffm`
- `tx_query_ffm`
- `tx_config_fscan`
- `tx_query_fscan`
- `tx_config_lscan`
- `tx_query_lscan`
- `tx_config_mscan`
- `tx_query_mscan`

这一层回答的问题是：

- 当前 RF 是固定载波还是扫描。
- 如果是扫描，扫描表、驻留时间、频点或电平变化轨迹是什么。

软件含义：Sweep 不应被理解成“另一个独立业务页”，而应被理解成 RF carrier plan 的一种形态。

### 3.4 能力域 D：基带来源域

对应 API：

- `tx_config_cw`
- `tx_config_playback`
- `tx_config_stream`
- `tx_clear_waveform`
- `tx_download_waveform`
- `tx_send_stream`

这一层回答的问题是：

- 当前是纯 CW，没有基带。
- 当前基带来自设备 RAM 中的 waveform 列表。
- 当前基带来自主机实时持续下发。

软件含义：Playback / Modulation / Streaming 在更高层的软件语义上，应该更接近 provider 或 session adapter，而不是“系统最终运行态真相源”。

### 3.5 能力域 E：执行与触发域

对应 API：

- `channel_config_trigger`
- `channel_query_trigger`
- `channel_config_trigger_out`
- `channel_query_trigger_out`
- `channel_preset`
- `tx_config_output`
- `tx_query_output`
- `channel_start`
- `channel_stop`
- `channel_bus_trigger`

这一层回答的问题是：

- 输出是否真正打开。
- 触发从哪来。
- 每次触发是做 HOP 还是做 SWEEP。
- 通道是否已经进入可运行状态。
- 是否需要显式 BUS trigger 才开始执行。

软件含义：这一层决定的是执行时机与推进方式，不能和“参数编辑”混在一起。参数页只是写计划，真正进入运行态要靠 runtime 或 orchestrator 调这些执行 API 收口。

---

## 4. 从 API 能力组合推导主业务流水线

目前需求希望把`Sweep + Stream`放出来，下面有任何冲突，都以这一条为准则来推导软件设计。

基于 H2 API，软件最合理的一阶运行态可以整理为下面几类：

| 流水线 | RF 载波计划 | 基带来源 | 输出语义 |
| :--- | :--- | :--- | :--- |
| `Mute` | 无 | 无 | RF OFF, MOD OFF |
| `FixedCw` | `FFM` | `CW` | 固定载波连续波 |
| `SweepCw` | `FSCAN / LSCAN / MSCAN` | `CW` | 扫描连续波 |
| `FixedPlayback` | `FFM` | `Playback` | 固定载波回放 |
| `SweepPlayback` | `FSCAN / LSCAN / MSCAN` | `Playback` | 扫描回放 |
| `FixedStream` | `FFM` | `Stream` | 固定载波实时流式 |
| `SweepStream` | `FSCAN / LSCAN / MSCAN` | `Stream` | 扫描实时流式 |

把 `Sweep + Stream` 包装成标准流水线，其实有点小问题：

1. `Stream` 的本质是主机持续实时推送 IQ。
2. `Sweep` 的本质是设备按既定 RF 计划推进载波状态。
3. 两者同时成立时，软件必须同时满足“持续稳定的流式会话”和“按触发推进的扫描计划”，复杂度显著高于现有 API 暴露出的稳定组合。

---

## 5. 触发模型：API 实际在表达什么

### 5.1 输入触发 `tx_trigger`

`channel_config_trigger(channel* ch, const tx_trigger* trg)` 定义的是“收到一次触发时，通道要怎么推进”。

关键字段：

- `source`：触发从哪里来。
  - `TRIGGER_SOURCE_BUS`
  - `TRIGGER_SOURCE_EXTERNAL`
  - `TRIGGER_SOURCE_XPPS`
- `edge`：外部边沿类型，BUS 触发下通常只是保持显式配置一致。
- `action`：每次触发时做什么。
  - `TRIGGER_ACTION_HOP`：前进一步。
  - `TRIGGER_ACTION_SWEEP`：执行一次完整列表或完整扫描。
- `count`：响应触发的次数，`-1` 表示无限次。

软件建模含义：trigger policy 应独立建模。它既不等于 RF plan，也不等于 provider。

### 5.2 输出触发 `trigger_out`

`channel_config_trigger_out(channel* ch, const trigger_out* trg)` 定义的是“设备内部发生某类动作时，要不要从硬件端口打一个同步脉冲”。

这通常用于：

1. 给示波器、采集卡、频谱仪做边界同步。
2. 对齐每次 hop 或每次完整扫描的时间点。

### 5.3 BUS trigger 不是配置，而是执行命令

`channel_bus_trigger(const channel* ch, uint32_t stream_mask)` 的语义不是“写一个参数”，而是“对已经 start 且已完成配置的通道，主动送入一次 BUS 触发事件”。当前 API 的 `stream_mask` 尚未生效且只能传 `0`。

因此典型顺序通常是：

1. 先把 trigger policy 配好。
2. 再 `channel_start()`。
3. 最后根据 `source` 决定是等待外部事件，还是自己调用 `channel_bus_trigger(ch, 0)`。

---

## 6. 设备生命周期与公共流程

### 6.1 推荐的设备打开流程

```cpp
void* device = nullptr;
channel ch[MAXCHANNELS];
uint16_t chn = 0;
device_info dinfo{};
uint32_t count = 0;
device_info infos[16]{};

int status = device_list_usb(infos, &count);
if (status < 0 || count == 0) {
    return;
}

uint8_t dnum = 0; // 对应枚举数组索引
#if defined(HTRA_HAS_HLC_USB_POWER_ROUTING)
status = device_powered_by_usb(dnum);
if (status < 0) {
   return;
}
#endif
status = device_open_usb(&device, dnum, ch, &chn, &dinfo);
if (status < 0) {
    return;
}
```

注意点：

1. `device_open_usb()` 的第二个参数是设备枚举索引，不是 `device_info` 中的某个字段。
2. 若 HTRA 运行目标定义了 `HTRA_HAS_HLC_USB_POWER_ROUTING`，且设备需要走 USB 供电路由，应在 `device_open_usb()` / `device_open_eth()` 之前先调用对应的 `device_powered_by_*()`。
3. 打开后应记录 `uid_l64`、`uid_h32`、`hardware_version`、`mfw_version`、`ffw_version`、`pmu_version`、`agu_version`、`bus_version`、`eio_version`、`bus_bandwidth` 等真实设备字段。
4. 当前 `device_info` 字段名以 `_version` 结尾，不是旧文档里那种驼峰名。

### 6.2 推荐的设备关闭流程

```cpp
device_preset(&device);
device_close(&device);
```

建议原因：

1. 避免设备残留上一条流水线状态。
2. 让下一次 open 进入更可预测的初始状态。

### 6.3 什么时候需要显式 `channel_stop()`

不是每次配置前都需要 `channel_stop()`，常规流程通常直接走：

1. 配参数。
2. `channel_start()`。
3. 触发。

只有在下面场景才应显式 `channel_stop()`：

1. 当前有正在运行的 stream 或 sweep，需要明确退出旧执行态。
2. 你要做跨模式切换，且底层实现要求先停再配。
3. 软件层正在做 stop-then-reconfigure 事务。

如果要把通道的执行上下文一并清到初始态，可以额外调用 `channel_preset(channel* ch)`；它比单纯 `channel_stop()` 更接近“通道级复位”。

---

## 7. 软件设计该如何映射 API

### 7.1 UI 页面应该是参数编辑器，不是运行态真相源

从 API 看，设备并不知道“当前选中哪个页签”，它只知道：

1. 当前 RF plan 是 FFM 还是 scan。
2. 当前 baseband source 是 CW、Playback 还是 Stream。
3. 当前 trigger policy 是什么。
4. 当前输出是否打开，通道是否 start，是否已经收到 trigger。

因此软件设计上：

- `panel` 负责编辑参数。
- `provider` 负责准备数据或提供实时发送能力。
- `orchestrator` 负责根据全局状态裁决合法流水线。
- `runtime/session` 负责真正调用 API 进入运行态。

### 7.2 Common settings 应独立于 provider 页面

参考时钟、TriggerSource、TriggerCount、Center、Level、SystemClockOut 这一类公共参数，不应从属于具体页面。它们属于 API 的设备级公共配置或公共执行上下文。

这也是为什么 CommonPanel / CommonDeviceProfile 这类对象在当前架构里应继续存在，但其职责应是“保存公共上下文”，不是“偷着决定业务模式”。

### 7.3 Streaming 需要单独的 session 语义

从 API 角度看：

- `Playback` 是“先准备数据，再配置回放列表，再启动执行”。
- `Stream` 是“先进入 stream mode，再持续 `tx_send_stream()`”。

这决定了软件中 Streaming 不能被当成普通同步 apply 流水线，它天然更像一个长期运行的 session。参数变化往往需要 stop / reconfigure / restart，而不是立即同步 apply。

---

## 8. 通用推荐调用顺序

在 H2 API 当前语义下，绝大多数 TX 流水线都可以按下面顺序组织：

1. 配设备级公共参数。
2. 配 RF 载波计划。
3. 配触发策略。
4. 配基带来源。
5. 配输出开关。
6. `channel_start()`。
7. 根据触发源决定是否调用 `channel_bus_trigger(ch, 0)`。

翻译成 API 大致就是：

1. `device_config_clock()` / `device_config_gnss()` 等。
2. `tx_config_ffm()` 或 `tx_config_*scan()`。
3. `channel_config_trigger()`。
4. `tx_config_cw()` / `tx_config_playback()` / `tx_config_stream()`。
5. `tx_config_output()`。
6. `channel_start()`。
7. `channel_bus_trigger(ch, 0)` 或等待外部 / XPPS。

关键原则：

1. trigger policy 应先配置，trigger event 应后发生。
2. mode 配置和 waveform 下载必须在 `channel_start()` 之前完成。
3. `channel_start()` 之后是否立即开始，取决于触发源与设备固件语义，不应在软件中想当然。
4. 软件实现里应显式分支：只有 `TRIGGER_SOURCE_BUS` 才主动调用 `channel_bus_trigger(ch, 0)`；`TRIGGER_SOURCE_EXTERNAL / TRIGGER_SOURCE_XPPS` 应保持等待态。

---

## 9. 固定 CW：最小且最基础的流水线

目标：RF ON，MOD OFF，固定频点持续输出。

```cpp
int status = STATUS_NOERROR;

status = tx_config_ffm(&ch[0], 1.0e9, -10.0f);

tx_trigger trg{};
trg.source = TRIGGER_SOURCE_BUS;
trg.edge = TRIGGER_EDGE_RISING;
trg.action = TRIGGER_ACTION_SWEEP;
trg.count = -1;
status = channel_config_trigger(&ch[0], &trg);

status = tx_config_cw(ch);
status = tx_config_output(ch, STATE_ON, STATE_OFF);
status = channel_start(ch);
status = channel_bus_trigger(ch, 0);
```

业务逻辑上，这条链路可以理解为：

1. 用 `FFM` 定义固定载波。
2. 用 `CW` 指定“没有基带”。
3. 用 output 指定 RF 开，MOD 关。
4. 用 start + trigger 真正让通道进入执行态。

如果软件里要表达 “RF On + 没有任何 provider”，最终落点就应该是这条 `FixedCw` 流水线，而不是某个页面名。

---

## 10. Playback：设备 RAM 中的基带回放

### 10.1 Playback 的业务本质

Playback 不是“软件持续推流”，而是：

1. 主机先把 waveform 下载到设备。
2. 设备内部记住 waveform id 列表。
3. 通道运行时按 trigger policy 推进当前 waveform 列表。

因此 Playback provider 在软件中更像“数据准备器”，真正的设备执行仍应由 runtime 统一收口。

### 10.2 单波形推荐流程

```cpp
int status = tx_clear_waveform(&device);

uint16_t waveformId = 0;
status = tx_download_waveform(&device, iq, points, &waveformId);

status = tx_config_ffm(&ch[0], 1.0e9, -10.0f);

tx_trigger trg{};
trg.source = TRIGGER_SOURCE_BUS;
trg.edge = TRIGGER_EDGE_RISING;
trg.action = TRIGGER_ACTION_SWEEP;
trg.count = -1;
status = channel_config_trigger(&ch[0], &trg);

int16_t waveList[] = { static_cast<int16_t>(waveformId) };
int32_t repeat[] = { -1 };
double sampleRate[] = { 50e6 };
status = tx_config_playback(&ch[0], waveList, repeat, sampleRate, 1);

status = tx_config_output(ch, STATE_ON, STATE_ON);
status = channel_start(ch);
status = channel_bus_trigger(ch, 0);
```

如果触发源改为 `TRIGGER_SOURCE_EXTERNAL` 或 `TRIGGER_SOURCE_XPPS`，则上面最后一步不应改写成 BUS fire，而应停在 `channel_start()` 后等待硬件事件。

### 10.3 多波形与触发推进

`tx_config_playback()` 的核心不是“只配一个波形”，而是配一整个回放序列：

- `waveform[]`：设备中已下载好的 waveform id 列表。
- `repeat[]`：每个 waveform 被选中后自身重复多少次。
- `sample_rate[]`：每个 waveform 的采样率。

这里最容易误解的是：`repeat[i]` 控制的是“当前 waveform 内部如何重复”，而不是“整个列表如何切换”。整个列表如何推进，由 trigger action 决定。

### 10.4 `HOP` 和 `SWEEP` 在 Playback 中的区别

#### 每次触发切到下一个 waveform

适用于“每 1 秒打一枪，切到下一个波形”的需求：

- `trg.action = TRIGGER_ACTION_HOP`
- 每次 `channel_bus_trigger(ch, 0)` 前进一步

如果 `repeat[i] = -1`，设备会一直停在当前 waveform 上重复输出，直到下一次外部触发或 BUS trigger 发生。

#### 一次触发跑完整个 waveform 列表

适用于“给一次触发，设备自己把整个列表跑完”的需求：

- `trg.action = TRIGGER_ACTION_SWEEP`

但此时需要把两层时间语义分开理解：

1. `repeat[i]` 控制的是当前 waveform 自身循环多少次。
2. `FSCAN / LSCAN / MSCAN` 的 `dwell` 控制的是当前扫描点停留多久。

对于带扫描 dwell 的 Playback 组合场景，更合理的理解是：

1. `repeat[i] = -1` 可视作“当前点位内持续填充该 waveform”。
2. 当前点最终停留多久，仍由 sweep / mscan 的 `dwell` 边界决定。
3. 如果需要确定性的内容长度，更推荐使用有限 `repeat[i]`，并让 `dwell` 与内容总时长相匹配。

因此，在 ProgrammedArb 这类 RAM 受限场景里，通常应优先使用 `repeat[]` 复用已下载 waveform，而不是为了逻辑重复去扩展更多 IQ 数据占用 RAM。

---

## 11. Stream：实时流式发送

### 11.1 Stream 的业务本质

Stream 是“设备进入实时接收 IQ 帧的模式，然后主机持续调用 `tx_send_stream()`”。

也就是说，`tx_config_stream()` 只是把设备切到 stream mode，真正的数据面是在后续的 `tx_send_stream()` 循环里完成的。

### 11.2 推荐流程

```cpp
int status = tx_config_ffm(&ch[0], 1.0e9, -10.0f);

tx_trigger trg{};
trg.source = TRIGGER_SOURCE_BUS;
trg.edge = TRIGGER_EDGE_RISING;
trg.action = TRIGGER_ACTION_SWEEP;
trg.count = -1;
status = channel_config_trigger(&ch[0], &trg);

status = tx_config_stream(&ch[0], 50e6);
status = tx_config_output(ch, STATE_ON, STATE_ON);
status = channel_start(ch);
status = channel_bus_trigger(ch, 0);

for (;;) {
    status = tx_send_stream(&ch[0], iq, points);
    if (status < 0) {
        break;
    }
    if (status > 0) {
        // warning: 记录后继续，或按业务策略处理
    }
}
```

### 11.3 软件设计含义

Streaming 在软件中应被建模为“带生命周期的异步会话”：

1. start 之前要把公共参数、载波、触发、stream mode 全部配置好。
2. running 之后主要工作变成持续下发数据。
3. 参数变化时，很多情况下需要进入 stop / reconfigure / restart 事务。

这就是为什么 streaming business 不应简单退化为“同步生成一次数据再交给 runtime”，它天然有 session 语义。

---

## 12. Sweep：RF plan，而不是独立世界

### 12.1 FSCAN

`tx_config_fscan(channel* ch, double start, double stop, double step, float level, float dwell)` 表达的是：

- 固定功率 `level`
- 频率从 `start` 到 `stop`
- 按 `step` 推进
- 每个点驻留 `dwell` 秒

推荐流程：

```cpp
int status = tx_config_fscan(&ch[0], start, stop, step, level, dwell);

tx_trigger trg{};
trg.source = TRIGGER_SOURCE_BUS;
trg.edge = TRIGGER_EDGE_RISING;
trg.action = TRIGGER_ACTION_SWEEP;
trg.count = -1;
status = channel_config_trigger(&ch[0], &trg);

status = tx_config_cw(ch);
status = tx_config_output(ch, STATE_ON, STATE_OFF);
status = channel_start(ch);
status = channel_bus_trigger(ch, 0);
```

### 12.2 LSCAN

`tx_config_lscan(channel* ch, double fc, float level_start, float level_stop, float level_step, float dwell)` 表达的是：

- 固定中心频率 `fc`
- 功率从 `level_start` 到 `level_stop`
- 按 `level_step` 推进
- 每点驻留 `dwell`

它仍然是 RF 计划变化，不是基带变化。

### 12.3 MSCAN

`tx_config_mscan(channel* ch, const double fc[], const float level[], const float dwell[], int16_t n)` 表达的是离散点表扫描。每个点都可以有自己的频率、功率和驻留时间。

推荐流程：

```cpp
double fc[] = { 1e9, 2e9, 3e9, 4e9 };
float level[] = { -30.0f, -20.0f, -10.0f, 0.0f };
float dwell[] = { 1e-3f, 10e-3f, 20e-3f, 30e-3f };

int status = tx_config_mscan(ch, fc, level, dwell, 4);

tx_trigger trg{};
trg.source = TRIGGER_SOURCE_BUS;
trg.edge = TRIGGER_EDGE_RISING;
trg.action = TRIGGER_ACTION_SWEEP;
trg.count = -1;
status = channel_config_trigger(ch, &trg);

trigger_out trgout{};
trgout.enable = STATE_ON;
trgout.action = TRIGGER_ACTION_HOP;
trgout.edge = TRIGGER_EDGE_RISING;
status = channel_config_trigger_out(ch, &trgout);

status = tx_config_cw(ch);
status = tx_config_output(ch, STATE_ON, STATE_OFF);
status = channel_start(ch);
status = channel_bus_trigger(ch, 0);
```

如果需要读回点表：

```cpp
int16_t nQuery = 64;
std::vector<double> fcQuery(nQuery);
std::vector<float> levelQuery(nQuery);
std::vector<float> dwellQuery(nQuery);

int status = tx_query_mscan(&ch[0], fcQuery.data(), levelQuery.data(), dwellQuery.data(), &nQuery);
```

### 12.4 Sweep 对软件建模的直接启示

Sweep 这组 API 说明：

1. Sweep 首先是 RF domain 的事情。
2. Sweep 与 CW、Playback 的组合是自然的，因为它们只是改变“有没有基带”。
3. 软件应把 sweep 参数保存在 carrier plan 中，而不是藏到某个业务页私有状态后又试图反推。

---

## 13. 参考时钟与 GNSS：设备公共上下文的一部分

### 13.1 参考时钟

相关 API：

- `device_config_clock(void** device, referenceclock_source source, double fref, state out)`
- `device_query_clock(void** device, referenceclock_source* source_o, double* fref_o, state* out_o, state* locked_o)`
- `device_reset_clock_monitor(void** device)`

业务语义：

1. `source` 决定参考时钟来自内部还是外部。
2. `fref` 在外部参考时钟下尤其重要。
3. `out` 决定系统时钟是否向外输出。

软件设计含义：参考时钟属于设备级公共配置，应该放在统一公共 profile 中，而不是由某个 TX provider 自己偷偷维护。

当 `locked_o` 指示系统时钟监控已进入异常态时，软件可把 `device_reset_clock_monitor()` 视为一次显式恢复动作，而不是隐式塞进普通 apply 流程。

### 13.2 GNSS 配置

相关 API：

- `device_config_gnss(void** device, gnss_setting gnss)`
- `device_query_gnss(void** device, gnss_setting* gnss_o)`

`gnss_setting` 当前包含：

- `antenna`
- `pps_en`
- `pps_x`
- `pps_delay`

也就是说，GNSS 在 API 层不仅是“查询有没有定位”，还包含可配置的 PPS 行为。软件如果要展示 GNSS，不应只做一个状态灯，而应把它视为设备公共能力的一部分。

### 13.3 GNSS 实时信息

```cpp
gnss_info g{};
int status = device_query_gnss_info(&device, &g);
if (status == STATUS_NOERROR) {
    // g.locked
    // g.sat_nums
    // g.snr_max / g.snr_avg / g.snr_min
    // g.latitude / g.longitude / g.altitude
    // g.ns_sinceepoch
}
```

当前 `gnss_info` 字段名是 `ns_sinceepoch`，不是旧写法 `nsSinceEpoch`。

软件建议：

1. GNSS 实时状态可按固定周期轮询，例如 1 Hz。
2. 若设备断连判据另有统一机制，GNSS 查询失败不应单独把设备判死。
3. GNSS 实时信息应经由共享状态总线分发到 UI，而不是由单个对话框自己直接轮询底层。

### 13.4 XPPS 触发

```cpp
tx_trigger trg{};
trg.source = TRIGGER_SOURCE_XPPS;
trg.edge = TRIGGER_EDGE_RISING;
trg.action = TRIGGER_ACTION_SWEEP;
trg.count = 1;

int status = channel_config_trigger(&ch[0], &trg);
status = channel_start(ch);
// 不调用 channel_bus_trigger，等待 XPPS 上升沿
```

前提：使用 XPPS 之前，软件至少应满足以下条件：

1. 先通过 `device_query_gnss()` / `device_config_gnss()` 确认 `pps_en == 1`，否则设备不会产生可供 XPPS 使用的秒脉冲。
2. 再确认 `gnss_info.locked == STATE_ON`；如果未锁星，设备即使进入 armed 状态，也未必会有可靠的 XPPS 边沿到来。

UI 层更稳妥的做法不是在下发阶段偷偷改写 XPPS 参数，而是直接把 Trigger In 的 XPPS 选项按这两个条件置灰；只有 `pps_en == 1 && locked == STATE_ON` 时才允许选中 XPPS。

当前 SGStudio 的 HTRA 实现还会在 `FancyDevice::open()` 时做一次 GNSS bootstrap：设置 `pps_en = 1`，`device_config_gnss()`，随后再通过对话框侧的 `refreshGnssConfig()` 把设备最终接受值回显到 UI。

一旦 XPPS 因失锁或 `pps_en = 0` 变为不可用，SGStudio 的公共 TriggerSource 会默认退化到 `BUS`，以保证后续 apply / reapply 仍有一个内部可执行的触发源；当 GNSS 恢复时不会自动切回 XPPS。

### 13.5 供电、风扇与通道硬件公共设置

相关 API：

- `device_query_state(void** device)`
- `device_query_supply(void** device, supply_info* supplyInfo_o)`
- `device_config_fan(void** device, fan_mode mode, float auto_threshold)`
- `device_config_power_state(void** device, power_state state)`
- `channel_config_lo_mode(channel* ch, lomode mode)`
- `channel_query_lo_mode(channel* ch, lomode* mode_o)`

软件语义上，这几项都属于“运行中的硬件健康与公共硬件配置”：

1. `device_query_state()` 更适合作为状态探针或排障入口，不要把它误建模成 UI 勾选态。
2. `device_query_supply()` 返回 RF/USB 两组电压电流，适合设备信息页、诊断页或日志采样。
3. `device_config_fan()` 是设备级控制，`auto_threshold` 只在 `FAN_AUTO` 下真正有意义。
4. `device_config_power_state()` 是无 query 对应项的设备级快捷配置。SGStudio 的 Low Power ON 映射 `POWEROFF`，OFF 映射 `POWERON`；PGA 单口供电设备每次 open 默认 ON，其他设备默认 OFF。
5. `channel_config_lo_mode()` / `channel_query_lo_mode()` 虽然挂在 `channel` 上，但它们仍属于公共硬件上下文，不能被某个 provider 页面偷偷私有化。

由于 H2 API 不提供 power-state query，软件只能缓存本次 open 默认值和最近一次成功配置值。该缓存用于 Device Settings UI 回显，不应加入业务 CommonDeviceProfile 或在 pipeline reapply 时重复下发。

### 13.6 Epoch 时间转换

`device_epoch_to_utc(uint64_t ns_sinceepoch, ...)` 提供了把 GNSS 或设备时间戳转为 UTC 可读字段的标准入口。若软件要展示 `gnss_info.ns_sinceepoch` 的人类可读时间，优先用这条 API，而不是在上层重新实现一套纳秒到日期时间的转换逻辑。

---

## 14. 查询类 API 的作用：回读真实设备态，而不是猜测

当前 API 提供了多类 query：

- `tx_query_ffm`
- `tx_query_fscan`
- `tx_query_lscan`
- `tx_query_mscan`
- `channel_query_trigger`
- `channel_query_trigger_out`
- `channel_query_lo_mode`
- `tx_query_output`
- `tx_query_stream`
- `tx_query_playback`
- `device_query_state`
- `device_query_options`
- `device_query_clock`
- `device_query_supply`
- `device_query_gnss`
- `device_query_gnss_info`

这些函数的价值不只是“调试时看看”，更重要的是：

1. 做配置 writeback。
2. 做 UI 与设备真实状态同步。
3. 做设备 reopen 后的状态恢复和对账。

软件上层不应仅凭上一次 UI 输入值自认为“设备已经是这个状态”，而应尽量通过 query 或显式缓存策略维护真实设备态。

另有 `tx_test_fscan()`、`tx_test_lscan()`、`tx_test_mscan()` 这组三个 preview/test API。它们不是 query，而是“把待写参数先拿给底层做限制校验和钳位预演”的 helper，更适合用于 UI 预览、参数编辑回写和 apply 前快速验证。

---

## 15. 建议的软件抽象边界

如果要围绕 API 能力组织软件，推荐至少有下面几层：

1. `DeviceSession`
   - 持有 `device` 句柄、`channel[]`、`device_info`。
   - 负责 open / close / preset / disconnect handling。

2. `CommonExecutionContext`
   - 负责参考时钟、TriggerSource、TriggerCount、SystemClockOut、固定载波 fallback 等公共参数。

3. `CarrierPlan`
   - `Fixed / FScan / LScan / MScan`
   - 保存 RF 计划参数。

4. `BasebandProvider`
   - `None / Playback / Stream`
   - 负责准备 waveform、描述采样率、或提供实时发送能力。

5. `TriggerPolicy`
   - `source / edge / action / count`

6. `TxPipelineRuntime`
   - 根据前四类信息调用 API，统一完成进入、退出和重配。

7. `StreamingSession`
   - 仅在 Stream 场景下维护持续发送与异步重配事务。

这套分层不是软件自嗨，而是对 H2 API 五个能力域的直接映射。

---

## 16. 实施与排障建议

1. 不要把“当前 UI 选中了哪个页面”当成设备运行态真相源。
2. 不要把返回码压平；负值、正值都要保留语义。
3. 对 Playback，先想清楚 waveform list 的推进策略，再决定 `repeat` 与 trigger action。
4. 对 Stream，把它当 session，不要当一次性 apply。
5. 对 Sweep，把它当 RF carrier plan，而不是独立宇宙。
6. 对 GNSS / 时钟，把它们当所有 pipeline 共享的公共上下文。
7. 当 query API 不能完整表达某些软件期望状态时，再做本地缓存，但必须明确是“软件补充缓存”，不是“设备真相”。

---

## 17. 一句话总结

H2 API 真正暴露的是一套“设备会话 + 公共配置 + RF 计划 + 基带来源 + 执行控制”的发射系统。软件如果顺着这套能力边界设计，UI、provider、runtime、orchestrator 的职责会自然清晰
