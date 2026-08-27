# FM 复基带波形生成算法方案

## 1. 任务目标

为 `src/plugins/analog/fmmodulator.cpp` 当前使用的第三方 `GenerateFmWaveform(...)` 设计一套可本地实现的 FM 波形生成算法。

目标参数面保持和当前 `FmModulator` 一致：

- `Rate`：FM 调制率，单位 Hz。
- `Deviation`：最大频偏，单位 Hz。
- `Shape`：调制波形形状，支持 `Sine / Square / Triangle / Ramp`。

目标输出保持和当前链路一致：

- 输出 `int16_t` 交织 IQ 数据：`I0, Q0, I1, Q1, ...`。
- 单个 complex sample 占 `4 Bytes`。
- FM 输出为复基带恒包络 IQ。
- 不生成 carrier frequency；RF 搬移由设备频率/DUC/射频链路负责。

本方案是静态分析和 `data/FM1K.csv`、`data/FMSqure.csv`、`data/FMRamp.csv`、`data/FMRate10M.csv`、`data/FMDevication10K.csv` 反推结果，没有编译或运行验证。

## 2. 现有代码事实

当前 FM 生成链路在 `FmModulator::workerLoop()` 中完成：

```cpp
m_sampleRate = calculateFMSampleRate(params.Rate, params.Deviation);
result.sampleRate = m_sampleRate;
int computeResult = GenerateFmWaveform(objId, &params, result.sampleRate, &iq, &len);
```

当前参数默认值：

```text
Rate = 1 kHz
Deviation = 1 kHz
Shape = Sine
```

当前参数收口：

```text
Rate:      1 Hz ~ 10 MHz
Deviation: 1 Hz ~ 10 MHz
```

当前 `calculateFMSampleRate(rb, df)` 的主要约束：

```text
Fs = n * Rate
n >= 6 且 n 为偶数
Fs > 2 * (Rate + Deviation)
```

并在本发射机合法采样率范围内倾向取高采样率。

第三方头文件只暴露：

```cpp
struct FMModParams
{
    double Rate;
    double Deviation;
    ModShape Shape;
};

int GenerateFmWaveform(int objId, const FMModParams *paramsIn,
                       double sampleRate, short **iq, int32_t *lenOut);
```

仓库中没有第三方 API 的实现源码，因此本方案对第三方内部算法的描述是基于接口、当前代码、调制理论和参考 CSV 的反推。

## 3. 参考数据结论

### 3.1 `data/FM1K.csv`

VSG60 导出参数：

- `Rate = 1 kHz`
- `Deviation = 1 kHz`
- `Shape = Sine`

CSV 特征：

- 行数：`3000`
- 复基带 IQ，恒包络接近 `32767`。
- 首点：`I[0] = 32767, Q[0] = 0`。
- 末点：`I[2999] = 32767, Q[2999] = 0`。
- 关键点：
  - `I[500] ~= 28742, Q[500] ~= 15736`
  - `I[750] ~= 17676, Q[750] ~= 27592`
  - `I[1000] ~= 2288, Q[1000] ~= 32688`
  - `I[1500] ~= -13636, Q[1500] ~= 29796`

相位反推：

```text
phase[n] = atan2(Q[n], I[n])
phase[0]    ~= 0
phase[500]  ~= 0.5 rad
phase[750]  ~= 1.0 rad
phase[1000] ~= 1.5 rad
phase[1500] ~= 2.0 rad
```

这些点与下式吻合：

```text
N = 3000
Fs = Rate * N = 3 MS/s
beta = Deviation / Rate = 1

phase[n] ~= beta * (1 - cos(2*pi*n/N))
I[n] = round(32767 * cos(phase[n]))
Q[n] = round(32767 * sin(phase[n]))
```

更准确地说，CSV 与“逐点累加瞬时频偏”的离散实现高度一致：

```text
m[n] = sin(2*pi*n/N)
phase[n] = phase[n-1] + 2*pi * Deviation / Fs * m[n]
I[n] = round_or_table(32767 * cos(phase[n]))
Q[n] = round_or_table(32767 * sin(phase[n]))
```

该离散积分模型与 `C:\Users\jsl\Desktop\Modem.cpp` 中 `ASD_GenerateFMWaveform(...)` / `ASD_FM_ModulatePoint(...)` 的思路一致：先生成调制源 `m[]`，再用 `Deviation / SampleRate` 驱动相位累加器，最后查 `sin/cos` 表输出 IQ。

### 3.2 `data/FMSqure.csv`

VSG60 导出参数：

- `Rate = 1 kHz`
- `Deviation = 1 kHz`
- `Shape = Square`

CSV 特征：

- 行数：`3000`
- 复基带 IQ，恒包络接近 `32767`。
- 首点：`I[0] = 32767, Q[0] = 69`，对应 `phase ~= 0.002106 rad`。
- 半周期附近：
  - `I[1499] = -32767, Q[1499] = 0`，对应 `phase ~= pi`。
  - `I[1500] = -32767, Q[1500] = 69`，相位开始回落。
- 末点：`I[2999] = 32767, Q[2999] = 0`，相位回到 `0`。

该数据与下式吻合，误差通常在 `1 LSB` 量级：

```text
N = 3000
Fs = 3 MS/s
phaseStep = 2*pi * Deviation / Fs

square[n] =
    +1, 0 <= n <  N/2
    -1, N/2 <= n < N

phase[n] = phase[n-1] + phaseStep * square[n]
I[n] = round(32767 * cos(phase[n]))
Q[n] = round(32767 * sin(phase[n]))
```

这说明 FM Square 与 AM Square 的相位定义一致：先正频偏，再负频偏。

### 3.3 `data/FMRamp.csv`

VSG60 导出参数：

- `Rate = 1 kHz`
- `Deviation = 1 kHz`
- `Shape = Ramp`

CSV 特征：

- 行数：`3000`
- 复基带 IQ，恒包络接近 `32767`。
- 首点：`I[0] = 32767, Q[0] = -69`，对应 `phase ~= -0.002106 rad`。
- 中点附近：`I[1499] ~= -34, Q[1499] = -32768`，对应 `phase ~= -pi/2`。
- 末点：`I[2999] = 32767, Q[2999] = -69`，与首点相同。

该数据与下式吻合，误差通常在 `1 LSB` 量级：

```text
N = 3000
Fs = 3 MS/s
ramp[n] = -1 + 2*n/N

phase[n] = phase[n-1] + 2*pi * Deviation / Fs * ramp[n]
I[n] = round(32767 * cos(phase[n]))
Q[n] = round(32767 * sin(phase[n]))
```

这说明 FM Ramp 与 AM Ramp 一致，是上升锯齿对应的瞬时频偏曲线：从 `-Deviation` 线性上升到接近 `+Deviation`。VSG60 没有对 Ramp 做离散均值校正；因此一个周期末尾相位回到首点相位，而不是严格回到 `0`。

### 3.4 `data/FMDevication10K.csv`

VSG60 导出参数：

- `Rate = 1 kHz`
- `Deviation = 10 kHz`
- `Shape = Sine`

CSV 特征：

- 行数：`3000`
- 复基带 IQ，恒包络接近 `32767`。
- 采样率仍可按 `Fs = 3 MS/s` 理解。
- `beta = Deviation / Rate = 10`。

该数据与下式吻合，误差通常在 `1~3 LSB` 量级：

```text
N = 3000
Fs = 3 MS/s
m[n] = sin(2*pi*n/N)
phase[n] = phase[n-1] + 2*pi * 10000 / 3000000 * m[n]
```

这说明在 `Deviation = 10 kHz` 时，VSG60 仍保持 `3000 samples/period`；它没有因为频偏从 `1 kHz` 增大到 `10 kHz` 而改变点数。该样本仍远低于 `Fs > 2 * (Rate + Deviation)` 的约束。

### 3.5 `data/FMRate10M.csv`

VSG60 导出参数：

- `Rate = 10 MHz`
- `Deviation = 1 MHz`
- `Shape = Sine`

CSV 特征：

- 行数：`1024`
- 文件内容是 `4` 点周期重复 `256` 次。
- 单周期数据：
  - `I[0] = 32767, Q[0] = 0`
  - `I[1] = 32365, Q[1] = 5126`
  - `I[2] = 32365, Q[2] = 5126`
  - `I[3] = 32767, Q[3] = 0`

单周期模型：

```text
N_period = 4
Fs = Rate * N_period = 40 MS/s
Deviation / Fs = 1M / 40M = 0.025
phaseStep = 2*pi * 0.025 ~= 0.1570796 rad

m = [0, +1, 0, -1]
phase = [0, 0.1570796, 0.1570796, 0]
```

这与 CSV 完全吻合。该文件还说明 VSG60 FM 在高 `Rate` 下可能会：

- 先按 `50 MS/s` 上限选择每周期点数，`floor(50M / 10M) = 5`，再收口到偶数 `4`。
- 当单周期点数很短时，把周期重复到至少 `1024` 个 complex samples。

## 4. 先生成瞬时频偏，再积分成相位

`FM1K.csv` 证明 VSG60 的 `Shape=Sine` 表示：

```text
instantaneous frequency offset = Deviation * sin(2*pi*Rate*t)
```

因此正确做法是先生成瞬时频偏，再积分成相位：

```text
f_offset(t) = Deviation * shape(2*pi*Rate*t)
phase(t) = 2*pi * integral(f_offset(t), dt)
x(t) = exp(j * phase(t))
```

对正弦 `shape(t) = sin(2*pi*Rate*t)`，若初始相位为 `0`，连续闭式为：

```text
phase(t) = beta * (1 - cos(2*pi*Rate*t))
beta = Deviation / Rate
```

这正是 `FM1K.csv` 的相位形态。

## 5. 波形数学模型

### 5.1 参数归一化

```text
rate = clamp(Rate, 1 Hz, 10 MHz)
deviation = clamp(Deviation, 1 Hz, 10 MHz)
beta = deviation / rate
```

### 5.2 输出标度

参考 `FM1K.csv` 显示，FM 使用满幅恒包络：

```text
amplitude = 32767
```

因此：

```text
I[n]^2 + Q[n]^2 ~= 32767^2
```

这与 AM 的 `baseScale = 16384` 不同。AM 需要给 `1 + depth` 包络留余量；FM 是恒包络相位调制，可以使用接近满量程。

### 5.3 推荐离散模型

设单周期 complex sample 数为 `N`，采样率为：

```text
Fs = Rate * N
```

逐点生成：

```text
phase = 0
for n = 0..N-1:
    m = shapeValue(Shape, n, N)
    phase += 2*pi * Deviation / Fs * m
    I[n] = quantize(32767 * cos(phase))
    Q[n] = quantize(32767 * sin(phase))
```

注意：这里采用“先累加当前频偏样点，再输出当前 IQ”的顺序。对 `Sine`，`m[0] = 0`，因此首点仍为 `(32767, 0)`。这个顺序和 `Modem.cpp` 的相位累加器实现更接近。

### 5.4 正弦闭式校验

对 `Shape=Sine`：

```text
m[n] = sin(2*pi*n/N)
phase[n] ~= beta * (1 - cos(2*pi*n/N))
```

当：

```text
Rate = 1 kHz
Deviation = 1 kHz
beta = 1
N = 3000
```

则：

```text
n = 0:    phase = 0
n = 500:  phase ~= 0.5 rad
n = 750:  phase ~= 1.0 rad
n = 1000: phase ~= 1.5 rad
n = 1500: phase ~= 2.0 rad
```

与 `FM1K.csv` 的相位一致。

## 6. Shape 定义

### 6.1 Sine

已由 `FM1K.csv` 确认：

```text
shape[n] = sin(2*pi*n/N)
```

含义：

- 起点频偏为 `0`。
- 四分之一周期频偏为 `+Deviation`。
- 半周期频偏回到 `0`。
- 四分之三周期频偏为 `-Deviation`。

### 6.2 Square

已由 `FMSqure.csv` 确认，FM Square 与 AM Square 的相位定义一致，表示瞬时频偏先为 `+Deviation`，后为 `-Deviation`：

```text
shape[n] =
    +1, 0 <= n < N/2
    -1, N/2 <= n < N
```

该定义均值为 `0`，一个周期积分后相位可回到起始值，适合循环 playback。

### 6.3 Triangle

FM Triangle 当前仍没有 VSG60 CSV。建议先与 AM 的 Triangle 形状保持一致：

```text
u = n / N

if u <= 0.5:
    shape = -1 + 4*u
else:
    shape = 3 - 4*u
```

该定义从 `-Deviation` 线性扫到 `+Deviation`，再线性扫回 `-Deviation`。它的周期均值为 `0`，适合循环 playback。

### 6.4 Ramp

已由 `FMRamp.csv` 确认，FM Ramp 与 AM Ramp 的形状定义一致：

```text
shape[n] = -1 + 2*n/N
```

VSG60 没有做离散均值校正。由于 `shape[0] = -1` 且 `shape[N-1] = 1 - 2/N`，单周期累加后相位回到首点相位附近；从 CSV 看，首点和末点均为 `32767,-69`，循环播放时样点连续。

## 7. 采样率与每周期点数策略

### 7.1 当前 SGStudio 策略

当前 `calculateFMSampleRate(rate, deviation)` 倾向在本发射机合法范围内取高采样率，并满足：

```text
Fs = n * Rate
n >= 6 且 n 为偶数
Fs > 2 * (Rate + Deviation)
```

该策略的优点：

- 保守满足 FM Carson 带宽近似和采样约束。
- 高采样率下相位轨迹更平滑。
- 对高 `Deviation / Rate` 的宽带 FM 更稳妥。

代价：

- 默认 `Rate = 1 kHz, Deviation = 1 kHz` 时会倾向非常高的采样率，与 `FM1K.csv` 的 `3 MS/s` 不一致。
- 波形体积、生成、下载和预览成本更高。

### 7.2 VSG60 采样率与长度策略

结合当前 FM CSV，VSG60 的 FM 采样率策略可以按以下口径理解：

```text
FsMax = 50 MS/s
PreferredSamplesPerPeriod = 3000
MinSamplesPerPeriod = 4
MinTotalComplexSamples = 1024
```

每周期点数选择：

```text
N_raw = min(PreferredSamplesPerPeriod, floor(FsMax / Rate))
N_period = floor_to_even(N_raw)
N_period = max(N_period, MinSamplesPerPeriod)
Fs = Rate * N_period
```

总输出点数选择：

```text
if N_period >= MinTotalComplexSamples:
    N_total = N_period
else:
    N_total = ceil(MinTotalComplexSamples / N_period) * N_period
```

验证：

```text
Rate = 1 kHz:
  N_period = min(3000, floor(50M / 1k)) = 3000
  Fs = 3 MS/s
  N_total = 3000

Rate = 10 MHz:
  floor(50M / 10M) = 5
  floor_to_even(5) = 4
  Fs = 40 MS/s
  N_total = ceil(1024 / 4) * 4 = 1024
```

`FMDevication10K.csv` 说明 `Deviation = 10 kHz` 时仍使用 `N_period = 3000`，因此频偏不会在低频偏范围内直接改变每周期点数。对于更大的 `Deviation`，仍建议保留带宽下限约束：

```text
Fs > 2 * (Rate + Deviation)
```

### 7.3 推荐策略分层

建议拆成以下策略，便于迁移期对比：

```text
Vsg60Exact:
  FsMax = 50 MS/s
  PreferredSamplesPerPeriod = 3000
  MinSamplesPerPeriod = 4
  MinTotalComplexSamples = 1024
  目标是匹配当前 VSG60 CSV。

Tx125Quality:
  使用本发射机 125 MS/s 上限。
  可以复用 PreferredSamplesPerPeriod = 3000，也可以提高高 Rate 下的 N_period。
  目标是发射质量和设备合法性，不保证逐点匹配 VSG60。

CurrentMaxRate:
  沿用 calculateFMSampleRate(rate, deviation)。
  目标是保留当前 SGStudio 行为。
```

### 7.4 可落地的样点数选择伪代码

```cpp
qint64 chooseFmSamplesPerPeriod(double rate,
                                double deviation,
                                double fsMax,
                                qint64 preferredSamplesPerPeriod)
{
    constexpr double kFsMin = DATA_SAMPLE_RATE_MIN;
    constexpr qint64 kMaxComplexSamples = Analog::MAXDOWNLOADSIZE / 4;
    constexpr qint64 kMinSamplesPerPeriod = 4;

    const double minFsByBandwidth = 2.0 * (rate + deviation);
    const double minFs = std::max(kFsMin, minFsByBandwidth);

    qint64 minN = static_cast<qint64>(std::ceil(minFs / rate));
    qint64 maxN = static_cast<qint64>(std::floor(fsMax / rate));
    maxN = floorToEven(maxN);
    maxN = std::min(maxN, kMaxComplexSamples);

    if (maxN < minN) {
        return 0; // 参数组合在当前 fsMax / size 约束下不可生成
    }

    qint64 n = preferredSamplesPerPeriod;
    n = std::max(n, minN);
    n = std::min(n, maxN);
    n = std::max(n, kMinSamplesPerPeriod);

    // 保持偶数，便于 Square/Triangle 半周期对齐。
    if (n > minN && (n % 2 != 0)) {
        --n;
    }

    return std::max<qint64>(1, n);
}
```

对于 `FM1K.csv`：

```text
rate = 1 kHz
deviation = 1 kHz
preferredSamplesPerPeriod = 3000
fsMax = 50 MS/s

N = 3000
Fs = 3 MS/s
```

总长度选择：

```cpp
qint64 chooseFmTotalSamples(qint64 samplesPerPeriod)
{
    constexpr qint64 kMinTotalComplexSamples = 1024;

    if (samplesPerPeriod >= kMinTotalComplexSamples) {
        return samplesPerPeriod;
    }

    const qint64 repeats = (kMinTotalComplexSamples + samplesPerPeriod - 1)
                           / samplesPerPeriod;
    return repeats * samplesPerPeriod;
}
```

这可以解释 `FMRate10M.csv`：`N_period = 4`，重复 `256` 次后得到 `N_total = 1024`。

## 8. 推荐实现策略

### 8.1 本地生成入口

建议新增独立 helper，而不是直接把算法散落在 `FmModulator::workerLoop()` 中：

```cpp
struct FmGenerationResult
{
    QVector<int16_t> iq;
    double sampleRate = 0.0;
};

enum class FmSampleRatePolicy
{
    Vsg60Exact,
    Tx125Quality,
    CurrentMaxRate
};

FmGenerationResult generateFmBasebandWaveform(const FMModParams &params,
                                              FmSampleRatePolicy policy);
```

### 8.2 Shape 生成

```cpp
double fmShapeValue(ModShape shape, qint64 n, qint64 N)
{
    const double u = static_cast<double>(n) / static_cast<double>(N);

    switch (shape) {
    case Sine:
        return std::sin(2.0 * M_PI * u);

    case Square:
        return (u < 0.5) ? 1.0 : -1.0;

    case Triangle:
        return (u <= 0.5)
            ? (-1.0 + 4.0 * u)
            : ( 3.0 - 4.0 * u);

    case Ramp:
        return -1.0 + 2.0 * u;
    }

    return std::sin(2.0 * M_PI * u);
}
```

FM Ramp 不做均值校正，以匹配 `FMRamp.csv`。

### 8.3 IQ 生成伪代码

```cpp
QVector<int16_t> generateFmIq(const FMModParams &params,
                              qint64 samplesPerPeriod,
                              qint64 totalSamples)
{
    constexpr double kAmplitude = 32767.0;
    constexpr double kTwoPi = 2.0 * M_PI;

    const double rate = std::max(1.0, std::min(params.Rate, 10e6));
    const double deviation = std::max(1.0, std::min(params.Deviation, 10e6));
    const qint64 N = std::max<qint64>(1, samplesPerPeriod);
    const qint64 total = std::max(N, totalSamples);
    const double sampleRate = rate * static_cast<double>(N);
    const double phaseStepScale = kTwoPi * deviation / sampleRate;

    QVector<double> shape;
    shape.resize(static_cast<int>(N));

    for (qint64 n = 0; n < N; ++n) {
        shape[n] = fmShapeValue(params.Shape, n, N);
    }

    QVector<int16_t> iq;
    iq.resize(static_cast<int>(total * 2));

    double phase = 0.0;
    for (qint64 n = 0; n < total; ++n) {
        const qint64 p = n % N;
        if (p == 0) {
            phase = 0.0;
        }

        phase += phaseStepScale * shape[p];

        const long i = std::lround(kAmplitude * std::cos(phase));
        const long q = std::lround(kAmplitude * std::sin(phase));

        iq[2 * n] = static_cast<int16_t>(std::max<long>(-32768, std::min<long>(32767, i)));
        iq[2 * n + 1] = static_cast<int16_t>(std::max<long>(-32768, std::min<long>(32767, q)));
    }

    return iq;
}
```

这里每个周期开始时重置 `phase = 0`，用于生成与 `FMRate10M.csv` 一样的重复周期文件。对 `Sine / Square`，周期相位本身闭合；对 `Ramp`，VSG60 的首尾样点也一致，因此重复周期不会产生样点跳变。

### 8.4 `Modem.cpp` 的借鉴边界

`C:\Users\jsl\Desktop\Modem.cpp` 的 FM 部分值得借鉴的点：

- 先生成调制源 `m[]`，再把 `m[]` 积分成相位。
- 使用相位累加器，而不是把 FM 写成实载波或直接套 passband 公式。
- 用 `Deviation / SampleRate` 控制每点相位增量。
- 输出恒包络 IQ：`I = cos(phase) * Amplitude`，`Q = sin(phase) * Amplitude`。

核心思路在：

```cpp
pFreqMod->ref = (ModDeviation / SampleRate) * (1 << 18);
phase_index += round(pFreqMod->ref * m[n]);
I = cos_table[phase_index] * Amplitude;
Q = sin_table[phase_index] * Amplitude;
```

## 9. 验证计划

### 9.1 对齐 `FM1K.csv`

输入：

```text
Rate = 1 kHz
Deviation = 1 kHz
Shape = Sine
Policy = Vsg60Exact
```

期望：

```text
N = 3000
sampleRate = 3 MS/s
constant envelope ~= 32767
I[0] = 32767, Q[0] = 0
phase[500] ~= 0.5 rad
phase[750] ~= 1.0 rad
phase[1000] ~= 1.5 rad
phase[1500] ~= 2.0 rad
I[2999] = 32767, Q[2999] = 0
```

可接受误差：

- 使用 double 逐点累加模型时，与 CSV 应稳定在数个 LSB 内。
- 使用闭式 `beta * (1 - cos(...))` 时也能解释相位趋势，但逐点误差会略大。

### 9.2 对齐 `FMSqure.csv`

输入：

```text
Rate = 1 kHz
Deviation = 1 kHz
Shape = Square
```

期望：

```text
N_period = 3000
N_total = 3000
sampleRate = 3 MS/s
首半周期正频偏，后半周期负频偏
phase[1499] ~= pi
phase[2999] ~= 0
```

### 9.3 对齐 `FMRamp.csv`

输入：

```text
Rate = 1 kHz
Deviation = 1 kHz
Shape = Ramp
```

期望：

```text
N_period = 3000
N_total = 3000
sampleRate = 3 MS/s
ramp[n] = -1 + 2*n/N
不做均值校正
I[0],Q[0] = 32767,-69
I[2999],Q[2999] = 32767,-69
```

### 9.4 对齐 `FMDevication10K.csv`

输入：

```text
Rate = 1 kHz
Deviation = 10 kHz
Shape = Sine
```

期望：

```text
N_period = 3000
N_total = 3000
sampleRate = 3 MS/s
beta = 10
phase[n] = phase[n-1] + 2*pi * 10000 / 3000000 * sin(2*pi*n/3000)
```

### 9.5 对齐 `FMRate10M.csv`

输入：

```text
Rate = 10 MHz
Deviation = 1 MHz
Shape = Sine
```

期望：

```text
N_period = 4
sampleRate = 40 MS/s
N_total = 1024
单周期 IQ = [(32767,0), (32365,5126), (32365,5126), (32767,0)]
该 4 点周期重复 256 次
```

### 9.6 仍建议补充的 FM CSV

当前仍缺少 Triangle 和更多高频/大频偏组合，建议继续导出：

```text
Rate = 1 kHz,  Deviation = 1 kHz,  Shape = Triangle
Rate = 1 MHz,  Deviation = 1 MHz,  Shape = Sine
Rate = 10 MHz, Deviation = 10 MHz, Shape = Sine
Rate = 1 kHz,  Deviation = 1 MHz,  Shape = Sine
```

这些样本可以进一步确认：

- FM Triangle 的 shape 相位是否与 AM 一致。
- `Deviation` 很大时是否会抬高 `N_period` 或直接报错/收口。
- `N_period < 1024` 且不能整除 `1024` 时，VSG60 是取整周期重复到超过 `1024`，还是固定输出 `1024` 点。
- 高 Rate 高 Deviation 组合下，是否仍优先满足 `Fs > 2 * (Rate + Deviation)`。

## 10. 推荐落地顺序

1. 保留当前第三方 API 路径，新增本地 FM 生成 helper。
2. 先实现 `Shape=Sine`，用 `FM1K.csv` 做离线对比。
3. 增加策略开关：`Vsg60Exact / Tx125Quality / CurrentMaxRate`。
4. 用 `FMSqure.csv`、`FMRamp.csv`、`FMDevication10K.csv`、`FMRate10M.csv` 固定 Square/Ramp、频偏和高 Rate 收口。
5. 补齐 Triangle 和极端大频偏 CSV 后，固定完整 VSG60 策略。
6. 验证通过后再考虑替换 `GenerateFmWaveform(...)`。

## 11. 当前建议结论

FM 本地算法的核心应采用“瞬时频偏积分成相位”的复基带模型：

```text
f_offset[n] = Deviation * shape[n]
phase[n] = phase[n-1] + 2*pi * f_offset[n] / Fs
I[n] = quantize(32767 * cos(phase[n]))
Q[n] = quantize(32767 * sin(phase[n]))
```

`FM1K.csv` 已经证明 VSG60 的 `Shape=Sine` 是正弦瞬时频偏，积分后相位为 `beta * (1 - cos(...))`。

采样率/长度策略目前可以按以下 VSG60 观测结果实现：

```text
FsMax = 50 MS/s
PreferredSamplesPerPeriod = 3000
MinSamplesPerPeriod = 4
MinTotalComplexSamples = 1024
```

当前 CSV 已确认：

```text
Rate = 1 kHz, Deviation = 1 kHz/10 kHz:
  N_period = 3000, Fs = 3 MS/s

Rate = 10 MHz, Deviation = 1 MHz:
  N_period = 4, Fs = 40 MS/s, N_total = 1024
```

面向本发射机生产默认值，建议提供 `Vsg60Exact` 用于对齐参考 CSV，同时保留 `Tx125Quality` 或 `CurrentMaxRate` 用于利用 `125 MS/s` 硬件能力。
