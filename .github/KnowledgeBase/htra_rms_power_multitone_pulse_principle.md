# HTRA RMS 功率原理：Multitone 与 Pulse

本文用于回答一个具体问题：当前 HTRA `multitone` 和 `pulse` 的 RMS 功率显示，从原理上是否正确。

结论先行：

- `pulse` 目前已经把满幅脉冲样点调整为 `(32767, 32767)`，与共享 RMS 参考口径一致。因此占空比 `100%` 时 `RMS = PEP`，占空比 `50%` 时 `RMS = PEP - 3.01 dB`，这是正确结果。
- `multitone` 目前仍按 `max(|I|, |Q|)` 做分量峰值归一化，典型 `Count = 2` 等相位结果仍可能峰值落在 `(32767, 0)`，没有对齐到共享 RMS 参考中的 `(32767, 32767)` 满幅 PEP。
- 因此 `multitone` 的 `Count = 2, Level/PEP = 0 dBm, RMS = -6 dBm` 不是 RMS 公式错误，而是“共享满幅 PEP 参考”和“multitone 当前生成满幅口径”不一致后的正确测量结果。

## 共享 RMS 参考口径

当前 RMS helper 直接扫描最终生成的 int16 交织 IQ：

```text
power[n] = I[n]^2 + Q[n]^2
avgPower = mean(power[n])
rmsOffsetFromFullScalePepDb = 10 * log10(avgPower / fullScalePeakPower)
displayedRmsDbm = LevelDbm + rmsOffsetFromFullScalePepDb
```

共享满幅 PEP 参考是：

```text
fullScalePeakPower = 32767^2 + 32767^2
```

这表示当前 Core 显示链路把 `Level` 解释为“最终 IQ 复包络峰值达到 `(32767, 32767)` 时对应的 RF PEP”。这是当前仓库的产品约定，不是通用 RF 定义。

这里必须分清两个量：

```text
PAPR = peakPower / avgPower
RMS offset = avgPower / fullScalePeakPower
```

所以 RMS 不能普遍写成：

```text
RMS = Level - PAPR
```

这个简化只有在 `peakPower == fullScalePeakPower` 时才成立。如果某个 provider 生成的最终 IQ 峰值低于共享满幅 PEP，那么 RMS 显示必须同时包含“峰值低了多少”和“峰均比是多少”两部分。

等价地说，当前 helper 回答的是：

```text
最终 IQ payload 的平均功率，相对共享满幅 PEP 参考是多少？
```

它不是在回答：

```text
如果每个 provider 都用自己的本地满幅 PEP 定义，RMS 应该是多少？
```

## ARB / QuickWaveform / IQS Streaming 的复包络 AutoScale

2026-09-16 用户确认：设备 Level 的 PEP 参考确实对应角点 `(32767,32767)`。ARB OrdinaryWav/IqsWav 与 QuickWaveform 的共享文件 AutoScale 已统一到复包络半径 32767，与 IQS Streaming 现有归一化定义一致。

```text
gain = 32767 / sqrt(max(rawI² + rawQ²))
ideal peakOffset = 10log10(32767² / (2 * 32767²)) = -3.0103 dB
actual PEP = Level + peakOffsetFromFullScalePepDb
RMS = Level + rmsOffsetFromFullScalePepDb
```

- 原始峰值扫描包含 -32768，不使用 RMS helper 的分量钳位；两个 -32768 的平方和为 2^31，用 uint32 保存。
- 非零波形在 AutoScale 后理想 PEP 为 Level - 3.0103 dB；最终 int16 截断引入微小偏差，实际指标以量化样点统计为准，不能硬编码显示偏移。
- RMS 的角点参考、Pulse、period 补零分母不变；无需添加 3 dB 补偿。
- 手动 IQScale 不保证固定峰值。IQS Streaming 还会叠加 IQScale，100% 且峰值元数据有效时才适用上述归一化参考；普通 WAV Streaming 没有这条 IQS AutoScale。
- ARB/QuickWaveform 仍扫描实际所选切片；本次未新增峰值元数据加速，未改变 ProgrammedArb。

## Pulse

当前 HTRA pulse 的幅度定义是：

```cpp
kPulseOff  = {0, 0}
kPulseHalf = {16384, 16384}
kPulseFull = {32767, 32767}
```

也就是说，满幅脉冲样点本身就是：

```text
I = 32767
Q = 32767
```

因此 active pulse 的瞬时功率正好等于共享满幅 PEP：

```text
activePower = 32767^2 + 32767^2 = fullScalePeakPower
```

Pulse generator 生成的是完整 period，off 区域填零，并且 worker 对最终 `iqData` 调用共享 RMS helper。所以 off 区域天然参与平均功率计算。

对理想矩形脉冲，占空比为 `D` 时：

```text
avgPower = D * fullScalePeakPower
rmsOffset = 10 * log10(D)
```

因此：

```text
D = 1.0  -> RMS = Level + 10log10(1.0) = Level
D = 0.5  -> RMS = Level + 10log10(0.5) = Level - 3.01 dB
```

当 `Level/PEP = 0 dBm`：

```text
100% duty -> RMS = 0 dBm
50% duty  -> RMS = -3.01 dBm
```

这与你的实测一致，说明 pulse 的生成口径和 RMS 参考口径已经对齐。

一个细节：当前 pulse 对很窄的宽度会使用 half-level 边沿样点。此时精确 RMS 不一定严格等于理想 `10log10(duty)`，而是以最终 IQ 样点的实际平均功率为准。这仍然是正确的，因为 RMS 显示要反映实际下载波形，而不是理想化矩形公式。

Pulse 判断：

- 满幅 active 样点达到共享 `(32767, 32767)` PEP。
- off 区域属于完整 period，必须参与 RMS 平均。
- `100% duty -> RMS = PEP` 正确。
- `50% duty -> RMS = PEP - 3 dB` 正确。

## Multitone

当前 HTRA multitone 仍按分量峰值做 AutoScale：

```text
scale = 32767 / max(max(|I|), max(|Q|))
```

这个口径保证 I 或 Q 的最大分量吃满 int16 动态范围，但不保证复包络峰值达到：

```text
32767^2 + 32767^2
```

以 `Count = 2`、等相位、未对 multitone 做 `(32767, 32767)` 满幅调整的场景为例。无论它表现为默认对称双音，还是当前 HTRA 临时中心遮盖路径下的 `DC + 单边 tone`，只要两个 tone 等幅等相位，未缩放波形都有下面这个关键特征：

```text
componentPeak = 2
mean(|x|^2) = 2
```

当前归一化把 `componentPeak = 2` 缩放到 `32767`，因此缩放系数是：

```text
scale = 32767 / 2
```

缩放后的平均功率是：

```text
avgPower = mean(|scale * x|^2)
         = (32767 / 2)^2 * 2
         = 32767^2 / 2
```

但共享满幅 PEP 参考仍然是：

```text
fullScalePeakPower = 2 * 32767^2
```

所以 RMS offset 为：

```text
avgPower / fullScalePeakPower
  = (32767^2 / 2) / (2 * 32767^2)
  = 1/4

10 * log10(1/4) = -6.02 dB
```

当 `Level/PEP = 0 dBm` 时：

```text
RMS = 0 dBm - 6.02 dB = -6.02 dBm
```

这就是你之前看到 `RMS = -6 dBm` 的原因。

再看峰值本身。当前 `Count = 2` 等相位场景缩放后，典型峰值仍然只是：

```text
peakPower = 32767^2
```

相对共享满幅 PEP：

```text
peakPower / fullScalePeakPower
  = 32767^2 / (2 * 32767^2)
  = 1/2
  = -3.01 dB
```

也就是说，在共享 RMS 参考下，当前 multitone 的实际 PEP 本身就比 `Level` 低约 `3 dB`；双音自身的 PAPR 又是约 `3 dB`。两者叠加后，RMS 就比 `Level` 低约 `6 dB`：

```text
RMS offset = peak offset - PAPR
           = -3.01 dB - 3.01 dB
           = -6.02 dB
```

如果把 multitone 的本地满幅 PEP 定义为 `(32767, 0)`，同一个最终 IQ 会得到另一个解释：

```text
localFullScalePeakPower = 32767^2
avgPower / localFullScalePeakPower = 1/2
RMS = local PEP - 3.01 dB
```

这个 `-3 dB` 并不和当前显示的 `-6 dB` 矛盾。它们只是参考口径不同：

- `-6 dB`：相对共享 `(32767, 32767)` 满幅 PEP。
- `-3 dB`：相对 multitone 本地 `(32767, 0)` 满幅 PEP。

当前 Core RMS helper 使用的是前者，因此当前显示 `-6 dBm` 是按共享口径得到的正确计算结果。

Multitone 判断：

- RMS helper 对最终 IQ 的平均功率计算是正确的。
- `Count = 2, Level = 0 dBm, RMS = -6 dBm` 在共享 `(32767, 32767)` 参考下是正确的。
- 但 multitone generator 当前仍未把自己的满幅 PEP 生成口径调整到 `(32767, 32767)`。
- 如果产品要求 multitone 的 `Level` 也严格表示共享满幅 PEP，则应调整 multitone 的生成/归一化策略，或明确引入 provider-specific reference；不应通过修改共享 RMS 公式来掩盖这个差异。

## 对照表

| 场景 | 最终 IQ 峰值口径 | 平均功率相对共享满幅 PEP | `Level = 0 dBm` 时 RMS | 判断 |
| :--- | :--- | :--- | :--- | :--- |
| Pulse, 100% duty | `(32767, 32767)` | `1.0` | `0 dBm` | 正确，且口径已对齐 |
| Pulse, 50% duty | active 半周期为 `(32767, 32767)`，off 半周期为 `0` | `0.5` | `-3.01 dBm` | 正确，且口径已对齐 |
| Multitone, `Count = 2`, 等相位 | 典型为 `(32767, 0)` | `0.25` | `-6.02 dBm` | RMS 计算正确；生成满幅口径未对齐共享 PEP |

## 实用结论

当前 RMS 计算公式本身是正确的，因为它直接基于最终 IQ 样点计算：

```text
RMS dBm = Level dBm + 10 * log10(avgPower / fullScalePeakPower)
```

对于 pulse：

- generator 已经把满幅 active 样点调整为 `(32767, 32767)`。
- 因此 RMS 显示与 PEP / duty-cycle 理论一致。

对于 multitone：

- generator 当前仍是分量峰值 AutoScale。
- 它的某些波形实际峰值只到 `(32767, 0)`，相对共享 PEP 已经低 `3 dB`。
- 因此 `Count = 2` 下显示 `PEP - 6 dB` 是正确计算，不是 RMS 公式错误。

后续如果要统一所有 provider 的 `Level = PEP` 语义，应优先统一 generator 的满幅 PEP 口径。共享 RMS helper 应继续保留现在这种基于最终 IQ 平均功率和统一 `fullScalePeakPower` 的计算方式。

## 2026-11-03 补充：Pulse 的行业语义与控制建议

本节记录行业调研和最终只读交互。2026-11-03 用户已将 Pulse RMS 改为与 Streaming 一样不可编辑，保留平均功率数值；此前方案 A 的一次性输入取消。业务插件仍声明 Pulse 保持 Level，其余有界 baseband 默认保持 RMS，RMS 标签/布局不变。当前合同见 [Level/RMS 设计](rms_level_async_interaction_design.md)。调研记录见 [审查 TaskLog](../TaskLog/2026-11-03_pulse-rms-pep-industry-review.md)，最终实现见 [只读 TaskLog](../TaskLog/2026-11-03_pulse-rms-readonly.md)。已完成源码修改与静态检查，尚未编译/运行验收。

### RMS 方法与平均时间窗

在纯电阻负载上，平均功率为 `mean(v²)/R = Vrms²/R`。行业界面常称为 RMS Power；它表达基于电压平方的功率平均，不能理解为对瓦特值再次计算 `sqrt(mean(P²))`。RMS 本身不规定平均时间窗。

- **整周期平均功率**：包含 ON 和 OFF，用于描述平均输出能量/时间、热负载和平均功率预算。
- **脉冲导通期间平均功率（Average ON / Pulse Power）**：仅在定义的导通区间内平均，仍可用 RMS 方法计算。导通区间、有效平台区间和门控范围需要明确。
- **顶部功率（Top Power）**：描述脉冲平台电平；有过冲、droop 或脉内幅度调制时，不必等于 ON 区间平均或最大包络功率。
- **PEP**：包络最大处的 RF 周期平均功率；不是 RF 正弦瞬时功率最大值。理想平顶无过冲脉冲的 Top / ON Power / PEP 数值相同，非理想包络不能直接等同。

官方依据：

- [Keysight V3500A User's Guide，第 104 页](https://www.keysight.com/us/en/assets/9018-02547/user-manuals/9018-02547.pdf)：由 RF 周期平均得到 `P = Vrms²/R`。
- [Keysight 89600 VSA 的 Power Averaging](https://helpfiles.keysight.com/csg/89600B/Webhelp/Subsystems/powerspectrum/content/ps_vbw.htm)：在线性功率单位中平均，再转为 dB 显示，解释 RMS 名称的来源。
- [Tektronix Fundamentals of Radar Measurements，Average ON Power / Average Transmit Power](https://www.tek.com/en/documents/primer/fundamentals-radar-measurements)：区分仅脉冲 ON 时间内的 RMS 功率与覆盖 ON+OFF 的发射平均功率。
- [ITU-R SM.1413-1，Power Measurement Method Code](https://www.itu.int/dms_pubrec/itu-r/rec/sm/R-REC-SM.1413-1-200304-S%21%21PDF-E.pdf)：区分包络峰顶 RF 周期平均的 PEP 与足够长时间内的 Mean Power。

### SGS 当前代码观察

相关源码由 `src/plugins/htra/CMakeLists.txt` 和 `src/plugins/core/CMakeLists.txt` 明确纳入，调用链为：

1. `pulsemodulator.cpp:25–27` 定义 OFF=`(0,0)`、半幅=`(16384,16384)`、满幅=`(32767,32767)`；`generateWaveform` 生成完整周期。
2. `pulsemodulator.cpp:416–418` 对最终 payload 统计；`iplaybackbusiness.cpp:14–41,108–136` 以 `F=2×32767²` 计算平均/峰值偏移与 PAPR。短 payload 重复整周期，不改变统计比例。
3. `pulsemodulation.cpp` 的 `buildPlaybackExecutionContext` 把该指标传给 Core；`TxSessionService::resolvePowerLevel` 对允许 RMS 输入的业务通过 `Level = targetRms - offset` 解析硬件电平，受硬件范围约束。Pulse 选择后保持 Level，函数内没有 Pulse 专用判断。
4. `PulseModulation::providerCapabilities` 声明 `TxPowerHoldMode::Level`；Core 根据该声明将 RMS 作为只读派生值，`publishAppliedRequest` 只为 Rms 业务建立 RMS。内部状态由 PendingLevel 更名为 Level，没有新增持久化字段。
5. `fancydevice.cpp:1333–1355` 将解析后的 Level 传入 `tx_config_ffm`，没有把 RMS 数值作为另一项独立硬件设置。

对理想平顶脉冲，忽略边沿和 RF 链路误差：

```text
Pulse Top / ON Power / PEP ≈ Level
Average over period = Level + 10log10(D)
```

现场用户观察“DET 脉冲顶部对应 Level，而非界面的 RMS”与该模型一致。这里按项目 DET 测试文档所述的功率-时间观察理解；只有完整周期积分才与整周期 RMS 对应，不能仅凭模式名确定读数。RBW、VBW、门控与读数类型仍决定测量结果。

当前 half-level 边沿已作用于最终样点，所以整周期平均应继续以 payload 统计为准，不能用理想 Duty 公式替换。双 32767 是本设备已确认的标定约定，不是 RF 行业通用数字满幅定义；保持现有 Pulse IQ 和参考分母，没有依据改为单分量满幅或额外补偿 3 dB。

### 厂商实践与适用边界

- [R&S Pulse Sequencer User Manual v25，第 125 页](https://scdn.rohde-schwarz.com/ur/pws/dl_downloads/pdm/cl_manuals/user_manual/1176_9512_01/RS_PulseSequencer_UserManual_en_25.pdf#page=125)明确把未衰减脉冲的 RF Level 设为 pulse-top power；脉冲 Top Power 衰减相对该参考定义。
- [R&S SMA100B 窄脉冲电平控制应用说明](https://www.rohde-schwarz.com/us/applications/generating-narrow-pulses-with-highest-level-accuracy-and-repeatability-with-the-rs-sma100b_56279-434176.html)用脉冲顶部与 CW 的电平偏差、顶部电平重复性描述控制性能，包括 1% 占空比场景。
- [Keysight Pulse Building：Getting Correct Output Power](https://helpfiles.keysight.com/csg/N7620B/Content/Main/Correct_Output_Power.htm)要求用 pulse modulator / ALC hold 防止在 OFF 时间进行电平控制；ALC OFF 时也使用波形 RMS 电压作为 power-search 的校准参考。这证明 RMS 统计仍有内部用途，不能从中推出面板默认保持 ON+OFF 平均功率。
- [Keysight Signal Studio LTE Uplink Output Power Calculation](https://helpfiles.keysight.com/csg/n7624/Content/Main/Output_Power_Calculation_%28Uplink%29.htm)以功率最高符号的 RMS 作为面板参考，并说明完整波形平均读数会更低。这是特定 LTE 配置的行为，不能泛化为所有 Keysight 波形，但说明即使数字调制也必须定义功率参考的时间范围。

### 当前交互：RMS 只读（替代方案 A，已静态实施）

Pulse 的整周期平均功率有物理意义，适合平均能量、热负载和与平均功率计对照；雷达/接收机脉冲电平设置更适合以导通平台功率为主。

Pulse 保持 Level（当前满幅 Pulse 对应顶部功率），更改 Width/Period/Duty 时保持顶部不变，RMS 按完整周期最终 IQ 更新。保留 RMS 标签、数值格式和布局，主窗/Minibar 的 RMS 控件禁用；统计未知时显示 --，Reset/Startup 不用 Level 冒充平均功率。

Pulse 不再接受 RMS 输入，包括此前的一次性换算：SCPI SET 沿用 SettingsConflict，Core setter 也拒绝；QUERY 可返回只读平均功率。Streaming 无统计值仍为 --；两者复用同一可编辑标志和数值投影，不增加只读状态字段。多音、ARB、文件回放等原 RMS 业务仍按原成功边界建立 RMS。

手动进入 Pulse 和配置恢复选择 Pulse 时均沿用 API Level；从 Pulse 转入 Rms 业务，以当前 Level 和目标波形成功建立 RMS。旧 Pulse 文件若含 rms 参考，采用已有 common.Level，不再执行该目标的一次性换算。旧格式/Reset 仍从 Startup 开始，Pulse 平均值仅由当前 IQ 指标派生，未知时沿用不可用合同。

例：手改 `Level=-10 dBm, Duty=50%` 后，整周期平均约为 `-13.01 dBm`。随后 Duty 改为 10%：

| 保持意图 | 脉冲顶部 | 整周期平均 |
| --- | --- | --- |
| 已采用的 Pulse 保持 Level | -10 dBm | -20 dBm |
| 旧版成功后自动保持 RMS | -3.01 dBm | -13.01 dBm |

该表是理想平顶、稳态且未触及硬件限幅时的静态推导，不是本轮新增实测。只维护现有一条 G13，覆盖占空比、主窗/Minibar 只读和 SCPI SET 拒绝/QUERY 平均值；G6～G10 原用例不改。保存恢复、RF/MOD、SCPI 调用链已静态核对，未新增其他人工用例。双 32767、半幅边沿和统计公式均不变。

其他 baseband 的判断：Digital/DSSS/OFDM、AWGN、多音、ARB/QuickWaveform 均可继续以完整波形 RMS 为主要指标；FM/PM 的理想恒包络下，平均与导通功率不随相位/频偏本身改变。AM 和带空闲尾段的 Ramp 是需要区分参考语义的候选，而非数学上不能使用 RMS：AM 若目标是固定载波，应保持 carrier power（并非直接把现有 Level 当作 carrier）；Ramp 若目标是固定 chirp 导通功率，整周期 RMS 保持会随空闲间隔变化而调整活动段电平。当前代码可确认 AM 包络含 `1+m×shape`、Ramp 在 `Period>SweepTime` 时补零；本轮不改变两者策略。AM 行业参考见 [Keysight E8267D 规格的 AM 注释](https://www.keysight.com/us/en/assets/7018-01210/data-sheets/5989-0697.pdf)；Ramp 的候选判断来自当前算法和统计区间的直接推导，不声称厂商所有 chirp 模式都采用同一保持量。
