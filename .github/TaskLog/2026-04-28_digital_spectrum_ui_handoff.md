# 2026-04-28 Digital Spectrum UI Handoff

## 目的

- 记录当前数字调制频谱数据准备层的实际实现状态。
- 为下一步 UI 对接提供稳定的数据流说明、接口说明和边界约束。
- 当前文档只覆盖“生成并缓存频谱数据”，不覆盖 UI 绘制细节。

## 当前实现状态

频谱计算已经直接落在 `DigitalModule::DigitalModulator::workerLoop()` 内部。

当前行为是：

1. 参数变化后，仍然走现有链路：`setter -> configurationAndGenerateData() -> requestGenerateData()`。
2. `requestGenerateData()` 会把当前波形状态和频谱状态一起清空，标记 `status = 0`，然后唤醒 worker 线程重算。
3. `workerLoop()` 调用 `GenerateDigitalModWaveform()` 成功拿到 IQ 数据后，若期间没有新的参数变更，就在同一个 worker 线程里立即计算频谱。
4. 频谱和波形会在同一次提交里一起写入 `DigitalModulator` 内部缓存。
5. 写入完成后发出：
   - `statusChanged()`
   - `spectrumDataChanged()`

失败路径下：

- `errorOccurred()` 会照常发出。
- 频谱缓存会被清空，避免 UI 继续显示旧数据。

## 已落地的代码位置

- `DigitalSpectrumResult`：`src/plugins/analog/digitalmodulator.h`
- `DigitalModulator::spectrum() const`：`src/plugins/analog/digitalmodulator.h/.cpp`
- `DigitalModulator::spectrumDataChanged()`：`src/plugins/analog/digitalmodulator.h`
- `buildAnalyzerSpectrum(...)`：`src/plugins/analog/digitalmodulator.cpp`
- 频谱缓存清理：`DigitalModulator::requestGenerateData()`
- 频谱计算与提交：`DigitalModulator::workerLoop()`

## 当前对外数据接口

### 频谱结果结构

```cpp
struct DigitalSpectrumResult
{
    QVector<double> freqHz;
    QVector<double> levelDbFs;
    double rbwHz = 0.0;
    int displayPoints = 0;
    int analysisFftSize = 0;
};
```

字段含义：

- `freqHz`：最终可直接绘图的频率轴，范围为 `[-Fs/2, +Fs/2]`。
- `levelDbFs`：与 `freqHz` 一一对应的 dBFS 频谱值。
- `rbwHz`：当前显示链对应的 RBW。
- `displayPoints`：最终输出给 UI 的显示点数。
- `analysisFftSize`：内部实际用于计算的 FFT 点数。

### 读取接口

```cpp
DigitalSpectrumResult spectrum() const;
```

说明：

- 该接口返回的是快照副本，内部带锁，适合 UI 线程直接读取。
- 当前设计有意不暴露内部缓存引用，避免 UI 持有跨线程悬空数据。

### 更新信号

```cpp
void spectrumDataChanged();
```

说明：

- 该信号表示“当前参数对应的频谱缓存已经就绪”。
- UI 不应该在任意时刻主动猜测何时取数，而应在收到该信号后调用 `spectrum()` 取快照。

## 当前频谱算法实现

当前实现使用的是 analyzer 风格显示链，而不是简单单窗 FFT：

1. 输入数据为 `QVector<qint16>`，按 IQ 交织解释。
2. 将交织 short 还原为复数样点。
3. 对复数样点应用 `Blackman-Nuttall` 窗。
4. 做一次复数 FFT。
5. 以 coherent-gain 做归一化。
6. 先得到线性功率谱，再按目标 RBW 做频域积分。
7. 将高分辨率频谱压缩为最终显示点数。
8. 对最终 dBFS 轨迹做一次轻微平滑。

当前固定参数：

- 优先分析 FFT 点数：`65536`
- 默认显示点数：`512`
- 窗函数：`Blackman-Nuttall`
- RBW 系数：`1.8851`
- display reducer：`max`
- display smoothing：`3 bin`
- display offset：`+6.0 dB`

## 通过 WAV 生成 VSG60 类似频谱的过程

这部分对应的是已经验证过的 `wav -> analyzer display trace` 路径，目标不是“数学上最标准的普通 FFT”，而是“视觉效果和 VSG60 更接近的显示链”。

### 输入假设

当前实现假设输入 wav 满足以下条件：

- 16-bit
- 2 通道
- I/Q 交织存储
- wav header 中的 `framerate` 作为频谱计算使用的采样率

对于当前校准样本 `data/16QAMOversample8.wav`，实际使用的信息是：

- 采样率：`8 MHz`
- 调制：`QAM16`
- 滤波：`RootRaisedCosine`
- alpha：`0.35`
- filter length：`16`
- oversample：`8`

这些业务字段主要用于理解波形来源；真正画频谱时，核心输入只有两项：

- IQ 样本序列
- sampleRate

### 处理步骤

1. 从 wav 中读取交织 IQ short，并还原成复数样点序列。
2. 不对 wav 额外做 csv 那种“重建式平滑”或饱和修复。
3. 根据样点数选择分析 FFT 点数：
    - 默认目标是 `65536`
    - 如果样点不够，则退化到“不超过当前复样点数的最大 2 次幂”
4. 根据分析 FFT 点数选择最终显示点数：
    - 样点充足时固定为 `512`
    - 小样本时退化为 `analysisFftSize`
5. 对这一段复数样点施加 `Blackman-Nuttall` 窗。
6. 计算一次复数 FFT，并做 `fftshift` 语义上的重排，使频谱中心落在 0 Hz。
7. 使用 coherent-gain 做幅度归一化，得到线性功率谱。
8. 根据目标 RBW 做频域积分，而不是直接拿单个 FFT bin 上图。
9. 将高分辨率频谱压缩成最终显示点数，当前使用 `max reducer`。
10. 转成 dBFS 后，再做一次 `3 bin` 的轻微显示平滑。
11. 最后施加 `+6.0 dB` 的显示校准偏移，得到更接近 VSG60 的峰值高度和平台视觉效果。

### 为什么 wav 不需要额外源修复

这一点和 csv 不同。

- `wav` 更接近当前生成器原始输出的 IQ。
- 它没有 csv 那种明显的大面积满幅饱和边沿。
- 因此 wav 这一路的问题不在“源数据已经被裁剪，需要先修复”，而在“如何把原始 IQ 变成接近仪器显示风格的谱线”。

换句话说：

- csv 需要“源适配 + analyzer 显示链”
- wav 只需要“analyzer 显示链”

### 当前用于逼近 VSG60 的关键参数

- `analysisFftSize = 65536`（样本足够时）
- `displayPoints = 512`
- `window = Blackman-Nuttall`
- `RBW factor = 1.8851`
- `display reducer = max`
- `video smoothing = 3 bins`
- `display offset = +6.0 dB`

当 `sampleRate = 8 MHz` 且 `displayPoints = 512` 时：

- `RBW = Fs / 512 * 1.8851`
- 结果约为 `29.4546875 kHz`

这和前面用于校准 VSG60 截图的 RBW 是一致的。

### 输出结果的含义

最终给 UI 的不是“原始 FFT 复数结果”，而是已经可以直接用于绘图的显示轨迹：

- `freqHz`：从 `-Fs/2` 到 `+Fs/2` 的频率轴
- `levelDbFs`：与之对应的显示电平
- `rbwHz`：用于图例或状态栏显示
- `displayPoints`：实际输出点数
- `analysisFftSize`：内部分析分辨率

因此 UI 侧不需要再自己做：

- window
- FFT
- fftshift
- RBW 积分
- reducer
- smoothing

UI 只需要消费最终结果并绘制。

## 小样本退化策略

这部分是本次实现里最关键的边界处理。

因为有些数字波形样本点数会比较少，所以当前实现没有把 FFT 点数写死，而是做了自适应：

- `iqData` 是 IQ 交织 short，因此复数样点数等于 `iqData.size() / 2`。
- `analysisFftSize = floor_pow2(min(65536, complexSampleCount))`
- `displayPoints = min(512, analysisFftSize)`

这样做的意义：

- 样本足够时，仍然走 `65536 -> 512` 的标准 analyzer 风格显示链。
- 样本不足时，FFT 点数自动退化到当前数据量允许的最大 2 次幂。
- 如果 FFT 点数本身已经小于 `512`，显示点数也同步下降，避免 UI 收到“伪造出来的细分频点”。

换句话说，当前实现优先保证“数据真实性”，而不是机械维持固定 512 点显示。

## 线程与状态语义

### requestGenerateData()

当前在请求重算时会清理以下状态：

- `m_status = 0`
- `m_complex.clear()`
- `m_spectrumFreqHz.clear()`
- `m_spectrumLevelDbFs.clear()`
- `m_spectrumRbwHz = 0.0`
- `m_spectrumDisplayPoints = 0`
- `m_spectrumAnalysisFftSize = 0`

这意味着：

- 一旦参数变化，旧波形和旧频谱都会立刻失效。
- UI 不应该在 `status = 0` 时继续显示“当前参数仍然有效”的频谱。

### workerLoop()

当前逻辑分三段：

1. 拿当前 profile，调用底层 API 生成 IQ。
2. 若生成过程中参数又变了，当前结果直接丢弃，不提交缓存。
3. 若生成完成后参数仍然没变，则计算频谱并一次性提交缓存。

这样保证了：

- UI 拿到的波形和频谱始终来自同一组参数。
- 不会出现“波形是新参数、频谱还是旧参数”的混搭状态。

## 对 UI 的推荐接法

下一步 UI 对接时，建议不要直接在 `DigitalPanel` 里计算任何频谱，只做消费。

推荐接法：

1. 在 `DigitalModulation` 增加一个槽，例如：

```cpp
void onSpectrumDataChanged();
```

2. 连接：

```cpp
connect(digitalModulator,
        &DigitalModule::DigitalModulator::spectrumDataChanged,
        this,
        &DigitalModulation::onSpectrumDataChanged);
```

3. 在槽里调用：

```cpp
const auto spectrum = digitalModulator->spectrum();
```

4. 把 `freqHz / levelDbFs / rbwHz / displayPoints / analysisFftSize` 转发给 panel 或 plot model。

### UI 层建议遵守的规则

- 收到 `spectrumDataChanged()` 后再拉取数据，不要轮询。
- 若 `spectrum.freqHz.isEmpty()` 或 `spectrum.levelDbFs.isEmpty()`，按“暂无有效频谱”处理。
- 不要假设频谱点数永远是 `512`；必须按 `spectrum.displayPoints` 或数组实际长度绘图。
- X 轴不要自己重建，直接使用 `spectrum.freqHz`。
- 图例里需要显示 RBW 时，直接使用 `spectrum.rbwHz`。

## 当前未做的事情

以下内容还没有实现，留给下一步 UI 对接：

- `DigitalModulation` 还没有接 `spectrumDataChanged()`。
- `DigitalPanel` 还没有频谱绘图控件的数据入口。
- 还没有定义“status = 0 时 UI 显示 loading / clear / keep previous”的最终交互策略。
- 还没有做绘图节流；如果后面参数编辑会高频触发，可在 UI 层按需做合并刷新。

## 建议的下一步

下一步建议按下面顺序推进：

1. 在 `DigitalModulation` 增加 `onSpectrumDataChanged()`，先把频谱数据从 modulator 转出来。
2. 为 `DigitalPanel` 或对应 plot model 设计一个明确的更新接口，例如 `setSpectrumData(...)`。
3. UI 首版先只做静态折线更新，不要一开始就加入 marker、peak search、平均保持等附加功能。
4. UI 完成基础接通后，再决定 `status=0` 时是清空图、显示 loading，还是暂时保留旧图。

## 当前验证状态

当前实现已经通过 Debug 编译验证：

```powershell
cmake --build D:\development\vsg2.0\build\cmake-win-debug --config Debug --target ALL_BUILD --parallel
```

编译通过，说明本次新增的频谱结构、缓存、信号和 worker 内实现已经和现有数字调制链路正确接合。