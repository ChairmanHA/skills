## 背景

- `Center` / `Level` 已有明确需求保留运行期可编辑步长，本次不改它们。
- 用户当前希望两类升级同时推进：
  - 一批高把握的“主频率量”统一切到 `125` 步进；
  - `StepSweep_FreqStep` 与 `StepSweep_DwellTimeAnalog` 升级为可编辑步长。
- 现有公共层里如果同时配置 `stepStrategy=125` 与 `step`，会优先落到固定步长控制器，无法把 `step` 当成 `125` 的初始 seed 使用；这会让 `StepSweep_StartFreq/StopFreq` 这类默认值可能为 0 的参数起步不自然。

## 方案

- 公共层调整为：当声明 `stepStrategy=125` 且 `stepEditable=false` 时，始终使用 `Step125Controller`；若同时提供 `step`，则把它作为初始 seed，而不是固定步长。
- 在 `CorePlugin` 中：
  - `StepSweep_StartFreq` / `StepSweep_StopFreq` 切到 `125`，并给 `1E6` seed；
  - `StepSweep_FreqStep` 保持默认 `10MHz`，但开启可编辑步长；
  - `StepSweep_DwellTimeAnalog` 保持默认 `1ms`，并开启可编辑步长。
- 在 `AnalogModulationPlugin` 中，仅给高把握的主频率量配置 `125`：
  - `Am_Rate`
  - `Fm_Rate`
  - `Fm_Deviation`
  - `Ramp_Span`
  - `Awgn_Bandwith`
  - `Digital_SymbolRate`
  - `Dsss_SymbolRate`
  - `Ofdm_SampleRate`

## 预期行为

- `StepSweep_StartFreq` / `StepSweep_StopFreq` 走 `1-2-5` 数量级步进，从 0 起步时默认以 `1MHz` 为 seed。
- `StepSweep_FreqStep` / `StepSweep_DwellTimeAnalog` 打开软键盘后会出现标题栏步长编辑区，分别以 `10MHz` / `1ms` 作为默认步长。
- common panel 的 `Center` / `Level` 保持现状不变。