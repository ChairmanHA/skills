# Sweep Single 重复触发修复

## 目标

修复标题栏已经处于 `Single` 状态时再次点击 `Single`，List Mode 只执行第一次、后续点击不再启动一次扫描的问题；同时保持同一条 `SweepCw` 路径下 Freq Sweep、Power Sweep 与 List Mode 的行为一致。

## 已知事实与判断

- 标题栏 `Single` 当前把共享属性 `TriggerCount` 设置为 `1`，并通过 `CommonDeviceProfile::profileChanged` 请求刷新 TX session。
- 第二次点击时 `TriggerCount` 仍为 `1`，新构造的 `TxApplyRequest` 与设备已生效快照相同。
- `TxPipelineRuntime::requestApply()` 会把与 `m_hardwareRequest` 相同的请求视为无需执行，因此不会再次进入 `TxPipelineExecutor::applySweepCw()`。
- Freq、Power、List 在 CW 下共享 `SweepCw` 执行入口，所以相同配置去重会影响三种 Sweep；Playback 的载荷驻留/播放路径不同，不作为 Sweep 重复触发是否正确的依据。

## 范围

- 标题栏需要把已经处于 `Single` 状态时的再次点击表达为明确的一次执行意图。
- `TxSessionService` 在下一次快照刷新中携带该执行意图。
- `TxSessionService` 只把该意图应用到 `SweepCw / SweepPlayback`；已经由实机确认正常的 AM/FM 等 Fixed Playback 不改变行为。
- `TxPipelineRuntime` 对明确的 Sweep 执行意图绕过“相同 desired / 相同 hardware”去重；普通参数刷新继续沿用现有合并策略。
- 若点击发生在设备任务执行中，只保留一个最新待执行请求，继续遵守现有串行 I/O 和 pending-latest 边界。
- 不修改 H2 API、Sweep 参数建模、List Mode 点表或 Playback 载荷逻辑。

## 成功标准

1. 第一次选择 `Single` 时仍以 `TriggerCount = 1` 下发。
2. 已处于 `Single` 时再次点击，会再次进入设备执行链，启动一次 Sweep。
3. Freq Sweep、Power Sweep、List Mode 使用相同的重复触发语义。
4. 普通的等值属性刷新仍可被去重，不导致无条件重复配置。
5. 执行中的再次点击不会并发调用设备，只进入现有 pending-latest 串行队列。

## 计划

1. 核对标题栏点击、session 刷新和 runtime 去重/排队代码。
2. 增加窄范围的显式执行意图，并接入标题栏 `Single` 点击。
3. 静态检查调用点、队列收尾分支和差异格式。

## 验证级别

`static`

未在本任务中编译或连接真实设备；最终仍需在设备上分别验证 Freq、Power、List 的连续两次及以上 `Single` 点击。

## 实施结果

- 标题栏会在点击前记录 `TriggerCount` 是否已经为 `1`；只有再次点击已经选中的 `Single` 才发出显式 Sweep 执行意图，首次从 Continue 切到 Single 的既有路径不变。
- `TxSessionService` 将该一次性意图与下一份完整 request 快照绑定，并只允许 `SweepCw / SweepPlayback + TriggerCount=1` 使用强制执行；Fixed Playback 不受影响。
- `TxPipelineRuntime` 对强制执行绕过相同 desired/hardware 去重；若设备 I/O 正在执行，则保留一个 forced pending-latest，并在当前完成后优先派发。
- forced pending 标志已在 deactivate、materialization、idle、snapshot pending、desired satisfied 和正常 pending 派发等收尾分支中清理。

## 静态检查结果

- `src/plugins/core/CMakeLists.txt` 已包含 `mainwindow.cpp`、`txsessionservice.cpp`、`txpipelineruntime.cpp`。
- 新接口和调用点经 `rg` 核对一致，无遗漏的旧签名调用。
- `git diff --check` 通过；仅有仓库既有的 LF/CRLF 转换提示，无空白错误。
- 按仓库默认验证边界未执行编译和实机运行。
