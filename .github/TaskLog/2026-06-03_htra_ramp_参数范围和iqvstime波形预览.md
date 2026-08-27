
## 2026-06-03 参数范围收敛

- 当前 HTRA Ramp 的业务夹紧只有：
   - `Span: 1 MHz ~ 100 MHz`
   - `SweepTime/Period: 2 us ~ 1 s`
   - `SweepTime <= Period`
- 这个范围对“能算出来”是够的，但对“用户不容易撞到 125MiB 下载硬边界”并不合理，因为 `125MHz * 1s * 4B = 500MB`，只能靠后续降采样或报错兜底。
- 这次准备把用户可编辑范围收敛到更适合 Ramp 回放的区间：
   - `Span` 仍保持 `1 MHz ~ 100 MHz`，因为这符合当前复基带 chirp 语义和 `Fs >= 1.25 * Span` 的 Nyquist 裕量。
   - `SweepTime/Period` 改为 `2 us ~ maxPeriod(Span)`，且 `SweepTime <= Period`。
   - `maxPeriod(Span)` 采用“在当前 Span 下满足 Nyquist 裕量且不超过 125MiB 下载上限”的动态上限，并保留 `1 s` 的绝对硬上限。
- 动态上限的行业语义：
   - 不顽固追求 `125 MHz` 最大采样率；当 Period 变大时，优先允许 Ramp 降采样，只要仍满足 `Fs >= 1.25 * Span`。
   - 这样像 SignalHound 这类 `50 MHz / 1 s` 的长时长 Ramp 口径，在较低 Span 下仍可成立；高 Span 时则自动缩小允许的 Period，避免用户碰到 125MiB 边界。
- 典型结果：
   - `Span = 20 MHz` 时，动态上限可放宽到 `1 s`。
   - `Span = 40 MHz` 时，动态上限约为 `655 ms`。
   - `Span = 100 MHz` 时，动态上限约为 `262 ms`。
- 实现方式：
   - 在 `RampModulator` 的业务夹紧里把 Span 下限改到 `1 MHz`。
   - 在 `RampModulator` 里引入 `maxPeriodSecForSpan(span)`，按当前 Span 计算安全的最大 Period。
   - 在 `RampModulation` 里同步设置 property metadata min/max，并动态保持 `SweepTime.max = min(Period, maxPeriod(Span))`。

## 2026-06-03 最小时长收敛

- 新增的分析结论需要同步进代码和知识库：当前 `1 us` 下限对生成器来说并非“根本采不到”，因为在现有 `Period <= 1 s` 且 `MAXDOWNLOADSIZE = 125 MiB` 的合法参数区里，生成阶段实际采样率通常不会低于约 `32.768 MHz`，所以 `SweepTime = 1 us` 在最差正常场景下仍大约对应 `33` 个活动样点。
- 但 `1 us` 作为用户可编辑下限仍然过于激进：
   - 当前实现的活动样点数采用 `round(Fs * SweepTime)` 落地；
   - `Period > SweepTime` 时，活动段首尾还会各受 4-interval taper 影响；
   - 外部仪器观测链路对超短时长的显示、触发与时间分辨率余量不足，实际联调里“看得到”和“能生成”不是一回事。
- 这次收敛的决定是：把当前共享的最小时长从 `1 us` 提高到 `2 us`。
- 原因：按“至少约 64 个活动样点”这个工程准则，`SweepTime_min ≈ 64 / Fs`；在当前合法参数区的最差正常采样率 `32.768 MHz` 下，对应约 `1.95 us`，取整到 `2 us` 是合适的硬下限。
- 需要说明的一点：当前实现里 `Period` 和 `SweepTime` 共用同一个 `minTimeSec()`/`kMinRampTime` 边界，因此这次代码收敛会把两者的最小值同时改为 `2 us`，而不是只改 `Period` 不改 `SweepTime`。
- 同时在文档里保留更保守的用户建议：若目标是让外部仪器更容易稳定抓到波形，`5 us` 更适合作为推荐最小值，但不作为当前代码硬门槛。

## 2026-06-03 Ramp I/Q 全周期总览调整

- 当前 `RampPreviewDialog` 的 I/Q 页误复用了频谱页的 512 点 FFT 常量，所以只显示了前 512 个复点，横轴也还是样本序号；对长周期 Ramp，这只能看到极短的起始片段。
- 这次只调整 I/Q 页的数据准备与横轴，不改频谱页当前 analyzer 口径。
- 新行为：
   - 对话框缓存完整周期的 interleaved int16 IQ 与 `sampleRate`，不再把 I/Q 页点数绑到频谱 FFT 常量。
   - I/Q 页改成整周期总览，从全周期均匀抽样；目标显示点数由当前绘图区宽度推导，并夹在 `1024~4096`，如果全周期本身更短则直接全量显示。
   - 横轴改为真实时间 `0..Period`，刻度文本使用 `Utils::normalizeTimeSpan`。
   - 在首次显示和后续 resize 时重建总览，保证点数能随绘图区宽度变化。
- 最便宜验证：
   - 先做 touched-file diagnostics，确认新增 override / helper / ticker 调整没有引入本地编译错误。
