# Ramp 大波形提示可达性分析

## 结论

不要删除 Ramp 的大波形提示逻辑。

当前实现下，用户仍然可以输入一组合法的 Ramp 参数，使理论最小采样率对应的波形大小已经超过 MAXDOWNLOADSIZE，因此 period 编辑路径里的提示框不是死代码。

## 关键依据

1. Ramp 参数约束只限制：Span <= DATA_SAMPLE_RATE_MAX / 1.25、Period <= 1.0、SweepTime <= Period。
2. Ramp 采样率选择 `calculateRampSampleRate()` 先满足 Nyquist 与整数周期约束；当满足 size cap 的采样率不存在时，会退回不受 size cap 约束的 `bestFs`。
3. 因此对某些高 Span / 长 Period 组合，函数仍会返回导致 `Ramp_EstimatedSize()` 超过 MAXDOWNLOADSIZE 的采样率。
4. 公共下载路径 `AnalogPlaybackBusiness::updateAndTrimData()` / `buildPlaybackExecutionContext()` 会对超限 payload 做 trim，这进一步说明“超限生成”在运行时是被容忍并截断的，而不是根本不可达。

## 反例

可输入合法参数：

- Span = 100e6
- Period = 1.0
- SweepTime = 0.001

推导：

- Nyquist 下限：`1.25 * Span = 125e6`
- 在 `Period = 1.0` 时，size cap 对应上限：`MAXDOWNLOADSIZE / (4 * Period) = 32768000`
- 因为 `32768000 < 125e6`，不存在同时满足 Nyquist 和 size cap 的采样率。
- `calculateRampSampleRate()` 最终会回退到 `bestFs = 125e6`
- `Ramp_EstimatedSize() = 125e6 * 1.0 * 4 = 500000000`，显著大于 MAXDOWNLOADSIZE

## 额外观察

Ramp 当前只有 period 编辑路径带提示；span / sweepTime 编辑路径没有对应提示，其中 span 改大后同样可能把波形推到超限区间。

因此当前问题不是“提示框不可达”，而是“提示覆盖面并不完整”。
