# Streaming Bridge Integration — FixedStream / SweepStream (FScan, LScan)

## 目标

用桥接方式把 FixedStream 和 SweepStream（FScan/LScan）整合到现有 Streaming 业务中。

- **裁决不动**：`TxOrchestrator` 继续按现有规则 resolve FixedStream / SweepStream。
- **session 不动**：`StreamingBussiness` 的 sender loop 仍然是实际的 session runtime。
- **新增桥接**：core 构建 `TxApplyRequest` 后，通过新增的 `IBusiness::setBridgedApplyRequest()` 把 carrier plan 传给 streaming business；streaming 在 workerLoop 的配置阶段据此决定 Fixed 还是 Sweep 启动序列。
- **删除冗余**：空 fileList 时 workerLoop 里的隐式 Mute 分支已失去意义（Panel 层已能自动取消选中，走仲裁退出），一并删除。

## 改动列表

| 文件 | 改动 |
|:---|:---|
| `plugins/core/ibusiness.h` | 新增 `virtual void setBridgedApplyRequest(const TxApplyRequest &)` |
| `plugins/core/idevice.h` | 新增 `startStreamingFrequencySweep` / `startStreamingLevelSweep` 虚方法 |
| `plugins/core/deviceoperator.h/.cpp` | 转发上述两个新方法 |
| `plugins/htra/fancydevice.h/.cpp` | 实现 streaming + FScan / LScan 设备配置序列 |
| `plugins/htra/streamingbussiness.h/.cpp` | 接收桥接 request、workerLoop 增加 sweep 配置、删空列表 Mute |
| `plugins/core/mainwindow.cpp` | legacy path 中为 streaming pipeline 桥接 request |

## 设备配置序列

### FixedStream（不变）

```
configuration(mode=STREAM)
 → applyCommonDeviceSettingsLocked (clock, ffm, trigger)
 → applyModeConfigurationLocked (tx_config_stream, output, start, trigger_bus)
```

### SweepStream（新增 sweep overlay）

```
configuration(mode=STREAM)           ← 先投 FixedStream 完成公共配置
startStreamingFrequencySweep / LevelSweep  ← 再用 sweep 覆盖 carrier plan
 → tx_config_fscan / tx_config_lscan
 → channel_config_trigger (action=SWEEP, count=-1)
 → tx_config_stream (重发，保证 stream 配置未被覆盖)
 → tx_config_output
 → channel_start
 → channel_trigger_bus
```

## 桥接时序

```
MainWindow::updateOrchestrator
 → resolve → buildApplyRequest → applyResolvedPipeline
   → (streaming pipeline?) → targetBusiness->setBridgedApplyRequest(request)
   → (target unchanged?) → return (不 terminate/reactivate)
   → (target changed?)   → terminate old + active new
StreamingBussiness::setBridgedApplyRequest
 → cache carrier plan under m_mutex
 → set m_profileChanged + interrupt generator (if active)
StreamingBussiness::workerLoop (configuration phase)
 → configuration(mode=STREAM)
 → if FScan/LScan: overlay streaming sweep
```

## 风险与边界

- MSCAN 不在本次范围，后续单独处理。
- sweep repeat 使用 -1（无限），与 streaming 连续供数语义一致。
- 空 fileList 隐式 Mute 删除后，退出语义完全由 Panel 层 + 仲裁驱动。
