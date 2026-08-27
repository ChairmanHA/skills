# H2 API 2026-08-07 单通道兼容迁移方案

## 状态

- 当前阶段：Phase 1-3 代码适配与 Windows x86_64 Debug 构建已完成，等待实机冒烟和日志回收。
- Verification Level：`debug-build`；实机语义留待用户完成运行验证后根据日志继续收口。
- 多通道范围：新版 API 的打开流程必须正确接收设备通道信息，但本轮不提供通道选择、双通道并发、逐通道状态或多通道 UI；业务只绑定一个主 TX 通道和 `stream 0`。

### 2026-08-10 已确认实施口径

- 仅支持并验证 Windows x86_64；本轮不适配其他平台，也不更新 KnowledgeBase。
- USB 按最新枚举结果的 `device_info.chn` 打开，ETH 固定传 `chs = 1`；打开后只绑定返回范围内的第一个 TX 通道和 `stream 0`。
- 不猜测新版设备元数据字符串的角色或顺序；先完整记录 `model_name`、`version_name[]`、`interface_name[]` 和通道信息，待实机日志确认后再改公共数据模型。
- Trigger Out 与 GPIO 第一阶段均返回 unsupported，并隐藏现有入口。
- Sweep 第一阶段开放并完整适配 FScan、LScan；只有 MScan 保持现有隐藏入口并返回 unsupported，旧实现留待下一阶段迁移。
- 前端范围直接以 `channel_tx_query_capabilities()` 为准。若已按能力范围约束后的 FScan 配置仍返回 `H2_WARNING_PARAMOUTRANGE`，接受该警告但记录为 H2 API capability contract 异常，待供应商修复，不为此增加第二套前端钳位规则。

## 真相源与审计边界

接口判断按以下优先级进行：

1. `3rdParty/h2_api/include/h2_api.h` 是唯一接口真相源。
2. 当前 CMake 和实际纳入编译的源码用于判断项目影响面。

明确排除：

- 不以 KnowledgeBase 作为本次 API 结论依据。
- `h2_typedef.h` 没有被当前活跃源码包含，且其内容已被新版 `h2_api.h` 取代，不做兼容层，也不继续引用其中的旧枚举和结构体。
- `h2_mod.h` 当前同样没有被项目源码包含，本次不改调制算法 API。

## 静态观察

### 1. 实际编译边界

- `3rdParty/CMakeLists.txt` 纳入 `3rdParty/h2_api`。
- `src/plugins/htra/CMakeLists.txt` 的 `HTRA` 目标链接 `HTRA::HTRA`。
- 当前只有以下活跃源码直接包含 `h2_api.h`：
  - `src/plugins/htra/fancydevice.h`
  - `src/plugins/htra/htradevicescanner.cpp`
  - `src/plugins/htra/plugin.cpp`
- 绝大多数调用集中在 `src/plugins/htra/fancydevice.cpp`。因此适配应继续收口在 HTRA 设备边界，不应把供应商结构体扩散到业务层。

### 2. 当前源码无法直接通过新版头文件编译

当前仍使用新版头文件已删除的类型或函数，包括但不限于：

- 旧类型：`state`、`referenceclock_source`、`lomode`、`trigger_source`、`trigger_action`、`trigger_edge`、`power_state`、`fan_mode`、`gnss_info`、`supply_info`、`trigger_out`。
- 旧状态宏：`STATUS_*`、`POWERON`、`POWEROFF`、`REFCLKSOURCE_*`。
- 旧函数：`device_powered_by_usb()`、`device_query_state()`、`device_query_supply()`、`device_query_gnss_info()`、`device_gpio_setbits()`、`device_gpio_resetbits()`、`channel_config_trigger_out()`、`channel_query_trigger_out()`、`channel_trigger_bus()`、`tx_config_cw()`、`tx_config_playback()`、`tx_test_fscan/lscan/mscan()`。
- 已改变签名的函数：`device_open_usb/eth()`、`device_query_options()`、`device_query_apiversion()`、`device_config_fan()`、`device_config_gnss()`、`device_query_temperature()`、`device_config/query_clock()`、`tx_config_stream()`、`tx_query_stream()`、`tx_send_stream()`。

### 3. SDK 二进制目前不是全平台同代

仓库状态和二进制字符串静态检查显示：

- `msvc_x86_64/h2_api.dll` 与 `.lib` 是本次新加入/更新的版本，能看到 `device_ignore_pd`、`device_query_power_supply`、`device_config_trigger_out`、`channel_bus_trigger` 等新符号，并不再出现旧 `tx_config_cw/tx_config_playback`。
- `msvc_x86_32`、`gcc_x86_64`、`gcc_aarch64` 现有二进制仍能看到旧 `tx_config_cw/tx_config_playback`，看不到上述关键新符号；它们也没有出现在本次 git 修改列表中。

由此可直接观察到当前 SDK 包只有 Windows x86_64 头文件/二进制看起来属于同一次更新。Linux x86_64、Linux aarch64 和 Windows x86 的新头文件编译结果会与现有库产生缺符号或 ABI 不匹配风险。在获得匹配二进制前，不能把这些平台列为可验证目标。

## 适配清单

### A. 基础类型、状态码和错误处理

1. HTRA 设备层中所有已删除的供应商枚举类型改为头文件要求的定宽类型，主要为 `uint8_t`；值继续使用新版宏。
2. 状态码统一迁移到 `H2_NOERROR`、`H2_ERROR_*`、`H2_WARNING_*`。
3. 原 `STATUS_WARNING_LOREF_UNLOCKED` 不再存在；新版对应命名为 `H2_WARNING_DLREF_UNLOCKED`，并新增 `H2_WARNING_IFLO_UNLOCKED`。UI 文案和警告分类必须使用新版名称，不能保留不存在的旧宏。
4. 保持“负数为错误、正数为警告”的现有上层策略，但实时轮询不能再把所有非零返回都判定为断连：
   - 只有 `H2_ERROR_BUS_DISCONNECT` 应直接进入断连回收路径；
   - 其他负值按具体调用失败处理；
   - 正警告保留连接并发布警告。
5. 结构体全部使用 `{}` 初始化，确保 `extension1/extension2` 为 `nullptr`。

### B. USB 扫描、设备打开与主 TX 通道

1. `device_powered_by_usb(dnum)` 改为 `device_ignore_pd(dnum)`。
2. USB 扫描结果中的 `device_info.chn` 必须随设备快照保存；重新按 UID 枚举时也要刷新该值。
3. USB 打开改为：
   - `device_open_usb(dnum, chs, &device, channels, &openedInfo)`；
   - `chs` 来自与当前 UID 匹配的最新 `device_list_usb()` 结果；
   - 不再把 `channelCount` 地址作为输出参数，打开后以 `openedInfo.chn` 和本次请求值校验返回范围。
4. `channels` 容量继续使用头文件的 `MAXCHANNELS`，拒绝 `chn == 0` 或 `chn > MAXCHANNELS`。
5. 虽然本轮不实现多通道功能，仍不能默认 `channels[0]` 一定是可发射通道。打开后遍历已返回通道，选择第一个 `type == CHANNELTYPE_TX` 的通道作为主 TX 通道，并缓存其索引；没有 TX 通道则打开失败。
6. 主 TX 通道必须满足 `max_streams > 0`；本轮固定 `strm = 0`，不增加 stream 选择 UI。
7. 零 UID 探测路径也必须迁移到新打开签名，不能继续调用旧五参数顺序。
8. ETH 打开改为新版 `eth_setting` 字段和指针传参：`is_ipv6/ip_address/remote_port/read_timeout`，删除 `eth_interface`。

### C. ETH `chs` 的本轮契约

新版只有 `device_list_usb()`，没有 ETH 预枚举接口，但 `device_open_eth()` 同样要求调用前传入 `chs`。当前头文件没有说明对 ETH 应传“期望打开数”“缓冲区容量”还是设备真实通道数。

本轮已经确认采用 Windows x64 单通道路由：ETH 固定传 `1`，只绑定返回范围内的第一个 TX 通道；该限制必须在代码注释和实机日志中明确，不能宣称已支持双通道 ETH。

### D. 设备信息、API 版本和选件

1. `device_query_apiversion()` 改为调用方提供 `char[16]`，返回值只作为状态码处理。
2. `device_query_options()` 改为 `option[] + uint16_t count`，有效选件只读取 `state != 0` 的 `code`；不能再把数组元素本身当选件号。
3. 头文件没有提供 `option[]` 的容量输入或最大选件数宏。当前可以保留有界本地数组，但需要供应商确认 API 的最大写入数，避免把“本地数组够大”当成正式契约。
4. `device_info` 已删除：
   - `hardware_version`
   - `mfw_version/ffw_version/pmu_version/agu_version/bus_version/eio_version`
   - `bus_bandwidth`
5. 新版只给出 `model_name`、`version_name[16][16]`、`interface_name[8][16]`，但头文件没有规定 `version_name` 的角色、顺序或字符串格式。因此不能无依据地把数组下标映射回 MCU/FPGA/BUS/EIO 数值字段。
6. 当前 `Core::DeviceInfo`、Device Info UI、About、Updater、能力快照和 Streaming USB2/USB3 提示都依赖已删除字段。这里必须选择一种明确策略，见“实施前决策点”。

### E. TX stream 统一迁移

在 HTRA 设备层增加少量专用 helper，把所有模式集中构造成新版 `tx_stream`；不要用宏伪造旧 API：

1. CW：
   - `state = STATE_ON`
   - `mode = TX_CW`
   - `fc_offset = 0`
   - `phase_offset = 0`
   - `tx_config_stream(primaryTx, 0, &setting)`
2. Realtime：
   - `mode = TX_REALTIME`
   - `realtime_srate = sampleRate`
   - 同样配置 `stream 0`
3. Playback：
   - `mode = TX_PLAYBACK`
   - 填充 `waveforms/waveform/repeat/playback_srate`
   - 单波形、多波形、FSCAN/LSCAN/MSCAN Playback 和普通 `triggerStart()` 全部走同一个 helper
4. 实时发送改为 `tx_send_stream(primaryTx, 0, iq, points)`；当前 Streaming 业务传入的是 IQ 复数点数，可直接对应新版 `points` 语义。
5. `tx_config_output()`、`channel_start()` 和其他 TX 配置统一使用主 TX 通道，不再依赖数组到指针的隐式退化。
6. 总线触发改为 `channel_bus_trigger(primaryTx, 1U << 0)`。
7. `channel_config_trigger()` 和 `channel_bus_trigger()` 在当前头文件中标为“临时恢复”，应隔离在单一 trigger helper，避免未来再次变化时散落修改。

### F. 扫描业务与参数预检

1. `tx_test_fscan/lscan/mscan()` 已无替代函数，`previewCarrierPlan()` 不能机械改名。
2. 打开主 TX 通道后调用一次 `channel_tx_query_capabilities()` 并缓存：频率、带宽、功率、步进、dwell、Realtime/Playback 采样率和波形内存范围。
3. UI preview 只做能力结构能证明的范围检查/钳位：
   - FSCAN：起止频率、频率步进、level、dwell；
   - LSCAN：频率、起止 level、level step、dwell；
   - MSCAN：逐点频率、level、dwell，并单独检查 `int16_t` 点数边界。
4. 能力结构没有给出的组合约束、点数上限和离散采样率语义不能推断；最终仍以 `tx_config_*()` 返回状态为准。
5. 若实际配置返回 `H2_WARNING_PARAMOUTRANGE`，需要决定是否立即调用相应 `tx_query_*()` 回读并写回 UI。当前接口只返回成功/失败，完整写回可能需要扩展 Core 的 sweep apply 结果；第一阶段可先接受警告、记录设备实际值与 UI 请求值可能暂时不一致这一限制。
6. `tx_channel_capabilities` 的 `playback_srate_min/max` 只是边界，不能据此把现有 `400 MSPS` 离散点误扩成连续区间。现有选件策略可与设备查询结果求交集，`memory_size` 则优先采用设备查询值。

### G. 触发输入与 Trigger Out

触发输入仍可使用 `tx_trigger + channel_config_trigger()`，只需把本地类型改为 `uint8_t` 并继续设置 source/edge/action/count。

Trigger Out 没有等价迁移：

- 旧：通道级 `trigger_out { enable, action, edge }`，支持 config/query。
- 新：设备级 `device_trigger_out { state, source, negative_pulse, recounter }`，只有 config，没有 query。

因此：

1. 现有 `triggerOutAction` 不能映射到 `recounter`。
2. 现有 rising/falling edge 不能无证据地映射到正/负脉冲。
3. 写回逻辑不能继续声称读取到了硬件 Trigger Out 状态。
4. MScan 当前强制打开 Hop/Rising Trigger Out 的代码也必须停止使用旧接口。

推荐第一阶段禁用/隐藏 HTRA Trigger Out 配置，并在设备侧只保留明确的“关闭”状态；后续在确认产品需求后，用“来源通道、脉冲极性、重计数”重做公共 Profile/UI。若产品决定使用固定默认值直接启用，也必须明确默认 `source/negative_pulse/recounter`，不能复用旧 action/edge 名称掩盖语义变化。

### H. 实时状态、GNSS、电源和风扇

1. 温度：改为 `device_query_temperature(&device, 0, &temperature)`；当前头文件只证明 `domain` 参数存在，迁移说明给出当前使用 `0`。
2. 供电：删除本地供应商 `supply_info`，分别调用：
   - `device_query_power_supply(&device, RF_RAIL, &rfVoltage, &rfCurrent)`
   - `device_query_power_supply(&device, USB_RAIL, &usbVoltage, &usbCurrent)`
   两条 rail 独立降级，单条失败不应抹掉另一条成功数据。
3. GNSS：
   - `gnss_info` 改为 `gnss_data`；
   - `locked` 改为 `is_locked`；
   - `pps_en` 改为 `state_pps_out`；
   - `device_query_gnss_info()` 改为 `device_query_gnss_data()`；
   - `device_config_gnss()` 改为传 `const gnss_setting*`；
   - 当前 Core 没有星座配置，输出结构至少显式填充全星座位掩码，不依赖未初始化值。
4. 风扇：新版只接受阈值。按头文件可无歧义映射：
   - On -> `-INFINITY`
   - Off -> `+INFINITY`
   - Auto -> 当前产品阈值 `50.0f`
   查询仍只能返回应用缓存值，因为头文件没有 fan query。
5. 电源：类型改为 `uint8_t`，值改为 `POWER_ON/POWER_OFF/POWER_SAVING`。可继续使用现有串行策略；`device_query_power_state()` 可用于需要真实回读的后续增强，不是完成迁移的必要条件。
6. `device_query_state()` 已删除且无等价健康/锁相查询，不能用 `device_query_power_state()` 冒充。实时 LO warning 只能来自其他 API 返回值；原轮询分支必须删除或降级。

### I. GPIO

新版 `h2_api.h` 同时删除了 `hardware_version`、`device_gpio_setbits()` 和 `device_gpio_resetbits()`，没有替代接口。因此当前按硬件版本推导 GPIO 数量、open 时清零 GPIO、菜单查询和写 GPIO 全部失去 API 基础。

推荐第一阶段对 HTRA 返回 GPIO unsupported，隐藏或禁用对应入口；不要保留只改本地缓存、不下发硬件的伪实现。若 GPIO 仍是产品要求，必须先由供应商补充新版 API 或确认改走其他稳定接口。

### J. API/固件版本与上层数据模型

头文件把 API 版本和设备版本都改成字符串，项目公共模型仍是打包 `uint32_t`。可选策略：

1. **临时最小策略**：仅为 Windows x64 主 TX 功能恢复编译；无法可靠识别的硬件/固件字段设为 unavailable，并同步让 UI 显示 `-`，不得显示伪造的 `0.0.0`；API 版本若严格符合 `major.minor.patch`，可在 HTRA 边界临时解析为现有 packed integer。
2. **推荐长期策略**：把 `Core::DeviceInfo` 增补原始 `modelName/versionNames/interfaceNames`，把 `DeviceApiVersion` 属性改为字符串或新增字符串属性；Device Info/About/Updater 使用原始字符串。若仍要保留 MCU/FPGA/BUS/EIO 固定栏目，需要供应商先定义 `version_name[]` 的角色和顺序。

长期策略会额外影响：

- `src/plugins/core/idevice.h`
- `src/plugins/core/deviceinfowidget.*`
- `src/plugins/core/aboutdialog.*`
- `src/plugins/updater/updatedialog.cpp`
- 可能使用硬件版本的 capability snapshot
- `src/plugins/htra/streamingpanel.*` 的 USB2/USB3 提示

`interface_name[]` 的格式同样没有定义。在确认字符串格式前，Streaming 不应猜测 USB2/USB3；可暂时采用保守阈值，或把此提示改为使用明确的应用配置/供应商后续接口。

## 实施前决策点

### D1. 首个目标平台

- 推荐：先只做 Windows x86_64，因为当前只有该平台看起来具备匹配的新库。
- 若要求一次覆盖 Linux x86_64/aarch64 或 Windows x86，先向供应商取得同版二进制，再开始业务改造。

### D2. 被删除的设备元数据

- 推荐：采用长期字符串模型，但在供应商未定义版本数组角色前只展示原始列表，不猜 MCU/FPGA/BUS/EIO 映射。
- 快速路线：第一阶段统一显示 unavailable，后续再补模型。

### D3. Trigger Out

- 推荐：第一阶段禁用；后续按新结构重做。
- 备选：产品明确给出固定 `source/negative_pulse/recounter` 后，仅实现 enable/disable，不复用旧 action/edge 语义。

### D4. GPIO

- 推荐：标记 unsupported 并隐藏入口。
- 只有供应商补充新接口后才恢复。

### D5. ETH `chs`

- 已选择单通道路由：固定传 `1`，并只绑定返回的第一个 TX 通道。

### D6. Sweep 警告回写

- 推荐第一阶段完成能力范围 preview 和实际配置状态处理；完整设备钳位回写作为第二阶段接口设计。
- 若产品要求 UI 始终显示硬件真实值，则本次同步扩展 sweep apply/writeback 结果，范围会明显扩大。

## 分步实施方案

### Phase 0：SDK 与语义闭环

目标：在写业务代码前消除会导致错误实现的外部不确定性。

1. 确认首发平台；就是 Windows x86_64。
2. 向供应商确认：
   - ETH `device_open_eth(..., chs, ...)` 的 `chs` 语义；
   - `option[]` 最大写入数量；
   - `version_name[]` 的顺序/角色/格式；
   - `interface_name[]` 的格式；
   - Trigger Out 的产品推荐 `source/negative_pulse/recounter`；
   - GPIO 是否有新版替代 API；
   - `tx_stream` 中指针数组是否只在调用期间同步读取。
3. 固化本轮范围：单个主 TX 通道、`stream 0`、无通道选择 UI。

完成条件：目标平台头文件和库同代，D1-D6 至少有明确的临时策略。

### Phase 1：完成单通道编译闭环

主要文件：

- `src/plugins/htra/fancydevice.h`
- `src/plugins/htra/fancydevice.cpp`
- `src/plugins/htra/htradevicescanner.cpp`
- `src/plugins/htra/plugin.cpp`

步骤：

1. 替换类型、宏、状态码和 API 版本查询。
2. 迁移 USB/ETH 打开、设备选件、GNSS、温度、供电、风扇和电源签名。
3. 增加主 TX 通道索引和 `stream 0` 校验。
4. 增加 CW/Realtime/Playback 三个窄 helper，替换全部旧 stream/CW/Playback 调用。
5. 将 bus trigger 改为 stream mask。
6. 删除 `tx_test_*`、旧 Trigger Out、GPIO 和 `device_query_state()` 的编译依赖，按已选降级策略暴露 unsupported。
7. 保持所有供应商结构体在 HTRA 边界内。

完成条件：活跃源码不再出现已删除的旧 H2 类型/宏/函数；目标平台 HTRA Debug target 可以编译链接。

### Phase 2：设备打开、状态与能力链路

1. 验证扫描快照 `chn`、按 UID 重枚举、打开通道数和主 TX 选择。
2. 打开后查询并缓存 `tx_channel_capabilities`。
3. 将 capability query 与现有 Playback/Streaming 选件策略做保守合并。
4. 改造实时状态轮询的错误分类、分 rail 供电、GNSS 新结构和温度域。
5. 按 D2 处理设备/API/接口版本显示和 Updater 输入。

完成条件：单通道旧设备与双通道新设备都能打开，但所有业务只操作已验证的主 TX 通道；实时轮询不会因普通警告误判断连。

### Phase 3：TX 主业务迁移与回归

按风险从低到高逐条验收：

1. Mute / CW：`TX_CW`、RF/MOD 状态、start、bus trigger mask。
2. 普通 Playback：清波形、下载、单波形 `TX_PLAYBACK`、触发启动。
3. 多波形 Playback：数组数量、repeat、sample rate 与调用期生命周期。
4. Realtime Streaming：`TX_REALTIME`、动态 sample rate 重配、`tx_send_stream(..., 0, ...)`。
5. FSCAN/LSCAN + CW。
6. FSCAN/LSCAN + Playback。
7. FSCAN/LSCAN + Streaming；MScan 本阶段保持隐藏且返回 unsupported。
8. BUS/External/XPPS 三种 trigger source；BUS 固定验证 `stream_mask == 1U`。

每条链路同时检查负错误、正警告、UNLEVEL 和 PARAMOUTRANGE，不只检查返回 0。

完成条件：设备可打开，CW、Playback、Realtime、FScan 与 LScan 单通道主业务恢复；双通道设备只在主 TX 通道产生输出，未选择的通道不被业务配置。MScan、Trigger Out、GPIO 不作为本阶段成功条件。

### Phase 4：非等价功能收口

1. 根据 D3 决定保持 Trigger Out disabled，还是按新 Profile/UI 实现设备级配置。
2. 根据 D4 决定永久移除 HTRA GPIO 入口，还是等待新 API。
3. 根据 D2 完成字符串版本模型、动态版本展示、Updater 和接口名称展示。
4. 根据 D6 决定是否增加 sweep 配置后的硬件回读写回。

完成条件：UI 不展示无法下发或无法回读的伪能力；所有降级都对用户可见且有明确原因。

### Phase 5：目标平台验证

本轮只验证 Windows x86_64：

1. Windows x86_64 Debug build/run。
2. 检查部署产物实际复制的是对应新库，并在运行时记录 `device_query_apiversion()` 字符串。

Linux x86_64、Linux aarch64 与 Windows x86 均明确不在本次升级范围内，不以 Windows x64 的结果推断其兼容性。

## 实施方式选择

### 方案 A：两阶段迁移（推荐）

先以 Windows x86_64 完成 Phase 1-3，并明确禁用 Trigger Out/GPIO、暂时降级无法解释的版本元数据；随后补齐供应商契约和跨平台库，再做 Phase 4-5。

优点：最快恢复核心 TX 链路，风险集中且每个降级都可见。缺点：会有一个功能受限的中间版本。

### 方案 B：完整闭环后一次迁移

先完成 Phase 0 的所有供应商确认、取得所有平台库，并同步重做字符串设备信息、Trigger Out 和可能的 sweep writeback，再一次进入实现。

优点：没有临时数据模型和 UI 降级。缺点：依赖外部确认最多，首次可编译版本会更晚，改动面也最大。

### 方案 C：私有适配器分支

在 `FancyDevice` 内部先建立窄的主通道、stream、trigger、status helper，再逐条切换调用方；不在公共头文件中定义旧函数名兼容宏。

这可以与方案 A 或 B 组合。它能减少重复构造 `tx_stream`，并隔离当前仍标为临时的 trigger API。不要建立“旧 API 同名 shim”，因为 Trigger Out、预检、设备信息和 GPIO 并不存在等价实现，同名包装会隐藏真实语义差异。

## 成功标准

1. 活跃源码只使用当前 `h2_api.h` 中存在的类型、宏、字段和函数签名。
2. `h2_typedef.h` 不被重新引入。
3. USB 打开使用最新枚举结果的 `device_info.chn`，并验证通道数组边界。
4. 本轮只操作一个已验证的 TX 通道和 `stream 0`，但能打开包含两个通道的新设备。
5. CW、Realtime、Playback 都通过 `tx_stream + tx_config_stream()` 配置。
6. BUS trigger 使用 `channel_bus_trigger(primaryTx, 1U)`。
7. 预检不再调用已删除的 `tx_test_*`，也不声称能力范围等价于完整设备校验。
8. GNSS、供电、温度、风扇、电源均遵守新签名和结构体。
9. Trigger Out、GPIO、旧版本字段没有无依据的伪映射。
10. Windows x86_64 的头文件和二进制同代，并完成链接和实机冒烟。

## 2026-08-10 实施结果

- USB 打开按最新枚举 `device_info.chn` 传入通道数；ETH 固定传 `chs = 1`。打开后只缓存第一个 `CHANNELTYPE_TX && max_streams > 0` 的通道，所有 TX 业务固定使用该通道和 `stream 0`。
- CW、Realtime、单/多波形 Playback 已统一迁移到零初始化的 `tx_stream + tx_config_stream()`；BUS trigger 使用 `channel_bus_trigger(primaryTx, 1U)`。
- FScan、LScan 已迁移，前端 preview 直接按 `channel_tx_query_capabilities()` 的频率、步进、功率和 dwell 范围钳位；配置后若仍收到 `H2_WARNING_PARAMOUTRANGE`，写入 `[H2ApiContract]` 日志并按 warning-success 继续。
- 初始 Phase 3 曾让 MScan 启动、配置、查询和 preview 返回 unavailable；该状态已由文末的 Sweep MScan/ListMode 延伸实施取代。
- Trigger Out 控件第一阶段隐藏，配置/写回不再调用旧通道级接口；GPIO 查询与配置统一返回 unsupported，现有动态菜单因此保持隐藏。
- 打开设备时记录 `[H2ApiMetadata]` 和 `[H2ApiCapabilities]`：API 版本、型号字符串、版本字符串数组、接口字符串数组、选件、通道和完整 TX 能力范围。旧数值版本字段暂置为 unavailable 值，不猜测字符串数组下标含义。
- 温度使用 domain 0；RF/USB 供电分 rail 查询；GNSS、风扇阈值和电源状态均迁移到新签名；只有 `H2_ERROR_BUS_DISCONNECT` 进入断连回收。
- `cmake --build build/cmake-win-debug --config Debug --target HTRA --parallel 1` 已成功，产物为 `build/cmake-win-debug/plugin-runtime/HTRA.dll`。并行构建曾因现有 MSVC PDB 并发写入失败，改为单任务构建后通过；最终构建没有 H2 迁移编译或链接错误。
- 未执行实机运行；设备打开、实际波形播放、FScan/LScan 与日志字符串语义仍需在硬件上验证。

## 后续验证清单

- [x] `rg` 确认活跃 HTRA 源码不存在旧 H2 符号。
- [x] 检查所有修改文件仍由当前 CMake target 纳入。
- [x] Windows x86_64 增量构建 `HTRA` Debug target。
- [ ] 单通道旧设备：USB 扫描、打开、切换、断开、重连。已经测试通过
- [ ] 双通道新设备：打开成功，只选择第一个有效 TX 通道，不配置其他通道。不要管
- [ ] 手工 ETH：按确认后的 `chs` 契约打开。已经测试通过
- [ ] CW、普通 Playback、多波形 Playback、Realtime Streaming。 测试通过
- [ ] FSCAN/LSCAN/MScan 分别覆盖 CW/Playback/Streaming；确认 MScan ListMode 参数写回。测试通过
- [ ] BUS/External/XPPS 触发与 BUS stream mask。
- [ ] 正警告不误判断连，真实 `H2_ERROR_BUS_DISCONNECT` 正确回收。已经测试过，拔掉网线程序能够进入未连接状态
- [ ] 温度 domain 0、RF/USB 两条供电 rail、GNSS query/config。
- [ ] Fan On/Off/Auto 分别映射负无穷/正无穷/默认阈值。
- [ ] 版本、接口、Trigger Out、GPIO 按选择的降级或新模型表现一致。
- [x] `git diff --check`。

## 2026-08-10 Sweep MScan / ListMode 延伸实施

### 范围

- 只适配 Sweep 业务中的 ListMode/MScan，不修改 `arbmodulation` 的 MScan 路径。
- 恢复 Sweep Type 的 List 选项和现有 `ListModePanel` 页面；沿用已有列表编辑、范围选择、全局/逐点 dwell 逻辑。
- HTRA 设备层迁移到最新 `tx_config_mscan()` / `tx_query_mscan()`，并复用当前主 TX 通道、stream 0、trigger、CW/Playback/Streaming 编排。
- 根据实机日志已经确认的 `hw-v*`、`mcu-v*`、`fpga-v*`、`bus-v*`、`eio-v*` 前缀填充现有设备版本字段；按前缀识别角色，不依赖数组固定下标。
- 不更新 KnowledgeBase，不扩展多通道业务，不恢复 Trigger Out/GPIO。

### 静态结论

- ListMode 内部频率、功率、驻留时间单位分别是 Hz、dBm、s，与 `tx_config_mscan(fc, level, dwell, n)` 一致。
- Global dwell 和 From List dwell 在 `StepSweepPanel::fillCarrierPlanContext()` 中都会展开成逐点 `TxMScanPoint`，设备层无需引入第二套数据模型。
- UI 选择范围按一基下标转换为点数组，顺序与 API 的下标扫描语义一致。
- 唯一额外边界是 API 点数类型为 `int16_t`；空列表或超过 `INT16_MAX` 的列表必须在 preview/配置前拒绝，不能窄化截断。
- 当前 ListMode 参数链已存在，但 List 下拉选项未注册且面板被隐藏；这两处需要恢复，否则设备适配没有可用 UI 入口。

### 实施步骤与成功标准

1. 在 Sweep Type 枚举显示选项中恢复 List，并把现有 `ListModePanel` 加入 `pageListScan` 的布局。
2. 在 `FancyDevice` 中实现 MScan preview、配置、查询、CW/Playback/Streaming 启动和缓存回读；所有 H2 调用使用 `primaryTxChannel()`。
3. Sweep Playback MScan 沿用当前流水线的一份波形，在全部 MScan 频点复用；Streaming MScan 使用 `TX_REALTIME`，普通 Sweep MScan 使用 `TX_CW`。`arbmodulation` 的逐点多波形语义不在本轮范围内。
4. `H2_WARNING_PARAMOUTRANGE` 按与 FScan/LScan 相同的 contract 日志策略处理；正 warning 视为成功。
5. 解析已验证的版本字符串并写入 `HardwareVersion/MFWVersion/FFWVersion/BUSVersion/EIOVersion`，无法识别的字符串保持对应字段为 0 并记录日志。

完成条件：Sweep 的 List UI 能生成 MScan 点表，preview 按 TX capability 钳位逐点参数，CW/Playback/Streaming 路径能下发 `tx_config_mscan()` 并完成触发启动，查询能回填同一批点；设备信息 UI 能显示日志中已确认的 MCU/FPGA/BUS/EIO 版本。验证级别为 `static`，本轮不主动构建或运行。

### 实施结果

- Sweep Type 已恢复 List 选项，现有 `ListModePanel` 已放回 `pageListScan` 布局；没有修改 `arbmodulation`。
- MScan preview 对每个点的 frequency/level/dwell 分别按当前 TX capability 范围钳位并写回；空列表和超过 32767 点直接拒绝。
- `startListSweepLocked()` 已接入 `tx_config_mscan()`，并分别复用 `TX_CW`、单波形 `TX_PLAYBACK` 和 `TX_REALTIME` stream 配置；Trigger Out 仍未恢复。
- `setListSweep()` / `getListSweep()` 已恢复配置与查询，查询使用调用方容量并检查 API 返回点数。
- `version_name[]` 已按 `hw/mcu/fpga/bus/eio` 前缀解析；已验证的 `usb-625MB/s` 接口字符串会恢复为现有 `BusSpeed = 3` 元数据。
- 已执行静态符号检查和 `git diff --check`；未构建、未运行。

## 2026-08-12 Trigger Out 最小恢复（修正版）

本节撤销此前“删除现有通道数组/主 TX 选择并在各扫描路径重复配置 Trigger Out”的结论。

### 实机证据与当前基线

- 前一次实现同时改动了设备打开/通道保存契约，并把 `device_config_trigger_out()` 扩散到 RF、FScan、LScan、MScan、Streaming 和 Playback 路径。
- 用户实机验证发现该版本打开 RF 或 FScan 后设备立即断联；回退这些代码后，当前 RF/FScan 基线恢复正常。
- 现有证据不能把断联单独归因于某一个调用，但足以否定前一次扩大修改面的方案。本轮必须完整保留 `channels[]`、`channelCount`、扫描快照通道数、`m_primaryTxChannelIndex`、`primaryTxChannel()` 以及现有 USB/ETH 打开流程。

### 本轮范围

- 只取消 `DeviceSettingPanel` 中 Trigger Out group 的隐藏状态；现有 Property、CommonDeviceProfile、TxApplyRequest 和 `IDevice::Profile` 链保持不变。
- 只在 `FancyDevice::applyCommonDeviceSettingsLocked()` 的现有 Trigger In 配置之后调用一次新版设备级 `device_config_trigger_out()`；不修改任何 RF、FScan、LScan、MScan、Streaming 或 Playback 专用路径。
- `device_trigger_out.source` 使用现有 `primaryTxChannel()->num`，并只接受头文件定义的 `CHANNEL0 / CHANNEL1`；不再假定主 TX 必然是通道 0。
- `state` 直接映射 `TriggerOutState`；Rising/Falling 分别映射 `negative_pulse = 0/1`。
- 用户已确认产品映射：旧 `TriggerOutAction::Hop` 使用 `recounter = 1`，`TriggerOutAction::Sweep/Scan` 使用 `recounter = 10`。该映射只在现有公共配置点下发，不在扫描路径追加第二次配置。
- 新 API 没有 Trigger Out query。只有整次配置成功后才把本次请求值写回 UI；该值是 write-after 结果，不是硬件回读。
- GPIO 不在本轮范围内。

### 成功标准与验证级别

1. 现有设备打开、通道数组、主 TX 选择和所有 RF/scan/stream 调用保持逐行不变。
2. Trigger Out group 恢复可见，现有 Action/Edge/Output 控件与 property binding 不重做。
3. 公共配置仅新增一次 `device_config_trigger_out()`；失败沿用当前 `handleStatus()` 规则中止本次配置。
4. Trigger Out 来源跟随实际主 TX 通道号，输出使能与脉冲极性能映射到新版结构体。
5. 仅执行静态 diff、符号和 CMake 纳入检查；按用户要求不编译、不运行，由用户完成实机验证。

### 静态实施结果

- `DeviceSettingPanel` 只删除了 Trigger Out group 的隐藏语句，原有三个控件和 property binding 未改。
- `FancyDevice` 只在公共配置链新增一次 `device_config_trigger_out()`；`source` 取当前主 TX 通道号，`state`、正/负脉冲以及 Hop/Sweep 对应的 `recounter = 1/10` 均使用零初始化的新 API 结构体下发。
- writeback 不再强制把 Trigger Output 改回 OFF，而是保留本次成功配置的请求值；没有调用不存在的 query API。
- 静态 diff 确认没有改动设备打开与任何 RF/FScan/LScan/MScan/Streaming/Playback 专用路径；`channels[MAXCHANNELS]` 与 `m_primaryTxChannelIndex` 等现有适配仍保留。
- 两个代码文件仍分别由 Core/HTRA 的现有 CMake target 纳入，`git diff --check` 通过。按用户要求未编译、未运行。
