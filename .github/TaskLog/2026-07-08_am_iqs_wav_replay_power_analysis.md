# AM IQS-WAV 回放功率补偿分析

## Scope

- 理解外部 `IQS-WAV` 格式说明与全局最大功率点查找说明。
- 静态解析 `AMDefaultiq.wav`，确认有效数据包、样点峰值、平均功率与全局最大功率点。
- 对照当前 HTRA AM 与普通 WAV/ARB 回放链路的 IQ 满幅 PEP 口径，给出在 SGS 中回放该 WAV 时的 IQ 补偿建议。

## Verification Level

`static`

不编译、不运行 SGStudio。使用离线文件解析与源码/文档静态阅读完成分析。

## Assumptions And Evidence Plan

- 观察：读取 `.github/KnowledgeBase/Index.md` 后，AM、普通 WAV 回放、RMS/PEP 口径相关文档是本任务的主要背景。
- 观察：读取 `src/plugins/htra/ammodulator.cpp`，以当前 active code 的 AM IQ 生成口径为准。
- 观察：读取外部 IQS-WAV 文档和样本 WAV 文件头/数据区，以文件自身 chunk 和数据包信息为准。
- 推断：若外部 WAV 记录值代表频谱仪 IQ 采样幅度，则回放前需要按当前 SGS PEP 满幅参考，把有效 IQ 的峰值功率归一到目标 PEP。

## Success Criteria

- 明确 `AMDefaultiq.wav` 的 RIFF/data 布局、有效数据包数和有效 IQ 样点范围。
- 明确全局最大功率点的样点位置、I/Q 数值、`I^2+Q^2` 峰值功率。
- 给出 `scale` 或 `IQScale` 建议，并说明它对应当前仓库的 `(32767,32767)` PEP 参考还是分量满幅参考。
- 区分观察值与推断值，避免把频谱仪记录格式假设成 SGStudio 自生成 WAV 格式。

## Status

- 已完成静态分析。
- 2026-07-08 实现更新：`ArbDataGenerator` 新增 `IqsWav` 模式，自动识别 IQS-WAV `prof/trig/data` 结构，只读取 TriggerRecord 标记的有效 IQ 字节；`ArbPanel` 在该模式下将 `AutoScale` 锁定为 On 并禁用 `IQScale`。

## Findings

- `AMDefaultiq.wav` 是 IQS-WAV 扩展 RIFF/WAVE，`fmt` 为 `2ch/16bit/15.625 MS/s`。
- 文件实际 `data` chunk 位于 `25 MiB + 400 = 26,214,800`，IQ payload 从 `26,214,808` 开始。外部说明中的表达式正确，但部分十进制数值少了 400。
- `prof` 中 `PacketDataSize=64960`、`PacketSamples=16240`、`PacketCount=11`；TriggerRecord 显示 10 个完整包加 1 个 80 字节尾包。
- 有效 IQ 样点数为 `162,420`，有效字节数为 `649,680`；第 11 个包只有前 20 个复样点有效，后续 full-slot padding 不应参与分析/回放。
- TriggerRecord 全局最大 `MaxPower_dBm` 位于 packet 3，值约 `-6.085824 dBm`。
- 按实际 IQ 样点扫描，全局最大复功率也位于 packet 3 / packet index 15776 / global IQ pair index 64496，`I=-10459, Q=17`，`I^2+Q^2=109,390,970`。
- 当前 SGS/HTRA 的共享 PEP 参考是 `(32767,32767)`，即 `32767^2 + 32767^2`。原始记录峰值相对该参考为 `-12.929 dB`，有效样点 RMS 相对该参考为 `-15.862 dB`，PAPR 约 `2.932 dB`。
- 当前 HTRA AM active code 生成 `I=Q=iValue`，峰值对齐 `(32767,32767)`；ARB OrdinaryWav 的 AutoScale 只按单个 int16 分量最大值归一，不能自动把旋转 IQ 的复功率峰值对齐到 `(32767,32767)`。

## Known-AM Compensation Calculation

- 对 `AMDefaultiq.wav`，用户已明确其来源是 SGS 以 AM 方式发射后由频谱仪 IQS 记录。因此下面补偿只适用于“外部已知它是 AM”的分析结论，不能由 WAV 文件名或 IQS-WAV header 自动推断。
- 若目标是在 SGS 中按当前 AM 口径回放这个已知 AM 信号并让 PEP 对齐，应先从记录 IQ 取包络：
  `r[n] = sqrt(I[n]^2 + Q[n]^2)`。
- 只使用有效的 `162,420` 个复样点，丢弃第 11 包 20 点之后的 padding。
- 用 `rMax = 10459.013816` 做归一，生成新的 AM 回放 IQ：
  `I'[n] = Q'[n] = round(32767 * r[n] / rMax)`。
- 等价包络缩放因子为 `32767 / 10459.013816 = 3.132895756`，即 `313.289576%`，但这个因子应作用在包络并写到 `I=Q`，不应直接线性乘原始旋转 IQ。
- 若直接打开原始 IQS-WAV 走 ARB AutoScale，当前实现会把最大单分量归一到 32767，峰值仍约为共享 PEP 的 `-3.01 dB`，功率不会完全对齐。

## Detailed ARB Playback Handling Design

### Design Boundary

- IQS-WAV header 只能可靠说明“这是频谱仪 IQS 记录文件”，不能说明被测信号是 AM、Pulse、FM、数字调制或其他波形。
- WAV 文件名也不能参与判断；命名可能随机，不能作为业务语义依据。
- 按当前产品事实收口：只有 AM 和 Pulse 这类原始业务语义为单路实包络、历史上 `Q=0` 的波形，因为 SGS 发射机特殊满幅口径才在 generator 内改成 `I=Q`。
- FM / PM / Ramp / Digital / Multitone / AWGN 等复 IQ 波形不应做 `I=Q` 包络化补偿；它们在 ARB playback 中应继续按原始复 IQ 轨迹播放，否则会破坏相位、星座或扫频轨迹。
- 因为文件本身无法可靠提供业务类型，所以 ARB playback 不应自动做 AM/Pulse 的 `EnvelopeToDiagonal` 补偿。
- 自动行为只能收口为 `IQS-WAV source handling`：识别频谱仪记录格式，正确读取有效 IQ 包并丢弃预留/padding；之后按普通复 IQ 数据处理。

### Source Detection And Parsing

新增一个轻量 helper，建议放在 `src/libs/utils/`，例如 `iqswavreader.{h,cpp}`，不要复用 `Utils::Decoder` 先吞掉 25 MiB `trig` 预留区。

识别条件：

- `RIFF/WAVE`
- `fmt ` chunk 位于 offset 12，当前至少支持 `PCM, 2ch, 16bit`
- `prof` chunk 位于 offset 36，payload size 为 356
- offset 46 魔数为 `8C 22 52 9B`
- offset 50 protocol version 为 `0x0002`
- offset 400 为 `trig`
- `data` chunk 起点按实际布局计算为 `400 + trigChunkLength`，其中 `trigChunkLength = 25 * 1024 * 1024`

解析结果结构建议包含：

```cpp
struct IqsWavPacketInfo {
    quint32 packetIndex = 0;
    quint32 validBytes = 0;
    quint32 maxIndex = 0;
    float maxPowerDbm = 0.0f;
};

struct IqsWavInfo {
    bool valid = false;
    quint32 sampleRate = 0;
    quint16 bitsPerSample = 0;
    quint32 packetSamples = 0;
    quint32 packetDataSize = 0;
    quint64 dataPayloadStart = 0;
    quint64 validDataBytes = 0;
    quint64 validComplexSamples = 0;
    QVector<IqsWavPacketInfo> packets;
};
```

有效样点读取规则：

- 以 `prof` 中的 `PacketDataSize` / `PacketSamples` 作为包槽大小。
- 每包有效字节优先取 TriggerRecord 中的 `InPacketTriggeredDataSize`，要求 `0 < validBytes <= PacketDataSize` 且按 I/Q pair 对齐。
- 如果 TriggerRecord 字段无效，才回退到 data chunk 剩余长度推导。
- 拼接输出时只拷贝每包前 `validBytes`，不要把最后一个包槽的空白 padding 放进 `m_data`。

### ARB Integration

建议在 `ArbFileMode` 中增加一个模式：

```cpp
enum class ArbFileMode {
    None,
    OrdinaryWav,
    ProgrammedArb,
    IqsWav
};
```

`ArbDataGenerator::handleFile()` 中先 probe IQS-WAV：

1. 若 `IqsWavReader::probe(fileName, &info)` 成功：
   - `newMode = ArbFileMode::IqsWav`
   - `samplesInFile = info.validComplexSamples`
   - `loadedSampleRate = clampSampleRate(info.sampleRate)`
   - 保存 `IqsWavInfo` 或保存足够的 packet layout 供 worker 读取。
2. 若不是 IQS-WAV，再走现有 `Utils::Decoder` 普通 WAV / ProgrammedArb 分支。

`ArbModulationOnly::buildPlaybackExecutionContext()` 应把 `IqsWav` 与 `OrdinaryWav` 一样作为单段 Playback payload 参与 core runtime。

`SampleOffset / SamplesToUse / Period` 对 `IqsWav` 仍可编辑，语义基于有效样点数，而不是 WAV data chunk 的包槽样点数。

### Single Safe Playback Behavior

- 只做 IQS-WAV 有效包裁剪。
- 后续继续使用现有 ARB 的 AutoScale / IQScale / slice / period 逻辑。
- 不用 `(32767,32767)` 共享 PEP helper 去强行提升所有复 IQ 波形；否则会把本来准确的复波形过补偿。
- 对未知业务类型的频谱仪记录，若目标是在 SGS `Playback/ARB` 中以同样 `Level/PEP = 0 dBm` 回放，应按 raw-complex ARB 口径做峰值归一：

  ```text
  componentPeak = max(max(|I[n]|), max(|Q[n]|))
  I'[n] = round(32767 * I[n] / componentPeak)
  Q'[n] = round(32767 * Q[n] / componentPeak)
  ```

  这等价于当前 ARB `AutoScale=true` 的行为，但前提是输入数据已经按 IQS-WAV 有效字节裁剪，不能包含尾包 padding。
- 该处理保证的是“未知复 IQ 记录在 ARB playback 的通用满幅口径下 PEP 对齐”，而不是 AM/Pulse 的特殊 `I=Q` 满幅口径。后者必须依赖外部业务类型信息。
- 当前实现对 IQS-WAV 自动强制 `AutoScale=true`，并在前端禁用 AutoScale 开关和 IQScale 编辑。用户要改变发射功率时应调整通用 `Level/PEP`，不要通过 IQScale 改记录幅度。

这是唯一适合自动进入 ARB playback 的安全行为。它会保住 FM / PM / Ramp / Digital / Multitone / AWGN 等复 IQ 记录的正确性；代价是 AM / Pulse 这类本来需要 `I=Q` 特殊满幅补偿的记录，不能仅凭文件自动修正。

### AM/Pulse Compensation Boundary

若产品仍需要把频谱仪记录的 AM/Pulse 文件补偿到 SGS 当前 AM/Pulse 的 `I=Q` PEP 口径，必须满足至少一个外部条件：

- 用户在导入/回放前明确选择“按 AM/Pulse 包络补偿”。
- 上层业务上下文明确知道该文件来自 AM/Pulse 测试，而不是从 WAV header 推断。
- 未来文件格式新增可靠的业务类型 metadata。

没有这些外部条件时，不应自动执行：

```text
r[n] = sqrt(I[n]^2 + Q[n]^2)
I'[n] = Q'[n] = round(32767 * r[n] / max(r))
```

原因：这个变换会丢掉原始复相位轨迹。对其他本来准确的复波形，它不是补偿，而是破坏。

### Power Metrics

- 自动 ARB playback 下，`powerMetrics` 应基于 IQS-WAV 有效包裁剪、现有缩放、slice、period 后的最终 raw-complex IQ 计算。
- 如果未来增加显式 AM/Pulse 包络补偿入口，则该入口的 `powerMetrics` 应基于补偿后的 `I=Q` payload 计算。
- 不应使用 TriggerRecord 的 `MaxPower_dBm` 直接反推 DAC 缩放；该字段是频谱仪/API 的 dBm 标尺，可能叠加仪表校准、参考电平或 `ampOffset` 显示补偿，不等价于 SGS int16 满幅。

### AMDefaultiq.wav Expected Result

在当前样本上，若外部已知它是 AM 并显式执行包络补偿，应得到：

- 有效样点数：`162,420`
- `rMax = sqrt(109,390,970) = 10459.013816`
- 补偿因子：`32767 / rMax = 3.132895756`
- 峰值输出：`I'=32767, Q'=32767`
- RMS 相对 PEP：约 `-2.93 dB`，对应该 AM 包络的实际峰均比。

如果按自动 ARB playback 的安全行为，也就是 raw-complex + 当前 AutoScale：

- 最大单分量会到 32767。
- 复峰值仍约为 `(32767,0)` 口径，离当前 AM/Pulse 的 `(32767,32767)` PEP 口径低约 `3.01 dB`。
- 对未知业务类型而言，这是正确取舍：保留记录的复相位轨迹，并按通用 ARB 口径把该记录的峰值放到 `Level/PEP = 0 dBm`；不能因为该样本事后已知是 AM，就把这个规则推广到所有 IQS-WAV。

## 2026-07-08 Common IQS-WAV Reader Extraction

### Scope Update

- 将频谱仪 IQS-WAV 的 `prof/trig/data` 识别和有效 IQ 字节抽取从 `ArbDataGenerator` 移到公共 `Utils` 层。
- ARB 继续使用同一语义：识别 IQS-WAV 后只读取 TriggerRecord 标记的有效 IQ 字节，AutoScale 强制开启且 IQScale 锁定 100%。
- Streaming 后续也应通过同一公共 API 获取 IQS-WAV 的有效 IQ 字节，避免把 25 MiB `trig` 预留区和末包 padding 当作 waveform 播放。

### Success Criteria Update

- `src/libs/utils/iqswavreader.{h,cpp}` 提供 `probeIqsWav(...)`、`readIqsWavIqBytes(...)`、`readIqsWavIqData(...)`。
- `ArbDataGenerator` 不再包含 IQS-WAV 私有解析器，只依赖 `Utils` 公共 API。
- Streaming 可以复用公共 API；本轮若接入 Streaming，必须保持普通 WAV 路径不变，并且 IQS-WAV 只按有效 IQ payload 参与 replay/stream 组帧。

### Temporary Boundary

- 用户确认 Streaming 接入暂缓，后续单独讨论 Streaming 的数据源、replay cache、连续流组帧与 UI 统计语义。本轮只把 IQS-WAV 解析能力公共化，并让 ARB 使用公共 API。
- 静态检查时发现 `ArbDataGenerator::handleData()` 在 worker 已持有 `m_mutex` 的上下文中调用 `arbFileMode()` 会二次加锁；本轮改为在 `handleData()` 内直接读取已受同一锁保护的 `m_arbFileMode`。

## 2026-07-08 QAM16 IQS-WAV Loop Continuity Analysis

### Scope Update

- 分析 `data/digital.iq.wav` 这个频谱仪 IQS-WAV 记录在 ARB/playback 循环播放时的首尾不连续风险。
- 验证“彻底丢弃最后一包”是否比当前“只丢 padding、保留所有有效包”更接近首尾连续。
- 先只做离线分析和方案判断，不修改 Streaming；如需修改 ARB 循环裁剪策略，另行确认实现边界。

### Success Criteria Update

- 解析 `digital.iq.wav` 的 packet count、每包有效字节、有效复样点数、采样率和 TriggerRecord 峰值信息。
- 计算当前全有效数据、丢最后一包、以及若可行的内部候选切点的首尾跳变指标。
- 给出 QAM16 记录在周期性 playback 中实现首尾连续的可落地策略，区分“丢包裁剪”“搜索循环点”“淡入淡出/交叉淡化”和“源端生成周期波形”的适用性。

### Current Implementation Check

- 当前 `Utils::probeIqsWav(...)` 按 `prof` offset 108 的 MsgPack payload 解析流信息；对 `data/digital.iq.wav` 可得到：
  - `PacketSamples = 16,240`
  - `PacketDataSize = 64,960`
  - `IQSampleRate = 15,625,000`
  - `data` chunk size = `714,560`，因此共有 `11` 个 packet slot。
- 当前 `Utils::readIqsWavIqBytes(...)` 按 packet slot 定位 data，并对每条 TriggerRecord 调用 `effectiveValidBytes(...)`：
  - 前 10 包 `InPacketTriggeredDataSize = 64,960`，完整保留。
  - 第 11 包 `InPacketTriggeredDataSize = 80`，保留其中 20 个 Complex16 样点。
  - 第 11 包剩余 slot padding 不进入 ARB payload。
- 因此当前实现已经是“严格按 TriggerRecord 丢 padding，但保留 TriggerRecord 标记的有效数据”，不是整体丢弃第 11 包。

### Final Playback Policy For This Issue

- IQS-WAV reader 的默认语义保持为：以 TriggerRecord 为权威，只丢弃每包 `InPacketTriggeredDataSize` 之后的 padding，不因为最后一包是残包就整体丢弃。
- 对 `digital.iq.wav`，ARB payload 应包含 `10 * 64,960 + 80 = 649,680` 字节，即 `162,420` 个复样点。
- 第 11 包 20 点虽然来自停止时刻的残包，但它仍然是频谱仪记录的有效 IQ 数据；自动回放路径不应擅自丢弃有效采样。
- 首尾不连续导致的频谱扩散是“随机记录片段被周期播放”的问题，不能通过包头唯一推断出真正的 QAM16 周期终止点。TriggerRecord 只能定义数据有效边界，不能定义调制波形循环边界。
- 若后续要优化频谱扩散，应作为独立的 loop conditioning 功能讨论，例如显式裁剪、搜索近似循环点、交叉淡化或由源端生成周期数字调制波形；这些都不应改变 IQS-WAV reader 的基础有效数据语义。

## 2026-07-08 Repository Skill Update

### Scope Update

- 新增仓库本地 skill，沉淀频谱仪 IQS-WAV / 带魔数字节 WAV 的 SGS playback 分析流程。
- Skill 聚焦 ARB/SGS 回放所需信息：格式识别、ProfileStreamInfo 中的采样率与包槽尺寸、TriggerRecord 中的每包有效字节、data payload 裁剪、AutoScale/PEP 口径和循环边界判断。
- Skill 不覆盖完整频谱仪元数据分析，不展开 DeviceState、GPS、触发边沿数组、仪表 dBm 标尺反推等与 SGS 回放无关的信息。

### Success Criteria Update

- `.github/skills/iqs-wav-playback-analysis/SKILL.md` 描述下次分析此类文件时的最小检查步骤和常见误区。
- 明确禁止把 `prof` 当固定 9 字节字段扫描，禁止把 TriggerRecord 当 32 字节记录扫描。
- 明确 reader 默认语义：严格按 TriggerRecord 保留有效数据，只丢 padding，不自动丢弃最后残包。

## 2026-07-08 Streaming IQS-WAV Large File Design

### Scope Update

- 分析 Streaming 模式如何把频谱仪 IQS-WAV 当作“大记录文件回放”输入，同时仍允许它与普通 WAV 按列表顺序正常串接。
- 分析 IQS-WAV 小文件 replay cache 与大文件逐帧下发的边界，尤其是 2GB IQS-WAV 不能整体放入缓存时的效率风险。
- 明确 IQS-WAV Streaming 的幅度语义：先对记录文件有效 IQ 做 AutoScale，再叠加 panel 上的 `Streaming_IQScale`。
- 本轮只做静态分析和方案沉淀，不修改 Streaming 代码，不编译，不运行设备。

### Observations

- 当前 `StreamingDataGenerator` 实际帧长是 `m_readLen = 8,000,000 bytes`，即 `2,000,000` 个 Complex16 IQ 样点，不是 8M 个 complex 点。
- 当前小文件 replay cache 的条件是所有文件 `dataSize` 总和 `<= m_readLen` 且 4 字节对齐，然后把数据预展开成至少一个 8MB replay frame 循环入队。
- 当前大文件路径每帧会经历 `QFile::read -> array -> IQ -> QByteArray iqData -> DataSender`，至少两次大块内存复制；`IQScale != 100%` 时还会逐 int16 缩放。
- 当前 Streaming 对每个文件直接使用 `Utils::WavHeader`。对 IQS-WAV 而言，这会把 25 MiB `trig` chunk 当成普通 other chunk 读入内存，并且会把 data chunk 的 packet slot padding 当作可播放数据；后续必须先 `probeIqsWav`，成功后进入 IQS-WAV 专用抽取路径。
- 当前 Streaming 只有一个会话级 `Streaming_SampleRate`，`tx_config_stream()` 也按会话级采样率配置；播放采样率以 panel 为准，文件 header / IQS-WAV `prof` 中的 sample rate 只应作为文件来源 metadata 或显示参考，不应自动改写 panel 采样率。
- `Utils::readIqsWavIqBytes(...)` 适合 ARB 的小文件抽取，但它要求有效字节数能放入一个 `QByteArray`，不适合 2GB 级别的 Streaming 主路径。
- IQS-WAV 的 `prof` / `trig` 已经提供快速回放所需的关键 metadata：`IQSampleRate`、`PacketCount`、`StreamDataSize`、`PacketSamples`、`PacketDataSize`、每包 `InPacketTriggeredDataSize`、`MaxPower_dBm` 和 `MaxIndex`。
- `MaxPower_dBm` / `MaxIndex` 可以快速定位全局最大复功率候选样点，从而避免为了 AutoScale 在启动阶段全量扫描 2GB IQ payload；但它更适合作为“复幅度峰值”依据，不等价于全文件逐 int16 分量扫描的严格 component peak。

### Industry Context

- RF 行业惯例中，ARB/Playback 适合“先下载到仪器波形内存再循环/序列播放”的短波形或可分段波形；Streaming 适合超过仪器内存的长时 I/Q 记录回放。
- R&S 对大波形 I/Q streaming 的应用说明明确把“分钟或小时级场景、TB 级 I/Q 文件、从 PC 经以太网流向 VSG”作为典型动机。
- Keysight 的 VSG 说明把 waveform memory 作为 ARB/Playback 的前提，波形需要先进入仪器 volatile memory 后才能播放或加入序列。
- NI 的 RF record/playback 资料也把“一个文件或多个文件记录，再按通道回放”作为记录回放系统的典型形态。

### Design Decision

- Streaming 保留两条输入语义，并允许二者在 file list 中按顺序串接：
  - `OrdinaryWav`：现有普通 SGS WAV streaming 路径，按 WAV `data` chunk 直接作为 Complex16 IQ 源。
  - `IqsWavRecord`：频谱仪 IQS 记录文件回放路径，利用 `prof/trig/data` metadata 快速定位有效 IQ、峰值样点和包槽布局。
- 若普通 WAV 与 IQS-WAV 穿插，系统正常按列表顺序拼接，不拒绝、不插零、不自动淡化；这种混合的信号语义由用户负责。
- 后续实现可以按 source 粒度处理 gain：
  - 普通 WAV：只应用 panel `Streaming_IQScale`。
  - IQS-WAV：先应用 IQS-WAV AutoScale，再应用 panel `Streaming_IQScale`。
  - 若一个列表中有多个 IQS-WAV，推荐对列表中的 IQS-WAV source 统一寻找全局最大 TriggerRecord 峰值并建立一个共同 `iqsAutoScaleGain`，避免同一次记录的多个 part 之间幅度跳变；普通 WAV 不参与这个 IQS-WAV AutoScale。
- IQS-WAV Streaming 不自动改 panel 的 `Streaming_SampleRate`，不自动重采样。文件中的 `IQSampleRate` 只作为记录 metadata 和显示参考，真正播放采样率始终以 panel 为准。
- IQS-WAV Streaming 默认做 raw-complex AutoScale，但不做 AM/Pulse `I=Q` 包络补偿。推荐默认顺序：

  ```text
  IQS-WAV valid IQ bytes
    -> head/trig assisted AutoScale gain
    -> panel Streaming_IQScale
    -> frame buffer
    -> tx_send_stream()
  ```

- `Streaming_IQScale` 在 IQS-WAV 中应生效，但它是 AutoScale 之后的用户线性缩放。当前 UI 上限为 100% 时，它主要用于把 AutoScale 后的记录幅度向下调；不承担频谱仪 dBm 校准、AM/Pulse PEP 补偿或每文件归一化职责。
- 这里的 AutoScale 与 ARB 的安全目标一致：让频谱仪记录在进入 SGS 前先获得合理的 DAC 幅度利用率；差异是 Streaming 为大文件服务，必须优先使用 `prof/trig` 元信息快速建立缩放因子，不能为了启动回放把 2GB 数据全量读入内存。

### IQS-WAV Fast AutoScale

- 首选快速路径：
  - 解析 `prof` 得到 `PacketDataSize`、`PacketSamples`、`PacketCount`、`StreamDataSize`。
  - 解析 `trig` 得到每包 `InPacketTriggeredDataSize`，构造有效数据 segment table。
  - 同步扫描 TriggerRecord 的 `MaxPower_dBm`，找到全局最大复功率所在 packet 和 `MaxIndex`。
  - 只读取该 packet 内 `MaxIndex` 对应的一个 Complex16 样点，计算 `rMax = sqrt(I^2 + Q^2)`。
  - `autoScaleGain = 32767 / rMax`；最终发送样点使用 `autoScaleGain * Streaming_IQScale / 100`。
- 该路径是 O(packet count metadata + 1 个 IQ 样点读)；对 2GB 文件只读 25 MiB `trig` 预留区中的实际 TriggerRecord 子集和一个峰值样点，不扫描完整 IQ payload。
- 必须校验 `MaxIndex * 4 < validBytes`。如果峰值记录无效、`rMax <= 0` 或 packet metadata 不可信，回退策略应窄而明确：
  - 小文件：可全量读有效 IQ 并精确扫描。
  - 大文件：提示无法建立 AutoScale，或退回保守 `autoScaleGain = 1.0` 并记录 warning；不要在实时发送线程里边播边追踪最大值改变已发送数据的缩放。
- 注意：TriggerRecord 的 `MaxPower_dBm` 更自然对应复功率峰值，不保证等价于全文件 `max(max(|I|), max(|Q|))` 的 component peak。若产品要求与 ARB component AutoScale 完全一致，需在启动前做一次全有效 IQ 扫描或建立 sidecar 缓存；这会牺牲大文件启动速度。当前 Streaming 记录回放优先选择快速复幅度 AutoScale。

### digital.iq.wav Fast-Path Verification

- 使用 `.github/skills/iqs-wav-playback-analysis/SKILL.md` 的固定 offset / MsgPack / TriggerRecord 规则，静态解析真实文件 `data/digital.iq.wav`。
 TriggerRecord 快速路径验证了“复功率 AutoScale”正确；它不等同于 ARB 旧的 single-component AutoScale。Streaming IQS-WAV 方案应明确采用复幅度 AutoScale。

### Cache And Large File Policy

- 拆分两个概念：
  - `frameBytes`: 单次 `tx_send_stream()` 的帧大小，当前为 8MB。
  - `preloadLimitBytes`: 是否把整个逻辑 cycle 放入内存的阈值，应按产品定义明确。如果目标真是“小于 8M complex 点”，阈值应是 `8M * 4 = 32MB`，而不是当前 8MB。
- 小逻辑 cycle 使用内存 replay cache：
  - 只缓存 AutoScale 后、再叠加 panel `IQScale` 前或后的 logical payload；不要缓存 IQS-WAV 的 `trig` 预留区和 packet padding。
  - 发送帧时从 cache 按读指针取数据，不足一帧时从 cache 头部 wrap 填满，避免每帧重新构造整块 replayFrame。
- 大逻辑 cycle 使用文件 segment cursor：
  - 不调用 `readIqsWavIqBytes(...)` 全量抽取。
  - 预解析轻量 metadata 和 segment table。
  - 每次直接把下一个 logical frame 读入预分配帧 buffer，跨文件、跨 IQS-WAV part、跨 segment、跨 cycle wrap 时只更新 cursor。
  - 通过 segment table 自然跳过 packet padding。
  - frame 填满后再原地应用 `autoScaleGain * Streaming_IQScale / 100`，避免读文件时产生额外中间 buffer。
- 2GB 文件在 62.5 MSps 时数据率约 `62.5e6 * 4 = 250 MB/s`，播放时长约 `2GB / 250MB/s = 8.6s`；在 15.625 MSps 时约 34.4s。SSD 顺序读通常能覆盖这个量级，主要风险不是文件太大，而是每帧多次复制、频繁分配、队列持有大对象和 seek 过碎。

### Implementation Shape

- P0 正确性：
  - `StreamingDataGenerator` 打开文件时先 `Utils::probeIqsWav(...)`，成功则不走 `WavHeader`，避免 25 MiB `trig` chunk 被普通 WAV parser 读入。
  - 新增轻量 `IqsWavStreamSource` / `IqsWavStreamSegment` 内部结构，不改变外部 property。
  - UI 统计 `totalSamples` 对 IQS-WAV 使用 `validComplexSamples`，对普通 WAV 使用 `WavHeader::sampleCount()`；duration 继续按 panel 当前 `Streaming_SampleRate` 计算。
  - 文件 sample rate 仅作 metadata，不自动设置 panel，先不做实时重采样。
  - 如果 file list 同时包含 IQS-WAV 和普通 WAV，正常按列表顺序串接；普通 WAV 走现有 raw data 语义，IQS-WAV 走有效 segment + AutoScale 语义，用户负责混合后的信号含义。
  - IQS-WAV 启动时利用 TriggerRecord 的 `MaxPower_dBm` / `MaxIndex` 建立 `iqsAutoScaleGain`，并在每帧下发前对 IQS-WAV segment 叠加 panel `IQScale`。
- P1 吞吐：
  - 用 `QFile::read(char*, qint64)` 直接读入最终帧 buffer，移除 `array -> IQ -> iqData` 的重复搬运。
  - 普通 WAV 继续只在 `IQScale != 100%` 时触碰样点；IQS-WAV 则始终按 `autoScaleGain * IQScale / 100` 处理样点。
  - `DeviceDataRequest` 承载可移动或共享的帧 buffer，避免每次入队/出队复制 8MB 数据。
- P2 稳定性：
  - 引入固定数量 frame buffer pool，大小建议 `maxQueueSize + 2`，运行期不再频繁分配/释放大块内存。
  - 记录 producer fill time、queue depth、`tx_send_stream()` time、underrun/empty-wait 次数，用于判断瓶颈在磁盘、CPU memcpy、IQScale 还是设备反压。
  - 若设备 API 支持更优帧长，再用实测决定 `frameBytes`，不要只凭 8MB 固定值长期硬编码。

### Success Criteria For Future Implementation

- 普通 SGS WAV 行为保持不变：按 WAV data chunk 作为 Complex16 IQ 源参与 streaming。
- IQS-WAV 行为正确：识别魔数，使用 `prof/trig/data` metadata，只播放 TriggerRecord 标记的有效 IQ 字节。
- IQS-WAV Streaming 默认执行快速 AutoScale，并在 AutoScale 后应用 panel `Streaming_IQScale`。
- IQS-WAV 与普通 WAV 混合时正常按列表顺序串接；系统只保证各 source 的抽取和缩放规则正确，用户负责混合播放的业务合理性。
- 小文件能从内存 replay cache 下发，不再反复读盘。
- 2GB 级文件不全量载入内存，内存占用主要由 frame buffer pool 和队列上限决定。

## 2026-07-08 Streaming P0/P1 Implementation Plan

### Scope Update

- Implement the Streaming P0/P1 design only.
- Defer the P2 stability refactor: no fixed-size reusable buffer pool, no telemetry counters, and no frame-size policy change in this pass.
- Preserve existing external Streaming properties: `Streaming_FileList`, `Streaming_IQScale`, and `Streaming_SampleRate`.

### Observations Before Editing

- `src/plugins/htra/streamingdatagenerator.{h,cpp}` is included by `src/plugins/htra/CMakeLists.txt`.
- `src/plugins/htra/streamingpanel.{h,cpp}` is included by `src/plugins/htra/CMakeLists.txt`.
- `src/libs/utils/iqswavreader.{h,cpp}` is included by `src/libs/utils/CMakeLists.txt` and already exposes `Utils::probeIqsWav(...)`.
- `Utils::WavHeader` still reads unknown chunks into `mOtherChunks`; therefore Streaming must probe IQS-WAV before constructing `WavHeader`.

### Implementation Success Criteria

- IQS-WAV files in Streaming are recognized by `Utils::probeIqsWav(...)` before `WavHeader` is used.
- IQS-WAV Streaming reads only TriggerRecord-valid packet ranges from the `data` payload and skips packet padding.
- Ordinary WAV Streaming keeps the existing raw data semantics and applies panel `IQScale` only when it is not `100%`.
- IQS-WAV Streaming applies one session-level fast complex-amplitude autoscale gain derived from TriggerRecord max metadata plus the referenced IQ sample, then applies panel `IQScale`.
- Mixed ordinary WAV and IQS-WAV file lists concatenate in list order.
- UI `totalSamples` uses `validComplexSamples` for IQS-WAV and `WavHeader::sampleCount()` for ordinary WAV; duration remains based on the panel `Streaming_SampleRate`.
- `DeviceDataRequest` carries a shared frame buffer so queue enqueue/dequeue does not copy the 8 MB payload.

### Verification Level

`static`

- Inspect the edited files and run text/static checks only.
- Do not build or run SGStudio unless explicitly requested later.

### Implementation Update

- Updated `src/plugins/htra/streamingdatagenerator.{h,cpp}`:
  - `DeviceDataRequest` now carries `QSharedPointer<QByteArray>`.
  - Streaming sources are normalized into ordinary WAV segments or IQS-WAV TriggerRecord-valid segments.
  - IQS-WAV is probed before `WavHeader`, so the ordinary parser is not used for IQS-WAV.
  - Large-frame generation reads directly into the final frame buffer and applies per-source scaling in place.
  - Replay cache uses the same source cursor and shared frame buffer.
- Updated `src/plugins/htra/streamingbussiness.cpp` to send from the shared frame buffer.
- Updated `src/plugins/htra/streamingpanel.cpp` so UI statistics probe IQS-WAV before ordinary WAV parsing.
- Static verification:
  - `git diff --check` reported no whitespace errors; it only printed the repository line-ending warning for edited files.
  - Text search found no remaining old `DeviceDataRequest::data` direct `QByteArray` access or old `array -> IQ -> iqData` generator path.
