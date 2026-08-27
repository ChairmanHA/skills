# AM 复基带波形生成算法方案

## 1. 任务目标

为 `src/plugins/analog/ammodulator.cpp` 当前使用的第三方 `GenerateAmWaveform(...)` 设计一套可本地实现的 AM 波形生成算法。

目标参数面保持和当前 `AmModulator` 一致：

- `Rate`：AM 调制率，单位 Hz。
- `Depth`：AM 调制深度，单位 `%`。
- `Shape`：调制波形形状，支持 `Sine / Square / Triangle / Ramp`。

目标输出保持和当前链路一致：

- 输出 `int16_t` 交织 IQ 数据：`I0, Q0, I1, Q1, ...`。
- 单个 complex sample 占 `4 Bytes`。
- AM 输出为复基带包络，`Q` 路为 `0`。
- 数据可直接交给现有 playback / save IQ 链路。

本方案是静态分析和数据反推结果，没有编译或运行验证。

## 2. 现有代码事实

当前 AM 生成链路在 `AmModulator::workerLoop()` 中完成：

```cpp
m_sampleRate = calculateAMSampleRate(params.Rate);
result.sampleRate = m_sampleRate;
int computeResult = GenerateAmWaveform(objId, &params, result.sampleRate, &iq, &len);
```

现有本地代码负责：

- 参数收口：`AM_Configuraion(...)`。
- 采样率选择：`calculateAMSampleRate(rate)`。
- 调用第三方 API 生成 IQ。

第三方头文件只暴露：

```cpp
struct AMModParams
{
    double Rate;
    double Depth;
    ModShape Shape;
};

int GenerateAmWaveform(int objId, const AMModParams *paramsIn,
                       double sampleRate, short **iq, int32_t *lenOut);
```

仓库中没有第三方 API 的实现源码，因此本方案对第三方内部算法的描述是基于接口、当前代码、调制理论和参考 CSV 的反推。

## 3. 参考数据结论

### 3.1 `data/amDefault.csv`

VSG60 导出参数：

- `Rate = 1 kHz`
- `Depth = 50%`
- `Shape = Sine`

CSV 特征：

- 行数：`16384`
- `Q = 0`
- `I` 最小值：`8192`
- `I` 最大值：`24576`
- `I` 平均值：`16384`
- 关键点：
  - `I[0] = 16384`
  - `I[4096] = 24576`
  - `I[8192] = 16384`
  - `I[12288] = 8192`

该数据与下式吻合：

```text
N = 16384
I[n] = round_or_nearest(16384 * (1 + 0.5 * sin(2*pi*n/N)))
Q[n] = 0
```

因此，VSG60 的正弦 AM 包络起点是均值并向上变化：

```text
shape(0) = 0
```

这和用户提供的 Matlab 示例不同。Matlab 示例中 `+3*pi/2` 会让正弦 AM 从最小包络开始。

### 3.2 `data/am1M.csv`

VSG60 导出参数：

- `Rate = 1 MHz`
- `Depth = 90%`
- `Shape = Triangle`

CSV 特征：

- 行数：`32`
- `Q = 0`
- `I` 最小值：`1638`
- `I` 最大值：`31130`
- `I` 平均值：`16384`
- 首点：`I[0] = 1638`
- 中点：`I[16] = 31130`

该数据与下式完全吻合：

```text
N = 32
triangle[n] =
    -1 + 4*n/N,  n <= N/2
     3 - 4*n/N,  n >  N/2

I[n] = round(16384 * (1 + 0.9 * triangle[n]))
Q[n] = 0
```

这说明：

- `Depth` 是标准调制指数百分比，`90% -> m = 0.9`。
- 基准缩放值为 `16384`。
- `Triangle` 起点是最小包络，线性上升到最大包络，再线性下降。
- VSG60 并不是固定每周期 `16384` 点；高 `Rate` 时会减少每周期点数。

### 3.3 `data/amSquare.csv`

VSG60 导出参数：

- `Rate = 1 kHz`
- `Depth = 50%`
- `Shape = Square`

CSV 特征：

- 行数：`16384`
- `Q = 0`
- `I` 最小值：`8192`
- `I` 最大值：`24576`
- `I` 平均值：`16384`
- 首点：`I[0] = 24576`
- 半周期边界：
  - `I[8191] = 24576`
  - `I[8192] = 8192`

该数据与下式完全吻合：

```text
N = 16384
square[n] =
    +1, 0 <= n <  N/2
    -1, N/2 <= n < N

I[n] = round(16384 * (1 + 0.5 * square[n]))
Q[n] = 0
```

这说明 VSG60 Square 首半周期为高电平，后半周期为低电平。

### 3.4 `data/amramp.csv`

VSG60 导出参数：

- `Rate = 1 kHz`
- `Depth = 50%`
- `Shape = Ramp`

CSV 特征：

- 行数：`16384`
- `Q = 0`
- `I` 最小值：`8192`
- `I` 最大值：`24575`
- `I` 平均值：`16383.5`
- 首点：`I[0] = 8192`
- 末点：`I[16383] = 24575`

该数据与下式完全吻合：

```text
N = 16384
ramp[n] = -1 + 2*n/N

I[n] = round(16384 * (1 + 0.5 * ramp[n]))
Q[n] = 0
```

这说明 VSG60 Ramp 是上升锯齿：从最小包络线性上升到接近最大包络，然后在周期边界跳回最小包络。

### 3.5 `data/am10M.csv`

VSG60 导出参数：

- `Rate = 10 MHz`
- `Depth = 50%`
- `Shape = Sine`

CSV 特征：

- 行数：`4`
- `Q = 0`
- 数据点：
  - `I[0] = 16384`
  - `I[1] = 24576`
  - `I[2] = 16384`
  - `I[3] = 8192`

该数据与下式完全吻合：

```text
N = 4
I[n] = round(16384 * (1 + 0.5 * sin(2*pi*n/N)))
Q[n] = 0
```

这说明高 `Rate` 时，VSG60 每周期点数会收口到 `4`，对应：

```text
Fs = Rate * N = 10 MHz * 4 = 40 MS/s
```

在 VSG60 最大采样率 `50 MS/s` 的前提下，这是 `floor_power_of_two(50M / 10M)` 的结果。

## 4. AM 原理选择：应生成复基带包络，而不是实载波

用户提供的 Matlab 示例是音频/通用信号里的 passband AM：

```text
s(t) = [1 + m * sin(2*pi*fm*t + phase)] * sin(2*pi*fc*t)
```

其中 `fc` 是载波频率。

发射机 ARB/IQ playback 链路不应这样做。当前 SGStudio 发射链路的 RF 频率由设备频率设置、DUC、混频或后端射频链路负责。AM 波形文件本身应表示复基带包络：

```text
x(t) = A * [1 + m * shape(2*pi*Rate*t)]
I(t) = x(t)
Q(t) = 0
```

换句话说，AM 生成器只负责“幅度随时间变化”，不负责再生成一个 `fc` 载波。

## 5. 波形数学模型

### 5.1 参数归一化

```text
rate = clamp(Rate, 1 Hz, 10 MHz)
depth = clamp(Depth, 1%, 100%)
m = depth / 100
```

### 5.2 输出标度

参考 CSV 显示，VSG60 AM 使用：

```text
baseScale = 16384
```

因此：

```text
Depth = 50%:
  min = 16384 * (1 - 0.5) = 8192
  max = 16384 * (1 + 0.5) = 24576

Depth = 90%:
  min = 16384 * (1 - 0.9) = 1638.4
  max = 16384 * (1 + 0.9) = 31129.6
```

这与两个 CSV 的幅度范围一致。

### 5.3 通用公式

设单周期 complex sample 数为 `N`：

```text
phase[n] = 2*pi*n/N
shapeValue[n] = shape(phase[n])  // 范围 [-1, 1]
envelope[n] = 1 + m * shapeValue[n]
I[n] = quantize(baseScale * envelope[n])
Q[n] = 0
```

输出向量：

```text
iq[2*n]     = I[n]
iq[2*n + 1] = 0
```

## 6. Shape 定义

### 6.1 Sine

```text
shape(phase) = sin(phase)
```

参考特征：

- 起点为均值：`sin(0) = 0`。
- 1/4 周期为最大值。
- 1/2 周期回到均值。
- 3/4 周期为最小值。

### 6.2 Triangle

参考 `am1M.csv`，Triangle 起点为最小值，半周期到最大值：

```text
u = n / N

if u <= 0.5:
    shape = -1 + 4*u
else:
    shape = 3 - 4*u
```

等价离散形式：

```text
if n <= N/2:
    shape = -1 + 4*n/N
else:
    shape = 3 - 4*n/N
```

这个定义和 `asin(sin(phase))` 版本存在相位差。为了匹配 VSG60 CSV，应使用上面的最小值起点定义。

### 6.3 Square

参考 `amSquare.csv`，Square 首半周期为高电平，后半周期为低电平：

```text
shape = +1, 0 <= phase < pi
shape = -1, pi <= phase < 2*pi
```

### 6.4 Ramp

参考 `amramp.csv`，Ramp 是上升锯齿，从最小包络开始，线性上升到接近最大包络，然后在周期边界跳回最小值：

```text
shape = -1 + 2*n/N
```

## 7. 采样率与每周期点数策略

### 7.1 当前 SGStudio 策略

当前 `calculateAMSampleRate(rate)` 的核心关系是：

```text
Fs = 4 * Rate * k
```

并在硬件合法范围内优先取最大值：

```text
DATA_SAMPLE_RATE_MIN = 195.3125 kS/s
DATA_SAMPLE_RATE_MAX = 125 MS/s
```

因此当：

```text
Rate = 1 kHz
```

当前策略会倾向：

```text
Fs = 125 MS/s
N = Fs / Rate = 125000 samples/period
```

这个策略背后的考虑大概率是：

- 尽量利用硬件允许的最高 DAC/ARB 采样率。
- 提高包络时间分辨率。
- 对 Triangle / Ramp / Square 这类含尖点或跳变的形状，保留更多高频分量。
- 让低速 AM 的包络非常平滑，减少离散采样造成的形状误差。
- 保持一个简单稳定的规则：只要不超过下载大小，就尽量高采样率生成。

这个策略在“追求最高波形保真度”时是合理的，但代价是：

- 低 `Rate` 时每周期点数非常大。
- 生成、保存、下载和预览成本更高。
- 对 AM 包络来说，很多样点可能并不会带来可观的射频输出收益。

### 7.2 VSG60-compatible 策略

结合当前 CSV 和用户确认信息，VSG60 的 AM 采样率策略可按以下确定口径理解：

```text
FsMax = 50 MS/s
MaxSamplesPerPeriod = 16384
MinSamplesPerPeriod = 4
```

每周期点数选择：

```text
N_raw = floor_power_of_two(FsMax / Rate)
N = clamp(N_raw, MinSamplesPerPeriod, MaxSamplesPerPeriod)
Fs = Rate * N
```

验证：

```text
Rate = 1 kHz:
  floor_power_of_two(50e6 / 1e3) = 32768
  min(32768, 16384) = 16384
  Fs = 16.384 MS/s

Rate = 1 MHz:
  floor_power_of_two(50e6 / 1e6) = 32
  min(32, 16384) = 32
  Fs = 32 MS/s

Rate = 10 MHz:
  floor_power_of_two(50e6 / 10e6) = 4
  Fs = 40 MS/s
```

### 7.3 VSG60 策略是否适合发射机播放

如果目标是“在发射机上稳定播放 AM 包络”，VSG60 这种策略整体合理，原因是：

- AM 波形是低频包络，不是最终 RF 载波。
- RF 搬移由硬件完成，不需要在 IQ 文件里用极高采样率重建 `fc`。
- `1 kHz` AM 用 `16384` 点一周期已经非常细，继续增加到 `125000` 点对可观测 RF 包络改善有限。
- `1 MHz` Triangle 用 `32` 点一周期，对基本三角包络已经可以准确表达线性上升/下降。
- `10 MHz` Sine 用 `4` 点一周期虽然已经是最低可表达点数，但它正好落在正弦的 `0 / +1 / 0 / -1` 四个关键相位上，可用于循环播放。
- power-of-two 点数对循环播放、缓存、FFT 预览和设备内部处理都更友好。
- 波形文件更小，下载更快，UI 预览和保存压力更低。

但迁移到本发射机时有两个额外边界：

1. 本发射机最大采样率是 `125 MS/s`，高于 VSG60 的 `50 MS/s`。如果使用 `125 MS/s` 作为 `FsMax`，`Rate = 1 MHz` 会得到 `N = 64`，`Rate = 10 MHz` 会得到 `N = 8`，会更高保真，但不再匹配 VSG60 导出数据。
2. 本发射机当前通用最小采样率是 `195.3125 kS/s`。若严格固定 `MaxSamplesPerPeriod = 16384`，则当：

```text
Rate < 195312.5 / 16384 ~= 11.9209 Hz
```

会出现：

```text
Fs = Rate * 16384 < 195.3125 kS/s
```

也就是 `Rate` 仍在当前 AM 参数范围内，但生成出来的采样率低于本发射机合法下限。

因此，`MaxSamplesPerPeriod = 16384` 作为 VSG60-compatible 的常规上限是合理的；但如果本发射机仍要支持 `1 Hz ~ 10 MHz` 的完整 AM `Rate` 范围，并且要求实际调制率准确，低于约 `11.9209 Hz` 时需要额外低率保护策略。

可选低率保护策略：

- 对 `Rate < 11.9209 Hz`，临时允许 `N > 16384`，选择 `N = ceil_power_of_two(DATA_SAMPLE_RATE_MIN / Rate)`，同时受 `MAXDOWNLOADSIZE / 4` 约束。
- 或者在 `Vsg60Exact` 策略下把 AM `Rate` 最小值收口到约 `11.9209 Hz`。
- 或者对超低 `Rate` 回退到当前 `calculateAMSampleRate(rate)` 的高采样率策略。

推荐结论：

- 如果目标是匹配 VSG60 导出和第三方 API 行为，采用 `Vsg60Exact` 策略：`FsMax = 50 MS/s`、`MaxSamplesPerPeriod = 16384`、`MinSamplesPerPeriod = 4`。
- 如果目标是利用本发射机 `125 MS/s` 能力获得更高包络分辨率，可增加 `Tx125Quality` 策略：`FsMax = 125 MS/s`，其输出不会与 VSG60 CSV 完全一致。
- 如果目标是产品默认 playback 体验，建议默认使用 `Vsg60Exact` 或在低 `Rate` 下自动切到低率保护策略。

## 8. 推荐实现策略

### 8.1 策略分层

建议把采样率策略显式拆开，避免把“匹配 VSG60”与“利用本发射机最高采样率”混在一个函数里：

```text
Vsg60Exact:
  FsMax = 50 MS/s
  MaxSamplesPerPeriod = 16384
  MinSamplesPerPeriod = 4
  目标是匹配 VSG60 导出 CSV 和第三方 API 行为。

Tx125Quality:
  FsMax = 125 MS/s
  MaxSamplesPerPeriod 可仍保持 16384
  MinSamplesPerPeriod = 4
  目标是利用本发射机硬件能力提高高 Rate 下的包络采样点数。

CurrentMaxRate:
  沿用 calculateAMSampleRate(rate)
  目标是保持当前 SGStudio 既有行为。
```

### 8.2 VSG60Exact 采样率选择

推荐先实现 `Vsg60Exact`：

```text
MaxSamplesPerPeriod = 16384
MinSamplesPerPeriod = 4
FsMaxForVsg60Exact = 50e6
```

伪代码：

```cpp
int floorPowerOfTwo(double value)
{
    if (value < 1.0) {
        return 1;
    }

    int result = 1;
    while (result <= value / 2.0) {
        result *= 2;
    }
    return result;
}

int calculateVsg60AmSamplesPerPeriod(double rate)
{
    constexpr int kMaxSamplesPerPeriod = 16384;
    constexpr int kMinSamplesPerPeriod = 4;
    constexpr double kCompatFsMax = 50e6;

    int n = floorPowerOfTwo(kCompatFsMax / rate);
    n = std::min(n, kMaxSamplesPerPeriod);
    n = std::max(n, kMinSamplesPerPeriod);
    return n;
}

double calculateVsg60AmSampleRate(double rate)
{
    return rate * calculateVsg60AmSamplesPerPeriod(rate);
}
```

如果要使用当前硬件上限 `125 MS/s`，则 `Rate = 1 MHz` 会得到 `N = 64`，不能匹配 `am1M.csv`。因此要匹配 VSG60 CSV，应使用兼容上限 `50 MS/s` 或继续通过更多 CSV 校正该常量。

面向本发射机的低率保护可在外层补充：

```cpp
int calculateTxSafeAmSamplesPerPeriod(double rate, int preferredN)
{
    constexpr double kTxMinFs = DATA_SAMPLE_RATE_MIN;
    constexpr quint64 kMaxComplexSamples = Analog::MAXDOWNLOADSIZE / 4;

    if (rate * preferredN >= kTxMinFs) {
        return preferredN;
    }

    int n = ceilPowerOfTwo(kTxMinFs / rate);
    n = std::min<quint64>(n, kMaxComplexSamples);
    return n;
}
```

这段逻辑不属于 VSG60 精确匹配，而是本发射机合法采样率下限带来的工程收口。

### 8.3 AM 波形生成伪代码

```cpp
double amShapeValue(ModShape shape, qint64 n, qint64 N)
{
    const double u = static_cast<double>(n) / static_cast<double>(N);

    switch (shape) {
    case Sine:
        return std::sin(2.0 * M_PI * u);

    case Triangle:
        return (u <= 0.5)
            ? (-1.0 + 4.0 * u)
            : ( 3.0 - 4.0 * u);

    case Square:
        return (u < 0.5) ? 1.0 : -1.0;

    case Ramp:
        return -1.0 + 2.0 * u;
    }

    return std::sin(2.0 * M_PI * u);
}

int16_t quantizeAmI(double value)
{
    const long rounded = std::lround(value);
    const long clipped = std::max<long>(-32768, std::min<long>(32767, rounded));
    return static_cast<int16_t>(clipped);
}

QVector<int16_t> generateAmIq(const AMModParams &params, qint64 samplesPerPeriod)
{
    constexpr double kBaseScale = 16384.0;

    const double depth = std::max(1.0, std::min(params.Depth, 100.0));
    const double m = depth / 100.0;
    const qint64 N = std::max<qint64>(1, samplesPerPeriod);

    QVector<int16_t> iq;
    iq.resize(static_cast<int>(N * 2));

    for (qint64 n = 0; n < N; ++n) {
        const double shape = amShapeValue(params.Shape, n, N);
        const double envelope = 1.0 + m * shape;
        const double iValue = kBaseScale * envelope;

        iq[2 * n] = quantizeAmI(iValue);
        iq[2 * n + 1] = 0;
    }

    return iq;
}
```

### 8.4 关于 `Depth = 100%`

按公式：

```text
max = 16384 * (1 + 1) = 32768
```

`int16_t` 最大值是 `32767`，因此必须裁剪：

```text
32768 -> 32767
```

这会让 `Depth=100%` 的正峰值少 1 LSB，属于正常量化边界。

## 9. 验证计划

### 9.1 对齐 `amDefault.csv`

输入：

```text
Rate = 1 kHz
Depth = 50%
Shape = Sine
Policy = Vsg60Exact
```

期望：

```text
N = 16384
sampleRate = 16.384 MS/s
Q 全 0
I min = 8192
I max = 24576
I avg = 16384
I[0] = 16384
I[4096] = 24576
I[8192] = 16384
I[12288] = 8192
```

### 9.2 对齐 `am1M.csv`

输入：

```text
Rate = 1 MHz
Depth = 90%
Shape = Triangle
Policy = Vsg60Exact
```

期望：

```text
N = 32
sampleRate = 32 MS/s
Q 全 0
I[0] = 1638
I[8] = 16384
I[16] = 31130
I[24] = 16384
I[31] = 3482
```

### 9.3 对齐 `amSquare.csv`

输入：

```text
Rate = 1 kHz, Depth = 50%, Shape = Square
```

期望：

```text
N = 16384
sampleRate = 16.384 MS/s
Q 全 0
I[0] = 24576
I[8191] = 24576
I[8192] = 8192
I[16383] = 8192
```

### 9.4 对齐 `amramp.csv`

输入：

```text
Rate = 1 kHz, Depth = 50%, Shape = Ramp
```

期望：

```text
N = 16384
sampleRate = 16.384 MS/s
Q 全 0
I[0] = 8192
I[8192] = 16384
I[16383] = 24575
```

### 9.5 对齐 `am10M.csv`

输入：

```text
Rate = 10 MHz, Depth = 50%, Shape = Sine
```

期望：

```text
N = 4
sampleRate = 40 MS/s
Q 全 0
I = [16384, 24576, 16384, 8192]
```

### 9.6 仍建议补充的边界样本

Square / Ramp / 高 Rate 收口已经由当前 CSV 固定。后续若要覆盖本发射机低率边界，建议补充：

```text
Rate = 1 Hz, Depth = 50%, Shape = Sine
Rate = 10 Hz, Depth = 50%, Shape = Sine
Rate = 100 kHz, Depth = 50%, Shape = Sine
```

这些样本主要用于确认 VSG60 低采样率能力和低 `Rate` 下是否也坚持 `MaxSamplesPerPeriod = 16384`。

## 10. 推荐落地顺序

1. 保留当前第三方 API 路径，新增本地 AM 生成 helper。
2. 用 `amDefault.csv` 和 `am1M.csv` 写离线单元/工具级对比，不依赖设备。
3. 在 debug 开关下允许 `AmModulator` 切换第三方 API 和本地算法。
4. 使用 `amSquare.csv`、`amramp.csv`、`am10M.csv` 固定 Square/Ramp 相位和高 Rate 收口。
5. 如果本地算法与参考数据误差稳定在 1 LSB 内，再考虑替换第三方 API。

## 11. 当前建议结论

本地 AM 算法的核心应采用：

```text
I[n] = quantize(16384 * (1 + Depth/100 * shape[n]))
Q[n] = 0
```

采样率策略建议拆成三种：

```text
Vsg60Exact:
  使用 50 MS/s 上限、power-of-two 每周期点数、16384 最大点数、4 最小点数。

Tx125Quality:
  使用本发射机 125 MS/s 上限，保留同样的 shape / AM 公式，但输出不再逐点匹配 VSG60。

CurrentMaxRate:
  沿用 calculateAMSampleRate(rate)，保留当前 SGStudio 行为。
```

如果产品目标是发射机稳定 playback，且希望更接近 VSG60 行为，`Vsg60Exact` 更合理；如果目标是尽可能锐利的包络边沿和最高时间分辨率，则 `Tx125Quality` 或当前 `125 MS/s` 优先策略更保守。`MaxSamplesPerPeriod = 16384` 对常规 AM 播放是合理上限，但在本发射机 `Rate < 11.9209 Hz` 的低率场景下，需要额外处理最小采样率约束。
