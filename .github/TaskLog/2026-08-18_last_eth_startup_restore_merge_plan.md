# Last ETH 启动恢复与 6b59ad1 合并方案

日期：2026-08-18  
状态：分析完成，待实施  
目标提交：`6b59ad1620ecc790c4b0b884d71673601ffd5be4`  
当前分支：`integration/api30-multidevice-into-dev`  
验证级别：`static`

## Scope

在不引入 ETH scanner、后台保活、自动发现或 A/B 会话持久化的前提下，记录最后一次成功成为 current 的连接类型；若最后一次成功连接为 manual ETH，则单实例冷启动时对该 endpoint 做一次新的 `device_open_eth()` 尝试。

连接目标恢复与业务状态恢复保持正交：

- `Settings.ini [Device]` 保存最后成功 transport 和 ETH endpoint；
- `Profile.json` 继续按既有 `APP/StartSetting` / `APP/Reboot` 规则恢复业务状态；
- 不向 `Profile.json` 增加 endpoint 或 current-device 字段；
- 不使用退出时删除的 `configuration/device_profiles/*.json` 作为跨进程启动依据。

## Current observations

1. `MainWindow::closeEvent()` 每次退出都会写 `configuration/Profile.json`；语言/主题重启还会设置 `APP/Reboot=True`，新进程因此强制加载该文件。
2. 普通冷启动仅在 `APP/StartSetting=Last` 时加载 `Profile.json`；`Default/User` 继续维持各自语义。设备 transport 选择不应改变 Power On State。
3. HTRA plugin 在启动 USB scanner 前已经注册 manual ETH factory。目标提交在这个时点创建最后一个 ETH endpoint，可以避免 USB 先抢占 current。
4. 当前 manual ETH 首次 open 失败不会自动 retry，但对象会继续占据 current。若照搬目标提交，USB scanner 无法 fallback。
5. 当前所有运行期显式切换必须经过 `DeviceRuntimeProfileCoordinator`。启动时不存在源设备、源 profile 或待停止的 Streaming，因此初始 ETH binding 可以沿用 USB 启动的直接 `DeviceManager::setCurrentDevice()` 语义。
6. 软件目录复制更新会保留 `configuration/Settings.ini`，但当前 `CopyThread` 不回灌 `configuration/Profile.json`；固件-only `Local Default` 不复制目录，因此不受此缺口影响。

## Recommended behavior

### Cold start and UI restart

- 最后成功 connection 为 USB：保持现有 USB scanner 自动选择。
- 最后成功 connection 为 ETH：仅单实例启动时，在 scanner 启动前创建并打开最后成功的一个 `{IP, Port}`。
- endpoint 只接受严格 IPv4 和当前产品支持的端口 `5000/5001`。
- open 成功：该设备成为 current 并进入 Device List；之后由既有 `Profile.json` / Default / User 启动设置完成配置。
- open 失败：立即注销这个仅用于启动恢复的临时 manual ETH，清空 current；不调用 `markConnectionLost()`，因为它不是已打开后失联的旧 handle；后续 USB fallback 必须走当前 `runtimeFallbackSelectionRequested -> DeviceRuntimeProfileCoordinator` 链路。
- 失败后不做 ping、不做 retry、不保留 UID=0 条目；显示一次现有 open-failed 提示。保存的 ETH endpoint 不主动清除，没有 USB 成功覆盖时，下次冷启动仍可再尝试一次。

### A/B shared chassis

- 本阶段只恢复重启前最后 current 的一个 endpoint，不根据端口推断并自动打开同 IP peer。
- 即使重启前 5000/5001 都已打开，重启后标题栏 A/B 也只在用户再次手工连接 peer 后恢复展示。
- 自动恢复整组 A/B 需要持久化“已打开 endpoint 集合”和批量 open/失败回滚，不属于目标提交的单 endpoint 模型，本阶段不扩展。

### Update restart

- `--UpdateCompleted` 使用同一套单 endpoint、单次 open 策略，不增加第二条 post-update 连接流程。
- updater 已保留 `Settings.ini`，因此最后 ETH endpoint 可跨软件更新保存；firmware updater 返回后若设备已恢复服务，应用会自动重连。
- 若设备仍在重启窗口内导致 open 失败，按普通启动失败处理并要求用户手工 ETH Connect；本阶段不增加延时和重试状态机。
- 软件复制更新当前不保留 `Profile.json`，所以只能承诺“尝试恢复 ETH 连接”，不能承诺“恢复更新前业务参数”。如要保证后者，应单独修复 `CopyThread` 的运行期文件回灌并评估 profile format 兼容性，不混入本次 cherry-pick。

## Merge strategy

建议使用 `git cherry-pick -n 6b59ad1620ecc790c4b0b884d71673601ffd5be4` 后人工收敛，不直接接受冲突文件的 theirs。

### Accept with minor adaptation

- `src/libs/utils/settings.cpp/.h`
  - 接受最后成功 USB/ETH 写入接口。
  - 继续只在成功 open 后写入，避免保存失败目标。
- `src/plugins/core/deviceruntimebridge.cpp/.h`
  - 接受成功连接记录。
  - 保留“进程启动时已有其他实例则不写共享 Settings.ini”的稳定 owner 规则。
- `src/plugins/core/ethconnectdialog.cpp/.h`
  - 接受上次 ETH 地址预填。
  - saved port 只有在 `5000/5001` 时才预填，否则回到产品默认端口。
- `src/libs/utils/stringutils.cpp/.h`
  - 只新增 `isTcpPort()`；保留当前严格 `isIPv4Format()`，不采用目标提交更宽松的 `QString::toInt()` 版本。
- `src/plugins/htra/fancydevice.cpp`
  - 仅人工合入共享 IPv4 校验；保留当前断联、`markConnectionLost()` 和 handle 生命周期实现。

### Resolve manually

- `src/plugins/htra/plugin.cpp`
  - 人工加入启动 ETH restore helper，并放在 scanner `start()` 之前。
  - 保留当前 API metadata、业务注册和 shutdown 代码。
  - 在 helper 中增加 `5000/5001` 限制和启动临时对象的异步失败清理。
  - 不在启动恢复前调用 ping；fresh `device_open_eth()` 才是服务可用性的最终判据。

### Keep current branch implementation

- `src/plugins/core/mainwindowdevicecontroller.cpp/.h`
  - 完整保留当前 `DeviceRuntimeProfileCoordinator`、provisional ETH、异步 ping、同 IP group 清理和 Device List 统一入口。
  - 拒绝目标提交的 `releaseEthConnectDialogDevice()` 方案；该代码来自 coordinator 引入前的生命周期模型，可能注销仍在异步切换中的目标并破坏 profile suspension/rollback。

## Success criteria

1. USB 成功成为 current 后，`Settings.ini [Device]/Interface=USB`；ETH 成功成为 current 后写入 `Interface=ETH + Address + Port`。
2. 语言/主题重启前 current 为 ETH 时，新进程只打开最后 current endpoint，并按 `APP/Reboot=True` 加载刚保存的 `Profile.json`。
3. 普通冷启动遵循原有 `Default/Last/User` 配置语义，但连接目标仍按最后成功 transport 选择。
4. 启动 ETH 不可达时，不留下 current/manual ETH 条目，不阻塞 USB fallback，不发生循环 retry。
5. 运行期 USB/ETH 切换、retained ETH ping、断联同 IP group 注销和 profile coordinator 行为无回归。
6. 双 ETH 重启只恢复一个 endpoint，不隐式创建 peer，不错误显示标题栏 A/B。
7. `--UpdateCompleted` 可复用保存的 ETH endpoint；软件更新前业务状态是否恢复作为 `Profile.json` preservation 的独立问题记录。

## Static verification

- 检查 cherry-pick 后仅目标范围文件发生变化，冲突文件没有丢失当前 coordinator/断联逻辑。
- `rg` 确认所有运行期 Device List/ETH dialog 切换仍调用 `requestDeviceSwitch()`。
- `rg` 确认启动 restore 是唯一允许直接 `setCurrentDevice()` 的新增 ETH 路径，并发生在 scanner 启动前。
- 检查启动失败注销后现有 scanner snapshot 能通过 runtime fallback signal 进入 coordinator。
- 运行 `git diff --check`；按当前任务要求不编译、不运行。
