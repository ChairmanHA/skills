# 2026-04-03 Preset Completeness Fix

## Problem

- `MainWindow::preset()` 当前会在复位过程中触发中途的 `selectBusiness2Work()` / `updateOrchestrator()`，导致 pipeline 可能在“半恢复”状态下被重新裁决和下发。
- `resetSweepPanelToDefault()` 只恢复了 StepSweep 的模拟参数，没有清掉 `ListModePanel` 的点表编辑态；切回 List 后旧数据仍然存在。
- `preset()` 最后直接激活 `MuteBusiness`，这仍是旧式兜底路径；在当前 request/runtime 架构下，更合适的是在所有输入恢复完成后统一走一次 orchestrator。

## Fix Plan

- 为 `MainWindow` 增加一个临时的 pipeline 更新抑制标志，在 `preset()` 复位期间屏蔽中途的 `updateOrchestrator()`。
- `preset()` 内部顺序改为：取消当前业务选中 -> 重置所有 business -> 重置 `CommonDeviceProfile` -> 完整重置 StepSweep（含 ListMode）-> 先关闭 runtime -> 若设备已连接则执行 `resetDevice()`。
- 调整原因：`TxPipelineRuntime` 会在设备重新 open 时自动重放缓存请求；虽然 `FancyDevice::resetDevice()` 本身不直接发出重连信号，但底层 preset 仍可能触发瞬时断开/重连，因此需要先 `deactivate()`，避免旧请求被意外重放。
- 复位完成后只调用一次 `selectBusiness2Work()`，用恢复后的默认输入统一重建并下发默认 pipeline。
- 为 `StepSweepPanel` 增加显式 `resetToDefault()`，同时恢复模拟 sweep 参数、ListMode 默认表状态，并关闭面板 enabled。

## Expected Result

- Preset 不再在中间状态下触发多次 pipeline apply。
- Sweep 页面无论当前停留在 Freq / Power / List，都会完整回到默认 profile。
- 如果设备已连接，会先关闭旧 runtime，再执行硬件 reset，最后按默认公共配置统一进入默认 pipeline。