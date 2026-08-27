# Trigger Out API 隐藏缺陷与工作区收敛

## 根因结论

- 设备 open 成功后，旧配置把 `TriggerOutAction::Sweep` 映射为 `device_trigger_out.recounter = 10`。
- 当前 A60_R1 + H2 API/固件组合中，该参数会使 USB 会话随后失效；前端异步收到的 open/feature/status 事件因此与设备销毁交错，最终表现为 FeatureSpec 悬空指针崩溃、UI 长时间连接中和 retry-open `-8`。
- 固定 `state=0, source=0, negative_pulse=0, recounter=1` 后，实机连续 13 次 Trigger Out 调用及其前后查询均返回 0，没有断联。
- 因此此前围绕“独立 FeatureSpec 生命周期缺陷”和“scanner 借用指针所有权缺陷”扩大的修改不再保留；真正兼容点收敛在 HTRA Trigger Out 参数上。

## 范围

1. 保持 Device Settings 的 Trigger Out 页面开放。
2. `device_config_trigger_out()` 继续在公共配置链下发，但参数固定为：
   - `state = 0`
   - `source = 0`
   - `negative_pulse = 0`
   - `recounter = 1`
   - `extension1/extension2 = nullptr`（结构体零初始化）
3. 删除 `[H2TriggerOutProbe]` 定点取证日志与额外前后查询，恢复正常调用开销。
4. 回退以下不再需要的扩大修改：
   - `IDevice::FeatureSpec` 指针接口改值语义；
   - Core/Profile/Runtime/Pipeline 的 FeatureSpec 级联改动；
   - scanner 每轮新建候选对象的所有权改造；
   - `IDevice` 全局实例表 mutex；
   - 与上述假设配套的接口注释。
5. 不修改、不删除工作区中既存的无关未跟踪文件。

## 成功标准

1. tracked 工作区最终只保留 `src/plugins/htra/fancydevice.cpp` 的窄补丁。
2. Trigger Out 页面保持可见，调用参数不会受 UI 当前值影响。
3. writeback 回到固定硬件兼容值 Hop / Rising / OFF，不声称其他选择已下发。
4. 活跃源码不存在 `[H2TriggerOutProbe]`、`QElapsedTimer` 诊断残留。
5. FeatureSpec、scanner 与设备实例表恢复到当前分支基线。
6. `git diff --check` 通过；按用户要求不构建、不运行。

## 实施结果

- tracked 源码修改已收敛为 `src/plugins/htra/fancydevice.cpp` 一个文件：继续调用 `device_config_trigger_out()`，固定下发 `state=0, source=0, negative_pulse=0, recounter=1`。
- Trigger Out 页面保持可见；writeback 固定为 Hop / Rising / OFF，与实际下发一致。
- `[H2TriggerOutProbe]`、额外前后查询和耗时日志已清除。
- FeatureSpec 值语义、scanner 候选所有权、设备全局表互斥及其连锁改动已恢复到分支基线。
- 未触碰工作区中既存的 untracked API 示例、说明文档、发布记录及脚本。
- 按用户要求未构建、未运行；完成 `git diff --check` 和源码静态检索后交付。

## 验证级别

- `static`
- 不构建、不运行，由用户自行完成后续实机验证。
