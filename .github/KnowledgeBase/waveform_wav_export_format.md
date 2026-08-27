# SGStudio 保存波形 WAV 格式说明

## 1. 适用范围

本文说明 SGStudio 当前 `Save IQ Data` / `Save IQ` 功能实际写出的 `.wav` 文件格式。

当前纳入 CMake 构建并使用该格式的导出模块包括：

- Analog：Digital、DSSS、OFDM；
- HTRA：AM、FM、PM、Pulse、AWGN、Ramp、Multitone。

这些文件本质上是带自定义参数块的基带复数 IQ 文件，不是以扬声器播放为目标的普通立体声音频。它们采用标准 RIFF/WAVE 容器和 PCM `fmt ` 块，因此通用 WAV 工具通常能够识别容器；但“左/右声道”在 SGStudio 中分别约定为 I/Q 分量。

本文描述的是当前源码事实，不把普通音频软件的兼容行为视为格式保证。

## 2. 核心格式结论

| 项目 | 当前写入值或约定 |
| :--- | :--- |
| 容器 | RIFF/WAVE（传统 32-bit RIFF，不是 RF64/WAVE64） |
| 字节序 | RIFF 数值字段和 `prof` 长度均为 little-endian |
| `fmt ` 格式 | PCM，`AudioFormat = 1` |
| 通道数 | 2 |
| 通道语义 | 通道 1 = I，通道 2 = Q |
| 样点格式 | signed 16-bit integer，二进制补码 |
| IQ 排列 | `I0, Q0, I1, Q1, ...` |
| 每个复数样点字节数 | 4 bytes（I 2 bytes + Q 2 bytes） |
| SampleRate | 当前生成结果的复数 IQ 采样率，单位 Hz，以 `uint32` 写入 |
| ByteRate | `SampleRate * 4` |
| BlockAlign | 4 |
| 自定义参数块 | `prof`，内容为紧凑 UTF-8 JSON |
| 波形数据块 | `data`，内容为交织的 PCM16 IQ 数据 |

一个复数样点定义为：

```text
sample[n] = I[n] + j * Q[n]

data bytes = I0(int16), Q0(int16), I1(int16), Q1(int16), ...
```

源码中的频谱和波形预览也按偶数下标读取 I、奇数下标读取 Q，因此通道顺序不是根据立体声音频含义推测，而是当前业务代码的明确约定。

## 3. 文件总体布局

设：

- `P`：紧凑 JSON 的实际 UTF-8 字节数；
- `Pad = P & 1`：当 `P` 为奇数时补 1 个 `0x00`，否则为 0；
- `A = P + Pad`：`prof` payload 对齐后的物理占用字节数；
- `N`：复数 IQ 样点数；
- `D = N * 4`：`data` payload 字节数。

文件按以下顺序写入：

| 文件偏移 | 长度 | 内容 | 说明 |
| :--- | ---: | :--- | :--- |
| `0` | 4 | `RIFF` | RIFF 标识 |
| `4` | 4 | `44 + A + D` | RIFF ChunkSize，即文件总长减 8 |
| `8` | 4 | `WAVE` | WAVE 标识 |
| `12` | 4 | `fmt ` | 标准格式块 ID |
| `16` | 4 | `16` | `fmt ` payload 长度 |
| `20` | 2 | `1` | PCM |
| `22` | 2 | `2` | I/Q 两通道 |
| `24` | 4 | `Fs` | IQ SampleRate，Hz |
| `28` | 4 | `Fs * 4` | ByteRate |
| `32` | 2 | `4` | BlockAlign |
| `34` | 2 | `16` | BitsPerSample |
| `36` | 4 | `prof` | SGStudio 自定义参数块 ID |
| `40` | 4 | `P` | JSON 实际长度，不包含 pad |
| `44` | `P` | JSON | `QJsonDocument::Compact` 生成的 UTF-8 JSON |
| `44 + P` | `Pad` | `00` | 仅当 `P` 为奇数时存在 |
| `44 + A` | 4 | `data` | 波形数据块 ID |
| `48 + A` | 4 | `D` | 波形数据字节数 |
| `52 + A` | `D` | IQ data | 交织 PCM16：`I0,Q0,I1,Q1,...` |

因此：

```text
data chunk header offset = 44 + A
data payload offset      = 52 + A
file size                = 52 + A + D
complex sample count N   = D / 4
waveform duration        = N / Fs = D / (Fs * 4)
```

`data` payload 总是 4 的整数倍，不需要额外的 RIFF 奇数字节 padding。

## 4. `fmt ` 块详细定义

公共写入实现使用 16-byte PCM `fmt ` payload，不写 WAVE_FORMAT_EXTENSIBLE、ValidBitsPerSample、ChannelMask 或 SubFormat GUID。

| 字段 | 类型 | 值/计算方式 |
| :--- | :--- | :--- |
| Subchunk1Size | `uint32_le` | `16` |
| AudioFormat | `uint16_le` | `1`（PCM） |
| NumChannels | `uint16_le` | `2` |
| SampleRate | `uint32_le` | 当前生成结果的 IQ 采样率 |
| ByteRate | `uint32_le` | `SampleRate * NumChannels * BitsPerSample / 8 = Fs * 4` |
| BlockAlign | `uint16_le` | `NumChannels * BitsPerSample / 8 = 4` |
| BitsPerSample | `uint16_le` | `16` |

各导出模块都只覆盖公共 header 的 `DataSize` 和 `SampleRate`，所以 `NumChannels = 2`、`BitsPerSample = 16` 来自 `wavHeaderInfo` 的公共默认值。

`SampleRate` 在业务对象中是 `double`，写入 header 前赋给 `quint32`。当前波形使用整数 Hz 采样率；若未来传入非整数采样率，文件头只能保留转换后的 32-bit 整数值。

## 5. `prof` 自定义块

### 5.1 二进制结构

```text
4 bytes   "prof"
4 bytes   JSON byte length P, uint32 little-endian
P bytes   compact UTF-8 JSON, no trailing newline
0/1 byte  0x00 padding when P is odd; padding is not included in P
```

保存端用当前调制器的 `saveSettings()` 生成 `QJsonObject`，再用 `QJsonDocument::Compact` 序列化。读取端按 `prof` ID 找到 payload，并直接把它作为 JSON object 解析为 profile。

`prof` 位于 `fmt ` 和 `data` 之间。兼容解析器应遍历 RIFF chunk，而不应假定 `data` 永远位于普通 44-byte WAV header 之后。遇到未知 chunk 时，应跳过 `8 + chunkSize + (chunkSize & 1)` 字节。

### 5.2 当前各波形的 JSON 字段

字段名大小写属于当前文件格式的一部分。下表中的 `number` 同时涵盖 JSON 中的整数和浮点数。

| 波形 | `prof` JSON 字段 |
| :--- | :--- |
| Digital | `SymbolRate:number`、`PN:number`、`FSKDeviation:number`、`Oversample:number`、`FilterAlpha:number`、`FilterLength:number`、`SequenceSeed:number`、`ModulationType:string`、`FilterType:string` |
| DSSS | `SymbolRate:number`、`Code:number`、`Oversample:number`、`FilterAlpha:number`、`FilterLength:number`、`SequenceSeed:number`、`ModulationType:string`、`FilterType:string` |
| OFDM | `ModulationType:string`、`FFTSize:number`、`SymbolCount:number`、`GuardBandCarriersLeft:number`、`GuardBandCarriersRight:number`、`GuardInterval:number`、`WindowLength:number`、`NullDC:number`、`Windowed:number`、`SampleRate:number` |
| AM | `Rate:number`、`Depth:number`、`Shape:string` |
| FM | `Rate:number`、`Deviation:number`、`Shape:string` |
| PM | `Rate:number`、`PhaseDeviation:number`、`InitPhase:number`、`Shape:string` |
| Pulse | `Width:number`、`Period:number` |
| AWGN | `Bandwith:number`、`Length:number` |
| Ramp | `Span:number`、`SweepTime:number`、`Period:number` |
| Multitone | `PhaseMode:number`、`FixedPhaseOffset:number`、`Seed:number`、`NotchWidth:number`、`Count:number`、`FreqSpacing:number`、`DiscreteKeepToneMode:boolean`、`EvenCountCenterToneMask:boolean`、`EnabledToneIndices:string[]` |

注意：

- `Bandwith` 是当前源码中的实际拼写，解析时不能自行改成 `Bandwidth`。
- Digital、DSSS 的 `ModulationType` / `FilterType`，以及 OFDM 的 `ModulationType`，保存为枚举名称字符串。
- AM、FM、PM 的 `Shape` 保存为枚举名称字符串。
- OFDM 的 `NullDC`、`Windowed` 在 profile 定义中是 `int`，当前 JSON 因此按 number 保存，而不是 boolean。
- profile 用于恢复当前调制参数；公共文件头中的 SampleRate 和 `data` 才是重建时域 IQ 所需的核心数据。

## 6. `data` 块与数值语义

`data` payload 是按复数样点顺序交织的 `qint16` / `int16_t` 数据：

```text
word index:  0   1   2   3   4   5   ...
meaning:    I0  Q0  I1  Q1  I2  Q2  ...
```

取值范围是 signed 16-bit integer 的 `-32768..32767`。保存层不额外做音频归一化、声道重排、采样率转换或浮点 WAV 编码：

- Digital 和 HTRA 各模块直接写出生成器持有的 16-bit IQ 数组；
- DSSS、OFDM 先把内部 `float` 容器中的每个值以 `static_cast<short>` 转为 16-bit，再写入文件；
- 波形幅度的生成、缩放和量化在保存函数之前已经完成。

RIFF header 数值字段明确按 little-endian 写入。IQ payload 当前通过 16-bit 数组的原始内存写出，因此在仓库当前支持的 little-endian Windows/Linux 目标上同样是 little-endian；代码没有为 big-endian 主机额外转换 payload。

## 7. 文件大小限制

当前实现使用传统 RIFF 的 32-bit ChunkSize 和 `data` 的 32-bit长度，不支持 RF64/WAVE64。

必须满足：

```text
44 + A + D <= 0xFFFFFFFF
```

也就是完整文件最大为 `0xFFFFFFFF + 8` bytes，实际可用的 IQ 数据空间还要扣除固定 header、`prof` header、JSON 和可选 pad。由于每个复数样点占 4 bytes，实际 `D` 还必须是 4 的整数倍。

Digital 的新保存链路会先检查数据字节数，并捕获公共 header 对 RIFF 总长的溢出错误。其他当前模块在写 header 前把数据长度收窄为 `uint32_t`，因此调用方仍应保证波形尺寸没有越过传统 RIFF 上限。

## 8. 与 HAROGIC IQS-WAV 的区别

SGStudio 保存的上述文件不是 HAROGIC 频谱仪录制的 IQS-WAV：

| SGStudio Save IQ WAV | HAROGIC IQS-WAV |
| :--- | :--- |
| `prof` payload 是 compact JSON | `prof` 内含专用 magic `8C 22 52 9B`、协议版本和 MessagePack 风格 profile |
| `prof` 后直接进入 `data` | 固定位置还包含约 25 MiB 的 `trig` chunk |
| `data` 是连续交织 IQ | `data` 按录波 packet 语义解析，并结合 TriggerRecord 有效字节数 |
| `data` 偏移随 JSON 长度变化 | `data` 使用 IQS 协议定义的固定布局 |

两者都可能显示为 PCM、2 通道、16 bit 的 RIFF/WAVE，但不能只根据扩展名或基础 `fmt ` 字段互相替代。

## 9. 第三方解析建议

解析 SGStudio 导出的文件时，建议按以下顺序处理：

1. 校验 `RIFF` 和 `WAVE`。
2. 从偏移 12 开始逐个遍历 chunk；不要硬编码 `data` 偏移。
3. 读取 `fmt `，校验 PCM、2 channels、16 bits，并取得 SampleRate。
4. 如需恢复调制参数，读取 `prof` payload 并按 UTF-8 JSON object 解析。
5. 每次跳过 chunk 后按偶数字节边界对齐。
6. 读取 `data`，每 4 bytes 解出一组 little-endian `int16 I`、`int16 Q`。
7. 用 `N = dataSize / 4` 得到复数样点数，用 `N / SampleRate` 得到时长。

如果解析目标只是回放或分析 IQ，`fmt ` + `data` 已足够；如果需要在 SGStudio 中恢复具体调制参数，则还需要保留并解析 `prof`。

## 10. 代码依据

- 公共 WAV header 与 chunk padding：`src/libs/utils/wavheader.h`、`src/libs/utils/wavheader.cpp`。
- little-endian 基础字段序列化：`src/libs/utils/streamingutils.h`。
- `prof` JSON 读取：`src/libs/utils/decoder.cpp`。
- Digital 完整波形保存：`src/plugins/analog/digitalmodulation.cpp`。
- DSSS / OFDM 保存：`src/plugins/analog/dsssmodulation.cpp`、`src/plugins/analog/ofdmmodulation.cpp`。
- HTRA 保存：`src/plugins/htra/ammodulation.cpp`、`fmmodulation.cpp`、`pmmodulation.cpp`、`pulsemodulation.cpp`、`awgnmodulation.cpp`、`rampmodulation.cpp`、`multitonemodulation.cpp`。
- I/Q 顺序证据：`src/plugins/analog/digitalspectrumdialog.cpp`、`src/plugins/analog/digitalmodulator.cpp`、`src/plugins/htra/demodutils.h`、`src/plugins/htra/multitonegenerator.cpp`。
- IQS-WAV 专用布局：`src/libs/utils/iqswavreader.cpp`。
