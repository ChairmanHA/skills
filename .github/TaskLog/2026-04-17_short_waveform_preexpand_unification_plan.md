# Playback 短波形预扩展统一方案

## 目标

把当前散落在不同链路中的短波形预扩展行为统一为一套公共逻辑，并确保以下路径都使用同一规则：

1. legacy playback 下载路径
2. Analog 各调制的 core-managed Playback provider
3. Arb ordinary WAV 的 core-managed Playback provider

## 当前问题

1. `Core::IPlaybackBusiness::downloadData()` 内已有短波形预扩展逻辑，但它只覆盖 legacy 设备线程下载路径。
2. `AnalogPlaybackBusiness::buildPlaybackExecutionContext()` 直接把原始 IQ payload 塞进 `TxProviderExecutionContext`，没有做预扩展。
3. `ArbModulationOnly::buildPlaybackExecutionContext()` 直接把 `ArbDataGenerator` 输出塞进 `TxProviderExecutionContext`；而 `ArbDataGenerator::handleData()` 还保留了一套只针对 ordinary wav 的 32768 words 扩展逻辑。
4. 结果是：
   - FM/AM 等 core-managed FixedPlayback 会把极短 payload 直接下发。
   - Arb ordinary WAV 在 provider 阶段使用另一套不同规则做扩展。
   - legacy 与 core-managed 行为分裂，高采样率下阈值也不一致。

## 统一策略

抽取 `IPlaybackBusiness` 公共静态 helper，作为“Playback 下载前 payload 规范化”的唯一入口：

1. `sampleRate < 100E6`：若 words 数小于 32768，则重复扩展。
2. `sampleRate >= 100E6`：若 words 数小于 131072，则重复扩展。
3. 扩展策略保持与现有 `downloadData()` 完全一致：使用整数除法/向下取整的 copy-append 方式，避免引入新的设备行为变化。

## 代码改动

### `src/plugins/core/iplaybackbusiness.h/.cpp`

1. 新增公共静态 helper，例如 `normalizePlaybackPayloadForDownload(QVector<int16_t>&, double)`。
2. `downloadData()` 内部改为先调用 helper，再执行 `downloadDataSeq()`。

### `src/plugins/analog/analogplaybackbusiness.cpp`

1. 在 `buildPlaybackExecutionContext()` 里，float -> int16 转换完成后，调用公共 helper。
2. 再把扩展后的 payload 写入 `context->playbackIqInterleaved`。
3. 这样 `TxPipelineRuntime::requestApply()` 中的 `payloadWords` 会反映真实下载前长度。

### `src/plugins/htra/arbdatagenerator.cpp`

1. ordinary WAV 分支不再使用自带的 32768-only 扩展逻辑。
2. 在 `IQ_handled` 生成完成后，直接调用 `Core::IPlaybackBusiness::normalizePlaybackPayloadForDownload()`。
3. `ArbModulationOnly::buildPlaybackExecutionContext()` 保持现状，只负责把 `ArbDataGenerator` 已处理好的 `iqData` 塞进 `TxProviderExecutionContext`。
4. 这样 Arb playback 仍然在“生成层”完成预扩展，但扩展规则与 legacy / Analog core-managed 完全一致。

## 预期结果

1. 所有 Playback 波形下发前都走同一套短波形预扩展规则。
2. FM fixed modulation 与 Arb ordinary playback 在相同 sampleRate 下将使用相同的最小 payload 规则。
3. Arb ordinary WAV 不再保留与 legacy 不一致的 32768-only 扩展分支。
4. requestApply 日志中的 `payloadWords` 将与实际设备下载前 payload 保持一致。