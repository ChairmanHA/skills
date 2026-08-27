# Keysight N9040B 脉冲检测：122 设备窄脉宽/短周期操作教程

日期：2026-07-27

## 1. 教程目标

本文只讲 Keysight N9040B 的 `Pulse Analysis / Pulse Detection` 功能，用它验证 SGStudio HTRA Pulse：

1. 先复测既有 `Width = 48 ns, Period = 96 ns`。
2. 再逐级缩小 Width 和 Period。
3. 最终尝试当前含带宽选件设备的边界 `15 ns / 20 ns`。

本文不展开普通频谱、IQ Analyzer、AM/FM/PM 解调或自动化 SCPI。

本文依据：

- 当前实际编译的 [pulsemodulation.cpp](../../src/plugins/htra/pulsemodulation.cpp)。
- 当前实际编译的 [pulsemodulator.cpp](../../src/plugins/htra/pulsemodulator.cpp)。
- [Playback 波形参数的设备能力约束策略](playback_waveform_parameter_capability_policy.md)。
- [HTRA RMS 功率原理：Multitone 与 Pulse](htra_rms_power_multitone_pulse_principle.md)。
- [此前的 N9040B 脉冲实测结果](../TaskLog/脉冲测试结果.md)。

Keysight 官方资料确认 N9067C Pulse Analysis 能输出 pulse table，并测量 power、droop、overshoot、ripple、rise/fall、Width、PRI 等指标；N9040B 的 B2X 路径提供 255 MHz 分析带宽。官方参考链接放在文末。

## 2. 先明确四个不同的量

测试时不要混淆下面四个参数：

| 参数 | 属于谁 | 本轮含义 |
| :--- | :--- | :--- |
| SGStudio `Width / Period` | DUT 波形参数 | 用户要求生成的脉宽和周期 |
| DUT Playback Sample Rate | 122 设备 | SGStudio 自动解析，本轮可能是187.5、200或400 MSPS |
| N9040B IF/Analysis Bandwidth | 测量仪器 | 本轮使用可用的255 MHz宽带路径 |
| N9040B Pulse Analysis Sample Rate | 测量仪器 | 用户当前可设置到255 MSa/s，用于检测和分析采样 |

`DUT 使用 400 MSPS` 不表示 N9040B 也必须显示 400 MSa/s。DUT Sample Rate 决定发射波形的离散构造；N9040B 的 IF 带宽和分析 Sample Rate 决定仪器如何采集这个模拟 RF 信号。

若 N9040B 的 Bandwidth、Span、IF Path 和 Sample Rate 在当前固件中自动耦合，以仪器最终显示的实际值为准。整套矩阵必须保持同一组宽带设置，并在记录表中写下实际值。

## 3. 当前代码决定的预期

### 3.1 122 型号本身不是极限能力判据

当前代码不再仅凭 model 122 判断能力，而是在设备打开后检查 `OPTION_BW_320M_TX`：

| 设备实际返回 | Playback 能力 | Pulse 下限 |
| :--- | :--- | :--- |
| 未返回选件，或选件查询失败 | 连续到125 MSPS，无400 MSPS点 | Width至少48 ns，Period至少96 ns |
| 返回 `OPTION_BW_320M_TX` | 连续到200 MSPS，另有精确400 MSPS点 | 连续档30/40 ns；400 MSPS档15/20 ns |

因此开始极限测试前，必须先确认这台 122 确实进入含选件档。

最直接的现场判断：

1. 关闭 Pulse `Enabled`。
2. 输入 `Width = 15 ns`、`Period = 20 ns`。
3. 完成编辑后观察 SGStudio 是否仍显示 `15 ns / 20 ns`。
4. 如果被永久回写到 `48 ns / 96 ns` 或附近，先停止极限测试，检查设备选件查询、软件版本和设备连接；不要把它当成 N9040B 检测失败。

### 3.2 当前 Pulse 的样点约束

当前 resolver 强制：

```text
Width samples  >= 6
Period samples >= 8
Width <= Period
```

Width 和 Period 必须落到整数样点。若输入不能精确落格，SGStudio 会把 UI 永久回写为实际生成值。

### 3.3 当前 50% Width 会比 UI Width 短

当前 generator 的窄脉冲边沿是：

```text
Off -> Half -> Full ... Full -> Half -> Off
```

若 `Nw` 是 Width 样点数、`Fs` 是 DUT Playback Sample Rate，则在理想离散波形、50% 电压阈值下：

```text
N9040B Width 预期约为 (Nw - 1) / Fs
```

而不是：

```text
Nw / Fs = SGStudio UI Width
```

这不是 N9040B 测错，而是当前半幅边沿语义。模拟 RF 链路、仪器滤波和线性插值会再造成少量偏差。

### 3.4 48/96 ns 在当前含选件122上的真实计划

对含 `OPTION_BW_320M_TX` 的122：

```text
UI Width       = 48 ns
UI Period      = 96 ns
DUT Fs         = 187.5 MSPS
Width samples  = 9
Period samples = 18
代码理想50% Width ≈ (9 - 1) / 187.5 MHz
                    ≈ 42.67 ns
代码理想50% Duty  ≈ 42.67 / 96
                    ≈ 44.44 %
```

旧的无选件基线档才是：

```text
125 MSPS，6/12点，代码理想50% Width约40 ns
```

所以本轮在含选件122上，不应继续把 `40 ns` 当作48/96组合的唯一目标。

## 4. 台架连接和固定条件

推荐连接：

```text
122 RF OUT
  -> 固定衰减器（建议先用10 dB或20 dB）
  -> N9040B RF INPUT
```

测试前固定并记录：

- SGStudio版本、commit、构建时间。
- 设备型号、序列号、固件版本。
- RF Center Frequency。
- SGStudio Level/PEP。
- 外部衰减器标称值。
- 射频线缆和转接头。
- N9040B输入衰减、Preamp、Reference Level。
- N9040B实际 IF/Analysis Bandwidth 和 Pulse Analysis Sample Rate。

建议整个矩阵保持 RF Frequency、Level/PEP、线缆、外部衰减、N9040B输入衰减和参考电平不变。只改变 Width 和 Period。

如果要让 `Top Level` 与 SGStudio PEP 直接比较，应在 N9040B 中配置外部增益/损耗修正，数值等于外部衰减器、线缆和转接损耗之和。若暂时不做修正，也可以用本文的长脉冲基线做相对比较。

## 5. N9040B 脉冲检测初始设置

不同固件的菜单文字可能略有差异。以下同时给出英文功能名；按功能名找到等价入口即可。

### 5.1 进入 Pulse Analysis

1. 打开 N9040B 的 `Pulse Analysis / Pulse Measurement` 应用。
2. 对该应用执行一次 `Mode Preset / Meas Preset`。
3. 将 Center Frequency 设置为 DUT 的 RF Frequency。
4. 打开以下两个主要结果窗口：
   - `Amplitude / Power vs Time`，用于观察脉冲时域形状。
   - `Pulse Table`，用于读取每个脉冲的数值。
5. 若界面支持 `Summary`，一并打开，主要看有效 Pulses 数、Top、Base 和 Detection Threshold。

Pulse Table 至少保留以下列：

```text
Pulse
Mod
Top Level
Base Level
Top/Base
Rise Time
Fall Time
Width
PRI
Duty Cycle
Droop
Overshoot
Ripple
```

`Mod = CW` 是正常结果，表示脉冲打开期间没有 chirp、FSK 或相位编码，不表示没有脉冲。

### 5.2 RF 前端

初始建议：

```text
Input Impedance    = 50 ohm
Preamp             = Off
Input Attenuation  = 先用20 dB；确认不过载后可改10 dB
Reference Level    = 让脉冲峰值距离屏顶保留约5~10 dB
```

若出现 ADC Overload、IF Overload 或明显削顶：

1. 先增加 N9040B Input Attenuation。
2. 再降低 DUT Level/PEP。
3. 不要通过缩小分析带宽来掩盖过载。

若信号太低、Top/Base 不足：

1. 确认没有过载后逐步降低 Input Attenuation。
2. 必要时提高 DUT Level/PEP。
3. 保持宽带 IF，不要为了降低噪声而先牺牲窄脉冲带宽。

### 5.3 宽带采集

进入 `Time / Acquisition / Pulse Properties` 中与采集有关的页面：

```text
IF/Analysis Bandwidth = 255 MHz可用最大档
Sample Rate           = 255 MSa/s可用最大档
Averaging             = Off
```

如果仪器自动耦合 Bandwidth、Span 与 Sample Rate：

1. 先选择255 MHz宽带 IF Path。
2. 将 Pulse Analysis Bandwidth/Span 或 Sample Rate 调到当前配置允许的最大值。
3. 记录仪器最终显示值，不要强行要求两个输入框都恰好为255。

在用户当前的 `255 MSa/s` 下：

```text
仪器分析采样间隔约为 1 / 255 MHz = 3.92 ns
```

因此：

- `48 ns / 96 ns` 有较充足的分析样点。
- `15 ns / 20 ns` 的 UI Width/Period 只有约3.83/5.10个分析样点。
- Pulse Analysis 会进行阈值交点插值，但 `15/20 ns` 仍应视为极限功能探索，不应单凭一次读数做高精度定标。

### 5.4 Acquisition Length 和 Pulse Count

长脉冲基线使用：

```text
Acquisition Length = 20 us
Maximum Pulse Count = 10或20
Selected Pulses = All，或前10个
```

纳秒级矩阵统一使用：

```text
Acquisition Length = 2 us
Maximum Pulse Count = 20
Selected Pulses = All，或前10个
```

`2 us` 能覆盖：

- 48/96 ns：约20个周期。
- 15/20 ns：约100个周期。

Pulse Table 只需分析前20个有效脉冲。若应用提示记录长度不足，略微增加 Acquisition Length，但不要把时域显示拉得过长；显示窗口可单独缩放到2~4个周期。

### 5.5 Pulse Detection

进入 `Pulse Detection` 页面，初始设置：

```text
Reference       = Relative To Top Level
Threshold       = -6 dB
Hysteresis      = 0 dB
Ignore Dropouts = 0 s
Min Pulse Width = Disabled
Max Pulse Width = Disabled
```

关键解释：

- `Threshold = -6 dB` 只用于判定一个有效的开/关脉冲，不等同于最终 Width 的50%测量阈值。
- `Hysteresis = 0 dB` 对本轮高 PRF、短 off-time 更稳妥。增加 hysteresis 会要求信号下降到更低的关断电平，可能让15/20 ns更难形成有效 pulse-off。
- `Ignore Dropouts` 必须为 `0`。15/20 ns的 UI off-time 只有5 ns；任何大于或接近5 ns的 dropout 忽略设置都可能把多个周期合并成一个长脉冲。
- `Min Pulse Width` 先关闭。若必须填写，设为 `5 ns` 或更小。不要设为15 ns，因为当前代码的15 ns UI Width在50%阈值下预期约12.5 ns。

若出现噪声造成的假脉冲：

1. 先保持 Hysteresis 为0。
2. 检查输入功率和 Top/Base。
3. 再尝试 Hysteresis 1~2 dB。
4. 每次只改一个量，并在记录中注明。

若高占空比组合检测不到 pulse-off，例如15/20 ns：

1. 保持 `Ignore Dropouts = 0`。
2. 保持 `Hysteresis = 0`。
3. 将 Threshold 从 `-6 dB` 改到 `-4 dB`，再试 `-3 dB`。
4. 阈值越靠近 Top，越容易把尚未完全降到底的短暂关断识别为 pulse-off；但若过于接近Top，又可能受平台纹波影响。

### 5.6 Regions / Thresholds

进入 `Regions / Thresholds`：

```text
Amplitude Domain       = Voltage
Pulse Width Threshold  = 50 %
Rise Time Lower        = 10 %
Rise Time Upper        = 90 %
Fall Time Upper        = 90 %
Fall Time Lower        = 10 %
Top/Base Method        = Mode
```

`Amplitude Domain = Voltage` 和 `Pulse Width = 50%` 是本轮与代码半幅边沿对账的统一口径。若改成 Power 域，50% 对应的幅度位置不同，Width 会发生系统性变化。

Top/Base Method 建议整套矩阵固定为 `Mode`。若最窄脉冲下 Mode 明显不稳定，可另做一轮 `Median` 复测，但不要在同一张趋势表中混用两种方法。

### 5.7 Trigger

对连续重复脉冲，优先使用：

```text
Trigger = Free Run / Auto / Immediate
```

让 Pulse Analysis 在采集记录内自行检测脉冲。这样不会要求5 ns左右的短 off-time 重新触发仪器。

如果时域轨迹无法稳定，可尝试：

```text
Trigger Source = IF Magnitude / RF Burst
Slope          = Positive
Trigger Level  = 约Top Level下方3~6 dB
```

但到了 `15/20 ns`，外部或IF幅度触发的重新武装时间可能比脉冲周期更重要。若 Trigger 模式丢脉冲，而 Free Run 能稳定生成 Pulse Table，优先保留 Free Run。

## 6. 正式测试前：长脉冲基线

这一步仍在 N9040B Pulse Detection 功能内完成。

### 6.1 SGStudio

1. 将 Pulse `Enabled` 关闭。
2. 保持本轮固定的 RF Frequency 和 Level/PEP。
3. 先设置：

```text
Width  = 1 us
Period = 2 us
Duty   = 50 %
```

4. 等待参数稳定，确认 UI 没有异常回写。
5. 打开 Pulse `Enabled`。

含选件档短周期下，当前代码预期：

```text
DUT Fs         = 200 MSPS
Width samples  = 200
Period samples = 400
代码理想50% Width ≈ 995 ns
PRI                   ≈ 2 us
```

### 6.2 N9040B

使用：

```text
Acquisition Length = 20 us
Maximum Pulse Count = 10或20
Detection Threshold = Relative To Top, -6 dB
```

记录：

- `Top Level`：作为本轮长脉冲平台功率基线。
- `Base Level` 和 `Top/Base`。
- `Width`、`PRI`、Duty。
- Rise/Fall、Droop、Overshoot、Ripple。

判定：

- PRI 应接近2 us。
- Width 应接近995 ns，而不是必须恰好1 us。
- Pulse Table 应连续检测到至少10个有效脉冲。
- Top Level 应稳定且仪器无过载。

如果这一步都不稳定，先不要进入纳秒级测试。

## 7. 第一阶段：复测48/96 ns

### 7.1 SGStudio操作

1. 关闭 Pulse `Enabled`。
2. 先改 Width，再改 Period：

```text
Width  = 48 ns
Period = 96 ns
```

先改 Width 是为了在从大参数向小参数切换时始终满足 `Width <= Period`。

3. 完成输入后重新查看两个字段，必须仍显示：

```text
48 ns / 96 ns
```

4. 等待波形生成完成。
5. 打开 Pulse `Enabled`。

若当前版本再次回写成 `50 ns / 95 ns`，很可能仍在使用旧的纳秒分数容差实现；当前代码已经修复该问题。

### 7.2 N9040B操作

设置：

```text
Acquisition Length = 2 us
Maximum Pulse Count = 20
Sample Rate = 当前可用255 MSa/s
IF/Analysis BW = 当前可用255 MHz
Pulse Detection Reference = Relative To Top
Threshold = -6 dB
Hysteresis = 0 dB
Ignore Dropouts = 0
Min/Max Pulse Width Filter = Off
Amplitude Domain = Voltage
Pulse Width Threshold = 50 %
```

先连续采集，确认 Pulse Table 稳定后，再 `Single` 保存证据。

### 7.3 当前代码预期

```text
DUT Fs                  = 187.5 MSPS
DUT Width/Period点数     = 9 / 18
代码理想50% Width        = 42.67 ns
代码理想50% PRI          = 96 ns
代码理想50% Duty         = 44.44 %
```

建议初始判定窗口：

```text
PRI   : 96 ns ± max(4 ns, 5%)
Width : 42.67 ns ± max(4 ns, 10%)
```

这里的窗口是本轮使用255 MSa/s分析采样率时的工程判定建议，不是 Keysight 规格保证。

### 7.4 48/96 ns功能通过条件

同时满足以下条件，可判为“组合功能正常”：

1. SGStudio 最终仍显示48/96 ns，没有被意外回写。
2. Pulse Table 连续检测至少10个有效脉冲，没有明显漏脉冲或双计数。
3. PRI 落在建议窗口内。
4. Width 接近当前代码预期的42.67 ns，而不是机械要求48 ns。
5. Duty 接近 `Width / PRI`，约44%。
6. Top Level稳定；与1 us/2 us长脉冲基线的差值已记录。
7. N9040B无 RF/IF/ADC overload。

Top Level下降不应与“周期功能失败”混为一谈。若 PRI和Width正确但Top Level明显下降，应进入功率/带宽排查。

## 8. 第二阶段：逐级缩小Width和Period

不要从48/96 ns直接跳到15/20 ns。分成两个子矩阵：

- A：保持 UI Duty = 50%，观察脉宽和周期同步缩小时的趋势。
- B：固定最小 Width，再缩短 Period，专门验证短 off-time 和高占空比检测。

### 8.1 矩阵A：50% UI Duty阶梯

| 顺序 | SGStudio Width/Period | 当前代码DUT Fs | DUT点数 W/P | 代码理想50% Width | 代码理想50% Duty | 255 MSa/s下UI W/P约含分析样点 | 目的 |
| :---: | :---: | ---: | :---: | ---: | ---: | :---: | :--- |
| A0 | 1000/2000 ns | 200 MSPS | 200/400 | 995 ns | 49.75% | 255/510 | 长脉冲功率基线 |
| A1 | 48/96 ns | 187.5 MSPS | 9/18 | 42.67 ns | 44.44% | 12.24/24.48 | 既有组合回归 |
| A2 | 40/80 ns | 200 MSPS | 8/16 | 35 ns | 43.75% | 10.20/20.40 | 连续档8点 |
| A3 | 35/70 ns | 200 MSPS | 7/14 | 30 ns | 42.86% | 8.93/17.85 | 连续档7点 |
| A4 | 30/60 ns | 200 MSPS | 6/12 | 25 ns | 41.67% | 7.65/15.30 | 连续档最小Width |
| A5 | 25/50 ns | 400 MSPS | 10/20 | 22.5 ns | 45.00% | 6.38/12.75 | 首个明确400 MSPS阶梯 |
| A6 | 20/40 ns | 400 MSPS | 8/16 | 17.5 ns | 43.75% | 5.10/10.20 | 更窄400 MSPS阶梯 |
| A7 | 17.5/35 ns | 400 MSPS | 7/14 | 15 ns | 42.86% | 4.46/8.93 | 接近分析采样极限 |
| A8 | 15/30 ns | 400 MSPS | 6/12 | 12.5 ns | 41.67% | 3.83/7.65 | 最小Width、保留较长off-time |

这些组合都能精确落在当前代码格点上，目的是减少参数回写对测试的干扰。

### 8.2 矩阵B：短Period和高占空比

先验证连续档Period边界：

| 顺序 | SGStudio Width/Period | DUT Fs | DUT点数 W/P | 代码理想50% Width | 代码理想50% Duty | 目的 |
| :---: | :---: | ---: | :---: | ---: | ---: | :--- |
| B1 | 30/50 ns | 200 MSPS | 6/10 | 25 ns | 50.00% | 缩短连续档Period |
| B2 | 30/40 ns | 200 MSPS | 6/8 | 25 ns | 62.50% | 连续档最小Period |

再验证400 MSPS档Period边界：

| 顺序 | SGStudio Width/Period | DUT Fs | DUT点数 W/P | 代码理想50% Width | 代码理想50% Duty | 目的 |
| :---: | :---: | ---: | :---: | ---: | ---: | :--- |
| B3 | 15/30 ns | 400 MSPS | 6/12 | 12.5 ns | 41.67% | 最小Width基准 |
| B4 | 15/25 ns | 400 MSPS | 6/10 | 12.5 ns | 50.00% | 缩短off-time |
| B5 | 15/20 ns | 400 MSPS | 6/8 | 12.5 ns | 62.50% | 当前代码极限组合 |

### 8.3 每一行的固定操作

每个组合严格执行同一循环：

1. SGStudio关闭 `Enabled`。
2. 从大参数改到小参数时，先改 Width，再改 Period。
3. 完成输入后重新读回 Width/Period，记录是否发生回写。
4. 等待波形生成完成。
5. 打开 `Enabled`。
6. N9040B保持同一组RF前端、255 MHz宽带和255 MSa/s分析设置。
7. `Continuous` 观察至少3次采集。
8. 检查每次 Pulse Table 是否都有至少10个有效脉冲。
9. 执行一次 `Single`。
10. 保存时域截图、Pulse Table截图和表格数据。
11. 记录Top、Base、Top/Base、Width、PRI、Duty、Rise/Fall、Droop、Overshoot、Ripple。
12. 本行结论写成 `Pass / Degraded / Not Detected / Recheck`。
13. 当前行稳定后才进入下一行。

若某一行失败，不要跳过后继续缩小。先按第11节排查并保存失败现场。

## 9. 怎样判读Pulse Table

### 9.1 Pulse

表示检测到的第几个有效脉冲。

判断：

- 2 us采集内应稳定得到至少10行。
- Pulse编号跳变、数量忽多忽少，优先检查 Detection、Min Width、Ignore Dropouts和Trigger。

### 9.2 Mod

本轮预期为 `CW`。它表示pulse-on期间没有额外脉内调制。

### 9.3 Top Level

最接近pulse-on平台功率，应与SGStudio Level/PEP或长脉冲基线比较。

建议分级：

```text
相对1 us/2 us Top差值 <= 1 dB：理想
1~3 dB：记录为Degraded，继续观察趋势
> 3 dB：进入功率/带宽/链路排查
```

这只是调试分级，不是产品最终规格。

不要用右侧 Reference Value、Base Level或全周期平均功率代替Top Level。

### 9.4 Base Level和Top/Base

- Base Level是pulse-off底电平，包含泄漏、宽带噪声和仪器底噪。
- `Top/Base = Top - Base`。

短Period时off-time非常短，Base统计会变得不稳定。对15/20 ns，若Top/Base显著收缩，Pulse Detection可能先于波形本身失效。

### 9.5 Width

必须在：

```text
Amplitude Domain = Voltage
Pulse Width Threshold = 50%
```

的统一口径下与本文“代码理想50% Width”列比较。

当前代码预期Width比UI Width短一个DUT采样间隔。不要用UI Width直接判Fail。

### 9.6 PRI

PRI是相邻脉冲同一50%上升阈值点之间的间隔，应直接接近SGStudio Period。它是判断周期功能是否正常的主指标。

建议纳秒级工程窗口：

```text
|PRI_measured - Period_UI| <= max(4 ns, Period_UI * 5%)
```

若多个脉冲被合并，可能出现PRI为2倍、3倍目标值。

### 9.7 Duty Cycle

N9040B按：

```text
Duty = measured Width / measured PRI
```

计算，所以本轮不应期待所有“UI 50% Duty”用例都测得50%。应与矩阵中的“代码理想50% Duty”比较。

### 9.8 Rise/Fall、Droop、Overshoot、Ripple

- Rise/Fall默认按10%~90%阈值。
- Droop看pulse-on平台的下降或上扬。
- Overshoot看边沿后的过冲。
- Ripple看平台纹波。

当Width只剩3~6个N9040B分析样点时，这些值对滤波、插值、阈值和Top/Base方法高度敏感。对A7、A8、B4、B5：

- Width和PRI仍是主判断。
- Rise/Fall、Droop、Overshoot、Ripple作为趋势记录。
- 不用单次极端值直接判算法失败。

## 10. 分阶段通过标准

### 10.1 严格回归：48/96 ns

必须同时满足：

- 参数不回写。
- 连续检测至少10个脉冲。
- PRI在建议窗口。
- Width接近42.67 ns代码预期。
- 无过载。
- Top相对长脉冲基线的差值已记录且可重复。

### 10.2 连续档边界：30/40 ns

必须同时满足：

- 参数保留30/40 ns。
- Pulse Table稳定。
- PRI接近40 ns。
- Width接近25 ns。
- 没有周期翻倍或脉冲合并。

### 10.3 400 MSPS切换点：25/50 ns

这是第一条明确需要400 MSPS的阶梯。必须确认：

- 参数保留25/50 ns。
- 输出没有在跨档时消失。
- Pulse Table仍稳定。
- PRI接近50 ns，Width接近22.5 ns。

若A4正常、A5失败，应优先检查400 MSPS Playback能力和下发链路，而不是先调整N9040B。

### 10.4 极限组合：15/20 ns

判为“功能探索通过”需要：

- SGStudio保留15/20 ns。
- 输出连续存在。
- N9040B在相同设置下能重复检测出Pulse Table。
- PRI围绕20 ns且不系统性翻倍。
- Width围绕12.5 ns呈现合理趋势。
- 改动Detection Threshold后，结论可重复。

由于255 MSa/s下20 ns周期仅约5.1个分析样点，这一组合不宜直接给出严苛的绝对精度Pass/Fail。若要做最终规格定标，应增加更高时间分辨率的交叉测量手段。

## 11. 常见问题和排查顺序

### 11.1 15/20 ns被SGStudio回写到48/96 ns

优先检查：

1. 设备是否实际返回 `OPTION_BW_320M_TX`。
2. 当前SGStudio是否为包含选件能力改造的版本。
3. 设备open时选件查询是否失败。
4. 是否切换到了另一台无选件设备。

这不是N9040B问题。

### 11.2 48/96 ns被回写成50/95 ns

优先检查SGStudio版本。旧实现对纳秒参数使用过宽的分数容差，可能漏掉精确格点；当前代码已把容差收紧。

### 11.3 Pulse Table完全没有脉冲

按以下顺序：

1. 检查Center Frequency和DUT RF是否已打开。
2. 检查N9040B是否过载。
3. 检查时域轨迹里是否肉眼可见脉冲。
4. 关闭Min/Max Pulse Width过滤。
5. 设置 `Ignore Dropouts = 0`。
6. 设置 `Hysteresis = 0`。
7. 使用 `Reference To Top, Threshold = -6 dB`。
8. 高占空比时改试 `-4 dB`、`-3 dB`。
9. 从IF Magnitude Trigger退回Free Run。
10. 回到上一条已通过组合，确认输出链路没有中断。

### 11.4 Pulse数变少，PRI变成2倍或3倍

这是典型的脉冲合并或漏检：

- `Ignore Dropouts` 不为0。
- Detection Threshold太低，短off-time没有跌破阈值。
- Hysteresis太大。
- Sample Rate/IFBW没有保持在宽带档。
- Top/Base不足。

优先把 Threshold从-6 dB向-4/-3 dB移动，并保持Hysteresis和Ignore Dropouts为0。

### 11.5 PRI正确，但Width比UI值短

先与本文代码预期比较：

```text
Width_50% ≈ (Nw - 1) / Fs
```

例如：

- 48/96 ns含选件档：约42.67 ns。
- 30/40 ns：约25 ns。
- 15/20 ns：约12.5 ns。

若接近这些值，说明当前半幅边沿语义被正确测到，不是周期功能失败。

### 11.6 Top Level比PEP低

先比较长脉冲基线：

- 长脉冲也低：优先检查外部衰减、线损、幅度修正和RF功率定义。
- 长脉冲正常，越窄越低：优先检查DUT宽带响应、上升/下降时间、N9040B宽带路径和短平台统计。
- 只有跨入25/50 ns后突降：同时检查400 MSPS Playback切换。

N9040B的内部Input Attenuation通常已在校准读数中补偿，不能直接拿“内部衰减10 dB”解释Top低了若干dB。

### 11.7 15/20 ns时Width、Base、Ripple乱跳

先判断是“检测极限”还是“DUT不稳定”：

1. 回到15/30 ns。
2. 若15/30稳定、15/20不稳，重点检查短off-time和Pulse Detection阈值。
3. 保持Free Run、Hysteresis 0、Ignore Dropouts 0。
4. 比较-6/-4/-3 dB三档Detection Threshold。
5. 每档至少做3次Single。
6. 若PRI仍稳定在20 ns附近，但Base/Ripple变化大，先标记为分析分辨率受限，不要立即判DUT失败。

## 12. 建议记录模板

### 12.1 固定配置

```text
Date/Operator:
SGStudio commit/build:
Device model/SN/FW:
OPTION_BW_320M_TX confirmed:
RF Frequency:
SGStudio Level/PEP:
External attenuator:
Cable/adapter loss correction:
N9040B firmware:
Pulse application version:
Input Attenuation:
Preamp:
Reference Level:
IF/Analysis Bandwidth:
Pulse Analysis Sample Rate:
Trigger:
Detection Reference/Threshold:
Hysteresis:
Ignore Dropouts:
Min/Max Pulse Width:
Amplitude Domain:
Pulse Width Threshold:
Top/Base Method:
Acquisition Length:
Maximum Pulse Count:
```

### 12.2 每个组合

| Case | UI W/P | 回写后W/P | 有效Pulse数 | Top | Base | Top/Base | Width | PRI | Duty | Rise/Fall | Droop | Overshoot | Ripple | 结论 |
| :--- | :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :--- | ---: | ---: | ---: | :--- |
| A1 | 48/96 ns |  |  |  |  |  |  |  |  |  |  |  |  |  |
| A2 | 40/80 ns |  |  |  |  |  |  |  |  |  |  |  |  |  |
| A3 | 35/70 ns |  |  |  |  |  |  |  |  |  |  |  |  |  |
| A4 | 30/60 ns |  |  |  |  |  |  |  |  |  |  |  |  |  |
| A5 | 25/50 ns |  |  |  |  |  |  |  |  |  |  |  |  |  |
| A6 | 20/40 ns |  |  |  |  |  |  |  |  |  |  |  |  |  |
| A7 | 17.5/35 ns |  |  |  |  |  |  |  |  |  |  |  |  |  |
| A8 | 15/30 ns |  |  |  |  |  |  |  |  |  |  |  |  |  |
| B1 | 30/50 ns |  |  |  |  |  |  |  |  |  |  |  |  |  |
| B2 | 30/40 ns |  |  |  |  |  |  |  |  |  |  |  |  |  |
| B3 | 15/30 ns |  |  |  |  |  |  |  |  |  |  |  |  |  |
| B4 | 15/25 ns |  |  |  |  |  |  |  |  |  |  |  |  |  |
| B5 | 15/20 ns |  |  |  |  |  |  |  |  |  |  |  |  |  |

每个失败用例至少保留：

- 一张完整时域轨迹。
- 一张放大到2~4周期的轨迹。
- 一张Pulse Table。
- 一张Pulse Detection设置页。
- 一张Time/Sample Rate设置页。

## 13. 推荐执行顺序

现场按以下顺序最容易定位问题：

1. 在Pulse Detection中完成1 us/2 us长脉冲基线。
2. 严格复测48/96 ns。
3. 跑矩阵A到30/60 ns，确认连续200 MSPS档。
4. 单独跑30/50和30/40 ns，确认连续档最小Period。
5. 跑25/50 ns，确认首次400 MSPS切换。
6. 继续20/40、17.5/35、15/30 ns。
7. 最后跑15/25和15/20 ns。
8. 任何一步失败，回到上一条已通过组合做A/B对照。

这样可以把故障快速分成：

```text
参数/选件能力问题
连续200 MSPS边界问题
400 MSPS切换问题
DUT短脉冲模拟带宽问题
N9040B Pulse Detection设置或时间分辨率问题
```

## 14. Keysight官方参考

- [N9067C Pulse Analysis X-Series Measurement App Technical Overview](https://www.keysight.com/us/en/assets/7018-05140/technical-overviews/5992-1384.pdf)
- [New Pulse Signal Processing and Analysis Techniques](https://www.keysight.com/zz/en/assets/7018-04840/application-notes/5992-0782.pdf)
- [N9040B Option H1G Supplemental Guide（含255 MHz B2X IF路径说明）](https://www.keysight.com/gb/en/assets/9018-04397/reference-guides/9018-04397.pdf)
- [N9040B UXA Signal Analyzer Specifications Guide](https://www.keysight.com/us/en/assets/9018-04944/technical-specifications/9018-04944.pdf)
- [Keysight Pulse Detection设置说明](https://helpfiles.keysight.com/csg/89600B/Webhelp/Subsystems/pulse/content/dlg_meassetup_pulseproperties_pulsedetection_tab.htm)
- [Keysight Pulse Time / Sample Rate设置说明](https://helpfiles.keysight.com/csg/89600B/Webhelp/Subsystems/pulse/content/dlg_meassetup_pulseproperties_time_tab.htm)
- [Keysight Pulse Regions / 50% Width说明](https://helpfiles.keysight.com/csg/89600B/Webhelp/Subsystems/pulse/content/dlg_meassetup_pulseproperties_regions_tab.htm)
- [Keysight Pulse Table说明](https://helpfiles.keysight.com/csg/89600B/Webhelp/Subsystems/pulse/content/trc_pulse_table.htm)

Keysight 的应用说明指出 N9067C 和89600 Pulse应用使用相同分析算法；后四个在线帮助链接用于解释检测、阈值和表格算法。N9040B多点触控界面的菜单层级可能略有不同，应按同名功能项操作。
