# Large Waveform Prompt 清理与意图分流重构方案

## 本次代码改动目标

1. 删除以下四种调制当前无效的大波形提示：AM、FM、Pulse、Multitone。
2. 为 Ramp 的 Span 编辑路径补充大波形提示。
3. 保持现有 Digital / Ramp / OFDM 的超限提示逻辑不被误删。

## 本次实现边界

### 直接落地

- AM：删除 Rate 编辑路径的大波形提示，参数回写后直接应用。
- FM：删除 Rate 编辑路径的大波形提示，参数回写后直接应用。
- Pulse：删除 Width / Period 编辑路径的大波形提示，参数回写后直接应用。
- Multitone：删除 Count 编辑路径的大波形提示，参数回写后直接应用。
- Ramp：在 Span 编辑路径增加提示；保持当前 Period 路径行为不扩大改动面。

### 暂不在本次代码中扩大范围

- 不在本次把 Ramp / OFDM 的旧式 `MessageDialog(Yes/Cancel)` 统一重构到 `showLargeWaveformPrompt()`。
- 不在本次改动 Digital 的真实生成长度策略。
- 不在本次修改保存 WAV 时的全量导出语义。

## 设计判断依据

### 1. 无效提示清理

这四类调制在当前参数钳位与采样率选择规则下，估算值不会严格大于 `MAXDOWNLOADSIZE`：

- AM
- FM
- Pulse
- Multitone

因此继续弹“Generate And Trim / Generate & Stream”没有实际业务价值，只会制造误导。

### 2. Ramp 为何仍要补 Span 提示

Ramp 当前只有 Period 编辑路径有提示，但 `Span` 变大同样会推高 Nyquist 下限，从而让 `calculateRampSampleRate()` 进入超限区间。

因此 Ramp 的提示不是删掉，而是应该补齐到真正影响超限的入口。

## 后续重构目标：按用户意图分流

当前大波形交互混合了两类完全不同的用户意图：

1. 我只想把一个可播放的 payload 下发到设备
2. 我想完整生成并保存/分析全量波形

这两类意图在工程上应该明确分流，否则会出现：

- 为了下发 125MB payload，先无意义地生成数百 MB 甚至数 GB 的完整波形
- UI 只给出“波形太大”的泛化提示，却没有明确告诉用户系统会如何处理
- 保存 / 频谱分析 / 设备下发共用同一生成策略，成本被最坏场景绑死

## 推荐交互分层

### A. Ramp / OFDM：轻超限，允许“生成并截断下发”

特点：

- 超限通常有限，量级与 `MAXDOWNLOADSIZE` 相邻
- 全量生成成本可接受
- 截断后下发通常仍符合用户对“立即发射/应用参数”的预期

建议交互：

1. 当用户修改参数导致估算值 `> MAXDOWNLOADSIZE` 时，弹出统一提示。
2. 提示文案要明确写出：
   - 预计全量大小
   - 设备可下发上限
   - 继续后将“生成完整波形，但下发时自动截断到设备上限”
3. 按钮建议：
   - `Adjust Params`
   - `Generate And Trim For Download`
   - 若业务支持，再提供 `Generate Full Waveform For Save/Analysis`
4. 默认推荐按钮应为 `Generate And Trim For Download`，因为这是最贴近实时发射意图的主路径。

工程实现建议：

- UI 层统一调用公共 helper，而不是每个调制自己拼 `MessageDialog`
- 继续复用 `AnalogPlaybackBusiness::requestGenerateAndTrim()` 作为“确认后生成并进入截断下载语义”的入口
- 保存 WAV 时如果用户明确要求保存文件，则允许走完整 payload

### B. Digital：重超限，应优先“按目标长度生成”而不是“先全量再截断”

特点：

- `PN` 与 `oversample` 组合可远超 `MAXDOWNLOADSIZE`
- 当前代码固定把 `SymbolLength` 传成 `2^PN`，见 `GenerateDigitalModWaveform(..., SymbolLength, ...)`
- 当用户真实意图只是设备下发时，这种“先全量生成再截断”的策略在 CPU、内存、等待时间上都不合理

建议交互分成两条主路径：

#### Digital 下载意图

1. 当用户点击发射、应用、下载，或在会触发自动重算的场景下，如果估算全量大于 `MAXDOWNLOADSIZE`：
   - 不再默认按 `2^PN` 生成全量波形
   - 先计算设备可容纳的最大 `SymbolLength`
   - 调用算法库时直接传入“截断后所需的 SymbolLength”
2. UI 提示要说明：
   - 当前 PN 对应完整序列超过设备 RAM
   - 系统将生成“用于下载的最大片段”，而不是完整 PN 周期
   - 这可能影响 BER/锁定/严格周期一致性
3. 按钮建议：
   - `Generate Download Segment`
   - `Generate Full Sequence For Save/Analysis`
   - `Adjust Params`

#### Digital 全量分析 / 保存意图

1. 当用户明确点击保存 WAV、导出、频谱离线分析时，再允许走完整 `2^PN` 生成。
2. 生成前再次提示内存/耗时风险。
3. 超大任务建议进入后台任务队列，而不是阻塞 UI 线程语义。

## Digital 的工程方案建议

### 阶段 1：引入生成意图枚举

在 Analog 公共层或 Digital 内部引入明确意图：

- `DownloadTrimmed`
- `FullExport`
- `StreamingHandover`

### 阶段 2：把 Digital 的长度决策从 UI 抽到公共 helper

新增例如：

- `computeDigitalSymbolLengthForDownload(profile, maxBytes)`
- `computeDigitalFullSymbolLength(profile)`

### 阶段 3：Digital 调用算法库时不再写死 `pow(2, params.PN)`

当前：

- `GenerateDigitalModWaveform(..., pow(2, params.PN), ...)`

目标：

- 下载路径：传入按 `MAXDOWNLOADSIZE / bytesPerSymbol` 计算出的可下发片段长度
- 全量导出路径：传入完整 `2^PN`

### 阶段 4：统一保存与下载的行为说明

- 下载：允许非完整周期，但必须明确告知可能影响严格协议语义
- 导出：优先保证完整性，不主动截断
- 分析：根据分析目标选择下载片段或全量文件，不默认共用一条路径

## 最佳实践取舍

### 为什么 Ramp / OFDM 可以接受“先生成再截断”

- 超限幅度通常有限
- 算法成本可控
- 用户对这两类波形的主要期望更偏向“参数效果正确、能尽快下发”

### 为什么 Digital 不应该继续“先全量再截断”

- PN 序列长度呈指数增长
- 资源消耗与用户设备下发意图不匹配
- 对 BER/同步测试类场景，截断与全量在语义上并不等价，必须让用户显式选择

## 推荐后续实施顺序

1. 先完成本次清理：删除 AM/FM/Pulse/Multitone 提示，补 Ramp Span 提示。
2. 第二步把 Ramp / OFDM 的超限提示统一接入公共 `showLargeWaveformPrompt()` 与 `requestGenerateAndTrim()`。
3. 第三步单独重构 Digital：引入“下载片段生成”和“全量导出生成”两套明确路径。