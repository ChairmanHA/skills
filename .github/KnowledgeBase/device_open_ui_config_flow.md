# 设备打开 → UI 更新 → 按当前 Business 配置设备（现状流程梳理）

本文基于当前工程源码的真实调用链路，总结“设备被发现/设为当前设备后，UI 如何更新，以及如何根据当前 UI 业务模式（business）去配置设备”的完整流程。

> 结论先说（更新于 2026-07-15）：
> - **“按当前 UI business 去配置设备”这个意图是成立的**：配置入口在 `IBusiness::startBusiness()/onDeviceProfileChanged()`，最终调用到 `IDevice::configuration()`。
> - `BusinessManager` 先同步消费 `currentDeviceCapabilitiesChanged(...)`，完成全部 business reconcile；随后 `currentDeviceOpenStateChanged(bool)` 才负责 start/stop。

---

## 1. 参与角色（谁负责什么）

- `Core::DeviceManager`
  - 维护全局 `currentDevice` 和 capability revision；设备切换时先发布 unavailable snapshot，open 成功后发布最终 snapshot，再发 `currentDeviceOpenStateChanged(true)`。
- `Core::Internal::FancyTabWidget` + `MainWindow::selectBusiness2Work()`
  - 由 UI（RF/Mod 开关、调制模式选择）决定“当前应该激活哪个 business”。  - 注意：`selectBusiness2Work()` 属于 `MainWindow`（full UI host）专属，minibar 模式不走这条路径。
- `Core::Internal::TxSessionService`
  - 负责 TxPipelineRuntime 的统一裁决（resolve / buildApplyRequest / applyResolvedPipeline）。
  - 属于共享运行时层，由 `CoreRuntimeServices` 创建，minibar / MainWindow 两种模式均可用。
  - 直接监听 `RF.editingFinished` / `Mod.editingFinished` / `CommonDeviceProfile::profileChanged`，自动触发 `requestRefresh()`。
- `Core::BusinessManager`
  - 维护 `activedBusiness`（当前激活业务）。
  - 同步把同一 capability snapshot 分发给全部 business，完成后通知 TxSession 重建 request。
  - 只有 capability usable 才调用当前激活业务的 `startBusiness()`；否则调用 `stopBusiness()`。
- `Core::IBusiness`
  - 业务抽象基类。`setActive(true)` 会调用虚函数 `startBusiness()`。
  - 构造函数里会把 `CommonDeviceProfile::profileChanged` 连接到 `IBusiness::onDeviceProfileChanged()`。
- `Core::Internal::CommonDeviceProfile`
  - 用 PropertySystem 的 `ExternalPropertyMapping` 将 UI 参数（Center/Level/Trigger...）映射为一个全局“公共设备配置”。
  - 当公共参数组变化时发出 `profileChanged()`，用于驱动业务重新下发配置。
- `Core::DeviceOperator`
  - 业务侧对设备的薄封装：`configuration()` 最终调用 `DeviceManager::currentDevice()->configuration(...)`。

---

## 2. 设备发现与注册（以 HTRA 为例）

### 2.1 设备被发现

现状（更新于 2026-08-12）：
- `HTRADeviceScanner` 周期调用 `device_list_usb()` 获取 present 设备快照。
- 对每个枚举项创建一个未打开的 `FancyDevice` 候选，并调用 `FancyDevice::updateDeviceInfoFromScan(info)` 注入 UID/model/dnum；scanner 不复用或跨线程重发 `DeviceManager` 已拥有的裸指针。
- 发现结果通过 `devicesDiscovered(QList<IDevice*>)` 发送给 `DeviceManager`。
- `DeviceManager` 按 UID 对账：已有 UID 保留已注册实例并销毁候选，新 UID 才接管候选；真正 open 前 `FancyDevice` 会再次按 UID 枚举并刷新 dnum。

重要语义：发现阶段不执行 open，retain/open/close 由 `DeviceManager::setCurrentDevice()` 控制。阶段 A 同一时刻仅有一个 `currentDevice` 接受 UI/Business 控制和状态轮询，但切到非空目标时，任意已打开的旧 USB/ETH 都继续保持 open，让已下发 Playback 自主输出。

### 2.2 首次注册时自动设为当前设备

`DeviceManager::registerDevice()`：
- 第一个设备注册进来时会执行 `setCurrentDevice(device)`。

---

## 3. 设为当前设备时，哪些信号会发出（关键）

`DeviceManager::setCurrentDevice(device)` 做了这些事（现状，更新于 2026-02-24）：

1) 如果设备没注册过，会先 `registerDevice(device)`。
2) 如果 `device == currentDevice` 且设备已 open：直接 return；否则视为一次“重试 open”。
3) **立即更新** `currentDevice = device`（用于 Connect 菜单勾选、UI 上下文切换）。
4) **停止状态轮询 timer**，避免切换过程中并发设备 I/O。
5) **不在 UI 线程执行** `open()/close()`：把 retain/close(old) 与 open(new) 投递到 `DeviceStatusUpdateThread` 上的 I/O worker 串行执行；目标非空时 retain 已打开旧设备，目标为空时才 close(old)。
6) **信号时序（异步）**：
  - 立即发布新的 unavailable capability revision，再 `emit currentDeviceOpenStateChanged(false);`
  - I/O worker open 成功后，`FancyDevice` 先调用 `device_query_options()`；`OPTION_BW_320M_TX` 决定 Playback 为125 MSPS/125 MiB基线，或200 MSPS连续档 + 400 MSPS单点/1000 MiB扩展档
  - `device_query_options()` 失败时记录 warning 并使用保守基线，不按 model 推测扩展能力
  - `FancyDevice` 随后调用 `device_config_power_state()`：PGA 单口供电（`PGA_PowerSourceType == 1`）默认 `POWEROFF` / Low Power ON，其他设备默认 `POWERON` / Low Power OFF
  - 随后发布最终 capability revision 并同步完成 business reconcile
  - 最后 `emit currentDeviceOpenStateChanged(true);`

补充（2026-03-25）：
- 若 open 阶段失败码为 `-8`，`DeviceManager` 不会立即把失败升级成 UI error。
- 当前策略会进入 8 秒 grace period，并继续沿用 status timer 的 retry-open。
- 若 grace period 内成功打开，则只会看到 `currentDeviceOpenStateChanged(false -> true)` 的正常连接切换，不会出现 `Device Open Failed`。
- 若 grace period 超时后仍未成功，才会走现有 `Device Open Failed` 消息链路。

> 这一步是后续“UI 更新”和“自动配置设备”的共同触发点。

---

## 4. UI 更新链路（MainWindow 收到什么）

### 4.1 `currentDeviceOpenStateChanged(false)` → 先更新 FeatureSpec，并清空运行时态

当前由 `DeviceRuntimeBridge` 监听 `DeviceManager::currentDeviceOpenStateChanged(bool)`。

当 `isOpen == false` 时，它会：
- 读取 `DeviceManager::currentDevice()`
- 用当前设备或 `nullptr` 刷新 `refClockSourceSpecs()/triggerSourceSpecs()/rfPortSpecs()` 等 FeatureSpec 值快照，喂给 `CommonDeviceProfile::setFeatureSpec(...)`；Core/UI 不保存设备内部 spec 指针
- 清空 `DeviceInfo` / `DeviceRealTimeStatus` property

因此当前真实代码里的“早期切换”语义已经收敛为：
- `currentDeviceOpenStateChanged(false)` + `DeviceManager::currentDevice()` 非空：当前目标已切换，设备仍在连接中
- `currentDeviceOpenStateChanged(false)` + `DeviceManager::currentDevice()` 为空：当前设备已断开/解绑

### 4.2 `currentDeviceOpenStateChanged(true)` → 设备真正 open 完成后的 UI/业务同步

`DeviceRuntimeBridge::onDeviceOpenStateChanged(true)` 目前会：

- 刷新 `DeviceInfo` Property。
- 发出 `deviceConnected(deviceInfo)`。

`MainWindowDeviceController::onDeviceOpenStateChanged(true)` 则负责刷新设备信息展示与连接中提示文案。

因此，纯 UI/设备信息逻辑可继续挂在 `currentDeviceOpenStateChanged(true)` 或其派生的 `deviceConnected`；需要采样率/容量能力的 business 应覆写 `IBusiness::applyDeviceCapabilities()`，不能自行读取 model，也不能把 `currentDeviceOpenStateChanged(false)` 当作“设备已连接完成”。

#### 4.2.1 Analog 许可证门控（2026-04-17）

Analog 插件当前已经明确把两阶段拆成两种语义：

- `currentDeviceOpenStateChanged(false)`
  - 只用于“旧设备许可证状态立即失效”和早期 UI/FeatureSpec 切换。
- `deviceConnected`（来自 `currentDeviceOpenStateChanged(true)`）
  - 才用于读取当前设备的 `model/uid` 并执行最终许可证校验。

这样做的原因是：

- USB 扫描阶段虽然可能已经知道 `uid/model`，但手工 ETH 在 open 前会清空这些身份字段。
- 若把许可证判定挂在 `currentDeviceOpenStateChanged(false)` 上，会重新引入 USB/ETH 语义分叉。

详见 [analog_device_license_gating.md](analog_device_license_gating.md)。

### 4.3 `currentDeviceOpenStateChanged(true)` → 刷新设备信息展示

`MainWindow` 也连接了：
- `DeviceManager::currentDeviceOpenStateChanged` → lambda

当 `isOpen == true` 时：
- 更新 `m_deviceInfoWidget->updateDeviceInfo(...)`
- 清空错误码等状态

> 你删除了 `emit CommonDeviceProfile::instance()->profileChanged();` 这类显式触发后，UI 仍然会更新，因为 UI 更新主要靠以上两条信号链路 + PropertySystem。

---

## 5. “按当前 UI business 配置设备”的来源（确实存在）

### 5.1 UI 如何决定当前 business

`MainWindow::selectBusiness2Work()`（由 RF/Mod 属性编辑完成、或调制模式选择变化触发）逻辑是：
- RF=Off → 强制 `MuteBusiness`
- RF=On 且 Mod=Off → `ContinuesWaveBusiness`
- RF=On 且 Mod=On → 使用 `FancyTabWidget::selectedBusiness()`，若未选中则回落到 `MuteBusiness`

此外，`selectBusiness2Work()` 完成业务选择后也会调用 `TxSessionService::syncTxSessionState()`，将当前激活的 selectedBusiness / sweep context 注入 TxSessionService，供其下次 `requestRefresh()` 时构建正确的 `TxApplyRequest`。

补充当前 UI 收口规则：

- `MainWindow::updateSweepAndBusinessAvailability()` 会先检查当前是否存在已启用的 baseband business。
- 如果不存在，则会先把公共 `Mod` 强制收敛到 `false`，并禁用 `CommonPanel` 上的 `MOD` 按钮。
- 因此“RF=On 且 Mod=On 但没有已启用调制业务”在当前版本不会稳定停留为可操作 UI 状态，而会回收到 `Mod=Off` 的公共参数状态。

也就是说，“当前 UI 的 RF/Mod/调制模式选择”决定了哪个 `IBusiness` 会被 `setActive(true)`。

### 5.2 business 激活后，谁会真的下发设备配置

以 `MuteBusiness` 为例：
- `startBusiness()` 直接调用 `onDeviceProfileChanged()`。
- `onDeviceProfileChanged()`：
  1) `getCurrentProfile(&profile)` 从 `CommonDeviceProfile` 读取 center/level/trigger...
  2) 填上 `profile.mode`（Mute/CW/...）
  3) 调用 `m_operator.configuration(profile, &writeback, &errorStr)`
  4) 成功则 `emit deviceConfigurationEnd(writeback, errorStr)`

`DeviceOperator::configuration()`：
- 检查 business 仍处于 active
- `DeviceManager::currentDevice()` 不为空
- 调用 `currentDevice->configuration(input, writeback, errorMessage)`

**因此：配置确实是由“当前 active business”驱动的。**

---

## 6. 设备配置完成后，如何回写 UI（Device → UI）

`IBusiness` 构造函数里有一个关键连接：

- `connect(this, &IBusiness::deviceConfigurationEnd, CommonDeviceProfile::instance(), ...)`
- 若无错误，会调用 `CommonDeviceProfile::setProfile(center, level, triggerCount, triggerSource)`。

`CommonDeviceProfile::setProfile()` 会通过 `ExternalPropertyMapping::updateByExternal` 把值写回到 PropertySystem，从而驱动绑定到这些 property 的 UI 控件刷新。

---

## 7. UI 参数变化时，如何触发重新配置（UI → Device）

即使你删掉了 MainWindow 里显式的 `emit CommonDeviceProfile::profileChanged()`，系统仍然会在用户改动公共参数时触发配置：

- `CommonDeviceProfile` 内部把 `ExternalPropertyMapping::groupChanged` 连接到了一个 lambda：
  - `updateExternal<CommonPanelProfile>()` 把 property 值拉进 `m_profile`
  - `emit profileChanged()`

当前主线中，`CommonDeviceProfile::profileChanged` 的统一 apply owner 是：

- `TxSessionService::requestRefresh()`

`IBusiness` 基类已不再统一把这个信号转发给 `onDeviceProfileChanged()`；只有仍然需要 legacy session 内部重配的具体业务会显式订阅，例如 `StreamingBussiness`。

### 7.1 补充：加载配置文件时，不是靠 `restoreSettings()` 直接配置设备

这个区别需要单独记住，因为它很容易在排查“为什么加载配置后设备参数变化了”时被误判：

- `CommonDeviceProfile::restoreSettings()` 本身只恢复 common profile 并刷新 property/UI，不会直接发出 `profileChanged()`。
- `MainWindow::loadSettingsFile()` 的做法是：恢复 common 配置后，先终止当前激活业务，再调用 `selectBusiness2Work()`，通过重新激活业务来完成一次设备重新配置。

因此当前“加载配置文件”的真实语义是：

- 不是单纯 UI 回显。
- 也不是 `restoreSettings()` 自己直接触发设备配置。
- 而是“恢复 common 状态后，再通过业务激活链路重走一次配置”。

进一步地：

- 如果当前设备已 open，则这条链路会立即尝试重新下发设备配置。
- 如果当前设备尚未 open，则会先恢复 UI/业务状态；等设备后续 open 时，`BusinessManager` 仍会根据 `currentDeviceOpenStateChanged(true)` 再触发当前激活业务执行配置。

---

## 8. 重要问题：设备切换时会“配置两次”

更新于 2026-02-12：

当前业务启动/停止主要由 `currentDeviceOpenStateChanged(bool)` 驱动：

- `isOpen == true`：`startBusiness()`
- `isOpen == false`：`stopBusiness()`

这里的关键边界是：

- `Mute/CW` 会在 `startBusiness()` 内部直接调用 `onDeviceProfileChanged()` 完成 legacy 直配；
- `StreamingBussiness` 的 `startBusiness()` 会 rearm sender/generator 线程，并通过内部 `m_profileChanged` 路径触发设备重配；
- 其余 provider/playback 业务并未提供独立的 legacy `onDeviceProfileChanged()` 实现；设备 open 后由 capability reconcile 驱动 `TxSessionService` 重建 request，不再由 runtime 重放缓存 request。

因此当前版本在“切换 current device”时通常不会再出现两次 `startBusiness()` 的重复触发。

---

## 9. 时序图（简化版）

```text
[设备发现线程] HTRADeviceScanThread
    └─ HTRADeviceScanner::scan()：device_list_usb() 枚举快照
    └─ DeviceManagerPrivate::onDevicesDiscovered()：快照对账（add/remove）
       ├─ 启动期首选 → DeviceManager::setCurrentDevice(device)
       └─ 运行期 USB fallback → runtimeFallbackSelectionRequested(device)
          └─ DeviceRuntimeProfileCoordinator：suspend → setCurrentDevice → restore target UID profile → refresh
         ├─ target non-null → retain old open；target null → close old
         ├─ new closed → open
         ├─ publish unavailable capability revision
         ├─ emit currentDeviceOpenStateChanged(false)
         │     └─ DeviceRuntimeBridge: 刷新 FeatureSpec 并清空运行时 property
         ├─ publish final capability revision
         │   └─ BusinessManager: synchronous reconcile; TxSession request remains gated
         └─ emit currentDeviceOpenStateChanged(true)
           ├─ BusinessManager: capability usable → startBusiness()
           │   └─ emit capability ready → TxSessionService rebuild request
           └─ MainWindow lambda: 更新 UI

startBusiness()
    └─ onDeviceProfileChanged()
         └─ DeviceOperator::configuration()
              └─ currentDevice->configuration(...)
                   └─ emit IBusiness::deviceConfigurationEnd(writeback)
                        └─ CommonDeviceProfile::setProfile(...)
                             └─ PropertySystem 更新 → UI 刷新
```

---
