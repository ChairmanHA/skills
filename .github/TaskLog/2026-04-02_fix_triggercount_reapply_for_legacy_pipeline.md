# 2026-04-02 TriggerCount Legacy Reapply Fix

## Problem

- Title bar 中 Single / Continue 修改的是公共属性 `TriggerCount`。
- 当前部分业务（例如 HTRA Arb）仍走 legacy-managed 路径，不经过 `TxPipelineRuntime`。
- `MainWindow::applyResolvedPipeline()` 对 legacy-managed 非 streaming 路径存在“同 pipeline 且同 target 直接 return”的早退逻辑。
- 因此即使 `TxApplyRequest` 中的 `common.triggerCount` 已变化，也不会重新激活业务，不会触发设备重配。

## Root Cause

- legacy-managed 路径的早退条件只看 `pipelineChanged` 和 `targetChanged`，没有看 `TxApplyRequest` 是否发生变化。
- 这会吞掉 `TriggerCount`、参考时钟、Trigger In/Out 等公共参数变化带来的重配需求。

## Fix Plan

- 在 `MainWindow` 中缓存最近一次已应用的 `TxApplyRequest`。
- legacy-managed 路径改为：仅当 `pipeline`、`target`、`request` 都没变化时才跳过。
- 对 streaming 保持现有 bridge 机制，但同样基于 request 变化判断是否需要重新 bridge。
- core-managed 路径在每次成功提交 request 后同步更新已应用 request 缓存。

## Expected Result

- Single / Continue 切换会在 legacy-managed 业务下重新触发配置。
- 其他公共参数在 legacy-managed 路径下也不再因为“同 pipeline / 同业务”被错误吞掉。