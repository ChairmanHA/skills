# Retained ETH switch asynchronous ping guard

日期：2026-08-18  
状态：已完成  
验证级别：`static`

## Scope

为切换到已注册、非 current、仍被标记为 open 的 manual ETH 设备增加最小 P0 防护：先异步执行一次系统 `ping`，收到成功结果后才进入现有的“保存源 profile -> suspend pipeline -> switch current -> restore 目标 profile”流程；检查失败时保持 current、profile 和 pipeline 不变，并给出可见提示。

## Evidence

### Observation

- manual ETH 不受 scanner snapshot 管理，成功连接后会保留在 `DeviceManager::allDevices()` 中。
- `FancyDevice::isOpen()` 返回本地缓存状态；非 current ETH 在远端断开后没有轮询机制及时清除此状态。
- `DeviceIoWorker::switchDevice()` 对 `newDevice->isOpen() == true` 的目标跳过 `open()`，因此失效的旧 H2 handle 可能直到 profile restore/配置下发时才被访问。
- Device List、标题栏 A/B 和 ETH Connect 均通过 `DeviceRuntimeProfileCoordinator::requestDeviceSwitch()` 进入现有 profile 切换流程。

### Inference

- 在 coordinator 保存源 profile 和 suspend pipeline 之前做异步 reachability gate，可以在 ping 失败时完整保留当前运行状态，并覆盖 Device List 与标题栏 A/B 两个主要切换入口。
- ICMP 成功只说明目标主机可达，不证明 5000/5001 端口、H2 服务或旧 handle 有效；本改动是 fail-fast P0 防护，不替代后续 endpoint/session 健康检查。

## Design

- 仅检查同时满足以下条件的目标：manual ETH、非 current、当前缓存为 open。新建且尚未 open 的 ETH 继续走现有 SDK open/错误回滚流程；USB 行为不变。
- 使用单个 `QProcess` 异步调用系统 ping，不调用任何 `waitFor*()`；Windows 使用一次 echo 和毫秒超时参数，Linux 使用一次 echo 和秒级 reply timeout，并增加进程级超时。
- reachability check 期间 `isSwitchInProgress()` 返回 true，使 Device List 和标题栏 A/B 保持禁用并拒绝重复请求。
- ping 成功后重新按 UUID 查找注册目标，再进入现有切换主体。ping 失败、超时、进程启动失败或目标已注销时，不保存源 profile、不 suspend pipeline、不调用 `DeviceManager::setCurrentDevice()`。
- 异步失败通过独立信号交给 `MainWindowDeviceController`：ETH Connect 中显示连接失败状态，普通 Device List/A/B 切换使用 `Device Switch` warning。
- 添加聚焦日志，记录 endpoint、UUID、ping 成功或失败原因以及是否继续切换。

## Success criteria

- 对失联的已保留 manual ETH 发起切换时，日志显示 ping 失败，UI 给出提示，current device 不变。
- ping 失败路径中不存在 source profile save、pipeline suspension、active business stop 或 `setCurrentDevice(target)`。
- ping 成功后原有 save/suspend/switch/restore 顺序不变。
- USB、新建 manual ETH、切换到已是 current 且 open 的设备行为不变。
- ICMP 检查不阻塞 GUI 线程，检查期间不能发起第二次 coordinator switch。
- `git diff --check` 和静态引用检查通过；按仓库默认规则不编译、不运行。

## Result

- `DeviceRuntimeProfileCoordinator` 仅对“非 current、缓存为 open 的 manual ETH”启动异步系统 ping；USB、首次 ETH open 和 current no-op 路径保持原样。
- ping 阶段纳入统一 busy 状态，标题栏 A/B 通过 `switchProgressChanged` 即时刷新；重复切换请求会被拒绝。
- ping 成功后才进入原有 profile save、pipeline suspension、业务停止、current switch 和目标 profile restore；失败路径只记录日志并发出 UI warning/ETH Connect 失败状态。
- 新增 `DeviceProfile: ETH ping started/succeeded/failed` 日志，包含 target UUID、IP、端口和失败细节。
- 已同步 `htra_multi_device_stageA_design_and_debug.md` 与 KnowledgeBase Index，明确 ICMP 不验证 5000/5001 endpoint、H2 服务或旧 handle。
- 静态检查通过：Core CMake 已包含全部修改源文件且 Qt Core 可提供 `QProcess`；新增信号均有消费者；`git diff --check` 无 whitespace error。
- 按仓库默认验证规则未编译、未运行；需要在 Release 环境用已连接后断网的非 current ETH 做现场回归。

## 2026-08-18 field-test follow-up

现场验证确认异步 ping 防护有效。产品行为进一步收口为：ping 不通导致切换失败后，按同 IP 共享机箱语义注销仍注册的非 current 5000/5001 manual ETH；失败提示处理完成后再注销，避免 UI 持有已进入异步销毁流程的指针。目标随后只能由用户通过 `ETH Connect` 手动重新创建并打开。

补充成功标准：
- ping 失败提示仍正常显示，current、profile、active business 和 pipeline 不变。
- 同 IP 下全部非 current 5000/5001 从 Device List 和标题栏 A/B 投影中消失。
- 仅注销仍为非 current 的 manual ETH；同 IP current 不被误删。
- 注销复用 `DeviceManager` 既有 registry detach 和 I/O 线程异步销毁，不直接 `delete`。
- 注销前调用不执行设备 I/O 的 `IDevice::markConnectionLost()`；HTRA 实现只更新缓存连接状态，使随后 I/O 线程中的析构 `close()` 跳过失联 ETH 的 `device_preset()`，但仍调用 `device_close()` 释放旧 handle。

## 2026-08-18 shared-chassis unification

现场设计确认 5000/5001 是同 IP 共享机箱的两个 manual ETH endpoint。只删除单个失联对象不足以消除风险：current A 的状态轮询可以释放 A handle，但 retained B 不参与轮询，网络恢复后 ping 成功仍可能让 `isOpen()==true` 的 B 跳过 reopen 并使用断网前的旧 session。

统一设计：
- `DeviceManager` 提供按 IP 清理 5000/5001 manual ETH 组的唯一入口；先对全部候选调用 `markConnectionLost()`，再注销对象，current 始终最后注销，使 UI 收到 current=false 时 registry 中已不存在同组 peer。
- current manual ETH 运行期断联时清理包括 current 在内的整个同 IP 组，保留 UID profile 文件，不重启或自动发现 ETH。
- 非 current ETH ping 明确失败/超时时调用同一入口，但跳过任何同 IP 的 current，清理其余非 current 5000/5001 endpoint。
- 将 USB 特有的 `preferredReconnectUsbKey != 0` fallback 判据扩展为独立的 runtime-fallback pending 状态。任何 current 设备因运行期注销而空缺后，scanner 选出的 USB 都通过 `runtimeFallbackSelectionRequested` 进入 profile coordinator；成功 open 后清除 pending。
- I/O worker 删除非 current 设备时不 invalid 当前 TX executor；只有删除 current 时才 invalidate，避免清理失联 peer 扰动仍在运行的 current pipeline 软件状态。

补充成功标准：
- current A/B 任一路状态轮询确认断联后，同 IP 5000/5001 均从 registry、Device List 和标题栏投影移除，`currentDevice()==nullptr`。
- 非 current endpoint ping 失败后，同 IP 的所有非 current 5000/5001 endpoint 被移除；同 IP current 不被误删。
- 每个待删除 endpoint 都先标记 connection lost，异步析构只做 `device_close()`，不做 `device_preset()`。
- 后续 ETH 只能通过 `ETH Connect` 重新创建并执行 `device_open_eth()`；历史 UID profile 文件保持不变。
- current ETH 断联后若 USB 被选为 fallback，请求必须经过 `DeviceRuntimeProfileCoordinator` 的 suspension/profile restore 流程。

## Final result

- 新增 `DeviceManager::unregisterManualEthGroup()` 作为同 IP 5000/5001 manual ETH 的统一失联注销入口。该入口先对整组调用 `markConnectionLost()`，再异步注销，且 current 始终最后注销。
- current manual ETH 的运行期状态轮询确认断联后，会清理同 IP 的 current 与 retained peer，清空 current；不删除任何 UID profile，也不为 ETH 启动自动发现。
- retained 非 current ETH 的 ping 明确失败或超时后，会清理同 IP 下全部非 current 5000/5001；如果同 IP endpoint 已成为 current，则保留 current。
- 非 current peer 的异步销毁不再 invalidate 当前 TX executor；current 的销毁仍会 invalidate，并由 `FancyDevice::close()` 跳过 `device_preset()`、只释放旧 handle。
- runtime fallback 状态与 USB preferred UID 解耦：无论丢失的 current 是 USB 还是 ETH，scanner 后续选中的 USB 都通过 `DeviceRuntimeProfileCoordinator` 执行 profile restore。两台 USB 中 current 断联时，下一轮 USB 完整快照可自动选择另一台；ETH 因没有发现源，只会被移除并等待手动 `ETH Connect`。
- 静态引用检查和 `git diff --check` 通过；按仓库默认规则未编译、未运行。
