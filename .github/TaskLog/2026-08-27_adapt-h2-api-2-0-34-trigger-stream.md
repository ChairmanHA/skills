# H2 API 2.0.34 触发/Stream 适配

## Scope

- 适配 `3rdParty/h2_api/include/h2_api.h` 2.0.34 的触发与 Stream 接口变更。
- 将旧的 `tx_trigger`/`channel_config_trigger(channel, trigger*)` 调用迁移为 `stream_trigger` + `channel_config_trigger(channel, stream, action)`。
- 支持新的触发输出源 `TRIGGER_EVENT`，保持现有设备级触发输出配置语义。
- 更新必要的状态缓存、查询/回写与编译边界，不扩展无关业务行为。

## Success criteria

1. 源码不再引用已删除/改签名的 `tx_trigger`、`TRIGGER_EDGE_*`、旧 `channel_config_trigger` 和旧 `tx_config_stream` 调用。
2. Stream 配置把 `source` 与 `response_count` 传入 `tx_config_stream`，通道动作通过新的 `channel_config_trigger` 绑定到 stream。
3. 触发输出映射可识别 `TRIGGER_EVENT`，并正确填充新的 `device_trigger_out` 字段。
4. 完成静态搜索；若改动仅限 HTRA 插件，则使用现有 Debug build tree 做 HTRA 目标增量编译（除非环境缺少该构建目标）。

## Verification

- Static: `rg` 检查旧 API 符号与所有新签名调用。
- Debug-build: `build/cmake-win-debug` 中 HTRA 插件目标增量构建，必要时按仓库流程启用 `/FS` 和串行 MSBuild。

## Notes

- `h2_api.h`、Windows DLL 等用户已有改动视为输入，不回退。
- 新 API 文档说明 `tx_query_stream` 的触发配置由 Host 缓存，`channel_query_trigger` 只返回 stream 绑定和通道动作。

## Implementation

- `FancyDevice` 的 CW、Realtime、Playback 统一构造 `tx_stream + stream_trigger`，通过 `tx_config_stream(..., stream0, ...)` 下发 source / response_count，再通过 `channel_config_trigger(..., stream0, action)` 绑定通道动作。
- 外部 Rising / Falling UI 语义分别映射到 2.0.34 的独立 source；查询时由 `tx_query_stream()` 与 `channel_query_trigger()` 拆分回写。
- Core 新增 `TriggerOutSource`，完整接入 Property、CommonDeviceProfile、Profile 持久化、TxApplyRequest、Pipeline 与 StepSweep 快照。
- Trigger Out Source 支持 `Channel` 与 `Trigger Event`；Event 映射 `TRIGGER_EVENT`，发生原始触发事件后立即输出，`recounter` 按 API 约定忽略。
- 已同步更新 H2 API 使用指南、Device Settings/Trigger Split 设计与 Streaming Bridge 调用顺序。

## Verification result

- Static: `src/` 内已无 `tx_trigger`、`TRIGGER_EDGE_*`、旧 `channel_config_trigger/query`、`tx_config_cw/playback` 或旧 Trigger Out API 调用。
- Static: 所有 HTRA `tx_config_stream()` 调用均携带 stream0、`tx_stream` 与 `stream_trigger`；所有 `channel_config_trigger()` 调用均携带 stream0 与 action。
- Static: `git diff --check` 无空白错误，仅报告工作树现有 LF/CRLF 转换提示。
- Debug-build: 使用现有 `build/cmake-win-debug`，设置 `CL=/FS` 并串行构建 `HTRA` 目标成功；产物为 `build/cmake-win-debug/plugin-runtime/HTRA.dll`。
- Platform note: 仓库当前 Linux vendor 库仍为 `libh2api.so.2.0.32`；Linux 运行验证需要供应商提供对应 2.0.34 二进制，本次未改写这些外部库。
