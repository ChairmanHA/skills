# Playback 波形参数范围产品决策表（按 OPTION_BW_320M_TX 选件分档）

日期：2026-07-20

## 1. 使用方式

本文供产品经理确认“新设备能力是否应转化为更大的用户参数范围”。范围包括：

| 插件 | 纳入的当前 business |
| :--- | :--- |
| HTRA | AM、FM、PM、Pulse、Multitone、Ramp、AWGN、Ordinary/IQS ARB Playback |
| Analog | Digital Modulation、DSSS、OFDM |

本文按 `device_query_options()` 是否返回 `OPTION_BW_320M_TX` 分为“无带宽选件档”和“含带宽选件档”。model 122、132及其他 HTRA 型号都使用同一判据，本文给出两档能力对应的各业务参数范围。
表格使用三种结论：

| 标记 | 含义 |
| :--- | :--- |
| 能力已确定 | 设备能力、业务公式和现有产品含义都明确，可直接作为实现/验收范围 |
| 推荐待实现 | 已有推荐方案，但当前代码还没有达到该范围 |
| 产品待确认 | 技术上可以扩大，但是否开放、开放多少需要产品经理确认 |

“UI 范围”默认指用户提交后由 business 永久回写的有效范围，不仅是 PropertyMetadata 显示的键盘上下限。对于 SPS、Period、symbolCount 等联动参数，本文给出公式化范围。

## 2. 设备能力基线

| 项目 | 无带宽选件档 | 含带宽选件档 |
| :--- | :--- | :--- |
| Playback 采样率域 | 195.3125 kS/s～125 MSPS 连续 | 195.3125 kS/s～200 MSPS 连续，另加精确 400 MSPS 单点 |
| 200～400 MSPS | 不适用 | 空洞，仅仅200/400可用 |
| 最大 payload | 125 MiB | 1000 MiB |
| 最大 complex sample 数 | 32,768,000 | 262,144,000 |

400 MSPS 不能在 UI 中表现成连续最大值。需要直接编辑 SampleRate 的业务，应表现为“连续 195.3125 kS/s～200 MSPS，加一个 400 MSPS 离散选项”；由其他参数推导采样率的业务，只能在参数公式精确命中或连续档无法满足硬质量要求时使用 400 MSPS。

## 3. 一页产品决策总表

| Business | 无带宽选件档当前范围摘要 | 含带宽选件档当前范围摘要 | 对应采样率 | 结论 |
| :--- | :--- | :--- | :--- | :--- |
| AM | Rate 1 Hz～10 MHz；Depth 1%～100% | 暂同无选件档 | 当前参数下两者通常只用到约 195.3125 kS/s～40 MSPS | 产品待确认：是否把 Rate 扩到 10 MHz 以上 |
| FM | Rate/Deviation 均为 1 Hz～10 MHz | 暂同无选件档 | 当前参数下两者通常只用到约 195.3125 kS/s～60 MSPS | 产品待确认：是否分别放大Rate/Deviation两个上限 |
| PM | Rate 1 Hz～10 MHz；Phase Deviation 0°～360°；超能力时优先回写Rate | 含选件连续200 MSPS可覆盖当前整个矩形 | 无选件档可到125 MSPS；含选件档当前范围可到约160 MSPS | 已实现Rate优先回退 |
| Pulse | Width 48 ns～1 s；Period 96 ns～1 s；组合受6/8点和125 MiB约束 | Width 15 ns～1 s、Period 20 ns～1 s，并由采样点/容量动态决定组合上限 | 连续档优先；30/40 ns 可用200 MSPS，15/20 ns需要400 MSPS | 已确认并实现 |
| Multitone | Count产品范围2～1024，FreqSpacing至少1 kHz，并按125 MSPS动态联动 | Count仍为2～1024；普通组合按200 MSPS扩大，精确400 MSPS lattice单独开放 | 最小合法 tone lattice 采样率 | 已确定|
| Ramp | Span 1 kHz～100 MHz；Period 1 us～动态容量上限 | Span连续到160 MHz，另有精确320 MHz；Period按1000 MiB动态扩大 | Fs不低于1.25×Span；PeriodMax=Nmax/Fs | 已确认并实现 |
| AWGN | Bandwidth 50 kHz～100 MHz；Length 100 us～动态容量上限 | Bandwidth 连续到 160 MHz，另有精确 320 MHz；Length 按 1000 MiB 动态扩大 | Fs=resolveDomain(1.25×Bandwidth) | 已确定 |
| ARB | Fs 到 125 MSPS；文件/Period 到 125 MiB | Fs 连续到 200 MSPS加400单点；文件/Period 到 1000 MiB | 用户指定并按 domain 回写 | 已确定 |
| Digital | Rb×SPS 受 125 MSPS 限制；SPS 动态筛选 | Rb×SPS 受 200 MSPS连续档和精确400单点限制；SPS动态扩大 | 普通 Fs=Rb×SPS；FSK 还受频偏公式限制 | 已确定 |
| DSSS | Rb×SPS×(2^code-1) 受125 MSPS限制 | 使用200 MSPS连续档和精确400单点 | 由 Rb/SPS/code 唯一推导 | 已确定 |
| OFDM | SampleRate 到125 MSPS；symbolCount受125 MiB限制 | SampleRate到200 MSPS加400单点；当前全部FFT/GI/symbolCount矩形可放入1000 MiB | SampleRate为显式参数 | 已确定 |

## 4. HTRA 本地生成业务

### 4.1 AM

| 参数 | 默认值 | 无带宽选件档当前范围 | 含带宽选件档建议范围 | 产品说明 |
| :--- | :--- | :--- | :--- | :--- |
| Rate | 1 kHz | 1 Hz～10 MHz | 暂保持 1 Hz～10 MHz | 产品待确认是否扩展 |
| Depth | 50% | business 有效范围 1%～100% | 不变 | 当前 metadata 下限为0.01%，但 business 会回写到1%；建议后续统一显示口径 |
| Shape | Sine | Sine/Square/Triangle/Ramp | 不变 | 与设备能力无关 |
| 生成采样率 | 自动 | Fs=Rate×N，N=4/8/16/...；选择无选件domain中的最小合法格点 | 公式不变；选择含选件domain中的最小合法格点 | 当前Rate上限下最高通常为40 MSPS，因此带宽选件不会自动改善普通AM |

产品决策建议：第一版保持 Rate 上限 10 MHz。若产品希望体现带宽选件能力，可评估把含选件档连续 Rate 上限提高到50 MHz，因为最少4点对应200 MSPS；精确100 MHz Rate会对应400 MSPS，但4点/周期的调制包络质量必须先做实测，不能仅按算术开放。

### 4.2 FM

| 参数 | 默认值 | 无带宽选件档当前范围 | 含带宽选件档建议范围 | 产品说明 |
| :--- | :--- | :--- | :--- | :--- |
| Rate | 1 kHz | 1 Hz～10 MHz | 暂保持 1 Hz～10 MHz | 产品待确认是否扩展 |
| Deviation | 1 kHz | 1 Hz～10 MHz | 暂保持 1 Hz～10 MHz | 必须与Rate联合评审，不能只扩大单个上限 |
| Shape | Sine | Sine/Square/Triangle/Ramp | 不变 | 与设备能力无关 |
| 生成采样率 | 自动 | Fs=n×Rate；n为不小于6的偶数，并满足 Fs>2×(Rate+Deviation)；上限125 MSPS | 公式不变，连续上限200 MSPS；只有精确格点才可用400 MSPS | 当前参数矩形通常不超过60 MSPS，因此含选件档默认不会选更高Fs |

产品决策建议：第一版保持现有 Rate/Deviation。若扩展，应发布联合可行域，而不是两个互不相关的固定最大值；连续档必须同时满足偶数周期格点、严格频域下限和 Fs≤200 MSPS。

### 4.3 PM

| 参数 | 默认值 | 无带宽选件档当前范围 | 含带宽选件档建议范围 | 产品说明 |
| :--- | :--- | :--- | :--- | :--- |
| Rate | 1 kHz | UI 1 Hz～10 MHz；与Phase Deviation组合超能力时向下回写 | 保持1 Hz～10 MHz | 固定外框不扩展，实际值由联合约束决定 |
| Phase Deviation | 约57.30° | UI 0°～360° | 保持0°～360° | 含选件档可完整支持现有矩形，无选件档不能 |
| Init Phase | 0° | -180°～180° | 不变 | 等价相位归一化 |
| Shape | Sine | Sine/Square/Triangle/Ramp | 不变 | 与设备能力无关 |
| 生成采样率 | 自动 | Fs=n×Rate，n为不小于6的偶数，且 Fs>2×Rate×(1+PhaseDeviation_rad) | 同一公式，连续上限提高到200 MSPS | 当前最大组合约需160 MSPS，含选件档无需400 MSPS |

无带宽选件档的重要现状：`Rate=10 MHz、Phase Deviation=360°` 需要最小偶数格点160 MSPS。当前 business 保留 Phase Deviation，优先把 Rate 回写到 `125 MHz / 16 = 7.8125 MHz`，不再整组复位。含带宽选件档可直接保留10 MHz并使用160 MSPS。普通编辑回写保持 Enabled；设备切换回写关闭 Enabled。

### 4.4 Pulse

#### 当前已实现范围

| 参数 | 无带宽选件档 | 含带宽选件档 |
| :--- | :--- | :--- |
| Width | 48 ns～1 s | 15 ns～1 s；连续档理论下限30 ns，15 ns需400 MSPS |
| Period | 96 ns～1 s | 20 ns～1 s；连续档理论下限40 ns，20 ns需400 MSPS |
| Duty Cycle | 0%～100%派生编辑，最终受Width≤Period及容量约束 | 同左 |
| SampleRate | 容量允许时优先125 MSPS | 连续档优先；只有连续档不能保持6/8点或400精确落格时使用400 MSPS |

Width 与 Period 都会被 business 量化成整数样点并永久回写；它们的独立最小/最大值不是任意组合均可用。例如无带宽选件档的48 ns Width要求125 MSPS，受125 MiB容量限制时Period最大约0.262144 s。

#### 采样点和容量规则

| 项目 | 无带宽选件档 | 含带宽选件档 |
| :--- | :--- | :--- |
| Width最小值 | 48 ns，对应125 MSPS下6点 | 连续档30 ns；仅在400 MSPS精确落格或连续档无法满足6点时开放15 ns |
| Period最小值 | 96 ns，对应125 MSPS下12点 | 连续档40 ns；400 MSPS档20 ns |
| Width最大值 | 动态为Period | 动态为Period |
| Period最大值 | 由Width所需最低Fs和32,768,000点共同决定 | 由Width所需最低Fs和262,144,000点共同决定 |
| Period量化 | 至少8点；不要求点数为8的倍数 | 同左 |
| Fs选择 | 合法候选中优先量化误差，再在125 MiB软预算内偏向较高Fs | 先搜索连续档；在125 MiB软预算内偏向较高Fs；400单点严格受控 |

代表性硬容量边界：

| 工作点 | 最大单周期时长 |
| :--- | ---: |
| 无选件 / 125 MSPS | 0.262144 s |
| 含选件 / 400 MSPS | 0.65536 s |
| 含选件 / 200 MSPS | 1.31072 s |
| 含选件 / 125 MSPS | 2.097152 s |
| 含选件 / 195.3125 kS/s | 1342.17728 s |

这些时长不是统一Period上限。例如15 ns Width只能走400 MSPS，因此Period最多0.65536 s；较宽脉冲可降低Fs并获得更长Period。

产品决定已经落地：至少6个Width样点、至少8个Period样点、连续档优先、125 MiB偏好预算、设备容量硬上限。PropertyMetadata只保留全产品连续外框，真正范围由business回写。

### 4.5 Multitone

令 `OccupiedSteps` 为完整tone lattice占用的频率步数，当前HTRA偶数Count使用center mask。核心限制为：

`1.25 × OccupiedSteps × FreqSpacing` 必须落入当前采样率域。

| 参数 | 无带宽选件档当前有效范围 | 含带宽选件档建议范围 | 结论 |
| :--- | :--- | :--- | :--- |
| Count | 产品范围2～1024；范围内继续与Spacing联动，使OccupiedSteps×Spacing≤100 MHz | 同为2～1024；普通连续组合≤160 MHz，另允许精确400 MSPS lattice | 能力已确定 |
| FreqSpacing | 至少1 kHz；与Count联动 | 同一公式按含选件档能力扩大 | 能力已确定 |
| 1 kHz Spacing示例 | Count按产品上限取1024，此时设备带宽不是限制项 | 同样按产品上限取1024 | 代表性边界 |
| Count=2示例 | Spacing最多50 MHz | 连续档80 MHz；精确400 MSPS点160 MHz | 代表性边界 |
| Phase Mode | Fixed/Random/Parabolic | 不变 | 与设备无关 |
| Seed | 非负整数，Random时可编辑 | 不变 | 建议后续明确uint32上限 |
| Notch Width | 当前仅保证不小于0，无明确UI最大值 | 不变 | 产品待确认是否限制到完整lattice带宽 |
| SampleRate | 优先取tone fundamental整数倍；允许capacity内精确闭合的125 MSPS有理周期边界 | 同一规则；普通最高200 MSPS，精确lattice可为400 MSPS | 低采样率与exact-period优先 |

输入首先按产品范围把Count收口到2～1024、FreqSpacing收口到至少1 kHz。原组合无法形成 plan 时，当前实现继续先保持 Count 并降低 FreqSpacing；只有计算得到的间隔低于1 kHz时，才在1 kHz下降低 Count。隐藏 tone 只会放松约束，不触发旧配置回滚。

### 4.6 Ramp

| 参数 | 默认值 | 无带宽选件档当前范围 | 含带宽选件档建议范围 |
| :--- | :--- | :--- | :--- |
| Span | 20 MHz | 1 kHz～100 MHz | 1 kHz～160 MHz连续，另有精确320 MHz点；160～320 MHz普通输入回写160 MHz |
| Period | 1 ms | 1 us～32,768,000/Fs_legal | 1 us～262,144,000/Fs_legal |
| SweepTime | 1 ms | 1 us～Period | 1 us～Period |
| SampleRate | 自动 | 满足Fs≥1.25×Span和周期闭环的最小合法值，最高125 MSPS | 普通最高200 MSPS；Span=320 MHz时为400 MSPS |

代表性Period上限：无带宽选件档在Span=20 MHz时约1.31072 s、Span=100 MHz时约0.262144 s；含带宽选件档在Span=20 MHz时约10.48576 s、Span=160 MHz时约1.31072 s、Span=320 MHz时为0.65536 s。低Span最低采样率下，两档理论上限分别约167.77216 s/1342.17728 s。该范围已确定。

### 4.7 AWGN

| 参数 | 默认值 | 无带宽选件档当前范围 | 含带宽选件档建议范围 |
| :--- | :--- | :--- | :--- |
| Bandwidth | 40 MHz | 50 kHz～100 MHz | 50 kHz～160 MHz连续，另有精确320 MHz点；160～320 MHz普通输入回写160 MHz |
| Length | 10 ms | 100 us～32,768,000/Fs | 100 us～262,144,000/Fs |
| SampleRate | 自动 | resolveDomain(1.25×Bandwidth)，最高125 MSPS | 连续最高200 MSPS；Bandwidth=320 MHz时为400 MSPS |

代表性Length上限：

| Bandwidth / Fs | 无带宽选件档 | 含带宽选件档 |
| :--- | ---: | ---: |
| 50 kHz / 195.3125 kS/s | 167.77216 s | 1342.17728 s |
| 100 MHz / 125 MSPS | 0.262144 s | 2.097152 s |
| 160 MHz / 200 MSPS | 不支持 | 1.31072 s |
| 320 MHz / 400 MSPS | 不支持 | 0.65536 s |

设备范围已经明确，但产品经理仍需确认：含带宽选件设备的窄带AWGN是否真的允许用户生成接近1000 MiB、时长超过20分钟的单段波形；如果不希望，应增加独立产品上限或软预算，而不是改变设备硬容量。

### 4.8 ARB 文件 Playback

| 参数 | 无带宽选件档当前范围 | 含带宽选件档建议范围 |
| :--- | :--- | :--- |
| SampleRate | 195.3125 kS/s～125 MSPS | 195.3125 kS/s～200 MSPS连续，加400 MSPS离散点 |
| 文件有效IQ payload | ≤125 MiB | ≤1000 MiB |
| SamplesInFile | 只读；由文件决定，且不能超过设备容量对应点数 | 同左 |
| SampleOffset | 0～SamplesInFile-1 | 同一公式 |
| Period | 1～32,768,000 complex samples | 1～262,144,000 complex samples |
| SamplesToUse | 0～min(SamplesInFile-SampleOffset, Period) | 同一公式 |
| IQ Scale | 0.01%～100% | 不变 |
| Auto Scale | Off/On；IQS模式锁定 | 不变 |

从含带宽选件档切换到无带宽选件档时，只有“已加载文件本身超过125 MiB并被自动卸载”弹窗；Period等参数超限只静默回写。

## 5. Analog 第三方算法业务

### 5.1 Digital Modulation

Digital属于能力已确定项。普通调制满足 `Fs=Rb×SPS`；FSK满足 `Fs=max(Rb×SPS, 4×(Rb+MaxDF))`。

| 参数 | 无带宽选件档当前范围 | 含带宽选件档新范围 |
| :--- | :--- | :--- |
| Rb，普通调制 | 对当前SPS，Rb×SPS必须在195.3125 kS/s～125 MSPS；非法输入按连续档回写Rb | 对当前SPS，Rb×SPS必须在195.3125 kS/s～200 MSPS，或精确等于400 MSPS |
| SPS | 2/4/6/.../32中动态保留满足无选件档采样率域的项 | 同一枚举中动态保留满足含选件档采样率域的项；空洞项禁用，精确400项可用 |
| Rb绝对示例 | SPS=32时最低6103.515625 Bd；SPS=2时最高62.5 MBd | 连续档SPS=2最高100 MBd；精确400点为200 MBd |
| MaxDF，FSK | 非FSK为0且只读；FSK时至少1 Hz、至多15×Rb，并与Rb共同满足采样率域 | 公式不变；连续档预算从125提高到200 MSPS，精确400组合可单独使用 |
| Filter Length span | 不小于2的偶数，且SPS×span≤400 | 不变 |
| PN | 4～24 | 不变；1000 MiB允许更多组合保存完整PN波形，超过容量仍trim |
| Sequence Seed | 1～2,147,483,647 | 不变 |
| Alpha，RC/RRC | 0.025～1.0 | 不变 |
| Alpha，Gaussian | 0.15～2.5 | 不变 |
| Filter Type | Rectangular/RC/RRC/Gaussian/Half-Sine | 不变 |
| Modulation Type | BPSK、QPSK、OQPSK、8/16PSK、16/64/256/1024QAM、2/4/8ASK、2/4/8/16FSK、DBPSK、DQPSK、Pi/4 DQPSK、D8PSK、16APSK | 不变 |
| 生成容量 | 125 MiB | 1000 MiB |

普通调制给定SPS时的Rb精确可行域为：

| 设备 | Rb可行域 |
| :--- | :--- |
| 无带宽选件档 | 195312.5/SPS ～ 125000000/SPS |
| 含带宽选件档 | 195312.5/SPS ～ 200000000/SPS，另加精确 400000000/SPS 单点 |

FSK不能用一个固定Rb最大值描述。UI必须针对当前Rb、MaxDF和SPS动态筛选；普通落入200～400 MSPS空洞的组合向连续200 MSPS收口，不自动抬升到400 MSPS。

### 5.2 DSSS

DSSS属于能力已确定项，采样率为 `Fs=Rb×SPS×(2^code-1)`。

| 参数 | 无带宽选件档当前范围 | 含带宽选件档新范围 |
| :--- | :--- | :--- |
| Rb | 对当前SPS/code，使Fs位于195.3125 kS/s～125 MSPS | 使Fs位于195.3125 kS/s～200 MSPS，或精确等于400 MSPS |
| SPS | 4/6/8/.../32中动态筛选 | 相同枚举，按含选件domain动态扩大；空洞项禁用 |
| code | 4～16 | 不变 |
| Rb最大值示例 | code=4、SPS=4时约2.083333 MBd | 同组合连续最大约3.333333 MBd；精确400点约6.666667 MBd |
| Filter Length span | 不小于2的偶数，且SPS×span≤400 | 不变 |
| Sequence Seed | 1～1000 | 不变 |
| Alpha，RC/RRC | 0.025～1.0 | 不变 |
| Alpha，Gaussian | 0.15～2.5 | 不变 |
| Modulation Type | 当前UI只开放BPSK | 不变 |
| Filter Type | Rectangular/RC/RRC/Gaussian/Half-Sine | 不变 |
| 生成容量 | 125 MiB；symbolLength按容量和约1.024 ms目标时长选择 | 1000 MiB；使用同一选择公式 |

给定SPS和code时，Rb范围直接由设备domain除以 `SPS×(2^code-1)` 得到。设备切换导致组合无效时整组静默reset；组合仍合法但容量变小时，只重新选择symbolLength。

### 5.3 OFDM

OFDM属于能力已确定项，SampleRate为显式参数；其余设备差异主要来自容量。

| 参数 | 无带宽选件档当前范围 | 含带宽选件档新范围 |
| :--- | :--- | :--- |
| SampleRate | 195.3125 kS/s～125 MSPS | 195.3125 kS/s～200 MSPS连续，加400 MSPS离散点；空洞输入回写200 MSPS |
| FFTSize | 16/32/64/128/256/512/1024/2048 | 不变 |
| SymbolCount | 2～min(16384, floor(125 MiB/bytesPerSymbol)) | 2～16384；当前最大FFT/GI组合也不超过1000 MiB |
| GuardInterval | 0%～100% | 不变 |
| WindowLength | 0%～100% | 不变 |
| GuardBand Left/Right | 当前仅在Left+Right>FFTSize时各回写FFTSize/2，未形成严格独立范围 | 与设备无关，建议产品确认0～FFTSize-1且Left+Right≤FFTSize-1 |
| NullDC | Off/On | 不变 |
| Windowed | Off/On | 不变 |
| Modulation Type | BPSK/QPSK/8PSK/16PSK/QAM16/QAM64/QAM256 | 不变 |

`bytesPerSymbol=ceil(FFTSize×(1+GuardInterval/100))×4`。在最坏的FFTSize=2048、GuardInterval=100%时，无带宽选件档最多允许8000个symbol；含带宽选件档原始容量可容纳64000个，但UI产品上限仍为16384，因此整个现有参数矩形都可生成。

## 6. 产品经理待确认清单

| 编号 | 决策问题 | 推荐默认选择 | 影响 |
| :--- | :--- | :--- | :--- |
| D1 | 含带宽选件档的AM Rate是否超过10 MHz | 第一版不扩大 | 扩大前需要确认4点/周期等低点数包络质量 |
| D2 | 含带宽选件档的FM Rate/Deviation是否超过10 MHz | 第一版不扩大 | 必须定义联合范围，不能分别给独立最大值 |
| D3 | PM是否需要在UI提示Rate会随Phase Deviation自动回写 | 建议保留当前Rate优先回退，并在产品文案中说明 | 避免用户把联合约束回写误解为输入丢失 |
| D4 | Pulse 6/8点、动态Period、连续档优先和125 MiB软预算方案 | 已批准：无选件档采用48/96 ns，含选件档开放30/40 ns连续档和15/20 ns 400档 | 已实现 |
| D5 | Multitone是否在UI中明确标识“精确400 MSPS lattice” | 建议标识为离散能力 | 防止把160～320 MHz占用范围误解为连续可用 |
| D6 | 含带宽选件设备的窄带AWGN是否允许接近1000 MiB、超过20分钟的Length | 建议增加产品软上限，保留1000 MiB硬能力 | 控制生成等待和用户误操作 |
| D7 | OFDM GuardBand是否改成严格0～FFTSize-1且总和≤FFTSize-1 | 建议确认并修正 | 这是现有参数定义缺口，与设备型号无关 |

## 7. 决策后的实施顺序

1. 先确认D1～D7，并把确认结果写回本文。
2. 优先实现Pulse动态范围，因为当前显示范围与有效波形之间存在明确矛盾。
3. 对AM/FM/PM只按产品确认调整business有效范围；PropertyMetadata仍保持简单。
4. Digital/DSSS/OFDM、Ramp/AWGN、Multitone和ARB以本文“能力已确定”表作为含带宽选件档验收矩阵。
5. 所有设备切换参数回写保持静默；只有ARB已加载文件因新设备容量不足被卸载时弹窗。
