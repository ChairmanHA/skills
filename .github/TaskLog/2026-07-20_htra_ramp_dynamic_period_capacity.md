# HTRA Ramp 动态 Period 容量实施

日期：2026-07-20

## 目标

Ramp 与 AWGN 使用相同的时长容量语义：Ramp business 先由 `Span` 和 current sample-rate domain 解析最终采样率，再由 current `maxWaveformBytes` 反推 `Period` 最大值。低采样率允许超过1秒的长波形，高采样率自动缩短Period，最终payload不得超过设备容量。

## 当前问题

- `RampModulation::maxPeriodSecForDownloadLimit()` 虽然已经计算容量上限，但仍用固定`1 s`再次截断。
- `RampModulator::normalizeProfile()`也把Period固定钳到`1 s`，导致business即使放宽也会被算法层重新压回。
- 现有`calculateRampSampleRate(span, period)`通过Period有理数分母寻找采样率格点；在动态容量上边界附近，最小采样率可能因分数格点被抬高，随后超过容量并退回默认Period。特别是设备最小采样率`195312.5 S/s`本身为非整数Hz，单纯要求采样率是Period分母的整数倍并不完整。

## 设计

1. `Span`按现有1.25倍带宽裕量和current非连续domain收口。
2. 收口后的Span决定`minimumFs`。连续档内若`N = ceil(minimumFs * Period)`且`Fs = N / Period`仍在current domain和容量内，则允许把Fs微量上调以精确保留用户Period；否则固定`minimumFs`并把Period量化到整数样点。200~400 MSPS空洞仍向连续低档回写，精确320 MHz Span固定使用400 MSPS。
3. 最大复样点数为：

   `maxComplexSamples = maxWaveformBytes / bytesPerComplexSample`

4. 动态Period上限为：

   `maxPeriod = maxComplexSamples / resolvedSampleRate`

   不再附加固定1秒产品上限。
5. business把用户Period转成整数`periodSamples = round(Fs * Period)`，夹到设备最大点数后，再永久回写`Period = periodSamples / Fs`并构建唯一payload layout。
6. SweepTime继续满足`1 us <= SweepTime <= Period`；generator只做设备无关基础合法化，不读取capacity，也不再固定钳位1秒。
7. 设备切换造成Span/Period/SweepTime回写时关闭Enabled，不弹窗；普通编辑沿用现有静默回写和重新生成。

## 成功标准

- 低Span下，132和扩展设备均允许Period超过1秒，最大值随各自125/996 MiB容量变化。
- 高Span下Period最大值按`maxComplexSamples / Fs`降低；132的100 MHz Span约为0.262144秒，扩展设备160 MHz Span约为1.30547712秒，320 MHz Span约为0.65273856秒。
- 任意最终plan均通过`makePayloadLayout()`，`allocatedBytes <= maxWaveformBytes`。
- Ramp generator中不存在`PlaybackCapabilities`、型号判断或固定1秒设备策略。
- Save IQ与Playback继续共用同一plan和payload硬门禁。

## 验证级别

静态检查，不执行全量编译；由用户自行编译与实机测试。

## 实施结果

- 已删除business和generator中的固定1秒Period上限；PropertyMetadata继续只保留1 us基础下限，不设置动态最大值。
- 已删除旧的`toFraction(period) -> sample-rate denominator lattice`搜索，避免195312.5 S/s和容量端点被误判无解。
- `RampModulation`现在先由Span得到最低合法Fs，再以整数样点数解析Period：连续档能精确保留Period时只微量提高Fs；否则固定最低合法Fs并永久回写量化后的Period。
- 400 MSPS继续只服务精确320 MHz Span；普通160~320 MHz输入仍向连续200 MSPS/160 MHz回写。
- 新增`RampGenerationPlan`，明确携带`activeSampleCount / periodSampleCount / playback layout`；generator只消费整数plan，不读取设备能力或再次计算样点数。
- SweepTime同步量化为实际活动段样点数，并始终不超过Period。
- Playback与Save IQ继续复用同一immutable payload；preview仍固定最多65536个complex sample。

## 静态验证

- `git diff --check`通过。
- 已确认Ramp generator不包含`PlaybackCapabilities`、model判断、旧1秒常量、旧Period有理分数搜索或Streaming 62.5 MSPS降级。
- 容量公式核对结果：132低Fs/20 MHz/100 MHz分别约167.77216 s、1.31072 s、0.262144 s；扩展设备低Fs/20 MHz/160 MHz/320 MHz分别约1336.80857088 s、10.44381696 s、1.30547712 s、0.65273856 s。
- 最大996 MiB对应261,095,424个complex sample和522,190,848个int16 word，仍低于当前容器索引上限。
- resolved profile按`Span -> Period -> SweepTime`顺序回写，避免400 MSPS离散档向上量化Period时，SweepTime先被旧Period截断而与整数生成计划相差一个采样点。
- 未执行编译或运行。

## 性能边界

动态放宽Period不等于建议普通操作主动生成接近996 MiB的Ramp。完整生成仍需要清零整个payload；`SweepTime`接近`Period`时还需要逐点执行chirp三角函数，随后RMS再次全量扫描。在树莓派上，接近容量上限的生成可能耗时很长，但内存仍遵守单一完整payload驻留契约。
