# AM/FM 参数迁移后的 VSG25 台架测试方案

## 1. 目标和范围

本方案用于在 AM/FM 参数迁移和本地波形生成完成后，使用一台 VSG25 作为射频输出源，并接入普通频谱仪进行台架验证。

测试目标分为三层：

1. 确认软件参数到发射机播放链路的映射正确：`Rate / Depth / Deviation / Shape` 生效，采样率和波形长度符合预期。
2. 确认射频输出的调制行为正确：AM 的包络和边带正确，FM 的瞬时频偏和频谱展开正确。
3. 确认长时间播放稳定：无明显中断、循环拼接毛刺、频谱跳变、异常宽带杂散或功率突变。

本方案默认使用前序设计文档中的 VSG60 兼容策略作为算法一致性验收口径：

```text
AM:
  FsMax = 50 MS/s
  MaxSamplesPerPeriod = 16384
  MinSamplesPerPeriod = 4

FM:
  FsMax = 50 MS/s
  PreferredSamplesPerPeriod = 3000
  MinSamplesPerPeriod = 4
  MinTotalComplexSamples = 1024
```

如果产品最终启用 `Tx125Quality` 或其他 125 MS/s 优先策略，需要额外执行第 8 节的采样率策略对比测试。不要把“匹配 VSG60 CSV”和“充分利用 VSG25 125 MS/s 能力”混成同一个验收标准。

## 2. 测试设备和连接

### 2.1 设备

- SGStudio 测试版本一份，记录 commit、构建时间、插件版本。
- VSG25 一台，记录序列号、固件版本、连接方式。
- 频谱仪一台，支持：
  - `SWP`：扫频频谱模式。
  - `IQS`：IQ 捕获或矢量信号分析模式。
  - `RTA`：实时频谱或瀑布图模式。
- 射频线缆、20 dB 或 30 dB 固定衰减器。
- 如频谱仪支持外参考，建议准备 10 MHz 参考时钟线。

### 2.2 推荐连接

```text
VSG25 RF OUT -> 固定衰减器 -> 频谱仪 RF IN
```

建议：

- VSG25 输出电平先设为 `-30 dBm ~ -20 dBm`，确认频谱仪不压缩后再调整。
- 频谱仪输入阻抗设为 `50 ohm`。
- 频谱仪前置放大默认关闭。
- 频谱仪参考电平至少留 `10 dB` 余量。AM `Depth=100%` 时峰值包络会比载波包络高约 `6 dB`。
- 如果 VSG25 和频谱仪都支持 10 MHz 外参考，优先锁到同一参考源。若不能共参考，频率绝对偏差不作为主要失败项，优先看边带间隔、调制深度和相对频偏。

### 2.3 推荐射频基础设置

选择一个远离设备频段边缘、频谱仪性能稳定的载频，例如：

```text
RF Frequency = 1 GHz
RF Level     = -25 dBm
Output Mode  = Continuous playback
```

如果频谱仪或线缆在 1 GHz 性能不理想，可以改为 `100 MHz` 或其他中频点。所有测试应使用同一个载频，便于对比。

## 3. 仪表模式分工

### 3.1 SWP 模式

SWP 用于稳态频谱验证，重点看：

- 载波是否在设定频率。
- AM/FM 边带间隔是否等于 `Rate`。
- AM 正弦边带幅度是否符合 `Depth`。
- FM 正弦谱线是否符合调频指数 `beta = Deviation / Rate` 的大致 Bessel 分布。
- 左右边带是否基本对称。
- 是否存在异常杂散、镜像、宽带抬升。

推荐设置：

```text
Center = RF Frequency
Span   = 按用例设置，至少覆盖主要边带
RBW    = Rate / 10 或更小；1 kHz Rate 建议 RBW <= 100 Hz
VBW    = RBW 或更小
Detector = RMS 或 Positive Peak
Trace    = Clear Write + Average 10 次；检查杂散时再用 Max Hold
```

### 3.2 IQS 模式

IQS 是最关键的算法验证模式。SWP 只能看到频谱幅度，很多相位和时序错误在 SWP 上不可见。

IQS 用于：

- AM：恢复包络 `env(t)=sqrt(I^2+Q^2)`，测 `Depth`、`Rate`、形状方向。
- FM：恢复相位 `phase(t)=unwrap(atan2(Q,I))`，再求瞬时频率 `f_inst(t)=diff(phase)/(2*pi)*Fs_iqs`，测 `Deviation`、`Rate`、形状方向。
- 检查 Square 首半周期方向、Ramp 上升或下降方向。
- 检查 FM 是否错误实现成直接相位调制，或相位积分顺序不一致。

推荐设置：

```text
Center     = RF Frequency
IQ BW      = 大于被测调制主要带宽的 2 倍
Capture    = 至少 10 个调制周期
1 kHz Rate = 建议捕获 20 ms ~ 50 ms
10 MHz Rate = 建议捕获 10 us ~ 100 us，前提是 IQ 带宽足够
```

如果频谱仪 IQ 带宽不足以覆盖 `Rate=10 MHz` 的用例，则高 Rate 用例以 SWP/RTA 为主，IQS 只验证低 Rate 用例。

### 3.3 RTA 模式

RTA 用于稳定性和瞬态问题，重点看：

- 播放循环边界是否有毛刺。
- 长时间输出是否有中断或功率突变。
- Square/Ramp 边沿是否引入异常宽带脉冲。
- 高 Rate 用例是否有间歇性频谱塌陷或跳变。

推荐设置：

```text
Span       = 覆盖 Carson 带宽或 AM 主要谐波
Persistence / Waterfall = 开启
Run Time   = 每个关键用例至少 30 s，最终回归建议 60 s
```

## 4. 基线测试

在 AM/FM 之前先跑基线，避免把线缆、仪表压缩或参考时钟问题误判为算法问题。

### 4.1 CW 基线

设置 VSG25 输出未调制 CW：

```text
RF Frequency = 1 GHz
RF Level     = -25 dBm
```

验收：

- 频谱仪载波频率接近设定值。
- 频谱仪读数无压缩迹象。调整输出电平时，频谱仪读数应等比例变化。
- 噪声底、杂散水平记录为本轮测试基线。
- 若共参考，载波频率误差应主要受仪表分辨率限制。

### 4.2 播放链路基线

使用一个简单的低风险调制用例：

```text
AM, Rate = 1 kHz, Depth = 50%, Shape = Sine
```

先确认软件可以完成：

- 参数应用。
- 波形生成。
- 下载或播放。
- RF 输出打开。
- 频谱仪能看到稳定载波和对称边带。

## 5. AM 测试矩阵

### 5.1 AM-A1：正弦 AM 默认参考用例

参数：

```text
Rate  = 1 kHz
Depth = 50%
Shape = Sine
```

预期波形策略：

```text
N_period = 16384
Fs       = 16.384 MS/s
I[n]     = 16384 * (1 + 0.5 * sin(2*pi*n/N))
Q[n]     = 0
```

SWP 预期：

- 载波位于 `Fc`。
- 主边带位于 `Fc +/- 1 kHz`。
- 正弦 AM 单音边带相对载波：

```text
Sideband / Carrier = Depth / 2 = 0.25
Level = 20*log10(0.25) = -12.04 dBc
```

验收建议：

- 边带间隔误差：小于 `Rate * 0.1%`，或受 RBW 限制时小于 `1 RBW`。
- 左右边带幅度差：小于 `1 dB`，普通台架可放宽到 `2 dB`。
- 主边带电平：`-12.04 dBc +/- 1.5 dB`。

IQS 预期：

- 包络最大值和最小值比例约为 `3:1`。
- 调制度：

```text
m = (env_max - env_min) / (env_max + env_min) ~= 0.5
```

- 包络从均值开始向上变化，和 VSG60 `amDefault.csv` 的 Sine 起点一致。

### 5.2 AM-A2：Square 方向和谐波验证

参数：

```text
Rate  = 1 kHz
Depth = 50%
Shape = Square
```

预期：

```text
N_period = 16384
Fs       = 16.384 MS/s
前半周期高包络，后半周期低包络
```

SWP 预期：

Square AM 产生奇次谐波边带。`Depth=50%` 时，主要边带相对载波近似为：

```text
k = 1: 20*log10(2*m/(pi*1)) = -9.94 dBc
k = 3: 20*log10(2*m/(pi*3)) = -19.49 dBc
k = 5: 20*log10(2*m/(pi*5)) = -23.92 dBc
```

IQS 重点：

- 包络前半周期为高电平，后半周期为低电平。
- 深度仍为约 `50%`。

注意：只看 SWP 无法判断 Square 是先高后低还是先低后高，方向必须用 IQS 或时域包络确认。

### 5.3 AM-A3：Ramp 方向验证

参数：

```text
Rate  = 1 kHz
Depth = 50%
Shape = Ramp
```

预期：

```text
N_period = 16384
Fs       = 16.384 MS/s
包络从最小值线性上升到接近最大值，然后在周期边界跳回最小值
```

SWP 预期：

Ramp 会产生所有整数倍谐波边带。`Depth=50%` 时，主要边带相对载波近似为：

```text
k = 1: 20*log10(m/(pi*1)) = -15.96 dBc
k = 2: 20*log10(m/(pi*2)) = -21.98 dBc
k = 3: 20*log10(m/(pi*3)) = -25.50 dBc
```

IQS 重点：

- 必须是上升锯齿，不是下降锯齿。
- 周期边界允许包络跳变，这正是 Ramp 定义。

### 5.4 AM-A4：Triangle 高 Rate 和高 Depth 用例

参数：

```text
Rate  = 1 MHz
Depth = 90%
Shape = Triangle
```

预期波形策略：

```text
N_period = 32
Fs       = 32 MS/s
包络从最小值上升到最大值，再下降到最小值
```

SWP 预期：

Triangle 主要为奇次谐波。`Depth=90%` 时，主要边带相对载波近似为：

```text
k = 1: 20*log10(4*m/(pi^2*1^2)) = -8.76 dBc
k = 3: 20*log10(4*m/(pi^2*3^2)) = -27.84 dBc
```

验收重点：

- `Fc +/- 1 MHz` 边带明显且对称。
- `Fc +/- 3 MHz` 边带存在但明显低于一阶边带。
- IQS 包络深度约 `90%`，最大/最小包络比约为 `19:1`。

### 5.5 AM-A5：10 MHz 高 Rate 收口用例

参数：

```text
Rate  = 10 MHz
Depth = 50%
Shape = Sine
```

VSG60 兼容策略预期：

```text
N_period = 4
Fs       = 40 MS/s
单周期包络点 = [mean, max, mean, min]
```

SWP 预期：

- `Fc +/- 10 MHz` 主边带约 `-12.04 dBc`。
- 频谱仪 Span 建议至少 `30 MHz`。
- 如果频谱仪有足够动态范围，观察是否有额外镜像或采样相关杂散。

IQS 说明：

- 只有当频谱仪 IQ 带宽足够覆盖 10 MHz 调制时，才用 IQS 验证时域。
- 如果 IQ 带宽不足，以 SWP 边带和 RTA 稳定性为主。

## 6. FM 测试矩阵

### 6.1 FM-F1：正弦 FM 默认参考用例

参数：

```text
Rate      = 1 kHz
Deviation = 1 kHz
Shape     = Sine
```

预期波形策略：

```text
N_period = 3000
Fs       = 3 MS/s
beta     = Deviation / Rate = 1
```

SWP 预期：

- 谱线间隔为 `1 kHz`。
- 正弦 FM 谱线幅度符合 Bessel 分布。
- 相对未调制载波，主要系数约为：

```text
J0(1) = 0.765, 约 -2.32 dB
J1(1) = 0.440, 约 -7.13 dB
J2(1) = 0.115, 约 -18.79 dB
```

如果以调制后的中心谱线为 `0 dBc`，则：

```text
J1/J0 ~= -4.80 dBc
J2/J0 ~= -16.46 dBc
```

IQS 预期：

- 解调瞬时频偏为正弦。
- 峰值频偏约 `+/- 1 kHz`。
- 频偏从 `0` 开始，先向正频偏方向变化。
- RF 包络基本恒定，FM 不应出现 AM 型包络变化。

### 6.2 FM-F2：Square 瞬时频偏方向验证

参数：

```text
Rate      = 1 kHz
Deviation = 1 kHz
Shape     = Square
```

预期：

```text
N_period = 3000
Fs       = 3 MS/s
前半周期 f_inst = +1 kHz
后半周期 f_inst = -1 kHz
```

SWP 预期：

- 以 `1 kHz` 为间隔的多谱线。
- 左右频谱整体应对称。

IQS 重点：

- 瞬时频偏必须先为 `+Deviation`，再为 `-Deviation`。
- 相位前半周期线性上升，后半周期线性下降。
- 一个周期结束时相位应回到起点附近，循环播放不应有相位跳变毛刺。

### 6.3 FM-F3：Ramp 瞬时频偏方向验证

参数：

```text
Rate      = 1 kHz
Deviation = 1 kHz
Shape     = Ramp
```

预期：

```text
N_period = 3000
Fs       = 3 MS/s
f_inst 从 -1 kHz 线性上升到接近 +1 kHz
```

IQS 重点：

- 必须是上升 Ramp，不是下降 Ramp。
- 不对 Ramp 做离散均值校正，保持和 VSG60 `FMRamp.csv` 一致。
- RF 包络仍应基本恒定。

RTA 重点：

- 周期边界处允许瞬时频偏从接近 `+Deviation` 跳回 `-Deviation`。
- 该跳变不应引起异常宽带脉冲或播放中断。

### 6.4 FM-F4：大调频指数用例

参数：

```text
Rate      = 1 kHz
Deviation = 10 kHz
Shape     = Sine
```

预期：

```text
N_period = 3000
Fs       = 3 MS/s
beta     = 10
Carson BW ~= 2 * (Deviation + Rate) = 22 kHz
```

SWP 设置：

```text
Span = 50 kHz 或 100 kHz
RBW  <= 100 Hz，便于分辨 1 kHz 间隔谱线
```

验收重点：

- 频谱展开宽度约落在 Carson 带宽量级。
- 主要谱线间隔仍为 `1 kHz`。
- IQS 解调频偏峰值约 `+/- 10 kHz`。
- FM 包络恒定，无明显 AM 调制。

### 6.5 FM-F5：10 MHz 高 Rate 收口用例

参数：

```text
Rate      = 10 MHz
Deviation = 1 MHz
Shape     = Sine
```

VSG60 兼容策略预期：

```text
N_period = 4
Fs       = 40 MS/s
N_total  = 1024
beta     = 0.1
```

SWP 预期：

- 主边带位于 `Fc +/- 10 MHz`。
- 小调频指数下：

```text
J1(0.1)/J0(0.1) ~= -26.0 dBc
```

- Span 建议至少 `40 MHz`。
- 重点检查 `Fc +/- 10 MHz` 边带是否存在且对称，以及是否有异常镜像。

IQS 说明：

- 只有频谱仪 IQ 带宽足够时才做时域频偏解调。
- 如果 IQ 带宽不足，则以 SWP 边带和 RTA 长时间稳定性为准。

## 7. 验收指标

### 7.1 通用指标

建议把验收分为两档：

- `严格算法档`：离线 CSV/保存 IQ 对比，允许误差在数个 LSB 以内。
- `台架射频档`：考虑 VSG25、线缆、频谱仪、RBW、参考时钟和模拟链路误差，使用较宽容的 RF 指标。

本方案主要定义台架射频档。

通用台架指标：

```text
输出稳定性：关键用例 RTA 连续观察 60 s，无明显掉波、重启、跳频、突发宽带异常
边带间隔：误差 < Rate * 0.1%，或 < 1 RBW
左右边带对称性：主边带差值 < 1 dB，普通台架可放宽到 2 dB
异常杂散：相对主载波或主边带没有新增的大幅异常尖峰；具体门限需结合设备本底记录
```

### 7.2 AM 指标

```text
Sine 50%:
  SWP 主边带 = -12.04 dBc +/- 1.5 dB
  IQS Depth = 50% +/- 2% abs

Triangle 90%:
  IQS Depth = 90% +/- 3% abs
  主边带量级符合理论，允许高次谐波受模拟带宽影响

Square / Ramp:
  SWP 谐波结构符合预期
  IQS 方向必须正确
```

AM 深度计算：

```text
Depth = (env_max - env_min) / (env_max + env_min)
```

建议对 `env_max/env_min` 使用滤波后峰值，或使用 95/5 分位值，避免瞬态尖峰影响结果。

### 7.3 FM 指标

```text
Sine 1 kHz / 1 kHz:
  IQS Deviation = 1 kHz +/- 2% 或 +/- 100 Hz，取较大者
  SWP beta=1 的主要谱线量级符合 Bessel 分布，允许 +/- 1.5 dB

Sine 1 kHz / 10 kHz:
  IQS Deviation = 10 kHz +/- 2%
  Carson 带宽量级正确，谱线间隔为 1 kHz

Square / Ramp:
  IQS 瞬时频偏方向必须正确
  RF 包络不应出现明显 AM 深度

10 MHz / 1 MHz:
  SWP 主边带位于 Fc +/- 10 MHz
  主边带约 -26 dBc，普通台架可放宽到 +/- 3 dB
```

FM 瞬时频偏计算：

```text
phase[n] = unwrap(atan2(Q[n], I[n]))
f_inst[n] = (phase[n] - phase[n-1]) * Fs_iqs / (2*pi)
```

测 Deviation 时建议先去除频率均值：

```text
f_offset[n] = f_inst[n] - mean(f_inst)
Deviation ~= max(abs(f_offset))
```

对 Sine 用例，也可以用：

```text
Deviation ~= sqrt(2) * rms(f_offset)
```

## 8. 采样率策略专项测试

如果迁移后仍保留多种采样率策略，建议显式测试两类策略。

### 8.1 VSG60Exact 策略

目标：匹配已导出的 VSG60 CSV 行为。

应确认：

```text
AM 1 kHz Sine:
  N = 16384, Fs = 16.384 MS/s

AM 1 MHz Triangle:
  N = 32, Fs = 32 MS/s

AM 10 MHz Sine:
  N = 4, Fs = 40 MS/s

FM 1 kHz Sine:
  N = 3000, Fs = 3 MS/s

FM 10 MHz Sine:
  N_period = 4, Fs = 40 MS/s, N_total = 1024
```

如果 UI 不显示采样率和点数，建议通过日志、保存 IQ 文件长度，或 debug 信息记录。

### 8.2 Tx125Quality 策略

目标：利用 VSG25 或本发射机 `125 MS/s` 上限改善高 Rate 下的时间分辨率。

应确认：

- 输出不再要求逐点匹配 VSG60 CSV。
- RF 行为仍然满足调制参数：AM Depth 正确，FM Deviation 正确，Rate 正确。
- 高 Rate 用例边带和杂散不劣化。
- 波形下载大小、播放稳定性、切换耗时可以接受。

建议至少对比：

```text
AM, Rate=10 MHz, Depth=50%, Shape=Sine
FM, Rate=10 MHz, Deviation=1 MHz, Shape=Sine
```

对比项：

- 主边带电平。
- 镜像和杂散。
- RTA 长时间稳定性。
- 参数切换耗时。

## 9. 推荐执行顺序

1. 记录软件版本、VSG25 固件、频谱仪型号、参考时钟状态。
2. 连接 VSG25 到频谱仪，串接固定衰减器。
3. 执行 CW 基线，确认没有压缩和频率异常。
4. 执行 AM-A1，用 SWP 确认基础边带，用 IQS 确认 Depth。
5. 执行 FM-F1，用 SWP 确认 Bessel 谱线，用 IQS 确认 Deviation。
6. 执行 AM-A2/AM-A3 和 FM-F2/FM-F3，重点用 IQS 确认 Square/Ramp 方向。
7. 执行 AM-A4/FM-F4，验证较高 Depth 和较大 Deviation。
8. 执行 AM-A5/FM-F5，验证高 Rate 收口和宽 Span 频谱。
9. 对 AM-A1、FM-F1、AM-A5、FM-F5 做 RTA 60 s 稳定性观察。
10. 若存在 VSG60Exact/Tx125Quality 两种策略，执行第 8 节专项对比。


## 10. 常见失败特征和定位方向

### 10.1 AM

- 正弦 AM 边带不是 `-12 dBc` 左右：优先检查 `Depth` 是否按百分比转成 `m=Depth/100`，以及 AM 基准幅度是否使用 `16384`。
- IQS 深度正确但 SWP 边带异常：检查频谱仪 RBW、输入压缩、VSG25 输出链路带宽和外部衰减。
- Square/Ramp 的 SWP 看起来正确但时域方向反了：SWP 幅度谱无法判断方向，必须以 IQS 包络为准。
- AM 输出没有明显载波或载波被抑制：可能误做成 DSB-SC，或基带包络没有 `1 + m*shape` 的直流项。
- AM 出现 Q 路显著变化：可能错误生成了复数旋转载波，而不是纯 I 路包络。

### 10.2 FM

- Sine FM 频谱大致像 FM，但 IQS 相位差 90 度：可能把 FM 写成 `exp(j*beta*sin(...))` 的直接相位调制，而不是先生成瞬时频偏再积分。
- Deviation 测量偏差明显：检查 `phaseStep = 2*pi*Deviation/Fs`，以及 Fs 是否使用了实际波形采样率。
- Square FM 不是先正频偏再负频偏：检查 Square shape 定义是否和 VSG60 CSV 一致。
- Ramp FM 方向反了或做了均值校正：检查是否错误采用了下降 Ramp 或额外去均值。
- FM 包络出现明显周期性起伏：检查 IQ 幅度归一化和播放链路是否引入 AM。
- 高 Rate 用例不稳定：检查 `N_period`、`N_total`、下载长度和循环播放边界。

