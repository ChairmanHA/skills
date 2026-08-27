# H2 ETH 双设备定制版详细实施方案（设备 A / 设备 B）

日期：2026-08-12  
状态：第 3.4 版 Phase 2B ArbPlayback 双设备初步实机通过；A/B 纯切换与 Playback 关闭回退仍待定向验收
当前验证级别：`debug-run`  
后续建议验证级别：`debug-build` + `debug-run`  
范围：形成底层设备架构、双路 Streaming 支持、下发链路和分阶段实施方案；当前实施 Phase 2B，不更新 KnowledgeBase  
本版重点：底层 Device 产品架构重构 + 两路 Streaming 并行；UI 采用固定宽屏前提，不再做紧凑折行适配  
本轮修订说明：第 1–2 节保留既有事实依据并修正结论，第 3–12 节按 2026-08-11 架构复核结果重写；新增 `DeviceProduct`、`canonicalDevice()` 调用点分类、控制面/数据面线程边界和首个双流实机验证切片。第 3.1 版进一步穷举 `activedBusiness()/isActive()` 消费者，并冻结“任一路 Streaming 时整机暂停 GNSS/温度等非流遥测”的策略。第 3.2 版按产品确认将 ETH pair capability 收敛为一份共享能力。第 3.3 版进一步固定 Playback 为 195.3125 kSps–125 MSps、Streaming 为 195.3125 kSps–62.5 MSps、波形内存为 125 MiB，并取消 ETH pair 的跨进程 lease：定制版不启动 USB scanner、不做 USB 自动连接，每个进程只允许连接一个固定 5000/5001 pair。第 3.4 版记录 Phase 2B ArbPlayback 首轮实机结果：A/B 独立下载、扫描及 RF/MOD 回读通过；同时保留 A/B 纯切换无副作用、Playback 关闭回退和 Quick Waveform 的定向验收项。

## 1. 结论先行

这不是双通道设备适配，也不应继续使用 `channel 1/channel 2`、`多通道 handle` 或“切换当前设备通道”来描述。

定制产品固定为一个 IP 下的两台独立 ETH 设备：

```text
设备 A = IP:5000 = FancyDevice A = A 自己的 H2 device handle = A 返回的 channels[0]
设备 B = IP:5001 = FancyDevice B = B 自己的 H2 device handle = B 返回的 channels[0]
```

供应商示例明确建立了两个独立 `device`、两组独立 `channel[]`，只是 IP 相同、端口分别为 5000/5001：[eth_2dev_cw.cpp:33](../../3rdParty/h2_api/eth_2dev_cw.cpp#L33)、[eth_2dev_cw.cpp:36](../../3rdParty/h2_api/eth_2dev_cw.cpp#L36)、[eth_2dev_cw.cpp:78](../../3rdParty/h2_api/eth_2dev_cw.cpp#L78)。这也是本方案的底层真相。

本定制版采用以下原则：

1. 保留两个独立 `FancyDevice`；不向 `FancyDevice` 塞第二个 handle，也不创建假的 H2 多通道设备。
2. 每个 `FancyDevice` 都固定 `device_open_eth(..., chs = 1, ...)`，并固定使用该次 open 返回的 `channels[0]`/`stream 0`。新 API 没有特殊说明时，也沿用单设备旧业务语义，只操作该 `FancyDevice` 的第一个 channel。
3. `DeviceManager` 只切换“产品”，不再切换裸设备：`setCurrentDevice()` 重构为 `setActiveProduct()`，`currentDevice()` 重命名为 `canonicalDevice()` 以强制审计旧调用点；A/B UI 切换只改变 `selectedRole`，不得触发产品切换。
4. ETH 产品连接是一个全成功事务：A、B 均成功才连接成功；任一失败就关闭并释放 A、B，不留下单边隐藏连接。
5. A/B 各自保存 center、level、RF/MOD、carrier plan、baseband 业务 profile、已下发状态和波形驻留状态。
6. DeviceSettingDialog 保留一份统一设置；除明确的共享资源外，统一值向 A、B 分别下发。
7. GNSS 只保留一份 UI 和一条设备链路，默认使用设备 A；固件/API/硬件版本只显示设备 A 的一份。UID 和温度例外，明确显示 A、B 两份。
8. 数字调制许可证只使用设备 A（5000）的 model/UID 校验，A/B 编辑目标切换不触发许可证重新校验。
9. 两路 Streaming 并行是第一版必须能力：A、B 可同时各跑一路实时流，参数互不影响。由此确定“回放类用 profile 快照、Streaming 用运行体分身”的分界；这里分身的是 `StreamingEngine`，不是复制两套 UI 或两个 `StreamingBussiness` 注册对象。
10. UI 采用固定宽屏前提：定制版取消 CommonPanel 紧凑折行与状态栏紧凑档，主窗口最小宽度 1280px，两块 CommonPanel 恒定以 Wide 模式呈现。
11. Preset / Reset Device 是整机语义：遍历产品的全部角色，A、B 都执行。
12. 已确定的实现前提：业务 `getProfile()/setProfile()` 默认视为无损往返，不为此建往返自检工具；两份 PlaybackPayload 必须同时常驻内存，不做容量降级；本轮不为树莓派资源做特殊优化。
13. A/B 任一路处于 Streaming 的 Starting/Running/Reconfiguring/Stopping 状态时，整机暂停 GNSS 配置和 A/B 温度、GNSS、供电、feature-spec 等非流实时查询；只保留数据面 throughput/error/fault。最后一路完全停止后，才从下一次控制面 timer 恢复遥测。
14. ETH pair 的 A/B capability 由产品定义为相同的一份共享能力；第一版 Playback 固定为 195.3125 kSps–125 MSps，Streaming 固定为 195.3125 kSps–62.5 MSps，波形存储固定为 125 MiB（`131,072,000 bytes`，即 32,768,000 个 complex16 IQ sample）。`role` 仍保留在 capability 查询入口中，用于显式路由和身份/revision 校验，不代表维护两套能力范围。
15. `PropertyManager` 只是一套 selectedRole 临时编辑缓冲区，主要服务数值键盘、单位、范围和 metadata；它不是 A/B 状态真相源、不是设备运行状态仓库，也不是只读字段的显示模型。凡是产品固定值、设备查询缓存或 workspace 摘要能够直接提供的字段，都直接驱动 View，不绕行 Property。

## 2. 判断依据与当前代码断点

### 2.1 H2 API 与示例

- 当前头文件只有 USB 枚举 `device_list_usb()`，没有 ETH list/probe：[h2_api.h:588](../../3rdParty/h2_api/include/h2_api.h#L588)。所以 ETH 对话框仍必须让用户手动输入 IP，不能设计成主动枚举列表。
- `device_open_eth()` 一次只打开传入的一个 endpoint：[h2_api.h:634](../../3rdParty/h2_api/include/h2_api.h#L634)。
- 两设备示例固定相同 IP、5000/5001，并执行两次 open：[eth_2dev_cw.cpp:33](../../3rdParty/h2_api/eth_2dev_cw.cpp#L33)、[eth_2dev_cw.cpp:36](../../3rdParty/h2_api/eth_2dev_cw.cpp#L36)。
- `channel_tx_query_capabilities()` 是 channel 级接口：[h2_api.h:925](../../3rdParty/h2_api/include/h2_api.h#L925)。但本定制产品已确认 A/B 硬件能力相同，因此产品层只发布一份共享 capability；adapter 仍可查询各 endpoint 并记录原始值用于诊断，但前端不据此形成两套范围。第一版共享上限固定为 Playback 125 MSps、Streaming 62.5 MSps 和 125 MiB 波形内存。
- GNSS API 是 device handle 级：[h2_api.h:762](../../3rdParty/h2_api/include/h2_api.h#L762)、[h2_api.h:772](../../3rdParty/h2_api/include/h2_api.h#L772)。本定制产品既然只有一个 GNSS，就应明确选择一个 canonical endpoint，而不是重复两套 GNSS UI。

### 2.2 `FancyDevice` 已经是正确的单 endpoint 边界

- ETH open 当前已经固定请求一个 channel：[fancydevice.cpp:606](../../src/plugins/htra/fancydevice.cpp#L606)、[fancydevice.cpp:620](../../src/plugins/htra/fancydevice.cpp#L620)。
- 当前实现会扫描并选定一个主 TX channel，再由 `primaryTxChannel()` 集中访问：[fancydevice.cpp:667](../../src/plugins/htra/fancydevice.cpp#L667)、[fancydevice.cpp:1070](../../src/plugins/htra/fancydevice.cpp#L1070)。定制版应进一步把 ETH/USB 都固定为返回数组的 `channels[0]`：open 后只校验下标 0 是可用 TX，不再向后扫描回退；也绝不把两个 ETH endpoint 映射进同一个 `channels[]`。
- FFM/FScan/LScan/MScan、trigger、stream 最终都通过当前 `primaryTxChannel()` 下发，例如 [fancydevice.cpp:1295](../../src/plugins/htra/fancydevice.cpp#L1295)、[fancydevice.cpp:1508](../../src/plugins/htra/fancydevice.cpp#L1508)、[fancydevice.cpp:1707](../../src/plugins/htra/fancydevice.cpp#L1707)。所以两个 `FancyDevice` 天然就能分别承载两套 carrier plan。
- `m_waveformCache` 是实例成员并随单个设备 close/reset 清理：[fancydevice.cpp:944](../../src/plugins/htra/fancydevice.cpp#L944)、[fancydevice.cpp:1547](../../src/plugins/htra/fancydevice.cpp#L1547)。维护两个 `FancyDevice` 已经天然得到两个波形仓库，不需要把一个 cache 改成二维结构。

### 2.3 当前 Core 只有一个设备目标，需要先改下发链路

- `DeviceManager` 当前只有单一 `currentDevice()`：[devicemanager.cpp:324](../../src/plugins/core/devicemanager.cpp#L324)。
- `setCurrentDevice()` 会停止轮询、关闭旧设备、打开新设备，是整机切换而不是 A/B 编辑目标切换：[devicemanager.cpp:352](../../src/plugins/core/devicemanager.cpp#L352)。
- 状态轮询当前只查询单个 current device，并只发出一份状态：[devicemanager.cpp:797](../../src/plugins/core/devicemanager.cpp#L797)、[devicemanager.cpp:838](../../src/plugins/core/devicemanager.cpp#L838)、[devicemanager.cpp:853](../../src/plugins/core/devicemanager.cpp#L853)。
- `TxApplyRequest` 当前只有 UID、capability revision 和一份 `common + carrier + provider`，没有 endpoint 目标：[txpipelinestate.h:285](../../src/plugins/core/txpipelinestate.h#L285)、[txpipelinestate.h:302](../../src/plugins/core/txpipelinestate.h#L302)。
- `TxPipelineExecutor` 在执行时重新读取 `DeviceManager::currentDevice()`，所以用户只要发生 UI 选择变化，异步结果就有串到另一设备的风险：[txpipelineexecutor.cpp:116](../../src/plugins/core/txpipelineexecutor.cpp#L116)、[txpipelineexecutor.cpp:523](../../src/plugins/core/txpipelineexecutor.cpp#L523)。
- `TxSessionService` 也只按一份 current capability 构建 request：[txsessionservice.cpp:352](../../src/plugins/core/txsessionservice.cpp#L352)、[txsessionservice.cpp:355](../../src/plugins/core/txsessionservice.cpp#L355)。

因此不能先复制 UI 再让两份 UI 都调用现有全局链路；必须先让 request、capability、executor、状态和异步结果都显式携带设备 A/B 目标。

### 2.4 当前 UI/Profile 也是单工作区

- `CommonPanel` 直接绑定全局 `Center`、`Level`、`RF`、`Mod` property：[commonpanel.cpp:303](../../src/plugins/core/commonpanel.cpp#L303)、[commonpanel.cpp:309](../../src/plugins/core/commonpanel.cpp#L309)、[commonpanel.cpp:359](../../src/plugins/core/commonpanel.cpp#L359)。简单 `new CommonPanel` 两次会让两块面板显示和修改同一份数据，不能满足 A/B 独立参数。
- 主窗口目前只有一块 CommonPanel，右侧 dock 跨越顶部和业务区：[mainwindow.cpp:393](../../src/plugins/core/mainwindow.cpp#L393)、[mainwindow.cpp:462](../../src/plugins/core/mainwindow.cpp#L462)、[mainwindow.cpp:464](../../src/plugins/core/mainwindow.cpp#L464)。
- 业务对象已经提供 `getProfile()/setProfile()`，可作为 A/B baseband profile 快照边界：[ibusiness.h:70](../../src/plugins/core/ibusiness.h#L70)。现有运行配置也已经能保存 common、全部 business 和 sweep profile：[runtimeprofilepersistence.cpp:125](../../src/plugins/core/runtimeprofilepersistence.cpp#L125)、[runtimeprofilepersistence.cpp:142](../../src/plugins/core/runtimeprofilepersistence.cpp#L142)、[runtimeprofilepersistence.cpp:143](../../src/plugins/core/runtimeprofilepersistence.cpp#L143)。因此应复用现有 profile 能力建立两份 workspace，而不是复制所有业务类实例。

### 2.5 定制版固定 Wide，不再保留 Compact 分支

- 当前 dock 直接以 `QListWidget` 为内容：[fancytabwidget.cpp:259](../../src/plugins/core/fancytabwidget.cpp#L259)。应改为“顶端 selector + 下方 list”的容器，selector 不参与业务项计数和滚动。
- 当前右栏已经定义 205px 双列和 100/125px 单列两组宽度，并在运行时切换列数：[fancytabwidget.cpp:50](../../src/plugins/core/fancytabwidget.cpp#L50)、[fancytabwidget.cpp:794](../../src/plugins/core/fancytabwidget.cpp#L794)、[fancytabwidget.cpp:971](../../src/plugins/core/fancytabwidget.cpp#L971)。定制版固定使用 205px 双列，不再保留 selector 单列宽度语义。
- 主窗口目前会按宽度把 CommonPanel 从 Wide 折叠为 Compact，并同步压缩状态栏：[mainwindow.cpp:889](../../src/plugins/core/mainwindow.cpp#L889)、[mainwindow.cpp:905](../../src/plugins/core/mainwindow.cpp#L905)、[mainwindow.cpp:917](../../src/plugins/core/mainwindow.cpp#L917)。定制版要从这一响应式入口移除两种 Compact 转换，并把主窗口最小宽度固定为 1280px。
- `SwitchButton` 的两个文本目前硬编码为 On/Off：[switchbutton.cpp:13](../../src/libs/controls/switchbutton.cpp#L13)、[switchbutton.cpp:16](../../src/libs/controls/switchbutton.cpp#L16)。应补“左右文本可配置”能力；视觉复用 SwitchButton，但 `FancyTabWidget` 对外暴露类型安全的 `DeviceRole`，不要让业务层用含义不清的 bool 表示 A/B。

### 2.6 元数据、许可证和统一设置的现状

- 状态栏当前只持有一份 `DeviceInfo`，UID 和温度也只有一个 label：[deviceinfowidget.cpp:477](../../src/plugins/core/deviceinfowidget.cpp#L477)、[deviceinfowidget.cpp:495](../../src/plugins/core/deviceinfowidget.cpp#L495)、[deviceinfowidget.cpp:518](../../src/plugins/core/deviceinfowidget.cpp#L518)。
- About 当前从单一 `DeviceInfo` property 读取一份 UID/固件信息：[aboutdialog.cpp:173](../../src/plugins/core/aboutdialog.cpp#L173)、[aboutdialog.cpp:192](../../src/plugins/core/aboutdialog.cpp#L192)、[aboutdialog.cpp:206](../../src/plugins/core/aboutdialog.cpp#L206)。
- Updater 当前读取 `DeviceManager::currentDevice()` 的固件版本和 endpoint：[updatedialog.cpp:418](../../src/plugins/updater/updatedialog.cpp#L418)、[updatedialog.cpp:461](../../src/plugins/updater/updatedialog.cpp#L461)、[updatedialog.cpp:468](../../src/plugins/updater/updatedialog.cpp#L468)。这些调用应在强制审计后明确改用 `canonicalDevice()`，固件显示保持单份 A。
- 数字调制许可证由 `DeviceRuntimeBridge::deviceConnected` 提供的一份 model/UID 触发：[analogmodulationplugin.cpp:43](../../src/plugins/analog/analogmodulationplugin.cpp#L43)、[analogmodulationplugin.cpp:57](../../src/plugins/analog/analogmodulationplugin.cpp#L57)，实际校验在 [deviceutils.cpp:143](../../src/plugins/analog/deviceutils.cpp#L143)、[deviceutils.cpp:159](../../src/plugins/analog/deviceutils.cpp#L159)。ETH pair 只向这条链路发布设备 A，就能稳定实现“始终用 5000 证书”。
- GNSS 设置当前也走 `DeviceManager::currentDevice()`：[commondeviceprofile.cpp:762](../../src/plugins/core/commondeviceprofile.cpp#L762)、[commondeviceprofile.cpp:774](../../src/plugins/core/commondeviceprofile.cpp#L774)。这些调用属于明确的 canonical A 类别，因此只保留一个 GNSS dialog。
- DeviceSettingPanel 的 System Clock Out、Low Power、Fan 当前都直接操作一个 `currentDevice()`：[devicesettingdialog.cpp:402](../../src/plugins/core/devicesettingdialog.cpp#L402)、[devicesettingdialog.cpp:477](../../src/plugins/core/devicesettingdialog.cpp#L477)、[devicesettingdialog.cpp:507](../../src/plugins/core/devicesettingdialog.cpp#L507)。这些调用不能机械改成 `canonicalDevice()`；统一 UI 下仍应通过产品设置协调器向 A、B 同值下发，只有 GNSS/固件等已明确的共享资源走 A。

### 2.7 `DeviceOperator` 是 legacy 业务通往设备的唯一出口

这是原方案完全遗漏、但对双路 Streaming 最关键的一个类。

- `DeviceOperator` 是 `IBusiness` 的成员：[ibusiness.h:106](../../src/plugins/core/ibusiness.h#L106)；构造函数私有且 `friend class IBusiness`：[deviceoperator.h:34](../../src/plugins/core/deviceoperator.h#L34)。
- 它的每个方法都在执行时重新读取 `DeviceManager::currentDevice()`，并用 `m_business->isActive()` 做门禁：[deviceoperator.cpp:12](../../src/plugins/core/deviceoperator.cpp#L12)、[deviceoperator.cpp:27](../../src/plugins/core/deviceoperator.cpp#L27)、[deviceoperator.cpp:46](../../src/plugins/core/deviceoperator.cpp#L46)。
- 全仓只剩两个使用者：`builtinbusiness.cpp` 的 Mute/CW legacy 路径（[builtinbusiness.cpp:15](../../src/plugins/core/builtinbusiness.cpp#L15)、[builtinbusiness.cpp:42](../../src/plugins/core/builtinbusiness.cpp#L42)）和 `streamingbussiness.cpp`（[streamingbussiness.cpp:208](../../src/plugins/htra/streamingbussiness.cpp#L208)、[streamingbussiness.cpp:216](../../src/plugins/htra/streamingbussiness.cpp#L216)、[streamingbussiness.cpp:347](../../src/plugins/htra/streamingbussiness.cpp#L347)）。
- 全部回放类 provider 已经迁到 `TxPipelineExecutor` 直连设备；executor 的头注释也明确写着“非参与型 Playback provider 与 Streaming 仍走 legacy IBusiness 路径”：[txpipelineexecutor.h:16](../../src/plugins/core/txpipelineexecutor.h#L16)。

结论：`DeviceOperator` 就是 Streaming 的设备出口。**给 `DeviceOperator` 加角色，等于给 Streaming 加角色**，改动面被压缩到一个约 120 行的类里，而不是散落在各业务中。

### 2.8 单一 `activedBusiness` 才是双路 Streaming 的真正障碍

- `BusinessManager` 只保存一个 active 业务：[businessmanager_p.h:32](../../src/plugins/core/businessmanager_p.h#L32)；`setActive(true)` 会把上一个 active 业务强制置 false：[businessmanager.cpp:59](../../src/plugins/core/businessmanager.cpp#L59)。
- `StreamingBussiness` 是单实例，内部只有一条 sender 线程、一个 `StreamingDataGenerator`、一组 `m_enabled / m_interrupt / m_deviceProfile / m_bridgedCarrierPlan`：[streamingbussiness.h:84](../../src/plugins/htra/streamingbussiness.h#L84)、[streamingbussiness.h:89](../../src/plugins/htra/streamingbussiness.h#L89)。

由此得到本方案最重要的一条分界：

| 业务族 | 下发完成后主机是否继续参与 | A/B 并行的实现方式 |
|---|---|---|
| 核心托管回放（Mute / FixedCw / SweepCw / FixedPlayback / SweepPlayback） | 否，波形下载后设备自走 | **profile 快照**。executor 已绕开 IBusiness activation，两份 request 各带目标即可 |
| legacy 常驻业务（Streaming 实时流） | 是，sender 线程必须持续喂数据 | **运行体分身**。快照模型对它结构性失效，必须每角色一份 `StreamingEngine` |

因此 `activedBusiness` 必须从“全局唯一”升级为“每角色唯一”，否则 A 跑 Streaming、B 跑 Streaming 这个需求在框架层就无法表达。注册和 UI 仍只有一个 `StreamingBussiness`；该业务内部持有 `StreamingEngine(A)`、`StreamingEngine(B)` 两个运行体。

### 2.9 吞吐前提（产品已定义，软件不做协商）

两路各 7.8 MSps complex16，即每路约 31.2 MB/s、合计约 62.4 MB/s，同一块千兆网口可容纳。速率上限归产品定义，软件侧只保证“两路能同时跑起来”，本方案不设计速率协商、动态降级或带宽仲裁。

### 2.10 当前 Streaming 已有可拆分的生产/发送边界

- `StreamingDataGenerator` 自带一条生产线程：[streamingdatagenerator.cpp:490](../../src/plugins/htra/streamingdatagenerator.cpp#L490)；`DataSender` 提供有界队列并由调用方消费：[streamingdatagenerator.cpp:32](../../src/plugins/htra/streamingdatagenerator.cpp#L32)、[streamingdatagenerator.cpp:49](../../src/plugins/htra/streamingdatagenerator.cpp#L49)。
- `StreamingBussiness` 另有一条 sender 线程执行 `workerLoop()`：[streamingbussiness.cpp:83](../../src/plugins/htra/streamingbussiness.cpp#L83)、[streamingbussiness.cpp:171](../../src/plugins/htra/streamingbussiness.cpp#L171)，最终由 `sendData()` 调用 `DeviceOperator::downloadDataRealTime()`：[streamingbussiness.cpp:335](../../src/plugins/htra/streamingbussiness.cpp#L335)、[streamingbussiness.cpp:347](../../src/plugins/htra/streamingbussiness.cpp#L347)。
- `FancyDevice::downloadDataRealTime()` 在实例级 `m_mutex` 下调用 `tx_send_stream()`：[fancydevice.cpp:1988](../../src/plugins/htra/fancydevice.cpp#L1988)、[fancydevice.cpp:1992](../../src/plugins/htra/fancydevice.cpp#L1992)、[fancydevice.cpp:2006](../../src/plugins/htra/fancydevice.cpp#L2006)。`m_mutex` 和 `m_sampleRate` 都是 `FancyDevice` 实例成员：[fancydevice.h:248](../../src/plugins/htra/fancydevice.h#L248)，所以两台实例天然可并行；不要再加一个跨 A/B 的全局 ETH 互斥。
- 当前 `DeviceIoWorker` 用同一线程串行生命周期、状态和 core apply：[devicemanager.cpp:613](../../src/plugins/core/devicemanager.cpp#L613)、[devicemanager.cpp:659](../../src/plugins/core/devicemanager.cpp#L659)、[devicemanager.cpp:731](../../src/plugins/core/devicemanager.cpp#L731)。双路方案应保留该控制面串行化，同时让两条 `tx_send_stream()` 数据面各自在自己的 sender 线程并行。

### 2.11 GNSS/温度门禁必须是产品级，而不是 A 设备级

- `GpsInfoDialog` 当前只检查全局 `activedBusiness()` 是否名为 Streaming，并据此禁用配置控件：[gpsdialog.cpp:30](../../src/plugins/gps/gpsdialog.cpp#L30)、[gpsdialog.cpp:172](../../src/plugins/gps/gpsdialog.cpp#L172)。双角色后若只检查 A，B 单独 Streaming 时会错误放开 GNSS。
- 禁用控件还不够：构造、showEvent 和 `gnssSettingsApplied` 回调仍会调用 `refreshGnssConfig()`：[gpsdialog.cpp:179](../../src/plugins/gps/gpsdialog.cpp#L179)、[gpsdialog.cpp:216](../../src/plugins/gps/gpsdialog.cpp#L216)、[gpsdialog.cpp:408](../../src/plugins/gps/gpsdialog.cpp#L408)；读写函数本身都必须检查产品级门禁。
- `FancyDevice::updateRealTimeStatus()` 把温度、两路供电和 GNSS 查询放在同一个锁区间：[fancydevice.cpp:2522](../../src/plugins/htra/fancydevice.cpp#L2522)、[fancydevice.cpp:2568](../../src/plugins/htra/fancydevice.cpp#L2568)、[fancydevice.cpp:2608](../../src/plugins/htra/fancydevice.cpp#L2608)。当 B Streaming、A 非 Streaming 时，当前按设备 mode 的判断仍会让 A 发起这些 H2 调用。
- `DeviceRuntimeBridge` 还会根据全局 Streaming active 决定是否刷新 feature specs：[deviceruntimebridge.cpp:45](../../src/plugins/core/deviceruntimebridge.cpp#L45)、[deviceruntimebridge.cpp:117](../../src/plugins/core/deviceruntimebridge.cpp#L117)。这也必须服从同一个产品级 aggregate，不能各自重写一份判断。

因此最终应只有一个权威查询和信号，例如 `BusinessManager::anyRoleStreaming()` / `anyRoleStreamingChanged(bool)`；GPS UI、DeviceManager status poll、DeviceRuntimeBridge 和设置协调器都消费它。

## 3. 目标设备架构：产品 = 有序角色集合

### 3.1 核心类型和依赖边界

`Core` 不应依赖 HTRA 插件中的 `FancyDevice` 具体类型。现有手动 ETH 创建已经通过 `IManualEthDeviceFactory` 穿过插件边界：[idevice.h:485](../../src/plugins/core/idevice.h#L485)、[mainwindowdevicecontroller.cpp:475](../../src/plugins/core/mainwindowdevicecontroller.cpp#L475)、[plugin.cpp:486](../../src/plugins/htra/plugin.cpp#L486)。因此新产品模型只持有 `IDevice*`，5000/5001 两个 `FancyDevice` 仍由 HTRA factory 创建。

```cpp
enum class DeviceRole : quint8 { A, B };

enum class DeviceProductKind : quint8 {
    UsbSingle,
    EthPair
};

struct DeviceTarget {
    quint64 productEpoch = 0;
    DeviceRole role = DeviceRole::A;
    quint16 endpointPort = 0; // 仅断言、日志和诊断；不靠 port/UID 查找裸指针
};

class DeviceProduct {
public:
    DeviceProductKind kind() const;
    QVector<DeviceRole> roles() const;          // 有序
    IDevice *device(DeviceRole role) const;
    EndpointDescriptor endpoint(DeviceRole role) const;
};
```

产品形态固定为：

```text
UsbSingleProduct.roles() = { A }
└─ A -> 当前 USB IDevice

EthPairProduct.roles() = { A, B }
├─ A -> IDevice/FancyDevice(IP:5000) -> 该次 open 返回的 channels[0]
└─ B -> IDevice/FancyDevice(IP:5001) -> 该次 open 返回的 channels[0]
```

这里不是为 H2 建任意 N 通道抽象；`roles()` 只是让产品级 close、preset、status、capability 和 pipeline 代码统一循环。标准 USB 路径自然退化为单元素 `{A}`，定制产品仍只允许固定 `{A,B}`。

### 3.2 `DeviceManager` 只管理 active product

目标职责如下：

```text
DeviceManager
├─ activeProduct() / setActiveProduct(product)
├─ productEpoch()
├─ roles()
├─ device(role)
├─ capabilities(role)
├─ runtimeStatus(role)
├─ canonicalDevice()          // 仅明确的单份资源入口，ETH pair 固定 A
└─ selectedRole               // UI 编辑上下文；不参与产品生命周期

DeviceIoWorker（控制面，单线程）
├─ switchProduct(old, next)
├─ pollStatus(product.roles())
├─ applyControl(role, request)
├─ TxPipelineExecutor(A)
└─ TxPipelineExecutor(B，仅 pair 时存在)

DeviceWorkspaceStore
├─ workspace(A)
└─ workspace(B，仅 pair 时存在)
```

`setCurrentDevice()` 改名为 `setActiveProduct()`，`DeviceIoWorker::switchDevice()` 改名为 `switchProduct()`。A/B selector 只写 `selectedRole`，永远不进入 open/close 流程。当前 `switchDevice()` 会在同一个 worker 中关闭旧设备、打开新设备：[devicemanager.cpp:613](../../src/plugins/core/devicemanager.cpp#L613)；重构后只把单设备步骤改为遍历有序 roles，线程归属不变。

### 3.3 强制把 `currentDevice()` 改名为 `canonicalDevice()`

不能保留 `currentDevice()` 作为“暂时默认 A”的兼容别名，否则新代码会继续无意识地把 A 当全局目标。直接改名制造编译错误，再逐个调用点归类：

| 调用类别 | 迁移结果 | 例子 |
|---|---|---|
| 明确的单份/canonical 资源 | `canonicalDevice()`，ETH pair 固定 A | GNSS、许可证输入、About/固件、Updater endpoint |
| endpoint 业务和状态 | `device(role)` / `capabilities(role)` / `runtimeStatus(role)` | TX apply、Streaming、温度、UID、错误 |
| 产品生命周期 | `activeProduct()` + 遍历 `roles()` | open、close、preset、reset、断线 |
| 统一设置 | 产品设置协调器按字段策略执行 | Reference、LO、RF Port、Trigger In、Fan、Low Power |

当前 `DeviceOperator`、`TxPipelineExecutor`、`TxSessionService` 都在执行期读取 current device：[deviceoperator.cpp:27](../../src/plugins/core/deviceoperator.cpp#L27)、[txpipelineexecutor.cpp:116](../../src/plugins/core/txpipelineexecutor.cpp#L116)、[txsessionservice.cpp:355](../../src/plugins/core/txsessionservice.cpp#L355)。它们全部属于 endpoint 类，禁止机械改成 `canonicalDevice()`。

`currentDeviceCapabilities()` 则直接收敛为 `capabilities(role)`，不提供 `canonicalCapabilities()` 这种新的模糊 fallback。完成改名后，仓库中不再存在 `currentDevice()`/`currentDeviceCapabilities()`，以静态搜索作为 Phase 0 的硬验收项。

### 3.4 稳定路由、epoch 和能力修订

每个异步 TX request 同时携带：

```text
DeviceTarget { productEpoch, role, endpointPort }
deviceUid
capabilityRevision
generation
```

路由只使用 `productEpoch + role` 从当前 `DeviceProduct` 解析 `IDevice*`。`endpointPort` 用于断言/日志，`deviceUid` 用于身份记录，二者都不是裸设备路由 key。结果写回前再校验：

```text
productEpoch 仍匹配
AND role 仍存在
AND deviceUid 仍匹配
AND capabilityRevision 仍匹配
AND generation 仍是该 role 的期望代次
```

当前 runtime 已用 generation 做 latest-wins 合并：[txpipelineruntime.cpp:111](../../src/plugins/core/txpipelineruntime.cpp#L111)、[txpipelineruntime.cpp:122](../../src/plugins/core/txpipelineruntime.cpp#L122)，但只有一套 pending/in-flight 状态。新模型为每个 role 保留一份 runtime，从结构上避免 B request 覆盖 A pending。

### 3.5 产品开关与标准版回归

根 CMake 已有集中产品选项入口：[CMakeLists.txt:43](../../CMakeLists.txt#L43)。新增默认关闭的 `SGS_PRODUCT_ETH_DUAL`，并通过一个共享的生成配置头向 Core/HTRA/App/Updater/Analog 暴露，避免各 target 私自定义不一致。

- `SGS_PRODUCT_ETH_DUAL=OFF`：创建 `UsbSingleProduct`，角色集合恒为 `{A}`，现有 USB 行为应保持不变。
- `SGS_PRODUCT_ETH_DUAL=ON`：只创建 `EthPairProduct`；隐藏/禁用 USB 扫描、自动连接和 USB 选择 UI，连接入口固定为 IP-only ETH pair。
- 产品开关只允许出现在产品创建、发现策略和 UI 呈现层。业务执行、状态、preset、workspace 和 pipeline 统一遍历 `roles()`，不散布 `if (isEthPair)`。

## 4. A/B workspace、业务状态和 Streaming 运行体

### 4.1 每角色状态边界

```text
DeviceWorkspace
├─ commonTx
│  ├─ center / level / RF / MOD / triggerCount
│  └─ RMS / UNLEVEL / warning / lastError
├─ carrier
│  ├─ Fixed / FScan / LScan / MScan kind
│  └─ 对应参数、ListMode 列表与 dwell
├─ authoring
│  ├─ selectedBusinessId / selectedPage
│  └─ 每个 IBusiness 的 QVariantMap widgetProfile
├─ payload
│  └─ PlaybackPayload 共享只读句柄（A/B 两份均可常驻）
└─ runtime
   ├─ lastRequested / lastApplied / generation / pending
   ├─ resident waveform identity
   ├─ activePipeline
   └─ StreamingEngine state/progress 摘要
```

`selectedBusiness`、`carrierPlan`、`lastApplied` 不能继续作为 `TxSessionService` 的全局单值；当前单值字段可见于 [txsessionservice.h:75](../../src/plugins/core/txsessionservice.h#L75)、[txsessionservice.h:76](../../src/plugins/core/txsessionservice.h#L76)、[txsessionservice.h:81](../../src/plugins/core/txsessionservice.h#L81)。目标是 `TxSessionService` 作为 UI 协调单例，内部按 `DeviceRole` 保存两套 authoring/runtime 引用，并提供 `requestRefresh(role)`。

### 4.2 为什么不是两套 `PropertyManager` 或两套业务 UI

当前业务 panel 依赖一套全局 PropertyManager；CommonPanel 也直接绑定 `Center/Level/RF/Mod`：[commonpanel.cpp:303](../../src/plugins/core/commonpanel.cpp#L303)、[commonpanel.cpp:359](../../src/plugins/core/commonpanel.cpp#L359)。复制两套 PropertyManager 或注册两套业务 UI 会把所有插件都变成双实例。

采用以下边界：

1. 业务编辑器和 PropertyManager 仍只有一套，始终编辑 `selectedRole`。
2. A/B 各有一份 `DeviceWorkspace`，保存独立 common、carrier、business profile 和 payload。
3. 两块 CommonPanel 是 role-aware view，直接读各自 workspace 的摘要，不同时绑定全局 property bank。
4. 核心回放路径按 role 持有 `TxPipelineRuntime + TxPipelineExecutor`；波形下发完成后设备自走，切换 UI 不影响另一侧。
5. Streaming 不能只靠 profile 快照：运行中的 A/B 各有一份 `StreamingEngine`，UI profile 只是配置来源。

这里必须反复强调 Property 的边界：一套 PropertyManager 只在用户正在编辑某个 role 时临时承载该 role 的可编辑字段，并为软键盘提供当前值、单位和约束。离开编辑角色后，真实状态仍保存在 `DeviceWorkspace(role)` / per-role runtime 中；CommonPanel 摘要、参考时钟、UID、温度、锁定状态等只读信息直接从 workspace、设备缓存或产品规则显示，绝不能为了复用控件而先写入共享 Property。否则 role 切换会制造虚假的 `profileChanged`、Apply 和跨设备串值。

业务 `getProfile()/setProfile()` 已存在：[ibusiness.h:70](../../src/plugins/core/ibusiness.h#L70)。按已冻结前提，默认其往返无损，不额外建设通用自检框架；若某个具体业务实测丢字段，只修该业务的 profile 实现。

### 4.3 role 切换事务

```text
用户选择 B（或点击 CommonPanel B 的可编辑项）
  -> suspend TxSessionService 的 property-triggered refresh
  -> save 当前 A 的 common/carrier/business widget profile 到 workspace(A)
  -> selectedRole = B
  -> restore workspace(B) 到 CommonDeviceProfile / Sweep / 当前业务编辑器
  -> 用 capabilities(B) 更新 metadata、sample-rate list 和业务可用性
  -> 刷新 A/B CommonPanel 摘要与 B 高亮
  -> StreamingPanel 只绑定 progress(B)，A engine 继续运行
  -> resume，不自动 Apply、不停止 A、不重生成或下载 A 波形
```

上述 restore 只覆盖当前业务确实需要编辑的 Property。产品固定/只读字段不进入 save/restore：例如 ETH dual 的 A/B 参考源、参考频率和 RefOut 直接由产品时钟拓扑与设备确认缓存刷新 DeviceSetting View，切换角色不得写 `CommonDeviceProfile`，更不得借此触发 TX refresh。

restore 期间必须屏蔽 `editingFinished`、`providerExecutionContextChanged`、自动 MOD 联动和 `requestRefresh()`。当前 `requestRefresh()` 会直接标记全局 runtime dirty：[txsessionservice.cpp:207](../../src/plugins/core/txsessionservice.cpp#L207)，因此在 role-aware 改造完成前不能先做 UI 切换。

### 4.4 `BusinessManager` 的 active 语义改为 per-role

注册表仍是一套业务类型，UI 仍只有一个业务 panel；变化的是“哪个角色的持续运行体处于 active”。

```text
BusinessManager
├─ registeredBusinesses                 // 不复制
├─ activeBusiness(A)                    // 仅 legacy/持续型运行体
└─ activeBusiness(B)

IBusiness
├─ isActive(role)
├─ setActive(role, enabled)
├─ startBusiness(role)
└─ stopBusiness(role)
```

当前全局唯一 `activedBusiness` 在 [businessmanager_p.h:32](../../src/plugins/core/businessmanager_p.h#L32)，激活新业务会关闭旧业务：[businessmanager.cpp:387](../../src/plugins/core/businessmanager.cpp#L387)。改造后只在同一 role 内互斥：A 切到 Playback 不得停止 B Streaming，B 切换也不得影响 A。

核心托管 pipeline 不依赖 `IBusiness::active`；每角色 runtime 自己表示硬件执行状态。`activeBusiness(role)` 主要服务 Streaming 等持续型 legacy runtime。`MuteBusiness`/`ContinuesWaveBusiness` 的 `m_operator` 使用点先在 Phase 0 判断是否仍可达：[builtinbusiness.cpp:15](../../src/plugins/core/builtinbusiness.cpp#L15)、[builtinbusiness.cpp:42](../../src/plugins/core/builtinbusiness.cpp#L42)。若已被 core pipeline 完全替代，就移除该 legacy 设备访问；若仍可达，则先迁入显式 target executor，不为它们长期保留隐式 canonical A。

#### 4.4.1 `activedBusiness()` / `isActive()` 调用点必须同步审计

`currentDevice()` 改名只能穷举“谁在用设备”，拦不住“谁在用全局唯一 active 业务”。`activedBusiness()` 必须同样用改名/加参制造编译错误，否则下表这些点会在双路 Streaming 下静默错误。

| 调用点 | 当前语义 | 双角色下的正确语义 |
|---|---|---|
| [businessmanager.cpp:59](../../src/plugins/core/businessmanager.cpp#L59)、[businessmanager.cpp:259](../../src/plugins/core/businessmanager.cpp#L259)、[businessmanager.cpp:331](../../src/plugins/core/businessmanager.cpp#L331)、[businessmanager.cpp:375](../../src/plugins/core/businessmanager.cpp#L375) | Manager 内部维护/停止一个 active 指针 | active map、注册移除、reset 和信号全部按 role；整机 reset 才遍历全部 roles |
| [coreruntimeservices.cpp:57](../../src/plugins/core/coreruntimeservices.cpp#L57) | 启动时若无 active 则激活 Mute | 按 `roles()` 逐个初始化，不能只初始化 A |
| [deviceruntimebridge.cpp:47](../../src/plugins/core/deviceruntimebridge.cpp#L47) | 取当前 active 业务决定 feature-spec 刷新 | 使用统一 `anyRoleStreaming()`；canonical 元数据仍只发 A |
| [gpsdialog.cpp:172](../../src/plugins/gps/gpsdialog.cpp#L172)、[gpsdialog.cpp:408](../../src/plugins/gps/gpsdialog.cpp#L408)、[gpsdialog.cpp:433](../../src/plugins/gps/gpsdialog.cpp#L433) | “当前是否在 Streaming”只禁用 GNSS 控件，refresh/apply 仍可能查询 | 必须改为“**任一** role 占用 Streaming”；控件、refresh、apply 三层都阻断 H2 调用 |
| [mainwindow.cpp:812](../../src/plugins/core/mainwindow.cpp#L812)、[mainwindowsettingscontroller.cpp:146](../../src/plugins/core/mainwindowsettingscontroller.cpp#L146)、[updatedialog.cpp:935](../../src/plugins/updater/updatedialog.cpp#L935) | `activedBusiness()->setActive(false)` 停当前业务 | 需要停全部 role（退出、加载配置、固件升级前断开都是整机行为） |
| [mainwindow.cpp:526](../../src/plugins/core/mainwindow.cpp#L526)、[mainwindow.cpp:1531](../../src/plugins/core/mainwindow.cpp#L1531) | active 变化更新窗口标题和 trigger 控件 | 信号带 role；只有变化 role 等于 selectedRole 时才更新中央 UI |
| [txsessionservice.cpp:533](../../src/plugins/core/txsessionservice.cpp#L533)、[txsessionservice.cpp:608](../../src/plugins/core/txsessionservice.cpp#L608) | legacy 过渡判断 | 按 role 判断，A 的 legacy 过渡不得终止 B |
| [streamingbussiness.cpp:314](../../src/plugins/htra/streamingbussiness.cpp#L314)、[streamingbussiness.cpp:328](../../src/plugins/htra/streamingbussiness.cpp#L328) | profile/carrier 变化重启唯一 Streaming runtime | 根据 request/selected role 只唤醒对应 engine；不得用 `anyRoleStreaming()` 决定具体目标 |
| [quickwaveformbusiness.cpp:739](../../src/plugins/quickwaveform/quickwaveformbusiness.cpp#L739)、[analogplaybackbusiness.cpp:72](../../src/plugins/analog/analogplaybackbusiness.cpp#L72) | 用单 bool 决定当前编辑 waveform 是否需生成 | 使用 selectedRole/workspace 的 provider 状态；不能因另一 role 正在使用同一业务类型而生成当前编辑侧 payload |
| [builtinbusiness.cpp:11](../../src/plugins/core/builtinbusiness.cpp#L11)、[builtinbusiness.cpp:38](../../src/plugins/core/builtinbusiness.cpp#L38) | Mute/CW legacy operator 门禁 | Phase 0 证明不可达后删除，或改为显式 target；禁止保留无 role `isActive()` |
| [deviceoperator.cpp:22](../../src/plugins/core/deviceoperator.cpp#L22) 等全部 operator 方法 | 用所属 business 的单 bool 做设备门禁 | operator 从 IBusiness 移出并持 target/token；不再查询全局业务 active |

其中 GNSS 那一条是真实的语义陷阱：它设计时假定“全机只可能有一路流”，如果直接把 `activedBusiness()` 换成 `activeBusiness(DeviceRole::A)`，则 B 单独跑流时 GNSS 配置控件会错误地保持可用。

`IBusiness::isActive()`/`setActive(bool)` 目前是 `IBusinessPrivate::active` 单布尔，并会发出无角色的 `activedStateChanged(bool)`：[ibusiness.cpp:77](../../src/plugins/core/ibusiness.cpp#L77)、[ibusiness.cpp:83](../../src/plugins/core/ibusiness.cpp#L83)。改为 role-aware 时，信号同步改为 `activedStateChanged(DeviceRole, bool)`，Manager 对外发 `currentActivedBusinessChanged(DeviceRole, current, prev)`；所有消费者必须一并迁移，不保留无 role 重载作兼容层。

此外需要两个不同层次的查询，不能混用：

- `activeBusiness(role)`：决定某个 request、engine 或 UI role 的具体业务目标。
- `anyRoleStreaming()`：只用于整机共享资源门禁（GNSS/温度遥测、feature-spec refresh、升级/退出 barrier）。它从第一路进入 Starting 起为 true，到最后一路完成 Stopping barrier 后才变 false。

`TxPipelineRuntime::isActive()`、Qt timer 的 `isActive()` 等同名 API 不属于这次迁移；验收搜索应限定 `IBusiness`/`BusinessManager` 调用，避免无意义改动。

### 4.5 `StreamingEngine`：一个业务 UI，两份独立运行体

```text
StreamingBussiness（单个注册对象 / 单个 StreamingPanel）
├─ widgetProfile(A/B) <-> DeviceWorkspaceStore
├─ engine(A)
│  ├─ StreamingDataGenerator A（生产线程）
│  ├─ DataSender queue A
│  ├─ sender thread A
│  ├─ DeviceOperator(target=A)
│  └─ profile/carrier/execution/progress A
└─ engine(B)
   ├─ StreamingDataGenerator B（生产线程）
   ├─ DataSender queue B
   ├─ sender thread B
   ├─ DeviceOperator(target=B)
   └─ profile/carrier/execution/progress B
```

`StreamingEngine` 不持有 UI widget，只接收一份不可变的 streaming widget settings、device profile 和 carrier context。`StreamingBussiness` 负责 selectedRole 的 profile save/restore、把 Apply 送到对应 engine，以及把 `playbackPosChanged(role, pos)` 只显示到当前正在编辑的 role。当前 progress 信号不带角色：[streamingbussiness.h:72](../../src/plugins/htra/streamingbussiness.h#L72)，必须扩展。

`DeviceOperator` 不再作为 `IBusiness` 的固定 protected 成员。推荐收敛为一个实现，不保留两套方案：

- 构造入口改为 Core-internal 可用，并显式接受 `DeviceTarget` 与 role-active predicate/token。
- 每个 `StreamingEngine` 自己持有一个 operator。
- operator 的配置类方法生成带 target/generation 的异步 control job，完成信号回到对应 engine 后才进入 Running；不要让 sender 线程用 blocking call 反向卡住产品 close。实时 `downloadDataRealTime()` 走绑定 endpoint 的 sender 数据面。
- 从 `IBusiness` 移除 `m_operator`；任何剩余 legacy 调用必须先完成显式 target 迁移。

这样无需给所有业务对象强塞两个 operator，也不会让 StreamingEngine 反向依赖 UI business 的全局 `isActive()`。

### 4.6 控制面与数据面线程边界

```text
控制面（单一 DeviceIoWorker，A/B 串行）
  open / close / preset / status / capability / configuration / sweep arm

数据面（两条 sender thread，并行）
  A sender -> FancyDevice A::downloadDataRealTime() -> tx_send_stream(A)
  B sender -> FancyDevice B::downloadDataRealTime() -> tx_send_stream(B)
```

不能把两路 `tx_send_stream()` 塞进单一 `DeviceIoWorker` 队列，否则任一阻塞发送都会造成另一流 underrun，并阻塞 Apply/status。也不能增加跨 A/B 的全局 SDK mutex；当前实例锁已让同一设备的 configuration/send 互斥，而两台实例彼此不串行。

产品切换/关闭需要一个明确 barrier：先把 A/B engine 标记 stopping、唤醒并排空/丢弃各自队列、等待 sender 不再持有 `IDevice*`，再由 `switchProduct()` 关闭 endpoint 并递增 epoch。旧 engine 的后续 callback 因 epoch 不匹配直接丢弃。

当前 `configuration()` 会在实例锁内完成 common 和 mode 配置：[fancydevice.cpp:2465](../../src/plugins/htra/fancydevice.cpp#L2465)、[fancydevice.cpp:2484](../../src/plugins/htra/fancydevice.cpp#L2484)；`m_sampleRate` 也是实例字段，因此 A/B 重配天然隔离。真正需要实测的是 H2 DLL 对两个独立 handle 同时 `tx_send_stream()` 的线程安全性，而不是在上层预先串行化数据面。

### 4.7 状态、断线与 Streaming 特例

状态轮询在控制 worker 中按 `roles()` 顺序进行并发布 `runtimeStatusUpdated(role, status)`。运行中任一 endpoint 断开，按已冻结产品语义停止两侧 engine、关闭整个 product、清空两个 role runtime，并显示整机断开；不做单边保活/自动重连。

最终门禁采用整机语义：

```text
anyRoleStreaming = A/B 任一 engine 状态属于
                   Starting | Running | Reconfiguring | Stopping

false -> true：
  停止 A/B 的 temperature / power-supply / GNSS / feature-spec H2 查询
  禁用 GpsInfoDialog 的配置、refresh 和 apply
  保存最后一次有效温度/GNSS快照并标记 paused/stale

true -> false（最后一路完成 stop barrier 后）：
  不立即同步查询
  从下一次 DeviceIoWorker status timer 恢复 A/B 非流遥测
  canonical A 可重新 refresh GNSS config，重新启用配置控件
```

这里不是简单跳过整个 status pipeline。`FancyDevice::updateRealTimeStatus()` 末尾还负责 throughput 统计：[fancydevice.cpp:2642](../../src/plugins/htra/fancydevice.cpp#L2642)、[fancydevice.cpp:2646](../../src/plugins/htra/fancydevice.cpp#L2646)。应把状态更新拆成两层：

- `updateDataPlaneStatus()`：不调用 H2 query，只从原子计数/engine fault 更新 connected、throughput、error；Streaming 时继续执行。
- `updateNonStreamingTelemetry()`：temperature、power supply、GNSS 等 H2 查询；仅 `!anyRoleStreaming()` 时在控制 worker 执行。

当前 `tx_send_stream()` 失败会在 sender 路径标记设备关闭：[fancydevice.cpp:2019](../../src/plugins/htra/fancydevice.cpp#L2019)。Phase 4 必须补 `streamFault(role, error)` 到产品断线协调器；暂停非流遥测期间，断线发现完全依赖两条 sender/data-plane fault，不能只等 status timer。

状态栏/GNSS dialog 在暂停期间保留最后一次有效值并明确显示 `Paused (Streaming)` 或 stale 标记，不把 GNSS 清零伪装成“未锁定”，也不把另一 role 的温度复制过来。

需要接受一个可证明的限制：若只有 B Streaming、空闲 A 此时断线，A 没有 sender，而新 API 也没有独立的 realtime device-state query（当前发送失败后的 TODO 同样指出这一缺口：[fancydevice.cpp:2024](../../src/plugins/htra/fancydevice.cpp#L2024)）。在“不做温度/GNSS等探测调用”的策略下，A 断线只能在下一次 A control request 失败，或所有 Streaming 停止、遥测恢复后被发现。第一版接受这个延迟，不额外建立 TCP 探针；若产品要求空闲侧也即时断线，必须由 H2 增加不干扰 Streaming 的 health API。

### 4.8 Common/DeviceSetting 状态归属

| 状态 | 归属 | 下发目标 |
|---|---|---|
| Center / Level / RF / MOD / Trigger Count | A/B 独立 | 当前 role |
| Fixed/FScan/LScan/MScan | A/B 独立 | 当前 role |
| Baseband 选择、profile、Streaming runtime | A/B 独立 | 当前 role |
| Playback payload / waveform / executor resident state | A/B 独立 | 对应 endpoint |
| Reference Clock / LO Mode / RF Port / Trigger In | 统一 UI | A 后 B 同值下发 |
| System Clock Out / Low Power / Fan | 统一 UI | 第一版 A 后 B 同值下发并分别记录结果 |
| Preset / Reset Device | 整机命令 | 遍历全部 roles |
| GNSS | 机箱单份 | 非 Streaming 时只查询/配置 A；任一路 Streaming 时暂停读写 |
| Trigger Out / GPIO | 继续隐藏 | 本轮不恢复 |
| 固件/API/硬件版本 | 机箱单份展示 | 读取 A；B 只写一致性日志 |
| UID / Temperature | endpoint 独立展示 | 同时显示 A、B；任一路 Streaming 时两侧温度冻结为 last-known + stale |

统一设置广播不是原子事务：按 A 后 B 执行并保留两份结果；任一失败显示“统一设置未完全应用”，不伪造回滚。若实机证明某字段是树莓派共享资源，再把该字段的策略从 `Broadcast` 改为 `CanonicalA`，不改变 UI 或产品模型。

## 5. UI 交互方案

### 5.1 固定 1280px Wide 布局示意

```text
┌──────────────────────────────────────────────────────────────────────────┬──────────────────────┐
│ A: [Frequency A] [Level A] [RMS A] [RF A] [MOD A] [Sweep] [General]     │ [      A | B       ] │
├──────────────────────────────────────────────────────────────────────────┤ selector 固定 205px  │
│ B: [Frequency B] [Level B] [RMS B] [RF B] [MOD B] [Sweep] [General]     ├──────────┬───────────┤
├──────────────────────────────────────────────────────────────────────────┤ Digital  │ DSSS      │
│                                                                          ├──────────┼───────────┤
│                  当前 selectedRole 的 carrier/baseband 编辑页            │ OFDM     │ AM        │
│                  A/B 正在运行的另一侧不受切换影响                         ├──────────┼───────────┤
│                                                                          │ FM       │ PM        │
│                                                                          ├──────────┼───────────┤
│                                                                          │ Playback │ Streaming │
└──────────────────────────────────────────────────────────────────────────┴──────────┴───────────┘
Status: UID A abcdef / B 123456 | Temp A 45.2°C / B 46.1°C | 单份 GNSS/版本摘要
```

这版不再设计窄布局：

- `MainWindow::minimumWidth = 1280`。
- 两块 CommonPanel 恒为 `Wide`，状态栏恒为完整 A/B 文本。
- 右栏恒为 205px 双列；selector 作为 dock 顶部独立控件占满 205px，视觉上跨两列。
- 定制开关打开时，`updateResponsiveUi()` 不再触发 CommonPanel/状态栏/右栏 Compact；标准版关闭开关时维持原响应式行为。

### 5.2 CommonPanel 交互规则

- 两块 CommonPanel 始终显示各自最后的工作区值，不因右侧 selector 切换而显示成相同数据。
- A/B 前缀必须是面板的一部分；推荐选中侧的前缀或面板边框使用当前选中色，避免用户看不出中央编辑器属于哪台设备。
- 点击 A/B 的 Frequency、Level、RF、MOD、Sweep 时，先完成 role 切换事务，再打开编辑/执行下发；不得先修改全局 property 再补切 selector。
- 两个 General 按钮都打开同一个 DeviceSetting 页面；因为设置统一，点击 General 不必改变 A/B 选择。
- 两侧 RF/MOD 状态独立。切换 selector 不得自动关闭离开侧输出。
- RMS、UNLEVEL、warning 和 Apply 错误按 A/B 分别回写到对应 CommonPanel。
- A/B 任一 Streaming 运行时，另一侧 CommonPanel、selector 和业务页面仍可正常编辑和 Apply。

### 5.3 右侧 A/B selector

- 默认选择 A。
- selector 表示“当前 carrier/baseband profile 编辑目标”，不是 RF 输出开关，也不是产品/设备切换。
- 固定填满 205px dock 宽度，左右文字为 A/B。
- selector 放在 dock 的固定顶部容器中，不作为 `QListWidgetItem`，否则会参与滚动、业务数量和单列滚动条计算。
- 复用 SwitchButton 样式，但为其补可配置左右文案；`FancyTabWidget` 对外发 `deviceRoleChanged(DeviceRole)`，不要把 `true/false` 传播到业务层。
- selector 变化、点击 A CommonPanel、点击 B CommonPanel 最终都走同一个 `selectRole(DeviceRole)` 入口，避免双向同步递归。

### 5.4 单 panel 编辑、双 runtime 运行

右侧业务按钮只决定 selectedRole 当前编辑的业务。A/B 的 `selectedBusinessId` 各存在 workspace 中：

- A 可停留在 Streaming，B 切到 Playback；A engine 不停止。
- A、B 都选择 Streaming 时，同一个 StreamingPanel 在切换 role 时显示各自 profile 和 progress，两份 engine 同时运行。
- 停止当前 role 的 Streaming 只停止该 engine；`BusinessManager::activeBusiness(otherRole)` 不变。
- profile 的 `save/restore` 只改变编辑副本；运行中 engine 持有自己的 settings snapshot，不引用会被 UI 切换覆盖的 Property 对象。

## 6. ETH 连接与关闭事务

### 6.1 EthConnectDialog

当前对话框创建了可编辑 port 并把 IP/port 一起上报：[ethconnectdialog.cpp:511](../../src/plugins/core/ethconnectdialog.cpp#L511)、[ethconnectdialog.cpp:707](../../src/plugins/core/ethconnectdialog.cpp#L707)。定制版修改为：

- 只保留 IPv4 输入、Local Interface 和网段提示。
- 删除 Port label、line edit、validator、软键盘和 validPort 判断。
- 可显示一行只读说明：`Connects device A (5000) and device B (5001)`；端口不是用户选项。
- signal 改为 `connectRequested(QString ipAddress)`；产品层内部固定构造 endpoint A/B。
- connecting 状态分两行显示 A/B endpoint 进度，失败时明确指出哪一个端口失败。
- `SGS_PRODUCT_ETH_DUAL=ON` 时隐藏/禁用 USB discovery、自动连接和 USB 选择入口；不是把扫描出的 USB 填进 A 角色。

### 6.2 连接事务

当前 controller 只 resolve 一个 manual ETH device 并交给 `setCurrentDevice()`：[mainwindowdevicecontroller.cpp:343](../../src/plugins/core/mainwindowdevicecontroller.cpp#L343)、[mainwindowdevicecontroller.cpp:348](../../src/plugins/core/mainwindowdevicecontroller.cpp#L348)、[mainwindowdevicecontroller.cpp:371](../../src/plugins/core/mainwindowdevicecontroller.cpp#L371)。建议替换为固定产品事务：

```text
输入 IP
  -> factory 创建 FancyDevice A，配置 IP:5000, chs=1
  -> factory 创建 FancyDevice B，配置 IP:5001, chs=1
  -> 组装未激活的 EthPairProduct{A,B}
  -> DeviceIoWorker::switchProduct() 先让旧 product 退出并失效旧 epoch
  -> 串行 open A
     -> 失败：close/delete 待选 product 的 A+B，active product = null，dialog 显示 A 失败
  -> 串行 open B
     -> 失败：先 close A，再 close/delete 待选 product 的 A+B，active product = null，dialog 显示 B 失败
  -> 两侧 capability 都成功建立
  -> 一次性提交 activeProduct 和新 productEpoch；canonicalDevice = A
  -> 发布“产品已连接”和 A/B 两份 metadata/status
```

第一阶段不沿用单设备 open 失败后的后台自动重试，因为这会产生 dialog 已失败但后台某一侧又偷偷连上的状态。重试必须由用户再次点击 Connect 明确发起。

### 6.3 整机切换/关闭

- 标准版 `OFF` 下 USB -> USB：用 `UsbSingleProduct{A}` 走同一个产品事务，行为保持不变。
- 定制版 `ON` 下不提供 ETH -> USB/UI USB 切换；代码层仍能把 active product 切到 null。
- ETH -> disconnected/null：先停止两路 runtime，再按 B、A 关闭；两个都完成后才发 `activeProductDisconnected()`。
- 运行中 A 或 B 任一断开：执行同一整机断开事务，不保留健康侧、不做单边 retry。
- 只切 A/B selector：不发生 open/close/preset/clear/mute。

## 7. UI 到设备的下发链路

### 7.1 Core-managed Playback/CW 路径

```text
CommonPanel A/B 或当前业务 panel
  -> DeviceWorkspaceStore 更新对应 role 的 intent
  -> TxSessionService::requestRefresh(role)
  -> 按 role 读取 common/carrier/business profile
  -> 按 role 读取 capability snapshot
  -> build TxApplyRequest { target(productEpoch, role, port), uid, revision, context }
  -> TxPipelineRuntime(role) 保存 pending/in-flight generation
  -> DeviceManager::submitTxApply(job)
  -> 单一 DeviceIoWorker 控制面串行取 job
  -> 按 target 解析 FancyDevice A 或 B
  -> TxPipelineExecutor(role) 执行
  -> result 带原 target + generation 返回
  -> 只回写对应 workspace/CommonPanel；与此时 UI 正在查看 A 还是 B 无关
```

当前 `DeviceIoWorker` 只有一个 executor：[devicemanager.cpp:609](../../src/plugins/core/devicemanager.cpp#L609)，而 executor 自己保存当前 request/resident state：[txpipelineexecutor.h:45](../../src/plugins/core/txpipelineexecutor.h#L45)。因此 pair 必须各有一份 executor，不能只在执行前临时替换 device。

### 7.2 Streaming 路径

```text
StreamingPanel（selectedRole）
  -> 保存 widget profile 到 workspace(role)
  -> StreamingBussiness::apply(role, settings, common, carrier)
  -> BusinessManager::setActive(role, StreamingBussiness)
  -> StreamingEngine(role)
       -> 控制请求：DeviceOperator -> DeviceIoWorker -> configuration/sweep arm(role)
       -> 控制成功后启动/更新 generator(role)
       -> generator producer thread -> DataSender queue(role)
       -> sender thread(role) -> DeviceOperator::downloadDataRealTime()
       -> FancyDevice(role)::tx_send_stream()
  -> progress/fault 带 role 回主线程
  -> 当前 UI 只显示 selectedRole progress；fault 始终进入产品断线策略
```

控制请求必须完成并确认成功后才允许 sender 消费；重配时仅暂停目标 role 的 generator/sender，不停止另一 role。

### 7.3 Request 必须显式带目标

修改 `TxApplyRequest`/`TxApplyJob`/`TxApplyResult`：

- 增加 `DeviceTarget target`。
- `deviceUid`、capability revision 仍校验目标 device，但不能负责路由；`productEpoch + role` 才负责解析 endpoint。
- stale 判断同时校验 `productEpoch + role + uid + capabilityRevision`。
- 日志统一带 `[Device A][IP:5000]` 或 `[Device B][IP:5001]`。

### 7.4 Executor/Operator 不再读取全局设备

`TxPipelineExecutor::requestApply()` 接收 worker 已按 target 解析的 `IDevice *targetDevice` 和 capability snapshot，执行阶段不得再读取全局设备。`DeviceOperator` 同样绑定 target，不在每个方法里重查 current device。

建议每个 endpoint 一份 `TxPipelineExecutor`，原因是当前 executor 自己保存 resident playback identity/device UID/revision。两份 executor 才能在 A/B 切换后保留各自的驻留状态，避免只因编辑目标变化而误判 cache 失效或重复下载。

### 7.5 Pending/Generation/active 必须按 role 分开

当前单一 runtime 的“latest wins”只能表达一个目标。改造后 A 和 B 各有 pending/latest/generation：

- A 的快速连续编辑只合并 A 的 request。
- B request 不得覆盖尚未执行的 A request。
- worker 串行执行 A/B 的控制请求；两条 streaming data send 不进入这个队列。
- 结果回主线程时按 target 回写，即使用户已经切到另一侧也不串台。
- `BusinessManager::activeBusiness(role)` 只终止同 role 的旧持续业务；不得再调用无 role 的全局 `terminate()`。

### 7.6 Capability 限制

- 每个 FancyDevice open 后仍可调用 H2 capability API 记录原始返回值，作为底层诊断信息；产品层不分别发布 A/B 两套能力。
- A/B CommonPanel、Sweep 和 baseband 共用一份产品 capability：Playback 最高 125 MSps、Streaming 最高 62.5 MSps，最大波形内存固定 125 MiB。
- selector 切换不重建 property metadata/available units/sample-rate list，也不触发 capability 查询。
- `capabilities(role)` 保留显式 role 参数，但返回的能力内容相同；per-role snapshot 的 UID/revision 只负责请求身份和 stale 校验。
- 实际下发收到 `H2_WARNING_PARAMOUTRANGE` 时仍按既有 contract 记录底层 capability bug，但日志必须注明 A/B。

### 7.7 Preset、close 和 Apply 的互斥顺序

- `preset/reset/close/switchProduct` 先阻止新 Apply，再停两路 StreamingEngine，再等待两条 sender 退出设备调用区，最后进入控制 worker。
- 控制 worker 内按固定顺序 B -> A 停止/关闭；open 顺序 A -> B。
- Apply job 入队时捕获 target/epoch；在队头等待期间若产品已切换，executor 不接触设备，直接返回 stale。
- 不允许从 UI 主线程直接调用 `IDevice`，也不允许 control worker 删除仍被 sender 使用的 endpoint。

## 8. 元数据、状态栏、About、Updater、GNSS 和许可证

### 8.1 状态栏

`DeviceInfoWidget` 增加 pair 输入或由 controller 拼好 display model：

```text
UID:  A abcdef / B 123456
Temp: A 45.2°C / B 46.1°C
```

- 沿用现有短 UID 策略可减少宽度压力，但 tooltip/复制信息应保留完整 UID。
- A/B 温度来自各自 status；一个无效时显示 `A -- / B 46.1°C`，不能拿另一侧温度补齐。
- 任一 role Streaming 时，两侧都沿用最后一次有效温度并显示 `Paused (Streaming)`/stale；本产品策略不在流中恢复周期温度查询，即使单独实测某个 H2 query 可用也不例外。
- warning/error 文本增加 A/B 前缀。
- GNSS lock、接口名、固件/API 版本仍显示单份 canonical A 信息。
- 定制版不再进入紧凑状态栏模式。

### 8.2 About

- `UID64` 显示 `A: ... / B: ...`。
- `UID32` 同样建议拼接 A/B，复制 Device Info 也同时带两份 UID。
- Model/HCD/MCU/FPGA/BUS/EIO/API 只显示 A 的一份。
- open 后比较 A/B 的 model/version 字符串；不一致时写清晰日志，但 UI 仍按产品约定显示 A，不增加重复固件栏。

### 8.3 Updater

- 当前固件版本显示继续读取 canonical A。
- 启动维护程序继续传 A 的 `IP:5000`，不增加 B 固件版本列。
- 已确定由外部 updater 程序负责整机升级；SGStudio 不拆分 A/B 升级事务。
- SGStudio 仍在连接时记录 A/B version mismatch，便于发现外部升级后的不一致，但不在本方案内自动修复。

### 8.4 GNSS

- 仅保留一个 GpsInfoDialog。
- 非 Streaming 时，查询、配置、状态锁定全部走设备 A。
- 不向 B 重复配置，也不在 A/B selector 切换时重建 dialog 或重新读 GNSS。
- 任一 role 从 Starting 起，立即禁用 GNSS 配置控件并关闭在用数字键盘；`showEvent`、`gnssSettingsApplied`、手工 refresh 和 apply 都不得调用 H2。
- 暂停期间保留最后一次有效定位并显示 `Paused (Streaming)`，不将清零快照解释成 GNSS 丢锁；最后一路完成 Stopping 后从下一次控制 timer 恢复。

### 8.5 数字调制许可证

- active ETH session 连接完成后，只以设备 A 的 `DeviceInfo` 调用 `DeviceParamsManager::onDeviceConnected()`。
- 设备 B metadata 不进入许可证链路。
- A/B selector 切换不发 `deviceConnected(B)`，不清空/重算许可证。
- Digital/DSSS/OFDM 的显示可用性对 A/B 相同，均由 A 证书决定；但两侧业务 profile 和生成的 waveform 可不同。

这几条旧链路保留单份，不意味着又引入“全局当前设备”；它们必须显式调用 `canonicalDevice()` 或消费产品级 canonical metadata，源码评审中可据此区分有意使用 A 与遗漏 role 的 bug。

## 9. 分步实施方案

### Phase 0：产品开关、术语和强制调用点审计

目标：先制造可控的编译断点，消除所有“隐式当前设备”访问，再引入双产品代码。

主要文件：

- [CMakeLists.txt](../../CMakeLists.txt)
- [devicemanager.h](../../src/plugins/core/devicemanager.h)
- [devicemanager.cpp](../../src/plugins/core/devicemanager.cpp)
- [deviceoperator.cpp](../../src/plugins/core/deviceoperator.cpp)
- [txpipelineexecutor.cpp](../../src/plugins/core/txpipelineexecutor.cpp)
- [txsessionservice.cpp](../../src/plugins/core/txsessionservice.cpp)
- [businessmanager.h](../../src/plugins/core/businessmanager.h)
- [businessmanager.cpp](../../src/plugins/core/businessmanager.cpp)
- [ibusiness.h](../../src/plugins/core/ibusiness.h)
- [coreruntimeservices.cpp](../../src/plugins/core/coreruntimeservices.cpp)
- [deviceruntimebridge.cpp](../../src/plugins/core/deviceruntimebridge.cpp)
- [gpsdialog.cpp](../../src/plugins/gps/gpsdialog.cpp)
- [quickwaveformbusiness.cpp](../../src/plugins/quickwaveform/quickwaveformbusiness.cpp)
- [analogplaybackbusiness.cpp](../../src/plugins/analog/analogplaybackbusiness.cpp)
- [remoteminibarservice.cpp](../../src/plugins/core/remoteminibarservice.cpp)
- [runtimeprofilepersistence.cpp](../../src/plugins/core/runtimeprofilepersistence.cpp)
- [instancestateregistry.h](../../src/libs/utils/instancestateregistry.h)
- [instancestateregistry.cpp](../../src/libs/utils/instancestateregistry.cpp)

步骤：

1. 增加默认 `OFF` 的 `SGS_PRODUCT_ETH_DUAL` 和共享生成配置头。
2. 增加 `DeviceRole`、`DeviceProductKind`、`DeviceTarget`、固定 endpoint 描述和统一日志 formatter。
   同时定义 ETH pair 共享能力上限：Playback 125 MSps、Streaming 62.5 MSps、波形内存 125 MiB；A/B 不创建两份 capability 数据。
3. 将 `currentDevice()` 强制改名为 `canonicalDevice()`；将 `currentDeviceCapabilities()` 全部改为显式 `capabilities(role)`，两者都不留 deprecated alias。
4. 按第 3.3 节表格逐个修复编译错误：canonical、role endpoint、product lifecycle、product setting 四类必须明确。
5. 用可执行判定而不是纯代码阅读确认 `MuteBusiness`/`ContinuesWaveBusiness` 的 legacy operator 路径是否仍可达：在两个 `onDeviceProfileChanged()` 入口各加一条临时 `qWarning`，Debug 运行覆盖 Mute、CW、业务切换、Preset 和加载配置路径；全程未命中则删除该 legacy 设备访问，命中则记录触发场景并迁入显式 target executor。结论记录后移除临时日志。
6. 对 `activedBusiness()`、`IBusiness::isActive()/setActive()`、`activedStateChanged` 和 `currentActivedBusinessChanged` 执行第 4.4.1 节的强制审计；本阶段先穷举归类，per-role 语义改造放到 Phase 2。
7. 冻结外围功能的临时所有权：`SGS_PRODUCT_ETH_DUAL=ON` 时，在完成第 11.3 节协议前明确禁用 Remote Minibar 和 Save & Load；不允许静默接 canonical A，让用户误以为操作的是 selectedRole。
8. 冻结定制版实例策略：不扩展 `InstanceStateRegistry`，定制版直接屏蔽 USB scanner/自动连接/选择入口；单进程只允许一个固定 `IP:5000 + IP:5001` product 处于 connecting 或 active。跨进程 ETH pair 排他不属于本轮范围。
9. 把“每个 FancyDevice 只使用本 endpoint 的 `channels[0]`/stream 0”写成 HTRA adapter 不变量和日志。
10. 不读取或更新 KnowledgeBase；本 TaskLog 是本轮实现边界。

验收：

- `rg "currentDevice\(|currentDeviceCapabilities\(" src` 无结果。
- `rg "activedBusiness\(|currentActivedBusinessChanged|activedStateChanged" src/plugins` 的每一条业务相关命中都能在第 4.4.1 节找到迁移类别；所有 `IBusiness::isActive()` 使用点也已归类。
- 定制版 Remote Minibar 和 Save & Load 在其 role-aware 协议完成前有明确禁用态，不存在“看上去支持 A/B、实则只作用于 A”的路径。
- `InstanceStateRegistry` 不增加 ETH schema/lease；定制版的 USB 多实例自动连接链路被整体绕过，ETH pair 只做进程内一次连接门禁。
- `SGS_PRODUCT_ETH_DUAL=OFF` 的 Windows x86_64 Debug build 通过，USB 单设备全部行为不变。
- 新增的 target formatter 能稳定输出 product epoch、role 和 endpoint；A/B 从未命名成 H2 channel index。现有 request/status 全量携带 target 的改造属于 Phase 1–2。

#### Phase 0 实施记录（2026-08-11）

- 已增加默认 `OFF` 的 `SGS_PRODUCT_ETH_DUAL` 和全 first-party target 共用的生成配置头：[CMakeLists.txt:44](../../CMakeLists.txt#L44)、[productfeatures.h.in:3](../../src/productfeatures.h.in#L3)、[src/CMakeLists.txt:7](../../src/CMakeLists.txt#L7)。
- 已增加 `DeviceRole`、`DeviceProductKind`、`DeviceTarget`、5000/5001 endpoint、统一 target formatter，以及共享产品能力常量：[deviceproducttypes.h:11](../../src/plugins/core/deviceproducttypes.h#L11)、[deviceproducttypes.h:23](../../src/plugins/core/deviceproducttypes.h#L23)、[deviceproducttypes.h:38](../../src/plugins/core/deviceproducttypes.h#L38)、[deviceproducttypes.cpp:38](../../src/plugins/core/deviceproducttypes.cpp#L38)。
- “125 M 波形”已由实机日志和产品确认收敛为 125 MiB，即 `125 * 1024 * 1024 = 131,072,000 bytes`，对应 32,768,000 个 complex16 IQ sample。Playback 上限固定 `125,000,000 Hz`，Streaming 上限固定 `62,500,000 Hz`，A/B 能力内容共享：[deviceproducttypes.h:40](../../src/plugins/core/deviceproducttypes.h#L40)、[deviceproducttypes.cpp:47](../../src/plugins/core/deviceproducttypes.cpp#L47)。
- 已删除旧读取入口；canonical 单份资源走 `canonicalDevice()`，endpoint 类调用点先显式写为 `device(DeviceRole::A)`，capability 调用统一为 `capabilities(role)`：[devicemanager.h:38](../../src/plugins/core/devicemanager.h#L38)、[devicemanager.cpp:329](../../src/plugins/core/devicemanager.cpp#L329)。`rg "currentDevice\(|currentDeviceCapabilities\(" src` 已无结果。
- 已按第 4.4.1 节完成 active 调用点清单；本阶段未提前修改 global active 语义。Mute/CW 两个 legacy 入口已加入临时 `[Phase0Audit][LegacyBusiness]` 日志：[builtinbusiness.cpp:13](../../src/plugins/core/builtinbusiness.cpp#L13)、[builtinbusiness.cpp:42](../../src/plugins/core/builtinbusiness.cpp#L42)。由于本轮没有执行带设备的 UI 场景，Mute/CW/Preset/配置加载覆盖仍待实机日志确认，确认后必须移除这两条临时日志。
- `SGS_PRODUCT_ETH_DUAL=ON` 时，Save/Open、启动恢复、Power On State 和底层 runtime profile API 均有显式禁用/拒绝；Remote Minibar 按钮禁用、helper 拒绝启动、remote service 不创建：[mainwindowsettingscontroller.cpp:183](../../src/plugins/core/mainwindowsettingscontroller.cpp#L183)、[runtimeprofilepersistence.cpp:63](../../src/plugins/core/runtimeprofilepersistence.cpp#L63)、[mainwindow.cpp:402](../../src/plugins/core/mainwindow.cpp#L402)、[mainwindow.cpp:1129](../../src/plugins/core/mainwindow.cpp#L1129)、[minibarhelpercontroller.cpp:112](../../src/plugins/core/minibarhelpercontroller.cpp#L112)。
- `FancyDevice` 已固定只接受本 endpoint 返回的 `channels[0]`/stream 0，并输出 `[H2AdapterInvariant]` 日志；不再向后扫描其他 H2 channel：[fancydevice.cpp:665](../../src/plugins/htra/fancydevice.cpp#L665)、[fancydevice.cpp:683](../../src/plugins/htra/fancydevice.cpp#L683)。
- 验证：`git diff --check` 通过；`SGS_PRODUCT_ETH_DUAL=OFF` 的现有 `build/cmake-win-debug` Windows x86_64 Debug 全量构建通过。构建只出现既有 QuickWaveform AutoMoc warning，没有新增编译错误。
- Phase 0 截止点尚未创建 `DeviceProduct`、双 endpoint 生命周期或 per-role runtime/status；这些内容已在下方 Phase 1 实施记录中补齐。

### Phase 1：`DeviceProduct` 生命周期和 per-role 状态

主要文件：

- [devicemanager.h](../../src/plugins/core/devicemanager.h)
- [devicemanager_p.h](../../src/plugins/core/devicemanager_p.h)
- [devicemanager.cpp](../../src/plugins/core/devicemanager.cpp)
- [idevice.h](../../src/plugins/core/idevice.h)
- [fancydevice.h](../../src/plugins/htra/fancydevice.h)
- [fancydevice.cpp](../../src/plugins/htra/fancydevice.cpp)
- [businessmanager.h](../../src/plugins/core/businessmanager.h)
- [businessmanager.cpp](../../src/plugins/core/businessmanager.cpp)
- [devicemanager.cpp](../../src/plugins/core/devicemanager.cpp)
- [deviceruntimebridge.cpp](../../src/plugins/core/deviceruntimebridge.cpp)
- [gpsdialog.cpp](../../src/plugins/gps/gpsdialog.cpp)
- [ethconnectdialog.h](../../src/plugins/core/ethconnectdialog.h)
- [ethconnectdialog.cpp](../../src/plugins/core/ethconnectdialog.cpp)
- [mainwindowdevicecontroller.h](../../src/plugins/core/mainwindowdevicecontroller.h)
- [mainwindowdevicecontroller.cpp](../../src/plugins/core/mainwindowdevicecontroller.cpp)
- [plugin.cpp](../../src/plugins/htra/plugin.cpp)

步骤：

1. 定义抽象 `DeviceProduct` 和两个具体形态 `UsbSingleProduct`、`EthPairProduct`；产品只保存有序 role→`IDevice*` 映射，不把两个 endpoint 合成一个 `FancyDevice`。
2. `DeviceManager` 增加 `activeProduct()/roles()/device(role)/capabilities(role)/runtimeStatus(role)/productEpoch()`；A/B 的 capability 内容共享，设备身份、snapshot revision 和 runtime status 按 role 维护。
3. 保留 `setCurrentDevice()` 作为标准版 USB 单产品包装入口，内部统一转成 `setActiveProduct(UsbSingleProduct)`；worker 生命周期入口改成 `switchProduct()`。
4. 定制版不扩展 `InstanceStateRegistry`：HTRA 不注册/启动 USB scanner，DeviceManager 禁止任何 USB fallback，USB Connect 菜单隐藏。现有 registry 只服务标准版，ETH pair 不申请跨进程 lease。
5. 新增 `connectEthPair(ip)`：固定创建 `IP:5000` 的 A 和 `IP:5001` 的 B。当前进程处于 connecting/active 时直接拒绝；两侧曾成功连接后，本次应用会话内永久拒绝第二次连接，失败 rollback 后仍允许用户重试。
6. worker open 顺序 A -> B，close 顺序 B -> A；仅在两个 endpoint 都成功 open 后发布 active product 和两份 role identity/status。任一 open 失败立即按 B -> A 关闭已打开侧，清空 active product，不进入后台单边 retry。
7. A/B 的产品能力内容固定相同：Playback sample-rate domain 为 `[195312.5, 125000000] Hz`，Streaming 为 `[195312.5, 62500000] Hz`，最大波形内存为 `131072000 bytes`；底层原始 H2 capability 仍由各 `FancyDevice` 打日志用于诊断。
8. 状态轮询遍历 `roles()` 并发出 `deviceRealTimeStatusUpdated(role, status)`；现有单份消费者在 Phase 6 前继续只消费 A compatibility signal。
9. 运行中任一 endpoint 断开进入整机 disconnect transaction；不保留另一侧，不自动单边重连。
10. `canonicalDevice()` 对 `UsbSingleProduct` 返回 A，对 `EthPairProduct` 固定返回 A，但只允许第 3.3 节明确列出的调用者使用。
11. 增加同步 shutdown barrier：软件退出时先停轮询和 TX，再在 Device I/O worker 上按 B -> A 调用 `close()`，等待完成后才销毁设备对象和卸载 HTRA 插件。
12. 定制版 ETH dialog 只保留 IP 输入；连接提示明确显示固定 endpoints `IP:5000 + IP:5001`。

验收：

- `SGS_PRODUCT_ETH_DUAL=OFF` Debug 全量构建通过，标准 USB 单产品行为不变。
- `SGS_PRODUCT_ETH_DUAL=ON` 构建后可以打开 IP-only ETH dialog；点击连接只产生固定 5000/5001 两次 open。
- 两侧都成功时 dialog 才关闭，日志按 A -> B 显示 open success；任一失败时 dialog 保留并显示错误，日志显示 rollback，产品保持未连接。
- 连接成功后 ETH Connect action 禁用，本次应用会话内 API 再次调用也明确拒绝；只有首次事务失败 rollback 后允许重试。
- 关闭软件时日志按 B -> A 显示 close begin/success，两个 H2 handle 都在插件卸载前释放。
- `roles()=={A,B}`、两侧 capability 内容一致且为固定范围；UID/runtime status 仍各自独立。
- `InstanceStateRegistry` 无 schema/共享内存布局修改；定制版不启动 USB scanner，不发生 USB 自动连接。

#### Phase 1 实施记录（2026-08-11）

- 已实现非 owning 的 `DeviceProduct`、`UsbSingleProduct` 和固定 A/B 的 `EthPairProduct`；两个 endpoint 始终是两个独立 `IDevice/FancyDevice`：[deviceproduct.h](../../src/plugins/core/deviceproduct.h)、[deviceproduct.cpp](../../src/plugins/core/deviceproduct.cpp)。
- `DeviceManager` 已改为产品生命周期入口，提供 `activeProduct()/roles()/device(role)/capabilities(role)/runtimeStatus(role)/productEpoch()`；标准版 `setCurrentDevice()` 只负责包装单角色 USB 产品：[devicemanager.h](../../src/plugins/core/devicemanager.h)、[devicemanager.cpp](../../src/plugins/core/devicemanager.cpp)。
- ETH 连接固定创建 `IP:5000` 和 `IP:5001`，worker 按 A→B 打开、B→A 关闭；任一打开失败会立即关闭已开侧并清空 active product，不进入单边后台重试：[devicemanager.cpp](../../src/plugins/core/devicemanager.cpp)。
- 定制版不注册或启动 HTRA USB scanner，DeviceManager 也拒绝 scanner fallback；USB Connect 菜单隐藏，启动阶段绕过 USB 多实例门禁：[plugin.cpp](../../src/plugins/htra/plugin.cpp)、[mainwindowdevicecontroller.cpp](../../src/plugins/core/mainwindowdevicecontroller.cpp)、[mainwindow.cpp](../../src/plugins/core/mainwindow.cpp)。
- ETH dialog 在定制开关下只显示 IP，连接提示固定展示两个 endpoint；连接中或本次会话曾成功后拒绝第二次连接，失败时保留 dialog 供用户显式重试：[ethconnectdialog.cpp](../../src/plugins/core/ethconnectdialog.cpp)、[mainwindowdevicecontroller.cpp](../../src/plugins/core/mainwindowdevicecontroller.cpp)。
- A/B 对外发布相同的固定产品能力：Playback `195312.5–125000000 Hz`、Streaming `195312.5–62500000 Hz`、波形内存 `131072000 bytes`；UID、revision 和 runtime status 仍按 role 保存：[deviceproducttypes.h](../../src/plugins/core/deviceproducttypes.h)、[deviceproducttypes.cpp](../../src/plugins/core/deviceproducttypes.cpp)。
- Core shutdown 已加入同步产品关闭 barrier，确保退出时先在 Device I/O worker 上按 B→A 关闭，再进入插件销毁：[coreplugin.cpp](../../src/plugins/core/coreplugin.cpp)、[devicemanager.cpp](../../src/plugins/core/devicemanager.cpp)。
- 静态/构建验证：`git diff --check` 通过；`SGS_PRODUCT_ETH_DUAL=OFF` 的 Core/HTRA 编译通过；随后将现有 Windows x86_64 Debug 构建树配置为 `SGS_PRODUCT_ETH_DUAL=ON` 并完成全量构建。无编译错误；仍有仓库既有的 MSVC 编码/STL deprecation warning 和 QuickWaveform AutoMoc warning。当前 `bin/SGStudio.exe` 和 `plugin/*.dll` 保留为定制版实机测试产物。

### Phase 2：显式 target 的 TX pipeline、Operator 和 active 语义

> 2026-08-11 实施拆分：Phase 2 不再一次性接入完整业务模型，先交付可实机验证的 2A，再进入 2B。此拆分改变实施顺序，不改变最终架构目标。

#### Phase 2A：双 CommonPanel 与独立 Fixed CW/Mute

本阶段只解决 `center + level + RF`，明确不接入 carrier plan、Sweep、baseband、Playback、Streaming、per-role active business 和完整 workspace。

实现边界：

1. `TxApplyRequest` 携带唯一 `DeviceTarget`；`TxPipelineRuntime`、`DeviceIoWorker` 和 `TxPipelineExecutor` 按 role 隔离，CW/Mute 请求只解析并操作目标 endpoint。
2. `TxSessionService` 暂存 A/B 两份轻量 `RoleCommonOutputState { center, level, rfEnabled }`。一套 `PropertyManager` 只作为 selectedRole 的编辑缓冲区；切换 selectedRole 先保存旧 role、再装载新 role，并屏蔽装载过程产生的 Apply。
3. 定制版增加第二块 `CommonPanel`，两块固定 Wide，并显示 `A`/`B`。点击任一面板的 center、level 或 RF 会先选中该 role；只有被选中的面板跟随全局属性编辑信号，另一块保留自己的显示和值。
4. 定制版右侧 business list 保持正常可读/可选样式，暂不让其选择参与 Phase 2A 的设备下发；原标准版单设备 UI、属性绑定和完整 TX orchestrator 行为保持不变。
5. ETH pair 成功打开后，A/B 都获得一次明确的初始 Apply；B 默认 RF Off，避免仅打开但未配置的 endpoint 意外输出。
6. role 切换本身不得触发 H2 调用；只有 center/level 编辑完成或 RF 点击才产生目标 role 的 Apply。

Phase 2A 实施记录（2026-08-11）：

- 已完成 `DeviceTarget` 贯穿 request/runtime/worker/executor；A/B 各自持有 runtime 与 executor，executor 内全部设备解析均使用 request role。
- 已完成 `RoleCommonOutputState` 双份状态和 selectedRole 属性缓冲区。Frequency/Level 点击顺序固定为：同步 role state 到 `CommonDeviceProfile/Property`，再发 `beginEditing` 初始化软键盘；装载阶段由 `m_loadingCommonOutputRole` 抑制 Apply。
- 已完成双 CommonPanel、A/B 标签、固定 Wide、1280px 最小宽度；定制版暂时禁用 MOD/Sweep/business enable，B 初始 RF Off。
- 已保护异步 A writeback：编辑 B 时，A completion 不再覆盖 PropertyManager 中的 B profile。

Phase 2A UI/Property/Preset 修正（2026-08-11）：
- A/B Center/Level 的持久显示改由 `commonOutputStateChanged(role, state)` 直接驱动对应 CommonPanel；共享 Property 只负责 selected role 的软键盘、范围、单位和编辑中的即时显示。非选中面板、Preset 和异步设备回写不再依赖临时切换 role 才能刷新标签。
- 右侧 business list 恢复正常启用样式；Phase 2A 的 TX request 仍忽略 business/carrier/baseband，只按各 role 的 center/level/RF 生成 Fixed CW 或 Mute。
- Preset 在更新暂停区内把 A/B 两份 `RoleCommonOutputState` 都重置为默认 center/level/RF，并刷新两块面板；随后遍历产品 `roles()` 调用每台设备的 `resetDevice()`。恢复 pipeline 更新后，A/B 分别按默认 Mute 状态重新 Apply。

Phase 2A 固定参考时钟拓扑（2026-08-11）：

- ETH pair 连接事务在 A/B 都 open 后、发布产品成功前固定建立参考关系：A/5000 配置为内部参考且 RefOut 开启；随后用 `device_query_clock(A)` 读取设备确认频率；B/5001 配置为外部参考、频率使用 A 的查询返回值、RefOut 关闭。任一步配置或查询失败都视为整机连接失败，并按 B→A 回滚关闭。
- 不能只在 open 后配置一次。`FancyDevice::configuration()` 的每次 CW/Mute Apply 都会重新调用 `device_config_clock`，因此 Phase 2A 构造每个 role 的 TX request 时也必须覆盖为上述固定拓扑，不能从当前 selectedRole 的共享 Property 取得时钟源/输出值。
- `IDevice` 增加完整参考时钟配置与“只读缓存快照”入口；H2 query/config 只在 Device I/O worker / FancyDevice 锁内执行。DeviceSetting UI 从设备确认后的缓存刷新，不在 UI 线程发起 H2 查询。
- 点击 A/B CommonPanel 的“通用设置”前先同步 selectedRole。DeviceSettingPanel 展示对应角色的 ReferenceClock、ExRefFrequency、RefOut，其中参考源、频率和输出三个控件在定制版全部不可编辑；标准单设备产品保持原交互。
- 验收日志应依次出现 A `source=Internal refOut=ON`、B `source=External refOut=OFF`，且两侧频率相同；切换 A/B 设置页面不产生 H2 调用，后续修改 Center/Level/RF 后 request 日志仍保持固定的 role 时钟字段。
- 实施完成：`IDevice/FancyDevice` 已提供完整时钟 config、H2 query 和无 H2 的缓存快照；DeviceIoWorker 已把固定 A→query→B 时钟配置纳入 ETH pair 全成功连接事务；TxSessionService 在受抑制的 selectedRole Property 装载和每个 role 的 Apply request 中都使用固定时钟拓扑；DeviceSettingPanel 已按 selectedRole 显示并锁定三个参考时钟控件。

Phase 2A 固定参考时钟 Property 简化（2026-08-11）：

- Center/Level 继续使用 PropertyManager，原因仅限于数值键盘、单位、范围和 metadata；两块 CommonPanel 的常驻摘要仍直接读取各 role state，不把 Property 当双份状态仓库。
- ETH dual 的 ReferenceClock/ExRefFrequency/RefOut 是产品固定只读值，不再绑定 `RefClockSource/RefClockFrequency/SystemClockOut` Property，也不在 selectedRole 切换时写入 `CommonDeviceProfile`。DeviceSettingPanel 直接读取 FancyDevice 的已确认时钟缓存并设置三个控件文本。
- 标准单设备产品仍保留原参考源 Property 绑定、外参考频率数值键盘和 RefOut 读写；不全局删除 Property 注册，避免破坏标准版功能。
- 验收要求：切 A/B 时三项只读显示正确；Property/Profile 不随查看动作变化；纯查看不产生 `profileChanged`、TX Apply 或 H2 调用。
- 实施完成：定制版三个参考时钟控件已解除 Property/键盘/写回绑定，直接用 role + `cachedReferenceClock()` 设置只读文本；TxSessionService role 装载不再调用 `CommonDeviceProfile::setReferenceClock()`。`git diff --check` 和 `SGS_PRODUCT_ETH_DUAL=ON` Core Debug 增量构建通过。
- 静态检查 `git diff --check` 通过；复用 `SGS_PRODUCT_ETH_DUAL=ON` 的 `build/cmake-win-debug` 完成 Windows x86_64 Debug 全量构建，无编译错误。输出仍只有仓库既有的 C4819、STL deprecation 和 QuickWaveform AutoMoc warning。

Phase 2A 实机复验与 RMS 决策（2026-08-11）：

- B 的 RMS 一直为 `--` 不是硬件问题：当前 `TxSessionService` 只在 role A Apply 成功后发布 RMS，MainWindow 也只把 legacy RMS signal 写入 CommonPanel A。
- Phase 2A 立即补 per-role RMS signal：Fixed CW 的 RMS 等于该 role 的 Level；Mute 保持 `--`。不能把 Level 在所有模式下直接显示为 RMS。
- Phase 2B/3 延续原语义：Playback RMS 使用各 role payload 的 `level + waveform RMS offset`；Streaming 在没有可靠实时功率指标前保持 `--`，不伪造数值。
- per-role RMS 修正完成后，`git diff --check` 通过；`SGS_PRODUCT_ETH_DUAL=ON` 的 Windows x86_64 Debug 全量构建通过，无编译错误，仍只有仓库既有 warning。

#### Phase 2A-carrier：双设备独立 FScan/LScan（2026-08-11）

本切片只把现有 CW carrier 从 `Fixed` 扩展到 `FScan/LScan`，仍不接入 MScan/ListMode、baseband、Playback、Streaming、per-role business activation 和完整 workspace。用户已从 UI 移除 ListMode/MScan 入口，本阶段不得恢复，也不得让残留的 `MScan` 值进入设备下发。

实现边界：

1. A/B 各自持有一份 Sweep enable 和 `TxCarrierPlanContext`；设备请求必须携带目标 role 的快照，异步执行期间不得再读取当前 UI/PropertyManager。
2. 单套 `StepSweepPanel` 仍是 selectedRole 的临时编辑器。切换 A/B 时先保存旧 role 的 analog profile，再恢复新 role 的 profile；切换本身不得产生 H2 Apply。PropertyManager 只承担当前编辑值和软键盘初始化，不作为 A/B carrier 真值仓库。
3. 定制版两块 CommonPanel 的 Sweep 按钮恢复可用；点击某块面板的 Sweep 必须先选择该 role，再进入同一个 Sweep 页面。两块按钮的点亮状态分别反映 A/B 的 Sweep enable。
4. 下发矩阵固定为：`RF=OFF -> Mute`；`RF=ON && Sweep=OFF -> FixedCw`；`RF=ON && Sweep=ON && kind=FScan/LScan -> SweepCw`。任何缺失上下文或 `MScan` 均不得伪装成成功的 Sweep 请求。
5. 复用现有 `TxPipelineExecutor::applySweepCw()`：公共配置和固定参考时钟仍按 role 构造，随后只对同一 role 调用 FScan/LScan API。Sweep writeback 必须携带 role，只允许回写当前正在编辑的 role；后台 B 的回写不能污染 A 的 UI。
6. Preset 同时把 A/B 的 Sweep profile 恢复为默认值并关闭 Sweep；随后按既有双设备 preset 流程处理设备。

验收标准：

- A、B 可以分别选择 FScan 或 LScan，并保存不同的起止值、步进和 dwell；来回切换时 UI 不串值。
- A/B role 切换只更换编辑缓冲，不触发设备 Apply；修改参数、Sweep enable、RF 后只刷新对应 role。
- `RF ON + Sweep ON` 时目标 role 进入 `SweepCw`，日志明确显示 role/endpoint 以及 FScan 或 LScan 参数；另一 role 的输出状态不受影响。
- `RF OFF` 始终 Mute，`Sweep OFF` 始终 Fixed CW；MScan/ListMode 不可达。
- 静态检查通过，并使用现有 Windows Debug 构建树完成 Core 目标构建验证。

实施记录：

- TxSessionService 已增加 A/B 各自的 Sweep enable + carrier context，并在双 ETH 刷新时按 role 选择 Mute、FixedCw 或 SweepCw；request 在进入 runtime 前已经固化目标 role、endpoint、固定参考时钟和 FScan/LScan 参数。
- MainWindow 已用两份 analog profile map 保存 A/B 的 Sweep authoring state；单套 StepSweepPanel/PropertyManager 只装载 selectedRole。CommonPanel Sweep 点击顺序为先选 role、再打开页面，纯切换不调用 requestRefresh。
- 双 CommonPanel 的 Sweep 按钮已恢复，A/B 各自显示 enable；Preset 同时清空两份 carrier state。Sweep runtime writeback 已增加 role 信号，后台 B 不再通过 legacy A 信号写入当前 UI。
- StepSweep preview 已按 selectedRole 使用对应 FancyDevice；List 选项仍由现有 Property 注册注释保持隐藏，本切片只接受 FScan/LScan。
- git diff --check 通过；SGS_PRODUCT_ETH_DUAL=ON 的 Windows x86_64 Debug Core 增量构建及全量构建均通过，无编译错误，仅有仓库既有 C4819/STL deprecation warning。实机扫频行为等待用户在双 ETH 设备上验证。

双内参考诊断配置（2026-08-11）：

- 为排除主从参考时钟连接对 B 扫描的影响，测试阶段 A/B 均固定使用 Internal 100 MHz；A 保持 RefOut ON，B 保持 RefOut OFF。
- 该配置必须同时作用于 ETH pair 连接事务和每次 TX Apply request。只修改连接事务会被 TxSessionService 在后续 CW/Sweep configuration 中重新覆盖。
- 运行日志验收：B 的连接配置和每次 requestApply 均应显示 Internal / RefClockSource=0；本配置是临时诊断拓扑，实机结论明确后再统一恢复 A Internal + B External。

Sweep 共享编辑器角色切换修正（2026-08-11）：

- 实机日志已确认：A Sweep 开启后首次点击 B Sweep，设备侧先收到 `role=B, carrier=Fixed`，但共享 `StepSweepPanel` 随后错误显示为开启；再次手动切换开关才收到 `role=B, carrier=FScan`。因此这是 UI authoring state 串写，不是 B 设备或 H2 Sweep API 失败。
- 根因是恢复 B profile 时 `restoreAnalogProfile()` 会同步发出 `stateChanged`；此时共享 Sweep 开关仍保留 A 的开启状态，现有 capture slot 没有遵守 `m_suspendingPipelineUpdates`，于是把 A 的开关值写入了 B 的 `m_roleSweepEnabled`。
- 修正边界：所有从共享 Sweep UI 捕获 selectedRole 快照的入口，在 `m_suspendingPipelineUpdates` 为 true 时必须直接返回。恢复流程只能装载目标 role，不能把恢复过程中的中间态回写到任何 workspace；用户真实修改仍照常 capture 并刷新目标 role。
- 已按上述边界在 `captureSelectedSweepAuthoringState()` 增加恢复期门禁。`build/cmake-win-debug` 的 Core Debug 构建通过；QtCreator Debug 树也已完成编译和链接，但运行中的 SGStudio 占用部署目录 `plugin/Core.dll`，因此最后的 copy_if_different 被拒绝，停止程序后重新构建即可部署本修正。

#### Phase 2B：carrier/baseband、Operator 与 active 语义

在 Phase 2A 实机通过后，再继续下方原 Phase 2 步骤中尚未覆盖的 carrier plan、Playback、Streaming/legacy Operator、per-role active business、GNSS aggregate 门禁和完整 role-aware 信号迁移。Phase 2A 的轻量 common state 将在 Phase 3 并入完整 `DeviceWorkspaceStore`，不保留第二套永久状态体系。

##### Phase 2B-Playback：双设备 Carrier + Playback（2026-08-12 实施切片）

本切片先贯通 `Playback`（Arb 普通 WAV/IQS WAV）与 `Quick Waveform`，允许 A/B 分别运行 Fixed/FScan/LScan Playback。ProgrammedArb、其他 modulation、Streaming、GNSS aggregate 门禁仍留在后续切片。

实现边界与真值所有权：

1. 单套业务 UI/PropertyManager 仍只是 `selectedRole` 的编辑器。MainWindow 为 A/B 各保存 selected business、各业务 profile 与 `TxProviderExecutionContext`；Property 不得成为另一 role 的运行时真值。
2. TxSessionService 为每个 role 保存独立 baseband 快照，并按 `RF + role carrier + role baseband` 解析 Mute、FixedCw、SweepCw、FixedPlayback、SweepPlayback。进入 DeviceIoWorker 前，request 必须已经固化 role/endpoint、carrier 和 payload identity。
3. A/B 的 `PlaybackPayload` 必须是两份可独立持有的不可变共享句柄。下载成功后不再主动释放 host storage；两个 TxPipelineExecutor 各自维护对应设备的 resident identity，并在 center/level/carrier 未换 payload 时复用设备内波形。
4. 原“同一时刻只允许一份扩展大波形”的 reservation 改为定制版最多两份。替换某一 role 波形时，应先让该 role workspace 放弃旧 payload，再生成新 payload，不能误释放另一 role 的共享 storage。
5. 切换 A/B 时先保存旧 role profile，再恢复目标 role profile和业务使能显示；恢复过程不得触发 capture/Apply。参数由用户真实修改后，ArbPlayback/QuickWaveform 必须先发布 data-not-ready 快照使该 role 的旧 payload 失效，再异步生成新 identity，完成后只刷新该 role。
6. `Playback`/`Quick Waveform` 即使使用同一个业务对象，也不能把当前业务对象中的 payload 当作 A/B 仓库；运行中的另一 role 必须继续由其 workspace/context 持有原 payload。
7. 右侧 business list 顶部增加一个跨整行的 `SwitchButton`，文案为 A/B，默认 A。它只是 `selectedRole` 编辑视图选择器，不具有使能语义：当前侧显示 SwitchButton 的绿色选中底色，另一侧只以遮挡/暗色表示其 list 与业务编辑页当前被隐藏。双列布局时横跨两列，窄成单列时自然占一列宽。CommonPanel 的 RF/Center/Level/Sweep/通用设置交互也必须反向同步该选择器，并刷新目标 role 的 business 使能状态、当前业务页 profile 与 sweep 编辑状态；纯切换不得下发 H2 调用。

最小验收：

- A=FScan Playback、B=LScan Playback 可同时下发，日志分别出现 `role=A endpoint=5000 pipeline=SweepPlayback` 和 `role=B endpoint=5001 pipeline=SweepPlayback`，payload identity 可以不同。
- A/B 可选择不同的 Playback 业务或其中一路关闭 baseband；切换编辑角色不改变另一设备输出，也不把另一 role 的 enable/profile/payload 写入当前 role。
- ArbPlayback 修改 SampleRate/AutoScale/IQScale/SampleOffset/SamplesToUse/Period，QuickWaveform 修改对应字段或文件后，目标 role 先失效旧 payload，生成完成后以新的 payload identity 重新下载并启动；另一 role 的 identity 与输出保持不变。
- 同一 payload 下只修改 center/level 或 FScan/LScan 参数时复用该 role 的设备 resident 波形，不重复释放 host buffer；关闭/重开产品或 Preset 后两个 role 的缓存均正确失效。
- 保持 125 MiB/role 能力上限，允许 A/B 合计两份 125 MiB 波形常驻；任何第三份扩展大波形生成必须明确失败，不能静默挤掉另一 role。
- 点击右侧 A/B 选择器或任一 CommonPanel 的 RF/Center/Level/Sweep 后，选择器、右侧 list 使能状态、业务编辑页参数和 sweep 参数均显示对应 role；A 与 B 都使用相同的绿色当前态视觉，不把 B 画成“关闭”。

实施状态（2026-08-12）：

- 已实现 A/B 独立 selected business/profile/provider snapshot，Fixed/FScan/LScan 均可解析到 per-role Playback request；两个 executor 按 role 保留各自 resident payload identity，ETH 双设备不再在同步下载后释放 host payload。
- 已实现 Arb 普通 WAV/IQS WAV 与 Quick Waveform 的 editor-only profile 恢复。切换 role 或业务页只装载 Property/UI，不把 Property 当成后台设备真值，也不因恢复动作主动重新生成；真实参数编辑仍先发布 not-ready，再生成新 payload。
- 已实现右侧 A/B SwitchButton：双列时横跨两列、单列时随 dock 收窄；A/B 采用相同的正向主题色，暗侧只代表该工作区被遮挡。CommonPanel 交互与选择器共用 `selectCommonOutputRole()`。
- 已限制双设备下仅 A/B role 当前持有的 Playback business 保留生成结果，避免浏览或配置未使能业务时形成额外常驻波形。
- Debug 窄目标构建 `HTRA`、`QuickWaveform`（包含 Controls/Core 依赖）通过。现有 C4819 与 `checked_array_iterator` 告警不由本切片引入；实机双波形下载与持续扫描仍待运行验收。

实机 ArbPlayback 首轮修正（2026-08-12）：

- 运行日志证明 B 已收到 `FixedPlayback`，完成独立波形下载、`TX_PLAYBACK` stream 配置、RF/MOD 输出配置、`channel_start` 和 bus trigger；关闭 B Playback 后也已收到 `FixedCw`。因此共享 `Mod` Property 不是双 ETH 设备下发真值，不能用 CommonPanel 是否点亮反推 H2 是否调用成功。
- 已确认的 UI 根因是定制版 `updateSweepAndBusinessAvailability()` 每次都把两个 MOD 按钮强制禁用并清成 OFF。修正后两个 role-aware MOD 按钮由各自 workspace 的 selected Playback business 直接驱动；PropertyManager 仍只服务单套编辑器，不保存 A/B MOD 真值。MOD 按钮作为只读状态/role 选择入口，不允许点击后只改共享 `Mod` Property 而制造假状态。
- 日志中的 H2 调用目前只记录“返回码按成功处理”，没有回读 `tx_query_output()`，不足以区分 SDK 真正生效与静默未生效。FancyDevice 需要在 RF/MOD 配置、启动和 bus trigger 后记录目标 endpoint、请求值、每步原始返回码以及输出回读值。
- 旧实现直接在正在运行的 Playback channel 上改配 CW，示例只覆盖“配置停止/新打开的 channel -> output -> start -> trigger”，没有覆盖运行中跨模式改配。为保证 B 关闭 Playback 后可靠恢复直流，target role 在 `Playback <-> CW/Mute` 或装载新 Playback 波形前先执行 `channel_stop()`，然后按新模式完整重配；只停止目标 FancyDevice，不影响另一 role。

本轮验收：A/B 分别启用 Playback 时对应 CommonPanel MOD 独立显示 ON，关闭后显示 OFF；日志的 `[H2OutputState]` 对 Playback 为 `requested RF=ON MOD=ON`、对 CW 为 `requested RF=ON MOD=OFF`，query 回读一致；B 执行 Playback -> CW 后必须重新出现固定载波，且 A 输出不中断。

实施结果：role-aware CommonPanel MOD 已改为外部状态驱动，点击只切换 selectedRole，不再写共享 `Mod` Property；MainWindow 按 A/B workspace 的 Playback 选择分别刷新 MOD。FancyDevice 已增加目标端口级 `[H2ModeTransition]`/`[H2OutputState]` 日志，并在跨模式或装载新 Playback 波形前停止目标 channel。`SGS_PRODUCT_ETH_DUAL=ON` 的 Windows x86_64 Debug `HTRA` 窄目标构建通过（包含 Core）；仅有仓库既有的 MSVC `checked_array_iterator` 弃用告警。实机 RF/MOD 回读与 B Playback -> CW 恢复仍需下一轮运行日志确认。

实机 ArbPlayback 回归记录（2026-08-12 11:36:31–11:44:29）：

- A（5000）和 B（5001）均独立打开成功，UID 分别为 `3863545562900345909`、`4514034234078671925`。退出时先 B 后 A 均 `close success`，没有残留单边连接。
- A 以 `payloadId=18`、131072 点、4 MSps 进入 FScan Playback；B 以 `payloadId=19`、500000 点、50 MSps 进入 LScan Playback。两份 payload identity、采样率和点数互不相同，证明本轮已经走通两份独立 host payload 到两个 endpoint 的下载链路。
- A/B Playback 的 `[H2OutputState]` 均为 `requestedRf=ON requestedMod=ON`，且 `queryStatus=0 queriedRf=ON queriedMod=ON`；所有 output config、`channel_start` 和 bus trigger 原始返回码均为 0。A 修改 FScan dwell 后出现 `Reuse device-resident Playback waveform: payloadId=18`，没有重复下载该波形。
- 本轮所有 `[TxApplyQueue] completion` 都是 `success=yes`；两条 `accepted=no` 均发生在旧 generation 已被更新请求覆盖之后，并伴随 `completion writeback dropped`，属于 latest-wins 队列的预期行为，不是设备失败。日志中未见 H2 error、参数越界、timeout 或 underrun。
- Save & Load 的禁用提示符合本方案当前边界；窗口 geometry、`TriggerSource` display option 和空文件名警告没有对应 H2 失败，不影响本轮双 Playback 结论，但后两项仍应在进入完整 UI 验收前清理或确认来源。

尚未闭环：

- 当前日志没有出现 Playback 之后的 `requestedMod=OFF/queriedMod=OFF`，因此没有覆盖 `B Playback -> SweepCw/FixedCw/Mute` 的恢复验收；不能仅凭本轮日志把该项标记为通过。
- 11:43:25 出现 `role=B business="Playback" ready=no payloadId=0`，随后 5001 执行 `PlayFromRam -> PlayFromRam`、`channelStopStatus=0` 并停在 `Phase 1 awaiting waveform`；同一时段 A 也先发布 `payloadId=0`，再生成并下载新的 `payloadId=20`。日志无法单独证明当时用户动作，但该序列与“纯 A/B 切换不得发布 not-ready、不得触发 H2、不得改变对侧输出”的验收要求冲突。如果 11:43:25 仅是 B -> A selector/CommonPanel 切换，则这是明确缺陷，Phase 2B 不能视为完整通过。
- 下一轮定向测试应让 A/B 同时稳定播放后连续切换 selectedRole：预期两侧 payloadId 始终保持 `18/19`（或本轮新建的对应固定值），日志中不得出现新的 `setRoleBasebandProvider(... ready=no)`、`requestApply`、`H2ModeTransition`、波形下载或 output 配置。随后单独关闭 B Playback，必须看到 B 回退到对应 carrier-only pipeline，并回读 `RF=ON/MOD=OFF`，A 全程不产生 H2 调用。

本轮结论：双 endpoint 的 ArbPlayback 下载、FScan/LScan 运行、RF/MOD 生效和关闭产品已经初步通过；Phase 2B 仍保留“role 切换无设备副作用”和“关闭 Playback 回退 carrier-only”两个阻断完整验收的问题。Quick Waveform、两份接近 125 MiB 的大波形、第三份 reservation 拒绝和 Preset 后双 resident cache 失效也尚未实测。

主要文件：

- [switchbutton.h](../../src/libs/controls/switchbutton.h)
- [switchbutton.cpp](../../src/libs/controls/switchbutton.cpp)
- [fancytabwidget.h](../../src/plugins/core/fancytabwidget.h)
- [fancytabwidget.cpp](../../src/plugins/core/fancytabwidget.cpp)
- [playbackpayload.h](../../src/plugins/core/playbackpayload.h)
- [playbackpayload.cpp](../../src/plugins/core/playbackpayload.cpp)
- [txpipelinestate.h](../../src/plugins/core/txpipelinestate.h)
- [txapplytypes.h](../../src/plugins/core/txapplytypes.h)
- [txsessionservice.h](../../src/plugins/core/txsessionservice.h)
- [txsessionservice.cpp](../../src/plugins/core/txsessionservice.cpp)
- [txpipelineruntime.h](../../src/plugins/core/txpipelineruntime.h)
- [txpipelineruntime.cpp](../../src/plugins/core/txpipelineruntime.cpp)
- [txpipelineexecutor.h](../../src/plugins/core/txpipelineexecutor.h)
- [txpipelineexecutor.cpp](../../src/plugins/core/txpipelineexecutor.cpp)
- [deviceoperator.h](../../src/plugins/core/deviceoperator.h)
- [deviceoperator.cpp](../../src/plugins/core/deviceoperator.cpp)
- [ibusiness.h](../../src/plugins/core/ibusiness.h)
- [ibusiness.cpp](../../src/plugins/core/ibusiness.cpp)
- [businessmanager.h](../../src/plugins/core/businessmanager.h)
- [businessmanager_p.h](../../src/plugins/core/businessmanager_p.h)
- [businessmanager.cpp](../../src/plugins/core/businessmanager.cpp)
- [txorchestrator.h](../../src/plugins/core/txorchestrator.h)
- [txorchestrator.cpp](../../src/plugins/core/txorchestrator.cpp)
- [coreruntimeservices.cpp](../../src/plugins/core/coreruntimeservices.cpp)
- [deviceruntimebridge.cpp](../../src/plugins/core/deviceruntimebridge.cpp)
- [gpsdialog.cpp](../../src/plugins/gps/gpsdialog.cpp)
- [mainwindow.cpp](../../src/plugins/core/mainwindow.cpp)
- [mainwindowsettingscontroller.cpp](../../src/plugins/core/mainwindowsettingscontroller.cpp)
- [updatedialog.cpp](../../src/plugins/updater/updatedialog.cpp)
- [quickwaveformbusiness.cpp](../../src/plugins/quickwaveform/quickwaveformbusiness.cpp)
- [analogplaybackbusiness.cpp](../../src/plugins/analog/analogplaybackbusiness.cpp)

步骤：

1. request/job/result 增加 `DeviceTarget`，日志和 completion 全程保留原 target。
2. capability snapshot、device resolve 和 stale 校验改为按 `productEpoch + role`；UID/revision 只校验。
3. executor 接受 worker 已解析的 target device，不再读取全局/canonical device。
4. `DeviceIoWorker` 为每个 role 持有独立 executor；`TxSessionService` 为每个 role 持有独立 runtime 和 applied/pending/generation。`submitTxApply(job)` 直接使用 job 内唯一的 `DeviceTarget`，不再重复传一个可能冲突的 role；`invalidateTxExecution(role)` 只清目标侧，另提供 `invalidateAllTxExecution()` 给 close/preset/product switch。
5. `requestRefresh()`、selectedBusiness、carrierPlan、RMS/writeback/error 信号全部增加 role。
6. `BusinessManager` 将单一 `activedBusiness` 改为 per-role active runtime owner；同 role 互斥、跨 role 独立，并增加 `anyRoleStreaming()` aggregate。
7. `IBusiness` 增加 role-aware active 生命周期；`activedStateChanged(role, bool)` 和 `currentActivedBusinessChanged(role, current, prev)` 同步改签名，core-managed provider 不借此表示设备执行状态。
8. 按第 4.4.1 节逐个迁移 Manager 内部、CoreRuntimeServices、DeviceRuntimeBridge、GpsInfoDialog、MainWindow/重启/Updater、TxSessionService、Streaming、QuickWaveform 和 AnalogPlayback；整机动作遍历 roles，编辑动作只使用 selectedRole，共享门禁只用 aggregate。
9. 重构 `DeviceOperator` 为显式 target 工具并从 `IBusiness::m_operator` 移出；按 Phase 0 的 Mute/CW 审计结果迁移或移除最后两个 legacy 使用点。
10. 完成 A/B 的 Mute/CW/Fixed Playback/FScan/LScan/MScan 下发；每个 request 只影响指定 endpoint。

验收：

- 硬编码 role/request 可让 A/B 具有不同 center/level、carrier plan 和 Playback payload。
- 交替下发不清理对侧波形，不因相同 UID、UI role 变化或 job 排队串台。
- A role 的 legacy/core 切换不再调用 B role 的 terminate。
- GNSS 配置控件在任一 role 进入 Streaming active 后禁用；B-only Streaming 也必须命中。此阶段先验证 active aggregate，Phase 4 再把门禁覆盖到 engine Starting/Stopping barrier 和实际 H2 telemetry poll。
- 退出、重启、升级、reset 会停全部 role；窗口标题、trigger 控件和 waveform 生成只跟随 selectedRole。
- `OFF` 下 role 集合为 `{A}`，所有业务路径回归不变。

### Phase 3：A/B workspace 与无副作用 profile 切换

主要文件：

- [commondeviceprofile.h](../../src/plugins/core/commondeviceprofile.h)
- [commondeviceprofile.cpp](../../src/plugins/core/commondeviceprofile.cpp)
- [ibusiness.h](../../src/plugins/core/ibusiness.h)
- [businessmanager.h](../../src/plugins/core/businessmanager.h)
- [businessmanager.cpp](../../src/plugins/core/businessmanager.cpp)
- [stepsweeppanel.h](../../src/plugins/core/stepsweeppanel.h)
- [stepsweeppanel.cpp](../../src/plugins/core/stepsweeppanel.cpp)
- [runtimeprofilepersistence.cpp](../../src/plugins/core/runtimeprofilepersistence.cpp)
- [txsessionservice.cpp](../../src/plugins/core/txsessionservice.cpp)
- [fancydevice.cpp](../../src/plugins/htra/fancydevice.cpp)

步骤：

1. 新增 `DeviceWorkspaceStore`，按 `activeProduct()->roles()` 创建 workspace；USB 只有 A，ETH pair 固定 A/B。
2. 把 common、carrier/sweep/list、每业务 widget profile、selectedBusiness/currentPage、两份 PlaybackPayload 纳入 workspace。
3. 实现 suspend -> save old -> change selectedRole -> restore new -> capability reconcile -> resume 的事务。
4. restore 期间屏蔽 property editing、自动 MOD、provider context 和 refresh；纯查看不下发。
5. apply/writeback、RMS、UNLEVEL、warning/error、playback identity 按 target 回写，即使 UI 已切到另一 role。
6. 两份 PlaybackPayload 常驻内存，不因切换释放；不做树莓派容量降级。
7. 运行配置持久化拆为 `shared + outputs.A + outputs.B` 可后置，不阻塞第一轮双设备硬件验证；但内存 workspace 必须在本阶段完成，且定制版 Save & Load 维持禁用。
8. 增加 Debug-only 的 H2 **控制面**调用审计计数器（按 role/操作族统计 configuration、sweep、query、preset；不把未来 Streaming data send 混入），专用于证明 role 切换没有产生设备 I/O。计数器只做测试观测，不进入产品逻辑。

验收：

- 程序化切换 A/B 十次，所有表单值和 selectedBusiness 各自保持。
- 切换前后断言 A/B H2 control-call counters、Tx runtime generation、pending job 数、PlaybackPayload identity/引用数和 resident waveform identity 完全不变；不是仅凭日志肉眼判断“似乎没下发”。
- 纯切换不生成/释放 payload、不改变任一设备输出；恢复 profile 引发的 property signal 全部被 suspend guard 吸收。
- `OFF` 下 selectedRole 恒为 A，save/restore 退化为空操作。

### Phase 4：抽取双 `StreamingEngine`，补首个实机测试入口

主要文件：

- [streamingbussiness.h](../../src/plugins/htra/streamingbussiness.h)
- [streamingbussiness.cpp](../../src/plugins/htra/streamingbussiness.cpp)
- [streamingdatagenerator.h](../../src/plugins/htra/streamingdatagenerator.h)
- [streamingdatagenerator.cpp](../../src/plugins/htra/streamingdatagenerator.cpp)
- [deviceoperator.h](../../src/plugins/core/deviceoperator.h)
- [deviceoperator.cpp](../../src/plugins/core/deviceoperator.cpp)
- [fancydevice.h](../../src/plugins/htra/fancydevice.h)
- [fancydevice.cpp](../../src/plugins/htra/fancydevice.cpp)

步骤：

1. 从 `StreamingBussiness` 抽取无 UI 的 `StreamingEngine`，把 generator、DataSender、sender thread、operator、settings、carrier、progress、fault 都移入 engine。
2. `StreamingBussiness` 保持单个注册对象和 panel，持有 A/B engine，并按 selectedRole 做 profile save/restore 与 Apply 路由。
3. progress/fault/profileChanged 信号增加 role；UI 只显示 selectedRole progress，fault 不受 selectedRole 过滤。
4. streaming 配置/sweep arm 进入 DeviceIoWorker 控制面；`tx_send_stream()` 保持在每个 engine 的 sender thread。
5. stop/close/preset 建立 barrier，保证产品释放 endpoint 前两条 sender 都离开设备调用区。
6. 不增加跨 A/B 全局锁；保留两个 FancyDevice 实例锁。为 H2 并发调用、队列深度、send duration、underrun、fault 增加 role 日志。
7. 在阶段末统一增加仅测试用入口，例如 `--eth-pair=<ip>` 和 per-role 固定 profile；它绕开尚未完成的 UI，但走正式 `DeviceProduct/Workspace/Pipeline/StreamingEngine` 链路。
8. 以两份 engine 生命周期驱动 `anyRoleStreaming()`：第一路进入 Starting 前关闭整机非流遥测，最后一路完成 Stopping barrier 后才释放门禁；不能在 `setActive(false)` 刚发出时提前恢复查询。
9. 拆分 data-plane status 与 non-stream telemetry：流中继续更新每 role throughput/error/fault，但 A/B 的 temperature、power supply、GNSS、feature-spec H2 调用计数必须保持为零。
10. GpsInfoDialog 在门禁期间禁用 config，`refreshGnssConfig()/applyGnssConfig()` 自身也直接拒绝；暂停期间显示最后有效 GNSS + `Paused (Streaming)`。

这是第一个双设备实机验证点。前 0–3 阶段只做静态/构建/USB 回归会延迟发现 H2 语义问题，因此本阶段必须一次验证完以下场景：

1. 输入测试参数后 5000/5001 全成功 open，任一失败都回滚。
2. A/B 分别使用不同 CW 参数并保持。
3. A/B 分别使用不同 Playback payload，切换/重下发不清对侧 resident waveform。
4. A/B 同时各跑 7.8 MSps complex16 Streaming 10 分钟；两条 sender 均持续推进，无 underrun、队列饥饿、死锁或互相停流。
5. 双流期间只重配 A，B sender 不停止；只停止 B，A 继续。
6. 流中拔掉任一 endpoint，两个 engine 均停止并进入整机断开，不出现 use-after-free 或后台单边重连。
7. 仅 B Streaming、仅 A Streaming、A+B Streaming 三种情况下，A/B 非流遥测 H2 counter 都不增长；GNSS dialog 打开/关闭、切换 selector、触发 `gnssSettingsApplied` 也不能绕过门禁。
8. 停止第一路但另一侧仍在流时门禁保持；最后一路 stop barrier 完成后，下一次 status timer 才恢复查询和 GNSS 配置控件。
9. 单路 Streaming 时拔掉空闲 endpoint，验证不会伪造即时断线；在该 endpoint 下一次 control request 或最后一路停止后进入整机断开，并输出“telemetry paused, disconnect detection delayed”诊断日志。

如果双流失败，先用日志区分 H2 多 handle 并发、主机 CPU/queue、网卡或上层锁竞争；不得直接用全局互斥“修复”，因为那会把数据面串行化并掩盖根因。

### Phase 5：IP-only ETH UI、双 CommonPanel 与 A/B selector

主要文件：

- [ethconnectdialog.h](../../src/plugins/core/ethconnectdialog.h)
- [ethconnectdialog.cpp](../../src/plugins/core/ethconnectdialog.cpp)
- [mainwindowdevicecontroller.h](../../src/plugins/core/mainwindowdevicecontroller.h)
- [mainwindowdevicecontroller.cpp](../../src/plugins/core/mainwindowdevicecontroller.cpp)
- [commonpanel.h](../../src/plugins/core/commonpanel.h)
- [commonpanel.cpp](../../src/plugins/core/commonpanel.cpp)
- [mainwindow.h](../../src/plugins/core/mainwindow.h)
- [mainwindow.cpp](../../src/plugins/core/mainwindow.cpp)
- [fancytabwidget.h](../../src/plugins/core/fancytabwidget.h)
- [fancytabwidget.cpp](../../src/plugins/core/fancytabwidget.cpp)
- [switchbutton.h](../../src/libs/controls/switchbutton.h)
- [switchbutton.cpp](../../src/libs/controls/switchbutton.cpp)
- [businessmanager.h](../../src/plugins/core/businessmanager.h)
- [businessmanager.cpp](../../src/plugins/core/businessmanager.cpp)
- [txsessionservice.h](../../src/plugins/core/txsessionservice.h)
- [txsessionservice.cpp](../../src/plugins/core/txsessionservice.cpp)
- [coreruntimeservices.cpp](../../src/plugins/core/coreruntimeservices.cpp)
- [remoteminibarservice.cpp](../../src/plugins/core/remoteminibarservice.cpp)

步骤：

1. EthConnectDialog 删除 port 输入，Connect 固定创建 5000/5001 product；显示 A/B 两步进度和精确失败 endpoint。
2. `SGS_PRODUCT_ETH_DUAL=ON` 隐藏/禁用 USB discovery、自动连接和 USB 选择；`OFF` 不改变标准版 UI。
3. MainWindow 最小宽度设为 1280，顶部改为 A/B CommonPanel 垂直容器，两块恒为 Wide。
4. CommonPanel 改为 role-aware view；点击可编辑项统一先调用 `selectRole(role)`。
5. FancyTabWidget 改为固定顶部 selector + 下方业务 list；定制版右栏固定 205px 双列。
6. SwitchButton 支持可配置 A/B 文案；对外只发 `DeviceRole`，默认 A。
7. 定制版关闭 CommonPanel compact、status compact 和右栏单列切换；不删除标准版原实现。
8. selector、两块 CommonPanel 高亮和中央编辑 profile 保持单向状态源，避免递归触发 Apply。
9. MainWindow 的 active-business 标题/trigger 更新只响应 selectedRole；另一 role 的 Streaming start/stop 不抢中央 UI。
10. 定制版明确隐藏/禁用 Remote Minibar 和 Save & Load，并给出“尚未支持 A/B workspace”的可见原因；不发送 canonical A 命令作为 fallback。

验收：

- 1280px 下两行 CommonPanel 字段完整，右栏 selector 占满两列宽。
- A/B 显示不同值；点击 B 字段会先选 B，切 selector 不改变 A 的输出或 profile。
- A/B 双 Streaming 同时运行时，切换 panel 只改变显示的 settings/progress。
- ETH pair 任一 open 失败，对话框保留并显示具体端口；主界面不出现半连接状态。
- B-only Streaming 时 GNSS 控件与非流遥测仍处于暂停；A/B selector 不影响该整机门禁。
- 定制版无法从 Minibar 或 Save & Load 绕过 role/workspace 语义。

### Phase 6：统一 DeviceSetting、元数据和 canonical 服务

主要文件：

- [deviceinfowidget.h](../../src/plugins/core/deviceinfowidget.h)
- [deviceinfowidget.cpp](../../src/plugins/core/deviceinfowidget.cpp)
- [devicesettingdialog.cpp](../../src/plugins/core/devicesettingdialog.cpp)
- [commondeviceprofile.cpp](../../src/plugins/core/commondeviceprofile.cpp)
- [gpsdialog.h](../../src/plugins/gps/gpsdialog.h)
- [gpsdialog.cpp](../../src/plugins/gps/gpsdialog.cpp)
- [aboutdialog.h](../../src/plugins/core/aboutdialog.h)
- [aboutdialog.cpp](../../src/plugins/core/aboutdialog.cpp)
- [updatedialog.cpp](../../src/plugins/updater/updatedialog.cpp)
- [deviceruntimebridge.cpp](../../src/plugins/core/deviceruntimebridge.cpp)
- [analogmodulationplugin.cpp](../../src/plugins/analog/analogmodulationplugin.cpp)
- [deviceutils.cpp](../../src/plugins/analog/deviceutils.cpp)
- [businessmanager.h](../../src/plugins/core/businessmanager.h)
- [deviceruntimebridge.h](../../src/plugins/core/deviceruntimebridge.h)
- [devicemanager.h](../../src/plugins/core/devicemanager.h)
- [devicemanager.cpp](../../src/plugins/core/devicemanager.cpp)
- [fancydevice.h](../../src/plugins/htra/fancydevice.h)
- [fancydevice.cpp](../../src/plugins/htra/fancydevice.cpp)

步骤：

1. 新增产品设置 coordinator；DeviceSettingPanel 不再直接抓裸/canonical device。
2. Reference/LO/RF Port/Trigger In/System Clock/Low Power/Fan 按同值 A 后 B 下发并分别记录结果；统一输入范围取能力交集。
3. Preset/Reset 遍历产品全部 roles；Trigger Out/GPIO 继续隐藏。
4. GNSS query/config 固定 canonical A，保留一个 dialog；所有读写同时受 `anyRoleStreaming()` 门禁，不能只禁用控件。
5. 状态栏拼接两份短 UID 和两份温度；复制信息保留完整 UID。任一路 Streaming 时两份温度冻结为 last-known + `Paused (Streaming)`，GNSS 同样保留最后快照。
6. About 的 UID64/UID32 拼接 A/B，Model/HCD/MCU/FPGA/BUS/EIO/API 只显示 A；A/B 不一致写日志。
7. Updater UI/参数继续使用 A；整机升级由外部 updater 保证。
8. DeviceRuntimeBridge 的 canonical `deviceConnected` 只发布 A，许可证只校验 A；B metadata 走 role-aware 状态链路。

验收：

- DeviceSetting 永远只有一份；双发部分成功能显示 A/B 明细，不伪造回滚。
- Preset 会作用于 A/B；GNSS 永远只走 A；A/B selector 不改变设置页面。
- selector 切换不改变许可证；状态栏同时显示两台 UID/温度；About 只有双 UID、没有两套固件栏；Updater/GNSS 仍单份。
- A-only、B-only、A+B Streaming 都会关闭整机 GNSS/温度/供电/feature-spec H2 实时查询；最后一路完整停止后才恢复。

### Phase 7：Debug build/run 与实机验收

建议按以下顺序验收：

1. `SGS_PRODUCT_ETH_DUAL=OFF` Windows x86_64 Debug 全量构建并回归 USB 单设备。
2. `SGS_PRODUCT_ETH_DUAL=ON` Windows x86_64 Debug 全量构建，确认 USB 发现/自动连接/UI 已隐藏禁用。
3. 正常 IP：5000/5001 均 open，日志有 A/B metadata、原始 capability、epoch/role；产品发布的 A/B 能力内容一致，Playback 最高 125 MSps、Streaming 最高 62.5 MSps、波形内存 125 MiB。
4. 分别屏蔽 5000、5001：每次连接都失败并释放已开侧，主界面没有半连接状态或后台 retry。
5. A/B 分别 CW，设置不同 center/level/RF/MOD，确认互不覆盖。
6. A/B 分别 Playback，使用不同业务/profile/IQ，确认两个 payload、waveform cache 和 resident ID 独立。
7. A/B 分别 FScan/LScan/MScan，确认 UI 使用同一份固定产品 capability，实际下发的 endpoint/role 正确。
8. A/B 同时 Streaming 7.8 MSps complex16 10 分钟；观察 send duration、queue、throughput、CPU、内存和 underrun。
9. 双流中单独修改 A sample rate/profile/carrier，B 连续；再对 B 做同样验证。
10. A/B 分别组合 Playback + Streaming，切换 selector 二十次，确认离开侧不 mute、不重复下载、不停流。
11. 运行中分别断开 5000/5001，确认整机断开、两 engine 退出且无悬挂 H2 调用。
12. DeviceSetting/Preset、状态栏/About、许可证/GNSS/Updater 按本方案检查 Broadcast/canonical/双份规则。
13. 分别运行 A-only、B-only、A+B Streaming：GNSS dialog 全程禁用读写，A/B 非流遥测 H2 counter 为零；停止一侧但另一侧仍在流时不得提前恢复；单流时空闲侧断线按第 4.7 节验证延迟发现语义。
14. 单个 SGStudio 进程连接成功后再次打开/调用 ETH Connect 会被拒绝；本轮不承诺两个进程之间的 ETH pair 排他，若同时连接由 H2 server/socket 行为决定。
15. Remote Minibar 和 Save & Load 在 role-aware 协议完成前保持明确禁用，不能静默操作 A；若本轮实现第 11.3 节，则按其完整兼容矩阵验收。

阶段门禁：Phase 4 的无 UI 双流 10 分钟未通过前，不进入 Phase 5 UI 合并；Phase 7 未通过前，不删除测试入口和 role 诊断日志。

## 10. 推荐的最小首个可交付切片

首个切片应是“无正式 UI 的完整底层竖切”，即 Phase 0–4，而不是先复制 CommonPanel：

```text
SGS_PRODUCT_ETH_DUAL
  -> DeviceProduct{A,B} 全成功生命周期
  -> DeviceTarget + per-role runtime/executor
  -> 两份 workspace / PlaybackPayload
  -> 一个 StreamingBussiness + 两个 StreamingEngine
  -> --eth-pair=<ip> 测试入口
```

最小验收必须同时包含：

1. 5000/5001 全成功 open 与失败 rollback。
2. A/B 不同 CW。
3. A/B 不同 Playback payload/resident waveform。
4. A/B 两路 7.8 MSps Streaming 10 分钟。
5. 单侧重配不打断对侧，单侧断线触发整机安全退出。

这个切片验证的是最难返工的设备所有权、异步路由和双数据面。通过后，Phase 5–6 只是把已验证能力接入 IP-only dialog、双 CommonPanel、selector 和元数据展示；若先做 UI，双 Streaming 的结构性问题只会被更晚发现。

## 11. 已冻结决策、待实机确认项与后期待办

### 11.1 已冻结，不再作为实现问题

1. 这不是 H2 双通道；是两个 endpoint、两个 handle、两个 FancyDevice，各用本地 `channels[0]`。
2. 初次连接任一失败即整体失败；运行中任一断开也整体断开。
3. 定制版只交付 ETH pair；USB discovery、自动连接和选择 UI 隐藏禁用。
4. 两路独立 Streaming 是第一版必须能力；不做速率协商/自动降级。
5. 点击 CommonPanel A/B 会联动 selectedRole/右侧 selector。
6. 主窗口最小宽度 1280，双 CommonPanel 恒 Wide，移除定制版 Compact。
7. Updater 由外部程序保证整机升级；SGStudio 只传 canonical A endpoint。
8. GNSS/固件/版本/许可证使用 A；UID/温度显示 A+B；Preset 遍历 A+B。
9. DeviceSetting 保持一份；未另行声明的 endpoint 设置第一版按相同值 A 后 B 下发。
10. 任一路 Streaming 时暂停整机 GNSS/温度/供电/feature-spec 等非流 H2 实时查询和 GNSS 配置；最后一路完成停止后才恢复。
11. 本定制版不扩展 `InstanceStateRegistry`；只保证单进程同一时刻最多一个 connecting/active ETH pair，不提供跨进程 ETH pair lease。
12. Remote Minibar 与 Save & Load 在完成 role-aware 协议前于定制版禁用，不使用隐蔽 canonical A fallback。

### 11.2 必须用日志/实机确认，但不阻塞架构

1. **H2 DLL 双 handle 并发安全**：两个 `tx_send_stream()` 是否可真正并行；收集每 role 调用耗时、错误码和 underrun，禁止先加全局锁。
2. **产品级遥测门禁完整性**：策略已确定为流中不查；实测只确认 A-only、B-only、双流和交错停止期间所有非流 H2 counter 为零，且 data-plane throughput/fault 仍更新，不再评估“是否可以偷偷恢复某个查询”。
3. **DeviceSetting 字段归属**：System Clock Out、Low Power、Fan 第一版按 A/B 同值广播；若日志/硬件证明某项是机箱共享，只把该字段策略切到 canonical A。
4. **7.8 MSps 双流余量**：62.4 MB/s 是 payload 估算，不等同于保证整条链路；用十分钟实测确认协议开销、CPU、队列和网卡余量。
5. **版本一致性**：A/B metadata 不一致只告警；需由部署/外部 updater 决定是否阻止使用。
6. **多进程同 IP 行为**：本轮没有应用层 lease。若部署环境允许同时启动两个定制版进程，需要实测 H2 server 对第二组 5000/5001 socket 的拒绝和恢复语义；结果只作为后续是否引入跨进程门禁的依据，不改变当前单进程产品模型。
7. **空闲侧断线发现延迟**：任一路 Streaming 时没有非流 health query；空闲侧拔线只能延迟到下一次 control request 或全部流停止后发现。第一版接受此限制；若要求即时发现，需要供应商新增不干扰流的 API，而不是恢复温度/GNSS轮询或自建端口探针。
8. **Playback workspace 跨 role 驻留**：A/B 同时播放后连续切换 selectedRole，必须保持两侧 provider snapshot 和 resident payload identity 不变，且 H2 调用计数为零。2026-08-12 首轮日志在疑似 B -> A 切换处出现 B `ready=no/payloadId=0`、B channel stop 和 A payload 重新生成，需结合明确操作步骤复验并修正。
9. **Playback 关闭回退**：分别覆盖 Fixed、FScan、LScan 下从 Playback 关闭 baseband，确认目标 role 回退到 FixedCw/SweepCw，回读 `RF=ON/MOD=OFF`，对侧没有 H2 调用；RF 本身关闭时则回退 Mute。本轮日志尚未覆盖 Playback 后的 MOD OFF 回读。

### 11.3 本轮架构完成后再处理

- **Remote Minibar role-aware 协议**：定制版在完成前保持禁用。后续协议必须显式携带 `selectedRole`，snapshot 至少提供 A/B 的 RF、center、level、active business、apply revision/error 摘要；写命令必须带 target role 和该 role 的 revision，不能读取主窗口 property 后猜目标。Minibar 选择 role 与主窗口 selector 双向同步，协议版本必须升级，旧 helper 不得连接后默认控制 A。当前单设备 snapshot/command 都直接使用 current device 与全局 property：[remoteminibarservice.cpp:451](../../src/plugins/core/remoteminibarservice.cpp#L451)、[remoteminibarservice.cpp:910](../../src/plugins/core/remoteminibarservice.cpp#L910)、[remoteminibarservice.cpp:1033](../../src/plugins/core/remoteminibarservice.cpp#L1033)。
- **Save & Load format v5**：定制版在完成前保持禁用。目标结构固定为 `shared + outputs.A + outputs.B + selectedRole`；每个 output 保存 common、carrier/sweep/list、全部 business widget profiles、selectedBusiness/currentPage，不保存运行中线程/handle。保存前必须先 flush 当前编辑 role，再从两个 workspace 序列化，不能直接只读当前 PropertyManager。当前 v4 只有单份 `common/bussiness/sweep/activedBussiness`：[runtimeprofilepersistence.cpp:22](../../src/plugins/core/runtimeprofilepersistence.cpp#L22)、[runtimeprofilepersistence.cpp:140](../../src/plugins/core/runtimeprofilepersistence.cpp#L140)、[runtimeprofilepersistence.cpp:153](../../src/plugins/core/runtimeprofilepersistence.cpp#L153)。
- **v4 向后兼容规则**：旧单设备文件只导入 A；B 使用 factory default 且保持 Mute/RF Off，禁止把 A 自动克隆到 B 造成意外双路输出。加载过程先 suspend 两个 role、停止持续 runtime、解析/校验完整文件，再一次性替换 workspace；任一 output 解析失败则全部不提交。加载完成只恢复 authoring state，不自动恢复 Streaming sender。
- 单控制 worker 的队头阻塞如何在 UI 上表达。
- Trigger Out / GPIO 隐蔽入口恢复。
- 树莓派 CPU/内存优化、payload 容量优化或速率产品策略。

## 12. 设计评审结论

- 真正的底层边界是两个 ETH endpoint、两个 `FancyDevice`、两个 H2 device handle；`channels[0]` 只是每个单设备内部的固定 API 参数。
- `DeviceManager` 的正确抽象是 active `DeviceProduct`；USB 是 `{A}`，ETH pair 是 `{A,B}`。`canonicalDevice()` 只服务明确单份资源，不能作为业务路由 fallback。
- 实施顺序必须是调用点审计 -> 产品生命周期 -> 显式 target/per-role runtime -> workspace -> 双 StreamingEngine -> UI；最先复制 UI 会把单设备全局状态扩散得更深。
- 回放和 Streaming 的运行模型不同：回放依靠两份 payload/executor resident state，Streaming 必须有两套 generator/queue/sender/operator/执行状态。
- 控制面由一个 DeviceIoWorker 串行，数据面由 A/B sender 线程并行；实例级 FancyDevice 锁足够，未经实测不得增加跨设备全局锁。
- `currentDevice()` 与全局 `activedBusiness()` 是两套独立的单例陷阱，必须分别用强制改名/加 role 穷举迁移；GNSS、退出、waveform 生成等调用点不能遗漏。
- 任一路 Streaming 都触发产品级非流遥测门禁：A/B GNSS、温度、供电和 feature-spec H2 查询全部暂停，只保留数据面 throughput/error/fault；最后一路完整停止后恢复。
- 两块 CommonPanel 由两份 workspace 驱动，中央 property/business panel 只编辑 selectedRole；role 切换不产生设备 I/O，也不影响另一侧输出。
- A/B selector 只是编辑目标，默认 A；产品切换只由 `setActiveProduct()` 完成。
- A 是 canonical endpoint：许可证、GNSS、固件/About/Updater 单份信息走 A；UID、温度、错误和 runtime 明确展示 A/B 两份。
- 首个真实门禁是 Phase 4 的无 UI 双流实测；两路各 7.8 MSps 持续 10 分钟通过后，才进入固定 1280px 的 UI 合并。
