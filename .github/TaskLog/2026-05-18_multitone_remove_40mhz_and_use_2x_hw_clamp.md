# 2026-05-18 Multitone Remove 40MHz And Use 2x Hardware Clamp

## 背景

- 上一轮把 Multitone 改成了 2 倍过采样。
- 复核后确认：当前 SGStudio 本地可见的设备采样率约束只有连续区间 `[DATA_SAMPLE_RATE_MIN, DATA_SAMPLE_RATE_MAX] = [195.3125 kHz, 125 MHz]`。
- `Utils::checkSampleRate()` / `Utils::clampSampleRate()` 也只对这两个边界负责，没有额外的 40 MHz 离散产品限制。

## 本轮结论

1. 对当前 Multitone 这种复基带离散多音，按 RF/数字信号处理的常见工程口径，`Fs >= 2 * occupied_bandwidth` 已是可工作的下界。
2. 当前 occupied bandwidth 可按 `B ≈ (Count - 1) * FreqSpacing` 处理，因此只要满足：

   `Fs >= 2 * FreqSpacing * (Count - 1)`

   就满足理想复基带 Nyquist 条件。
3. 由于当前设备采样率还有最小值 `DATA_SAMPLE_RATE_MIN`，最终实现不应直接返回裸 `2x` 结果，而应收口为：

   `Fs = clampSampleRate(2 * FreqSpacing * (Count - 1))`
