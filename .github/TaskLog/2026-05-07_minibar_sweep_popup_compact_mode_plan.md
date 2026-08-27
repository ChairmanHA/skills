# Minibar Sweep Popup / Compact Mode 实施计划

## 本次目标

本次在不新增第二套 sweep 业务 UI 的前提下，完成以下三件事：

1. 把 minibar 的 sweep 按钮从 placeholder popup 替换为真实的 `StepSweepPanel` popup
2. 给 `StepSweepPanel` 增加一个仅面向 minibar popup 的 compact 模式
3. 给 minibar 补齐到 `TxSessionService` 的最小接线，让 `RF + Sweep` 组合能走共享发射链

## 设计判断

### 1. 复用边界

`StepSweepPanel` 里真正值得复用的是：

1. `carrierPlanKind()`
2. `fillCarrierPlanContext(...)`
3. `previewCurrentCarrier(...)` 与设备 reopen 后的 normalize/writeback 逻辑
4. property binding 与 sweep 参数编辑信号

不复用它的默认页面皮肤；minibar 只复用其编辑器与 carrier context provider 角色。

### 2. Minibar host 边界

`MiniBarWindow` 继续做 popup host：

1. sweep popup parent 仍挂在 minibar 下
2. 继续复用现有 owned popup 判定，避免 outside-click 提前折叠 host
3. RF 仍由 minibar 自己持有按钮与 property 写回，不把 RF 语义塞回 `StepSweepPanel`

### 3. TxSessionService 最小接线

minibar 只注入：

1. 当前 carrier plan kind
2. 当前 sweep context provider
3. sweep enabled/args 变化时触发 refresh

selected business 先维持为空；本次目标仅支持 `RF + SweepCw`，不处理调制数据。

## 最小改动方案

### StepSweepPanel

新增 compact/minibar 模式接口，仅做局部硬编码样式：

1. 缩小按钮高度、间距、外边距
2. 将标题/副标题都设置为居中
3. 调整 `SwitchButton` 与 `LabelButton` 的局部字号/边框/背景
4. 保持所有业务逻辑与信号语义不变
5. 在 compact 模式下收掉 `pageFreq/pageLevel` 的底部垂直 spacer，避免 popup 出现额外空白区
6. 在 compact 模式下隐藏 `Enabled` 文本，仅保留 `SwitchButton` 的 On/Off 本体；普通模式保持原样
7. 在 compact 模式下把顶部 `enabled` 与 `Sweep Type` 收成等宽两列，并降低 On/Off 子按钮最小宽度，让它们与下方两列按钮自然对齐

### MiniBarWindow

1. 新增懒创建的 sweep popup 容器与 `StepSweepPanel`
2. 点击 sweep 时打开真实 popup，而不是 placeholder
3. 监听 `StepSweepPanel::enabledChanged` / `argsChanged`
4. 把 `TxSessionService::carrierPlan` 与 `carrierPlanContextProvider` 指向 minibar 的 sweep panel
5. 在 popup 生命周期结束时只关闭可见层，不丢失已编辑 sweep 状态

### 运行语义

1. `RF=OFF` 时，无论 sweep 是否 enabled，pipeline 仍为 `Mute`
2. `RF=ON + sweep disabled` 时走 `FixedCw`
3. `RF=ON + sweep enabled` 时走 `SweepCw`
4. sweep 参数变化时通过 `TxSessionService::requestRefresh()` 更新设备配置

## 本次不做

1. 不做 provider / modulation / playback / streaming 接线
2. 不做全局 QSS 迁移
3. 不做 mainwindow 模式下的 sweep UI 重构
4. 不做运行验证与编译

## 2026-05-07 minibar ActionManager 裁剪

### 本次目标

1. 移除 `MiniBarWindow` 对 `ActionManager` menubar/container/action 注册链路的依赖
2. 保留 minibar 现有 `RF / Freq / Level / Sweep / TxSessionService` 主线不变
3. 不改动 mainwindow 路径

### 最小改动方案

1. 删除 `MiniBarWindow::initializeGlobalBootstrap()` 中的 `ActionManager::setContext(...)`、默认 menu container 创建、`initializeDeviceMenu()`、`registerActions()`
2. 保留 `MainWindowDeviceController::initialize()`，继续承接设备消息与状态更新，但不再让 minibar 初始化 Device 菜单
3. 将启动主题样式应用收口到 minibar 本地 helper，避免继续依赖 `MainWindowSettingsController::applyStartupTheme()`

## 2026-05-07 minibar 设备通知桥接收口

### 本次目标

1. 从 minibar 完全移除 `MainWindowDeviceController`
2. 不再创建 `DeviceInfoWidget`，也不复用 device menu / ETH connect UI
3. 在 `MiniBarWindow` 内部保留最小设备通知能力，只承接 `DeviceManager::systemMessage` 与必要的实时 `errorCode` 弹窗

### 最小改动方案

1. 删除 `MiniBarWindow` 对 `MainWindowDeviceController` 的 include、成员与初始化
2. 在 `MiniBarWindow::initializeGlobalBootstrap()` 直接连接 `DeviceManager::systemMessage` 与 `deviceRealTimeStatusUpdated`
3. 复用原有弹窗语义：`Device Open Failed` 仍单独跟踪并在成功 open 后清理；实时错误仍按 error code 去重并在重新 open 成功后复位

## 2026-05-07 minibar 设备通知桥接回退

### 调整原因

1. minibar 当前只在设备成功打开后显示；打开失败阶段本身没有 minibar 宿主可见性
2. 在该阶段弹出设备错误对话框，会把 minibar 错误地扩展成一个新的全局通知入口
3. `DeviceInfoWidget`、busy status、USB/ETH 菜单、连接状态文本仍应留在 mainwindow 路径，不再挂入 minibar 启动链

### 最终收口

1. `MiniBarWindow` 完全不承接 `DeviceManager::systemMessage` 与实时 `errorCode` 弹窗
2. minibar 只保留设备成功打开后的显示判定，以及自身的 `RF / Sweep / TxSessionService` 主线
