# 2026-07-20 Playback 波形参数能力约束策略文档

## 目标

为当前实际启动的生成型/文件型 Playback business 建立统一的“设备能力 -> 波形参数约束”设计口径，并对比 HTRA model 132 与 model 122 的参数范围、采样率决策、容量约束和设备切换行为。

## 范围

- HTRA：AM、FM、PM、Pulse、Multitone、Ramp、AWGN、ARB Playback。
- Analog：Digital Modulation、DSSS、OFDM。
- 明确排除：Streaming、Quick Waveform、Programmed ARB，以及源码存在但当前未注册的其他 business。
- model 123/150/151 当前能力与 model 122 相同，正文以 122 为代表并注明等价范围。

## 设计原则

1. PropertyMetadata 只表达简单、连续、设备无关的输入底线；非连续采样率、参数联动和容量限制由 business resolver 处理并永久回写。
2. generator/第三方算法不读取设备型号或 `PlaybackCapabilities`；business 负责解析 profile 并提交精确 generation plan。
3. 采样率选择区分硬约束与质量偏好：普通生成优先避免无意义的大 payload；Pulse 等时域质量敏感业务可在合理内存范围内选择较高采样率；400 MSPS 单点不得被当作 200~400 MSPS 连续范围。
4. 最终容量按归一化后的 IQ payload 计算，统一使用 current `maxWaveformBytes` 和 `bytesPerComplexSample`，Save IQ 与 Playback 使用相同前置门禁。
5. 设备切换后由 business 静默回写或复位不兼容参数；只有 ARB 已加载文件因新设备容量不足而自动卸载时弹窗。

## 成功标准

- 文档列出实际启动 business，并能从注册代码和 CMake 入口交叉验证。
- 清晰对比 model 132 与 122：
  - 132：195.3125 kS/s~125 MSPS，125 MiB。
  - 122：195.3125 kS/s~200 MSPS，加 400 MSPS 单点，996 MiB。
- 每个 business 至少说明：核心公式、132 策略、122 策略、400 MSPS 使用边界、容量约束、设备切换行为。
- Pulse、AM/FM/PM、Ramp、AWGN 等存在多采样率选择或参数互相制约的业务给出明确决策顺序；Digital/DSSS/OFDM/Multitone/ARB 保持相对简洁。
- 新知识库文档加入 `.github/KnowledgeBase/Index.md`。

## 验证级别

`static`

## 静态验证清单

- 对照 `htradevicecapabilityresolver.cpp` 核对 model 能力数值和 400 MSPS 非连续语义。
- 对照 HTRA/Analog plugin 注册点核对业务范围。
- 对照各 `*modulation.cpp` resolver 与当前知识库，区分“已实现行为”和“建议决策”。
- 对新文档执行 `git diff --no-index --check`，并人工核对 Index 条目；不编译、不运行。

## 实施记录

- 已新增 `playback_waveform_parameter_capability_policy.md`，覆盖范围内全部当前启动 business，并明确区分当前行为与目标策略。
- 已将新文档加入 KnowledgeBase 索引。
- Pulse 以最少 Width/Period 样点、连续/离散采样率候选、软内存预算和动态 Period 上限为核心决策；其他 business 按物理公式和容量语义选择最小充分采样率。
- 本任务仅修改文档；已执行 no-index whitespace 检查、关键章节检索和链接目标存在性检查，不编译、不运行。
