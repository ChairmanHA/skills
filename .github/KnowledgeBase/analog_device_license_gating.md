# Analog 设备许可证校验与波形生成门控

本文档说明 Analog 插件当前基于设备参数的许可证校验实现，重点覆盖以下问题：

- `FixedLic=true/false` 的真实语义分别是什么。
- 为什么 current device 切换时要先让 Analog 波形生成功能失效，再在设备 open 成功后重新校验。
- 校验结果如何联动到 Analog UI、modulation list、波形生成线程和状态栏 warning。

---

## 1. 总览

当前 Analog 插件把“设备许可证是否允许波形生成”收敛成一条独立状态：

- 状态持有者：`DeviceUtils::DeviceParamsManager`
- 设备连接完成信号来源：`Core::Internal::DeviceRuntimeBridge::deviceConnected`
- UI 联动入口：`Analog::AnalogPlaybackBusiness` + `FancyTabWidget`
- 用户反馈入口：`DeviceInfoWidget` warning 队列

设计目标是：

1. `FixedLic=false` 时，**每次切换设备都重新校验**，不能沿用旧设备结果。
2. 无论设备来自 USB 还是手工 ETH，只在“设备真正 open 成功后”才使用其 `model/uid` 做许可证判断。
3. 当许可证失败时，阻断 Analog 插件保留的数字类波形生成链路，并给用户明确提示。

---

## 2. `FixedLic=true` 与 `FixedLic=false` 的语义

### 2.1 `FixedLic=true`

此模式下，Analog 不依赖当前设备提供 `model/uid`。

- `DeviceParamsManager::m_isUsingDeviceProvider == false`
- `getDeviceParams()` 直接返回内建固定参数：
  - `model = 22`
  - `uid_h32 = 540029490`
  - `uid_l64 = 4776154130035016271`
- `isWaveformGenerationAvailable()` 恒为 `true`

因此：

- 不存在“切设备后重新校验许可证”的运行时行为。
- Analog UI 不会因为 current device 切换而进入临时不可用状态。

### 2.2 `FixedLic=false`

此模式下，Analog 许可证依赖当前已连接设备的真实 `model/uid`。

- `DeviceParamsManager::m_isUsingDeviceProvider == true`
- 初始 `LicenseValidationState == Unknown`
- 只有在设备 open 成功并完成 post-open 校验后，状态才会落定为：
  - `Licensed`
  - 或 `Unlicensed`

这意味着：

- current device 每次切换都必须先把旧设备许可证状态作废。
- 未完成 open 的目标设备，不允许被视为“已确认授权的波形生成设备”。

---

## 3. `DeviceManager::setCurrentDevice()` 的时序

1. 立即更新 `currentDevice`
2. 立即发 `currentDeviceOpenStateChanged(false)`
3. I/O worker 完成 open 后，才发 `currentDeviceOpenStateChanged(opened)`

因此 `currentDeviceOpenStateChanged(false)` 只是“当前目标已切换/已断开”的早期信号，不代表设备已经可用。要等设备打开，再做许可证校验和 UI 同步。

### 3.1 USB 路径为什么看起来似乎可以更早拿到 UID/model

USB 扫描阶段会在 `FancyDevice::updateDeviceInfoFromScan()` 中预先写入：

- `DeviceUID`
- `DeviceUID32`
- `Model`

所以从纯数据字段角度，USB 设备在 open 前就可能已经有身份信息。

但当前实现**仍然不在这个阶段做最终许可证校验**，原因是系统还需要与 ETH 路径保持统一语义，避免一半逻辑挂在 scan-time，一半逻辑挂在 open-time。

### 3.2 手工 ETH 路径为什么绝不能在 `currentDeviceOpenStateChanged(false)` 校验

手工 ETH 设备在 `FancyDevice::configureManualEthTarget()` 中会调用 `resetCachedDeviceInfo()`，把以下字段全部清零：

- `DeviceUID`
- `DeviceUID32`
- `Model`

在 `device_open_eth(...)` 真正成功之前，这些字段都不可用。

因此如果在 `currentDeviceOpenStateChanged(false)` 上就做许可证判断：

- USB 可能“碰巧可用”
- ETH 一定拿到无效身份

这会让两条 transport 语义分叉，也会引入“沿用上一台设备许可证状态”的风险。

---

## 4. 当前真实信号链路

### 4.1 current device 切换时：先作废旧许可证状态

`AnalogModulationPlugin::initialize()` 中把：

- `DeviceManager::currentDeviceOpenStateChanged`
  连接到一个 `isOpen == false` 分支
- 再调用 `DeviceParamsManager::onCurrentDeviceChanging()`

其行为是：

1. 清空 `m_DeviceParams`
2. 若当前为 `FixedLic=false`，把许可证状态改为 `Unknown`
3. 发出 `licenseValidationStateChanged(Unknown)`

这一步的目的不是“判断新设备是否合法”，而是**确保旧设备许可证立即失效**。

注意：

- `Unknown` 只影响 Analog panel 的本地可用性解释；
- 它**不会**触发 modulation list 的 remove/re-add；
- 这样可以避免设备切换和断开时列表反复闪烁。

### 4.2 设备真正 open 成功后：再做 post-open 校验

`DeviceRuntimeBridge::onDeviceOpenStateChanged(true)` 会发出：

- `DeviceRuntimeBridge::deviceConnected(deviceInfo)`

`AnalogModulationPlugin` 在初始化时把它连接到本地同步 lambda：

1. 从 `deviceInfo` 提取 `model/uid_h32/uid_l64`
2. 组装为 `DeviceUtils::DeviceParams`
3. 调用 `DeviceParamsManager::onDeviceConnected(params)`

`onDeviceConnected()` 的行为是：

1. 更新缓存中的 `m_DeviceParams`
2. 发出 `deviceParamsUpdated()`
3. 调用 `checkDeviceLicense(model, uid_h32, uid_l64)`
4. 根据结果把状态更新为：
  - `Licensed`
  - 或 `Unlicensed`
5. 发出 `licenseValidationStateChanged(...)`

只有这个“最终判定”时刻，Analog 插件才会：

- 决定是否隐藏 modulation list 中 Analog 插件保留的数字类 item；
- 同步 DeviceInfoWidget 的许可证 warning。

### 4.3 插件晚加载时的 bootstrap

Analog 插件初始化后，还会主动检查：

- `DeviceManager::currentDevice()` 是否存在
- 且该设备是否已 `isOpen()`

若为真，则立刻使用当前设备的 `DeviceInfo` 做一次同步。

这保证了：

- 即使 Analog 插件加载晚于设备连接完成时刻
- 也不会漏掉当前设备的许可证状态

---

## 5. `DeviceParamsManager` 的职责边界

当前 `DeviceParamsManager` 实际承担三类职责：

### 5.1 保存当前设备许可证参数

- `m_DeviceParams`
- `getDeviceParams()`

当 `FixedLic=false` 时，这里保存的是“最近一次 post-open 成功同步到的设备身份”。

### 5.2 持有“许可证是否已落定”的实时状态

- `m_licenseValidationState`
- `licenseValidationState()`
- `licenseValidationStateChanged(...)`
- `isWaveformGenerationAvailable()`

这里需要区分两层语义：

- `LicenseValidationState`
  - 表示当前许可证判定处于 `Unknown / Licensed / Unlicensed` 哪个阶段；
- `isWaveformGenerationAvailable()`
  - 是一个派生 helper，仅在 `Licensed` 时返回 `true`。

因此当前实现不再把 `Unknown` 与“明确失败”混为同一个 `false`。

### 5.3 作为最后一道运行时防线

即使 UI 已经根据许可证状态做了禁用，`createSignalObjectWithDeviceParams()` 仍会再次：

1. 读取当前参数
2. 重新执行 `checkDeviceLicense(...)`
3. 若失败则返回 `-1`

因此当前实现是“双保险”：

- UI/业务层提前门控
- 真正调用底层生成算法前再次校验

---

## 6. UI 与业务层如何响应许可证状态

`AnalogPlaybackBusiness` 在构造时连接：

- `DeviceParamsManager::licenseValidationStateChanged`
  到
- `AnalogPlaybackBusiness::onLicenseValidationStateChanged(...)`

此外，`AnalogModulationPlugin` 还会监听同一个状态信号，把结果映射到：

- modulation list 中 Analog 数字类 item 的显示/隐藏；
- DeviceInfoWidget warning 的添加/移除。

### 6.1 当状态为 `Unknown`

当前行为：

1. `m_modulatorDataReady = false`
2. 取消 pending 大波形请求（当前仅 trim 请求）
3. panel 保持可见和可编辑：`m_widget->setEnabled(true)`
4. `m_widget->onDataStatusChanged(false)`，仅禁用 `Save Data`
5. 不更新 modulation list

这对应“程序启动后尚未判定”以及“设备切换过程中”的产品状态。

### 6.2 当状态为 `Unlicensed`

当前行为：

1. 调用 `cancelPendingLargeWaveformRequest()`，取消 pending 大波形请求（当前仅 trim 请求）
2. 将 `m_modulatorDataReady` 置为 `false`
3. 对当前 panel 执行 `m_widget->setEnabled(false)`
4. 调用 `m_widget->onDataStatusChanged(false)`，让保存/记录按钮同步失效
5. `onDeviceProfileChanged()` 直接 return，不再下发配置/下载数据
6. `handleModulatorStatusChange()` 只把真正生效状态视为 `status && isWaveformGenerationAvailable()`，因此会忽略“许可证无效期间晚到的旧结果”
7. `AnalogModulationPlugin` 把受许可证控制的 Analog 数字类 item 从 modulation list 中隐藏

若当前正停留在某个被隐藏的 Analog 页面：

- 当前页会回退到 `Playback` / `Streaming` 等仍可见页面；
- 但不会自动把新的 provider 设为 selected business。

### 6.3 当状态为 `Licensed`

当前行为：

1. 当前 panel 重新 `setEnabled(true)`
2. 若已有数据 ready，则 `onDataStatusChanged(true)` 恢复按钮状态
3. 若该业务当前处于 active，则主动调用 `requestGenerateData()` 重新生成一次
4. `AnalogModulationPlugin` 把受许可证控制的 Analog 数字类 item 重新加回 modulation list，顺序保持原注册顺序

因此当前实现的产品语义是：

- 启动和切设备过程中，Analog panel 可以继续浏览和编辑，但 `Save Data` 被禁用
- 只有当新设备许可证最终判定失败后，Analog 数字类 item 才会从 modulation list 中隐藏
- 只有当新设备许可证最终判定通过后，Analog 波形生成链路才会恢复到真正可用状态

---

## 7. Warning 反馈链路

当 post-open 校验失败时：

1. `DeviceParamsManager::onDeviceConnected()` 把状态更新为 `Unlicensed`
2. `AnalogModulationPlugin` 监听 `licenseValidationStateChanged(...)`
3. 通过 `MainWindow` 下的 `DeviceInfoWidget` 调用 `addWarnningMessage(...)`
4. 在状态回到非失败态时，通过 `removeWarnningMessage(...)` 删除该 warning

当前代码里的文案字面量是：

```text
License validation failed, waveform generation unavailable.
```

注意：

- 这条 warning 不走 `DeviceManager::systemMessage`
- 它不是 `Device Open Failed`
- 它属于 Analog 业务层自己的“设备已连通，但不允许波形生成”反馈
- 它复用了 `DeviceInfoWidget` 现有的 warning 队列和循环展示机制，而不是额外弹窗

---

## 8. 与设备类型相关的结论

### 8.1 USB

- 扫描阶段可能已知 UID/model
- 但 Analog 仍统一等到 `deviceConnected` 后再做最终许可证校验

### 8.2 手工 ETH

- open 前身份信息会被清空
- 只能在 post-open 阶段拿到可靠的 UID/model

### 8.3 统一原则

当前系统明确采用：

- `currentDeviceOpenStateChanged(false)`：仅用于“旧许可证失效”和早期 UI 切换
- `deviceConnected`：才用于“新设备许可证最终判定”

同时 UI 侧还采用：

- `Unknown`：不改 modulation list，只更新 Analog panel 的本地状态解释
- `Licensed / Unlicensed`：才真正更新 modulation list 与 warning 展示

这正是为了让 USB 与 ETH 的运行时语义保持一致。

---

## 9. 维护建议

后续若继续演进此链路，建议遵守以下边界：

1. 不要把 USB 的 scan-time 身份信息再次当成最终许可证判定依据，否则会重新引入 USB/ETH 分叉。
2. 若要改 warning 文案或做 i18n，应保持 add/remove 使用同一条文案，避免 DeviceInfoWidget 队列中出现无法移除的残留 warning。
3. modulation list 的显示/隐藏必须继续只挂在“post-open 最终判定”上，不要在 `Unknown` 阶段提前改 list，否则会重新引入闪烁。
4. `createSignalObjectWithDeviceParams()` 的再次校验属于必要兜底，不应因为 UI 已经禁用而删除。

---

## 10. 一句话总结

当前 Analog 许可证逻辑采用“切设备先进入 Unknown、设备 open 成功后再落定为 Licensed/Unlicensed”的统一模型：list 只在最终判定时更新，失败通过 DeviceInfoWidget warning 提示，成功后自动移除该 warning。