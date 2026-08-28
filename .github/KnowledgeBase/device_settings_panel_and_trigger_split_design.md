# Device Settings Panel And Trigger Split Design

本文档总结当前“集中设备设置”页面的设计目标、UI 承载方式、Trigger In / Trigger Out 语义拆分，以及这套设计如何打通到 CommonProfile、runtime 和 HTRA 设备层。

## 1. 目标与背景

原先设备相关设置入口是分散的：
## 2. 当前 UI 承载方式

当前 Device Settings 不再是独立弹窗，而是主窗口中的 standalone page。

- 入口位于 CommonPanel，放在 Sweep 按钮右侧。
- 点击入口后，由 MainWindow 切换到 FancyTabWidget 的 standalone page。
- 页面本体实现为 `DeviceSettingPanel : Core::Panel`，而不是 `Controls::Dialog`。

这样做的原因是：

- 设备设置本质上是全局设备上下文，不是一次性临时弹窗操作。
- 与 StepSweepPanel 一样作为主工作区页面，更符合当前 UI 导航方式。
- 可以避免多个弹窗与主界面状态并行存在时的焦点与上下文割裂。

## 3. 页面结构

当前页面使用 2 列 Grid，顶部仍是 2x2 主布局，右下角 `RF Hardware` 下方再追加一个独立 group：

- Reference Clock
- Trigger In
- Trigger Out
- RF Hardware
- Fan Control

各 group 的当前语义如下。

### 3.1 Reference Clock

- Ref Source
- Ref Frequency
- Ref Output

语义：

- `Ref Source` 选择内参考或外参考。
- `Ref Frequency` 表示当前参考频率，内参考场景下由 CommonDeviceProfile 控制为只读。
- `Ref Output` 表示系统参考时钟输出开关。

当前边界：

- `Ref Source / Ref Frequency` 仍属于公共配置，改动后需要进入主业务重配链路。
- `Ref Output` 不再作为“触发重配”的公共参数；页面点击后直接调用当前 `IDevice` 配置。
- 但普通 apply 仍会携带当前 `Ref Output` 状态，避免其它公共参数重下发时把 RefOut 意外冲回默认值。

### 3.2 Trigger In

- Trigger Source
- Trigger Action
- Trigger Edge

语义：

- 这一组定义“设备收到输入触发后，按什么方式执行”。
- `Source` 表示输入触发来源。
- `Action` 表示一次输入触发对应一次 Hop 还是一次完整 Sweep。
- `Edge` 表示上升沿还是下降沿触发。

### 3.3 Trigger Out

- Trigger Source
- Trigger Action
- Trigger Edge
- Trigger Output

语义：

- 这一组定义“设备在运行过程中，如何对外输出时序标记”。
- `Source` 可选择当前 TX Channel，或选择原始 `Trigger Event`。
- `Trigger Event` 表示输入触发事件发生后立即输出信号，不再按通道 Hop/Sweep 重计数。
- `Action` 表示按 Hop 还是按 Sweep 产生输出触发脉冲。
- `Edge` 表示输出边沿。
- `Trigger Output` 表示输出使能开关。

### 3.4 RF Hardware

- LO Mode
- RF Output Port
- Low Power

语义：

- 表示当前 HTRA 射频本振工作模式。
- 表示当前射频输出端口。
- `Low Power` 是独立设备开关：ON 映射 H2 `POWEROFF`，OFF 映射 `POWERON`。

### 3.5 Fan Control

- Fan Mode
- Auto Threshold（℃）

语义：

- 表示设备风扇工作模式。
- 当前提供 `On / Off / Auto` 三个选项。
- `Auto Threshold` 是仅在 debug 模式下展示的内部调试输入，使用普通 `QLineEdit`，不调用自定义软键盘；HTRA 默认值为 40 ℃。
- 这组配置不参与 CommonDeviceProfile，也不进入主业务 apply request，而是直接调用当前设备的快捷接口下发。

### 3.6 布局细节

当前页面还有两个刻意的布局约束：

- 每个 group 的高度按内部真实控件高度收紧，不主动吃掉页面多余空间。
- 对于某一行只有一个控件的场景，该控件只占左半区域，右半保持留白，不横向铺满整行。

第二条约束当前适用于：

- Ref Output
- Trigger In 的 Trigger Edge
- Trigger Out 第二行的 Trigger Edge 与 Trigger Output
- LO Mode
- Fan Mode

`RF Hardware` 的第一行放置 `LO Mode` 与 `Low Power`，各占一列；第二行左侧保留 `RF Output Port`。`Fan Control` 仍是 RF Hardware 之后最后追加的独立 group，并继续遵守现有 debug-only 可见性策略；第二行使用普通文本标签与 `QLineEdit` 编辑 Auto 阈值。

## 4. 为什么 Trigger 必须拆成 In / Out

这不是纯 UI 调整，而是语义分层。

`Trigger In` 与 `Trigger Out` 的职责不同：

- Trigger In 负责“设备何时开始执行、一次触发执行多少”。
- Trigger Out 负责“设备向外部系统报告哪个运行时刻值得被观察”。

两者不应该被视为同一组参数，原因如下：

- 一次输入触发完全可以启动整条 Sweep，而输出触发仍然按 Hop 粒度发脉冲。
- 输入侧是执行控制语义，输出侧是对外时序标记语义。
- 如果把两者混在一起，用户会误以为输入动作和输出动作必须一一对应。

当前拆分后的设计直接把这层语义写进 UI 结构中，避免继续使用一个混合 Trigger group 造成认知歧义。

## 5. 公共配置链路

这套设计不是页面孤岛，而是完整接入了当前 Core 管线。

### 5.1 Property 层

CorePlugin 当前为集中设备设置页提供如下公共 property：

- `RefClockSource`
- `RefClockFrequency`
- `SystemClockOut`
- `TriggerSource`
- `TriggerAction`
- `TriggerEdge`
- `TriggerOutSource`
- `TriggerOutAction`
- `TriggerOutEdge`
- `TriggerOutState`
- `LoMode`

其中：

- Trigger In / Trigger Out 的枚举值都保持为 `Utils::Id` 对应的公共语义 ID。
- `Ref Source / Ref Frequency`、Trigger、LO Mode 按钮通过 `PropertyBindingManager` 直接绑定到这些 property。
- `Ref Output` 的 property 现在只作为运行时镜像值，不再承担点击即触发公共重配的职责；Device Settings 页面改为直接调用当前 `IDevice`。
- `Fan Mode / Auto Threshold` 不使用 property，也不走 `PropertyBindingManager`；它们直接读取当前 `IDevice` 并调用设备侧快捷接口。
- `Low Power` 同样不创建 property；页面通过 `IDevice::queryLowPowerEnabled()` / `configureLowPowerEnabled()` 直接读取设备缓存并下发。

### 5.2 CommonDeviceProfile

`CommonDeviceProfile` 是这套设计的中心状态容器。

当前它已经把以下字段纳入 `CommonPanelProfile` 与 `ExternalPropertyMapping`：

- `TriggerSource`
- `TriggerAction`
- `TriggerEdge`
- `TriggerOutSource`
- `TriggerOutAction`
- `TriggerOutEdge`
- `TriggerOutState`
- `RefClockSource`
- `RefClockFrequency`
- `LoMode`

当前 `SystemClockOut` 不再加入 `ExternalPropertyMapping`，只保留为 CommonDeviceProfile 内部的运行时镜像值。

`Fan Mode`、`Fan Auto Threshold` 与 `Low Power` 不在这份列表中。

这意味着：

- 页面改动会经由 property group 汇总成公共设备配置。
- 公共设备配置可以保存到 `Profile.json`。
- 设备 writeback 后也可以反向刷新 UI。
- 但 `Ref Output`、`Fan Mode / Auto Threshold` 与 `Low Power` 都属于设备直连快捷控制，不走这条“改动即触发公共重配”的汇总通路。

### 5.3 TxApplyRequest / TxPipelineRuntime

TxSessionService 在构建 `TxApplyRequest` 时，会从 `CommonDeviceProfile` 读取当前公共设置，并填入 `TxCommonSettings`。

当前纳入 runtime request 的触发相关字段包括：

- `triggerSource`
- `triggerAction`
- `triggerEdge`
- `triggerOutSource`
- `triggerOutAction`
- `triggerOutEdge`
- `triggerOutState`

参考时钟相关字段当前边界是：

- `refClockSource / refClockFrequency`：既参与 request 构建，也会在 UI 改动时触发新一轮 apply。
- `systemClockOut`：仍会被带入 request，作为“当前设备状态保持值”；但它本身不再通过 `profileChanged()` 触发 request 重建。

`TxPipelineRuntime::fillDeviceProfile()` 再把这些字段写入 `IDevice::Profile`，作为设备配置与 sweep 调用的统一输入。

`Fan Mode / Auto Threshold` 与 `Low Power` 不进入 `TxApplyRequest` / `TxPipelineRuntime`。

## 6. HTRA 设备层的当前实现

当前 HTRA 插件 `FancyDevice` 以最小兼容方式接入这套设计。

### 6.1 普通配置路径

在 `FancyDevice::applyCommonDeviceSettingsLocked()` 中：

- 每次设备 open 后的第一次公共配置无条件执行 `tx_config_ffm()`；随后各模式和 RF 开关引起的公共配置会先 `tx_query_ffm()`，查询成功且 center/level 与目标一致时跳过 FFM 重配，任一参数不同或 query 失败时仍执行配置。
- Trigger In 的 source / action / edge / count 先缓存为当前 stream 触发策略；外部 Rising / Falling 会分别映射为 `TRIGGER_SOURCE_EXTERNAL_RISING_EDGE / TRIGGER_SOURCE_EXTERNAL_FALLING_EDGE`。
- CW、Realtime 与 Playback 都通过 `tx_config_stream(..., stream0, tx_stream, stream_trigger)` 下发，`source / response_count` 属于 `stream_trigger`。
- `channel_config_trigger(..., stream0, action)` 只负责把 stream0 与通道 Hop/Sweep 动作绑定。
- `device_config_trigger_out()` 接收新版设备级 Trigger Out 的 state / source / negative pulse / recounter。
- Trigger Out Source 为 Channel 时，`source` 使用现有 `primaryTxChannel()->num`，并只接受新版头文件定义的 `CHANNEL0 / CHANNEL1`，不会假定当前主 TX 一定是通道 0。
- Trigger Out Source 为 Trigger Event 时，`source = TRIGGER_EVENT`，触发事件发生后立即输出，`recounter` 被 API 忽略。
- 旧页面的 Rising / Falling 分别映射新版正 / 负脉冲。
- Channel 源下的产品映射为：Trigger Output 控制 state，Hop 使用 `recounter = 1`，Sweep/Scan 使用 `recounter = 10`。
- FScan、LScan、MScan、Streaming 和 Playback 专用路径不重复配置 Trigger Out。

H2 API 已修复此前 `recounter = 10` 可能令设备会话失效的问题，因此 Trigger Out 的使能、通道、脉冲极性和 Hop/Sweep 重计数均重新按 UI/Common Profile 动态下发。

### 6.2 回写路径

在 `FancyDevice::fillWritebackProfileLocked()` 中：

- 已配置 stream 的模式通过 `tx_query_stream()` 回写 Trigger In 的 source / response count，通过 `channel_query_trigger()` 回写绑定的 stream 与 action；外部上/下沿 source 再拆回 UI 的 External + Edge。
- 新版 H2 API 没有 Trigger Out query；整次设备配置成功后，writeback 保留本次 Trigger Out 请求值。
- `channel_query_lo_mode()` 回写 LO Mode。

因此 Trigger In、LO Mode 等可查询字段仍以设备真实状态回流；Trigger Out 是 write-after 结果，不声称来自硬件回读。

### 6.3 Fan Control 的当前实现

`FancyDevice` 当前通过 H2 API 的 `device_config_fan()` 直接处理 `Fan Mode` 与 Auto 阈值。

需要注意两点：

- 当前底层 API 只有 `set fan`，没有 `query fan`，因此 UI 不能做真实硬件回读。
- 当前实现使用设备实例内缓存回显最近一次模式与 Auto 阈值；设备重新 open 后会重放这份缓存。
- HTRA Auto 阈值的初始缓存来自 `kDefaultFanAutoThresholdCelsius = 40.0f`。Auto 模式下编辑会立即下发；On/Off 模式下只更新缓存，不破坏当前的强制开/关语义，后续切换到 Auto 或重新 open 时再使用该阈值。

### 6.4 Ref Output 的当前实现

`FancyDevice` 当前通过已有的 `device_query_clock()` / `device_config_clock()` 提供 `querySystemClockOut()` / `configureSystemClockOut()` 快捷接口。

需要注意两点：

- `configureSystemClockOut()` 只复用当前设备上的 `source_query / refFreq_query` 去切换时钟输出，不再顺带走 `tx_config_ffm()`、trigger、LO 等整条公共配置链。
- Device Settings 页面会在显示、设备切换、设备 open 状态变化时主动 query 当前 RefOut 状态；点击按钮后直接下发，并把结果同步回运行时镜像值。

### 6.5 Low Power 的当前实现

`FancyDevice::open()` 使用现有的 PGA 单口供电判据 `Plugin::usbPortOnly()` 设置每次 open 的 Low Power 策略默认值，但硬件 open 初始化统一先进入 `POWERON`：

- 所有设备：open 阶段先调用 `device_config_power_state(..., POWERON)`，并保证随后第一次公共配置无条件执行一次 `tx_config_ffm()`。
- `PGA_PowerSourceType == 1`：Low Power 策略缓存与 UI 默认显示 ON；首次 Mute 配置完成后再进入 `POWEROFF`。
- 其他设备：Low Power 策略缓存与 UI 默认显示 OFF，后续保持 `POWERON`。

H2 API 当前没有 power-state query，因此 `queryLowPowerEnabled()` 返回本次设备 open 或最近一次成功编辑的策略缓存值。页面点击后调用 `configureLowPowerEnabled()` 更新策略；ON/OFF 分别映射后续 Mute/非 Mute 配置中的 `POWEROFF` / `POWERON`，失败时回退显示缓存值。

Low Power 的视觉状态继续由业务 `checked` 值承载。所有 `configuration_files/*/theme.css` 与 `theme_light.css` 模板都把 `#lowPowerBtn` 纳入 Trigger Output 的同组选择器，因此按钮本体、上下两行间距和 `parentChecked` 状态文字会沿用 Trigger Output 的主题样式；深色主题 ON 状态为绿色。

这项设置不进入 CommonDeviceProfile、PropertySystem、TxApplyRequest 或 Profile 持久化；每次设备重新 open 都重新应用平台默认值。

### 6.6 Sweep 路径的关键调整

这是本轮设计里最重要的行为修正。

旧问题是：

- 虽然普通 `configuration()` 可以设置 Trigger In，
- 但 `setFrequencySweep()` / `setLevelSweep()` / `startStreamingFrequencySweep()` / `startStreamingLevelSweep()` 等 sweep 路径又把输入触发重新写死成 `BUS + RISING + SWEEP`。

这样会导致：

- 页面上的 Trigger In 能改，
- 但真正进入 Sweep 时又失效。

当前实现已经改成：

- `FancyDevice` 在普通配置阶段缓存最近一次下发成功的 Trigger In source / action / edge。
- 后续各类 sweep 与 playback 启动路径复用这组缓存，而不是重新写死。
- 启动阶段只有 `BUS` 触发源会主动调用 `channel_trigger_bus()`；`External / XPPS` 只会完成 `channel_start()` 并进入等待态。
- 若触发源为 `XPPS`，设备侧还需要保证 GNSS PPS 已启用且 GNSS 已锁定；仅仅把 TriggerSource 设成 `XPPS` 并不等于设备已经具备可触发的秒脉冲。
- 一旦 XPPS 因 `pps_en=0` 或 GNSS 失锁而变为不可用，当前公共 `TriggerSource` 应默认退化到 `BUS`；这和 HPPort 能力丢失时回退到 RFPort 的策略一致。

这样可以保证“集中设备设置页里看到的 Trigger In 配置”与“实际进入 sweep 时的设备行为”一致。

## 7. 持久化与恢复

当前集中设备设置页相关字段已经进入 `CommonDeviceProfile::saveSettings()` / `restoreSettings()`：

- `TriggerSource`
- `TriggerAction`
- `TriggerEdge`
- `TriggerOutAction`
- `TriggerOutEdge`
- `TriggerOutState`
- `RefClockSource`
- `RefClockFrequency`
- `LoMode`

`SystemClockOut`、`Fan Mode / Auto Threshold` 与 `Low Power` 当前都不参与保存/恢复。

因此：

- 保存 Profile 后，真正影响业务 apply 的公共参数可以恢复。
- 恢复后的状态会重新进入 CommonProfile，而不是只做 UI 回显。
- 但 `RefOut`、`Fan Mode / Auto Threshold` 与 `Low Power` 保持设备直连语义，不会因为加载业务配置而被隐式改写。

## 8. 当前边界与已知约束

### 8.1 Trigger In / Out 已完成语义拆分，但不是所有模式都完全自由

当前 FScan / LScan 及其 streaming / playback 对应路径已经按统一模型接入。

但仍需注意：

- 当前 HTRA 适配保留新版 API 所需的通道数组、返回通道数和主 TX 选择：USB 使用枚举得到的 `device_info.chn`，单 ETH 当前传 `chs = 1`，打开后选择第一个可用 TX 通道，业务仍固定使用 `stream 0`。这套形式不能当作可删除的无效多通道代码。
- Trigger Out 当前按产品口径动态映射：Hop 使用 `recounter = 1`，Sweep/Scan 使用 `recounter = 10`；由于新版没有 query，页面回显本次成功配置所使用的请求值。
- `MSCAN` 当前 UI 入口处于隐藏状态。
- 某些底层模式如果有额外硬件约束，仍可能在设备实现层做模式特化。
- 当前语义是“BUS 触发源下软件会主动 fire；External / XPPS 下软件只 arm，不代替外部事件发起执行”。
- 但 `XPPS` 比普通 `External` 多一层设备内前置条件：GNSS 需要锁定，且 PPS 输出需要真实开启；因此更合适的实现是直接在 TriggerSource 的 `enumDisplayOptions` 里禁用 XPPS，而不是等到下发阶段再兜底拒绝。
- 当 XPPS 再次满足条件时，系统不自动从 `BUS` 回切到 `XPPS`；用户若要恢复 XPPS，应显式重新选择。

因此知识库结论应理解为：

- 当前公共模型与主线设备路径已经支持 Trigger In / Trigger Out 分层。
- 但这不等于所有硬件模式都已开放成完全无约束组合。

## 9. 为什么这篇文档要单独存在

这篇文档解决的是跨层信息分散的问题。

如果没有这篇文档，理解当前设计通常需要同时翻以下几类文件：

- Device Settings 页面布局文件
- CommonDeviceProfile
- TxSessionService::buildApplyRequest()
- TxPipelineRuntime
- FancyDevice 的 apply/query/sweep 实现
- 历史任务记录

单独沉淀这篇文档后，后续针对以下问题都可以直接从知识库进入：

- 为什么设备设置改成 standalone page
- 为什么 Trigger 要拆成 In / Out
- Trigger In / Out 各自控制什么
- 为什么 sweep 路径必须复用最近一次 Trigger In 配置
- 这套设计当前已经打通到哪一层、还剩哪些边界
