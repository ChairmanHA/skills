# H2 ETH 双设备 API 评估与 UI 交互设计问题

日期：2026-08-10  
状态：设计讨论稿，等待产品与底层 API 语义确认  
验证级别：static  
范围：只形成问题清单与推荐设计，不修改业务代码，不更新 KnowledgeBase

## 1. 已确认的真实硬件与软件模型

本设计不再把 H2 的 `channel[]` 理解为当前产品需要支持的真正多通道抽象。当前产品事实固定为：

- 多通道产品只存在于 ETH 形态，没有多通道 USB 产品。
- 一台树莓派通过同一个 IP (192.168.1.100) 暴露两个独立 ETH server：
  - UI `CH1` 固定映射端口 `5000`；
  - UI `CH2` 固定映射端口 `5001`。
- 每个 server 都独立执行一次 `device_open_eth(..., chs = 1, ...)`，得到一个独立 `device` handle 和一个可用 TX 通道。
- SGStudio 维护两个独立 `FancyDevice`，每个实例只使用自己返回的 `channels[0]` 和 `stream 0`。
- USB 设备也固定只使用 `channels[0]`；H2 API 表面上的 USB 多通道能力不进入本项目设计。
- 两个 ETH 射频设备没有共享 LO、相干相位和跨设备原子启动/Trigger 顺序保证；只能把已确认的公共频率参考能力称为“频率相干”，不能扩展宣传为相位相干。

供应商示例 [eth_2dev_cw.cpp](../../3rdParty/h2_api/eth_2dev_cw.cpp) 与上述模型一致：它使用相同 IP、端口 5000/5001、两个 `device`、两组 `channel`，分别完成配置、启动、BUS Trigger 和关闭。

推荐在项目中明确采用以下命名，避免再与 API 的 `channel.num` 混淆：

```text
ETH chassis / ETH device group（一个 IP）
├─ logical CH1 → endpoint IP:5000 → FancyDevice 0 → local channels[0]
└─ logical CH2 → endpoint IP:5001 → FancyDevice 1 → local channels[0]
```

`CH1/CH2` 是 SGStudio 的产品逻辑编号；两个 API endpoint 返回的 `channel.num` 即使都为 0 也完全合理。

## 2. 对当前 H2 API 的总体评价

### 2.1 结论

如果把 H2 API 定位为“控制一个 server endpoint 对应的一台射频设备”，它目前基本合理，能够支持双 ETH 设备分别配置和分别输出：

- `device_open_eth()`/`device_close()` 管理单个 endpoint 生命周期；
- `tx_config_ffm/fscan/lscan/mscan()` 能给两台设备设置不同 carrier plan；
- `tx_config_stream()` 能给两台设备设置不同 CW、Playback 或 Realtime baseband；
- `channel_config_trigger()`、`channel_start()`、`channel_bus_trigger()` 能分别控制两台设备；
- `tx_download_waveform()` 以 device handle 为作用域，天然形成两套独立波形驻留仓库。

API 的首要缺口是只有 `device_list_usb()`，没有 ETH 枚举或针对已知 IP 的 endpoint 探测接口。其次，双 ETH 部署所需的生命周期、共享资源、并发和故障隔离契约没有在 [h2_api.h](../../3rdParty/h2_api/include/h2_api.h) 中写清楚。当前 `chs`、`channel[]` 等形式反而容易让上层误以为一次 open 可以表示整台双路产品。

### 2.2 核心缺口：没有 ETH list/probe

USB 可以先通过 `device_list_usb()` 获得设备与 `device_info.chn`，再决定打开哪个对象；ETH 没有对等流程。当前上层只能：

1. 让用户输入一个 IP；
2. 根据产品外部约定自行拼出 `IP:5000` 和 `IP:5001`；
3. 直接调用两次 `device_open_eth()`；
4. 把 open 成功/失败同时当作“发现结果”和“生命周期结果”。

这会让“IP 不可达”“该端口没有设备”“server 正在启动”“设备存在但 open 初始化失败”“第二块板缺失”等情况都挤在同一条错误路径中，也使 SGStudio 必须自行维护供应商端口拓扑知识。

理想 API 不一定要做跨网段广播扫描，但至少应提供针对用户输入 IP 的定向枚举/探测能力，例如在不创建正式业务 handle 的情况下返回：

- 该 IP 下有几个 H2 ETH endpoint；
- 每个 endpoint 的固定端口、逻辑角色和在线状态；
- 每个 endpoint 的 `device_info` 或最小身份信息；
- 是否属于同一个 chassis；
- 单路还是双路产品。

如果供应商不增加 ETH list/probe，当前固定端口方案仍然可以实施，但必须把“一个 IP 固定派生 5000/5001 两个 endpoint”升级为正式 API/产品契约，而不能只存在于 demo 和 SGStudio 私有实现中。

### 2.3 当前没有必要要求的 API

在硬件不提供共享 LO、相位相干和原子触发的前提下，不建议仅为了“看起来像双通道”要求供应商增加一个聚合双设备 handle。把两个独立 endpoint 强行包装进底层 API 会掩盖真实的部分失败、独立重连和独立波形仓库语义。

除非硬件将来真的提供跨设备同步能力，否则也没有必要增加名义上的 `start_all_channels()` 或 `bus_trigger_all_channels()`。上层连续调用两个接口不能被描述为同步触发。

## 3. H2 API 必须补充确认的 P0 问题

以下问题会直接改变实现方式，开始双路实现前必须得到供应商确认或通过实机测试确定。

### 3.1 ETH 枚举/定向探测

1. 供应商是否计划提供 `device_list_eth()`，或接受一个目标 IP 的 `device_probe/list_eth_endpoints()`？
2. 如果不提供，`device_open_eth()` 是否被正式定义为 ETH 的唯一发现方式？
3. open 失败的返回码能否区分 IP 不可达、端口无 server、协议不匹配、设备初始化失败和通道不可用？
4. 探测 5000/5001 时，是否允许轻量 TCP 连接，还是必须完整 open 才能确认 endpoint？

不建议 SGStudio 自行做网络广播扫描或猜测私有协议；缺少 list 时，只按用户输入 IP 和供应商确认的两个固定端口尝试 open。

### 3.2 Endpoint 身份与固定映射

1. 端口 5000 是否永久代表产品 CH1，5001 是否永久代表产品 CH2？升级、重启和不同型号上是否都保持不变？
2. 两次 open 返回的 `device_info.uid_l64/uid_h32` 是两个射频板各自的稳定 UID，还是树莓派级相同 UID？
3. `device_info.model_name`、版本字符串和能力值是否可能在两个 endpoint 间不同？
4. 是否存在只有 5000 的单路 ETH 产品或历史设备？如果存在，连接 UI 是否需要显式的“单路/双路”模式？

建议 API 或设备元数据补充稳定的 `endpoint_role`、`chassis_uid` 和 `board_uid`。如果不改 API，至少需要把“端口到逻辑通道的固定映射”写成正式契约。

### 3.3 生命周期与故障隔离

1. 同一进程能否长期同时持有 5000 和 5001 的两个 handle？
2. `device_preset()`、`device_close()`、`device_config_power_state()` 作用于一侧时，是否会影响另一侧 server、射频输出或波形内存？
3. 一侧网络断开或 server 重启时，另一侧 handle 是否保证继续有效、继续输出？
4. 能否只重新 open 失败的一侧，而不关闭健康侧？
5. `device_open_eth()` 是否可取消；最坏阻塞时间是否严格由 `read_timeout` 控制？连接两个 endpoint 时总等待时间是否可能达到两倍 timeout？

其中第 2、3、4 项决定 SGStudio 是否能真正实现“部分故障时保留健康通道”。

### 3.4 多 handle 并发与线程安全

1. H2 DLL 是否允许两个 ETH handle 在不同线程并发调用？
2. 是否允许一侧持续执行 `tx_send_stream()` 时，另一侧同时配置 RF、下载波形或查询状态？
3. DLL 是否存在进程级全局状态，导致两个 handle 之间需要调用方串行化？
4. 同一 IP 的两个 server 是否共享总网络带宽、树莓派 CPU 或内部总线限制？双路 Realtime 的最大聚合吞吐量是多少？

当前头文件没有线程安全声明。确认之前，建议 open/close/config/download 先在同一个设备 I/O worker 中串行执行；但“双路不同 Realtime baseband 同时发送”无法长期依赖单路串行模型，必须单独验证。

### 3.5 波形仓库

1. 5000、5001 是否各有完全独立的波形内存和 waveform ID 序列？
2. 对 device0 调用 `tx_clear_waveform()`、`device_preset()` 或低功耗操作，是否保证不影响 device1？
3. 相同 IQ 分别下载到两个 handle 时，返回 ID 是否允许不同？上层将按“允许不同”实现，不依赖 ID 相同。
4. `tx_channel_capabilities.memory_size` 是单个 endpoint 的容量，还是两块设备共享容量？
5. 是否有查询已驻留 waveform、删除单个 waveform 的能力？如果没有，上层只能通过自己的 cache 和整库 clear 管理。

建议供应商明确声明：waveform namespace、容量、clear、reset 和掉电失效均以单个 device handle 为边界。

### 3.6 DeviceSettingDialog 中所谓“统一参数”的底层含义

UI 可以保持一份统一设置，但必须确认每项参数在硬件上的实际作用域：

1. Reference Clock：两块板是各自设置相同参数，还是 CH1 负责 Ref Out、CH2 必须使用 External Ref？
2. System Clock Out：是否只应对某个固定 endpoint 下发？对两边同时开启是否会形成错误连接或冲突？
3. Fan：两个 server 控制同一个树莓派/机箱风扇，还是各自控制板上风扇？重复下发是否安全？
4. GNSS：只有一个共享 GNSS，还是两个 endpoint 都能查询和配置同一个模块？应以哪个 endpoint 的返回为真相？
5. Low Power/Power State：统一开关是否应同时下发两边；关闭其中一侧是否会影响公共供电？
6. LO Mode、RF Port：API 是 endpoint 级，但产品决定保持统一。两个 endpoint 能力不一致时应该取交集还是分别钳位？
7. Trigger In 的 source/action/edge 当前位于 DeviceSettingDialog。产品要求“DeviceSettingDialog 参数统一”是否明确包含 Trigger In？若包含，两边必须始终广播同一配置；若希望 trigger 不同，应把 Trigger In 移入每通道工作区。

这里最需要优先确认 Reference Clock、System Clock Out 和 Trigger In。它们不能只靠“对两台设备循环调用相同函数”来猜测语义。

### 3.7 Trigger 与同步能力边界

1. 两个 endpoint 的 External Trigger 输入是否接到同一物理信号？
2. 同一外部边沿到两台设备的相对延迟和抖动是否有任何规格保证？
3. 两次 `channel_bus_trigger()` 顺序调用的时间差是否完全不承诺？当前应按“不承诺”处理。
4. `device_config_trigger_out()` 在两个 endpoint 上分别控制什么物理 IO？能否用一侧 Trigger Out 驱动另一侧 Trigger In？
5. “频率相干”具体依赖内部公共参考、CH1 Ref Out 到 CH2 Ref In，还是其他固定布线？应用是否需要配置它，还是硬件上电后自动建立？

在答案明确前，UI 不应出现“同步启动”“相干相位”等字样。

### 3.8 状态查询与诊断接口

新版 API 缺少一个轻量、无副作用的 `device_query_health/connection_state()`。当前项目只能借助温度、供电或其他业务查询间接发现断连，这对两个 endpoint 的独立状态显示不够清晰。

建议供应商至少补充或明确以下能力：

- endpoint 是否在线；
- server/协议是否就绪；
- 当前 device handle 是否有效；
- 最近一次底层通信错误；
- 不触发 RF 或设备状态变化的轻量查询。

## 4. 当前 SGStudio 需要补充的组内模型

### 4.1 保留 setCurrentDevice 的整机切换职责

[DeviceManager::setCurrentDevice()](../../src/plugins/core/devicemanager.cpp) 当前用于真实产品设备之间的切换，例如 USB A 切到 USB B、USB 切到 ETH 机箱或 ETH 机箱切回 USB。它在切换时调用旧设备的 `closeForSwitch()`，随后 open 新设备，这个生命周期语义是正确的，应当保留。

CH1/CH2 是同一个 ETH 机箱内部的编辑目标，不是两个让用户通过 `setCurrentDevice()` 切换的产品设备。因此 CH 按钮不得复用 `setCurrentDevice()`；否则 [FancyDevice::closeForSwitch()](../../src/plugins/htra/fancydevice.cpp) 会释放上一 endpoint handle，破坏双路持续连接。

正确的层级是：

```text
DeviceManager current product：USB 设备，或一个 ETH chassis/session
ETH session open endpoint set：两个持续打开的 FancyDevice
ETH session selected channel：当前 UI 正在查看和编辑的 CH1 或 CH2
```

只有最上层整机切换调用 `setCurrentDevice()`。通道按钮只改变 ETH session 内部的 `selected channel`，不得触发 `open()`、`close()`、`preset()`、Mute 或 waveform clear。

### 4.2 当前 TX pipeline 只有一个目标设备和一份编辑状态

当前 `TxApplyRequest` 只有一份 `common + carrier + provider`，执行器通过 `DeviceManager::currentDevice()` 找目标设备。Carrier panel、baseband provider 选择和 Playback payload 也都是单工作区语义。

要允许两个通道拥有不同 carrier 和 baseband，需要至少维护：

```text
ChannelWorkspace[2]
├─ carrier plan 及 FScan/LScan/MScan 参数
├─ baseband provider 类型、业务配置和 sample rate
├─ RF/MOD/mode/trigger-count 等执行状态
├─ 未应用/已应用状态与最近一次错误
└─ 指向对应 FancyDevice 的稳定 endpoint key
```

切换 UI 时保存当前工作区并加载另一个工作区，不能把 CH1 当前表单值覆盖到 CH2。

### 4.3 两个 FancyDevice 已经天然形成两套波形 cache

在“两个 FancyDevice”模型下，不需要把一个 `FancyDevice::m_waveformCache` 改成二维表。每个实例保持自己的 cache 和 H2 返回的 waveform ID 即可。

但 Core 层必须为两个通道分别保存或重建 Playback payload：

- 相同 baseband 可以复用同一份只读 host IQ 数据，但仍需向两个 device handle 分别下载；
- 不同 baseband 各自生成和下载；
- 仅切换 UI channel 不得重新生成或重新下载；
- 对某一 endpoint 执行 reset、power-off、close/reopen 或 clear 后，只使该 FancyDevice 的驻留 cache 失效。

### 4.4 状态轮询、错误归属和 UID

当前 DeviceManager 只轮询 `currentDevice`。双路保持 open 后，需要同时维护两侧连接状态，否则非当前通道断开可能长期不可见。

错误和异步结果不能只依赖 `deviceUid`：如果两个 server 返回相同树莓派级 UID，异步结果可能串台。建议内部使用稳定复合键：

```text
EthEndpointKey = { chassisIp, fixedPort }
```

产品层再映射为 `{groupId, channelIndex}`。

## 5. 推荐的 ETH 连接交互

### 5.1 把连接对象从 endpoint 改成 chassis

当前 [EthConnectDialog](../../src/plugins/core/ethconnectdialog.cpp) 要求用户输入 IP 和一个端口，只创建一个 `FancyDevice`。由于 H2 API 没有 ETH list，保留手工输入 IP 是必要的；但双设备产品中，用户实际连接的是一个 IP 对应的一台机箱，而不是手工选择某个内部 server。

推荐默认界面：

- 保留 `IP Address` 输入；
- 移除可编辑 Port；
- 以只读说明展示：
  - `CH1  5000`
  - `CH2  5001`
- 保留 Local Interface 和网段提示；
- Connect 按钮一次建立整个双路 session。

如果仍需要支持单路 ETH 历史设备，可在确认产品需求后增加非默认的 Advanced/Diagnostic 单 endpoint 模式；不建议让普通用户继续手工输入 5000/5001。

### 5.2 连接过程显示两个 endpoint 的独立进度

推荐状态区显示：

```text
CH1  192.168.1.100:5000   Connecting / Connected / Failed
CH2  192.168.1.100:5001   Connecting / Connected / Failed
```

在 H2 DLL 并发契约确认前，两个 open 可以在同一个 I/O worker 中顺序执行，但 UI 应分别更新状态。

首次连接推荐采用“产品级全成功”策略：

- 两侧都 open 成功，才进入主界面并宣告双路连接成功；
- 任一侧失败，连接对话框保留并明确显示失败端口；
- 已成功打开的一侧应在退出失败流程时显式关闭，避免形成用户看不到的隐藏 handle；
- 提供 Retry，重试前复用或清理状态必须明确。

是否允许初始连接后以单路降级模式继续，是产品决策问题。第一阶段不建议默认允许，否则设备名称、能力、设置广播和断开动作都会出现两套语义。

### 5.3 运行中部分断连与首次连接失败应区别处理

首次连接建议全成功；但运行中一侧断开时，推荐保留健康侧：

- 健康通道继续输出，不自动 reset/close；
- 失败通道按钮显示红色/断开状态并禁止 Apply；
- 提供仅重连失败 endpoint 的动作；
- 公共 DeviceSetting 修改应提示“未能应用到全部通道”，不能静默只改健康侧；
- 整机 Disconnect 明确关闭两个 FancyDevice。

这要求底层确认一侧故障确实不会污染另一侧 handle。

## 6. 推荐的通道切换 UI

### 6.1 位置与形式

在主 TX 工作区、靠近当前设备状态的位置增加一个简洁的 segmented button：

```text
[ CH1 ● ] [ CH2 ● ]
```

状态点建议表示 endpoint 状态：

- 绿色：已连接；
- 黄色：连接中、配置中或存在 warning；
- 红色：断开/错误；
- 灰色：未连接或不可用。

不要把该按钮放进 DeviceSettingDialog，因为它切换的是 carrier/baseband 工作区，而 DeviceSettingDialog 按产品要求保持整机统一。

### 6.2 切换行为

点击 CH1/CH2 时只执行：

1. 保存离开通道的 carrier/baseband 编辑快照；
2. 加载目标通道的编辑快照；
3. 更新 capability 限制、波形状态和运行状态显示；
4. 将后续 Apply 明确路由到目标 FancyDevice。

不得执行：

- 关闭或重新打开任一 FancyDevice；
- 自动 Mute 离开通道；
- clear waveform；
- 自动把当前 carrier/baseband 配置复制到目标通道；
- 因 UI 切换而重新生成 Playback IQ。

如果异步生成或 Apply 尚未结束，结果必须绑定到发起时的 `{groupId, channelIndex}`，不能落到用户切换后的当前页面。

### 6.3 两路同时输出的能力边界

不同 CW 或 Playback 可以按通道依次配置，保持两个 handle 打开后同时运行。不同 Realtime Streaming 则不是一个按钮可以解决的问题：当前只有一套 streaming business、发送生命周期和吞吐统计。

必须确认产品第一阶段是否要求：

- 两路同时 CW；
- 两路同时 Playback；
- 一路 Playback、一路 CW；
- 一路 Realtime、另一路 CW/Playback；
- 两路同时使用不同 Realtime baseband。

最后一项需要两套持续数据生产/发送上下文、明确的 H2 多 handle 并发保证和聚合网口吞吐验证，工作量显著高于独立 Playback。

## 7. DeviceSettingDialog 统一、Carrier/Baseband 分离的状态归属

推荐状态边界如下：

| 设置 | UI 归属 | 硬件下发策略 |
|---|---|---|
| Reference Clock / Ref Frequency / Ref Out | 整机统一 | 依据供应商确认决定分别下发或主从下发 |
| Fan / Low Power / GNSS | 整机统一 | 依据共享资源语义选择主 endpoint 或两侧广播 |
| LO Mode / RF Port | 整机统一 | 两侧广播；可选值取两侧 capability/spec 交集 |
| Trigger source/action/edge | 暂按整机统一 | 两侧广播；需产品再次确认是否允许不同 |
| Trigger Out | 继续隐藏 | 等待 API/硬件语义确认 |
| Center / Level | 每通道 | 下发当前选中 FancyDevice |
| Fixed/FScan/LScan/MScan carrier plan | 每通道 | 下发当前选中 FancyDevice |
| Baseband provider 及其业务参数 | 每通道 | 生成/发送到当前选中 FancyDevice |
| Playback waveform residency | 每通道/每 FancyDevice | 两套独立 cache 和 waveform ID |
| RF/MOD 输出状态与运行模式 | 每通道 | 独立保持，不随 UI channel 切换 |
| Trigger count / sweep repeat | 建议每通道 | 属于本次执行计划，不属于机箱静态设置 |

公共设置广播没有硬件原子性。若 CH1 成功、CH2 失败，不建议静默回滚，因为回滚本身仍可能失败。推荐：

1. 保存两侧修改前快照；
2. 顺序应用并分别记录结果；
3. 任一失败时显示明确的部分应用状态；
4. 重新 query 两侧真实值；
5. 暂停新的 TX Apply，直到用户 Retry 或恢复统一状态。

## 8. 推荐的项目侧对象关系

坚持维护两个 `FancyDevice`，同时增加一个不伪装成 H2 多通道 handle 的上层协调对象：

```text
EthDualDeviceSession / EthDeviceGroup
├─ chassis IP / group identity
├─ FancyDevice CH1 (IP:5000)
├─ FancyDevice CH2 (IP:5001)
├─ selected channel index
├─ ChannelWorkspace[2]
├─ common DeviceSetting state
└─ group lifecycle / partial failure / reconnect policy
```

协调对象负责整机连接和公共设置广播；`FancyDevice` 继续只负责一个 endpoint 的真实 H2 handle、`channels[0]`、capabilities、waveform cache 和状态。

不建议通过给 `FancyDevice` 增加第二个 handle 来实现，因为这会重新混淆单 endpoint 责任，并让错误、能力、波形和重连再次纠缠在同一实例中。

DeviceManager 后续至少需要区分：

- 当前产品设备：USB 设备或 ETH chassis/session；
- ETH session 组内两个持续打开的 endpoint；
- ETH session 当前编辑通道；
- 当前 TX Apply 的显式目标 endpoint。

`setCurrentDevice()` 和 `closeForSwitch()` 继续只处理整机切换。不要为 CH1/CH2 增加 `closeForSwitch()` 特判，也不要让 `currentDevice` 承担组内通道选择；组内选择应由 ETH session 自己维护。

## 9. 需要产品决定的问题

### P0：决定后才能实施

1. 初次连接一个 endpoint 失败时，是整机连接失败，还是允许单路降级？本文推荐第一阶段整机失败。
2. 运行中一侧断开时，是否必须保持另一侧继续输出？本文推荐保持。
3. DeviceSettingDialog 的“统一”是否包含 Trigger In source/action/edge？
4. 两路同时不同 Realtime baseband 是否属于第一阶段目标？
5. 是否仍要支持只监听 5000 的历史单路 ETH 产品？
6. 通道切换后的 carrier/baseband 编辑状态是否需要跨应用重启持久化？
7. 是否要求供应商补充 ETH 定向枚举/probe；若短期不补，是否正式接受由 SGStudio 固定尝试 5000/5001？

### P1：可在基础双路打通后决定

1. Device Info 是显示一个机箱摘要，还是同时展示两块板的 UID/版本/温度？建议机箱摘要加 CH1/CH2 明细。
2. 公共设置部分应用失败时，是阻止全部 TX、仅阻止失败通道，还是允许用户强制继续？
3. 是否提供“复制 CH1 设置到 CH2”快捷操作？这应是显式用户动作，不能成为切换默认行为。
4. 是否需要“同时 Apply 两个通道”？若需要，它仍是顺序执行且不具备硬件原子性，UI 文案必须准确。
5. 是否需要整机 Mute 按钮，同时关闭两侧 RF；以及每通道是否仍保留独立 RF 开关？

## 10. 建议的实机真相测试

在大规模改造前，建议先用最小双 endpoint 诊断程序完成：

1. 同时 open `IP:5000` 与 `IP:5001`，记录两份完整 `device_info`、`channels[0]` 和 capability。
2. 分别配置不同 center/level/CW，确认互不覆盖。
3. 分别配置不同 FScan/LScan/MScan，确认互不覆盖。
4. 向两边下载相同 IQ，记录各自 waveform ID；再下载不同 IQ 并分别播放。
5. clear/preset/close CH1，确认 CH2 的波形和输出是否继续存在。
6. 断开或重启一个 server，确认另一 handle 的调用结果和持续输出。
7. 并发执行一侧 Realtime send 与另一侧查询/配置/下载，验证 DLL 线程安全和吞吐。
8. 对两侧配置 External Trigger，使用示波器测量启动偏差；测试结果只用于明确能力，不据此承诺相位相干。
9. 分别测试 Reference Clock、Ref Out、Fan、GNSS、Low Power，确定共享资源的真正 endpoint 归属。

## 11. 设计结论

- H2 API 足以分别控制两个 ETH server，但应被当作单 endpoint API，而不是当前产品的多通道 API。
- H2 API 最明显的功能缺口是没有 ETH list/定向 probe；其次才是双 handle 并发、共享资源副作用、endpoint 身份、波形仓库边界和故障隔离契约。
- 用户连接对象应是一个 IP 对应的双路机箱，5000/5001 不应继续作为普通用户可编辑参数。
- UI 上一个 CH1/CH2 切换按钮是合适的最终交互，但按钮背后必须保持两个 `FancyDevice` 同时 open，并分离“连接生命周期”和“当前编辑通道”。
- DeviceSettingDialog 保持统一是可行的，但必须逐项定义向一个或两个 endpoint 的下发规则；carrier、baseband、波形驻留和输出状态应独立保存。
- `DeviceManager::setCurrentDevice()` 的整机切换语义保持不变；ETH CH1/CH2 切换必须在当前 ETH session 内部完成，不能进入整机切换路径。
