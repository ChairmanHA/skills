## 背景

- Device Settings 的 Trigger In 已经能把 TriggerSource 写入 CommonDeviceProfile，并进入 TxApplyRequest。
- HTRA 的普通 configuration 阶段也会把 Trigger In source/action/edge 下发到设备，并缓存到 `m_triggerSource/m_triggerAction/m_triggerEdge`。
- 但 playback 与 sweep 启动路径在 `channel_start()` 后仍无条件调用 `channel_trigger_bus()`，会覆盖 external/XPPS 的等待语义。

## 局部假设

- 当前问题不在 TriggerSource 传递链，而在启动阶段少了“按触发源决定是否主动发 BUS trigger”的分支。
- 只要把 `triggerStart()` 与各类 sweep start 路径改成仅在 `m_triggerSource == TRIGGER_SOURCE_BUS` 时调用 `channel_trigger_bus()`，external/XPPS 就会保持等待外部事件，且 BUS 现有行为不变。

## 便宜校验

- 检查 `FancyDevice::triggerStart()`、`startFrequencySweepLocked()`、`startLevelSweepLocked()`、`startListSweepLocked()` 是否都在 `channel_start()` 后无条件调用 `channel_trigger_bus()`。
- 若属实，则抽一个本地 helper 收口这一分支，并对触发源记录日志，随后做静态错误检查即可验证改动面是否闭合。

## 计划

- 在 `FancyDevice` 本地新增 helper，统一处理“BUS 触发时主动发 trigger，其它触发源仅进入等待态”。
- 替换 fixed playback 与 CW/Playback/Streaming sweep 启动路径中的无条件 `channel_trigger_bus()`。
- 更新 Trigger In 设计文档，明确“配置阶段会下发并缓存 Trigger In，但启动阶段仅 BUS 源会主动 fire”。
- 更新 H2 API 使用文档，使实现与推荐顺序保持一致。
- 对改动文件做静态错误检查。