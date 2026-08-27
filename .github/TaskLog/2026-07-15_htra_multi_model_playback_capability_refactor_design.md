# HTRA 多型号 Playback 能力重构设计

Date: 2026-07-15

## Scope

- 为 HTRA 型号 `132 / 122 / 123 / 150 / 151` 设计统一的设备能力快照，覆盖 Playback 采样率域、板载波形容量和 Streaming 采样率域。
- 设计 `IDevice -> Core -> Business -> Property/UI` 的连接、断开、切换和热插拔刷新顺序。
- 盘点 ARB、Quick Waveform、Analog/HTRA 生成型 Playback、Digital、DSSS、OFDM、Ramp、AWGN 等现有硬编码入口，规划分阶段迁移。
- 先语义回退提交 `4e62cc8` 在 Digital 路径引入的连续 400 MHz 临时适配，并同步当前行为文档。
- 本轮继续采用静态验证，不主动编译、不运行。

## Verification Level

- `static`

## Observations

- 目标扩展型号的真实采样率集合不是连续区间，而是 `[195.3125 kHz, 200 MHz] U {400 MHz}`；
- 当前设备抽象已通过 `FeatureSpec` 暴露枚举能力，但没有数值域/容量能力值对象。
- `FancyDevice::open()` 已在 open 成功后获得最终 `model + hardware_version`，适合作为当前硬编码能力解析入口；未来 API 查询可替换 resolver 的数据源。
- `DeviceRuntimeBridge` 已负责设备 open 后的 `DeviceInfo` 和 FeatureSpec 发布，但 `BusinessManager`、`TxPipelineRuntime` 等也直接监听 `currentDeviceOpenStateChanged`，目前没有能力先于业务启动生效的强顺序保证。
- 当前 `TxProviderExecutionContext` 按值持有 `QVector<int16_t>`，`TxSessionService` 会复制和整向量比较 request，runtime 在设备重开时会重放缓存 request。把上限扩至 996 MiB 后会产生显著复制、比较和旧设备 payload 重放风险。
- ARB、Quick Waveform、Analog Playback、HTRA 各调制器及 `Utils::arbfileutils` 存在多份 `100/125 MiB` 常量；Quick Waveform 甚至同时存在 100 MiB 和 125 MiB 两种口径。
- 当前普通 WAV / IQS-WAV 会先整文件读入，再经 `std::vector<float>`、`std::vector<short>`、`QVector<float>`、`QVector<int16_t>` 多次复制；仅提高阈值不能可靠支持接近 996 MiB 的文件。
- Streaming 的 62.5 MSPS 是独立传输约束，不应由 Playback 的 125/200/400 MSPS 能力推导。

## Inferences

- 能力必须建模为“连续区间 + 离散点”的采样率域，不能继续使用单一 min/max 宏。
- 设备能力应在 open 成功后形成带 UID/revision 的不可变快照，并在 `currentDeviceOpenStateChanged(true)` 驱动业务启动前发布。
- 所有 business 都必须先接收同一能力快照并完成参数收口，UI 再通过 PropertySystem 的 value、基础 metadata 和业务状态信号自然刷新。
- runtime request 必须绑定能力 revision；切换设备后旧 request 即使 payload 本身仍在，也不得直接重放到新设备。
- 996 MiB 支持要求 Playback payload 改为共享不可变 handle/view，文件业务还需元数据探测和单次物化，避免把 996 MiB payload 在 provider、request、runtime 之间按值复制。

## Confirmed Decisions

- 采样率归一化应优先选择满足业务约束的较小合法值，避免设备切换或参数联动时无意跳到 400 MSPS，造成波形数据量突增。
- `PropertyMetadata` 只表达最基础、跨设备稳定的输入约束，例如采样率必须大于 0；不在 UI metadata 中表达 `[195.3125 kHz, 200 MHz] U {400 MHz}` 的空洞。
- 非连续采样率域的合法性、候选选择和参数联动由 business 负责。用户输入或设备能力变化后，business 计算归一化值并永久回写 Property value，UI 不复制一套 domain 判定逻辑。
- 当前回退只移除 `4e62cc8` 的临时连续 400 MHz 语义；后续 Phase D 再把 Digital 和其他生成型 Playback 业务统一接入设备能力域。
- 400 MHz 只响应用户明确请求；业务计算落入空洞时优先缩小关联参数到连续低档，不自动跳到 400 MHz。
- 设备断开或未知 model 时发布新的 unsupported snapshot，禁止下发，但保留当前业务参数和文件路径。
- Playback 参数采用单一有效值模型，不保留 requested/effective 双状态；能力切换触发的业务回写永久覆盖原值，换回高能力设备时不自动恢复旧的高采样率值。

## Success Criteria

- 设计明确给出以下型号表，并统一采用 MiB 字节口径：
  - model 132：Playback `[195.3125 kHz, 125 MHz]`，最大 `125 * 1024 * 1024` Bytes。
  - model 122/123/150/151：Playback `[195.3125 kHz, 200 MHz] U {400 MHz}`，最大 `996 * 1024 * 1024` Bytes。
  - 当前所有型号 Streaming：`[195.3125 kHz, 62.5 MHz]`。
- 能力模型提供 `contains / highestAtOrBelow / lowestAtOrAbove` 等无歧义操作，400 MHz 只作为精确单点参与判定。
- 明确设备 open/switch/disconnect 的能力发布顺序、business 参数归一化顺序、UI 刷新方式和异步生成结果反陈旧策略。
- 明确文件超过当前设备容量时不静默下发、不清空用户选择；保留文件意图、释放大 payload、标记当前设备不兼容，并在换回兼容设备后可重新准备。
- 明确所有 Playback 入口的迁移清单，不留下新的业务代码直接读取全局 `DATA_SAMPLE_RATE_MAX` 或 `MAXDOWNLOADSIZE`。
- 明确 996 MiB payload 的零/少复制边界和 runtime capability revision 校验。
- 当前代码恢复为 Digital 与通用 125 MHz 采样率边界一致，且保留提交之后的频谱、功率和预览等无关改进。
- 文档明确 PropertyMetadata 不表达采样率空洞，非连续能力由业务层归一化并回写；默认候选策略避免自动抬升到 400 MSPS。
- 形成可按阶段提交和验证的实施顺序，每阶段均可独立静态审查，最终包含 Debug build 与多型号热插拔运行验收建议。

## Planned Phases

0. 已完成：语义回退 `4e62cc8` 的连续 400 MHz 特例并同步当前行为文档。
1. Phase A 已完成：引入 Core 能力值对象、HTRA model resolver、IDevice 能力接口和 DeviceManager current snapshot。
2. Phase B 已完成：建立同步分发、late registration 补发、request UID/revision、runtime 最终校验并移除 cached reapply。
3. Phase C：迁移共享 payload handle、ARB 与 Quick Waveform 的文件探测、容量校验和少复制物化。
4. Phase D：迁移所有生成型 Playback 的参数约束、估算、裁剪和 Property value/status 永久回写。
5. Phase E：保持 Streaming 独立 62.5 MSPS 域，并完成断开、重连、132 与扩展型号互换回归。

## Deliverables

- `src/plugins/analog/packing.h`
- `src/plugins/analog/packing.cpp`
- `src/plugins/analog/digitalmodulator.cpp`
- `src/plugins/core/devicecapabilities.h/.cpp`
- `src/plugins/core/idevice.*`
- `src/plugins/core/devicemanager*`
- `src/plugins/core/ibusiness.*`
- `src/plugins/core/businessmanager*`
- `src/plugins/core/txpipelinestate.h`
- `src/plugins/core/txsessionservice.*`
- `src/plugins/core/txpipelineruntime.*`
- `src/plugins/htra/htradevicecapabilityresolver.h/.cpp`
- `src/plugins/htra/fancydevice.*`
- `src/libs/utils/constants.h`
- `.github/KnowledgeBase/Waveform_Parameters_Constraints.md`
- `.github/KnowledgeBase/htra_multi_model_playback_capability_refactor.md`
- `.github/KnowledgeBase/device_open_ui_config_flow.md`
- `.github/KnowledgeBase/tx_execution_context_phase1_and_provider_migration.md`
- `.github/KnowledgeBase/ui_independent_runtime_and_minibar_design.md`
- `.github/KnowledgeBase/Index.md` 新增索引项

## Completed In This Step

- 已删除 `Analog::DIGITAL_SAMPLE_RATE_MAX`，Digital/FSK/sps 恢复使用通用 125 MHz 采样率校验、钳位和预算。
- 未回退 CMake 版本，也未改动后续加入的频谱、RMS power metrics、preview 等无关逻辑。
- 已同步当前行为文档和目标设计文档，明确小采样率优先以及 PropertyMetadata 简化原则。
- Phase A 已完成：能力域、125/996 MiB model resolver、IDevice/FancyDevice 能力接口、DeviceManager current snapshot/revision 和显式 62.5 MSPS Streaming 上限已落地。
- Phase B 已完成：IBusiness hook、BusinessManager 同步 reconcile/late registration、Tx request UID/revision、TxSession capability gate 和 runtime 最终校验已落地。
- capability-ready 在 legacy business 的 open-start 阶段之后发出，TxSession 才重建 request，避免同一次设备切换导致 legacy business 双启动。
- cached `reapply()` 已删除；断开、重连或换机必须生成新 request。

## Static Verification

- `packing.cpp` 与 `digitalmodulator.cpp` 均确认包含在 `src/plugins/analog/CMakeLists.txt`。
- `src/` 中已无 `DIGITAL_SAMPLE_RATE_MAX`、`checkDigitalSampleRate` 或 `clampDigitalSampleRate` 残留。
- `Waveform_Parameters_Constraints.md` 中已无 Digital 400 MHz 当前行为描述。
- Core/HTRA CMake 已包含新增能力值对象与 resolver 源文件。
- HTRA resolver 已静态核对 model `132 / 122 / 123 / 150 / 151`、125/996 MiB、200 MHz 连续上限、400 MHz 单点和 62.5 MSPS Streaming 上限。
- Core 源码已无 `reapply()`/cached reopen 重放入口。
- `git diff --check` 通过；按本任务 verification level 未编译、未运行。
- 当前仓库没有 first-party test target；`SampleRateDomain` 可执行单元测试仍按 KnowledgeBase 11.1 的矩阵留待后续测试基础设施或 Debug build 验证。

## Build Regression Follow-up

- 2026-07-15 Debug 编译发现 `constants.cpp` 无法识别 `STREAMING_SAMPLE_RATE_MAX`。
- 根因：新增 hunk 在无 BOM UTF-8 文件中形成“中文注释 + LF”组合，而当前 MSVC 工程按代码页 936 解析并报告 C4819；预处理器误解析紧随注释的首个声明。该问题先吞掉 `STREAMING_SAMPLE_RATE_MAX`，随后完整构建还暴露了同类的 `DeviceManager::currentDevice()` 声明误解析。
- 修复：把两个受影响 hunk 中紧邻声明的中文注释改为纯 ASCII；常量值、接口和业务语义均未改变。
- 验证：`cmake --build build/cmake-win-debug --config Debug --target Utils`、`--target Core` 以及完整 `cmake --build build/cmake-win-debug --config Debug` 均已成功。

## Phase C Implementation Update (2026-07-16)

### Scope

- 将 `TxProviderExecutionContext` 中按值保存的 `QVector<int16_t>` 改为不可变 payload handle/view；request、session 和 runtime 复制时只复制轻量句柄与身份信息。
- Quick Waveform 的完整波形只使用 `QVector<int16_t>`（或外部只读指针所有权），删除完整尺寸 `QVector<float>` 中间态。
- ARB 与 Quick Waveform 共用文件 header probe、设备容量校验和最终 payload 物化路径，移除业务内重复的 100/125 MiB 下发上限。
- 提供外部波形指针 + owner/deleter 的 payload 接口，为合作方 Digital 后续直接移交生成结果做准备；本阶段不迁移 Digital/DSSS/OFDM 等生成型业务参数约束，仍归 Phase D。

### Confirmed Memory Contract

- 可以假设单次 `996 * 1024 * 1024` Bytes 的连续分配能够成功，但绝不允许两个同级大 payload 同时驻留。
- `tx_download_waveform()` 是同步消费边界；已确认 SDK 内部不会再完整复制一份波形，只申请很小的分块传输内存。
- 大 payload 在业务、request、runtime 之间只能共享同一底层存储；下载返回后清除业务与运行时的强引用，使底层连续内存立即具备释放条件。
- Core 对超过 legacy 125 MiB 的 payload 使用进程内独占租约：旧 payload 未析构前，新大 payload 物化必须失败，而不是尝试第二次大分配。
- payload 发布后只暴露 `const int16_t *`；任何裁剪、补齐、缩放或转换都必须在唯一可写的 builder 阶段完成，发布后不得触发 `QVector` detach。

### Success Criteria

- Core request equality 不再逐字比较波形数据，而是比较 payload identity/view；缓存 request 不保留完整 payload storage。
- Quick Waveform 源码中不存在用于容纳完整波形的 `QVector<float>`；普通 WAV/IQS-WAV 直接填充最终 `int16_t` payload。
- ARB/Quick 文件大小校验使用当前设备 `maxPlaybackPayloadBytes`，132 为 125 MiB，122/123/150/151 为 996 MiB。
- 任意时刻最多存在一个超过 125 MiB 的 Core-owned/adopted payload；外部 owner 也遵守相同租约。
- HTRA adapter 是唯一为兼容 vendor API 而执行 `const_cast<int16_t *>` 的位置；Core/runtime 全程只读。
- 同步下载成功或失败返回后，session/runtime 只保留 payload ID、word count 等轻量描述，完整存储不因 request 缓存继续驻留。
- 静态检查通过；实现过程中已使用现有 `build/cmake-win-debug` 完成受影响的 `QuickWaveform`、`HTRA` Debug 目标验证。完整 Debug build 按用户要求不再执行，由用户自行编译与硬件验收。

### Verification Level

- `debug-build`

### Implemented Result

- Core 新增 `PlaybackPayload` / `PlaybackPayloadBuilder`：request、session 与 runtime 只复制 payload identity/view 句柄，不逐字比较或复制完整 IQ 数据。
- 超过 125 MiB 的 Core-owned/adopted payload 使用进程级独占租约；旧的大 payload 未释放时，第二次同级分配会直接失败。
- Quick Waveform 与 ARB Ordinary/IQS WAV 已共用 header probe、容量校验和直接 `int16_t` 读取路径；最终 payload 一次定长分配并原地完成缩放、截取、补零/补齐。
- Quick Waveform 已删除完整波形 `QVector<float>` 路径；ARB Ordinary/IQS 不再保留“源 short + float 中间态 + 最终 short”三份完整数据。
- `downloadDataSequence()` 在 Core 侧改为只读指针；仅 HTRA vendor adapter 为兼容 SDK 签名执行 `const_cast`。
- `tx_download_waveform()` 同步返回后，无论成功或失败，runtime 都释放共享 host storage；成功时仅保留 payload identity、长度、设备 UID 与 capability revision，用于复用已驻留设备的波形。
- Quick/ARB 在未选中、inactive 或设备能力变化时释放已物化 host payload；保留文件路径和轻量元数据，重新选中后按最新能力懒加载。
- Programmed ARB 仍保持 legacy 多段解析/容量口径，尚未接入统一 runtime；本阶段没有将其扩展到 996 MiB，避免在执行模型未明确前引入第二套大内存路径。

### Verification Result

- 静态搜索确认 Quick Waveform 中不存在完整波形 `QVector<float>`，旧的 100/125 MiB 文件硬编码已从 Quick/ARB Ordinary/IQS 活跃路径移除。
- `QuickWaveform` Debug 目标编译通过。
- `HTRA` Debug 目标编译通过。
- 根据用户 2026-07-16 的要求，未执行完整编译；后续完整构建与设备下载/切换验证由用户执行。

## ARB File Capacity Feedback Follow-up (2026-07-16)

### Observed Problem

- Ordinary/IQS WAV 在 `handleFile()` 同步加载阶段只读取 header 并写入文件属性，没有按当前设备 `maxWaveformBytes` 拒绝超限 payload。
- 真正的容量校验延迟到 worker 的 `buildFilePlaybackPayload()`；失败后只写入 `m_lastError` 和 `status=0`。
- `ArbPanel::onDataStatusChanged()` 当前不消费异步错误；Load 按钮又只检查文件属性是否为空，因此替换已有文件时尤其无法向用户提示超限。
- StatusBar 的 throughput 在 `tx_download_waveform()` 返回后才累计/刷新，不能作为超限校验或下载是否开始的可靠实时判据。

### Implementation Boundary

- Ordinary WAV 使用 WAV `data` chunk 字节数，IQS-WAV 使用去除 TriggerRecord padding 后的 `validDataBytes`，与当前设备 `PlaybackCapabilities::maxWaveformBytes` 同步比较。
- 超限文件在同步 `handleFile()` 阶段直接拒绝，不进入 payload 分配、文件全量读取或设备下载；原有已加载文件保持不变。
- `ArbPanel::loadFile()` 通过“请求路径是否成为最终属性值”判断本次替换是否被接受，确保从空状态加载和替换已有文件两种场景都会提示用户。
- worker/runtime 中已有容量校验继续保留为二次防线。

### Success Criteria

- 132 设备对超过 125 MiB 的 Ordinary/IQS 有效 payload 立即提示超限。
- 122/123/150/151 设备对超过 996 MiB 的 Ordinary/IQS 有效 payload 立即提示超限。
- 被拒绝时不分配大 payload、不读取完整 IQ 数据、不调用 `tx_download_waveform()`。
- 替换已有合法文件时，如果新文件超限，旧文件和业务状态保持不变，同时仍出现超限提示。
- 仅执行静态搜索和 `git diff --check`；按用户要求不编译。

### Verification Result

- 已确认同步拒绝路径同时覆盖 Ordinary WAV `dataSize` 与 IQS-WAV `validDataBytes`，并直接使用当前 `PlaybackCapabilities::maxWaveformBytes`。
- 已确认超限 return 位于 `PlaybackPayloadBuilder::create()` 和 `readPlaybackWavIqData()` 之前，不会进行大内存分配或完整文件读取。
- 已确认面板以本次请求路径和最终属性路径是否一致判断接受结果，能够覆盖“空状态首次加载”和“替换已有合法文件”两种提示场景。
- worker 中 `info.validDataBytes` 校验及 runtime 中最终 payload bytes 校验仍保留。
- `git diff --check` 通过；按用户要求未编译、未运行。



## Device-switch Popup Policy And Digital Capability Migration (2026-07-17)

### Confirmed Interaction Policy

- 设备切换后的能力收口以业务值永久回写为主，避免连续弹窗。
- 唯一的设备切换提示是：ARB Playback 已加载文件因新设备容量不足而被自动卸载。该场景继续关闭 Enabled、清空文件与相关参数，并弹出一次容量提示。
- ARB Period 补零导致的参数复位、Digital 整组默认复位以及其他业务的能力归一化均静默执行，不弹窗。
- ARB 用户主动加载超限文件时同步拒绝，但不弹窗；文件格式等普通加载失败也不新增设备切换类提示。
- Digital 正常参数编辑或下发时已有的大波形确认流程不属于设备切换提示，继续保留，但判断阈值必须来自当前设备 `maxWaveformBytes`。

### Digital Scope

- Digital 普通调制采样率按 `Rb * SPS` 计算；FSK 继续使用其现有特殊采样率规则。SPS 枚举弹出时将现有算法/API 可用项与当前设备 Playback 采样率域取交集。
- model 132 使用 `[195.3125 kHz, 125 MHz]` 与 `125 MiB`；model 122/123/150/151 使用 `[195.3125 kHz, 200 MHz] U {400 MHz}` 与 `996 MiB`。400 MHz 必须作为精确单点判断，不能把 200~400 MHz 空洞当作连续可用范围。
- Digital 的“大波形”估算、默认裁剪、已有裁剪数据提示和实际生成上限统一使用当前设备容量，不再固定为 125 MiB。
- 收到可用新设备能力时，如果当前 Digital 参数对应的采样率不在新设备采样率域内，则关闭 Enabled，并把整组 Digital 参数静默重置为默认配置；不逐项钳位，也不弹窗。
- 临时断连或 unsupported capability 不清空 Digital 参数；只在新设备可用能力确定后执行上述检查。

### Success Criteria

- ARB Period 因设备降级而复位时仍关闭 Enabled并永久回写安全参数，但不显示 MessageDialog；只有已加载文件被容量降级卸载时显示一次容量提示。
- 固定 Rb 时，Digital SPS 菜单只允许会产生当前设备合法采样率的选项；普通调制按 `Rb * SPS`，FSK 按现有特殊规则判断。
- 扩展型号允许合法的 200 MHz 连续档和精确 400 MHz SPS 组合，禁用落入 200~400 MHz 空洞的组合；132 禁用所有高于 125 MHz 的组合。
- 扩展型号上的 Digital 大波形阈值和实际裁剪上限为 996 MiB，132 为 125 MiB；切换后使用新容量重新生成/裁剪，不保留旧容量语义。
- 从扩展型号切换到 132 时，若当前 Digital 采样率越界，则整组参数回到默认配置、Enabled 关闭且无弹窗；合法参数不被无故重置。
- 验证级别保持 `static`：检查 CMake inclusion、信号/能力传播、常量残留和 `git diff --check`；不执行编译或运行。

### Implemented Result

- ARB 主动加载超限/无效文件和 Period 容量复位均改为静默处理；Period 复位仍关闭 Enabled、保留文件并永久回写安全参数。只有 `fileRejectedByDeviceCapacity`（可用新设备确认已加载文件本体超限并自动卸载）继续显示容量提示。
- `DigitalModulation::applyDeviceCapabilities()` 已消费 current snapshot：临时 unavailable 不复位；可用新能力确认当前采样率越界时整组 reset 默认配置并关闭 Enabled，全程不弹窗。
- Digital SPS options 已按 current `SampleRateDomain` 动态求交集：普通调制使用 `Rb * SPS`，FSK 使用 `max(Rb * SPS, 4 * (Rb + MaxDF))`；200~400 MHz 空洞禁用，400 MHz 只接受精确命中。
- Digital 普通编辑落入空洞时优先回写连续低档，FSK 普通编辑优先同比缩小 `Rb / MaxDF` 到连续低档；设备切换越界不走复杂逐项钳位，直接整组 reset。
- PN/SPS 大波形判断、已有 trimmed cache 判断和生成阶段 `SymbolLength` 上限统一使用当前 `maxWaveformBytes`；125/996 MiB 的包含边界语义为 `estimatedBytes <= maxWaveformBytes`。
- Digital runtime 路径不再创建 full-size `QVector<float>` 或第二份 `QVector<int16_t>`：合作方原始指针直接由 immutable `PlaybackPayload` 接管，Preview 只复制小片段，Save IQ 完整缓存直接读取只读指针。
- Core 新增 move-only `PlaybackPayloadReservation`；Digital Playback 与完整 Save IQ 在调用合作方生成 API 前按预计字节数取得大 payload 租约，租约冲突时不启动生成，避免先出现第二份 996 MiB 再在生成/adoption 阶段失败。
- 同步下载后的 `releaseStorage()` 会触发合作方 `GenSignalObjRelease()`；payload identity/count 仍可用于同设备参数-only reapply。

### Static Verification Result

- 确认 `digitalmodulation.cpp`、`digitalmodulator.cpp`、`arbpanel.cpp` 和既有 `playbackpayload.*` 均包含在对应 CMake target。
- Digital 活跃下发路径中已无 `digitalModulator->data()`、`iqData()`、`m_complex` 或 full-size QVector copy；仅 Preview 保留有界 `QVector<int16_t>`。
- Digital 代码中无 model/125/200/400/996 私有表，设备差异只来自 `PlaybackCapabilities`；`MAXDOWNLOADSIZE` 仅保留为尚未收到可用能力前的 legacy fallback。
- ARB 面板只有热切换文件卸载路径保留 `MessageDialog`；Period 复位信号仍存在，但 slot 只关闭使能，不显示提示。
- `git diff --check` 通过；按本任务 `static` 验证级别未编译、未运行。

## DSSS / OFDM Capability And Large-Payload Migration (2026-07-17)

### Scope

- DSSS 按当前设备 Playback 采样率域和容量解析参数；可用设备能力切换后若当前整组参数不合法，则关闭 Enabled 并静默回写 DSSS 默认配置，不逐项钳位、不弹窗。
- DSSS 的 SPS 可选项按 `Rb * SPS * (2^code - 1)` 与当前设备采样率域求交集；生成长度和预计字节数使用当前 `maxWaveformBytes`。
- DSSS 在调用合作方生成接口前取得 `PlaybackPayloadReservation`，并把合作方 `short *` 直接交给 immutable `PlaybackPayload`，删除 full-size `QVector<int16_t>` 复制。
- OFDM 在业务层按当前设备 `maxWaveformBytes` 预估 `FFTSize / GuardInterval / symbolCount` 的最终字节数；参数编辑或设备能力变化时永久回写安全参数，禁止把超容量组合送入生成接口。
- OFDM Playback 与 Save IQ 共用同一容量门槛；完整保存不提供超容量生成/截断路径。合作方生成前取得大内存租约，结果指针直接交给 payload 或在租约持有期内直接写文件。
- 延续设备切换弹窗策略：DSSS/OFDM 的参数复位和容量收口均静默完成，不新增 MessageDialog。

### Success Criteria

- model 132 的 DSSS/OFDM 使用 `[195.3125 kHz, 125 MHz]` 和 125 MiB；扩展型号使用 `[195.3125 kHz, 200 MHz] U {400 MHz}` 和 996 MiB，业务源码不复制 model 表。
- DSSS SPS 菜单只提供当前参数和设备域下合法的项目；设备切换造成采样率越界时整组恢复默认配置、Enabled 关闭且无弹窗。
- DSSS 生成前按预计字节申请租约，合作方波形不再 `memcpy` 到第二个 full-size QVector；下载结束后由 payload release callback 调用 `GenSignalObjRelease()`。
- OFDM 任一生成或完整保存入口在调用 `GenerateOFDMWaveform()` 前完成溢出安全的字节预估并确认不超过当前设备容量；超限时不生成、不保存、不依赖 runtime trim。
- OFDM 参数收口优先降低 `symbolCount`，必要时恢复默认配置；Property metadata 不表达设备动态上限，业务值回写为唯一有效值。
- OFDM 生成前取得租约并直接接管合作方指针；DSSS/OFDM 的 metrics 和 preview 只读 payload，禁止产生第二份完整 IQ 容器。
- 保持 `static` 验证：确认 CMake inclusion、生成 API 前置门禁、租约生命周期、旧 full-size memcpy/QVector 路径消失并运行 `git diff --check`；不主动编译或运行。

### Implemented Result

- DSSS 已消费 current Playback capability：SPS 列表按 `Rb * SPS * (2^code - 1)` 与 current sample-rate domain 动态求交；设备切换导致当前组合越界时，关闭 Enabled 并静默整组 reset，合法组合则按新容量重新生成。
- DSSS 的 `symbolLength` 和预计字节数按 current `maxWaveformBytes` 选择；生成前取得 `PlaybackPayloadReservation`，合作方 `short *` 由 immutable `PlaybackPayload` 直接接管，释放回调统一调用 `GenSignalObjRelease()`。
- OFDM 新增溢出安全的保守估算 `ceil(FFTSize * (1 + GuardInterval / 100)) * symbolCount * 4`；业务归一化会按 current capacity 永久降低 `symbolCount`，容量不足以容纳最小组合时才恢复默认配置。
- OFDM Playback 和 Save IQ 在进入 `GenerateOFDMWaveform()` 前使用同一估算与容量门禁；超限组合不生成、不保存，也不再走确认后 trim。设备切换只因采样率越界时整组 reset，只因容量变小时降低 `symbolCount` 并关闭 Enabled，全程无弹窗。
- DSSS/OFDM 下发 execution context 直接引用 immutable payload；完整保存直接从 payload 指针写文件。若同步下载后 host storage 已释放，保存会在相同 capability 门禁和租约下重新生成一次，结束后立即释放，不创建 full-size `QVector` 或 `QByteArray` 副本。

### Static Verification Result

- 已确认 DSSS/OFDM modulator、business 与 `packing.*` 均包含在 Analog/HTRA 当前 CMake target 中。
- 已确认两个合作方生成 API 的调用前均存在 current capability 字节检查和 `PlaybackPayloadReservation::acquire()`；生成结果仅在短波形最小下载长度补齐时使用小型 builder，大波形路径不再执行 full-size `memcpy`。
- 已确认 DSSS/OFDM business 不新增 `MessageDialog`，设备切换参数 reset/收口只通过 property signal 永久回写。
- 已确认 Save IQ 只建立小型 WAV header/profile chunk，波形数据直接从只读指针写入；旧 `m_complex`、`iqData()` 和完整 `QVector<float>` 转换路径已删除。
- `git diff --check` 通过（仅有仓库既有的 LF/CRLF 工作区提示）；按本任务 `static` 验证级别未编译、未运行。


## HTRA Local Generator Algorithm-Preservation Follow-up (2026-07-17)

### Confirmed Principle

- 生成型 Playback 的内存优化只删除对最终结果没有影响的完整副本，例如直接写唯一 `PlaybackPayloadBuilder`、原地 FFT、只保留有界 preview。
- 不通过多次重新执行完整波形合成来换取更低工作区占用；算法的计算次数、频谱合并顺序和最终量化语义优先保持稳定。
- immutable payload、current capability/capacity 门禁、生成前大内存租约、下载后释放 storage 等 Phase C/D 基础设施继续保留。

### Scope And Evidence

- Multitone 非 2 次幂路径是本轮迁移中唯一新增的“完整三遍振荡器合成”：第一遍统计均值、第二遍统计峰值/RMS、第三遍量化写入。它需要恢复为一次合成。
- Multitone 仍可避免旧实现同时持有完整 frequency-domain 与 time-domain `complex<double>` 数组：先用按 natural bin 排序的小型列表合并冲突 tone，再一次写入唯一完整时域工作区；2 次幂路径继续复用同一个工作区执行原地 IFFT。
- AM/FM/PM/Ramp 当前均为单遍直接写最终 payload，没有同类重复合成，不修改算法。
- AWGN 的两遍处理在本轮迁移前已经存在：第一遍以固定 seed 统计整段滤波噪声的实际 mean/RMS，第二遍重放同一随机序列并量化。把它改成单遍必须增加一份完整浮点噪声或改变归一化结果，因此不属于可无损删除的冗余副本，本次保留。

### Success Criteria

- Multitone 非幂次路径只执行一次 `tone/bin × sampleCount` 合成；后续 DC 修正、metrics 和量化仅线性遍历已经生成的时域工作区。
- 同 bin tone 按旧算法语义先合并幅相，再进入直接合成；natural-bin 顺序、每 1024 点振荡器模长校正、DC target、峰值归一化和 `int16_t` 量化规则保持不变。
- Multitone 幂次与非幂次路径都最多同时持有一个完整 `complex<double>` 工作区和唯一最终 IQ payload，不恢复旧的完整 spectrum + waveform + IQ 三份驻留。
- AM/FM/PM/Ramp/AWGN 不因本次跟进发生波形算法变化；文档不再描述 Multitone 非幂次多遍生成。
- 验证级别保持 `static`：检查 CMake inclusion、生成循环数量、旧三遍 visitor 消失、完整 IQ copy 不回归并执行 `git diff --check`；按用户要求不编译、不运行。

### Implemented And Verified

- 删除 Multitone 非幂次的三次 `visitDirectWaveform()`；新增按 natural bin 稳定排序并原地合并的轻量 bin 列表，同 bin tone 的幅相累加顺序和 collision 计数保持旧语义。
- 非幂次路径恢复为一次直接逆变换求和并写入唯一完整 `complex<double>` 时域工作区；DC 残差、metrics、AutoScale 和 `int16_t` 量化随后只遍历该工作区，不再次执行 tone 合成。
- 2 次幂路径继续把同一个工作区先作为频域数组、再原地 IFFT 为时域数组，没有恢复第二个完整 complex 数组；最终 IQ 仍只存在于唯一 builder/payload。
- 静态审计确认 AM/FM/PM/Ramp 都是单遍生成；AWGN 的固定 seed 两遍统计/量化与迁移前一致，未因本次修改改变。
- 已同步 Multitone 当前算法、参数约束和多型号 Playback 文档；`git diff --check` 通过，仅有既有 LF/CRLF 转换提示。按约定未编译、未运行。

## HTRA Local Generator Capability Boundary Refactor (2026-07-17)

### Scope

- 覆盖 AM、FM、PM、Pulse、AWGN、Ramp 和 Multitone 七类 HTRA 本地波形业务。
- 设备采样率域、最大下载字节数、设备切换后的 reset/回写和 Enabled 策略只存在于 `*Modulation` 业务层及其 resolver。
- `*Modulator` / `MultitoneGenerator` 算法层不再 include、保存或接收 `Core::PlaybackCapabilities`；只接收已解析的 profile、精确 sample rate 和 payload layout。
- 生成前大内存租约、唯一 writable builder、immutable payload、有界 preview 和直接 Save IQ 机制继续保留，不改变已稳定的 DSP 算法和量化顺序。
- Pulse 从固定 125 MiB / 连续 125 MSPS 旧路径迁移到 current Playback capability，同时删除 full-size `QVector<float>` 和第二份 `QVector<int16_t>` 下发副本。

### Design Boundary

- 公共设备无关 `WaveformGenerationPlan` 只表达精确 `sampleRate` 和 `PayloadLayout`；算法可验证 plan 与 profile 的数学一致性，但不能重新选择设备档位。
- AM/FM/PM/Pulse 业务 resolver 先从参数得到固有格点要求，再与 current sample-rate domain/capacity 求交。
- AWGN/Ramp/Multitone 的动态参数收口在业务 resolver 完成，将最终 profile 永久回写后再提交 plan。
- 设备切换导致参数无解时，关闭 Enabled 并静默恢复默认参数；不新增弹窗。

### Success Criteria

- 七类算法层源码中不存在 `PlaybackCapabilities`、`setPlaybackCapabilities()`、`maximumWaveformBytes()` 或设备型号/容量常量。
- 业务层在每次参数编辑、profile 恢复、reset 和 current device capability 更新后，先解析并回写安全 profile，再发布精确 generation plan。
- Pulse 使用 current domain/capacity，且 execution context/Save IQ 直接消费 immutable payload，不再物化完整 IQ 副本。
- 现有 AM/FM/PM/AWGN/Ramp/Multitone 的相位、滤波、统计、FFT/直接合成和量化循环不改变；只替换参数决策与调度边界。
- 验证级别为 `static`：确认 CMake inclusion、算法层能力类型残留、调用链、租约与 payload 生命周期、文档一致性和 `git diff --check`；按用户约定不编译、不运行。

### Implemented Result

- 新增设备无关的 `WaveformGenerationPlan`/`PayloadLayout`：business 提交精确采样率、源点数和最终分配大小；generator 只校验数学一致性并生成，不再选择设备档位。
- AM/FM/PM/Pulse 的周期格点与采样率求交均迁入各自 `*Modulation`；设备切换后若当前参数无解，则关闭 Enabled、静默恢复默认参数并重新解析，不弹窗。
- AWGN 的 Bandwidth、Length，Ramp 的 Span/SweepTime/Period，以及 Multitone 的 Count/FreqSpacing、采样率格点与 sample count 均由 business 按 current domain/capacity 收口并永久回写。
- Pulse 已删除完整 `QVector<float>` 与 `QVector<int16_t>` 返回副本；七类生成器均直接写唯一 `PlaybackPayloadBuilder`，发布 immutable payload，并由 execution context/Save IQ 共享同一 storage。
- Multitone 的 tone lattice、notch、phase、bin mapping、FFT/直接合成、DC 修正、幅度统计和量化流程保持不变；generator 仅新增设备无关的数学 requirements/sample-count helper。
- CMake 已收录公共 payload/generation-plan 头文件与实现；未新增 KnowledgeBase 文档，因此无需变更 `Index.md`。

### Static Verification Result

- 七类算法头/源文件中已无 `PlaybackCapabilities`、`SampleRateDomain`、`setPlaybackCapabilities()`、固定 125/996 MiB 或 400 MHz 设备常量；这些内容只保留在 business/resolver 或公共 capability helper。
- 每类 business 的参数编辑、profile 恢复、reset、面板重新生成和 `applyDeviceCapabilities()` 路径均进入 resolver 后再调用 `setGenerationPlan()`。
- 对 AM/FM/PM/Pulse/AWGN/Ramp/Multitone 的全仓调用点检查未发现绕过 business 直接修改并生成的外部入口；未新增 capability 收缩弹窗。
- `git diff --check` 仅报告仓库既有的 LF/CRLF 转换提示，无空白错误。
- 按用户约定未编译、未运行；硬件行为由用户后续自行验证。
