# AWGN 自研替换 GenerateAWGNWaveform 方案

## 1. 任务目标

为 `src/plugins/analog/awgnmodulator.cpp` 当前调用的第三方：

```cpp
GenerateAWGNWaveform(objId, &params, result.sampleRate, &iq, &len)
```

设计一套本地可实现的 AWGN 波形生成方案，目标是保持当前业务接口和下载链路不变：

- 输入仍使用 `AWGNParams::Bandwith` 和 `AWGNParams::Length`。
- 输出仍为 `int16_t` 交织 IQ：`I0, Q0, I1, Q1, ...`。
- `lenOut` 仍表示 `int16_t` word 数，而不是 complex sample 数。
- 单个 complex sample 占 `4 Bytes`。
- 不生成 RF carrier，中心频率搬移仍由设备频率/DUC/RF 链路负责。

本方案是静态分析和 `data/AWGN40M.csv` 反推结果，没有编译或运行验证。

## 2. 本次数据更正与成功标准

用户重新提供的 VSG60 参考参数为：

```text
Bandwidth = 40.000000 MHz
Length    = 10.000 ms
Seed      = 23
```

本次更新的范围：

- 重新分析 `data/AWGN40M.csv` 的时域统计与频域分布。
- 修正旧文档中基于错误数据得到的 LFM/chirp 算法假设。
- 给出更符合 AWGN、射频仪器实践和当前参数面的本地替换算法。
- 明确 `Seed` 字段的作用、是否需要，以及追求 VSG60 更高一致性时还需要哪些数据。

本次文档更新的成功标准：

- 算法不再把 AWGN 解释为 `Span/SweepTime/Period` 或常包络扫频。
- `Bandwith` 仍解释为复基带噪声总带宽。
- `Length` 仍解释为最终输出波形时长。
- 推荐算法生成带限复高斯随机噪声，而不是确定性 chirp。
- seed 语义被定义为“选择可复现随机实现”的 PRNG 初始状态，而不是带宽、功率或长度参数。

## 3. 当前代码事实

当前 AWGN 参数收口在 `AWGN_Configuraion(...)`：

```text
Bandwith: 50 kHz ~ DATA_SAMPLE_RATE_MAX / 1.25
Length:   100 us ~ MAXDOWNLOADSIZE / (targetFs * 4)
```

当前采样率选择在 `AwgnModulator::workerLoop()`：

```cpp
m_sampleRate = params.Bandwith * 1.25;
if (!Utils::checkSampleRate(m_sampleRate)) {
    m_sampleRate = Utils::clampSampleRate(m_sampleRate);
}
```

当前工程文档约定：

- `Bandwith` 是复基带 IQ 总带宽。
- `Length` 单位为秒。
- `targetFs = clamp(1.25 * Bandwith)`。

第三方头文件只暴露：

```cpp
struct AWGNParams
{
    double Bandwith; // 带宽，单位 Hz
    double Length;   // 头文件注释写 ms，但当前工程按秒使用
};

int GenerateAWGNWaveform(int objId, const AWGNParams *paramsIn,
                         double sampleRate, short **iq, int32_t *lenOut);
```

重要边界：

- `AWGNParams` 当前没有 `Seed` 字段。
- 仓库当前 AWGN property 面也只有 `Awgn_Bandwith` / `Awgn_Length`。
- 因此，如果不扩展参数面，本地替换只能使用内部固定 seed 或无 seed 的随机源，不能完整表达 VSG60 UI 上的 `Seed`。
- 仓库中没有第三方实现源码，因此对第三方内部算法的描述只能来自接口、当前代码、VSG60 导出 CSV 和 DSP/RF 理论反推。

## 4. 新 VSG60 参考数据结论

参考文件：

- `data/AWGN40M.csv`
- VSG60 截图参数：`Bandwidth = 40 MHz`、`Length = 10 ms`、`Seed = 23`

CSV 基本事实：

- 行数：`500000`
- 每行两列整数：`I,Q`
- `500000 / 10 ms = 50 Msps`
- `50 Msps = 1.25 * 40 MHz`
- 不存在 `0,0` 静默尾段：`zero rows = 0`

因此 VSG60 的该导出仍与当前工程采样率公式一致：

```text
Fs = 1.25 * Bandwith
```
### 4.1 时域统计

对全量 `500000` 个 complex samples 的统计结果：

```text
I: min=-29367, max=32340, mean=-8.715, std=6511.021, kurtosis=2.999
Q: min=-28595, max=31268, mean=13.384, std=6515.876, kurtosis=3.002
corr(I,Q) ~= -0.00021
full-scale component hits: 0
```

幅度分布：

```text
median(|I+jQ|) ~= 7664
p90(|I+jQ|)    ~= 13972
p99(|I+jQ|)    ~= 19769
p99.9(|I+jQ|)  ~= 24280
max(|I+jQ|)    ~= 32768
```

这些现象说明：

- I/Q 都接近零均值高斯分布，峰度约为高斯分布的 `3`。
- I/Q 零滞后相关性接近 `0`。
- 复幅度不是常量，而是符合复高斯噪声常见的 Rayleigh 型幅度分布。
- 该文件没有扫频门控、没有满幅常包络、没有周期静默段。

### 4.2 频域统计

以 `Fs = 50 Msps` 做 Welch 频谱估计：

```text
in-band |f| <= 20 MHz:
  p05 ~= -1.38 dB
  p50 ~=  0.00 dB
  p95 ~= +0.91 dB

edge 19 MHz ~ 21 MHz:
  p50 ~= -6.15 dB

out-of-band |f| > 20 MHz:
  median ~= -66.4 dB

power within +/-20 MHz ~= 99.52%
power within +/-21 MHz ~= 99.99%
```

这说明该文件是带限 AWGN：

- 频域主体覆盖 `[-Bandwith/2, +Bandwith/2]`。
- `1.25 * Bandwith` 的采样率给带外滚降留下了从 `20 MHz` 到 `25 MHz` 的 guard band。
- 时域相邻样点会因为带限滤波产生相关性，这不矛盾；带限白噪声不等于“每个输出采样点完全独立”。

### 4.3 修正结论
基于新数据，VSG60 该 AWGN 导出更符合：

```text
seed 驱动的伪随机复高斯噪声
+ 复基带低通/带限滤波
+ 固定 RMS/crest-factor 量级的 int16 量化
```

因此本地替换应实现统计意义上的带限复高斯 AWGN，而不是为了逐样本拟合旧 CSV 去生成 chirp。

## 5. 推荐替换语义

为了“完全替代当前 `GenerateAWGNWaveform` 调用点”，建议先实现一个兼容当前双参数面的 AWGN generator：

```text
Bandwith -> 复基带噪声总带宽
Length   -> 输出波形时长
Fs       -> clamp(1.25 * Bandwith)
```

也不应默认生成静默尾段。当前 VSG60 AWGN UI 的参数面是 `Bandwidth / Length / Seed`，不是 Ramp 的 `Span / SweepTime / Period`。

如果当前业务面暂时不扩展 `Seed`，推荐使用内部固定 seed：

```text
seed = 23
```

理由：

- 与 VSG60 默认示例一致。
- 同一组输入在本地实现内可复现，便于回归测试和用户复测。
- 比每次生成都取不可控随机源更符合 RF 仪器“同一配置可重复”的常见实践。

但这只是当前双参数面的保守落地方式。若产品希望对齐 VSG60 UI，应新增 `Awgn_Seed` property，并把 seed 写入保存/恢复/profile。

## 6. 波形生成算法

### 6.1 输入收口

沿用现有 `AWGN_Configuraion(...)`：

```text
bandwidth = clamp(params.Bandwith, 50 kHz, DATA_SAMPLE_RATE_MAX / 1.25)
sampleRate = Utils::clampSampleRate(1.25 * bandwidth)
length = clamp(params.Length, 100 us, MAXDOWNLOADSIZE / (sampleRate * 4))
```

生成函数内部仍应防护传入异常：

- `paramsIn == nullptr`
- `iq == nullptr`
- `lenOut == nullptr`
- `sampleRate <= 0`
- `Bandwith <= 0`
- `Length <= 0`
- `Nsamples <= 0`
- 输出字节数超过 `MAXDOWNLOADSIZE` 或 `int32_t` 可表达范围

### 6.2 采样点数

```text
N = round(sampleRate * Length)
```

为了 IQ 交织输出：

```text
lenOut = 2 * N
bytes  = lenOut * sizeof(int16_t)
```

对当前新参考文件：

```text
Bandwith = 40e6
Length   = 0.01
Fs       = 50e6
N        = 500000
lenOut   = 1000000 int16 words
bytes    = 2000000
```

当前 `AnalogPlaybackBusiness` 会在下载前对过短波形做重复补齐，因此 AWGN 生成器本身不需要额外重复周期。

### 6.3 随机源与高斯样本

生成复高斯白噪声：

```text
uI[n], uQ[n] ~ N(0, 1)
x[n] = uI[n] + j*uQ[n]
```

实现要求：

- 使用明确 seed 初始化 PRNG。
- 同一实现、同一 seed、同一参数必须得到同一段 IQ。
- 不同 seed 应得到统计等价但互相关很低的另一段噪声。
- seed 不应改变带宽、采样率、长度或目标功率。

如果需要跨平台 bit-stable，不能直接依赖 `std::normal_distribution` 的实现细节。更稳妥的做法是：

- 使用标准化的 PRNG engine，例如 `std::mt19937` 或自定义 PCG/Xoshiro。
- 自己实现 `uint32 -> (0,1)` 的 double 映射。
- 自己实现 Box-Muller 或其他固定高斯变换。

这样可以避免不同标准库版本下 `std::normal_distribution` 产生不同样本序列。

### 6.4 带限滤波

推荐的最小本地实现是“时域复高斯 + 复基带 FIR 低通”：

```text
passband target: |f| <= Bandwith / 2
guard band:      Bandwith / 2 .. sampleRate / 2
```

当 `sampleRate = 1.25 * Bandwith` 时：

```text
cutoff = Bandwith / 2 = 0.4 * sampleRate
Nyquist = 0.5 * sampleRate
```

也就是说，滤波器有 `0.1 * sampleRate` 的单边过渡带。以 `Bandwidth = 40 MHz` 为例，目标主体到 `20 MHz`，Nyquist 为 `25 MHz`，过渡带宽约 `5 MHz`。

工程实现可选：

1. 固定奇数长度 windowed-sinc / Kaiser FIR，例如 `129` taps 起步。
2. DC gain 归一化为 `1`。
3. 为避免起始 transient，额外生成至少 `tapCount - 1` 个预热样本，滤波后丢弃 group delay，再输出正好 `N` 个样本。
4. I/Q 作为同一组复样本的实部/虚部分别通过同一个实系数 FIR。

频域法也是可行方案：

- 在 FFT bin 上生成复高斯频域样本。
- 对 `|f| <= Bandwith/2` 保留，对过渡带加窗衰减，对带外置零。
- IFFT 得到时域复噪声。

频域法更容易精确控制频谱边界，但需要可靠 FFT 支持；时域 FIR 对当前替换任务更容易作为最小实现落地。

### 6.5 幅度归一化与量化

新 CSV 的组件 RMS 约为：

```text
std(I) ~= 6511
std(Q) ~= 6516
std(component) / 32767 ~= 0.199
```

因此推荐先按固定 RMS 口径对滤波后噪声缩放，而不是按每段 peak 做 autoscale：

```text
targetComponentRms = 0.2 * 32767
currentComponentRms = sqrt(mean((I - meanI)^2 + (Q - meanQ)^2) / 2)
scale = targetComponentRms / currentComponentRms
```

然后量化：

```text
Iout = round(scale * I)
Qout = round(scale * Q)
Iout,Qout -> clamp(-32768, 32767)
```

理由：

- 固定 RMS 更符合 AWGN 作为噪声功率载体的语义。
- seed 或 length 改变时，平均功率不应因为某次随机峰值大小不同而明显漂移。
- 新 CSV 中接近满幅的峰值可以由高斯噪声的 crest factor 自然解释，不需要满幅常包络或逐段 peak normalize。

最终仍保留饱和保护，但饱和不应成为正常工作路径。若验证发现频繁 clipping，应降低 `targetComponentRms`，而不是改回常包络。

### 6.6 不建议保留的旧算法内容

以下旧内容应从正式替换设计中移除：

- `fStart = -span/2 - df/2`
- `chirpRate = span / sweepTime`
- 二次相位 LFM 公式
- 4-sample `sin^2` 淡入淡出
- `SweepTime < Period` 的静默尾段
- 满幅常包络量化

这些属于旧错误数据的反推结果，与当前新 VSG60 AWGN 文件不一致。

## 7. Seed 字段语义

### 7.1 Seed 是否有必要

从 AWGN 的统计定义看，seed 不是描述噪声功率谱密度、带宽或采样率的物理参数；只要生成的是同分布随机噪声，不同 seed 都是合法 AWGN。

但从射频仪器和 DSP 工程实践看，seed 很有必要，尤其是 AWGN 作为 ARB/playback 波形下发时：

- 保证同一配置可以复现同一段噪声，便于 A/B 测试。
- 便于问题复盘、售后复测和自动化回归。
- 便于多次测量中保持相同干扰 realization，只改变被测对象或其他参数。
- 便于需要 decorrelation 的场景显式切换到另一组噪声 realization。

因此推荐结论是：

- 若只追求“统计正确的 AWGN”，seed 可以隐藏为内部固定值。
- 若追求 VSG60 UI/产品语义一致，seed 应作为 AWGN 参数暴露。
- 若追求跨机器、跨版本回归稳定，seed 必须参与算法，且 PRNG/高斯变换也要固定实现。

### 7.2 Seed 控制什么

seed 只控制随机序列的初始状态：

```text
same seed + same params + same implementation -> same IQ samples
different seed + same params                  -> different IQ samples, same statistics
```

seed 不应控制：

- `sampleRate`
- `Bandwith`
- `Length`
- filter cutoff
- target RMS
- RF output level

如果不同 seed 导致明显不同的平均功率或明显不同的带宽，那通常说明实现使用了 peak normalize、样本数太短、滤波 transient 处理不当，或统计验证口径不稳定。

### 7.3 还需要哪些数据

仅凭一个 `Seed = 23` 的 CSV，无法反推出 VSG60 的 bit-exact PRNG、Gaussian transform、滤波器系数和量化规则。

如果目标只是实现统计一致的 AWGN，本次数据已经足够支撑算法方向。

如果目标是更接近 VSG60，建议补充这些导出：

1. 同一参数、同一 seed 连续导出两次：确认 VSG60 是否 bit-exact 可复现。
2. 同一 `Bandwidth = 40 MHz`、`Length = 10 ms`，导出 `Seed = 1`、`Seed = 23`、`Seed = 24`：确认 seed 是否只改变 realization。
3. 同一 `Bandwidth = 40 MHz`、`Seed = 23`，导出不同长度，例如 `1 ms`、`5 ms`、`10 ms`：确认较短文件是否是长文件前缀，或是否每个长度重新归一化。
4. 同一 `Length = 10 ms`、`Seed = 23`，导出不同带宽，例如 `20 MHz`、`40 MHz`、`80 MHz`：反推滤波器过渡带和幅度归一化是否随带宽变化。
5. 如果 VSG60 有相关导出选项，确认是否存在 AutoScale、RMS、crest factor、noise power 或 clipping/headroom 设置。

如果要逐样本对齐，还需要 VSG60 明确或反推出：

- PRNG 类型。
- seed 取值范围与 `0` 的语义。
- uniform-to-Gaussian 变换。
- 带限滤波器或频域 shaping 规则。
- 缩放、rounding、clamp 的顺序。
- I/Q 输出顺序和文件量化格式。

## 8. 建议代码落点
若要产品层暴露 seed，则还需要：

- 新增 `Awgn_Seed` property。
- AWGN panel 显示并绑定 seed 编辑项。
- profile save/restore 写入 seed。
- HTRA fallback 与 Analog provider 的 AWGN 参数面保持一致。

## 9. 验证标准

静态/离线验证优先，不需要先上设备。

基础结构检查：

1. `Bandwith = 40e6`
2. `Length = 0.01`
3. `sampleRate = 50e6`
4. 输出 complex sample 数为 `500000`
5. `lenOut == 1000000`
6. `N * 2 * sizeof(int16_t) <= MAXDOWNLOADSIZE`

统计检查：

1. I/Q 均值接近 `0`。
2. I/Q 标准差接近 `0.2 * 32767`，允许根据最终滤波器与归一化策略设置容差。
3. I/Q 峰度接近 `3`。
4. `corr(I,Q)` 接近 `0`。
5. 幅度分布不应接近常量，应呈 Rayleigh 型分布。
6. 正常参数下不应出现大量 clipping。

频域检查：

1. 绝大部分功率落在 `[-Bandwith/2, +Bandwith/2]`。
2. `Bandwith/2` 到 `sampleRate/2` 是滤波器过渡/guard 区。
3. 带内功率谱应近似平坦，不应呈现单个 chirp 的确定性相位轨迹。
4. 频谱边缘的滚降可由 FIR/FFT shaping 策略决定，但应与 `1.25 * Bandwith` 的工程裕量一致。

seed 检查：

1. 同一 seed 与同一参数重复生成，IQ 应完全一致。
2. 不同 seed 与同一参数生成，IQ 不应完全一致。
3. 不同 seed 的 RMS、带宽、PSD 统计应保持同一量级。

对 `data/AWGN40M.csv` 的验证口径：

- 应做统计和频谱相似度验证。
- 不应要求与 VSG60 CSV 逐样本 `1~2 LSB` 级一致，除非后续拿到了 VSG60 的 PRNG、滤波器和缩放细节。

## 10. 风险与边界

- 该方案复现的是 VSG60 新数据体现出的“带限复高斯 AWGN”统计语义，不承诺 bit-exact 复刻 VSG60。
- 当前 `AWGNParams` 没有 seed；若不扩展参数面，只能采用内部固定 seed 或内部随机策略。
- 固定 RMS 口径需要和 RF level/playback 链路确认：外层 level 应继续负责最终射频输出功率，本地 AWGN generator 只负责稳定的数字波形归一化。
- FIR tap 数、窗口类型和归一化策略会影响边缘滚降；实现后需要用离线 PSD 检查调参。
- 若使用 `std::normal_distribution`，同一 seed 在不同标准库实现上可能不完全一致；需要跨平台 bit-stable 时应固定高斯变换。
- 当前 `src/plugins/htra/awgnmodulator.cpp` 若仍使用旧 LFM/chirp surrogate，则与本修正文档冲突；后续代码修复应以本文的带限复高斯方案为准。

