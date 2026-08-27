# 2026-05-22 HTRA Multitone 目标感知去直流实现

## 目标

- 在当前 HTRA multitone generator 内实现“保留合法中心 tone，只消除残余 DC”的去直流方案。
- 保持 owner 边界：只改 `src/plugins/htra/multitonegenerator.cpp`，不改 playback 补长层。
- 去直流位置固定在时域 waveform 恢复之后、metrics/autoscale/int16 量化之前。

## 当前产品语义

- 若参数组合合法落到中心 tone，则 0 Hz tone 是 wanted signal，不应被无条件零均值去掉。
- 若当前 active tones 不包含合法中心 tone，则目标均值为 0。

## 实现方案

1. 先按现有路径生成 active tones、resolved tones、频域 spectrum 和时域 waveform。
2. 根据 active tones 的业务语义计算 `targetMean`：
   - 存在合法中心 tone 时，保留该中心 tone 对应的复常量分量。
   - 不存在合法中心 tone 时，`targetMean = 0`。
3. 对时域 waveform 计算实际复均值 `actualMean`，求残余 `residual = actualMean - targetMean`。
4. 对整段 waveform 扣除该 residual，然后继续走当前 metrics、autoscale 和量化链路。

## 关键边界

- `targetMean` 必须由业务语义决定，不能仅凭最终某个 tone 落到了 `bin 0` 推断。
- `normalizePlaybackPayloadForDownload()` 只做整段重复补长，不参与去直流。
- 本轮先不扩展 UI/metrics 暴露字段，保持实现最小化。