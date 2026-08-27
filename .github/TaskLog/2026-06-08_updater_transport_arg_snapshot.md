# 2026-06-08 Updater Transport Arg Snapshot

## Goal

- 明确保证 updater 所需的传输参数在断链前被快照下来，不依赖断链后的 `currentDevice`。
- 对 USB 场景保留真实 `DeviceNum`。
- 对 ETH 场景保留真实 `IP/Port`，并同时携带 `deviceNumber()` 的即时值；如果底层实现能提供非 0 值，就不再被 updater 入口逻辑重置为 0。

## Local Hypothesis

- 当前 `UpdateDialog::buildMaintenanceArguments()` 虽然在断链前执行，但参数语义仍然混在 if/else 里，且只在 USB 分支填真实 `DeviceNum`。
- 把 transport 参数收敛成一个显式 snapshot helper，可以更直接保证“断链后仍使用断链前的 transport 参数”，并去掉 ETH 分支把 `DeviceNum` 强行留在 0 的入口逻辑。

## Cheap Disproving Check

- 若当前代码已经把 transport 参数独立快照，并对 ETH/USB 都统一读取 `deviceNumber()`，则无需修改。
- 当前检查结果：尚未独立快照，且 `deviceNumber()` 仅在 USB fallback 分支使用，因此假设成立。

## Minimal Change Plan

1. 在 `updatedialog.cpp` 增加局部 transport snapshot 结构与 helper。
2. `buildMaintenanceArguments()` 改为先 snapshot，再从 snapshot 生成参数。
3. 做 Updater 窄编译验证。
