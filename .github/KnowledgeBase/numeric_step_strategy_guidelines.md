# 数值按钮步长策略设计指南

## 1. 目的

本文档从整个软件的角度，统一说明 SGStudio 中“会弹数值软键盘的前端按钮”应该如何选择步长策略。

本文重点回答三个问题：

- 哪些按钮更适合“可编辑步长”；
- 哪些按钮更适合“125 步长”；
- 哪些按钮更适合“固定步长”。

为了便于产品、测试和研发沟通，本文优先使用前端控件名或按钮文案作为描述对象，再附带对应 property 名。

## 2. 适用范围

本文讨论的是当前通过 `PropertyBindingHelper::prepareNumericKeyBoard(...)` 进入软键盘链路的数值编辑控件，主要包括：

- `LabelButton`
- 少数通过 `NumericKeyboardConfig` 直接组装键盘的局部编辑控件

本文不重点讨论以下对象：

- `EnumTextButton` 这类枚举选择按钮
- `SwitchButton` 这类开关按钮
- 纯显示、只读、不允许打开数值键盘的状态按钮
- 旧的 `QDoubleSpinBox/FrequencyControl/TimeControl` 直编式控件

## 3. 三类策略的本质区别

### 3.1 可编辑步长

适用于“用户经常要自己决定当前粗调/细调网格”的按钮。

这类量的特征是：

- 它本身就是一个操作网格量，而不是被网格作用的目标值；
- 不同测试任务下，用户希望快速切换 `10 MHz`、`1 MHz`、`100 kHz` 这类步长；
- 单一固定步长无法覆盖现场使用场景。

典型例子：

- 频率步进
- dwell time
- common panel 主频点/主功率
- 外参考频率这类设备环境量

### 3.2 125 步长

适用于“典型射频主量或大范围主量”的按钮。

这类量的特征是：

- 用户通常是在浏览一个跨度很大的量级区间；
- 用户更关心“1 -> 2 -> 5 -> 10”这种数量级切换是否自然；
- 用户大多数时候是在调目标值，不是在调网格本身。

125 步长最适合：

- 主频率量
- 大范围 sample rate
- 大范围样点/长度主量

它不适合：

- 百分比
- 枚举近似量
- 强离散整数
- 协议/算法字段

### 3.3 固定步长

适用于“用户需要确定性，而不是自由网格”的按钮。

这类量的特征是：

- 量纲很小，且常有明确工程分辨率；
- 取值范围虽然连续，但业务上通常用固定精度工作；
- 或者它本质上是百分比、离散整数、计数、长度、seed、滤波器长度等强约束参数。

固定步长的优势是：

- 行为稳定，可预测；
- 更容易和业务校验、上下限、联动约束对齐；
- 不会把本来就离散的量错误地做成“浏览型主量”。

## 4. 选择原则

可以用下面这条判断链做快速分类：

1. 如果这个按钮编辑的是“扫描网格”或“用户现场要反复换粗细粒度的量”，优先可编辑步长。
2. 如果这个按钮编辑的是“射频主量、sample rate、跨度很大的主量”，优先 125 步长。
3. 如果这个按钮编辑的是“百分比、离散整数、滤波/协议字段、局部结构编辑量”，优先固定步长。

不要反过来按“单位类型”机械分类。不是所有 Frequency 都该 125，也不是所有 Time 都该 125。

真正决定策略的是这个量在交互上的角色：

- 是目标值，还是网格值；
- 是浏览型主量，还是确定性参数；
- 是前面板主调节量，还是局部算法字段。

## 5. 推荐为可编辑步长的按钮

### 5.1 CommonPanel / 设备公共设置

| 模块 | 前端按钮 | property | 推荐策略 | 原因 |
| :--- | :--- | :--- | :--- | :--- |
| CommonPanel | `Frequency` / `m_freq` | `Center` | 可编辑步长 | 主频点是典型前面板主操作量，用户会频繁在粗调与细调间切换。 |
| CommonPanel | `Level` / `m_level` | `Level` | 可编辑步长 | 主输出电平同样是前面板主操作量，现场经常需要临时切换 `10 dB`、`1 dB`、`0.1 dB`。 |
| Device Settings / Reference | `RefFreq` / `m_refClockFreqBtn` | `RefClockFrequency` | 可编辑步长 | 外参考频率更像“设备环境网格量”，用户需要按现场参考源快速切换步长。 |

### 5.2 StepSweep

| 模块 | 前端按钮 | property | 推荐策略 | 原因 |
| :--- | :--- | :--- | :--- | :--- |
| StepSweepPanel | `Frequency Step` / `btnStepFreq` | `StepSweep_FreqStep` | 可编辑步长 | 这是扫描网格本身，不是被扫描的目标值。 |
| StepSweepPanel | `Dwell Time` / `btnDwellTime_Analog` | `StepSweep_DwellTimeAnalog` | 可编辑步长 | dwell 是扫描节拍量，现场经常按测试节奏切换粗细。 |

### 5.3 List / Fill / Edit 场景

| 模块 | 前端按钮 | property | 推荐策略 | 原因 |
| :--- | :--- | :--- | :--- | :--- |
| EditDialog | `Increment Value` / `btnFrequencyStep` | `FrequencyStep` | 可编辑步长 | 它本质上是批量填表的频率网格。 |
| EditDialog | `Increment Value` / `btnTimeStep` | `TimeStep` | 可编辑步长 | 它本质上是批量填表的时间网格。 |

### 5.4 可编辑步长的边界

以下按钮虽然也是主量，但不建议默认做成可编辑步长：

- `Start Frequency`
- `Stop Frequency`
- `Rate`
- `Deviation`
- `Sample Rate`

原因是这些按钮更像“浏览型目标值”，而不是“网格定义量”。如果把它们全部升级为可编辑步长，会让用户先思考 step，再思考目标值，交互负担会变重。

## 6. 推荐为 125 步长的按钮

### 6.1 扫频主量

| 模块 | 前端按钮 | property | 推荐策略 | 原因 |
| :--- | :--- | :--- | :--- | :--- |
| StepSweepPanel | `Start Frequency` / `btnStartFreq` | `StepSweep_StartFreq` | 125 | 典型扫频起点，用户更需要自然浏览数量级。 |
| StepSweepPanel | `Stop Frequency` / `btnStopFreq` | `StepSweep_StopFreq` | 125 | 典型扫频终点，和起点应保持一致的 decade browsing 语义。 |
| EditDialog | `Start Value` / `btnStartFrequency` | `StartFrequency` | 125 | 频率填表起点属于主频率量。 |
| EditDialog | `End Value` / `btnStopFrequency` | `StopFrequency` | 125 | 频率填表终点属于主频率量。 |

### 6.2 Analog modulation 主频率量

| 模块 | 前端按钮 | property | 推荐策略 | 原因 |
| :--- | :--- | :--- | :--- | :--- |
| AMPanel | `Rate` / `btnRate` | `Am_Rate` | 125 | 调制率是 AM 的主频率量。 |
| FMPanel | `Rate` / `btnRate` | `Fm_Rate` | 125 | 调制率是 FM 的主频率量。 |
| FMPanel | `Deviation` / `btnDeviation` | `Fm_Deviation` | 125 | 频偏是 FM 的主幅度频率量，天然适合 125。 |
| RampPanel | `Span` / `btnSpan` | `Ramp_Span` | 125 | 扫宽是典型 RF 主量。 |
| AwgnPanel | `Bandwidth` / `btnBandwidth` | `Awgn_Bandwith` | 125 | 带宽是浏览型主频率量。 |
| DigitalPanel | `Symbol Rate` / `symbolRate` | `Digital_SymbolRate` | 125 | 符号率是数字调制主频率量。 |
| DigitalPanel | `FSK Deviation` / `fskDeviation` | `Digital_FSKDeviation` | 125 | FSK 频偏语义上与 FM 偏差同类。 |
| DssPanel | `Symbol Rate` / `btnSymbolRate` | `Dsss_SymbolRate` | 125 | DSSS 主速率量。 |
| OfdmPanel | `Sample Rate` / `btnSampleRate` | `Ofdm_SampleRate` | 125 | OFDM sample rate 是典型大范围主量。 |

### 6.3 HTRA Arb / 大范围结构量

| 模块 | 前端按钮 | property | 推荐策略 | 原因 |
| :--- | :--- | :--- | :--- | :--- |
| ArbPanel | `Sample Rate` / `btnSampleRate` | `Arb_SampleRate` | 125 | 大范围 sample rate 主量，用户多半是在浏览可用量级。 |
| ArbPanel | `Sample Offset` / `btnSampleOffset` | `Arb_SampleOffset` | 125 | 大范围样点位移量，更适合 decade browsing。 |
| ArbPanel | `Samples To Use` / `btnSamplesToUse` | `Arb_SampleToUse` | 125 | 大范围样点长度量，更适合 decade browsing。 |
| ArbPanel | `Period_Arb` / `btnPeriod` | `Arb_Period` | 125 | 周期样点量范围大，125 比固定 `+1` 更实用。 |

### 6.4 125 的边界

以下按钮虽然带有时间或长度语义，但默认不建议直接归入 125：

- `Width`
- `Period`（Pulse）
- `Sweep Time`
- `Period`（Ramp）
- `Length`（AWGN）
- `Start Time`
- `End Time`

这些量往往被业务联动强约束，用户更常做的是精确输入，而不是 decade browsing。

## 7. 推荐为固定步长的按钮

### 7.1 百分比、比例量

| 模块 | 前端按钮 | property | 推荐策略 | 原因 |
| :--- | :--- | :--- | :--- | :--- |
| AMPanel | `Depth(%)` / `btnDepth` | `Am_Depth` | 固定 | 百分比量，本质是确定性比例参数。 |
| PulsePanel | `Duty Cycle(%)` / `btnDutyCycle` | 局部 `NumericKeyboardConfig` | 固定 | 比例量，不适合 125，也不需要用户改 step 网格。 |
| ArbPanel | `I/Q Scale(%)` / `btnIQScale` | `Arb_IqScale` | 固定 | 百分比缩放量，业务语义稳定。 |
| DssPanel | `Filter Alpha` / `btnFilterAlpha` | `Dsss_FilterAlpha` | 固定 | 滚降因子类比例量。 |
| DigitalPanel | `Filter Alpha` / `filterAlpha` | `Digital_FilterAlpha` | 固定 | 滚降因子类比例量。 |
| OfdmPanel | `Guard Interval (%)` / `btnGuardInterval` | `Ofdm_GuardInterval` | 固定 | 百分比量。 |
| OfdmPanel | `Window Length (%)` / `btnWindowLengthPer` | 局部百分比量 | 固定 | 百分比量。 |

### 7.2 功率类确定性参数

| 模块 | 前端按钮 | property | 推荐策略 | 原因 |
| :--- | :--- | :--- | :--- | :--- |
| StepSweepPanel | `Start Level` / `btnStartLevel` | `StepSweep_StartLevel` | 固定 | 目标功率值需要确定性分辨率。 |
| StepSweepPanel | `Stop Level` / `btnStopLevel` | `StepSweep_StopLevel` | 固定 | 目标功率值需要确定性分辨率。 |
| StepSweepPanel | `Level Step` / `btnStepLevel` | `StepSweep_LevelStep` | 固定 | 功率步进一般使用明确的 dB 网格，不宜再引入 125。 |
| EditDialog | `Start Value` / `btnStartPower` | `StartPower` | 固定 | 填表功率值通常按明确 dB 分辨率工作。 |
| EditDialog | `End Value` / `btnStopPower` | `StopPower` | 固定 | 同上。 |
| EditDialog | `Increment Value` / `btnPowerStep` | `PowerStep` | 固定 | 功率步进一般就是确定性的 dB 网格。 |

### 7.3 时间主参数但强约束场景

| 模块 | 前端按钮 | property | 推荐策略 | 原因 |
| :--- | :--- | :--- | :--- | :--- |
| PulsePanel | `Width` / `btnWidth` | `Pulse_Width` | 固定 | 脉宽与周期强耦合，用户通常追求精确分辨率。 |
| PulsePanel | `Period` / `btnPeriod` | `Pulse_Period` | 固定 | 周期与 duty cycle 强联动，固定更稳妥。 |
| RampPanel | `Sweep Time` / `btnSweepTime` | `Ramp_SweepTime` | 固定 | 扫描时长是受业务约束的确定性量。 |
| RampPanel | `Period` / `btnPeriod` | `Ramp_Period` | 固定 | 周期量通常和扫宽/扫时联动。 |
| AwgnPanel | `Length` / `btnLength` | `Awgn_Length` | 固定 | 波形长度更像精确工程参数，不是主浏览量。 |
| EditDialog | `Start Value` / `btnStartTime` | `StartTime` | 固定 | 填表时间起点强调精确边界。 |
| EditDialog | `End Value` / `btnStopTime` | `StopTime` | 固定 | 填表时间终点强调精确边界。 |
| ArbSequenceEditor | `sectionStart` | 局部 `NumericKeyboardConfig` | 固定 | 局部结构编辑量，当前就适合用派生 seed 而不是 125。 |
| ArbSequenceEditor | `sectionLen` | 局部 `NumericKeyboardConfig` | 固定 | 局部结构编辑量。 |

### 7.4 离散整数、计数、算法字段

| 模块 | 前端按钮 | property | 推荐策略 | 原因 |
| :--- | :--- | :--- | :--- | :--- |
| ListModePanel | `Range From` / `btnRangeFrom` | `ListMode_RangeFrom` | 固定 | 行号/范围边界量，本质是整数计数。 |
| ListModePanel | `Range To` / `btnRangeTo` | `ListMode_RangeTo` | 固定 | 行号/范围边界量。 |
| EditDialog | `Row Count` / `btnRowCount` | `RowCount` | 固定 | 行数是离散整数。 |
| DigitalPanel | `PN` / `pn` | `Digital_PN` | 固定 | 协议/算法字段，离散量。 |
| DigitalPanel | `Sequence Seed` / `sequenceSeed` | `Digital_SequenceSeed` | 固定 | seed 量不适合 decade browsing。 |
| DigitalPanel | `Filter Length` / `filterLength` | `Digital_FilterLength` | 固定 | 过滤长度是离散整数。 |
| DssPanel | `Code` / `btnCode` | `Dsss_Code` | 固定 | 离散编码字段。 |
| DssPanel | `Sequence Seed` / `btnSequenceSeed` | `Dsss_SequenceSeed` | 固定 | seed 量。 |
| DssPanel | `Filter Length (symbols)` / `btnFilterLength` | `Dsss_FilterLength` | 固定 | 离散整数。 |

## 8. 不建议纳入主步长策略表的按钮

以下按钮存在，但不建议放进主步长设计表里作为重点对象：

- 纯显示或只读按钮，例如 `Samples In File`、`Signal Length`、`Period Length`、`Total Files`、`Total Samples`、`Total Duration`、`Playback Pos`
- 枚举按钮，例如 `Shape`、`Filter Type`、`Modulation Type`、`FFT Size`
- 开关按钮，例如 `Enabled`、`Null DC`、`Windowed`、`SystemClockOut`

它们不是“数值步进策略”问题，而是显示、枚举或状态切换问题。

## 9. 总结规则

如果只记一条经验法则，可以记成下面三句：

- “主频率量、主 sample rate、大范围样点量”优先 125。
- “扫描网格量、前面板主操作量、设备环境量”优先可编辑步长。
- “百分比、功率确定性参数、时间强约束量、离散整数/seed/长度字段”优先固定步长。

## 10. 相关实现锚点

步长策略不是只写在文档里，当前实现上的关键锚点如下：

- 软键盘公共入口：`src/libs/business/utils.h/.cpp`
- 属性按钮绑定：`src/libs/business/propertybindingmanager.cpp`
- CommonPanel：`src/plugins/core/commonpanel.cpp`
- StepSweep：`src/plugins/core/stepsweeppanel.cpp` 与 `src/plugins/core/coreplugin.cpp`
- Device settings / Reference：`src/plugins/core/devicesettingdialog.cpp`
- Analog panels：`src/plugins/analog/*.cpp` 与对应 `.ui`
- HTRA Arb：`src/plugins/htra/arbpanel.cpp`、`src/plugins/htra/arbmodulation.cpp`
- List fill/edit：`src/plugins/core/editdialog.cpp`
- Arb 局部编辑：`src/tools/arbeditor/arbsequenceeditor.cpp`

后续若要继续推广步长策略，建议先看“按钮在交互中的角色”，再决定是 `STEPEDITABLE`、`stepStrategy=125`，还是显式 `setStep(...)`。