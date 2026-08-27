# ARB 模式现状、文件解析与接入边界（HTRA 插件）

本文档总结当前工程中 ARB（Arbitrary Waveform Playback）模式的文件识别逻辑、数据生成规则、当前已接通的执行链路，以及 ProgrammedArb 尚未重新接入 runtime 时的边界约束。

> 适用范围：当前实现能够稳定执行的是普通 WAV 的 Playback 路径；信号编辑器生成的 `.arb`（本质为带自定义 chunk 的 WAV 容器）目前仍保留解析与模式识别能力，但执行链路尚未重新接入 runtime。

> 当前状态摘要：
> 1. OrdinaryWav 已收敛为 core-managed Playback data provider，由外部 request/runtime 主线负责设备配置与启动。
> 2. OrdinaryWav 的短波形补齐规则仍与其他 Playback provider 共用 `Core::IPlaybackBusiness::normalizePlaybackPayloadForDownload()`；ARB 没有分叉出单独的“小波形补齐”实现。
> 3. ProgrammedArb 当前仅保留“文件模式识别 + capability/UI 路由”语义，执行链路尚未接入 runtime；当前仓库里真正可下发播放的仍只有 OrdinaryWav。
> 4. ProgrammedArb 的 capability 暂不调整；等它真正接入 runtime 后，再把“文件内自带载波计划”的模型和 capability 一起重构。

## 1. 组成模块与职责边界

### 1.1 属性系统（PropertySystem）
- 属性创建位置：HTRA 插件初始化时创建 ARB 相关属性（SampleRate / FileName / AutoScale / IQScale / SampleOffset / SampleToUse / Period / SamplesInFile）。
- UI 和业务通过属性通信：UI 负责触发编辑与显示；业务层（ArbModulationOnly + ArbDataGenerator）负责属性间约束、文件解析、数据生成与设备下发。

### 1.2 UI：ArbPanel
- 主要职责：
  - 绑定按钮/开关到属性（PropertyBindingManager）。
  - 触发加载文件，并在 reset 时清空当前文件状态。
  - 显示文件名、SamplesInFile、信号时长（SignalLen/PeriodLen）等只读信息。
  - AutoScale 开关控制 IQScale 可编辑性（AutoScale 开启时禁用 IQScale）。

### 1.3 数据生成：ArbDataGenerator
- 主要职责：
  - 文件校验与“是否支持下载”的判定（基于 MAXDOWNLOADSIZE 和分段估算）。
  - 维护当前 ArbProfile（fileName/sampleRate/autoScale/iQScale/sampleOffset/samplesToUse/period）。
  - 在独立工作线程中生成波形语义层的数据结果（m_data_handled），并以 statusChanged 通知业务层。
  - 根据文件类型决定哪些参数可编辑（仅 .wav 可编辑 SampleOffset/SamplesToUse/Period）。

### 1.4 业务与设备：ArbModulationOnly
- 主要职责：
  - 将 ArbDataGenerator 的参数映射到 PropertySystem 属性；在 editingFinished 时写回生成器。
  - 接收生成器 statusChanged，拉取生成结果 m_data。
  - OrdinaryWav 模式下，作为 Playback data provider 在 `buildPlaybackExecutionContext()` 边界补齐 payload 的下载约束，并对外提供 sampleRate + 单波形 IQ payload，不再承担 legacy 设备配置/启动。
  - ProgrammedArb 模式下，当前只负责暴露文件模式/capability 差异，不再保留旧的设备线程、bridge 缓存或直接下发执行逻辑。

### 1.5 文件解析工具：Utils::Decoder / arbfileutils
- Utils::Decoder：打开文件、读取 WAV header、读取 IQ 数据、读取自定义 chunk（readCustomChunk）。
- utils/arbfileutils：定义 ARB 自定义 chunk 结构与解析逻辑，并提供“预估展开大小”和“生成可下载分块数据”的工具。


## 2. ARB 支持的文件类型与解析规则

### 2.1 普通 WAV（无自定义 rep chunk）
判定依据：Decoder::readCustomChunk("rep ") 为空。
- 文件读取：
  - 读取 header.sampleCount()
  - 读取 IQ 数据：decoder.readIQData(0, header.sampleCount())
  - 得到单段 dataForDownload（freq=0, powdBm=-100 作为“普通 wav”的标识）
- 参数可编辑性：
  - 可编辑 SampleOffset / SamplesToUse / Period（由 ArbDataGenerator::checkEditableStatus 决定，仅后缀为 wav 才开放）

### 2.2 .arb（带自定义rep chunk 的 多WAV 容器）
判定依据：存在自定义 chunk：
- "rep "：数据段信息（DataSectionInFile）
- 可选 "hop "：频率/功率段信息（HopSectionInFile）
- 可选 "user"：信号编辑器用于恢复用户输入的 JSON（仅编辑器侧需要）

#### 2.2.1 rep chunk（WAV_REPEAT = "rep ")
每条记录字段（arbfileutils.cpp 读取顺序）：
- index: quint32
- startPos: quint32（相对 data 区的字节偏移）
- len: quint32（字节长度）
- repetition: quint32（重复次数）
- window: quint16（WindowType）

#### 2.2.2 hop chunk（WAV_HOP = "hop ")
每条记录字段：
- index: quint32（频率/功率变化点）
- frequency: double
- powdBm: double
- isBaseBand: 兼容字段（当前读取 1 字节但业务上始终认为无意义/false）

#### 2.2.3 user chunk（WAV_USER = "user")
- 内容：JSON Array
- 用途：信号编辑器恢复用户原始输入（dataSection/constantSection/freqSection），与设备下发无直接关系。

#### 2.2.4 ARB 文件的“下发数据生成”
- getDatasDownload(fileName, minPackSize) 会：
  - 读取 rep/hop
  - 按 hop 的频率/功率分组（或认为单一组）
  - 逐段读取 IQ（decoder.readIQByPos(startPos, len)）
  - 生成 realDataSec 列表，再通过 handleAllBlocks/handleSignalBlock 做“合并/展开”生成最终 dataForDownload 序列

> 重要行为：ARB（信号编辑器生成的分段文件）不允许 UI 的截取逻辑（SampleOffset/SamplesToUse/Period）介入。


## 3. 大小/内存限制与拒绝策略

### 3.1 Ordinary/IQS WAV 的设备能力上限
- Ordinary/IQS WAV 不再使用 ARB 业务内固定的 100/125 MiB 常量，而是读取当前设备 `PlaybackCapabilities::maxWaveformBytes`。
- 未返回 `OPTION_BW_320M_TX` 时上限为 `125 * 1024 * 1024` Bytes；返回该选件时上限为 `1000 * 1024 * 1024` Bytes。model 122、132 及其他 HTRA 型号均按选件结果分档。
- Ordinary WAV 按 WAV `data` chunk 字节数比较；IQS-WAV 按 TriggerRecord 去除 packet padding 后的 `validDataBytes` 比较。IQS 文件容器总大小不是实际下载 payload 大小。
- `Period` 的最大复样本数为 `maxWaveformBytes / bytesPerComplexSample`；当前 HTRA Complex16 IQ 的 `bytesPerComplexSample` 为 4。

### 3.2 Ordinary/IQS WAV 的拒绝时机
- `ArbDataGenerator::handleFile()` 在同步 header probe 阶段比较有效 payload 与当前设备容量；超限时保留原有业务状态，不进入大 payload 分配、完整 IQ 读取或设备下载。
- `ArbPanel::loadFile()` 比较“用户请求路径”和业务层最终接受的属性路径；从空状态加载和替换已有文件两种场景。
- worker 的 `buildFilePlaybackPayload()` 与 runtime 仍保留容量检查，作为设备能力切换和异步竞态下的二次防线。
- 文件被接受后，生成器保留其实际有效 payload 字节数。若后续热切换到可用但容量更小的设备，且该 payload 已超限，生成器会清空文件路径、SamplesInFile、SampleOffset、SamplesToUse、Period、已物化 payload、功率指标和文件模式，并使旧 worker generation 失效。
- 若源文件仍兼容，但旧 `Period` 经过短波形下载补齐后对应的最终 payload 超过新设备容量，文件不会卸载；生成器释放旧 payload/metrics，把 `SampleOffset` 复位为 0，并把 `Period/SamplesToUse` 永久回写为 `min(SamplesInFile, newMaxComplexSamples)`。
- 设备切换过程中的临时空 capability（`maxWaveformBytes == 0`）不会触发上述清理；只有新设备的有效能力发布后才判断是否超限。

### 3.3 ARB 分段文件的限制
- 依据 getTotalSize4Sections(fileName) 预估“展开后将要下发的总大小”：
  - genDownloadSections 会把 rep/hop 转换为 freqChangeSection 列表
  - prepareSingleBlock 会为每个数据段计算 exceptedLength（考虑 repetition 展开、minPackSize 拼包、以及 maxDownloadSize 约束）
  - totalSize = Σ exceptedLength
- 仅当 totalDownLoadSize <= MAXDOWNLOADSIZE 才认为可加载。

### 3.4 SamplesInFile 的计算口径
- 普通 WAV：samplesInFile = header.sampleCount()
- 分段文件：
  - samplesInFile += (len / 4) * repetition
  - 说明：len 为字节长度，除以 4 表示按复数 IQ（I16+Q16）每个“复样本”4 字节折算样本数。


## 4. 数据生成规则（ArbDataGenerator）

### 4.1 线程模型
- ArbDataGenerator 内部创建工作线程 m_workerThread。
- 外部参数变化通过 requestGenerateData()：
  - m_status=0
  - 清空 m_data_handled
  - m_profileChanged=true
  - emit statusChanged()
  - condvar 唤醒 workerLoop
- workerLoop 逻辑：
  1) 等待 m_profileChanged
  2) 如果 m_fileChanged：解析文件、生成中间数据 m_data
  3) 对 m_data 执行 handleData(profile) 生成最终 m_data_handled
  4) m_status=1，emit statusChanged()

### 4.2 缩放（AutoScale / IQScale）
- AutoScale=true：
  - 计算全局最大幅度 max
  - ratio = 32767 / max
  - 对所有 IQ 样本乘 ratio（归一化到 int16 满幅附近）
- AutoScale=false：
  - scale = IQScale * 0.01（IQScale=100 表示 1.0，不是归一化）
  - 对所有 IQ 样本乘 scale

### 4.3 普通 WAV 的截取/补零/最短长度处理
仅在“普通 wav 标识成立”时启用：m_data_handled.size()==1 且 powdBm==-100。
- 输入参数：
  - sampleOffset（以“复样本”为单位）在处理时乘 2 变为 int16 下标
  - samplesToUse 同理乘 2
  - period 用于决定输出长度（period*2 个 int16）
- 处理方式：
  - 先创建 IQ_handled(period*2) 并清零
  - 将 [sampleOffset, sampleOffset+samplesToUse) 对应的 IQ 拷贝到 IQ_handled 的开头
  - 这一阶段到此为止，不在生成器内部引入下载侧最小 payload 规则

> 业务含义：生成器只负责用户参数定义的波形语义。与设备/下载链路相关的“最小 payload 长度”约束不在这里处理。

### 4.4 ARB 分段文件的处理限制
- 对于信号编辑器生成的 ARB 分段数据：不允许截取（SampleOffset/SamplesToUse/Period 不参与）。
- handleData 对其仅做缩放，不做裁剪。

### 4.5 加窗（WindowType）
- 分段解析中，每个 DataSectionInFile 带 window 字段。
- 在 handleSignalBlock 等拼包逻辑中，存在如下规则：
  - 当某段 iqData.size()==2 且 window!=NoWindow 时，会对展开后的数据应用 applyAPIWindow()
  - applyAPIWindow() 通过 DSP_GetWindowCoefficient 获取窗系数并逐点相乘


## 5. 参数可编辑性规则

ArbDataGenerator::checkEditableStatus：
- fileName 为空：不可编辑
- 仅当文件后缀为 "wav" 才允许编辑：SampleOffset / SamplesToUse / Period

ArbModulationOnly 会将这一规则映射到 Property 的 readOnly：
- sampleOffsetProperty->setReadOnly(!isSampleOffsetEditable())
- samplesToUseProperty->setReadOnly(!isSamplesToUseEditable())
- periodProperty->setReadOnly(!isPeriodEditable())


## 6. 当前执行流（按模式分流）

### 6.1 OrdinaryWav：当前唯一已接通的 ARB 执行路径

- 文件识别：`ArbDataGenerator::handleFile()` 在没有 `rep` chunk 时把文件判定为 `OrdinaryWav`。
- 数据生成：`ArbDataGenerator::handleData()` 对普通 wav 只执行“缩放 + 截取 + period 补零”。
- 对外暴露：`ArbModulationOnly::buildPlaybackExecutionContext()` 从 `m_data.front().iqData` 取出单波形 IQ payload，并在这里调用共享 helper `Core::IPlaybackBusiness::normalizePlaybackPayloadForDownload()` 施加最小下载长度规则，然后设置：
  - `providerParticipating = true`
  - `sampleRate = Arb_SampleRate`
  - `dataReady = status == 1 && payload 非空`
- 主执行链路：`TxSessionService -> TxPipelineRuntime` 负责后续设备配置、下载和启动。

> 结论：当前 ARB 下发普通 `.wav` 时，仍然走和 `IPlaybackBusiness` 一样的小波形补齐规则；但补齐职责已经收口到 provider payload 边界，而不是放在生成器里。

### 6.2 ProgrammedArb：当前只保留模式识别与路由语义

- 文件识别：`ArbDataGenerator::handleFile()` 在存在 `rep` chunk 时把文件判定为 `ProgrammedArb`。
- 当前 capability：`ArbModulationOnly::providerCapabilities()` 仍返回 `{true, false, false}`。
  - 含义是“保留固定载波路由，禁止外部 sweep carrier 组合”。
  - MainWindow 会据此禁用外部 sweep 按钮，把 ProgrammedArb 视为“文件内部自带程序”的特殊 Playback 模式。
- 当前 provider 行为：`buildPlaybackExecutionContext()` 对 ProgrammedArb 返回 `providerParticipating = false`。
- 当前执行边界：仓库里已删除旧的 ProgrammedArb deviceThread / bridge cache / direct download 路径；因此它现在不再直接配置设备或启动播放。

> 结论：当前仓库的真实现状是“只有 OrdinaryWav 已接通，ProgrammedArb 仍未接入 runtime/设备执行链”。

### 6.3 为什么当前先不改 ProgrammedArb 的 capability

当前不建议立刻修改 ProgrammedArb 的这组 capability，原因有两个：

1. 这组 bit 在当前实现里不仅表达“设备语义”，还承担 UI/orchestrator 路由语义。
2. 如果现在直接把 `supportsFixedCarrier` 改成 `false`，固定载波下 orchestrator 会把 ProgrammedArb 直接解析成 `Mute`，而不会自动变成“文件内 MScan 播放”。

因此当前阶段的策略是：

- 保持 ProgrammedArb 的 capability 现状不变；
- 先承认它只是“文件模式识别 + UI 路由占位”；
- 等 ProgrammedArb 真正接入 runtime 时，再把“文件内自带 carrier plan / MScan program”建模为新的正式语义，而不是继续借用 `supportsFixedCarrier / supportsSweepCarrier` 做折中表达。


## 7. UI 行为要点（ArbPanel）

### 7.1 绑定与显示
- SampleRate/IQScale/SampleOffset/SamplesToUse/Period：按钮与属性绑定。
- FileName/SamplesInFile/SignalLen/PeriodLen：只读显示。
- AutoScale：
  - UI 直接把开关状态写入 Arb_AutoScale，并触发 editingFinished。
  - AutoScale 开启时禁用 IQScale 编辑。

### 7.2 加载文件（Load）
- 当前 Load 行为是“替换当前文件”，不会再因为已有文件而弹出“File already loaded.”。
- 当前文件选择 filter 为 `WAV Files (*.wav)`，与“目前只有 OrdinaryWav 已接通”的现状一致。
- `checkWavFile()` 使用 `Utils::Decoder::open()` 校验文件可读。
- 写入 `Arb_FileName` 后，UI 会读回属性：
  - 若最终属性路径与本次请求路径不同，说明业务侧拒绝了新文件；UI 静默结束本次加载。替换已有文件失败时旧文件保持不变。
  - 若写回成功，则刷新 SignalLen/PeriodLen。

### 7.3 清空文件状态
- 当前界面已移除 Unload 按钮。
- reset 或内部清理通过 `clearLoadedFile()` 把 `Arb_FileName` 置空。
- UI 会把 SignalLen/PeriodLen 复位为 `0s`，并同步禁用 Enabled 状态。
- 热切换到较小容量设备导致当前文件超限时，UI 关闭并禁用 Enabled，清空文件及文件参数显示，并提示文件超过设备内存。
- 若文件兼容而补零后的最终 payload 超限，UI 关闭 Enabled 但保持控件可用，保留文件并显示业务层静默复位后的 Offset/SamplesToUse/Period，不弹窗。
- 设备切换场景只有“已加载文件因新设备容量不足而被自动卸载”会弹窗；其他参数回写与加载失败均不弹窗。


## 8. 业务侧常见边界/注意事项

- “文件过大”路径：
  - Ordinary/IQS 生成器在同步 `handleFile()` probe 阶段按当前设备能力直接拒绝；UI 通过请求路径与最终属性路径是否一致判断接受结果，但主动加载拒绝不提示。
  - IQS-WAV 比较的是有效 IQ payload，不是包含 25 MiB TriggerRecord 保留区和 packet padding 的物理文件总长度。
- status 语义：
  - `0`：数据未就绪（生成中/刚触发）
  - `1`：数据已生成（即便是“空数据”也会置为 1）
- 当前 `ArbPanel::onDataStatusChanged()` 仍为空实现；是否可播放主要由生成器状态和 provider payload 是否就绪决定。
- OrdinaryWav 的短波形补齐不要在第二处重复实现：
  - 当前 canonical 规则已经收敛到 `Core::IPlaybackBusiness::normalizePlaybackPayloadForDownload()`。
  - 该规则应在 `buildPlaybackExecutionContext()` 这种 provider-to-runtime 边界调用，而不是提前揉进生成器。
  - 如果以后别的 Playback provider 改最小长度规则，ARB OrdinaryWav 会在同一边界一起收敛。
- ProgrammedArb 当前只是“模式识别已完成、执行链未接通”：
  - 不要再把它误写成“已有 deviceThread / bridge request / H2 MSCAN 下发正在使用中”。
  - 如需恢复该能力，应基于当前 `TxSessionService / TxPipelineRuntime` 重新设计接入点，而不是把刚删除的 legacy 线程路径再加回来。


## 9. 相关源码入口（便于维护者快速定位）
- 属性创建：src/plugins/htra/plugin.cpp
- UI：src/plugins/htra/arbpanel.cpp / arbpanel.h
- 业务：src/plugins/htra/arbmodulation.cpp / arbmodulation.h
- 生成器：src/plugins/htra/arbdatagenerator.cpp / arbdatagenerator.h
- 共享 Playback 补齐 helper：src/plugins/core/iplaybackbusiness.cpp
- 文件解析与预估：src/libs/utils/decoder.h、src/libs/utils/arbfileutils.h/.cpp
- 限制常量：src/libs/utils/constants.h（SampleRate 范围）、src/libs/utils/arbfileutils.h（MAXDOWNLOADSIZE）

## 10. 后续演进建议

### 10.1 ProgrammedArb 真正接入 runtime 时再改 capability 模型

下一阶段如果要恢复/实现 ProgrammedArb 播放，建议把它建模成“文件内部自带 carrier plan 的 Playback provider”，而不是继续把它塞进当前这组三个 capability bit 的折中语义里。

更合理的方向是：

1. 明确区分“外部 Fixed/Sweep carrier 能力”和“provider 自带内部 carrier plan”。
2. 让 ProgrammedArb 直接向 runtime 提供可执行的内部 MScan/list plan，而不是依赖已删除的 legacy 设备线程。
3. 等 runtime 语义就位后，再一起收敛：
   - `providerCapabilities()`
   - MainWindow 的 sweep 可用性判定
   - orchestrator 的 pipeline 路由逻辑

### 10.2 文档和代码需要同步收敛

任何后续涉及 ProgrammedArb runtime 接入的实现，都应同时更新本知识库文档，至少同步下面三件事：

1. ProgrammedArb 是否已经真正可执行。
2. OrdinaryWav 与 ProgrammedArb 是否仍共用同一套 payload/补齐规则。
3. capability bit 是否还在承担“UI 路由占位”职责，还是已经升级为正式执行语义。
