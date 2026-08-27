# 任务目标

1. 修正 Analog 大波形自动流切换顺序，改为先结束当前业务，再选中目标业务，再由现有 pipeline/orchestrator 路径激活目标业务。

# 当前问题

## 自动流切换顺序不符合主线

当前 `AnalogPlaybackBusiness::executeHandoverToStreaming()` 直接：

1. 写 Streaming 属性
2. 直接 `streamingBusiness->setActive(true)`
3. 再通过 `enabledChanged(true)` 触发 selected business 切换

这样会导致 `BusinessManager` 先切 active，再由 `MainWindow::updateOrchestrator()` 补做 resolve/bridge；对于新 pipeline 框架，顺序倒置。

## DSSS 维持现状

已确认 DSSS 生成链路会主动压缩 `symbolLength` 以避免生成阶段内存爆炸。

这意味着 DSSS 的“大波形”问题不只是 UI 是否弹出 handover 提示，而是其底层生成模型本身就带有防爆内存约束。当前任务不再改 DSSS，保持原样。

# 设计

## 自动流切换改为主线顺序

在 `Core::BusinessManager` 增加一个对外可调用的入口，例如“按 pipeline 顺序切换到指定业务”。

顺序：

1. 如果当前 active business 存在且不是目标业务，先 terminate
2. 将 FancyTabWidget 的 selected business 切到目标业务
3. 依赖现有 `currentSelectedBusinessChanged -> selectBusiness2Work -> updateOrchestrator -> applyResolvedPipeline` 完成 bridge 和最终 active

`AnalogPlaybackBusiness` 不再直接 `setActive(true)`；只负责：

1. 注入 Streaming 属性
2. 调用 BusinessManager 的切换入口

这样自动流进入 Streaming 时，会遵循和手工切换业务一致的时序。

## 本次不改 DSSS

原因：

1. DSSS 会消耗大量内存，当前已有底层保护。
2. 若仅补 UI 层 handover 提示，而不重构 DSSS 的生成/内存模型，语义会不一致。
3. 当前用户明确要求 DSSS 保持原样，因此本次不接入自动流。

## 验证

1. 静态检查编译错误
2. 确认 `MainWindow` 新入口不会破坏现有选中信号链
3. 确认不引入 DSSS 相关改动

## 新发现的交互 bug

在大波形提示里选择 `Adjust Params` 后，数值会回滚，但数值控件的编辑上下文单位可能没有回滚。

现象：

1. 按钮表面显示仍然是预期旧值
2. 再次打开软键盘时，数值会以基础单位（如 `Hz`）显示，而不是用户刚才看到的显示单位（如 `MHz`）

根因判断：

1. Reject 路径当前大多只执行 `property->setValue(prevValue)`
2. 软键盘初始单位来自属性上的 `currentUnit`
3. 若编辑过程中 `currentUnit` 已被新输入改写，而 Reject 没有恢复它，就会出现“显示层正确、编辑层单位错误”或两者不同步的情况

## 本次修复策略

1. 在 `AnalogPlaybackBusiness` 增加通用的 property 显示状态跟踪/恢复 helper
2. 在会触发大波形提示的、且带单位显示的属性上，进入编辑前记录：
	- 数值
	- `currentUnit`
	- `displayText`
3. 选择 `Adjust Params` 时，不只回滚数值，也回滚上述显示状态

这样可以保证：

1. 按钮文本回到编辑前状态
2. 再次打开软键盘时，初始单位与按钮显示保持一致

# 当前结果

1. `Generate & Stream` handover 已改为走主线顺序：先结束当前 active business，再选中 `Streaming`，最后由 orchestrator / applyResolvedPipeline 完成桥接与激活。
2. DSSS 保持原样，不纳入本次自动流改造。
3. `Adjust Params` 的 Reject 路径已为带单位属性恢复完整显示状态，避免数值回滚后软键盘单位错乱。
