# Playback 波形参数的设备能力约束策略（按 OPTION_BW_320M_TX 选件分档）

日期：2026-07-20

## 1. 目的与范围

本文回答一个统一问题：当新设备提供更高采样率、更大的 Playback 内存以及非连续的 400 MSPS 单点时，每种波形 business 应如何把设备能力转换为用户可用的参数范围。

当前实际注册并纳入本文的 business 为：

| 插件 | 当前启动的 business |
| :--- | :--- |
| HTRA | AM、FM、PM、Pulse、Multitone、Ramp、AWGN、ARB Playback |
| Analog | Digital Modulation、DSSS、OFDM |

明确排除：

- Streaming：受 USB 实时吞吐限制，有独立的 62.5 MSPS 上限，不属于本文的 Playback 参数模型。
- Quick Waveform：属于预置文件入口，不是这里讨论的可编辑波形 business。

model 122、132 及其他 HTRA 型号统一使用设备 open 后的 `device_query_options()` 结果分档，不再由型号推断 Playback 能力。本文后续所称“无带宽选件档”表示未返回 `OPTION_BW_320M_TX`，“含带宽选件档”表示返回该选件。

## 2. 设备能力基线

### 2.1 能力对比

| 能力 | 无带宽选件档 | 含带宽选件档 |
| :--- | :--- | :--- |
| 连续采样率 | 195.3125 kS/s~125 MSPS | 195.3125 kS/s~200 MSPS |
| 离散采样率 | 无 | 400 MSPS 精确单点 |
| 200~400 MSPS | 不适用 | 空洞，只有200/400 MSPS 有效 |
| 最大 Playback payload | 125 MiB | 1000 MiB |
| 最大复样本数 | 32,768,000 | 262,144,000 |


### 2.2 400 MSPS 的解释和处理

400 MSPS 是一个独立工作点：

```text
[195.3125 kS/s, 200 MSPS] U {400 MSPS}
```

所有 business 必须遵守：

1. 200~400 MSPS 的普通请求优先向连续 200 MSPS 档收口，不允许把 250、300、320 MSPS 等请求直接“就近抬升”为 400 MSPS，除非该 business 明确定义了与 400 MSPS 对应的离散参数点，例如 AWGN/Ramp 的精确 320 MHz 带宽点。
2. 只有参数组合能够精确形成 400 MSPS，或者连续 200 MSPS 确实无法满足硬质量约束时，才允许使用 400 MSPS。
3. 设备从含带宽选件档切换到无带宽选件档后，任何 400 MSPS 请求都必须重新解析。

## 3. 统一约束流水线

每个 business 应按相同顺序处理参数：

```text
UI/Profile
  -> 设备无关基础合法化
  -> 根据 当前设备能力 推导可行采样率/点数集合
  -> 按该 business 的采样率偏好选择候选
  -> 把时间、带宽、Rb、SPS 等量化结果永久回写
  -> 计算 source layout 与最终补齐 layout
  -> 容量门禁
  -> 提交精确 波形生成参数
  -> generator 生成唯一 immutable payload
```

### 3.1 硬约束与偏好

硬约束包括：

- 采样率必须属于 当前设备的 `SampleRateDomain`。
- 参数间的物理关系必须成立，例如 `Width <= Period`、`SweepTime <= Period`。
- 周期闭环、偶数 SPS、FFTSize 等离散约束必须成立。
- 最终生成的 波形长度 不得超过 当前设备 `maxWaveformBytes`，除非用户显式允许阶段下发。

偏好包括：

- 在多个合法采样率之间选择较低值以减少内存。
- Pulse 为改善边沿而在合理内存范围内选择较高值。
- 优先连续档，避免无意义使用 400 MSPS。

偏好不能破坏硬约束，也不能作为 generator 内部读取设备能力的理由。

### 3.2 PropertyMetadata 边界

PropertyMetadata 只保留简单、连续、设备无关的限制，例如：

- 时间、采样率、带宽大于 0。
- Count 至少为 2。
- 百分比在 0~100。

以下约束不得编码成一组固定 metadata 范围：

- 200~400 MSPS 空洞。
- `Width/Period/Fs/容量` 的联动。
- `Span` 对 Ramp `Period` 上限的影响。
- Digital/DSSS 的 SPS 可用项。
- OFDM `FFTSize/GuardInterval/symbolCount` 的容量组合。

这些约束由 business resolver 处理，并把最终值永久回写到 property。

### 3.3 容量策略

设备容量是硬上限，但并不意味着普通生成应该主动填满全部容量。

- 无带宽选件档：125 MiB 是硬上限。
- 含带宽选件档：1000 MiB 是硬上限。
- 对 Digital/DSSS/OFDM/ARB，业务已有明确的长度或文件语义，可以直接以硬上限决定可生成/可加载长度。
- 对 Pulse 这类“提高 Fs 只会增加时域采样密度”的业务，建议增加 125 MiB 的软预算；只有为了满足窄脉宽、超长周期或用户显式参数才突破软预算，1000 MiB 仍为最后硬门禁。
- Save IQ 与 Playback 使用相同的 plan 和容量检查；不能允许保存一份设备无法生成或播放的超大波形。

### 3.4 参数归一化与设备切换策略

当前十类参数生成型 Playback business 都把合法化后的 profile 解析为总函数。用户输入不能直接形成 plan 时，应按各业务的明确优先级回写成可用组合，而不是维护最近成功配置并回滚：

| 场景 | 处理 |
| :--- | :--- |
| 用户、远程控制或 `setProfile()` 编辑后的参数可通过安全量化继续工作 | 静默回写参数，提交新 plan |
| 十类参数生成型业务的普通编辑 | resolver 在当前产品参数范围和支持设备能力下是总函数；统一量化并提交 plan，不维护失败回滚状态 |
| PM 请求的 Rate/PhaseDeviation 超过连续采样率能力 | 保留 PhaseDeviation，优先降低并永久回写 Rate |
| Multitone 请求超过当前能力 | 先把Count收口到产品范围2～1024并保证FreqSpacing至少1 kHz；之后保留Count，优先降低并永久回写FreqSpacing，只有1 kHz仍无法满足时才降低Count |
| 上述总函数 resolver 返回空结果 | 视为 capability、算术或实现契约被破坏，只做内部断言和 release-safe 返回，不触发业务 reset、Enabled 变化或旧参数恢复 |
| 新设备仍支持当前参数或可通过安全量化支持 | 静默保留或回写参数，提交基于新能力的 plan |
| 设备切换导致 PM/Multitone 成功回写参数 | 关闭 Enabled，保留归一化后的可用组合，不恢复默认参数 |
| Digital/DSSS/OFDM 的设备兼容性预检明确失败 | 关闭 Enabled，并按各自既有策略恢复整组默认参数 |
| 用户显式 reset | 关闭 Enabled，恢复整组默认参数；不得因默认参数求解失败而回滚到 reset 前的配置 |
| ARB 文件本身超过新设备容量并被自动卸载 | 清空文件和 UI 参数、关闭 Enabled，并弹出一次提示 |
| ARB 文件仍兼容，但 Period 补零后超容量 | 保留文件，静默复位 Offset/SamplesToUse/Period，不弹窗 |

PM 和 Multitone 不再保留可恢复 plan 失败：两者分别通过 Rate 与 FreqSpacing/Count 回退获得可用组合。Digital/DSSS/OFDM 的设备不兼容仍由设备切换预检处理，Pulse/Ramp/AWGN 继续在设备量化后关闭 Enabled。ARB 和 Streaming 不使用同一套参数 profile 到 generation plan 的生成流程，继续遵守各自策略。


## 4. 总览：无/有带宽选件档的采样率选取差异

| Business | 采样率主要来源 | 无带宽选件档 | 含带宽选件档 | 400 MSPS 应用规则 |
| :--- | :--- | :--- | :--- | :--- |
| AM | `Rate * 2^k`，`k>=2` | 取最小合法周期格点 | 连续档扩大到 200 MSPS | 仅精确周期格点且没有更合理连续候选时使用 |
| FM | 偶数周期格点 + 调制带宽下限 | 上限 125 MSPS | 上限 200 MSPS，通常已覆盖当前参数范围 | 硬带宽需求超过 200 且精确对齐时才使用 |
| PM | 偶数周期格点 + 相位调制等效带宽 | 超出125 MSPS时优先回退 Rate | 最高约160 MSPS的现有组合可直接保留 | 当前参数范围通常不需要 400 MSPS |
| Pulse | Width/Period 样点格点 + 时域质量 | 6/8点约束；产品边界48/96 ns | 连续档可到30/40 ns；400档可到15/20 ns | 连续档无法满足窄脉宽质量时使用 |
| Multitone | tone 占用带宽 + tone lattice | 连续 125 MSPS 边界 | 连续 200 MSPS，精确组合可到 400 | 只保留精确 400 MHz lattice |
| Ramp | `Fs >= 1.25 * Span` + Period 闭环 | Span 最大 100 MHz | 连续 Span 最大 160 MHz，另有 320 MHz 单点 | 仅精确 320 MHz Span 对应 400 MSPS |
| AWGN | `Fs = 1.25 * Bandwidth` | Bandwidth 最大 100 MHz | 连续最大 160 MHz，另有 320 MHz 单点 | 仅精确 320 MHz Bandwidth |
| ARB | 文件/用户指定采样率 | 125 MSPS、125 MiB | 200 MSPS + 400 单点、1000 MiB | 只有文件/参数精确为 400 MSPS |
| Digital | 普通 `Rb*sps`；FSK 还受频偏约束 | 动态筛选 SPS，125 MiB trim | 更多 SPS/速率组合、1000 MiB trim | 公式精确命中 400 时可用 |
| DSSS | `Rb*sps*(2^code-1)` | 动态筛选 SPS，125 MiB 长度 | 更多组合、1000 MiB 长度 | 公式精确命中 400 时可用 |
| OFDM | 用户 SampleRate + FFT/符号容量 | SampleRate 到 125，symbolCount 受 125 MiB 限制 | 到 200 + 400，symbolCount 受 1000 MiB 限制 | 仅用户参数精确为 400 MSPS |

## 5. 多采样率候选业务的详细决策

### 5.1 AM

AM 使用一个完整调制周期作为源波形：

$$
F_s=Rate\times N,\qquad N\in\{4,8,16,\ldots\}
$$

#### 推荐决策

1. `N>=4` 是算法硬下限。
2. 从最小 `N` 开始，在 current domain 中选择第一个合法格点，保持当前“小采样率优先”的行为。
3. 不因为含带宽选件档具有 200/400 MSPS 就自动增加 `N`；AM 只有一个周期，最低合法格点通常已经提供足够点数，而且最终下载还会受统一最小 payload 补齐。
4. 如果以后实测证明 `N=4` 的高 Rate 正弦包络质量不足，应新增明确的设备无关质量目标，例如 `Npreferred=8`，而不是简单改成“总取设备最大 Fs”。

#### 两档差异

- 无带宽选件档在 125 MSPS 内寻找最小合法 `Rate*2^k`。
- 含带宽选件档把连续搜索上限扩展到 200 MSPS；只有精确的 400 MSPS 周期格点且连续档没有合理候选时才考虑离散点。
- 当前 Rate 范围为 1 Hz~10 MHz，常用组合在无带宽选件档已有解，因此含带宽选件档的主要价值是覆盖边缘周期格点，而不是让普通 AM 自动变成大波形。
- 设备切换后当前 Rate 无解时，关闭 Enabled 并静默恢复默认 AM profile。

### 5.2 FM

FM 要求：

$$
F_s=n\times Rate,\qquad n\ge6,\quad n\text{ 为偶数}
$$

$$
F_s>2\times(Rate+Deviation)
$$

#### 推荐决策

1. 频域下限、偶数周期格点和容量都是硬约束。
2. 在 current domain 中选择满足硬约束的最小合法 `n`；额外提高 Fs 对一个完整周期的本地 FM 合成收益有限，却会增加最小补齐后的数据量。
3. 先搜索连续档。只有硬下限已经高于 200 MSPS，并且 400 MSPS 同时满足整数周期格点时，含带宽选件档才可使用 400 MSPS。
4. 不为了命中 400 MSPS而放大 Rate 或 Deviation。

#### 两档差异

- 无带宽选件档的合法上限为 125 MSPS。
- 含带宽选件档连续上限为 200 MSPS。
- 在当前 `Rate<=10 MHz`、`Deviation<=10 MHz` 的产品范围内，最坏常规组合也通常可在 125 MSPS 内找到偶数周期格点，因此两种设备大多数情况下行为相同。
- 含带宽选件档主要提供边缘参数与格点容错，不应导致默认 FM 采样率无条件升高。

### 5.3 PM

PM 使用与 FM 相同的偶数周期格点，但等效频偏为：

$$
Deviation_{eq}=PhaseDeviation\times Rate
$$

$$
F_s>2\times Rate\times(1+PhaseDeviation)
$$

#### 推荐决策

- 与 FM 一样选择最小合法连续格点。
- 当前 `PhaseDeviation<=2\pi`，当 `Rate=10 MHz` 时，最小偶数格点约需 `n=16`，即 `160 MSPS`。
- 这一组合在无带宽选件档不能保持 10 MHz Rate；resolver 保留 PhaseDeviation 和其他波形参数，把 Rate 回写为 `125 MHz / 16 = 7.8125 MHz`。
- 在含带宽选件档的连续 200 MSPS 范围内，`Rate = 10 MHz` 可直接使用 `160 MSPS`；这是带宽选件带来的真实参数能力扩展。
- 当前参数范围通常不需要 400 MSPS，不能因为设备存在该单点就主动选择它。
- 从含带宽选件档切换到无带宽选件档后，如果当前 PM 组合需要降低 Rate，则永久回写 Rate 并关闭 Enabled，不恢复整组默认 profile。

### 5.4 Pulse：动态 Width、Period 与采样率

Pulse 是本文最重要的多候选业务。它不是由带宽公式唯一确定 Fs，而是在时域分辨率、周期长度和内存之间选择。

#### 5.4.1 已实施的行为变化

旧实现曾包含以下问题：

- Width/Period 固定下限 48 ns。
- Period 固定上限 1 s。
- `Period > 0.25 s` 时放弃严格格点并把 Fs 压到不超过 10 MHz。
- resolver 在普通分支选择最大合法格点。

这套策略没有正确表达设备能力。特别是：

```text
Width = 48 ns, Fs = 10 MHz
round(Width * Fs) = 0 samples
```

当前实现已经删除长周期10 MHz特例，并把质量约束改成明确的整数样点 plan。

#### 5.4.2 当前硬约束

参考无带宽选件档的产品基线：

```text
48 ns * 125 MHz = 6 samples
```

把固定时间常量改成采样点约束：

$$
N_{width}\ge6
$$

$$
N_{period}\ge8
$$

$$
N_{width}\le N_{period}
$$

$$
N_{period}\times4\le maxWaveformBytes
$$

Period 点数不再要求为8的倍数；否则 `48 ns / 96 ns @ 125 MSPS` 的6/12点组合会被错误拒绝。算法层仍保留现有半幅边沿序列，本次能力重构不改变波形合成算法。

#### 5.4.3 理论最小参数

| 档位 | 6 点最小 Width | 8 点最小 Period |
| :--- | ---: | ---: |
| 无带宽选件档 / 125 MSPS | 48 ns | 产品边界96 ns（12点） |
| 含带宽选件连续档 / 200 MSPS | 30 ns | 40 ns |
| 含带宽选件离散档 / 400 MSPS | 15 ns | 20 ns |

这些值由 business resolver 按 current capability 执行；PropertyMetadata只保留全产品连续外框。

#### 5.4.4 候选采样率的决策顺序

对用户输入 `Width=W`、`Period=P`：

1. 计算硬可行区间：

$$
F_{s,min}=\frac{6}{W}
$$

$$
F_{s,max,hard}=\frac{N_{device}}{P}
$$

2. 先在连续 domain 内构造候选，并把 Width/Period 量化成：

$$
W' = \frac{N_{width}}{F_s}
$$

$$
P' = \frac{N_{period}}{F_s},\qquad N_{period}\ge8
$$

最终还要保证 `N_period>=N_width`。business 优先选择 Width/Period 同时精确落点的连续格点；无精确格点时选择最接近用户输入且满足6/8点的整数 plan，并永久回写。

3. 对候选使用以下词典序：

   1. 硬约束全部满足。
   2. Width/Period 的量化误差最小。
   3. 量化误差相同时优先连续档。
   4. 在同一档内，在软内存预算允许时选择较高 Fs，以增加边沿和脉内样点。

4. 含带宽选件档只有在连续 200 MSPS 无法保持至少 6 个 Width 样点，或者用户的 Width/Period 只有在 400 MSPS 才能精确落格、而连续档必须回写时，才考虑 400 MSPS。不能仅凭“误差更小”就越过连续档。

这意味着“Pulse 可以适当用较高采样率”，但不是“永远选择设备最大采样率”。

#### 5.4.5 Pulse 软预算

当前实现使用：

```text
preferredPulsePayloadBytes = min(device.maxWaveformBytes, 125 MiB)
```

- 短周期下软预算不会限制 125/200 MSPS，可以选择较高连续档改善波形。
- 长周期下先按软预算降低 Fs，避免普通参数直接生成接近 1000 MiB 的波形。
- 如果窄 Width 的 6 点硬约束要求更高 Fs，可以突破软预算，但仍不得超过设备1000 MiB硬上限。

软预算只影响候选优先级，不是参数合法性上限。

#### 5.4.6 动态最大 Period

不同 Fs 下的硬容量上限为：

| 设备/采样率 | 最大单周期时长 |
| :--- | ---: |
| 无带宽选件档 / 125 MSPS | 0.262144 s |
| 含带宽选件档 / 400 MSPS | 0.65536 s |
| 含带宽选件档 / 200 MSPS | 1.31072 s |
| 含带宽选件档 / 125 MSPS | 2.097152 s |
| 含带宽选件档 / 195.3125 kS/s | 1342.17728 s |

这些值不能理解成一个全局 Period 上限。实际 `PeriodMax` 还取决于 Width 要求的最低 Fs：

$$
Period_{max}(W)=\max_{F_s\in feasible(W)}\frac{N_{device}}{F_s}
$$

例如 15 ns Width 只能使用 400 MSPS，因此即使设备有1000 MiB，Period 也不能超过0.65536 s；宽脉冲可以降低 Fs，得到更长 Period。

#### 5.4.7 回写和 generation plan

business 当前提交 Pulse 专用 plan：

```cpp
struct PulseGenerationPlan
{
    WaveformGenerationPlan playback;
    quint64 widthSamples;
    quint64 periodSamples;
};
```

- business 计算并回写 `W'`、`P'`。
- generator 只消费明确的样点数，不再重复通过 double 执行 `round()`。
- 从含带宽选件设备的窄脉冲切换到无带宽选件设备时，静默回写到当前基线档的最近安全格点并关闭 Enabled；不弹窗。
- 如果没有任何可行格点，才恢复默认 `1 ms / 2 ms`。

### 5.5 Ramp

Ramp 的基础带宽约束为：

$$
F_s\ge1.25\times Span
$$

并要求 Period 尽量形成整数样点闭环。

#### 推荐决策

1. Ramp 选择满足带宽和周期闭环的最小合法采样率。超过 `1.25*Span` 的额外采样率对 chirp 质量收益有限，却会线性缩短可用 Period。
2. 普通 Span 只搜索连续档；不要因为 Period 的浮点格点恰好无法对齐就跳到 400 MSPS，应先量化 Period 并回写。
3. 含带宽选件档的 400 MSPS 只服务精确 `Span=320 MHz`。
4. Ramp 不再保留独立的 `Period<=1 s` 产品上限；与AWGN一致，唯一上限为current payload容量除以最终采样率。低采样率可生成超过1秒的波形，高采样率会得到更短的动态上限。

#### 两档差异

| 项目 | 无带宽选件档 | 含带宽选件档 |
| :--- | :--- | :--- |
| 连续 Span 上限 | 100 MHz，对应125 MSPS | 160 MHz，对应200 MSPS |
| 离散 Span | 无 | 320 MHz，对应400 MSPS |
| 20 MHz Span | 最小Fs 25 MSPS，Period最大约1.31072 s | 同样优先25 MSPS，Period最大约10.48576 s |
| 最大连续 Span 的容量 Period | 100 MHz 时约0.262144 s | 160 MHz 时约1.31072 s |
| 320 MHz Span | 不支持 | 400 MSPS 下约0.65536 s |

设备切换后依次收口 `Span -> Period -> SweepTime`，永久回写并关闭 Enabled，不弹窗。

### 5.6 AWGN

AWGN 的采样率由带宽基本唯一确定：

$$
F_s=resolveDomain(1.25\times Bandwidth)
$$

因此它虽然理论上可选择不同 Fs，但产品决策没有 Pulse 那样的自由度。

#### 推荐决策

- 普通输入只使用连续档。
- 无带宽选件档：Bandwidth 最大100 MHz。
- 含带宽选件档：连续 Bandwidth 最大160 MHz；精确320 MHz 对应400 MSPS单点。
- 160~320 MHz 之间的输入回写到160 MHz，不抬升到320 MHz。
- Length 由容量直接反推：

$$
Length_{max}=\frac{N_{device}}{F_s}
$$

| 带宽/档位 | 无带宽选件档最大 Length | 含带宽选件档最大 Length |
| :--- | ---: | ---: |
| 100 MHz / 125 MSPS | 0.262144 s | 2.097152 s |
| 160 MHz / 200 MSPS | 不支持 | 1.31072 s |
| 320 MHz / 400 MSPS | 不支持 | 0.65536 s |

窄带 AWGN 会使用较低采样率，因此允许更长 Length；Bandwidth/Length 在设备切换时静默回写，调整后关闭 Enabled。

## 6. 采样率争议较少的业务

### 6.1 Multitone

完整 tone lattice 的最低采样率为：

$$
F_{s,required}=1.25\times OccupiedSteps\times FreqSpacing
$$

策略保持简洁：

- 先尝试在 current domain 中选择 tone fundamental 的整数倍；若边界值不是整数倍，但能由 current capacity 内的 sampleCount 形成精确有理周期，则保留该合法 Fs。
- `Count` 的产品范围固定为 `2..1024`，`FreqSpacing` 产品下限固定为 `1 kHz`；该产品范围先于设备能力解析生效，preset和远程入口不能绕过。
- 无带宽选件档把 `Count/FreqSpacing` 收口在125 MSPS边界。
- 含带宽选件档的普通组合收口在连续200 MSPS边界；只有完整 lattice 精确要求400 MSPS时保留离散点。
- 原组合无 plan 时先保持 Count，按连续采样率上限降低 FreqSpacing；仅当对应间隔低于1 kHz时，才在1 kHz下降低 Count。
- sampleCount 继续兼顾精确 tone 周期、约100 Hz目标 RBW、最小下载长度和 current capacity。
- 设备切换后按同一顺序静默回写，保留能映射的 tone 选择并关闭 Enabled，不弹窗；普通编辑回写不关闭 Enabled。

更高设备能力应扩大可用 tone 数量/间隔或提高精确周期可行性，不应自动选择更高 Fs 生成同一个普通 lattice。

### 6.2 Digital Modulation

普通数字调制：

$$
F_s=R_b\times sps
$$

FSK：

$$
F_s=\max(R_b\times sps,\ 4(R_b+MaxDF))
$$

- SPS 为2~32的偶数，UI 按 current domain 动态禁用候选。
- 无带宽选件档只保留不超过125 MSPS的组合。
- 含带宽选件档保留不超过200 MSPS的连续组合，以及精确400 MSPS组合；空洞组合向连续低档回写 `Rb`，不自动抬升。
- FSK 若超过连续能力，按现有语义同步缩小 `Rb/MaxDF`。
- full waveform 超过容量时，无带宽选件档按125 MiB、含带宽选件档按1000 MiB选择可生成 symbol length；Digital 保留现有 trim 提示语义。
- 设备切换后当前 SPS/Rb/FSK 组合不属于新 domain 时，关闭 Enabled 并整组静默 reset，不逐项复杂钳位。

### 6.3 DSSS

$$
F_s=R_b\times sps\times(2^{code}-1)
$$

- SPS 为4到32的偶数，code为4到16；UI 按 current domain 动态筛选 SPS。
- 无带宽选件档使用125 MSPS/125 MiB。
- 含带宽选件档使用连续200 MSPS、精确400 MSPS和1000 MiB。
- 空洞组合向连续低档回写 `Rb`；只有公式精确命中400 MSPS才保留离散点。
- symbol length 继续由目标记录时长、滤波器稳态长度、32-symbol粒度和 current capacity共同决定。
- 设备切换后采样率组合无效时整组静默 reset；组合仍有效时只按新容量重新选择 symbol length。

### 6.4 OFDM

OFDM 的 SampleRate 是显式参数，容量主要由以下估算决定：

$$
bytesPerSymbol=\left\lceil FFTSize\times\left(1+\frac{GuardInterval}{100}\right)\right\rceil\times4
$$

$$
symbolCount_{max}=\min\left(16384,\left\lfloor\frac{maxWaveformBytes}{bytesPerSymbol}\right\rfloor\right)
$$

- 无带宽选件档：SampleRate 属于连续125 MSPS范围，symbolCount按125 MiB回写。
- 含带宽选件档：SampleRate 属于连续200 MSPS范围或精确400 MSPS，symbolCount按1000 MiB回写。
- 200~400 MSPS空洞输入向下回写200 MSPS；只有显式400 MSPS保留离散点。
- 设备切换导致 SampleRate 无效时整组静默 reset；只有容量缩小时，保留其他参数并降低 symbolCount，同时关闭 Enabled。
- Playback 与 Save IQ 都必须在调用第三方生成 API 前完成相同估算。

### 6.5 ARB 文件 Playback

本文只讨论 Ordinary WAV 与 IQS-WAV。

#### 采样率

- `Arb_SampleRate` 必须属于 current domain。
- 无带宽选件档上大于125 MSPS的值向下回写125 MSPS。
- 含带宽选件档上200~400 MSPS空洞值向下回写200 MSPS；精确400 MSPS保留。
- 当前实现不做重采样，改变 SampleRate 会改变文件播放时长和频谱尺度，因此 UI 必须显示永久回写后的真实值。

#### 文件与参数容量

- 文件有效 IQ payload 本身必须不超过 current `maxWaveformBytes`：无带宽选件档为125 MiB，含带宽选件档为1000 MiB。
- `Period` 最大复样本数为 `N_device`。
- `SampleOffset < SamplesInFile`。
- `SamplesToUse <= min(SamplesInFile-SampleOffset, Period)`。
- 补零到 Period 后，还要按最终最小下载长度补齐 payload 再做一次容量校验。

#### 设备切换

- 含带宽选件设备上的大文件切换到无带宽选件设备后，如果文件有效 payload 超过125 MiB：自动卸载文件、清空参数、关闭 Enabled，并弹出一次提示。
- 文件本身不超限但旧 Period 补零后超限：保留文件，将 Offset 复位为0，并把 Period/SamplesToUse回写到安全值；不弹窗。
- Programmed ARB 不在本策略内，不能引用1000 MiB结论宣称其已经支持扩展设备。

## 7. 按选件档位理解最终策略

### 7.1 无带宽选件档

未返回 `OPTION_BW_320M_TX` 是兼容基线：

- 所有 Fs 必须位于195.3125 kS/s~125 MSPS连续区间。
- 所有 payload 必须不超过125 MiB。
- Pulse 基线为至少6个 Width样点、8个Period样点；产品边界采用48 ns/96 ns，对应125 MSPS下6/12点。
- Ramp/AWGN最大连续带宽均为100 MHz。
- Digital/DSSS SPS、OFDM SampleRate和ARB文件按125 MSPS/125 MiB收口。

### 7.2 含带宽选件档

返回 `OPTION_BW_320M_TX` 不是简单把所有参数乘以 `400/125`：

- 普通连续能力只提高到200 MSPS。
- 400 MSPS是按业务显式使用的单点。
- 1000 MiB扩大可用记录长度，但不要求普通业务主动生成接近1 GiB的波形。
- Pulse 可利用更高时域采样密度，将理论 Width/Period扩展到连续30/40 ns，必要时用400 MSPS达到15/20 ns。
- Ramp/AWGN连续带宽扩展到160 MHz，并单独开放320 MHz离散点。
- PM、Digital、DSSS、OFDM、Multitone 获得更多合法组合；AM/FM普通配置通常保持与无带宽选件档相同的最小充分采样率策略。
- ARB文件容量扩大到1000 MiB。

## 8. 实施优先级与验收建议

### 8.1 优先级

1. Pulse：已删除旧长周期10 MHz特例并完成整数样点plan、偏好预算和设备切换回写。
2. Ramp：确认 Period 格点失败时优先量化回写，不因格点偶然性跳到400 MSPS。
3. AM/FM/PM：补充候选选择单元测试，确认持续使用最小充分格点；PM增加无/有带宽选件档边界用例。
4. AWGN/Multitone/Digital/DSSS/OFDM/ARB：以现有 resolver 为主，补齐无/有带宽选件档及400空洞回归矩阵。

### 8.2 最小验收矩阵

| Business | 无带宽选件档用例 | 含带宽选件档用例 | 含选件 -> 无选件 |
| :--- | :--- | :--- | :--- |
| Pulse | 48/96 ns @125 MSPS为6/12点；窄Width下动态缩短Period | 30/40 ns连续档；15/20 ns 400档；长Period容量边界 | 窄脉冲静默回写，关闭Enabled |
| PM | 10 MHz/2π回写Rate为7.8125 MHz、16点周期 | 10 MHz/2π使用160 MSPS | 只回写Rate并关闭Enabled |
| Ramp | 20 MHz/1.2 s保留；100 MHz/约0.262 s边界 | 20 MHz/5 s保留；160 MHz/约1.311 s；320 MHz/约0.655 s | Span/Period/SweepTime静默回写 |
| AWGN | 100 MHz/约0.262 s | 160 MHz/约1.311 s；320 MHz/约0.655 s | Bandwidth/Length静默回写 |
| Multitone | 10/10与11/10保持；12/10优先回写Spacing | 200连续边界和精确400 lattice | Spacing优先、必要时才降Count并关闭Enabled |
| Digital/DSSS | SPS候选与125 MiB长度 | 200/400候选与1000 MiB长度 | 无效组合整组reset |
| OFDM | 125 MiB symbolCount回写 | 同参数在1000 MiB下保留更多symbol | 400 SampleRate整组reset；容量只降symbolCount |
| ARB | 125 MiB内文件及Period补零 | 450 MiB文件、1000 MiB边界、400 SampleRate | 超125 MiB文件自动卸载并唯一弹窗 |

## 9. 实现入口

- 设备能力：`src/plugins/htra/htradevicecapabilityresolver.cpp`
- 公共 domain/layout：`src/plugins/core/devicecapabilities.*`、`src/plugins/htra/generatedplaybackutils.*`
- HTRA business：`src/plugins/htra/*modulation.cpp`
- Analog business：`src/plugins/analog/digitalmodulation.cpp`、`dsssmodulation.cpp`、`ofdmmodulation.cpp`
- ARB：`src/plugins/htra/arbmodulation.cpp`、`arbdatagenerator.cpp`
- 当前参数行为：[Waveform_Parameters_Constraints.md](Waveform_Parameters_Constraints.md)
- 多型号总体设计：[htra_multi_model_playback_capability_refactor.md](htra_multi_model_playback_capability_refactor.md)
- ARB 当前边界：[arb_mode_summary.md](arb_mode_summary.md)
