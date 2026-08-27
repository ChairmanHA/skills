# RemoteMiniBar 异步设备回写收敛

## Scope

- Main 继续是唯一 property / runtime / device 真相源。
- Minibar 的 Center / Level 编辑立即显示 latest local intent。
- `RSP ok=true` 仅确认 Main 已接受 authoring intent，不再被当作设备配置完成。
- Main 只在当前异步 apply 已完成并经过 runtime generation 过滤后，发布新的
  `tx.applyRevision`；成功时该快照必须位于 `CommonDeviceProfile` 最终 writeback
  之后。
- Helper 收到更高 `applyRevision` 后，以 Main snapshot 中的 Center / Level 最终值
  清除对应 optimistic intent，并同步仍打开的数字键盘。
- Center 与 Level 各自只允许一个 IPC request in flight，期间的快速操作合并为
  latest intent。

本轮不改变 Sweep authoring revision、MOD editor revision、波形生成、TX executor
调度或 Streaming 路径。RF / MOD 已有 enabled 乐观状态仍保持现有 authoring-accepted
确认语义；它们的设备 applied 语义需要结合波形未 ready / 失败状态另行统一。

## Evidence

### Observed

1. `RemoteMinibarService::handleRequest()` 在写入 Center / Level property 后立即返回
   snapshot；异步 executor 尚未完成，因此这个 RSP 是 desired/authoring 值。
2. `TxPipelineRuntime` 仅对当前 epoch / device / desired generation 发布
   `deviceConfigurationDone`，旧 completion 不会进入 Main writeback。
3. `MainWindow` 在收到当前 `deviceConfigurationDone` 后先调用
   `CommonDeviceProfile::setProfile()`，再发出
   `CommonDeviceProfile::deviceConfigurationDone(error)`。
4. 成功的 `TxSessionService::appliedStateChanged()` 位于上述 writeback 之后，并覆盖
   “desired 已等于 effective hardware”的无设备调用 shortcut；失败则由
   `CommonDeviceProfile::deviceConfigurationDone(error)` 明确结束。
5. Helper 当前没有 Center / Level 专用 request correlation；任意普通 snapshot 都能
   覆盖 `m_centerHz` / `m_levelDbm`，并通过一个共享 pending bool 回灌数字键盘。
   快速编辑时，旧 authoring EVT/RSP 会过早消费该 bool，真正 writeback 到达时键盘不再
   同步。

### Inference

- Main runtime 已保证 authoring 变化后，旧 apply completion 不会成为 accepted
  writeback。因此 helper 在 latest request 的 accepted RSP 中记录当前
  `applyRevision`，并等待严格更高 revision，即可把随后完成的 Main snapshot 作为
  latest truth；不需要把 executor generation 或设备对象暴露给 helper。
- 失败路径当前不会把 Main authoring property 回滚到 last successful hardware 值。
  本轮通过 completion revision 结束 helper intent 并与 Main 当前状态一致，不额外查询
  或制造第二套硬件 truth。

## Success Criteria

1. Center / Level 提交后，collapsed、expanded 按钮立即显示 latest local value。
2. 快速连续 Center A -> B -> C 时，每个资源最多一个 IPC request in flight；A 的
   RSP/EVT 不得把 B/C 的按钮或键盘显示回滚。
3. Latest RSP 只进入 awaiting-writeback；只有更高 `applyRevision` 才清 intent。
4. 设备对 Center / Level 做归一化或 clamp 时，最终按钮与仍打开的键盘显示 Main 的
   writeback 值。
5. rejected / timeout / disconnect 在没有 newer intent 时恢复最近的 Main snapshot；
   有 newer intent 时继续提交 latest value。
6. 旧 resource-specific RSP 不得整包覆盖 RF、Sweep、MOD 或另一个 numeric resource。
7. 现有 RF / Sweep / MOD optimistic flow 和 global snapshot sequence filtering 保持。

## Planned Files

- `src/libs/minibaripc/minibarhelperprotocol.h`
- `src/plugins/core/remoteminibarservice.h`
- `src/plugins/core/remoteminibarservice.cpp`
- `src/app/minibarhelper/minibarclient.h`
- `src/app/minibarhelper/minibarclient.cpp`
- `src/app/minibarhelper/remoteminibarwindow.h`
- `src/app/minibarhelper/remoteminibarwindow.cpp`
- `src/app/minibarhelper/main.cpp`
- `.github/KnowledgeBase/minibar_cs_helper_scpi_architecture.md`

## Verification

- Level: static + existing Debug build.
- Confirm modified files remain included by their current CMake targets.
- Confirm Center / Level completion signals cover send failure, RSP, timeout and disconnect.
- Confirm RSP only releases IPC in-flight and a strictly newer Main `applyRevision` performs final
  reconciliation.
- Confirm writeback revision success is published after `CommonDeviceProfile::setProfile()`, while
  the hardware-equal shortcut still advances through `TxSessionService::appliedStateChanged()`.
- Run `git diff --check`.
- Build the existing Debug `SGStudioMiniBar` and Core/main targets available in the configured
  build tree.

## Implementation Result

1. Minibar snapshot 新增 `tx.applyRevision/applySucceeded`。
2. `RemoteMinibarService` 在 main applied/configuration completion 边界推进 revision；
   core-managed 成功路径位于 runtime generation 过滤和
   `CommonDeviceProfile::setProfile()` writeback 之后。
3. `MinibarClient` 为 Center/Level 增加独立 request-id classification，覆盖 RSP、
   send failure、timeout 和 disconnect。
4. `RemoteMiniBarWindow` 为 Center/Level 增加独立 numeric latest-intent：
   每个资源一个 in-flight request，新编辑合并为 latest value。
5. accepted RSP 只结束 IPC in-flight 并记录 baseline revision；普通 authoring
   EVT/RSP 不能覆盖 active intent。更高 apply revision 到达后，按钮和仍打开的键盘统一
   使用 main 最终值。
6. Sweep、MOD/Digital editor 继续使用现有 authoring revision/accepted snapshot；
   同步参数钳位仍在 main authoring 路径完成，波形生成和设备配置保持异步。

## Runtime Verification

2026-07-24 用户实机验证通过：

- Minibar Center 修改可同步并最终收敛。
- Minibar Level 修改可同步并最终收敛。
- SweepPanel 参数修改可同步。
- Digital 参数修改与 Enabled 修改可同步。
- Sweep/Digital 的同步 authoring 钳位耗时很短，交互上无明显等待；该现象不表示后续
  波形生成或设备 I/O 改回了主线程同步执行。

## Hidden Snapshot Scheduling Simplification

### Scope

- 保留 helper READY/显示初始化 snapshot。
- 保留 Minibar 请求对应的 RSP，以及请求引起的异步最终 writeback EVT。
- 保留 Minibar 可见期间来自 Main、SCPI、设备或 business 的权威状态变化 EVT。
- Minibar 已隐藏或正在隐藏时，不再为普通 property/profile/Sweep/MOD 状态变化排队
  debounce snapshot；下次显示时直接从 Main 当前状态重新初始化。
- 不改变设备断开/重连显隐状态机，也不改变请求错误/conflict 的即时权威纠正。

### Evidence

1. `scheduleSnapshot()` 当前只检查 `helperReady()`。Helper 进程在 Main 恢复后仍保持
   READY，因此隐藏期间的普通 property/profile/Sweep/MOD 变化仍会启动 50 ms timer。
2. timer callback 当前无可见性检查；即使 snapshot 在可见期间已排队，只要 Helper
   在 timeout 前进入隐藏流程，旧任务仍会向隐藏进程发送完整 EVT。
3. `miniBarVisibleChanged(false)` 当前还会主动发布一次 snapshot；隐藏 UI 不消费该
   状态，下次显示又会收到新的初始化 snapshot。

### Success Criteria

1. `scheduleSnapshot()` 只在 Helper READY 且 Minibar visible/showing 时启动 timer。
2. timer timeout 再次验证 visible/showing，避免隐藏转换期间发送已排队 snapshot。
3. 收到隐藏状态时停止 pending debounce timer，不再发送隐藏后的可见性 snapshot。
4. `helperReadyChanged(true)` 和 `miniBarVisibleChanged(true)` 的初始化路径保持。
5. Center/Level `applyRevision`、请求 RSP、设备显隐和 conflict/error 路径保持不变。

### Implementation Result

- `scheduleSnapshot()` 增加 visible/showing 门控，隐藏期间的普通变化不再启动 timer。
- debounce timeout 增加第二次 visible/showing 检查，覆盖排队后立即进入隐藏流程的
  时序。
- `miniBarVisibleChanged(false)` 改为停止 pending timer，不再向隐藏 helper 发布
  可见性 snapshot；`true` 仍立即发布初始化 snapshot。
- 直接 `publishSnapshotNow()` 的请求最终确认、设备显隐与纠错路径未修改。

## Level Display Unit Synchronization

### Evidence

1. Helper 的 `PowerUnitAdapter` 已把 `dBmV/dBμV` 输入正确换算成基准 dBm。
2. `RemoteMiniBarWindow` 关闭 Level 键盘时只提交 `adapter->getRealValue()`；
   `adapter->getCurrentUnit()` 被丢弃。
3. `SOUR:POW:LEV` 当前请求只携带 `dbm`，Main 的 Level `currentUnit` 与
   `metadata.displayText` 因而保持旧值，RSP/EVT 又把旧显示单位同步回 Helper。
4. 纯单位切换时基准 dBm 不变，不会触发设备 apply；若仍等待 `applyRevision`，
   numeric intent 将无法正常结束。

### Scope

- 保持 Level 内部值和设备配置单位为 dBm。
- 在现有 `SOUR:POW:LEV` 参数中复用 `levelUnit`，不增加新命令。
- Helper 的 Level latest intent 同时保留基准 dBm 和显示单位；快速编辑仍保持每资源
  一个 request in flight。
- Main 校验显示单位后更新 Level property 的 `currentUnit` 和权威
  `metadata.displayText`，RSP snapshot 同步驱动 Main/Helper 使用同一单位显示。
- 只有基准 dBm 真正变化的请求才等待 `applyRevision`；纯单位变化在 accepted RSP
  即完成。
- 不修改 Center、设备配置、功率换算公式或 PowerUnitAdapter 的单位集合。

### Success Criteria

1. 在 Minibar Level 键盘选择 `dBmV` 或 `dBμV` 并确认后，Main 与 Minibar 均使用该
   单位显示，内部/设备请求仍为等价 dBm。
2. 只切换单位、不改变功率时，不触发无意义设备配置，也不遗留 awaiting-apply intent。
3. 同时修改数值和单位时，RSP 先确认显示单位，随后仍等待设备最终 writeback。
4. 非法单位被 Main 拒绝，不改变 Level value/currentUnit/displayText。
5. Level 快速编辑、超时/拒绝恢复及 Center 现有行为保持。

### Implementation Result

- Level latest intent 新增 `desiredUnit`，键盘的
  `adapter->getCurrentUnit()` 不再丢失；乐观显示同时使用 desired dBm 和单位。
- `MinibarClient::requestLevelChange()` 在现有 `SOUR:POW:LEV` request 中发送
  `dbm + levelUnit`。
- Main 使用 `PowerUnitAdapter::availableUnits()` 校验并规范化单位，随后更新 Level
  `currentUnit` 和 `metadata.displayText`；数值/设备路径仍只使用 dBm。
- Numeric intent 在发送时记录基准值是否真的变化。纯单位请求由 accepted RSP 完成；
  数值变化继续等待更高 `applyRevision`。

### Verification

- 现有 Debug 构建树成功构建 `SGStudioMiniBar` 和 `Core`。
- Qt MOC 的两参数 Level signal/slot、Core 对 Business `PowerUnitAdapter` 的引用均通过
  编译和链接。
- 构建中仅出现仓库已有的编码/STL 弃用 warning，无本次修改产生的错误。
