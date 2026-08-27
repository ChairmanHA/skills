# 2026-04-28 Digital Spectrum C++ Integration Plan

## 目标

- 解释 `data/16QAM.csv` 与 `data/16QAMOversample8.wav` 的本质差异。
- 明确在 C++ 中把“参数 -> IQ 波形 -> 频谱数据”整合到数字调制业务里的正确层级。
- 当前阶段只考虑数据准备，不涉及 UI 绘制细节。

## 当前事实

- `DigitalPanel` 只是 UI 绑定层，本身不生成波形；它只负责属性绑定和发出 `requestSaveData()`。
- 真实的数字调制数据源在 `DigitalModule::DigitalModulator`。
- `DigitalModulator::workerLoop()` 在后台线程里调用 `GenerateDigitalModWaveform()`，把结果写入 `m_complex`，并通过 `statusChanged()` 通知业务层。
- `DigitalModulation::onModulatorStatusChanged()` 当前只是把 `digitalModulator->data()` 和 `sampleRate()` 送进 `AnalogPlaybackBusiness::handleModulatorStatusChange()`。
- `DigitalModulator::computeWaveformSync()` 已经提供了同步生成接口，适合做离线验证或单元测试；真正在线业务生成仍由 `workerLoop()` 驱动。

## csv 与 wav 的差异

- `data/16QAMOversample8.wav` 更接近“当前生成器输出的原始 IQ”，样本峰值约在 `±24100`，没有大面积顶到 `±32767/32768`。
- `data/16QAM.csv` 不是同一份原始 IQ 的等价导出。它大约有三分之一的样本直接命中满幅，说明导出前或导出过程中发生了明显的裁剪、限幅或显示链路相关处理。
- 因此：
  - `csv` 不能直接当作“当前 C++ 生成器的原始 IQ”；
  - `csv` 更适合作为“VSG60 最终显示风格”的校准参考；
  - `wav` 才是集成到当前业务代码时应该直接使用的数据源形态。

## 想达到 VSG60 效果时，是否需要额外处理

- 需要，但 `csv` 和 `wav` 需要的额外处理不一样。

### csv

- 直接 FFT 会把带外抬到大约 `-55 dBFS`，明显不像 VSG60 截图。
- 为了逼近 VSG60 截图，需要先做一层轻度“重建式”平滑，再进 analyzer 频谱链。
- 当前本地验证里，较合适的经验参数是：
  - 零相位 IIR smoothing
  - cutoff 约 `1.7 MHz`
  - 2 次 forward/backward pass

### wav / 生成器原始 IQ

- 不需要 csv 那层“修复饱和边沿”的预处理。
- 但仍然需要 analyzer 风格的频谱显示链，而不是“直接一把普通 FFT 就上图”。
- 当前可接受链路是：
  - 长记录 FFT（当前用 `65536`）
  - Blackman-Nuttall 窗
  - coherent-gain 归一化
  - 按目标 RBW 做频域积分
  - 压缩到 `512` 个显示点
  - `max` reducer
  - 3-bin 轻微显示平滑

## C++ 集成层级建议

- 不建议把频谱计算逻辑放进 `DigitalPanel`。
- `DigitalPanel` 应继续只做 UI，不负责业务计算。
- 更合适的层级是 `DigitalModule::DigitalModulator`，原因：
  - 它已经拥有最终生成的 `result.iqData`；
  - 它已经有独立 worker thread；
  - 它已经负责参数变化后的重算和结果失效语义；
  - 频谱数据天然和“这次生成出的 IQ 数据”同生命周期。

## 推荐的数据流

1. 用户修改参数。
2. `DigitalModulator::*setter()` -> `configurationAndGenerateData()` -> `requestGenerateData()`。
3. `workerLoop()` 调用 `GenerateDigitalModWaveform()` 拿到 `DigitalWaveformResult`。
4. 若生成成功，在同一 worker 线程里直接基于 `result.iqData` 计算频谱数据。
5. 计算完成后，在一次受保护的提交里同时更新：
   - `m_complex`
   - `m_sampleRate`
   - `m_status`
   - `m_spectrumFreqHz`
   - `m_spectrumDbFs`
   - `m_spectrumRbwHz`
6. 然后再发出数据就绪信号给上层业务/UI。

## 推荐新增的数据结构

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

可选方案：

- 方案 A：给 `DigitalWaveformResult` 增加一个 `DigitalSpectrumResult spectrum;`
- 方案 B：`DigitalModulator` 单独缓存一份 spectrum，并新增 getter/signal

当前更推荐方案 B：

- 不会把已有 `DigitalWaveformResult` 用途扩得太重；
- 更便于后面做“只重绘频谱、不重新复制整段 IQ”的 UI 更新。

## 推荐新增接口

在 `DigitalModulator` 中新增：

```cpp
DigitalSpectrumResult spectrum() const;
```

新增信号：

```cpp
void spectrumDataChanged();
```

或者更直接：

```cpp
void spectrumReady(const DigitalSpectrumResult &result);
```

## 推荐的频谱计算函数边界

建议在 `digitalmodulator.cpp` 内部新增一个纯算法 helper，例如：

```cpp
static DigitalSpectrumResult buildAnalyzerSpectrum(
    const QVector<qint16> &iqData,
    double sampleRate,
    int displayPoints = 512,
    int analysisFftSize = 65536);
```

当前生成器在线路径默认直接喂原始 `iqData`，不要套 csv 的额外平滑。

### 小样本退化策略

- `iqData` 是 IQ 交织 short，因此真正的复数样本数是 `iqData.size() / 2`。
- 当复数样本数不足 `65536` 时，`analysisFftSize` 不再强行取 `65536`，而是退化为“不超过样本数的最大 2 的幂”。
- 当 `analysisFftSize < 512` 时，显示点数也同步降为 `analysisFftSize`，避免为了凑满 `512` 点而人为插值出假的频谱细节。
- 也就是说：
  - 样本充足时：`analysisFftSize = min(65536, power_of_two_floor(complexSampleCount))`，`displayPoints = 512`
  - 样本不足时：`displayPoints = analysisFftSize`

若未来要支持“模拟 VSG60 导出 csv 的显示味道”，再单独加可选 source flavor：

```cpp
enum class SpectrumSourceFlavor {
    GeneratedIq,
    CsvExportLike,
};
```

## 为什么不建议先从 QVector<float> data() 开始

- `data()` 当前只是把内部 `QVector<int16_t>` 再复制成 `QVector<float>`，并没有真正归一化到 `[-1, 1]`。
- 对频谱计算来说，这一步既多一次拷贝，也没有信息增益。
- 最直接的做法是使用 `result.iqData` 或内部 `m_complex` 原始 `qint16` 数据。

## 当前结论

- `csv` 更像校准样本，不像当前业务应直接使用的原始 IQ。
- 真正整合进数字调制业务时，应基于 `DigitalModulator` 生成出来的原始 IQ（即更接近 wav 的那一路）直接计算频谱。
- 若目标是“先把数据准备好，后面 UI 再接”，最正确的落点是在 `DigitalModulator` worker 线程里，于波形生成成功后立即顺手计算并缓存 `DigitalSpectrumResult`。