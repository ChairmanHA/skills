# PM 复基带波形生成算法方案

## 0. 本次 HTRA 落地计划

Scope:

- 仅在 `src/plugins/htra/` 中新增 PM 业务入口、参数面板和本地波形生成器。
- 复用 FM 的界面结构和 playback business 模式。
- 不接入授权门控；PM 菜单项随 HTRA 插件正常注册。
- UI 对 `PhaseDeviation` 与 `InitPhase` 使用 degree；profile 和算法内部使用 rad。
- 不修改 `src/plugins/analog/` 中现有旧 Analog 插件 FM/PM 入口。

Success criteria:

- HTRA 插件编译链路包含 PM 新文件。
- 插件注册出 `Phase Modulation` business entry。
- 面板开放 `Rate / Phase Deviation / Init Phase / Shape`。
- PM 输出 `qint16` 交织 IQ，恒包络，sample rate 落在当前设备合法范围。
- `PhaseDeviation = 0` 时输出由 `InitPhase` 决定的固定相位 IQ。
- Sine PM 满足 `phase[n] = initPhase + phaseDeviation * sin(2*pi*n/N)`。

Verification level: static.

Implementation update:

- Added `src/plugins/htra/pmmodulator.{h,cpp}` with local PM IQ generation.
- Added `src/plugins/htra/pmmodulation.{h,cpp}` as playback business glue.
- Added `src/plugins/htra/pmpanel.{h,cpp,ui}` with `Rate / Phase Deviation(deg) / Init Phase(deg) / Shape`.
- Updated `src/plugins/htra/CMakeLists.txt`.
- Updated `src/plugins/htra/plugin.{h,cpp}` to create `Pm_*` properties and register/unregister `PmModulation`.
- Static check: `rg` confirmed PM symbols are connected through HTRA sources.
- Static check: `git diff --check` reported only existing LF-to-CRLF working-copy warnings, no whitespace errors.

## 1. 任务目标

为信号源新增 Phase Modulation（PM，相位调制）设计一套本地复基带 IQ 生成算法，并与当前 FM 文档保持同一输出口径。

目标输出保持和现有模拟调制链路一致：

- 输出 `int16_t` 交织 IQ 数据：`I0, Q0, I1, Q1, ...`。
- 单个 complex sample 占 `4 Bytes`。
- PM 输出为复基带恒包络 IQ。
- 不生成 carrier frequency；RF 搬移由设备频率/DUC/射频链路负责。

本文档为算法设计和静态分析，没有编译、运行或仪器验证。

## 2. 观察与依据

观察：

- `.github/TaskLog/2026-06-15_fm_baseband_generation_algorithm_design.md` 已经把 FM 收敛为复基带恒包络 IQ。
- FM 的核心模型是先生成瞬时频偏，再积分成相位：

```text
f_offset[n] = Deviation * shape[n]
phase[n] = phase[n-1] + 2*pi * f_offset[n] / Fs
x[n] = exp(j * phase[n])
```

- `src/plugins/htra/fmmodulator.cpp` 已经实现了本地 FM 生成逻辑，且该文件包含在 `src/plugins/htra/CMakeLists.txt` 中。
- `src/plugins/analog/fmmodulator.cpp` 仍通过第三方 `GenerateFmWaveform(...)` 生成 FM，且该文件包含在 `src/plugins/analog/CMakeLists.txt` 中。
- `.github/KnowledgeBase/Waveform_Parameters_Constraints.md` 规定当前通用复基带采样率范围为 `195312.5 S/s ~ 125 MS/s`，并且下发格式是 IQ 交织 `int16`。

推论：

- PM 应复用 FM 的输出格式、满幅恒包络量化方式、shape 源定义和采样率/长度收口思路。
- PM 不应复用 FM 的“频偏积分”核心公式；PM 的调制量本身就是相位偏移。

## 3. PM 与 FM 的关键区别

FM 的控制量是瞬时频偏：

```text
phase_fm(t) = 2*pi * integral(Delta_f * m(t), dt)
```

PM 的控制量是瞬时相位偏移：

```text
phase_pm(t) = phi0 + beta * m(t)
```

其中：

- `m(t)` 是归一化调制源，范围建议为 `[-1, +1]`。
- `beta` 是峰值相位偏移，单位 rad，也就是 PM 的调制指数。
- `phi0` 是初始相位，默认 `0`。

最终复基带输出为：

```text
x(t) = exp(j * phase_pm(t))
I(t) = cos(phase_pm(t))
Q(t) = sin(phase_pm(t))
```

因此 PM 的正确实现是“调制源直接控制相位”，不是“调制源控制频偏后再积分”。这是 PM 与 FM 在 DSP 实现中的主要分界。

## 4. 推荐参数面

第一版建议参数：

```text
Rate
PhaseDeviation
Shape
```
后续可以考虑 InitPhase 纳入 profile
我们当前设计里有 phase0 = 0，
参数含义：

- `Rate`：调制率，单位 Hz。
- `PhaseDeviation`：峰值相位偏移，内部单位 rad。UI 可显示为 degree。
- `Shape`：调制波形形状，支持 `Sine / Square / Triangle / Ramp`。

建议默认值：

```text
Rate = 1 kHz
PhaseDeviation = 1 rad
Shape = Sine
```

建议硬约束：

```text
Rate:            1 Hz ~ 10 MHz
PhaseDeviation:  0 rad ~ 2*pi rad
```

说明：

- `PhaseDeviation = 0` 表示无相位调制，输出恒定 `(32767, 0)`。
- DSP 算法本身可以支持大于 `2*pi` 的相位偏移，因为 `cos/sin` 天然周期化；但第一版 UI 建议先收口到 `0 ~ 2*pi`，避免频谱和用户理解过度发散。
- 后续如要支持宽指数 PM，可把上限放宽为产品规格参数，而不是算法限制。

## 5. Shape 定义

PM 的 shape 建议与 FM 文档保持一致，表示归一化相位偏移源 `m[n]`。

### 5.1 Sine

```text
m[n] = sin(2*pi*n/N)
```

含义：

- 起点相位偏移为 `0`。
- 四分之一周期相位偏移为 `+PhaseDeviation`。
- 半周期相位偏移回到 `0`。
- 四分之三周期相位偏移为 `-PhaseDeviation`。

### 5.2 Square

```text
m[n] =
    +1, 0 <= n <  N/2
    -1, N/2 <= n < N
```

含义：

- 前半周期相位为 `+PhaseDeviation`。
- 后半周期相位为 `-PhaseDeviation`。
- 在半周期边界和循环边界存在相位阶跃，这是 Square PM 的本征行为，会带来较宽频谱。

### 5.3 Triangle

```text
u = n / N

if u <= 0.5:
    m[n] = -1 + 4*u
else:
    m[n] = 3 - 4*u
```

含义：

- 相位从 `-PhaseDeviation` 线性上升到 `+PhaseDeviation`。
- 再线性回落到 `-PhaseDeviation`。
- 相位连续，但导数在转折点不连续。

### 5.4 Ramp

```text
m[n] = -1 + 2*n/N
```

含义：

- 相位从 `-PhaseDeviation` 线性上升到接近 `+PhaseDeviation`。
- 循环边界存在相位回跳，这是锯齿相位调制的本征行为。

## 6. 数学模型

设单周期 complex sample 数为 `N`，采样率为：

```text
Fs = Rate * N
```

逐点生成：

```text
beta = PhaseDeviationRad
phase0 = 0

for n = 0..N_total-1:
    p = n % N
    m = shapeValue(Shape, p, N)
    phase = phase0 + beta * m
    I[n] = quantize(32767 * cos(phase))
    Q[n] = quantize(32767 * sin(phase))
```

输出标度：

```text
amplitude = 32767
I[n]^2 + Q[n]^2 ~= 32767^2
```

注意：

- PM 不需要相位累加器，除非 `PhaseDeviation` 本身是由外部连续物理模型逐点输入。
- 如果直接给 `Delta_phi[n]`，则 `phase = phase0 + Delta_phi[n]`。
- 为避免超大 `phase` 对 `sin/cos` 精度产生不必要影响，实现中可以使用 `std::remainder(phase, 2*pi)` 做相位归一化。

## 7. PM 的等效频偏与采样率约束

虽然 PM 直接调相，但其瞬时频偏由相位导数决定：

```text
f_offset(t) = (1 / 2*pi) * d(phase_pm(t)) / dt
```

对正弦 PM：

```text
phase_pm(t) = beta * sin(2*pi*Rate*t)
f_offset(t) = beta * Rate * cos(2*pi*Rate*t)
```

因此正弦 PM 的峰值等效频偏为：

```text
Delta_f_equiv = beta * Rate
```

这也是射频行业中 PM 与 FM 互相换算的常用口径：PM 的调制指数 `beta` 等价于 FM 的 `Deviation / Rate`。

采样率下限建议沿用 FM 的保守形态：

```text
Fs > 2 * (Rate + Delta_f_equiv)
```

代入正弦 PM：

```text
Fs > 2 * Rate * (1 + beta)
```

对 Square/Ramp 这类存在相位阶跃或导数突变的 shape，理论带宽不再由上述正弦近似完全刻画。工程上第一版建议：

- 仍用 `Fs > 2 * Rate * (1 + beta)` 作为最低硬约束。
- 同时使用足够高的 `samplesPerPeriod` 提供边缘时间分辨率。
- 不在第一版添加自动平滑；如果未来要控制带外谱，可新增显式 edge shaping / low-pass 选项，而不是隐式改变 PM 定义。

## 8. 采样率与长度策略

建议复用 FM 文档中的分层策略，但将频偏参数替换为 PM 的等效频偏：

```text
beta = abs(PhaseDeviationRad)
equivalentDeviation = beta * Rate
```

推荐策略：

```text
VsgLike3000:
  FsMax = 50 MS/s
  PreferredSamplesPerPeriod = 3000
  MinSamplesPerPeriod = 4
  MinTotalComplexSamples = 1024
  目标是保持与 FM 参考 CSV 相近的波形长度口径；由于当前没有 PM CSV，不称为逐点匹配。

Tx125Quality:
  FsMax = 125 MS/s
  PreferredSamplesPerPeriod = 3000
  MinSamplesPerPeriod = 8 或 16
  MinTotalComplexSamples = 1024
  目标是利用当前发射机采样率能力，提升 PM 边缘和高 beta 场景质量。

CurrentFmCompatible:
  复用 FM 的 calculateCompatibleSampleRate 思路。
  用 equivalentDeviation 替代 FM Deviation。
```

可落地的样点数选择口径：

```text
minFsByBandwidth = 2 * Rate * (1 + beta)
minN = ceil(minFsByBandwidth / Rate)
maxN = floor(FsMax / Rate)

N = clamp(PreferredSamplesPerPeriod, minN, maxN)
N = floor_to_even(N)
N = max(N, MinSamplesPerPeriod)

Fs = Rate * N
```

如果 `maxN < minN`，说明该 `Rate / PhaseDeviation / FsMax` 组合在当前策略下不可生成，应报错或由参数层收口，而不是静默生成失真的波形。

总长度建议：

```text
if N >= 1024:
    N_total = N
else:
    N_total = ceil(1024 / N) * N
```

并始终满足：

```text
N_total * 4 <= Analog::MAXDOWNLOADSIZE
```

## 10. 推荐实现入口

建议为 PM 新增独立 helper，而不是把公式散落在业务类的 `workerLoop()` 中。

```cpp
struct PmParams
{
    double rate = 1e3;
    double phaseDeviationRad = 1.0;
    ModShape shape = Sine;
};

struct PmGenerationResult
{
    QVector<qint16> iqData;
    double sampleRate = 0.0;
    QString errorMessage;
};

PmGenerationResult generatePmBasebandWaveform(const PmParams &params,
                                              PmSampleRatePolicy policy);
```

shape 生成可复用 FM 的定义：

```cpp
double pmShapeValue(ModShape shape, quint64 index, quint64 sampleCount)
{
    const double u = static_cast<double>(index) / static_cast<double>(sampleCount);

    switch (shape) {
    case Sine:
        return std::sin(kTwoPi * u);
    case Square:
        return u < 0.5 ? 1.0 : -1.0;
    case Triangle:
        return u <= 0.5 ? (-1.0 + 4.0 * u) : (3.0 - 4.0 * u);
    case Ramp:
        return -1.0 + 2.0 * u;
    }

    return std::sin(kTwoPi * u);
}
```

IQ 生成伪代码：

```cpp
QVector<qint16> generatePmIq(const PmParams &params,
                             quint64 samplesPerPeriod,
                             quint64 totalSamples)
{
    constexpr double kAmplitude = 32767.0;
    constexpr double kTwoPi = 6.283185307179586476925286766559;

    const double beta = std::clamp(params.phaseDeviationRad, 0.0, kTwoPi);
    const quint64 N = std::max<quint64>(1, samplesPerPeriod);
    const quint64 total = std::max(N, totalSamples);

    QVector<qint16> iq;
    iq.resize(static_cast<int>(total * 2));

    for (quint64 n = 0; n < total; ++n) {
        const quint64 p = n % N;
        double phase = beta * pmShapeValue(params.shape, p, N);
        phase = std::remainder(phase, kTwoPi);

        const long i = std::lround(kAmplitude * std::cos(phase));
        const long q = std::lround(kAmplitude * std::sin(phase));

        iq[static_cast<int>(2 * n)] =
            static_cast<qint16>(std::clamp(i, -32768L, 32767L));
        iq[static_cast<int>(2 * n + 1)] =
            static_cast<qint16>(std::clamp(q, -32768L, 32767L));
    }

    return iq;
}
```

与 FM 伪代码相比，关键差异是：

```text
FM: phase += 2*pi * Deviation / Fs * shape[n]
PM: phase  = PhaseDeviationRad * shape[n]
```

## 11. 静态校验样例

### 11.1 Sine PM 默认例

输入：

```text
Rate = 1 kHz
PhaseDeviation = 1 rad
Shape = Sine
N = 3000
Fs = 3 MS/s
```

期望相位：

```text
phase[0]    = 0
phase[750]  = +1 rad
phase[1500] = 0
phase[2250] = -1 rad
```

期望 IQ 近似：

```text
IQ[0]    = (32767, 0)
IQ[750]  ~= (17702, 27572)
IQ[1500] = (32767, 0)
IQ[2250] ~= (17702, -27572)
```

### 11.2 Square PM

输入：

```text
Rate = 1 kHz
PhaseDeviation = pi/2 rad
Shape = Square
N = 3000
```

期望：

```text
0 <= n < 1500:
  phase = +pi/2
  IQ ~= (0, 32767)

1500 <= n < 3000:
  phase = -pi/2
  IQ ~= (0, -32767)
```

半周期边界存在相位阶跃，循环边界也存在相位阶跃；这是 Square PM 的定义结果，不应由算法偷偷抹平。

### 11.3 光学相位公式映射例

若外部物理模型给出：

```text
Delta_n(t) = Delta_n_peak * sin(2*pi*Rate*t)
```

则 PM 参数为：

```text
PhaseDeviation = beta = (2*pi / lambda) * Delta_n_peak * L
Shape = Sine
```

最终：

```text
phase[n] = beta * sin(2*pi*n/N)
```

## 12. 验证计划

静态验证：

- 检查 `PhaseDeviation = 0` 时输出全程为 `(32767, 0)`。
- 检查 Sine PM 的四分之一周期点达到峰值相位偏移。
- 检查输出恒包络：`I^2 + Q^2 ~= 32767^2`。
- 检查 `sampleRate` 落在 `195312.5 S/s ~ 125 MS/s`。
- 检查 `iqData.size() * sizeof(qint16) <= Analog::MAXDOWNLOADSIZE`。

后续实现验证：

- 为 Sine/Square/Triangle/Ramp 添加离线单元或脚本对比。
- 对 Sine PM 验证 `beta = PhaseDeviation`，并确认等效峰值频偏约为 `beta * Rate`。
- 对 Square/Ramp 使用频谱预览确认边缘导致的宽带谱线符合预期。
- 如能从 VSG60 或其他参考源导出 PM CSV，再补充逐点对齐策略。

## 13. 当前建议结论

PM 本地算法的核心应采用“调制源直接映射为相位偏移”的复基带模型：

```text
phase[n] = PhaseDeviationRad * shape[n]
I[n] = quantize(32767 * cos(phase[n]))
Q[n] = quantize(32767 * sin(phase[n]))
```

用户给出的：

```text
Delta_phi = (2*pi / lambda) * Delta_n * L
```

应理解为 `PhaseDeviation` 或逐点 `Delta_phi[n]` 的物理来源，而不是 FM 里的频偏。若 `Delta_n(t)` 是归一化调制源驱动的物理量，则：

```text
PhaseDeviation = (2*pi / lambda) * Delta_n_peak * L
shape[n] = Delta_n[n] / Delta_n_peak
```

采样率方面，PM 可把正弦相位偏移换算为等效峰值频偏：

```text
equivalentDeviation = PhaseDeviationRad * Rate
```

并沿用 FM 的保守采样率约束：

```text
Fs > 2 * (Rate + equivalentDeviation)
```

第一版落地建议保留 FM 的 shape 和恒包络 IQ 输出方式，但新增独立 PM 参数与生成 helper，避免把 `PhaseDeviation` 和 FM 的 `Deviation Hz` 混用。
