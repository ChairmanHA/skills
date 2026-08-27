# HTRA 过满幅 IQ 映射测试

## Scope

本任务按用户要求在测试分支上扩展 Pulse 的过满幅实验口径，目标对象是 HTRA 插件下当前已纳入构建的本地波形生成器：

- `src/plugins/htra/ammodulator.cpp`
- `src/plugins/htra/fmmodulator.cpp`
- `src/plugins/htra/rampmodulator.cpp`
- `src/plugins/htra/multitonegenerator.cpp`

当前工作树中未发现 `src/plugins/htra/awgnmodulator.*`、`awgnmodulation.*` 或 `awgnpanel.*`，`src/plugins/htra/CMakeLists.txt` 也未包含 HTRA AWGN 条目；AWGN 目前实际实现位于 `src/plugins/analog`。因此本次 HTRA-only 修改不新建 AWGN 业务，也不改 Analog AWGN。

## Assumption

本次是实机验证分支，不作为最终标定结论。沿用 Pulse 实验语义：原本满幅正 I 样点 `I=32767,Q=0` 经修正后应对应 `I=32767,Q=32367`。对已经是复 IQ 的 FM / Ramp / Multitone，采用同一个常量复增益测试口径：

```text
z_out = z_in * (1 + j * 32367 / 32767)
```

在不触发 int16 裁剪的局部区间，这只等价于整体增益和相位旋转；触发裁剪时属于本次“过满幅”测试预期风险。AM 原始 Q 为 0，因此该口径会自然退化为 `Q = round(I * 32367 / 32767)`。

## Success Criteria

1. AM 采样率、样点数、shape、Depth 包络计算保持不变；最终 IQ 输出将原 I 包络补成过满幅 Q。
2. FM 采样率、样点数、瞬时相位积分保持不变；最终量化前应用 `1 + j * ratio`。
3. Ramp 采样率、周期样点数、活动段样点数、taper 和 chirp 相位保持不变；最终写样点时应用同一过满幅复增益。
4. Multitone tone lattice、phase mode、sampleRate、sampleCount、DC residual 处理和 autoscale `componentPeak` 口径保持不变；最终 int16 写出前应用同一过满幅复增益。
5. AWGN 当前无 HTRA 源文件可改，作为发现记录，不在本次提交中改 Analog AWGN。

## Verification

静态验证即可：

- 确认 HTRA CMake 中包含 AM/FM/Ramp/Multitone 对应源文件。
- `rg` 检查四个生成器内的过满幅 helper / 常量使用点。
- `git diff --check` 检查本次改动无 whitespace error。

不编译、不运行，由用户在测试分支做实机验证。

## Implementation Notes

- AM：保留原 `kBaseScale * envelope` 和 `quantizeAmplitude(...)` 行为，随后按 `1 + j * ratio` 写出 IQ，因此正满幅样点落为 `32767,32367`。
- FM：保留原相位积分和 `cos/sin` 复包络，写出前应用过满幅复增益。
- Ramp：保留采样率、活动段、taper 和 chirp 相位；`writeComplexSample(...)` 统一应用过满幅复增益，单样点活动段也走同一路径。
- Multitone：保留 tone / phase / autoscale 的既有计算，最终量化前对 `scaled` 样点应用过满幅复增益。
- AWGN：当前 HTRA 目录和 CMake 中没有 AWGN 文件，本次未创建新业务，也未改 Analog AWGN。

## Static Verification Result

- `rg` 已确认 AM/FM/Ramp/Multitone 中存在过满幅 helper / 常量使用点。
- `rg` 在 `src/plugins/htra` 未找到 `awgnmodulator`、`awgnmodulation`、`awgnpanel` 或 `AWGN`。
- `git diff --check -- src/plugins/htra/ammodulator.cpp src/plugins/htra/fmmodulator.cpp src/plugins/htra/rampmodulator.cpp src/plugins/htra/multitonegenerator.cpp .github/TaskLog/2026-06-22_htra_overfull_iq_mapping_test.md` 通过；仅出现 LF/CRLF 归一化 warning。

## 2026-06-25 Calibration Correction

最新校准口径取代前一版 `32367` 实验假设：

- 不再使用 `32367` 作为 Q 路满幅值。
- IQ 波形的 PEP 满幅归一化按 `I=Q=32767`。
- 之前提到的 `I=32367,Q=0` 是错误描述；正确旧单路满幅应理解为 `I=32767,Q=0`。
- 本次只修改 HTRA `Pulse` 和 `AM`：
  - Pulse: `Full = 32767,32767`，`Half = 16384,16384`，`Off = 0,0`。
  - AM: 保留原 I 路包络和量化，最终直接令 `Q = I`。
- FM / Ramp / Multitone 暂不在本次请求范围内。

静态验证：

- `rg` 确认 HTRA Pulse / AM 不再出现 `32367`。
- `git diff --check` 检查本次 Pulse / AM 改动。

执行结果：

- HTRA Pulse / AM 已改为 `Q = I` 口径。
- `rg -n "32367|kOverfull|writeOverfull"` 检查 Pulse / AM，未再发现 `32367` 或旧 overfull helper。
- `git diff --check -- src/plugins/htra/ammodulator.cpp src/plugins/htra/pulsemodulator.cpp .github/TaskLog/2026-06-22_htra_overfull_iq_mapping_test.md` 通过；仅出现 LF/CRLF 归一化 warning。
- 额外观察：HTRA FM / Ramp / Multitone 仍保留上一轮实验中的 `32367` 常量；本次用户明确要求修改 Pulse 和 AM，因此未在本次改动中调整它们。
