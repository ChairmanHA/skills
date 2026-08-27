# Sweep Preview Validation Boundary

## 背景

StepSweep 的前端预校验现在是一个独立于真实发射流水线的编辑态链路：

1. `StepSweepPanel` 在参数 `editingFinished`、`ListModePanel` Apply、以及 `currentDeviceOpenStateChanged(true)` 后触发 preview。
2. `StepSweepPanel::previewCurrentCarrier()` 构造 `TxCarrierPlanContext + TxCommonSettings`。
3. 当前设备通过 `IDevice::previewCarrierPlan()` 调用底层无副作用测试接口。
4. 若设备返回了归一化结果，则先回写 UI。
5. 只有在 sweep `enabled=true` 时，归一化后的结果才继续上浮为 `argsChanged()` 参与 orchestrator / runtime。

这条链路的目标只有一个：

- 在编辑态把参数归一化到设备可接受范围，而不是执行真实设备配置。

## 设备侧边界

HTRA `FancyDevice::previewCarrierPlan()` 负责把统一的 carrier/common 语义收口到 H2 `tx_test_*` 系列 preview 接口。

当前至少包括：

1. `FScan` 的频扫 preview 校验
2. `LScan` 的电平扫 preview 校验
3. `MScan` 的点表 preview 入口与归一化语义边界

preview 路径必须保持无副作用：

1. 不调用 `tx_config_*`
2. 不调用 `channel_start()`
3. 不调用 `channel_trigger_bus()`
4. 不改变 RF/MOD 输出状态
5. 不写入真实运行态告警/错误状态

## warning 语义

`tx_test_*` 在参数被设备能力钳位时，可能返回正向 warning，例如 `STATUS_WARNING_PARAMOUTRANGE`。这类状态在 preview 语义里不是失败，而是“归一化成功”。

因此 preview 必须遵守下面的约束：

1. `status < STATUS_NOERROR` 才表示 preview 失败。
2. `status >= STATUS_NOERROR` 都表示 preview 成功，可以继续回写归一化结果。
3. preview warning 不应通过 `errorMessage` 向上冒泡为前端错误提示。
4. preview warning 不应写入 `m_warnningCode` 或 `DeviceRealTimeStatus::warnningCode`。

换句话说，preview warning 的语义是：

- 设备接受了这组参数，但对它们做了限位或修正。

而不是：

- 设备当前处于需要状态栏滚动提示的运行态告警。

## 与 DeviceInfoWidget 的边界

`DeviceInfoWidget` 的 warning 队列只消费 `MainWindowDeviceController` 从 `DeviceRealTimeStatus::warnningCode` 读到的运行态 warning。

preview 链路不属于实时运行态，因此：

1. 编辑态 preview 的钳位 warning 不能进入 `DeviceInfoWidget`
2. preview 失败只允许保留在日志里，不应弹窗
3. 真实设备告警仍由运行态 `handleStatus()` 路径负责

这个边界保证了两件事：

1. 用户编辑超限 sweep 参数时，只看到 UI 自动回写为设备支持值，不会收到无意义的状态栏 warning。
2. 真正的设备运行问题仍然通过既有 error / warning 机制可见。