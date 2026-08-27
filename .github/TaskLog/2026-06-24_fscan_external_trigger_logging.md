# FScan External Trigger Logging

日期：2026-06-24

## TaskLog Path
- `.github/TaskLog/2026-06-24_fscan_external_trigger_logging.md`

## Scope
- 为 `src/plugins/core/txpipelineruntime.cpp` 增补 FScan 外触发相关日志。
- 为 `src/plugins/htra/fancydevice.cpp` 增补 H2 API 下发前的触发参数日志。
- 仅增强日志可观测性，不改变触发行为、参数归一化策略或设备调用顺序。

## Verification Level
- `static`

## Observation
- `TxPipelineRuntime::requestApply()` 已记录一份公共参数快照，但 FScan 专项日志只打印频点、驻留和 repeat，缺少触发细节。
- `normalizeRequestForCurrentDevice()` 可能把请求的 TriggerSource 归一化为别的值，但当前只记录了 `rfPort` 归一化。
- `FancyDevice` 在 `applyCommonDeviceSettingsLocked()` 和 `startFrequencySweepLocked()` 中会再次把触发参数写入 H2 API，但当前日志不足以看清最终写入值。

## Assumptions
- 当前外触发测试重点是 FScan，因此优先补齐 FScan 申请、归一化、设备下发三处日志。
- “外触发相关配置参数”至少包括：
  - Trigger In: `source / action / edge / count`
  - Trigger Out: `enable / action / edge`
- 若日志能同时覆盖“运行时请求值”和“设备实写值”，则足以支持静态对照与现场测试排查。

## Success Criteria
- FScan 请求日志中明确打印 Trigger In/Out 全部相关字段。
- 若 TriggerSource 在运行时被归一化，日志明确打印请求值与归一化后的值。
- `FancyDevice` 在调用 `channel_config_trigger()` / `channel_config_trigger_out()` 前打印将要下发到 H2 API 的参数。
- 不修改任何触发判断、参数值或设备调用顺序。

## Plan
1. 在 `TxPipelineRuntime` 增加触发摘要 helper，并复用到 FScan request/writeback/failure 日志。
2. 在 `TxPipelineRuntime::requestApply()` 中补 TriggerSource 归一化日志。
3. 在 `FancyDevice` 中为触发源、动作、边沿、输出状态增加字符串 helper，并记录实际下发参数。
4. 完成后做静态检查，确认改动只涉及日志。
