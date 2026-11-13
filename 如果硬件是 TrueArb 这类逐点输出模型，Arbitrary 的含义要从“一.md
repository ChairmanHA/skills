如果硬件是 TrueArb 这类逐点输出模型，Arbitrary 的含义要从“一个周期的形状”改成“一段按设定采样率逐点输出的样点记录”。实现上会大幅变简单：不再做 FFT 重合成、谐波截断和 M/K 规划，改由硬件负责插值和采样率转换。

## 一、TrueArb 假设下 Arbitrary 的含义

以 SDG6000X 手册为依据（PDF 第 21、47–49 页）：TrueArb 按设定的 SRate 逐点输出，SRate 的定义是 “output points-per-second”；支持 2 点到 20 Mpts；参数有 SRate/Frequency、Amplitude、Offset、Phase、Interpolation（0-order hold 或 Linear）。

| 概念 | B0（当前） | TrueArb 假设 |
|---|---|---|
| 文件点的身份 | 一个周期形状的采样，会被重新合成 | **就是播放缓冲**，每个点输出一次，不增不减 |
| 权威参数 | 频率 | **采样率 SRate** |
| 频率 | 用户请求值，落到 K·fs/M 网格上 | 由 SRate/L 推导出的**记录重复频率** |
| 一个周期 | 文件的 L 点 = 1 个周期 | 整段 L 点 = 1 次重复。文件里含 N 个周期时，信号频率是 N·SRate/L，仪器不知道 N |
| 点与点之间 | 软件做带限三角插值 | **硬件**做 0 阶保持或线性插值，软件只记录选择 |
| 带宽和混叠 | 软件保证：截止 + 网格限制 | 不再由软件保证；文件内容要自己负责，镜像由硬件插值和模拟滤波处理 |
| 文件采样率（WAV 头、CSV 时间列） | 忽略 | 首次变得有意义，可作为 SRate 的默认建议值 |
| 改频率 | 重新生成缓冲 | 只改 SRate，缓冲不变 |

一句话概括：**软件交付“L 个点 + SRate + 插值方式”，由硬件保证逐点输出。**

这对应文档第 3 节里的模型 C（按给定更新率逐点输出），不再是模型 B。

## 二、实现需要怎样改

### 1. 新增硬件模型，保留 B0

B0 的契约是“固定 125 MSa/s 循环 M 点”，与逐点输出冲突，不要改 B0 的含义。应在 `awgwaveformcapabilities.h` 里新增一个模型（名字待定，例如 `C0`），它的能力项包括：
- Arb 的 SRate 范围和分辨率
- 记录长度的最小值、最大值和对齐粒度
- 支持的插值方式

这些数值需要硬件契约给出。SDG6000X 的数字（1 μSa/s–300 MSa/s、2 点–20 Mpts）只能参考，不能照抄。

### 2. `awgprofile.h`：请求结构

- 新增 `arbSampleRateHz` 作为唯一的权威参数。
- 界面可以像 SIGLENT 那样提供 SRate/Frequency 切换，但 Frequency 只是输入别名，编辑时就换算成 `SRate = f × L`，不另存频率。这样可以避免两个字段互相矛盾。
- 新增插值枚举 `{ZeroOrderHold, Linear}`。
- Arb 的 `phaseDegrees` 改为起始点偏移：起始点 = round(phase/360·L)，分辨率是 360°/L。实际相位应作为结果返回。这是我的假设，SDG6000X 手册没有写相位怎样作用于记录。

### 3. `awgplaybackplanner.cpp`：规划器

新增一种逐点记录类型（例如 `AwgPlaybackKind::Record`），规划结果为：
- `bufferLength = L`
- `sampleRateHz = SRate`
- `cycleCount = 1`
- `actualFrequencyHz = SRate / L`

规划器只校验 SRate 范围、L 范围和对齐粒度，不再搜索 M/K。

L 不满足对齐粒度时，应该报错，或者由用户明确选择补点。**不能自动重采样**，否则就不是逐点输出了。

### 4. `awgwaveformgenerator.cpp`：生成路径

在该模型下，Arbitrary 走一条独立的短路径：
1. 导入文件。
2. 按相位旋转起始点。
3. 调用 `applyShape()`，沿用现有的幅度、偏移和峰值缩放规则。
4. 设置 `waveform.sampleRateHz = SRate`。

`synthesizeArbitraryShape()` 和 `arbitraryCutoffRatio` 在这条路径上不再使用。`retainedHarmonicCount` 在这里没有意义，不应写出。

### 5. `awgfileimporter.cpp`：导入

目前 WAV 头里的采样率被丢弃，CSV 的时间列只用来做校验。可以改为：
- 返回一个可选的源采样率：WAV 用 `header.sampleRate()`，CSV 用 1/Δt。
- 界面在首次加载文件时用它预填 SRate，用户仍可覆盖。

是否采用这种行为需要你决定。RIGOL 和 SIGLENT 的手册都没有从文件读取采样率，RIGOL 甚至不支持带文件头的 Arb 文件。

### 6. 预览与 CSV 导出

**预览**：按 n/SRate 画出 L 个点，并用所选插值方式连接（台阶或折线）。它展示的是假定硬件的输出形状，不再是 125 MSa/s 的缓冲。

**CSV 导出**：
- 数据仍为 L 行，时间为 n/SRate。
- 元数据改为 `sample_rate`、`interpolation`、`record_length`、`repetition_frequency`、`start_point`。
- 删除与截止和谐波相关的字段。

### 7. 不需要改的部分

- 归一化的 `[-1,1]` 数值域
- WAV 和 CSV 两种格式
- `applyShape()` 的峰值缩放策略
- DAC 量化，现在仍不建模

## 三、另一种情况：硬件仍是 B0，但界面要 TrueArb 语义

这时软件必须自己完成采样率转换：用 0 阶保持或线性插值，把 L 点从 SRate 转到 125 MSa/s，再装进满足整数周期约束的 M 点缓冲。这相当于把现在的三角插值换成 0 阶保持或线性，并去掉 5 MHz 截止。

但此时周期 L/SRate 必须能落在 M/K 网格上，实际 SRate 会被量化。它是“用 B 模型模拟 C 语义”，不等于真正的 TrueArb 硬件。

## 四、实施前需要你确认

1. 新增模型，还是替换 B0？我建议新增。
2. 是否让 SRate 成为唯一权威参数，Frequency 只作输入别名？
3. 加载文件时，是否用文件自带的采样率预填 SRate？
4. L 不满足对齐粒度时：报错，还是让用户选择补点？
5. 相位是按整点旋转（分辨率 360°/L），还是 Arb 不提供相位？

另外，这个假设也会影响其他周期波形。逐点硬件下，正弦、方波等同样需要选择 SRate 和点数。以前删掉的 `fs = frequency × pointCount` 做法（模型 C）在这个假设下会重新成立。这超出了 Arbitrary 的范围，要做时应单独规划。

Searched for regex `sampleCount\(\)|sampleRate\(\) const` (`**/src/libs/utils/**`), 5 results

在 TrueArb 下，L 是播放缓冲的点数，即导入后得到的有效样点数。硬件从第 0 点逐点输出到第 L−1 点，再回到第 0 点，所以重复频率 = SRate/L。

## 五、L 的含义

- **L 就是播放缓冲长度**：硬件按 1/SRate 的间隔依次输出第 0 到第 L−1 点，然后回到第 0 点，不插点也不删点。
- **L 由文件决定**：它是导入后的有效数值个数，不是用户参数，也不由频率推出。改 SRate 不会改变 L。
- **手册依据**：SDG1000X Plus（PDF p44）写 `SRate = frequency × Length`，DG5000 Pro（p103）写 `Output Frequency = Sample Rate / Waveform Points`。这里的 Length 和 Waveform Points 都是 L。
- **L 的取值范围由硬件决定**：SDG6000X 是 2 点到 20 Mpts（p47）。DG5000 Pro 按文件类型不同：`.arb` 32 点到 64 Mpts，`.txt` 32 点到 8 Mpts，`.csv` 32 点到 1 Mpts（p103）。我们的范围要等硬件契约给出。

## 六、记录重复频率 SRate/L

一次完整播放的时长和对应频率是：

$$
T_{rec}=\frac{L}{S_{Rate}},\qquad f_{rep}=\frac{1}{T_{rec}}=\frac{S_{Rate}}{L}
$$

- 这里的周期包括最后一个点保持的那一个采样间隔，所以是 L/SRate，不是 (L−1)/SRate。
- f_rep 是“整段记录多久重复一次”，不一定等于信号频率。若文件中有 N 个完整周期，信号频率是 N·SRate/L，而仪器并不知道 N。
- 例子：L = 1000，SRate = 1 MSa/s，则 T_rec = 1 ms，f_rep = 1 kHz。如果这 1000 点里有 10 个正弦周期，正弦频率是 10 kHz，但仪器界面显示的 “Frequency” 仍是 1 kHz。
- 衔接点（第 L−1 点回到第 0 点）是否连续，由文件本身负责，硬件和软件都不修补。

一个常见错误：用含端点的方式生成一个周期，例如 `linspace(0, T, L)` 包含了 t = T。这样最后一点和第一点重复，每次循环都会多出一个重复样点，波形在衔接处出现小台阶，实际周期也比预期多一个采样间隔。正确做法是只取 t = n·T/L，n = 0…L−1。

## 七、整段 L 点如何从文件得到（对照当前导入代码）

| 格式 | L 的来源 | 文件自带采样率（可作为 SRate 默认值） |
|---|---|---|
| WAV（单声道 16-bit PCM） | `sampleCount = dataSize / (channels × bytesPerSample)`，单声道即 data 块字节数 ÷ 2（`wavheader.h:55`） | 头部的 `sampleRate()`，当前被丢弃 |
| CSV/TXT 单列 | 有效数值行数，不计空行、`#` 或 `;` 开头的注释行，以及第一个数值行之前的一行表头 | 无 |
| CSV/TXT 两列（时间, 值） | 有效数值行数，时间列不计入 L | 1/Δt；当前只用于检查时间严格递增且等间隔 |

这里有一个容易算错的地方：两列文件的记录时长应是 L·Δt，不是 `t_last − t_first`（那只有 (L−1)·Δt）。若把 SRate 设为 1/Δt，f_rep = 1/(L·Δt)。

在 TrueArb 语义下，导入流程有三条原则：
1. **文件中的数值原样成为 L 个样点**，只做归一化值到缓冲格式的转换，不做重采样、抽点或插值。
2. **L 不合规时报错**：超出硬件的最小/最大长度，或不满足对齐粒度时，应报错或请用户明确选择。不能像 USG 用作调制波时那样自动抽点，那是 DDS 路径的做法。
3. **任何改变 L 的操作都会改变 f_rep**：补零、截断、复制周期都会改变 L。所以这类操作必须显式进行，并在结果里报告新的 L 和 f_rep。相位旋转只改变起点，不改变 L。

如果希望按文件的原始时间尺度回放，把 SRate 设为文件自带的采样率即可。此时 T_rec 等于文件记录的时长，f_rep 等于它的倒数。