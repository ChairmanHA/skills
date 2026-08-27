# 2026-05-06 Digital Spectrum Fixed 4000 FFT Plan

## 目标

- 说明当前 `DigitalModulator` 频谱分析点数策略。
- 将分析 FFT 策略改为固定 `4000` 个复数点。
- 当波形复数点数不足 `4000` 时，对分析输入尾部补零。
- 将最终绘制点数也固定为 `4000`，与分析点数保持一致。

## 当前实现

- `buildAnalyzerSpectrum()` 当前先取复数样本数 `complexSampleCount = iqData.size() / 2`。
- `analysisFftSize` 通过 `floorPowerOfTwo(min(65536, complexSampleCount))` 计算。
- 因此当前只有在样本充足时才会使用较大 FFT；小样本不会补零，而是退化到“不超过样本数的最大 2 次幂”。
- `displayPoints` 通过 `min(512, analysisFftSize)` 计算，所以小样本时 UI 也会同步降点数。

## 本次修改判断

- 固定 `4000` 点分析对当前频谱预览是合理的：
  - 频谱显示的分析长度、RBW 和计算开销会稳定。
  - 小样本补零后，频率轴和显示密度不再随波形长度抖动。
- 将绘制点数同步到 `4000` 也是合理的：
  - UI 侧只要求横纵轴数据等长，没有写死 `512` 点。
  - `QCustomPlot` 处理 `4000` 点折线没有压力。
  - 频谱显示会更接近“逐 FFT bin”而不是“压缩后的 analyzer trace”。
- 代价也明确：
  - 相比当前长记录 `65536` 点分析，长波形的细节分辨率会下降。
  - 这更像“固定预览规格”，而不是“尽可能高分辨率分析”。
  - 由于不再把 `4000` 个 bin 压缩到 `512` 点，曲线会更细、更直接，也会比原先更毛一些。

## 实现边界

- 现有 `fftInPlace()` 是 radix-2 Cooley-Tukey 实现，只适用于 `2^n` 长度。
- `4000` 不是 `2^n`，不能直接沿用当前实现，否则结果错误。
- 本次改动需要把频谱分析内部 FFT 改为支持任意长度的实现。
- 为了保持改动局部且不引入额外依赖，优先在 `digitalmodulator.cpp` 内实现一个固定长度可用的通用 DFT/FFT helper。

## 验证

- 先做文件级静态检查，确认 `digitalmodulator.cpp` 无新诊断。
- 不做额外运行验证，除非后续出现编译或逻辑告警。