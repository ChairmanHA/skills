# 2026-07-20 Playback 参数范围产品决策矩阵

## 目标

为产品经理提供一份聚焦的 model 132 / model 122 Playback 波形参数对比表：明确 model 132 当前 UI 与业务有效范围、model 122 建议范围、对应采样率，以及仍需产品确认的取舍。

## 范围

- HTRA：AM、FM、PM、Pulse、Multitone、Ramp、AWGN、Ordinary/IQS ARB Playback。
- Analog：Digital Modulation、DSSS、OFDM。
- 排除 Streaming、Quick Waveform、Programmed ARB 和当前未注册 business。
- model 123/150/151 当前使用与 model 122 相同的 Playback capability，正文以 122 代表扩展型号。

## 文档口径

1. “132 当前范围”以当前 active 代码的最终业务回写为准；若 PropertyMetadata 与 business 有差异，单独标出。
2. “122 建议范围”分为三类：
   - 能力已确定：可直接作为实现/验收口径。
   - 推荐待实现：已有技术方案，但当前代码尚未实现。
   - 产品待确认：设备有余量，但是否扩大产品参数范围尚无产品结论。
3. 对动态范围使用公式和代表性边界，不伪装成一个固定 metadata 上下限。
4. 采样率始终按 `[195.3125 kS/s, 200 MSPS] U {400 MSPS}` 表达；400 MSPS 不视为连续上限。

## 成功标准

- 所有范围内 business 均有 132/122 对比和采样率说明。
- AM/FM/PM 等未扩大固定 UI 范围的 business 被明确列为产品待确认。
- Pulse 给出可批准的推荐范围和采样率选择方案，并注明尚未实现。
- Digital/DSSS/OFDM 给出可直接采用的公式化新范围和容量边界。
- 表格能区分固定参数、动态组合范围、设备容量影响和产品决策状态。
- 新文档加入 KnowledgeBase Index。

## 验证级别

`static`

## 静态验证清单

- 对照各 business property 创建、metadata、normalize/resolver 和注册代码。
- 对照 `Waveform_Parameters_Constraints.md` 与 `playback_waveform_parameter_capability_policy.md`。
- 复核 125/996 MiB 对应点数、时长和 OFDM symbolCount 示例。
- 对新文档执行 no-index whitespace 检查，并核对索引和相对链接。
- 不编译、不运行。

## 实施记录

- 已新增 `playback_parameter_product_decision_matrix.md`，以一页总表、逐业务参数表和D1～D7决策清单组织内容。
- 已区分132当前UI显示范围、business最终有效范围以及122目标范围，明确标出AM Depth、PM组合可行域、Pulse当前无效组合等差异。
- 已将AM/FM/PM列为固定范围产品待确认项，将Pulse列为推荐待实现项，将Digital/DSSS/OFDM等确定能力列为可直接验收项。
- 已更新KnowledgeBase Index；本任务只修改文档，不编译、不运行。
