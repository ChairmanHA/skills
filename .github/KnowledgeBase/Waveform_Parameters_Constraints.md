# 发射机波形参数与采样率约束技术说明

## 1. 文档目的与适用范围

本文档描述的是当前 active 代码的真实行为，覆盖模拟波形、数字调制、DSSS、OFDM 及相关通用规则。除特别说明外，文中采样率均指 IQ 复基带采样率。

---

## 2. 术语与符号

### 2.1 符号定义

- $F_s$：复基带采样率
- $B$：信号总带宽
- $T$：波形时长、周期或等效持续时间
- $R_b$：符号速率或调制率
- $sps$：每符号采样点数，亦称 oversample
- $MaxDF$：FSK 最大频偏
- $Size$：波形数据量

### 2.2 数据格式定义

当前系统下发链路为 IQ 交织的 int16 双通道格式，即每个复采样点占 4 字节：

$$
1\ \text{complex sample} = I_{16bit} + Q_{16bit} = 4\ \text{Bytes}
$$

因此，大多数波形大小均可按下式近似估算：

$$
Size \approx F_s \times T \times 4\ \text{Bytes}
$$

---

## 3. 平台级采样率规则

### 3.1 通用合法采样率范围

当前版本不再采用“三档离散采样率”或 Gear 1/2/3 模型，而是采用连续合法区间：

$$
F_{s,min} = 195312.5\ \text{S/s}
$$

$$
F_s \in [195312.5\ \text{S/s},\ 125\ \text{Msps}]
$$

也就是说：

- Digital、DSSS、OFDM 以及 HTRA AM/FM/PM/Pulse/Multitone/Ramp/AWGN 已改为消费当前设备 Playback domain：未返回 `OPTION_BW_320M_TX` 时使用 `[195.3125 kHz, 125 MHz]`，返回该选件时使用 `[195.3125 kHz, 200 MHz] U {400 MHz}`。model 122、132及其他 HTRA 型号都按选件结果分档。
- 400 Msps 是精确离散点；200~400 Msps 之间不是连续可用区间。已迁移业务在普通参数归一化时优先回写连续低档，只有精确命中 400 Msps 的组合才保留该点。
- Step Sweep 等尚未迁移的业务仍按 legacy 常量工作，其当前行为不能据此宣称支持带宽选件能力。

### 3.2 Streaming 模式采样率范围

Streaming 模式另外受实时链路吞吐能力限制，当前软件实现采用独立上限：

$$
F_{s,stream,max} = 62.5\ \text{Msps}
$$

因此，Streaming 模式的有效范围为：

$$
F_s \in [195312.5\ \text{S/s},\ 62.5\ \text{Msps}]
$$

该上限只用于 Streaming pipeline。已迁移的生成型 Playback 不再把 62.5 Msps 当成大波形降级档，而是在当前 Playback domain 与容量内解析参数。

### 3.3 通用采样率回写规则

legacy 公共采样率辅助函数的行为如下，仅供尚未迁移业务使用：

- `Utils::checkSampleRate()`：判断采样率是否落在通用连续范围内。
- `Utils::clampSampleRate()`：将采样率钳位到通用连续范围。
- `Utils::clampStreamingSampleRate()`：将采样率钳位到 Streaming 连续范围。

已迁移业务不再使用这些函数表达设备能力，而是消费 `Core::SampleRateDomain`；因此含带宽选件档的200~400 Msps空洞和400 Msps单点必须按domain精确处理。

---

## 4. 带宽语义与工程裕量

### 4.1 IQ 复基带带宽语义

本文档涉及的 AWGN、Ramp 等带宽类参数，均按 IQ 复基带总带宽理解。

- 对实信号，通常采用 $F_s \ge 2B$。
- 对 IQ 复基带信号，理论上总带宽可达 $F_s$。

因此，当前发射机不再使用“带宽只能到 $F_s/2$”的实信号语义解释 AWGN 或 Ramp 的带宽参数。

### 4.2 工程滚降裕量

虽然 IQ 复基带理论上允许总带宽接近采样率，但工程实现仍需给 DAC 重构滤波器与带外滚降预留裕量。当前版本统一采用 1.25 倍裕量：

$$
B_{max} = \frac{F_s}{1.25} = 0.8F_s
$$

当 $F_s = 125\ \text{Msps}$ 时：

$$
B_{max} = \frac{125\text{M}}{1.25} = 100\ \text{MHz}
$$

因此无带宽选件档的AWGN最大带宽和Ramp最大扫宽均为100 MHz；含带宽选件档的连续可调上限为160 MHz，并额外允许精确320 MHz对应400 Msps。160~320 MHz之间的带宽输入会由业务层回写到160 MHz，不能把它解释成连续范围。

---

## 5. 大波形保护规则

### 5.1 波形大小阈值

已迁移Playback业务的大波形保护阈值来自当前设备`maxWaveformBytes`：未返回`OPTION_BW_320M_TX`时为`125 MiB`，返回该选件时为`1000 MiB`。`Analog::MAXDOWNLOADSIZE`只作为尚未收到可用capability或尚未迁移业务的legacy 125 MiB fallback。

### 5.2 大波形处理原则

不同波形类型的处理细节并不完全相同，但整体原则一致：

- 优先保证物理正确性和频谱质量
- 在满足频域/时域约束和离散闭环的前提下，已迁移业务优先取较小的合法采样率，避免无意义地扩大波形
- 当前产品层大波形主策略已收敛为 trim-only / playback-first，不再从大波形提示框自动切到 Streaming 业务
- 尚未迁移的 legacy 业务仍可能在公共 playback 路径按固定上限裁剪；已迁移业务必须在生成 API 调用前完成 current capability 校验
- Digital 与 DSSS 由 business 按当前设备容量选择生成长度，并把采样率、symbol length 和预计 payload layout 写入设备无关 generation plan；第三方 generator 不读取设备能力
- OFDM 虽没有长度参数，但会先按 `FFTSize / GuardInterval / symbolCount` 保守估算，并在业务层降低 `symbolCount`；超容量组合不会进入生成接口，也不再依赖 runtime trim
- Digital/DSSS/OFDM 与 HTRA AM/FM/PM/Pulse/Multitone/Ramp/AWGN 都在分配前由 business 以 current capability 计算最终字节数，将精确 sample rate/layout 作为 generation plan 交给算法；generator 在调用第三方 API 或创建 builder 前按 plan 取得大内存租约
- 上述十类 generator 不再接收或缓存 `PlaybackCapabilities`；设备切换的静默参数回写、默认复位、离散选项和 Enabled 策略均位于 `*Modulation` 业务层
- Digital/DSSS/OFDM/AM/FM/PM/Pulse/Multitone/Ramp/AWGN 在当前产品参数范围和支持设备能力下均按总函数处理，不维护最近成功配置或普通编辑失败回滚；空 plan 只表示内部 capability、算术或实现契约被破坏
- PM 超出能力时保留 PhaseDeviation 并回退 Rate；Multitone 保留 Count 并优先回退 FreqSpacing，只有 1 kHz 仍无法满足时才降低 Count。普通编辑保持 Enabled，设备切换导致成功回写时关闭 Enabled
- Digital/DSSS/OFDM 的合作方授权对象由 business 注入的 signal-object factory 创建；第三方 generator 不包含 `DeviceUtils`，只负责算法对象的使用与释放
- 完整 Save IQ 直接读取 payload；同步下载已释放 host storage 时，按同一 capability 和租约重新生成一次，写完立即释放，不建立第二份 full-size `QVector/QByteArray`
- 因此“大波形是否提示、提示后是否可直接继续、生成阶段能否预裁剪”取决于具体调制，而不是一律按 Streaming 降级处理

---

## 6. 各波形类型参数说明

### 6.1 AM 幅度调制

#### 参数意义

- `Rate`：调制率，决定包络变化速度
- `Depth`：调制深度，决定幅度起伏幅值
- `Shape`：调制波形形状

#### 默认值

- `Rate = 1e3`
- `Depth = 50`
- `Shape = Sine`

#### 当前硬约束

- `Rate`: 1 Hz ~ 10 MHz
- `Depth`: 1 ~ 100

#### 采样率策略

AM 以一个完整调制周期作为源波形，采样率格点为：

$$
F_s = Rate \times N,\quad N \in \{4,8,16,\ldots\}
$$

当前实现按以下顺序处理：

1. 在 current Playback domain 中，从 `N = 4` 开始选择最小合法格点。
2. 点数同时受 current `maxWaveformBytes / bytesPerComplexSample` 限制；生成前还会计算最小下载长度补齐后的最终 payload 字节数。
3. 当前组合在新设备 domain 内无解时，关闭 Enabled，并静默恢复 AM 默认参数，不弹窗。

#### 工程说明

- AM 现在优先使用最小合法采样率，避免低 `Rate` 无意义地产生超大单周期波形。
- 生成器直接写入唯一 builder，execution context 不再复制第二份完整 IQ。

---

### 6.2 FM 频率调制

#### 参数意义

- `Rate`：调制率
- `Deviation`：频偏
- `Shape`：调制波形形状

#### 默认值

- `Rate = 1e3`
- `Deviation = 1e3`
- `Shape = Sine`

#### 当前硬约束

- `Rate`: 1 Hz ~ 10 MHz
- `Deviation`: 1 Hz ~ 10 MHz

#### 采样率策略

FM 采样率由 `FmModulation` 的 business resolver 决定，目标关系为：

$$
F_s = n \times R_b,\quad n \ge 6,\ n\ \text{为偶数}
$$

并同时满足：

$$
F_s > 2 \times (R_b + Deviation)
$$

当前实现按以下顺序处理：

1. 在 current Playback domain 中选择满足整数周期、偶数 `n` 和严格频域下限的最小合法采样率。
2. `n` 的上界由 current capacity 推导；最终补齐后的 payload 也必须不超过 current `maxWaveformBytes`。
3. 当前组合在新设备上无解时，关闭 Enabled，并静默恢复 FM 默认参数；不使用 Streaming 62.5 Msps 兜底。

#### 工程说明

- FM 与 AM 一样优先选择较小的合法 Playback 格点，并始终保持 $F_s = n \times R_b$ 的周期闭环关系。

### 6.2.1 PM 相位调制

- `Rate` 为 1 Hz~10 MHz，`PhaseDeviation` 为 0~$2\pi$，`InitPhase` 会归一化到等价相位。
- PM 与 FM 使用相同的整数周期格点：$F_s=n\times Rate$，其中 $n\ge6$ 且为偶数。
- 严格频域下限为 $F_s>2\times(Rate+PhaseDeviation\times Rate)$；在 current domain 中选择最小合法格点。
- 请求 Rate 无法在 current continuous domain 中形成合法格点时，保留 PhaseDeviation/InitPhase/Shape，计算满足严格不等式的最小偶数 `n`，并回写 `Rate = continuousMaximum / n`。例如无带宽选件档的 `Rate = 10 MHz, PhaseDeviation = 2π` 回写为 `7.8125 MHz`，使用16点周期和125 Msps。
- 普通编辑发生 Rate 回写时保持 Enabled；设备切换发生 Rate 回写时关闭 Enabled，不恢复整组默认参数。

---

### 6.3 Pulse 脉冲调制

#### 参数意义

- `Width`：脉宽
- `Period`：周期

#### 默认值

- `Width = 0.001`
- `Period = 0.002`

#### 当前硬约束

- 无带宽选件档：`Width = 48 ns ~ 1 s`，`Period = 96 ns ~ 1 s`
- 含带宽选件档：整体外框为 `Width = 15 ns ~ 1 s`，`Period = 20 ns ~ 1 s`；其中连续200 Msps档的理论边界为`30 ns / 40 ns`，`15 ns / 20 ns`需要精确400 Msps档
- 所有型号均满足 `Width <= Period`；Width 与 Period 的最大组合还受采样点和 current `maxWaveformBytes` 联合约束，因此上述两个独立范围不能理解为任意组合都可同时成立

#### 采样率策略

Pulse 采样率由 `PulseModulation` 的 business resolver 决定，需同时满足：

- Width 至少包含 6 个复样点
- Period 至少包含 8 个复样点
- 优先让 $F_s \times Width$ 与 $F_s \times Period$ 同时为整数；没有精确连续格点时，由 business 量化到明确的整数样点并永久回写参数

当前实现按以下顺序处理：

1. 在连续采样率档中优先寻找 Width/Period 都精确落点的格点；同样合法时，在 125 MiB 偏好预算内选择较高采样率，以改善边沿和脉内采样密度。
2. 无带宽选件档的 `48 ns / 96 ns` 在125 Msps下对应6/12点。旧实现的“Period 点数必须为8的倍数”已删除；真实质量约束是Period不少于8点。
3. 含带宽选件档的普通参数优先连续档；连续200 Msps无法保持6/8点，或参数只有在400 Msps才能精确落格而连续档必须回写时，才使用精确400 Msps。
4. 为满足窄脉宽的 6 点要求可以突破 125 MiB 偏好预算，但最终 payload 绝不能超过 current `maxWaveformBytes`。若窄 Width 与超长 Period 不能同时容纳，保留 Width 并静默缩短 Period。
5. business 将最终 `sampleRate / widthSamples / periodSamples / payload layout` 写入 Pulse 专用 generation plan；算法只消费整数样点 plan，不读取设备型号或能力，也不再次通过 double 独立决定点数。

#### 工程说明

- 在当前实现中，Pulse 不是“任意纳秒分辨率”的专用 RF gate，而是建立在 IQ 波形采样点上的矩形包络。
- `48 ns / 96 ns` 是无带宽选件档的业务边界；含带宽选件档由200/400 Msps档分别提供`30/40 ns`与`15/20 ns`理论边界。
- 对 50% duty pulse，仍应根据 business 最终回写的 sample rate 确认高/低电平的实际采样点数。
- `Width = Period` 表示常开满幅，不是脉冲。
- 当前半幅边沿合成算法保持不变；若按仪器 50% 阈值测量，窄脉冲的实测宽度可能接近 `(widthSamples - 1) / Fs`。这属于现有波形边沿语义，不应与本次设备能力 resolver 的 6 点计划混为一谈。

---

### 6.4 Multitone 多音信号

#### 参数与相位模式

- `2 <= Count <= 1024`，`FreqSpacing >= 1 kHz`；Count上限是先于设备能力解析生效的产品边界。
- 相位模式已支持 `Fixed / Random / Parabolic`；Random 使用 `Seed`，Fixed/Random/Parabolic 都可叠加固定相位偏置。
- 离散保留模式可只启用部分候选 tone；Notch 模式则按中心缺口移除 tone。
- HTRA 业务启用 even-count center mask，偶数 tone lattice 的中心偏移由 execution context 以 `FreqSpacing / 2` 补偿。

#### current capability 归一化

完整 lattice 的采样率需求按 1.25 倍复基带裕量计算：

$$
F_{s,required}=1.25\times OccupiedSteps\times FreqSpacing
$$

- `Count/FreqSpacing` 的上限由 current sample-rate domain 推导，不再固定为 125 MHz 口径。
- 需求低于设备最小采样率时回提到最小值；其余情况优先选择 tone fundamental 的整数倍。domain 边界不是整数倍时，只要 current capacity 内的 sampleCount 能形成精确有理周期，也可保留该合法采样率；`Count=10/11, FreqSpacing=10 MHz @ 125 Msps` 属于此类。
- 原组合无法形成 plan 时，先保持 `Count` 并把 `FreqSpacing` 降到 current continuous maximum 支持的最大值；只有该值低于 1 kHz 时，才固定 `FreqSpacing = 1 kHz` 并降低 `Count`。
- 落入 200~400 Msps 空洞的普通组合按上述顺序回写到连续 200 Msps 边界；只有完整 lattice 精确支持 400 Msps 时才保留离散点。
- 隐藏 tone 只会放松 active-tone 约束，不触发最近成功配置回滚。普通编辑回写保持 Enabled；设备切换回写关闭 Enabled。
- sampleCount 兼顾 tone 精确周期、约 100 Hz 的优选 waveform RBW、设备最小下载长度和 current capacity；最终补齐字节数在分配前再次检查。

#### 合成内存模型

- 最终 IQ 直接写入租约保护的唯一 `PlaybackPayloadBuilder`。
- 2 的幂次 sampleCount 使用一个原地 `complex<double>` FFT 工作区，不再复制出第二个时域数组；非幂次 sampleCount 先在小型 bin 列表中合并同 bin tone，再一次合成到唯一完整时域工作区，不建立完整频域数组，也不重复计算整段波形。
- metrics 和 512 点 spectrum 直接读取 payload；下发与 Save IQ 不复制完整 IQ。

---

### 6.5 Step Sweep 步进扫频

#### 参数意义

- `Span`：总扫宽
- `Points`：离散频点数
- `DwellTime`：每点驻留时间
- `Center`：中心频率，由后续参数转换得到起止频率

#### 默认值

- `Span = 10e6`
- `Points = 201`
- `DwellTime = 0.001`

#### 当前硬约束

- `Span`: 1 kHz ~ $125\text{M} / 8 = 15.625\text{MHz}$
- `Points`: 2 ~ `Span / 1000 + 1`
- `DwellTime`: 1 us ~ 10 ms

#### 约束含义

- `Span / (Points - 1) >= 1 kHz`
- 驻留时间被限制在 10 ms 以内，以抑制组合波形体积失控

---

### 6.6 Ramp Sweep 线性扫频（HTRA 当前实现）

#### 参数意义

- `Span`：扫宽，按 IQ 总带宽语义理解
- `SweepTime`：单次扫频时间
- `Period`：循环周期

#### 默认值

- `Span = 20e6`
- `SweepTime = 0.001`
- `Period = 0.001`

#### 当前硬约束

- `Span >= 1 kHz`；无带宽选件档上限100 MHz，含带宽选件档连续上限160 MHz，并额外允许精确320 MHz对应400 Msps
- `Period`: 1 us ~ `maxPeriod(Span, currentCapability)`
- `SweepTime`: 1 us ~ `Period`
- 始终满足 `SweepTime <= Period`

这里最重要的变化是：

- Ramp 已不再采用固定 `Period <= 1 s` 模型；Period 唯一上限来自 current payload 容量。
- business 会根据 `Span`、非连续 sample-rate domain 和 current capacity 静默收缩并永久回写参数；PropertyMetadata 只保留大于零等基础约束。

#### 动态 Period 上限

当前 HTRA Ramp 的 `Period` 上限由两条硬约束共同决定：

1. 最小可行采样率必须满足：

$$
F_{s,min,ramp}(Span) = \max(1.25 \times Span,\ DATA\_SAMPLE\_RATE\_MIN)
$$

2. 单周期 IQ payload 不能超过 current capability：

$$
N_{max}=\left\lfloor\frac{maxWaveformBytes}{bytesPerComplexSample}\right\rfloor
$$

$$
Period_{max}(Span)=\frac{N_{max}}{F_{s,min,legal}(Span)}
$$

其中 `Fs,min,legal` 是 current domain 中满足 `Fs >= 1.25 * Span` 的最低候选。无带宽选件档使用125 MiB；含带宽选件档使用1000 MiB。连续档内business可把Fs微量提高到`N / Period`以精确保留用户Period；若该候选越过domain或容量，则固定最低合法Fs并把Period量化回写为`N / Fs`。最终生成前仍由payload layout执行同口径容量检查。

代表性容量上限：

- 132：最低采样率195.3125 kS/s时约167.77216 s；Span=20 MHz、Fs=25 MSPS时约1.31072 s；Span=100 MHz、Fs=125 MSPS时约0.262144 s。
- 含带宽选件档：最低采样率时约1342.17728 s；Span=20 MHz时约10.48576 s；Span=160 MHz时约1.31072 s；Span=320 MHz、Fs=400 MSPS时为0.65536 s。

这意味着用户可以这样理解当前产品行为：

- 窄带 Ramp 允许更长的 `Period`。
- 宽带 Ramp 会自动缩小允许的 `Period`，因为它至少需要更高的采样率才能保证扫宽。
- `Span` 越大，可安全下发的单周期时长越短，这是当前实现的设计结果，不是界面随机限制。

#### 最小时长

当前代码的 `Period` 与 `SweepTime` 基础下限均为 1 us，且始终满足 `SweepTime <= Period`。这只是参数合法性下限；实际样点数仍由 current legal sample rate 决定，外部仪器稳定观测时应按具体带宽保留更充足时长。

#### 采样率策略

Ramp 采样率和整数样点plan由`RampModulation` business resolver决定，需要同时考虑：

- 频域下限：

$$
F_s \ge 1.25 \times Span
$$

- 时域闭环：尽量满足 $F_s \times Period$ 为整数
- 大小约束：$F_s \times Period \times bytesPerComplexSample \le maxWaveformBytes$

当前实现按以下顺序处理：

1. 先由Span解析最低合法采样率，并计算`Nmax = maxWaveformBytes / bytesPerComplexSample`。
2. 连续档优先令`N = ceil(Fs_min * Period)`、`Fs = N / Period`，在domain和capacity允许时精确保留Period；否则固定最低合法Fs并把Period量化到整数点。
3. 200~400 Msps 空洞中的 Span 回写到连续低档；精确 320 MHz Span 才保留 400 Msps 单点。
4. business把最终周期点数、活动段点数和payload layout写入Ramp专用plan；generator不再独立通过double重新决定点数。

#### 工程说明

- `Period`和`SweepTime`最终都由明确整数样点数除以实际Fs回写，因此UI/Profile、plan和generator使用同一离散时长。
- Ramp 当前采用“非连续 current domain 搜索 + current capacity 动态 Period 上限”，不再使用 Streaming 优先降级。

#### 大波形处理语义

- 当前 HTRA Ramp 的正常 UI/business 路径，已经通过 `maxPeriod(Span)` 把大波形风险前移到参数层处理，因此用户通常不会再看到“输入合法但生成后再靠 runtime trim 收口”的旧行为。
- 如果外部调用绕过 UI，直接传入超范围参数，business 层仍会再次执行本地参数收口，把 `Period` 和 `SweepTime` 夹回合法范围；该收口包含基于实际生成采样率的二次校正，避免 `round(F_s \times Period)` 越过下载样点上限。
- 生成阶段会再次消费已收口 profile，并在 builder 分配前检查最终补齐字节数不超过 current `maxWaveformBytes`。
- Ramp preview 只复制前 65536 个 complex sample；完整 payload 只保留一份并直接用于 metrics、下发和保存。
- 因此，对当前 HTRA Ramp 来说，用户应把“大波形保护”理解为“参数层动态限幅 + 生成层不变量检查”，而不是“先放行，下载时再裁剪”。

---

### 6.7 AWGN 加性高斯白噪声

#### 参数意义

- `Bandwith`：噪声总带宽
- `Length`：波形长度，单位为秒

#### 默认值

- `Bandwith = 40e6`
- `Length = 0.01`

#### 当前硬约束

- `Bandwith >= 50 kHz`；无带宽选件档上限100 MHz，含带宽选件档连续上限160 MHz，并额外允许精确320 MHz
- `Length`: 100 us ~ `maxLength`

#### 采样率与长度关系

AWGN 当前采用动态采样率策略：

$$
targetF_s = resolveCurrentDomain(1.25 \times Bandwith)
$$

低于设备最小采样率时使用 current domain 最小值；落入 200~400 Msps 空洞时回写连续 200 Msps 档及 `Bandwidth = 160 MHz`；精确 `Bandwidth = 320 MHz` 时保留 400 Msps。

长度上限由下式决定：

$$
maxLength = \frac{maxWaveformBytes}{targetF_s \times bytesPerComplexSample}
$$

#### 工程说明

- AWGN 不再采用“固定 125 Msps”模型。
- 对窄带 AWGN，采样率会随带宽下降而下降，因此可用最大长度会相应增加。
- Bandwidth/Length 在编辑和设备切换时由 business 静默收口并永久回写；容量变小时关闭 Enabled，但不弹窗。
- 生成采用两遍流式噪声统计/量化，只保留 65 tap 的小型 FIR/history 状态，最终 IQ 直接写入唯一租约 builder，不建立完整浮点噪声副本。

---

### 6.8 Digital Mod 数字调制

#### 参数意义

- `Rb`：符号速率
- `sps`：每符号采样点数
- `span`：成型滤波器长度
- `Alpha`：滚降系数或 Gaussian 参数
- `PN`：伪随机序列阶数
- `sequenceSeed`：序列种子
- `DigitalModType`：调制方式
- `MaxDF`：FSK 最大频偏
- `filterType`：滤波器类型

#### 默认值

- `Rb = 1e6`
- `MaxDF = 0`
- `PN = 15`
- `sequenceSeed = 23`
- `span = 16`
- `sps = 4`
- `Alpha = 0.35`
- `DigitalModType = QAM64`
- `filterType = RootRaisedCosine`

#### Digital 业务与 packing 基础硬约束

##### 1. 普通数字调制

普通数字调制的基础关系为：

$$
F_s = R_b \times sps
$$

Digital Modulation 已迁移为使用当前设备 Playback 采样率域：

- 无带宽选件档：$[195312.5\ \text{S/s},\ 125\ \text{Msps}]$
- 含带宽选件档：$[195312.5\ \text{S/s},\ 200\ \text{Msps}] \cup \{400\ \text{Msps}\}$

普通参数编辑若落入空洞或超出范围，会优先选择连续低档中不高于请求值的采样率，并反算新的符号速率；不会把 200~400 MHz 空洞自动向上提升到 400 MHz。只有 $R_b \times sps$ 精确命中 400 MHz 时才保留离散档。

即：

$$
F_s = normalizeDomain(R_b \times sps)
$$

$$
R_b \leftarrow \frac{F_s}{sps}
$$

##### 2. FSK 调制

当调制方式为 `FSK2 / FSK4 / FSK8 / FSK16` 时，还必须满足：

$$
F_s \ge 4 \times (R_b + MaxDF)
$$

当前实现对 `MaxDF` 的限制为：

$$
MaxDF \le 15 \times R_b
$$

并进一步受当前设备连续低档约束：

$$
R_b + MaxDF \le \frac{F_{s,continuousMax}}{4}
$$

当用户输入的 `R_b` 与 `MaxDF` 组合导致上式超限时，当前实现会优先同步缩小 `R_b` 与 `MaxDF`，尽量保留两者的相对关系；
这与当前 UI 在 FSK 下“修改 `symbolRate` 时自动同步 `fskDeviation`”的业务语义保持一致。

若缩放后仍触及最小值约束或 $$MaxDF \le 15 \times R_b$$ 约束，则再做二次收口。

FSK 的实际采样率由下式决定：

$$
F_s = resolveDomain\left(\max(R_b \times sps,\ 4 \times (R_b + MaxDF))\right)
$$

这意味着：

- FSK 下的实际采样率不一定等于 `Rb × sps`
- 当频偏要求较高时，软件会自动抬高采样率
- 若最低需求落入 200~400 MHz 空洞，普通编辑路径优先同步缩小 `Rb / MaxDF` 到连续低档；不会自动跳到 400 MHz
- 最低需求精确等于400 MHz时，含带宽选件档允许使用400 MHz离散档

##### 2.1 FSK 的工程推荐值

从当前产品定位与通用射频实验场景出发，`MaxDF >= Rb` 适合作为 FSK 的默认/推荐值；其中 `MaxDF = Rb` 是当前 UI 联动逻辑对应的保守缺省值。

这一建议的依据主要是工程性，而不是协议级硬约束：

- 当频偏不小于符号速率时，FSK 的频率摆幅更大，接收端更容易区分不同音调；对未知接收机、频率判决器、非相干检测或联调观察场景，通常更稳健。
- 更大的 `MaxDF` 会提高频率偏移、相位噪声、CFO 或实现误差下的判决裕量，因此作为信号源产品的通用默认值是合理的。
- 代价是占用带宽明显增加；若业务目标是频谱效率、窄带兼容或严格模拟既有空口协议，则不应把 `MaxDF >= Rb` 当成硬要求。

因此当前版本建议按以下方式理解：

- `MaxDF >= Rb`：工程推荐值，适合通用 FSK 默认配置。
- `MaxDF = Rb`：当前 UI 自动联动时的默认回写值。
- `MaxDF < Rb`：在需要贴近具体协议或控制带宽时允许用户手动设置，只要仍满足 packing 层与采样率约束即可。

##### 3. 其他参数约束

- `sps`：归一化到 2 ~ 32 的偶数
- `span`：最小 2，且满足 `sps × span <= 400`
- `PN`：最小 4，最大 24
- `sequenceSeed`：1 ~ 2147483647
- `Alpha`：
  - RC / RRC：0.025 ~ 1.0
  - Gaussian：0.15 ~ 2.5

#### UI 与调制器层联动

##### 1. 当前 UI 实际开放的调制方式

当前 UI 实际开放以下数字调制方式：

- BPSK
- QPSK
- OQPSK
- 8PSK
- 16PSK
- 16QAM
- 64QAM
- 256QAM
- 2ASK
- 4ASK
- 8ASK
- 2FSK
- 4FSK
- 8FSK
- 16FSK
- DBPSK
- DQPSK
- Pi/4 DQPSK
- D8PSK
- 1024QAM
- 16APSK

##### 2. `sps` 可选项的真实来源

虽然参数定义允许 `sps` 取 2 ~ 32 的偶数，但 UI 实际可选项还受采样率合法性约束：

候选采样率必须属于当前设备 Playback domain。

因此当前真实行为是：

- UI 会从 2、4、6、...、32 中动态筛选可用项
- 过滤条件由 `DigitalModulation` business 根据 current domain 计算并写入 `enumDisplayOptions`；generator 不参与设备选项判断
- 普通调制候选按 $R_b \times sps$ 判断
- FSK 候选按 $\max(R_b \times sps, 4(R_b + MaxDF))$ 判断
- 含带宽选件档上落入200~400 MHz空洞的项禁用，只有精确400 MHz项可用；无带宽选件档上高于125 MHz的项禁用

##### 3. FSK 下 `MaxDF` 的联动行为

调制器层当前还存在以下自动同步规则：

- 当切换到 FSK 模式时，若当前 `fskDeviation` 不等于 `symbolRate`，则自动同步为 `symbolRate`
- 当 FSK 模式下修改 `symbolRate` 时，也会自动同步 `fskDeviation`
- 用户仍可在 FSK 模式下手动修改 `fskDeviation`，手动修改后不会再被“自动保持相等”逻辑立即覆写
- 一旦退出 FSK 模式，`fskDeviation` 不再可编辑，packing 层最终会把 `MaxDF` 写回 0

#### 大小估算

当前 `Digital_EstimatedSize()` 近似为：

$$
Size \approx 2^{PN} \times samplesPerSymbol \times 4
$$

其中：

- 非 FSK：`samplesPerSymbol = sps`
- FSK：`samplesPerSymbol = max(sps, F_s / R_b)`

播放业务的大波形阈值和生成阶段 `SymbolLength` 上限均来自当前设备 `maxWaveformBytes`：无带宽选件档为125 MiB，含带宽选件档为1000 MiB。等于上限合法，严格大于上限才进入trim语义。`DigitalModulation`先解析并回写profile，再提交设备无关generation plan；`DigitalModulator`只校验plan与第三方返回长度。合作方生成器返回的原始`int16`指针会直接移交给immutable `PlaybackPayload`，不再复制到第二份full-size QVector；同步下载结束后释放该外部存储。

设备切换后，如果当前 Digital 采样率不属于新设备 domain，业务关闭 Enabled 并把整组参数静默 reset 为默认配置；不逐项钳位，也不弹窗。临时断连不触发该复位。

---

### 6.9 DSSS 直接序列扩频

#### 参数意义

- `Rb`：符号速率
- `code`：扩频码阶数
- `span`：成型滤波器长度
- `sps`：每 chip 过采样数
- `Alpha`：滤波器参数
- `sequenceSeed`：序列种子
- `DigitalModType`：调制方式
- `filterType`：滤波器类型

#### 默认值

- `Rb = 1e6`
- `DigitalModType = BPSK`
- `code = 5`
- `span = 8`
- `sps = 4`
- `Alpha = 0.35`
- `sequenceSeed = 23`
- `filterType = RootRaisedCosine`

#### packing 层硬约束

- `code`: 4 ~ 16
- `sps`: 4 ~ 32
- `sequenceSeed`: 1 ~ 1000
- `span`: 偶数，且满足 `sps × span <= 400`
- `Alpha`：
  - RC / RRC：0.025 ~ 1.0
  - Gaussian：0.15 ~ 2.5

采样率关系为：

$$
F_s = R_b \times sps \times (2^{code} - 1)
$$

若该值不属于当前设备 Playback domain，则普通参数编辑会优先回写连续低档中的较小合法采样率，并反算新的 `Rb`；不会把落入 200~400 MHz 空洞的组合自动抬升到 400 MHz：

$$
F_s = normalizeDomain\left(R_b \times sps \times (2^{code} - 1)\right)
$$

并反算新的 `Rb`：

$$
R_b \leftarrow \frac{F_s}{sps \times (2^{code} - 1)}
$$

#### UI 与调制器层说明

- 当前 DSSS UI 仅开放 `BPSK`
- `sps` 的 UI 枚举范围为 4 ~ 32 的偶数
- 实际可选项还需满足当前设备domain。无带宽选件档使用`[195.3125 kHz, 125 MHz]`；含带宽选件档使用`[195.3125 kHz, 200 MHz] U {400 MHz}`：

$$
R_b \times sps \times (2^{code} - 1) \in SampleRateDomain_{current}
$$

#### 大小估算

当前 `DSSS_EstimatedSize()` 近似为：

$$
Size = symbolLength \times (2^{code} - 1) \times sps \times 4
$$

#### 当前生成长度选择

当前 DSSS 生成不再把 `symbolLength` 固定写死为一个大常数，而是按参数自动收口：

- 目标记录时长约为 `1.024 ms`，即

$$
symbolLength_{target} \approx R_b \times 1.024\text{ ms}
$$

- 为避免成型滤波器收敛区占比过大，最小长度还会满足：

$$
symbolLength \ge \max(256, 16 \times span)
$$

- 同时受当前设备 `maxWaveformBytes` 约束：

$$
symbolLength \le \left\lfloor \frac{maxWaveformBytes}{(2^{code} - 1) \times sps \times 4} \right\rfloor
$$

- 最终长度再量化到 `32` 个 symbol 的粒度。

这个策略的目标不是盲目拉长记录，而是让 DSSS 在常见 ARB 循环回放场景里，既有足够稳态符号，又避免无意义的超长随机记录。

`DsssModulation` 根据 current domain/capacity 回写参数并提交包含采样率、symbol length 和 payload layout 的 plan；`DsssModulator` 不保存设备能力。合作方 `GenerateDssWaveform()` 返回的 `short *` 由 immutable `PlaybackPayload` 直接接管，不再复制到第二个 full-size `QVector<int16_t>`。预计 payload 超过 125 MiB 时，调用生成 API 前必须先取得大内存租约；下载或临时保存结束后由 release callback 调用 `GenSignalObjRelease()` 并归还租约。

设备切换后，若当前 DSSS 采样率不属于新设备 domain，业务关闭 Enabled 并把整组参数静默 reset 为默认配置；不逐项钳位，也不弹窗。参数仍合法时按新设备容量重新选择 `symbolLength`。

---

### 6.10 OFDM 正交频分复用

#### 参数意义

- `FFTSize`：FFT 点数
- `symbolCount`：OFDM 符号数
- `GardBandCarriers_Left`：左保护子载波数
- `GardBandCarriers_Right`：右保护子载波数
- `GuardInterval`：循环前缀比例，单位 %
- `windowLength`：加窗长度比例，单位 %
- `NullDC`：是否去除直流子载波
- `Windowed`：是否启用窗口化
- `SampleRate`：采样率
- `DigitalModType`：子载波调制方式

#### 默认值

- `FFTSize = 64`
- `symbolCount = 16`
- `GardBandCarriers_Left = 6`
- `GardBandCarriers_Right = 5`
- `GuardInterval = 25`
- `windowLength = 50`
- `NullDC = true`
- `Windowed = true`
- `SampleRate = 20e6`
- `DigitalModType = QPSK`

- 极值例子：`fftSize = 2048`, `guardInterval = 100`, `symbolCount = 16384`
  - 字节数 = `2048 * 2 * 16384 * 4 = 268435456`
- 即便 `fftSize = 1024`，同样可达到 `134217728`，已大于无带宽选件档的125 MiB容量，可用于验证`symbolCount`自动回写；在1000 MiB含带宽选件档上同一组合仍合法。

#### packing 层硬约束

- `SampleRate`：按当前设备 Playback domain 回写；空洞输入优先向下落到连续低档，精确 400 MHz 才保留离散点
- 当 `Left + Right > FFTSize` 时：
  - `Left = FFTSize / 2`
  - `Right = FFTSize / 2`
- `symbolCount`: 2 ~ 16384
- `GuardInterval`: 0 ~ 100
- `windowLength`: 0 ~ 100

#### 当前实现注意

- 当前约束是“若左右保护子载波之和过大，则各回写为 FFTSize 的一半”。
- 这会使 `Left + Right = FFTSize`，并非严格小于 `FFTSize`。
- business 同时校验 `FFTSize` 必须为 16~2048 的 2 次幂；异常输入回写默认 64。
- business 根据当前 `maxWaveformBytes` 计算最大 `symbolCount`，超限时优先降低并永久回写 `symbolCount`。

#### 当前 UI 可选项

- 调制方式：BPSK、QPSK、8PSK、16PSK、QAM16、QAM64、QAM256
- FFTSize：16、32、64、128、256、512、1024、2048

#### 大小估算

当前 `OFDM_EstimatedSize()` 近似为：

$$
Size = \left\lceil FFTSize \times \left(1 + \frac{GuardInterval}{100}\right) \right\rceil \times symbolCount \times 4
$$

#### 大波形处理语义

- OFDM 不再提供“先生成超大波形、确认后截断下载”的路径，也不因参数收口弹窗。
- 参数编辑时，业务先按当前设备容量降低 `symbolCount`；设备切换导致原组合超容量时关闭 Enabled，并静默永久回写安全值。
- 设备切换导致 `SampleRate` 不属于新 domain 时，整组静默 reset 为默认配置。
- Playback 和 Save IQ 在调用 `GenerateOFDMWaveform(...)` 前再次检查预计字节数；严格大于当前容量时不生成、不保存。
- `OfdmModulation` 在业务层完成 sample-rate domain 与 `symbolCount` 容量收口，并提交精确 generation plan；`OfdmModulator` 不读取设备能力，只校验算法预计长度和第三方返回长度均未越过 plan。合作方 `short *` 由 immutable `PlaybackPayload` 直接接管；生成前取得大内存租约，下载后释放合作方对象。保存若 host storage 已在下载后释放，则在同一容量门禁和租约下重新生成一次并直接写文件，不建立 full-size `QByteArray/QVector` 副本。

---

## 7. 当前版本的关键结论

当前版本关于采样率与波形参数的核心结论如下：

1. 已迁移Playback业务使用current `SampleRateDomain`：无带宽选件档为连续195.3125 ksps~125 Msps；含带宽选件档为连续195.3125 ksps~200 Msps加精确400 Msps单点。
2. Digital、DSSS、OFDM 与 HTRA AM/FM/PM/Pulse/Multitone/Ramp/AWGN 已消费 current domain/capacity；Step Sweep 等尚未迁移业务仍使用 legacy 口径。Streaming 模式单独受限于 62.5 Msps。
3. AWGN与Ramp使用1.25倍工程裕量：无带宽选件档最大有效带宽100 MHz；含带宽选件档连续上限160 MHz，并额外允许精确320 MHz对应400 Msps。
4. 当前产品层大波形主策略已收敛为 trim-only / playback-first，不再把自动切到 Streaming 作为默认处理。
5. HTRA Ramp的`Span/SweepTime/Period`由current domain/capacity动态收口；Period上限由`1.25 * Span`的最小合法采样率和current 125/1000 MiB容量共同决定。
6. 对当前 HTRA Ramp，超大单周期波形的主要保护已经前移到参数层与生成层，正常 UI/business 路径不会再依赖 runtime trim 作为第一道防线。
7. Digital/DSSS 可在生成阶段按当前设备 `maxWaveformBytes` 反推长度；OFDM 通过参数估算提前限制 `symbolCount`。HTRA 七类本地生成业务也在分配前检查最终字节数，并通过唯一 builder 取得大内存租约。
8. Digital 与 DSSS 的 oversample 可选项依然是按 current domain 动态计算的，不能仅依据固定枚举理解其真实可用范围。

---

## 8. 实现对应文件

本文档当前版本主要对应以下实现文件；其中 Ramp 章节已按当前 HTRA 本地实现同步：

- `src/libs/utils/constants.h`
- `src/libs/utils/constants.cpp`
- `src/plugins/analog/packing.cpp`
- `src/plugins/analog/analoggenerationresolverutils.h/.cpp`
- `src/plugins/analog/externalwaveformgenerationplan.h`
- `src/plugins/analog/digitalmodulation.cpp` / `digitalmodulator.cpp`
- `src/plugins/analog/dsssmodulation.cpp` / `dsssmodulator.cpp`
- `src/plugins/analog/ofdmmodulation.cpp` / `ofdmmodulator.cpp`
- `src/plugins/analog/analogmodulationplugin.cpp`
- `src/plugins/core/playbackpayload.h/.cpp`
- `src/plugins/htra/generatedplaybackutils.h/.cpp`
- `src/plugins/htra/ammodulator.cpp`、`fmmodulator.cpp`、`pmmodulator.cpp`
- `src/plugins/htra/awgnmodulator.cpp`
- `src/plugins/htra/multitonegenerator.cpp`
- `src/plugins/htra/rampmodulator.h`
- `src/plugins/htra/rampmodulator.cpp`
- `src/plugins/htra/rampmodulation.cpp`

如后续代码再次调整采样率模型、Streaming 上限或 UI 调制方式，本文件应同步更新。
