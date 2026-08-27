# 共享机箱 A/B 顺序固件更新流程与概率失败分析

本文总结当前活跃代码中，同一 IP 下 A/B 两个 manual ETH endpoint 顺序调用外部
`updater` 的完整流程，并针对“顺序调用有概率更新失败”的现场现象列出内部可疑点、
取证方法和建议的收敛顺序。

本文只做静态分析，不把尚未取得 updater 目标级日志的推断写成确定根因。

## 1. 结论先行

当前流程不是固定的“A 后 B”流程，而是：

```text
当前选中的 endpoint
  -> 同 IP 下另一个已注册 endpoint
```

所以：

- 当前设备是 A（5000）时，顺序是 `A -> B`；
- 当前设备是 B（5001）时，顺序是 `B -> A`；
- peer 没有注册时，只更新当前 endpoint，不会报告“共享机箱目标不完整”。

## 2. 范围与证据边界

当前 CMake 实际纳入的活跃实现是：

- [updatedialog.cpp](../../src/plugins/updater/updatedialog.cpp)：目标生成、设备断开、
  maintenance 参数与交接；
- [devicemanager.cpp](../../src/plugins/core/devicemanager.cpp)：A/B handle 的串行关闭；
- [progressdialog.cpp](../../src/maintenance/progressdialog.cpp)：多个 updater 子进程的
  顺序执行和成功门控；
- [main.cpp](../../src/maintenance/main.cpp)：maintenance 启动环境；
- [updater changelog](../../updater_files/windows/changelog.md)：外部 updater 已公开的
  参数与 timeout 契约。

`updater.exe` / Linux `updater` 是预编译二进制，仓库没有其实现源码。因此本文可以确认：

- SGStudio 给它传了什么参数；
- 子进程何时启动、何时被认为成功；
- stdout/stderr 和 exit code 如何被消费；
- 随二进制提供的 changelog 声明了什么约束。

但不能仅从当前仓库确认：

- exit code 0 是否保证机箱已经完成重启并重新提供 5000/5001 服务；
- A/B 是否分别拥有全部固件组件，还是共享 Root/Node/BUS 等组件；
- updater 内部对同版本跳过、断线重连、BootSetting、flash erase 和版本复查的精确时序；
- A/B 是否存在供应商规定的强制更新顺序。

这些边界必须通过 updater 目标级日志、供应商契约或实机 A/B 对照补齐。

## 3. 产品身份与目标映射

当前产品约定是：

| 角色 | Endpoint | 当前代码中的识别方式 |
| --- | --- | --- |
| A | 同一 IP 的端口 5000 | `ethEndpoint(ip, 5000)` |
| B | 同一 IP 的端口 5001 | `ethEndpoint(ip, 5001)` |

这个映射也用于标题栏 A/B 投影。更新器没有独立的 shared-chassis 产品对象，而是继续把
A、B 表示为两个 `Core::IDevice*` 和两个 `FirmwareUpdateProcessTarget`。

每个目标最终被展开成五个 updater 参数：

```text
Interface=ETH
DeviceNum=0
IP=<shared-ip>
Port=5000 或 5001
TimeOut=10000
```

目标 key 是 `ETH:<ip>:<port>`，只用于同一批参数内去重。

## 4. 当前完整执行流程

### 4.1 目标生成

[firmwareUpdateTargetsForCurrentDevice()](../../src/plugins/updater/updatedialog.cpp#L307)
执行以下步骤：

1. 读取 `DeviceManager::currentDevice()`。
2. 无条件把 current 转成第一个 updater target。
3. 如果 current 不是 ETH，或端口不是 5000/5001，到此结束。
4. 如果 current 是 5000，则 peer 端口为 5001；current 是 5001，则 peer 为 5000。
5. 遍历 `DeviceManager::allDevices()`，寻找同 IP、peer 端口的已注册对象。
6. 找到后追加为第二个 target，然后停止搜索。

因此顺序由用户点击 Update 时的 current 决定，不是固定角色顺序：

| 点击 Update 时的 current | 实际 target 1 | 实际 target 2 |
| --- | --- | --- |
| A / 5000 | A / 5000 | B / 5001（若已注册） |
| B / 5001 | B / 5001 | A / 5000（若已注册） |
| 单 endpoint | 当前 endpoint | 无 |

### 4.2 参数冻结与设备断开

[buildMaintenanceArguments()](../../src/plugins/updater/updatedialog.cpp#L584) 在断开设备前
冻结 target 参数和待关闭 `IDevice*` 列表。随后：

1. 进入 `firmwareUpdateDisconnectMode`，暂停 scanner 自动选择/重连；
2. 把 current 清空；
3. 在单一 `DeviceIoWorker` 线程中遍历目标设备；
4. 对每个仍为 `isOpen()` 的 endpoint 调用 `device->close()`；
5. 全部遍历完成后发出 `currentDeviceDisconnectFinished()`；
6. UpdateDialog 收到信号后启动 maintenance。

这保证了“关闭调用”是串行完成的，但当前完成信号只代表 worker 已经走完整个 close 循环，
不代表外部 updater 已经能够重新占用设备。

### 4.3 maintenance 交接与主程序退出

UpdateDialog 以管理员权限启动 maintenance，并等待 ready marker。marker 到位后，主程序走
正常关闭流程。

maintenance：

1. 等待父进程退出，最多 15 秒；
2. 超时则强制终止父进程；
3. 父进程退出后固定等待 2 秒；
4. 启动 target 1 的 updater 子进程。

这里的 2 秒只发生一次，位于第一个 updater 之前。它不是 A/B 两次调用之间的等待。

### 4.4 两个 updater 的顺序执行

[startUpdaterProcess()](../../src/maintenance/progressdialog.cpp#L696) 对当前 target 运行：

```text
<updater-path> -y <Interface> <DeviceNum> <IP> <Port> <TimeOut>
```

同一个 `QProcess` 对象被重复使用。每次 finished 后只检查：

```text
exitStatus == QProcess::NormalExit && exitCode == 0
```

如果 target 1 成功，[onUpdaterProcessFinished()](../../src/maintenance/progressdialog.cpp#L777)
把索引加一，并执行：

```cpp
QTimer::singleShot(0, this, &ProgressDialog::startUpdaterProcess);
```

也就是没有 readiness gate，下一次 Qt 事件循环就启动 target 2。

如果任一目标失败：

- 不再执行后续 target；
- 不复制软件包；
- 不自动重启 SGStudio；
- 已成功更新的前序 target 不回滚。

如果所有目标都成功，才进入软件目录复制和应用重启。

## 5. 已确认的内部问题

## 6. 高优先级待验证问题

以下各项是有代码依据的风险，但还不能单独认定为现场根因。

### P1：第一个成功、第二个失败会留下混合固件状态

观察：流程没有事务性回滚。target 1 一旦完成，target 2 失败只会停止软件复制和重启。

风险：A/B 或共享组件可能落在不一致版本组合。用户再次点击 Update 时，updater 是否能安全
从这个组合恢复完全取决于外部 updater，SGStudio 没有显式恢复模型。

必须记录第一次失败后 A/B 各组件实际版本，不能只记录 maintenance 最后一行错误。

### P1：peer 发现依赖点击时 registry 快照

观察：只有 peer `IDevice*` 已存在于 `allDevices()`，才会生成第二个 target。当前没有
“此产品必须存在 A+B 两个目标”的完整性门控。

风险：某次只有 A 被注册时，界面仍可能执行单目标更新并进入软件复制/重启；这不是进程
失败，但会制造部分更新，影响下一次双目标更新结果。

建议取证时同时记录 registry 中 A/B 是否存在、是否 open、UID、IP、port 和最终 target 数。

### P1：close 完成语义弱于“资源已可被 updater 独占”

[closeDevicesForFirmwareUpdate()](../../src/plugins/core/devicemanager.cpp#L802) 调用每个
`device->close()` 后不检查返回值或 `closeError`。当前
[FancyDevice::close()](../../src/plugins/htra/fancydevice.cpp#L842) 也忽略 `errorMessage`，
并在完成本地状态清理后固定返回 true。

推断：如果底层 `device_preset/device_close` 存在延迟释放、共享 socket/线程仍在退出，或者
关闭 A 影响 B 的 handle，`currentDeviceDisconnectFinished()` 仍会按正常路径发出。之后虽然
还有“父进程退出 + 2 秒”，但没有可观测证据证明 updater 获取资源时一定安全。

这一项应通过 close 开始/结束时间、底层返回值和 updater 第一次 open 错误联合判断，不能先
盲目增加多层等待。

## 7. 中低优先级契约风险

### P2：maintenance 改变 cwd，但没有显式设置 updater working directory

[maintenance main.cpp](../../src/maintenance/main.cpp#L49) 把 cwd 改为用户 home；启动 updater
时没有调用 `QProcess::setWorkingDirectory()`。

如果外部 updater 通过可执行文件目录定位 `data/firmware`，此行为没有问题；如果某个版本
依赖 cwd，则会找错资源。由于当前流程能够成功一部分，这不太像首要概率根因，但应向 updater
供应方确认并通过启动日志记录实际 firmware 文件绝对路径。

### P2：没有 updater 进程总时限

`TimeOut` 是传给 updater 的设备通信参数；maintenance 本身没有对整个子进程设置 watchdog。
因此进程异常卡住时不会转成可诊断失败，而会无限停留。它与“概率返回失败”不同，但会影响
完整可靠性统计。

### P2：用户可以关闭 maintenance 窗口

maintenance 的关闭按钮直接关闭窗口，没有显式的 update-in-progress 门控。用户在 updater
运行时关闭窗口可能中止整个进程和子进程。现场测试需要区分内部失败与人工关闭。

## 8. 当前可观测性缺口

现有 maintenance 只显示：

```text
Start firmware updater target 1/2
<external updater merged stdout/stderr>
Start firmware updater target 2/2
...
```

它缺少：

- transaction ID；
- target 的 A/B、IP、port、timeout；
- 每个阶段的绝对时间和 elapsed time；
- target 1 退出到 target 2 启动的真实间隔；
- close 每个 endpoint 的开始、结束和底层结果；
- updater 二进制 SHA-256 / 版本标识；
- 失败后 A/B 实际版本；
- 持久化日志文件。

`qDebug()` 中虽然记录 exit code/status，但 Windows GUI maintenance 通常没有可靠控制台，
当前目录也没有安装 file logger。只靠错误窗口截图不足以判断失败是在 open、query、erase、
transfer、reboot 还是 verify。

建议下一轮先补一条结构化目标时间线，至少包含：

```text
transaction=<uuid>
target=1/2 role=A endpoint=192.168.1.100:5000 timeout_ms=30000
close_begin / close_end
process_start utc=<...>
process_finished utc=<...> elapsed_ms=<...> exit_code=<...> exit_status=<...>
next_target_start_gap_ms=<...>
```

外部 updater stdout/stderr 应按 target 分段原样持久化，并限制单文件大小；不要只保存在
`QPlainTextEdit` 中。

## 9. 下一次失败的判别表

| 现场特征 | 更支持的假设 | 需要的证据 |
| --- | --- | --- |
| 单 A、单 B 也会在约 10 秒失败 | timeout 不足 | updater 阶段名、elapsed、错误码 |
| target 1 稳定成功，target 2 才失败 | 共享机箱恢复竞态 | 两目标时间线、target 2 首个 open 错误 |
| 仅 `B -> A` 明显更差 | 顺序存在隐含契约 | current role、实际 target 顺序 |
| 增大 ETH timeout 后显著改善 | timeout 主因 | 同机箱同版本 A/B 对照统计 |
| 增加目标间隔后改善，timeout 调整无效 | 恢复窗口主因 | exit-to-next-start gap 与成功率 |
| 日志显示只有 target 1/1 | peer 未进入 registry | 点击 Update 时的设备快照 |
| target 1 成功后版本已变化，target 2 失败 | 混合状态 | 失败后 A/B 全组件版本 |
| updater 找不到 firmware 文件 | cwd/资源定位契约 | updater 实际资源绝对路径 |

## 10. 建议测试矩阵

在可恢复的实验机箱上执行，避免在现场设备上无计划重复擦写 flash。

### 10.1 基线维度

| 维度 | 组合 |
| --- | --- |
| 单目标 | A only、B only |
| 双目标顺序 | A -> B、B -> A |
| ETH timeout | 10 s、供应商下限以上（建议先用 30 s 做诊断） |
| 目标间策略 | 立即启动、固定诊断间隔、ready 条件驱动 |
| 初始版本 | 全部相同、A/B 不同、一次失败后的混合状态 |
| 网络状态 | 稳定直连、正常交换机链路、受控延迟/丢包实验 |

固定间隔只作为验证“是否存在恢复窗口”的实验变量，不应未经数据直接固化为产品方案。

### 10.2 每次必须记录

- 包版本、固件 profile、updater SHA-256；
- current role 和实际 target 顺序；
- A/B 点击前是否都 registered/open；
- 两组完整参数；
- close、父进程退出、每个 updater start/finish 时间；
- 每个 updater 完整输出、exit code、exit status；
- 失败后 A/B 的 MCU/FPGA/BUS/EIO 实际版本；
- 是否需要断电、再次运行 updater 或手工恢复。

### 10.3 建议的最小对照顺序

1. 保持当前立即串行，只把 ETH timeout 从 10 秒提高到供应商下限以上，验证直接矛盾。
2. timeout 修正后分别统计 `A -> B` 与 `B -> A`。
3. 如果仍只在 target 2 失败，再对照“立即启动”和“ready 条件驱动”。
4. 只有确认同一种瞬态错误可恢复后，才设计有限次数、限定错误类型的 retry。

## 11. 建议整改阶段

### Phase 0：先补证据

- 持久化每个 target 的参数、时间、输出与结果；
- 明确 A/B 顺序；
- 记录 updater hash；
- 失败后禁止只凭总 exit code 下结论。

### Phase 1：修正已知 timeout 契约

- ETH 使用不低于 updater 要求的 timeout；
- USB 保持独立配置，不假设和 ETH 相同；
- timeout 值应与随包 updater 版本形成明确发布契约。

### Phase 2：建立共享机箱 target 模型

- 点击更新时明确要求 A/B 两个 endpoint 是否必须完整存在；
- 明确产品顺序是固定 `A -> B`，还是允许 current-first；
- 如果 A 是 canonical endpoint，应把规则写进产品模型而不是依赖 UI current；
- 启动 maintenance 前输出最终冻结的 target plan。

### Phase 3：用 readiness 取代猜测等待

- 向 updater/API 供应方确认 exit code 0 的完成语义；
- 定义 target 1 完成后共享机箱重新可更新的可靠条件；
- readiness 超时应报告具体 endpoint 和最后错误；
- 不要把 ping 成功直接等同于 updater-ready。

### Phase 4：只对已证明可恢复的错误增加有限 retry

retry 必须满足：

- 只覆盖明确的瞬态 open/timeout 类错误；
- 有最大次数和总时限；
- 每次 retry 前重新经过 readiness；
- flash erase/transfer/verify 等未知中间状态不能盲重试；
- target 1 已成功、target 2 最终失败时明确报告混合状态和恢复步骤。

## 12. 当前不建议直接做的改动

- 不先取证就叠加多个固定 sleep；
- 不对所有非零 exit code统一重试；
- 不用 `ping` 作为唯一恢复条件；
- 不在 peer 缺失时静默假装双目标更新完成；
- 不把 updater exit code 0 直接提升为“A/B 全部固件已验证一致”；
- 不在缺少供应商组件归属说明时猜测 Root/Node/BUS 是否应更新两次。

当前最合理的收敛路径是：先补目标级持久日志，同时修正 ETH 10 秒 timeout 与 25 秒下限的
明确冲突；再用顺序和 readiness 对照确认第二个概率失败是否来自共享机箱恢复窗口。

