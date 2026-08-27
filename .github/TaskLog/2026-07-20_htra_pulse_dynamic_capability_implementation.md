# HTRA Pulse 动态设备能力实施

日期：2026-07-20

## 目标

按最新产品决定实现 HTRA Pulse 的设备能力解析：

- model 132 能力档：`Width = 48 ns~1 s`，`Period = 96 ns~1 s`；在容量允许时优先使用 125 MSPS，以增加脉冲与边沿采样点。
- model 122/123/150/151 能力档：连续 200 MSPS 下理论边界为 `Width = 30 ns`、`Period = 40 ns`；只有连续档不能满足最小采样点时才使用精确 400 MSPS，从而开放 `Width = 15 ns`、`Period = 20 ns`。
- Pulse business 根据 current Playback domain/capacity 完成参数收口、永久回写和 generation plan 构建；generator 不读取设备型号或能力。
- 设备切换导致能力回写时关闭 Enabled，但不弹窗；普通参数编辑继续沿用现有重新生成流程。

## 设计边界

1. Pulse 波形质量硬约束为 Width 至少 6 个样点、Period 至少 8 个样点、`Width <= Period`。旧实现中的“Period 样点数必须为 8 的倍数”不是生成算法要求，并且会错误拒绝 `48 ns / 96 ns @ 125 MSPS`（6/12 点），本次删除该倍数约束。
2. model 132 档由能力域识别为无 400 MSPS 离散点；扩展档由 current domain 中存在精确 400 MSPS 离散能力识别，不把型号判断下沉到算法层。
3. 扩展档普通参数优先连续档；400 MSPS 只用于连续 200 MSPS 无法保持 6/8 点的窄脉冲/短周期。
4. Pulse 使用 `min(device.maxWaveformBytes, 125 MiB)` 作为偏好预算。在预算内选择尽可能高的采样率；6/8 点质量需要时允许突破偏好预算，但不得超过设备硬容量。
5. business 将最终 `sampleRate / widthSamples / periodSamples / payload layout` 一次性提交给 generator。generator 只验证 plan 与参数相符，并按明确样点数生成，避免两层分别对 double 取整。
6. PropertyMetadata 只表达全产品连续外框；132/扩展档差异和采样率空洞均由 business 回写，不做动态 UI metadata 限制。

## 成功标准

- 132 上 `48 ns / 96 ns` 能解析为 125 MSPS、6/12 点。
- 132 上正常短周期参数在容量允许时优先 125 MSPS；长周期按 125 MiB 硬容量降低采样率，并在必要时把 Width 回写到至少 6 点。
- 扩展档 `30 ns / 40 ns` 可留在连续 200 MSPS；`15 ns / 20 ns` 使用精确 400 MSPS。
- 扩展档不因“400 MSPS 量化更细”而让普通参数无条件跳到 400 MSPS。
- 所有最终 payload 均不超过 current `maxWaveformBytes`；设备切换回写静默完成并关闭 Enabled。
- `PulseModulator` 不包含 `PlaybackCapabilities` 或设备型号分支。

## 验证级别

静态检查。按仓库约定不执行全量编译；由用户后续自行编译和实机验证。

## 实施结果

- `PulseModulation` 已按 current capability 区分基线档与包含400 MSPS离散点的扩展档，并完成精确连续格点、125 MiB偏好预算、400 MSPS受控使用及动态Period容量回写。
- `48 ns / 96 ns @ 125 MSPS` 解析为6/12点；旧的Period点数8倍约束和长周期10 MHz特例已删除。
- 新增 `PulseGenerationPlan`，business提交明确的`widthSamples / periodSamples / payload layout`；`PulseModulator`不再根据设备能力或double二次推导样点数。
- `PulseModulator::normalizeParams()` 只保留正值、1 s外框和`Width <= Period`等设备无关基础合法化；15/20、48/96及容量组合全部由business处理。
- PropertyMetadata统一为全产品外框`Width >= 15 ns`、`Period >= 20 ns`；132输入较小值时由business永久回写48/96 ns。
- 设备能力变化导致参数回写时关闭Enabled但不弹窗；普通编辑继续使用原有重新生成流程。

## 静态验证

- 已执行`git diff --check`，未发现空白或补丁格式错误。
- 已逐项检查132 `48/96 ns`、扩展档`30/40 ns`与`15/20 ns`的样点公式，以及窄Width下132/扩展设备的动态Period硬容量边界。
- 未执行编译或运行，符合本任务约定。

## 实机验收注意

当前半幅边沿合成算法未修改。按仪器50%阈值测量时，窄脉冲宽度可能接近`(widthSamples - 1) / Fs`，因此6点的48 ns计划可能测得约40 ns，6点的15 ns计划可能测得约12.5 ns。这是既有波形边沿语义，若产品要求实测Width严格等于UI值，应作为独立算法任务处理。

## 编译后回归修正

- 122设备实测输入`48 ns / 96 ns`曾被回写成`50 ns / 95 ns`。原因是Pulse局部连分数转换使用绝对容差`1e-12 s`，对纳秒参数过宽，把48/96 ns提前接受成低精度近似分数，导致125 MSPS等精确连续格点未进入候选，最终退回200 MSPS并量化为10/19点。
- Pulse resolver的分数容差已收紧为`1e-18 s`。现在48 ns化为`3/62500000 s`、96 ns化为`3/31250000 s`，连续档可以识别精确格点，不再回写成50/95 ns。
