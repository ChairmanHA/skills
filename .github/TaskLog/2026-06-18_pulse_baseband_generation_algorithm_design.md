# Pulse 复基带波形生成算法方案

## 1. 任务目标

为 `src/plugins/analog/pulsemodulator.cpp` 当前使用的第三方 `GeneratePulseWaveform(...)` 设计一套可本地实现的 Pulse（脉冲调制）复基带 IQ 生成算法。

目标参数面保持和当前 `PulseModulator` 一致：

- `Width`：脉冲高电平宽度，单位 s。
- `Period`：脉冲周期，单位 s。

目标输出保持和当前 playback 链路一致：

- 输出 `int16_t` 交织 IQ 数据：`I0, Q0, I1, Q1, ...`。
- 单个 complex sample 占 `4 Bytes`。
- Pulse 输出为实值单极性复基带包络：`Q = 0`。
- 不生成 carrier frequency；RF 搬移由设备频率、DUC 或射频链路负责。

本文档为静态分析和 `data/Pluse1ms.csv`、`data/pluse10ms.csv` 反推结果，没有编译、运行或仪器验证。

## 2. 任务范围与成功标准

范围：

- 只设计 Pulse baseband 本地生成算法。
- 不修改现有 C++ 实现。
- 不改变 UI 参数面。
- 不改变公共 playback、保存 WAV 或 provider 切换链路。

验证级别：

- `static`

成功标准：

1. 能解释 `data/Pluse1ms.csv`、`data/pluse10ms.csv` 的采样率、样点数、幅度档位和边沿形态。
2. 给出可替换 `GeneratePulseWaveform(...)` 的本地生成模型。
3. 明确 VSG60 40 MS/s 参考行为与本发射机 125 MS/s 能力之间的取舍。
4. 明确参数收口、采样率选择、边沿样点、波形长度和内存上限。

## 3. 现有代码事实

当前 Pulse 生成链路在 `PulseModulator::workerLoop()` 中完成：

```cpp
m_sampleRate = calcMaxSampleRate(params.Width, params.Period);
result.sampleRate = m_sampleRate;
int computeResult = GeneratePulseWaveform(objId, &params, result.sampleRate, &iq, &len);
```

当前默认值：

```text
Width  = 1 ms
Period = 2 ms
```

当前参数收口在 `Pulse_Configuraion(...)` 中完成：

```cpp
profileOut.Period = std::max(Analog::MIN_PULSE_PERIOD, profileIn.Period);
profileOut.Period = std::min(profileOut.Period, 1.0);

profileOut.Width = std::max(Analog::MIN_PULSE_WIDTH, profileIn.Width);
profileOut.Width = std::min(profileOut.Width, profileOut.Period);
```

实际常量在 `src/plugins/analog/packing.h` 中：

```cpp
constexpr double MIN_PULSE_WIDTH = 64e-9;
constexpr double MIN_PULSE_PERIOD = 64e-9;
```

注意：`Pulse_Configuraion(...)` 附近的注释仍提到 `25 ns` / `0.1 us` 一类历史口径；当前代码已按 `64 ns` 常量收口。本文按实际代码行为描述，后续实现时不应只看注释。

当前 `calcMaxSampleRate(width, period)` 的约束：

```text
DATA_SAMPLE_RATE_MIN = 195.3125 kS/s
DATA_SAMPLE_RATE_MAX = 125 MS/s

Fs * Width  为整数
Fs * Period 为整数
Fs * Period 为 8 的整数倍
```

并在合法采样率范围内选最大值；若估算 payload 超过 `Analog::MAXDOWNLOADSIZE`，再按 size 上限搜索更低合法采样率。

当前 `src/plugins/analog/CMakeLists.txt` 已包含：

```text
pulsemodulation.cpp
pulsemodulation.h
pulsemodulator.cpp
pulsemodulator.h
pulsepanel.cpp
pulsepanel.h
pulsepanel.ui
```

因此 Pulse 本地生成 helper 可直接落在 analog 插件当前目标内。

第三方头文件只暴露调用接口和参数结构；仓库中没有 `GeneratePulseWaveform(...)` 的实现源码。因此本文对 VSG60 / 第三方内部行为的描述来自当前代码、参数、调制理论和 CSV 反推。

## 4. 参考 CSV 结论

参考文件：

```text
data/Pluse1ms.csv
data/pluse10ms.csv
```

### 4.1 `data/Pluse1ms.csv`

用户给出的 VSG60 导出参数：

```text
Width  = 1.000 ms
Period = 2.000 ms
```

CSV 观察：

- 行数：`80000`。
- 每行是一个 complex sample 的 `I,Q`。
- `Q` 全部为 `0`。
- `I` 只有三个取值：`0 / 16384 / 32767`。
- `I = 0` 的样点数：`40000`。
- `I = 16384` 的样点数：`2`。
- `I = 32767` 的样点数：`39998`。
- 样点变化只发生在 4 个位置：

```text
n = 1:     0     -> 16384
n = 2:     16384 -> 32767
n = 40000: 32767 -> 16384
n = 40001: 16384 -> 0
```

前 10 个样点：

```text
n=0: 0,0
n=1: 16384,0
n=2: 32767,0
n=3: 32767,0
...
```

宽度边界附近：

```text
n=39999: 32767,0
n=40000: 16384,0
n=40001: 0,0
```

采样率反推：

```text
N_period = 80000
Period   = 2 ms
Fs       = N_period / Period
         = 80000 / 0.002
         = 40 MS/s
```

脉宽样点反推：

```text
N_width = Width * Fs
        = 0.001 * 40e6
        = 40000 samples
```

结论：

1. VSG60 Pulse 是实值单极性复基带包络，不是 passband RF 脉冲。
2. `Q` 路恒为 `0`。
3. 高电平满幅接近 `32767`。
4. 关断电平为 `0`，不是 `-32768`。
5. 上升沿和下降沿各有一个半幅样点 `16384`。
6. CSV 的 `40 MS/s` 与 VSG60 最高约 `50 MS/s` 的设备能力相容；它不是当前 SGStudio `calcMaxSampleRate(...)` 在本发射机 `125 MS/s` 上限下会自然选出的值。

### 4.2 `data/pluse10ms.csv`

用户给出的 VSG60 导出参数：

```text
Width  = 10.000 ms
Period = 10.000 ms
```

CSV 观察：

- 行数：`400000`。
- 每行是一个 complex sample 的 `I,Q`。
- `Q` 全部为 `0`。
- `I` 全部为 `32767`。
- 没有 `0`、`16384` 或其他中间幅度样点。
- 全文件没有任何样点变化点。

采样率反推：

```text
N_period = 400000
Period   = 10 ms
Fs       = N_period / Period
         = 400000 / 0.010
         = 40 MS/s
```

结论：

1. `Width == Period` 时，VSG60 输出全周期常开满幅包络。
2. 此场景不保留上升沿半幅样点，也不在周期末尾插入下降沿半幅样点。
3. `Width == Period` 的正确本地退化行为应为全样点 `I=32767, Q=0`。
4. 该 CSV 与 `Pluse1ms.csv` 一样反推出 `40 MS/s`，说明 VSG60 在当前两个 ms 级 Pulse 样本中使用同一采样率。

## 5. Pulse 的正确业务语义

Pulse 在当前发射机 playback 链路中应理解为“基带幅度门控包络”：

```text
x[n] = a[n] + j*0
```

其中：

```text
a[n] ∈ [0, 32767]
```

它不负责生成 RF carrier：

```text
错误模型：x(t) = pulse(t) * cos(2*pi*fc*t)
正确模型：x(t) = pulse(t) + j*0
```

RF 频率由设备频率设置和后端上变频链路负责。Pulse generator 只负责生成一个周期性的基带开关包络。

与 AM 的区别：

- AM 的默认包络围绕中值变化，`I` 通常不会全关到 `0`。
- Pulse 是门控型包络，高电平为满幅，低电平为 `0`。
- Pulse 没有 `Depth` 和 `Shape`；当前参数面只有 `Width / Period`。

与 FM / PM 的区别：

- FM / PM 是恒包络相位类 IQ。
- Pulse 是幅度类 IQ。
- Pulse 不需要相位累加器。

## 6. 数学模型

设：

```text
Fs = sampleRate
N_period = round(Fs * Period)
N_width  = round(Fs * Width)
A_full   = 32767
A_half   = 16384
```

基本矩形模型：

```text
I[n] = A_full, 0 <= n < N_width
I[n] = 0,      N_width <= n < N_period
Q[n] = 0
```

但 `Pluse1ms.csv` 说明 VSG60 不是直接硬跳变，而是在两个边界上写入半幅样点。为了逐点匹配该 CSV，推荐把第一版兼容模型定义为：

```text
I[0] = 0

I[n] = A_half,  n == 1
I[n] = A_full,  2 <= n < N_width
I[n] = A_half,  n == N_width
I[n] = 0,       N_width < n < N_period
Q[n] = 0
```

对于默认输入：

```text
Fs = 40 MS/s
N_period = 80000
N_width  = 40000
```

得到：

```text
I[0]     = 0
I[1]     = 16384
I[2]     = 32767
...
I[39999] = 32767
I[40000] = 16384
I[40001] = 0
...
I[79999] = 0
```

与 `data/Pluse1ms.csv` 的结构一致。

## 7. 边沿模型选择

### 7.1 `Vsg60HalfSampleEdge`

这是用于对齐 `Pluse1ms.csv` 的推荐参考模型：

```text
上升沿：0, 1/2, 1
下降沿：1, 1/2, 0
```

优点：

- 能解释 CSV 中唯一的中间值 `16384`。
- 对默认 `1 ms / 2 ms / 40 MS/s` 可以逐点复现。
- 比完全硬跳变稍微降低一个采样点处的离散边沿突兀度。

限制：

- 边沿宽度以样点数定义，不是独立的 rise/fall time 参数。
- 当采样率从 `40 MS/s` 提高到 `125 MS/s` 时，边沿物理时间会随采样周期变短。
- 这不是严格的带限脉冲成形，只是 VSG60 风格的最小边沿离散化。

### 7.2 `HardRect`

硬矩形模型：

```text
I[n] = A_full, 0 <= n < N_width
I[n] = 0,      N_width <= n < N_period
Q[n] = 0
```

优点：

- 语义最直接。
- 脉宽按样点计数更容易解释。

限制：

- 不匹配 `Pluse1ms.csv`。
- 频谱带外更宽。

### 7.3 第一版建议

第一版替换第三方 Pulse 时，建议默认使用 `Vsg60HalfSampleEdge`，原因是当前唯一参考 CSV 明确显示半幅边沿。

如果后续产品希望把 Pulse 作为严格硬门控，可再增加显式策略或参数；不要在没有 UI 提示的情况下悄悄切换边沿模型。

## 8. 参数收口与边界情况

建议沿用当前 `Pulse_Configuraion(...)` 的实际收口：

```text
Period = clamp(Period, MIN_PULSE_PERIOD, 1.0)
Width  = clamp(Width,  MIN_PULSE_WIDTH,  Period)
```

当前实际常量：

```text
MIN_PULSE_WIDTH  = 64 ns
MIN_PULSE_PERIOD = 64 ns
```

生成前还应保证：

```text
N_period >= 1
1 <= N_width <= N_period
N_total = N_period
N_total * 4 <= Analog::MAXDOWNLOADSIZE
```

边沿退化规则建议：

```text
if N_width <= 0:
  全周期输出 0。

if N_width == 1:
  输出一个满幅样点或一个半幅样点需要产品选择。
  第一版建议输出一个满幅样点，保证极窄脉冲仍可见。

if N_width == 2:
  可输出 [16384, 16384] 或 [0, 32767, 16384] 的截断形式。
  第一版建议优先保持非零面积，不强求 VSG60 半边沿完整形态。

if N_period - N_width < 1:
  Width == Period 时应退化为常开包络。
  该行为已由 data/pluse10ms.csv 确认。
  此时输出全幅，避免在周期边界制造一个不属于用户参数的关断或半幅边沿样点。
```

说明：

- `Width == Period` 表示 100% duty cycle，应输出连续高电平；`data/pluse10ms.csv` 已确认 VSG60 也是全样点 `32767,0`。
- `Width < Period` 才应用上升/下降边沿。
- 对极窄脉冲，VSG60 半幅边沿模型无法完整表达，此时应优先保证“存在脉冲”和“不越界”。

## 9. 采样率策略

### 9.1 当前 SGStudio 策略

当前 `calcMaxSampleRate(width, period)` 会把 `Width / Period` 转为分数，并计算一个采样率步进，使得：

```text
Fs * Width  为整数
Fs * Period 为整数
Fs * Period 为 8 的整数倍
```

然后在 `[195.3125 kS/s, 125 MS/s]` 内选最大合法采样率。

对当前默认值：

```text
Width  = 1 ms = 1/1000 s
Period = 2 ms = 1/500 s
```

可以选择：

```text
Fs = 125 MS/s
N_width  = 125000
N_period = 250000
payload  = 250000 * 4 = 1,000,000 Bytes
```

该结果满足本发射机能力和 size 上限。

### 9.2 VSG60 参考策略

当前两份 Pulse CSV 均显示 VSG60 在 ms 级参数下使用 `40 MS/s`。

`Pluse1ms.csv`：

```text
Fs = 40 MS/s
N_width  = 40000
N_period = 80000
payload  = 80000 * 4 = 320000 Bytes
```

`pluse10ms.csv`：

```text
Fs = 40 MS/s
N_width  = N_period = 400000
payload  = 400000 * 4 = 1600000 Bytes
```

观察与推论需要分开：

- 观察：两份 CSV 均确认为 `40 MS/s`。
- 观察：`40 MS/s` 低于 VSG60 约 `50 MS/s` 上限。
- 推论：VSG60 Pulse 导出可能偏好某个内部标准采样率、采样率档位或设备友好时钟，而不是简单取 `<= 50 MS/s` 的最高合法值。

仅凭这两份 ms 级 CSV 仍不能反推出完整 VSG60 采样率选择函数。因此不要把 `40 MS/s` 写死成所有 Pulse 参数的唯一规则。

### 9.3 推荐策略分层

建议把采样率策略分成两层：参考复现与产品生成。

```text
Vsg60Reference:
  目标是复现当前 CSV。
  对 Width=1ms, Period=2ms 选择 40 MS/s。
  对 Width=10ms, Period=10ms 选择 40 MS/s。
  对其他参数优先在 <= 50 MS/s 中寻找满足整数宽度/周期/8点周期约束的采样率。
  若无法从更多 CSV 证明完整规则，不承诺逐点匹配所有 VSG60 Pulse。

Tx125Quality:
  目标是替换第三方库并利用本发射机 125 MS/s 上限。
  直接复用当前 calcMaxSampleRate(width, period) 的采样率选择。
  默认 Width=1ms, Period=2ms 时选择 125 MS/s。

CurrentCompatible:
  与 Tx125Quality 暂时相同。
  命名用于后续如果产品想改采样率策略，可以区分“沿用当前软件行为”和“质量优先新策略”。
```

第一版工程建议：

- 默认产品路径使用 `Tx125Quality / CurrentCompatible`，因为当前 SGStudio 已经把本发射机上限定义为 `125 MS/s`。
- 离线对比和回归测试提供 `Vsg60Reference`，用于复现 `data/Pluse1ms.csv` 和 `data/pluse10ms.csv`。
- 文档和日志中明确说明：VSG60 的 `40 MS/s` 是参考行为，不是本发射机必须降采样到 `40 MS/s` 的理由。

## 10. IQ 生成伪代码

### 10.1 量化 helper

```cpp
qint16 clampToInt16(long value)
{
    return static_cast<qint16>(std::clamp(value, -32768L, 32767L));
}
```

### 10.2 Half-sample edge

```cpp
QVector<qint16> generatePulseIqHalfEdge(qint64 periodSamples,
                                        qint64 widthSamples)
{
    constexpr qint16 kOff = 0;
    constexpr qint16 kHalf = 16384;
    constexpr qint16 kFull = 32767;

    QVector<qint16> iq;
    iq.resize(static_cast<int>(periodSamples * 2));

    const bool alwaysOn = widthSamples >= periodSamples;

    for (qint64 n = 0; n < periodSamples; ++n) {
        qint16 i = kOff;

        if (alwaysOn) {
            i = kFull;
        } else if (widthSamples <= 0) {
            i = kOff;
        } else if (widthSamples == 1) {
            i = (n == 0) ? kFull : kOff;
        } else if (widthSamples == 2) {
            i = (n == 0 || n == 1) ? kHalf : kOff;
        } else {
            if (n == 1 || n == widthSamples) {
                i = kHalf;
            } else if (n > 1 && n < widthSamples) {
                i = kFull;
            } else {
                i = kOff;
            }
        }

        iq[static_cast<int>(2 * n)] = i;
        iq[static_cast<int>(2 * n + 1)] = 0;
    }

    return iq;
}
```

说明：

- 该伪代码用于复现 `Pluse1ms.csv` 的默认形态。
- `Width == Period` 直接输出常开，用于匹配 `pluse10ms.csv`，避免产生非用户期望的边界关断或半幅样点。
- 极窄脉冲按退化规则处理，保证不会越界。

### 10.3 Hard rectangle

```cpp
QVector<qint16> generatePulseIqHardRect(qint64 periodSamples,
                                        qint64 widthSamples)
{
    constexpr qint16 kOff = 0;
    constexpr qint16 kFull = 32767;

    QVector<qint16> iq;
    iq.resize(static_cast<int>(periodSamples * 2));

    widthSamples = std::clamp(widthSamples, qint64(0), periodSamples);

    for (qint64 n = 0; n < periodSamples; ++n) {
        const qint16 i = (n < widthSamples) ? kFull : kOff;
        iq[static_cast<int>(2 * n)] = i;
        iq[static_cast<int>(2 * n + 1)] = 0;
    }

    return iq;
}
```


## 11. 对齐 `Pluse1ms.csv`

输入：

```text
Width  = 1 ms
Period = 2 ms
SampleRatePolicy = Vsg60Reference
EdgePolicy = Vsg60HalfSampleEdge
```

期望：

```text
sampleRate = 40 MS/s
periodSamples = 80000
widthSamples = 40000
iqData qint16 count = 160000
complex sample count = 80000
payload = 320000 Bytes
```

关键样点：

```text
n = 0:     I=0,     Q=0
n = 1:     I=16384, Q=0
n = 2:     I=32767, Q=0
n = 39999: I=32767, Q=0
n = 40000: I=16384, Q=0
n = 40001: I=0,     Q=0
n = 79999: I=0,     Q=0
```

统计结果：

```text
I=0:     40000 samples
I=16384:     2 samples
I=32767: 39998 samples
Q!=0:        0 samples
```

这与当前 CSV 完全一致。

## 12. 对齐 `pluse10ms.csv`

输入：

```text
Width  = 10 ms
Period = 10 ms
SampleRatePolicy = Vsg60Reference
EdgePolicy = Vsg60HalfSampleEdge
```

期望：

```text
sampleRate = 40 MS/s
periodSamples = 400000
widthSamples = 400000
iqData qint16 count = 800000
complex sample count = 400000
payload = 1600000 Bytes
```

关键样点：

```text
n = 0:      I=32767, Q=0
n = 1:      I=32767, Q=0
n = 199999: I=32767, Q=0
n = 200000: I=32767, Q=0
n = 399999: I=32767, Q=0
```

统计结果：

```text
I=32767: 400000 samples
Q!=0:         0 samples
变化点:       0
```

这确认 `Width == Period` 时不应生成 `0 -> 16384 -> 32767` 的上升沿，也不应生成 `32767 -> 16384 -> 0` 的下降沿；应直接退化为常开包络。

## 13. 本发射机 125 MS/s 输出示例

输入：

```text
Width  = 1 ms
Period = 2 ms
SampleRatePolicy = CurrentCompatible
EdgePolicy = Vsg60HalfSampleEdge
```

当前 `calcMaxSampleRate(...)` 预期：

```text
sampleRate = 125 MS/s
periodSamples = 250000
widthSamples = 125000
payload = 250000 * 4 = 1,000,000 Bytes
```

边沿样点：

```text
n = 0:      I=0
n = 1:      I=16384
n = 2:      I=32767
...
n = 124999: I=32767
n = 125000: I=16384
n = 125001: I=0
```

与 VSG60 的差异：

- 物理脉宽和周期相同。
- IQ 幅度语义相同。
- 边沿样点模式相同。
- 采样率更高，因此样点数更多，边沿物理时间更短。
- 不应逐点匹配 `Pluse1ms.csv`，但时域语义更符合本发射机 125 MS/s 能力。

## 14. 大波形与长周期处理

当前 `calcMaxSampleRate(...)` 已包含 size 上限搜索：

```text
size = Fs * Period * 4
size <= Analog::MAXDOWNLOADSIZE
```

特殊规则：

```text
if Period > 0.25:
    return 10 MS/s
```

这个工程特例用于避免超长周期带来极端 payload。对本地生成器应保持一致，避免替换第三方后出现同一参数生成体积突变。

需要注意：

- `Period = 1 s` 且 `Fs = 10 MS/s` 时，payload 约 `40 MB`，仍低于 `125 MB`。
- `Period = 2 ms` 默认例即便使用 `125 MS/s`，payload 也只有约 `1 MB`。
- 本地生成阶段可以直接拒绝超过 `MAXDOWNLOADSIZE` 的组合，而不要先生成超大数组再依赖公共 playback 裁剪。

## 15. 风险与未确认点

### 15.1 VSG60 采样率规则未完全反推

当前已有两份 Pulse CSV。它们共同证明：

```text
Width=1ms,  Period=2ms:  Fs = 40 MS/s
Width=10ms, Period=10ms: Fs = 40 MS/s
```

但仍不能证明所有 Pulse 参数都遵循固定 `40 MS/s` 或固定某个 `N_period` 规则。

建议补充导出：

```text
Width = 100 us, Period = 1 ms
Width = 10 us,  Period = 100 us
Width = 1 us,   Period = 10 us
Width = 25 ns,  Period = 100 ns
Width = 1 ms,   Period = 10 ms
Width = Period, 例如 1 ms / 1 ms
```

这些样本可确认：

- VSG60 是否固定偏好 `40 MS/s`。
- 是否存在采样率档位。
- `Width == Period` 在其他周期长度下是否同样常开。
- 极窄脉冲是否仍使用半幅边沿。
- 长周期是否降采样。

### 15.2 半幅边沿不是严格带限成形

`16384` 半幅样点只能说明 VSG60 做了最小离散边沿处理。它不是 Raised Cosine、Gaussian 或其他明确滤波器。

第一版不应新增复杂 rise/fall shaping，除非产品明确要控制带外谱或仪器测试显示硬边沿不可接受。

### 15.3 注释与实际常量不一致

当前代码注释和实际常量存在差异：

```text
注释：Width 25 ns / Period 0.1 us 等历史口径
实际：Width 64 ns / Period 64 ns
```

本地实现应按实际常量工作。若产品要再次调整下限，应单独修改参数约束和文档，不应混在 Pulse 算法替换里。

## 16. 验证计划

### 16.1 静态验证

检查默认参数：

```text
Width = 1 ms
Period = 2 ms
```

在 `Vsg60Reference + Vsg60HalfSampleEdge` 下：

```text
sampleRate = 40 MS/s
periodSamples = 80000
widthSamples = 40000
I 只包含 0 / 16384 / 32767
Q 全为 0
CSV 关键样点逐点一致
```

检查 `Width = Period = 10 ms`：

```text
sampleRate = 40 MS/s
periodSamples = widthSamples = 400000
I 全部为 32767
Q 全为 0
无半幅样点
无变化点
```

在 `CurrentCompatible + Vsg60HalfSampleEdge` 下：

```text
sampleRate = calcMaxSampleRate(width, period)
periodSamples = sampleRate * period
widthSamples = sampleRate * width
periodSamples 为 8 的整数倍
payload <= Analog::MAXDOWNLOADSIZE
```

### 16.2 离线对比

为 `data/Pluse1ms.csv`、`data/pluse10ms.csv` 增加一个离线对比脚本或单元测试：

```text
读取 CSV
生成本地 Vsg60Reference 波形
比较 complex sample 数
比较 sampleRate 推导值
逐点比较 I/Q
统计最大绝对误差
```

预期：

```text
maxAbsErrorI = 0
maxAbsErrorQ = 0
```

### 16.3 实机验证

替换第三方库后，使用默认参数：

```text
Width = 1 ms
Period = 2 ms
```

检查：

- 软件预览显示 50% duty pulse。
- 保存 WAV 后 header sampleRate 与生成策略一致。
- 发射机可正常下载并播放。
- 频谱或示波器观察到约 `1 ms` 高电平、`2 ms` 周期。

如使用 `CurrentCompatible`，实测应按 `125 MS/s` 生成；如使用 `Vsg60Reference`，实测应按 `40 MS/s` 生成。

## 17. 推荐落地顺序

1. 保留当前第三方 API 路径，新增本地 `generatePulseBasebandWaveform(...)` helper。
2. 先实现 `Vsg60Reference + Vsg60HalfSampleEdge`，用 `data/Pluse1ms.csv` 和 `data/pluse10ms.csv` 做逐点离线对比。
3. 实现 `CurrentCompatible`，确认默认 `1 ms / 2 ms` 使用当前 `calcMaxSampleRate(...)` 的 `125 MS/s` 行为。
4. 将 `PulseModulator::workerLoop()` 从 `GeneratePulseWaveform(...)` 切到本地 helper。
5. 保留 worker 线程、`PulseWaveformResult`、保存 WAV 和 `handleModulatorStatusChange(...)` 调用形态不变。
6. 删除第三方 Pulse API 依赖前，至少完成默认参数离线对比和一次 debug-build 验证。
7. 后续如补充更多 VSG60 CSV，再修正 `Vsg60Reference` 的泛化采样率策略。

## 18. 当前建议结论

Pulse 本地算法的核心应采用“单极性幅度门控复基带”模型：

```text
I[n] = pulseEnvelope[n]
Q[n] = 0
```

当前参考 CSV 已确认：

```text
Width=1ms, Period=2ms:
  VSG60 sampleRate = 40 MS/s
  N_period = 80000
  N_width = 40000
  edge = 0 -> 16384 -> 32767, and 32767 -> 16384 -> 0

Width=10ms, Period=10ms:
  VSG60 sampleRate = 40 MS/s
  N_period = N_width = 400000
  all samples = 32767,0
```

面向本发射机生产默认路径，建议沿用当前 `calcMaxSampleRate(width, period)`，让默认参数使用 `125 MS/s`。这样替换第三方库后不会人为丢掉本发射机采样率能力。

面向 VSG60 对齐测试，建议提供 `Vsg60Reference` 策略，至少对 `Width=1ms / Period=2ms` 固定生成 `40 MS/s`、`80000` complex samples，并使用半幅边沿模型逐点复现 `data/Pluse1ms.csv`；对 `Width=10ms / Period=10ms` 固定生成 `40 MS/s`、`400000` complex samples，并输出全程满幅以复现 `data/pluse10ms.csv`。

## 19. 2026-06-22 实施计划

Scope:

- 新增 HTRA 自主 Pulse provider，作为无证书 fallback。
- 保留 Analog Pulse 的第三方算法路径，作为有证书 provider。
- Analog 插件按 `LicenseValidationState` 在同名 `Pulse` provider 之间切换，保持 AM / FM / Ramp 已有 provider swap 模式。
- Pulse 参数名、默认值、参数收口和属性范围与当前 Analog Pulse 保持一致。

Success criteria:

1. HTRA 插件注册 `Pulse` 业务，且该业务不依赖第三方 `GeneratePulseWaveform(...)`。
2. HTRA Pulse 输出 `Q=0` 的单极性 IQ，`Width < Period` 使用 VSG60 半幅边沿，`Width == Period` 输出常开满幅。
3. HTRA Pulse 采样率选择复用当前 `calcMaxSampleRate(width, period)` 语义，默认 `1 ms / 2 ms` 在本机路径为 `125 MS/s`。
4. Analog 插件在 `Licensed` 时切换到 `Analog::PulseModulation`，在 `Unknown / Unlicensed` 时切换到 HTRA `Pulse`。
5. Pulse 不再作为单纯 license-hidden Analog business 注册；否则无证书时会把 fallback 一起隐藏。

Static verification:

- 确认新增源文件都包含在 `src/plugins/htra/CMakeLists.txt`。
- 确认 Analog 插件持有并关闭 analog pulse provider，与 AM / FM / Ramp 一致。
- 确认 HTRA 和 Analog 两侧 `Pulse_Width / Pulse_Period` 的 metadata 范围一致。
- 不主动编译，除非用户要求 build 验证。

## 20. 2026-06-22 实施记录

已完成：

1. 新增 HTRA `Pulse` provider：
   - `src/plugins/htra/pulsemodulator.{h,cpp}`
   - `src/plugins/htra/pulsemodulation.{h,cpp}`
   - `src/plugins/htra/pulsepanel.{h,cpp,ui}`
2. HTRA Pulse 算法不再依赖第三方 `GeneratePulseWaveform(...)`，按本文 `CurrentCompatible + Vsg60HalfSampleEdge` 方案生成：
   - `Q = 0`
   - `Width < Period` 时使用半幅边沿样点
   - `Width >= Period` 时生成全周期满幅常开波形
   - 采样率选择复用当前 `calcMaxSampleRate(width, period)` 语义
3. HTRA 插件注册 `Pulse` 业务，并将新增文件加入 `src/plugins/htra/CMakeLists.txt`。
4. Analog 插件新增 Pulse provider swap：
   - `Licensed`：切换到 `Analog::PulseModulation`
   - `Unknown / Unlicensed`：切换到 HTRA `Pulse`
   - 切换时保留 AM / FM 同样的 current / selected business 迁移逻辑
5. Pulse 不再作为单纯 license-hidden Analog business 注册，避免无证书时 fallback 一起被隐藏。
6. Analog 和 HTRA 两侧 `Pulse_Width / Pulse_Period` 均使用一致范围：
   - `Pulse_Width`: `64 ns` 到 `1 s`
   - `Pulse_Period`: `64 ns` 到 `1 s`
   - 归一化仍保持 `Width <= Period`

静态验证：

- `git diff --check` 未报告空白或补丁格式错误。
- `rg` 确认 HTRA Pulse 新实现不包含 `GeneratePulseWaveform` / `wrapperGenSignalWave` / `Pulse_Configuraion` 依赖。
- `rg` 确认 Analog Pulse 不再进入 `registerLicenseControlledBusiness(new Analog::PulseModulation)`。
- 按仓库规则，本轮未主动编译；需要时再执行 Debug build 验证。

## 21. 2026-06-22 最小脉宽/周期工程建议

观察：

- 调整前硬下限为 `Width = 8 ns`、`Period = 16 ns`。
- 在 `125 MS/s` 下，采样间隔为：

```text
Ts = 1 / 125e6 = 8 ns
```

因此极限参数实际只有：

```text
Width  = 1 sample
Period = 2 samples
```

这类波形在离散数组中可以构造，但不应视为日常可用的 RF Pulse 输出。`[32767, 0, 32767, 0, ...]` 这类逐点翻转序列已经把主要能量推到接近 Nyquist 的位置，实际 DAC 重构滤波、模拟带宽、插值链路、功放/ALC 和输出匹配都会显著改变其峰值和包络形态。实测出现峰均比基本失效，符合 DSP 与 RF 输出链路预期。

DSP 理由：

1. `8 ns` 脉宽的矩形脉冲主瓣第一零点约为 `1 / Width = 125 MHz`，已经超过 `125 MS/s` 采样系统的 `62.5 MHz` Nyquist 频率。
2. `16 ns` 脉宽的第一零点约为 `62.5 MHz`，正好贴近 Nyquist，工程上也没有足够过渡带。
3. 矩形脉冲边沿理论上需要无限带宽；当脉宽只有 1 到 2 个样点时，数字域无法表达稳定边沿，模拟域也无法按理想矩形还原。
4. 峰均比、占空比和频谱指标只有在高电平段、低电平段都包含足够样点时才有可重复意义。

RF 行业实践：

- 仪器参数下限通常不等同于 DAC 单采样点间隔，而是需要给输出链路留出幅度建立、滤波、校准和频谱余量。
- VSG60 的 `Width` 最小 `0.100 us` 是合理的工程下限。即使在 `40 MS/s` 下也约为 4 个样点，在 `50 MS/s` 下约为 5 个样点；在本机 `125 MS/s` 下约为 12 到 13 个样点，余量明显更好。

建议结论：

1. 不建议把 `8 ns / 16 ns` 暴露为日常 Pulse 合法范围。
2. 建议正常 UI/business 范围收紧为：

```text
Pulse_Width  >= 100 ns
Pulse_Period >= 100 ns
Width <= Period
```

3. 对真正的脉冲而非常开信号，建议进一步提示或限制低电平时间：

```text
if Width < Period:
    Period - Width >= 100 ns
```

也就是说，默认推荐的最小 50% duty pulse 是：

```text
Width  = 100 ns
Period = 200 ns
```

4. 如果仍需要服务/研发模式覆盖极限点，可以保留内部生成器的 8 ns 能力，但不要作为普通用户可见的默认参数范围。

历史落地建议，已被第 22 节的 `64 ns` 决策取代：

- 第一阶段：将 Analog 与 HTRA 两侧 `Pulse_Width / Pulse_Period` metadata 统一收紧到 `100 ns`，并将本地 HTRA normalize 常量同步为 `100 ns`。
- 第二阶段：增加 `Width < Period` 且 `Period - Width < 100 ns` 的质量保护。若 UI 框架不方便表达动态范围，可先在 business normalize 中收口或弹出明确错误。
- 第三阶段：如果未来增加 Advanced/Service mode，再用隐藏开关恢复 `8 ns / 16 ns` 极限参数，用于算法边界测试，而不是作为产品默认能力承诺。

## 22. 2026-06-22 64 ns 下限落地决定

用户确认希望 Analog 与 HTRA 的 Pulse 参数范围统一改为 `64 ns`。

工程判断：

- `125 MS/s` 下 `Ts = 8 ns`。
- `64 ns = 8 samples`，相比 `8 ns / 16 ns` 的 `1 / 2 samples` 极限已经有基本离散表达余量。
- `64 ns` 仍是偏激进的硬下限，不应理解为所有频谱/峰均比指标都能达到最佳；真实 RF 链路仍会受到 DAC 重构、模拟带宽、滤波器和输出链路影响。
- 对日常 50% duty pulse，更稳妥的最小组合应理解为：

```text
Width  = 64 ns
Period = 128 ns
```

而不是 `Width = Period = 64 ns`。后者是常开满幅，不是脉冲。

落地范围：

```text
Pulse_Width  >= 64 ns
Pulse_Period >= 64 ns
Width <= Period
```

本轮只修改静态最小值和 metadata，使 Analog 与 HTRA 行为一致；暂不增加 `Period - Width` 的动态低电平时间限制。

## 23. 2026-06-22 N9040B UXA 64 ns / 128 ns 测试方案

测试对象：

```text
Pulse Width  = 64 ns
Pulse Period = 128 ns
Duty Cycle   = 50 %
PRF          = 7.8125 MHz
Ideal PAPR   = 10 * log10(Period / Width) = 3.01 dB
```

仪器假设：

- Keysight N9040B UXA Signal Analyzer。
- 优先使用 N9067C Pulse Analysis 应用；若无该应用，则用 IQ Analyzer / VSA time trace 辅助，Spectrum Analyzer zero-span 仅作为粗略备用。
- 对 64 ns 脉冲，推荐分析带宽不低于 100 MHz；若有 255 / 510 MHz / 1 GHz 分析带宽选件，优先使用 255 MHz 或更高做边沿与包络验证。

主要关注指标：

1. 时间指标：pulse width、PRI、PRF、duty cycle、rise time、fall time、pulse-to-pulse jitter。
2. 幅度指标：top level、base level、on level、peak level、mean level、peak-to-average、on/off ratio。
3. 包络质量：overshoot、undershoot、droop、ripple。
4. 频域指标：carrier frequency error、PRF 谱线间隔、sinc 包络第一零点、偶次谱线抑制、杂散、谐波、邻近频谱扩展。
5. 脉内质量：in-pulse frequency/phase transient，确认无异常 FM/PM 或相位跳变。

建议判据，第一版用于算法和硬件联调，不作为最终产品规格：

```text
Width:        64 ns ± 8 ns
PRI:          128 ns ± 1 ns 或 ±1 %
PRF:          7.8125 MHz，若 10 MHz reference 锁定则按设备频率精度收紧
Duty:         50 % ± 5 %
PAPR:         3.01 dB ± 1 dB
Overshoot:    < 10 %
Droop/Ripple: < 5 % 初始目标
On/Off ratio: 64 ns 极限点先记录实测；1 us / 2 us 对照波形建议 > 35 dB
```

测试用例：

1. CW 基线：Pulse disabled 或 `Width == Period`，确认载波功率、频率、杂散底噪。
2. 容易观察对照：`Width = 1 us, Period = 2 us`。
3. 中间对照：`Width = 128 ns, Period = 256 ns`。
4. 目标极限：`Width = 64 ns, Period = 128 ns`。
5. 常开边界：`Width = 64 ns, Period = 64 ns`，应表现为连续满幅载波而非脉冲。

连接与保护：

- DUT RF OUT -> 10 dB 或 20 dB 外部衰减器 -> N9040B RF INPUT。
- DUT 输出先设低功率，例如到分析仪输入端约 `-30 dBm` 到 `-20 dBm`。
- N9040B input attenuation 先设 20 dB，preamp off，确认不过载后再优化。
- 若 DUT 和 N9040B 都有 10 MHz reference，建议同源锁定，减少 PRF / carrier frequency 判断的不确定性。

N9067C Pulse Analysis 推荐操作：

1. N9040B 预热至少 30 分钟，执行或确认 alignment。
2. `Preset`。
3. `Mode` -> `Pulse`。若列表没有 Pulse，检查 N9067C / Pulse Analysis license。
4. 设置 center frequency 为 DUT RF 频点。
5. 设置 reference level，使峰值距离屏幕顶端至少 5 到 10 dB 余量。
6. 设置 input attenuation 20 dB，preamp off。
7. 设置 analysis bandwidth：优先 255 MHz；没有则用可用的最高宽带，最低建议 100 MHz。
8. 设置 acquisition / record length：先抓 20 us，约 156 个目标脉冲；稳定后抓 128 us 以上，约 1000 个脉冲做统计。
9. Trigger 先用 free run / continuous 找信号，看到包络后改 RF envelope / pulse trigger，trigger level 放在 top/base 中点附近。
10. 打开 pulse detection / auto threshold；若识别失败，手动设置 threshold 为 top/base 之间的 50 %。
11. 选择结果表参数：width、PRI、PRF、duty、rise/fall、top/base/on/peak/mean level、peak-to-average、overshoot、droop、ripple、frequency mean、phase mean。
12. Run continuous 观察，再 Run single 固化一次，保存 screenshot 和 pulse table CSV。

Spectrum Analyzer 推荐操作：

1. `Mode` -> `Spectrum Analyzer`。
2. Center frequency = DUT RF 频点。
3. Span = 100 MHz；目标波形第一零点约在 carrier ±15.625 MHz，100 MHz span 足够观察主瓣和若干旁瓣。
4. RBW = 100 kHz 到 300 kHz，用于分辨 `7.8125 MHz` PRF 谱线。
5. Trace average 20 次以上，Detector 用 RMS / average；查找杂散时再切 Peak。
6. Marker peak 放在 carrier；delta marker 检查 ±7.8125 MHz、±15.625 MHz、±23.4375 MHz。
7. 50 % duty 理想矩形脉冲应主要出现奇次 PRF 线，`±2 * PRF = ±15.625 MHz` 附近接近 sinc 零点；非理想边沿和 off 泄漏会抬高这些线。

若无 Pulse Analysis 应用：

- 优先用 IQ Analyzer / VSA：设置 center frequency、analysis bandwidth、capture time，显示 magnitude vs time，用 marker 测 width/period。
- Spectrum Analyzer zero-span 只能作为粗略方法；若 RBW/IF bandwidth 不足，64 ns 包络会被明显展宽，不能用它给最终时间指标定结论。

## 24. 2026-06-22 Pulse 过满幅实验分支修改

Scope:

- 修改 HTRA Pulse 自主生成器的 IQ 高电平映射，用于用户分支上的实机对比测试。
- 同步修改 Analog Pulse 第三方路径的生成后 IQ 后处理，避免 licensed 状态切到 Analog provider 时测不到本轮实验口径。
- 不修改 UI 参数、采样率选择、保存 WAV、provider 切换或 playback 下发链路。

Assumption:

- 用户希望验证“过满幅”是否能让 Pulse Analysis 的 `top level` 更接近输入 PEP。
- 本轮按实验口径处理，不把该口径定义为最终产品校准结论。
- 高电平样点改为 `I=32767, Q=32367`。
- 半幅边沿按高电平向量逐路折半，使用 `I=16384, Q=16184`。
- 关断样点仍保持 `I=0, Q=0`。

Success criteria:

1. HTRA `Width < Period` 的高电平样点输出 `32767,32367`。
2. HTRA 半幅边沿样点输出 `16384,16184`。
3. HTRA `Width >= Period` 的常开边界也输出全周期 `32767,32367`。
4. Analog 第三方 Pulse 生成后，将非零 I 样点按 `Q = round(I * 32367 / 32767)` 补出 Q 路；因此典型满幅/半幅样点分别变为 `32767,32367` 和 `16384,16184`。
5. 采样率、样点数、64 ns 参数下限和最短 payload 补齐行为保持不变。

Verification level:

- `static`

Static verification:

- 确认 `src/plugins/htra/pulsemodulator.cpp` 中 Pulse 样点写入已从单 I 标量改为 I/Q level。
- 确认 `src/plugins/analog/pulsemodulator.cpp` 中第三方 Pulse 结果复制后执行同样的过满幅 Q 路后处理。
- 确认 `src/plugins/htra/CMakeLists.txt` 已包含 `pulsemodulator.cpp`，本轮无需新增 CMake 项。
- 按仓库规则，本轮不主动编译；由用户做实机测试后再决定是否保留。
