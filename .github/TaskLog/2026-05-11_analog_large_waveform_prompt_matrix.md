# Analog 大波形提示可达性矩阵

## 搜索范围

基于 `showLargeWaveformPrompt` 与相同提示文案 `too large,excepted size` 搜索 `src/plugins/analog/`。

命中的实际调制包括：

- AM
- FM
- Pulse
- Digital
- Multitone
- Ramp
- OFDM

## 结论

### 可删除/至少不应以 `>= MAXDOWNLOADSIZE` 弹提示的调制

1. AM
2. FM
3. Pulse
4. Multitone

这些调制在当前参数限制与采样率选择逻辑下，不会产生 **大于** `MAXDOWNLOADSIZE` 的估算值。
其中 AM / FM 在低速率边界可达到 **恰好等于** `MAXDOWNLOADSIZE`，若确认“等于上限可直接下载”已成立，则这些提示至少应改为 `>`，甚至可以整体移除。

### 仍然需要保留提示的调制

1. Digital
2. Ramp
3. OFDM

这些调制在当前参数限制下，仍然可以构造出 **严格大于** `MAXDOWNLOADSIZE` 的估算值。

## 逐项分析

### 1. AM

- 提示入口：`amplitudemodulation.cpp` 的 `Rate` 编辑路径
- 估算公式：`FMAM_EstimatedSize(rate, calculateAMSampleRate(rate))`
- 核心逻辑：`calculateAMSampleRate()` 在估算超限时，会把采样率压到 `fsUpper = min(STREAMING_SAMPLE_RATE_MAX, rate * MAXDOWNLOADSIZE / 4)` 以内
- 由于 `FMAM_EstimatedSize()` 的字节数是 `round(sampleRate / rate) * 4`，因此该路径估算值不会超过 `MAXDOWNLOADSIZE`
- 当 `rate <= 62.5e6 / 32768000` 时，可恰好取到 `MAXDOWNLOADSIZE`
- 结论：不会超过上限；提示无须因为“超限”存在

### 2. FM

- 提示入口：`frequencymodulation.cpp` 的 `Rate` 编辑路径
- 估算公式：`FMAM_EstimatedSize(rate, calculateFMSampleRate(rate, deviation))`
- `calculateFMSampleRate()` 在超限时会先尝试 streaming 区间内满足理论约束的偶数倍采样率
- 在当前参数约束 `rate <= 10MHz`、`deviation <= 10MHz` 下，streaming 区间内始终存在满足 `fs > 2 * (Rb + df)` 的偶数倍候选，因此不会落到返回 `STREAMING_SAMPLE_RATE_MAX` 的超限兜底
- 因而估算值不会超过 `MAXDOWNLOADSIZE`；低速率边界可恰好等于上限
- 结论：不会超过上限；提示无须因为“超限”存在

### 3. Pulse

- 提示入口：`pulsemodulation.cpp` 的 `Width` / `Period` 编辑路径
- 估算公式：`Pulse_EstimatedSize(width, period) = calcMaxSampleRate(width, period) * period * 4`
- 关键约束：
  - `period > 0.25` 时，`calcMaxSampleRate()` 直接返回 `10MHz`
  - `period <= 0.25` 时，采样率上限为 `125MHz`
- 因此最大估算值为：
  - `period > 0.25`：`10e6 * 1.0 * 4 = 40000000`
  - `period <= 0.25`：`125e6 * 0.25 * 4 = 125000000`
- 两者都严格小于 `MAXDOWNLOADSIZE = 131072000`
- 结论：提示不可达，可删除

### 4. Multitone

- 提示入口：`multitonemodulation.cpp` 的 `Count` 编辑路径
- 使用的是本地估算函数 `estimateMultitonePayloadSize(freqSpacing, count)`
- 公式可化简：
  - 奇数 `count`：字节数 = `64 * (count - 1)`
  - 偶数 `count`：字节数 = `128 * (count - 1)`
- `count <= 1000` 时，最大值为 `128 * 999 = 127872` 字节
- 与 `MAXDOWNLOADSIZE` 相比小了三个数量级
- 结论：提示不可达，可删除

### 5. Digital

- 提示入口：`digitalmodulation.cpp` 的 `PN` / `Oversample` 编辑路径
- 参数限制：`PN <= 24`，`oversample` 选项到 `32`
- 非 FSK 情况下，仅用估算公式：`2^PN * sps * 4`
- 例子：`PN = 24, sps = 4` 时，字节数已经是 `268435456`
- 结论：可严格超限，提示需要保留

### 6. Ramp

- 见单独文档：`2026-05-11_ramp_large_waveform_prompt_reachability.md`
- 结论：在高 `Span` + 长 `Period` 下，可严格超限，提示需要保留

### 7. OFDM

- 提示入口：`ofdmmodulation.cpp` 的 `FFTSize` / `SymbolCount` / `GuardInterval` 编辑路径
- 估算公式：`OFDM_EstimatedSize(fftSize, guardInterval, symbolCount) = fftSize * (1 + guardInterval / 100) * symbolCount * 4`
- 参数限制：
  - `FFTSize` 枚举包含 `16..2048`
  - `symbolCount <= 16384`
  - `guardInterval <= 100`
- 极值例子：`fftSize = 2048`, `guardInterval = 100`, `symbolCount = 16384`
  - 字节数 = `2048 * 2 * 16384 * 4 = 268435456`
- 即便 `fftSize = 1024`，同样可达到 `134217728`，已大于 `MAXDOWNLOADSIZE`
- 结论：可严格超限，提示需要保留

## 建议

如果后续要清理无效提示，优先处理：

1. 删除 Pulse / Multitone 的大波形提示
2. 删除 AM / FM 的大波形提示，或至少把判定从 `>= MAXDOWNLOADSIZE` 改为 `> MAXDOWNLOADSIZE`
3. 保留 Digital / Ramp / OFDM 的提示
