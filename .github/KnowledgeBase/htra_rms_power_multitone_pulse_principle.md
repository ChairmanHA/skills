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
