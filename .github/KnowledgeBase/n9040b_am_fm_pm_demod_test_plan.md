# Keysight N9040B AM/FM/PM 解调测试方案

## 1. 目的和范围

本文档用于指导使用者借助 Keysight N9040B 的 AM/FM/PM demod 功能，验证 SGStudio 当前 AM、FM、PM 射频输出是否符合设计语义。

本文档面向当前仓库中的本地波形定义，参考：

- [AM 复基带自研实现与参考行为对齐说明](am_baseband_self_implementation_alignment.md)
- [FM 复基带自研实现与参考行为对齐说明](fm_baseband_self_implementation_alignment.md)
- [PM 复基带波形生成算法方案](../TaskLog/2026-06-18_pm_baseband_generation_algorithm_design.md)

本文档的目标不是替代 Keysight 原厂手册，而是给出一套对 SGStudio 当前语义有效、可落地执行的台架测试流程。由于 N9040B 不同固件或 license 下菜单命名可能略有差异，文中使用“Analog Demod / Demodulation / Mod Type / Result Table / Time Trace”等通用称呼；若你的机器界面字样略有不同，按同义入口执行即可。

---

## 2. 当前被测定义

### 2.1 AM

当前 AM 的业务语义是复基带包络：

$$
x(t) = A \cdot (1 + m \cdot shape(t)), \quad Q(t) = 0
$$

对 9040B 而言，AM 的主观测量应放在：

- 调制率是否正确。
- 调制度是否正确。
- 包络波形方向是否正确。

### 2.2 FM

当前 FM 的业务语义是恒包络 IQ，瞬时频偏积分成相位：

$$
f_{offset}(t) = Deviation \cdot shape(t)
$$

因此对 9040B 而言，FM 的主观测量应放在：

- 调制率是否正确。
- 峰值频偏是否正确。
- 瞬时频率轨迹形状是否正确。

### 2.3 PM

当前 PM 的业务语义是恒包络 IQ，调制源直接控制相位偏移：

$$
\phi(t) = \phi_0 + \beta \cdot shape(t)
$$

其中：

- `PhaseDeviation` 在 UI 中建议用 degree。
- 算法内部使用 rad。
- `\beta = PhaseDeviationRad`。

对 9040B 而言，PM 的主观测量应放在：

- 调制率是否正确。
- 峰值相位偏移是否正确。
- 相位轨迹形状是否正确。

注意：连续播放场景下，N9040B 更适合验证 PM 的摆幅和形状，不适合作为 `InitPhase` 的主验收工具。若没有严格的相位参考和可重复触发，绝对初相常常会被仪器的载波参考消除或漂移。

---

## 3. 测试设备和连接

### 3.1 设备

- SGStudio 测试版本一份，记录 commit、构建时间、插件版本。
- 被测发射设备一台，能稳定输出 CW/AM/FM/PM。
- Keysight N9040B 一台，确认可进入 AM/FM/PM demod 测量界面。
- 10 dB 或 20 dB 固定衰减器。
- 射频线缆、转接头。
- 若条件允许，准备 10 MHz 参考时钟连接线。

### 3.2 推荐连接

```text
DUT RF OUT -> 10 dB 或 20 dB 外部衰减器 -> N9040B RF INPUT
```

建议：

- DUT 输出先设低功率，使 N9040B 输入端初始落在 `-30 dBm` 到 `-20 dBm`。
- N9040B 输入阻抗使用 `50 ohm`。
- 初始关闭 preamp。
- 若 DUT 与 N9040B 都支持 10 MHz reference，优先同源锁定。

---

## 4. N9040B 通用准备

### 4.1 仪器准备

1. N9040B 预热至少 30 分钟。
2. 执行或确认 instrument alignment。
3. `Preset`。
4. 先在普通 Spectrum 模式下找到 DUT 载波，再切 demod 模式，不要一上来就在 demod 页面盲调。

### 4.2 初始 RF 设置

建议所有用例先共用同一组射频基础参数，降低横向对比难度：

```text
RF Frequency = 1 GHz
RF Level     = 让 9040B 输入端约 -30 dBm ~ -20 dBm
```

9040B 初始建议：

- Center Frequency = DUT RF 频点。
- Reference Level 让屏顶保留至少 `5 dB ~ 10 dB` 余量。
- Input Attenuation 先设 `20 dB`。
- Preamp `Off`。

确认不过载后，再逐步把衰减降到 `10 dB` 甚至更低，以提高小偏移、小调制度场景的读数稳定性。

### 4.3 进入 demod 功能

典型操作思路：

1. 进入 `Measure`、`Mode` 或 `Application` 中的 analog demod 相关界面。
2. 选择解调类型为 `AM`、`FM` 或 `PM`。
3. 打开结果表和主 time trace。
4. 若仪器支持同时显示 RF spectrum、demod spectrum 或 AF spectrum，可作为辅助，但主验收仍以 demod time trace 和数值结果表为准。

---

## 5. 通用判读规则

### 5.1 先看 Rate，再看幅度，再看 Shape

建议固定按以下顺序判读：

1. 调制率是否正确。
2. 调制度、频偏或相位偏移幅度是否正确。
3. 波形形状和方向是否正确。

这样做的原因是：若 rate 都错了，后面的 depth/deviation/phase deviation 再接近也没有意义。

### 5.2 尽量使用 Peak 口径，不要把 RMS 当成目标值

对 SGStudio 当前文档语义，优先用 peak 类结果与目标参数对比：

- AM：看 `AM Depth` 或等价的调制度主读数。
- FM：优先看 `Peak Deviation`、`Peak+`、`Peak-`。
- PM：优先看 `Peak Phase Deviation`、`Peak+`、`Peak-`。

如果你误把 RMS 结果当作目标值，会出现系统性误判。例如：

```text
FM 正弦, Rate=1 kHz, Deviation=1 kHz
Peak 偏移应接近 1 kHz
RMS 偏移约为 0.707 kHz

PM 正弦, Rate=1 kHz, PhaseDeviation=57.3 deg
Peak 摆幅应接近 57.3 deg
RMS 摆幅约为 40.5 deg
```

### 5.3 Square / Triangle / Ramp 的自动统计只作辅证

对非正弦波形，很多仪器的自动数值统计更偏向“给出一个代表量”，不一定等同于你算法定义里的峰值平台或理想斜坡。

因此：

- `Sine`：数值结果和 time trace 都可作为主证据。
- `Square / Triangle / Ramp`：time trace + marker/cursor 才是主证据，结果表只是辅证。

### 5.4 Demod 带宽要足够宽

带宽太窄时，最常见症状是：

- 数值偏小。
- 方波边沿被圆滑。
- Ramp / Triangle 斜率被压扁。
- FM / PM 峰值被低估。

第一版联调可用下面的保守口径起步：

#### AM

- `Sine`：demod 带宽至少大于 `2 * Rate`。
- `Square / Triangle / Ramp`：若要看清形状，建议从 `10 * Rate` 起步，再逐步放宽直到时域形状不再明显变化。

#### FM

可从下面的经验值起步：

$$
B_{demod} > 2 \cdot (Rate + Deviation)
$$

#### PM

先换算等效峰值频偏：

$$
\beta = PhaseDeviationRad
$$

$$
\Delta f_{equiv} = \beta \cdot Rate
$$

再用保守口径起步：

$$
B_{demod} > 2 \cdot (Rate + \Delta f_{equiv})
$$

例：

```text
PM Sine
Rate = 1 kHz
PhaseDeviation = 57.3 deg = 1 rad

beta = 1
equivalentDeviation = 1 kHz
demod 带宽可先按大于 4 kHz 起步，再结合仪器档位上调
```

### 5.5 单次采集时间至少覆盖 5 到 10 个调制周期

建议：

- `Rate = 1 kHz` 时，time trace 先看 `10 ms ~ 20 ms`。
- `Rate = 10 kHz` 时，time trace 先看 `1 ms ~ 5 ms`。
- `Rate = 1 MHz` 时，time trace 先看 `10 us ~ 100 us`，前提是仪器带宽足够。

---

## 6. 标准测试流程

### 6.1 CW 基线

先关闭调制，输出 CW，确认：

- 能稳定锁到载波。
- N9040B 没有前端过载。
- 参考电平和输入衰减设置合理。
- 频率与功率读数稳定。

如果这一步不稳，后面所有 demod 结果都不可信。

### 6.2 先做一组低速正弦用例

每种调制先从 `1 kHz` 左右的正弦调制开始。原因很简单：

- 易于锁定。
- 易于判读。
- 一旦默认用例都不对，就没有必要先追高频极限。

### 6.3 切到对应 demod 类型

- 测 AM 时，解调类型选 `AM`。
- 测 FM 时，解调类型选 `FM`。
- 测 PM 时，解调类型选 `PM`。

不要用错解调类型去验证算法。比如用 FM demod 去验证 PM 摆幅，只能得到它的相位导数，结论会偏题。

### 6.4 打开 Time Trace 和 Result Table

推荐页面上至少保留：

- 一条 demod 主时域轨迹。
- 一组数值结果表。

如果界面允许，再加：

- RF spectrum 作为辅证。
- demod spectrum 或 AF spectrum，用来快速确认主调制率。

### 6.5 保存证据

每个关键用例至少保存：

- 一张 demod time trace 截图。
- 一张结果表截图。
- 一条简短记录：RF 频点、输入电平、衰减、preamp、demod 带宽、time span。

---

## 7. AM 测试用例

### 7.1 AM-A1：默认正弦调制度用例

SGStudio 参数：

```text
Rate  = 1 kHz
Depth = 50 %
Shape = Sine
```

9040B 主要看：

- Mod Rate 约为 `1 kHz`。
- AM Depth 约为 `50 %`。
- 包络 time trace 为平滑正弦。

建议初始判据：

- Rate：接近 `1 kHz`。
- Depth：`50 % ± 3 %`。
- Shape：正弦平滑，无明显削顶、压缩或偏斜。

### 7.2 AM-A2：Square 方向验证

SGStudio 参数：

```text
Rate  = 1 kHz
Depth = 50 %
Shape = Square
```

9040B 主要看：

- 前半周期高包络，后半周期低包络。
- 高低电平差对应约 `50 %` 调制度。
- 边沿应清晰，但不要求完全理想垂直。

这条用例的重点不是结果表里的某一个数字，而是确认“先高后低”的方向没有反。

### 7.3 AM-A3：Ramp 方向验证

SGStudio 参数：

```text
Rate  = 1 kHz
Depth = 50 %
Shape = Ramp
```

9040B 主要看：

- 包络从低值线性上升到高值。
- 到周期边界后快速回跳到低值。

若看到的是先高后低的斜坡，说明 Ramp 方向与当前文档定义不一致。

### 7.4 AM-A4：较高调制度压力用例

SGStudio 参数：

```text
Rate  = 1 MHz
Depth = 90 %
Shape = Triangle
```

这条用例用于确认：

- 高 rate 时 demod 仍可稳定锁定。
- 高 depth 时不会因为链路压缩导致结果异常偏小。

如果这条用例的 demod 波形明显依赖带宽设置，优先放宽 demod 带宽，而不是立刻怀疑算法。

---

## 8. FM 测试用例

### 8.1 FM-F1：默认正弦频偏用例

SGStudio 参数：

```text
Rate      = 1 kHz
Deviation = 1 kHz
Shape     = Sine
```

9040B 主要看：

- Mod Rate 约为 `1 kHz`。
- Peak Deviation 约为 `1 kHz`。
- 瞬时频率轨迹围绕 0 对称摆动，为正弦形状。

建议初始判据：

- Rate：接近 `1 kHz`。
- Peak Deviation：`1 kHz ± 5 %`。
- 若只看到 RMS 偏移接近 `0.707 kHz`，先检查你读到的是不是 RMS 口径。

### 8.2 FM-F2：Square 平台验证

SGStudio 参数：

```text
Rate      = 1 kHz
Deviation = 10 kHz
Shape     = Square
```

9040B 主要看：

- 前半周期频率平台接近 `+10 kHz`。
- 后半周期频率平台接近 `-10 kHz`。
- 半周期边界有清晰跳变。

这条用例比 `Deviation = 1 kHz` 更容易看清平台，因此更适合验证 shape 方向。

### 8.3 FM-F3：Ramp 轨迹验证

SGStudio 参数：

```text
Rate      = 1 kHz
Deviation = 10 kHz
Shape     = Ramp
```

9040B 主要看：

- 瞬时频率从约 `-10 kHz` 线性上升到约 `+10 kHz`。
- 周期边界回跳到约 `-10 kHz`。

若看到相反方向，说明当前 FM Ramp 方向与设计文档不一致。

### 8.4 FM-F4：较高速率用例

SGStudio 参数：

```text
Rate      = 100 kHz
Deviation = 100 kHz
Shape     = Sine
```

这条用例用于确认：

- 9040B 的 demod 带宽和 time span 已经调整到能覆盖较高 rate。
- 结果不会因为带宽太窄而被系统性低估。

若波形或数值随着 demod 带宽变化明显改变，优先认为是仪器设置问题，不要先下算法结论。

---

## 9. PM 测试用例

### 9.1 PM-P1：默认正弦相位摆幅用例

SGStudio 参数：

```text
Rate            = 1 kHz
PhaseDeviation  = 57.3 deg
InitPhase       = 0 deg
Shape           = Sine
```

对应关系：

```text
57.3 deg ~= 1 rad
```

9040B 主要看：

- Mod Rate 约为 `1 kHz`。
- Peak Phase Deviation 约为 `57.3 deg`。
- 相位轨迹为平滑正弦。

建议初始判据：

- Rate：接近 `1 kHz`。
- Peak Phase Deviation：`57.3 deg ± 5 %`。
- 若只看到约 `40.5 deg` 的结果，先确认仪器是否显示的是 RMS 口径。

### 9.2 PM-P2：Square 两平台验证

SGStudio 参数：

```text
Rate            = 1 kHz
PhaseDeviation  = 90 deg
InitPhase       = 0 deg
Shape           = Square
```

9040B 主要看：

- 前半周期相位平台接近 `+90 deg`。
- 后半周期相位平台接近 `-90 deg`。
- 半周期边界存在清晰相位跃迁。

### 9.3 PM-P3：Ramp 方向验证

SGStudio 参数：

```text
Rate            = 1 kHz
PhaseDeviation  = 60 deg
InitPhase       = 0 deg
Shape           = Ramp
```

9040B 主要看：

- 相位从约 `-60 deg` 线性上升到约 `+60 deg`。
- 周期边界回跳。

### 9.4 PM-P4：InitPhase 辅助检查

SGStudio 参数：

```text
Rate            = 1 kHz
PhaseDeviation  = 0 deg
InitPhase       = 30 deg
Shape           = Sine
```

这条用例只作为辅助检查，不作为主验收项。

理想上，若仪器保留绝对相位参考，你会看到接近常值的 `30 deg`。但在很多连续播放和自动参考场景下，仪器会把这类常值相位消掉、重置或漂移。因此：

- 如果看不到稳定的 `30 deg`，不能单独据此判定 PM 算法有误。
- `InitPhase` 更适合在有稳定触发和相位参考的离线 IQ 或单次捕获场景下验证。

---

## 10. 常见误判与排查

### 10.1 Rate 对，幅度偏小

常见原因：

- demod 带宽太窄。
- 读的是 RMS，不是 Peak。
- 9040B 输入端接近压缩，结果表被拉偏。

优先处理：

1. 放宽 demod 带宽。
2. 切换或确认结果口径。
3. 提高输入衰减或降低 DUT 功率。

### 10.2 Shape 看起来被磨平

常见原因：

- 带宽太窄。
- time span 太长，导致显示压缩。
- 结果表在做较重平均。

优先处理：

1. 缩短 time span。
2. 提高 demod 带宽。
3. 优先看原始 time trace，不要先看平均统计数。

### 10.3 AM 深度不对，且正负不对称

常见原因：

- DUT 输出功率过高，分析仪前端有轻微压缩。
- 带宽或耦合设置不合适。

优先处理：

1. 回到 CW 基线重新确认不过载。
2. 增加外部衰减或仪器输入衰减。
3. 重新检查参考电平余量。

### 10.4 PM 的 InitPhase 不稳定

这通常不是第一优先级 bug。更常见的原因是：

- 仪器使用自己的载波相位作为参考。
- 连续播放下没有统一的绝对起点。

因此 PM 现场联调优先看：

- `Rate`
- `PhaseDeviation`
- `Shape`

不要把 `InitPhase` 作为 9040B 连续解调的第一验收指标。

---

## 11. 建议记录模板

每个用例至少记录：

- DUT 软件版本、插件版本、设备型号。
- RF Frequency、RF Level。
- 9040B 的 input attenuation、preamp、reference source。
- demod 类型、demod 带宽、time span。
- 数值读数：Rate、Depth 或 Peak Deviation / Peak Phase Deviation。
- 一张 time trace 截图。
- 结论：Pass / Fail / Need Recheck。

---

## 12. 当前建议结论

如果你要用 N9040B 最快确认自己做的 AM/FM/PM 是否正确，最稳妥的顺序是：

1. 先用 CW 基线排除载波与输入链路问题。
2. 再跑 `1 kHz` 正弦默认用例，确认 rate 和主幅度量都对。
3. 再跑 Square 或 Ramp 用例，用 time trace 确认 shape 方向。
4. 最后再上高 rate 用例，并把“数值变化是否由带宽设置引起”与“算法本身错误”分开判断。

对当前 SGStudio 语义：

- AM 重点看包络深度和方向。
- FM 重点看瞬时频率峰值和轨迹。
- PM 重点看相位摆幅和轨迹，不要把 `InitPhase` 当作连续 demod 的主验收项。