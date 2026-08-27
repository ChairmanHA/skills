# Multitone VSG60 三组对比与 Playback 一致性说明

本文档只讨论当前实现、当前代码路径和当前实际读取过的三组文件，不回溯历史修正过程。目标不是证明“raw IQ 每点都必须一模一样”，而是明确：

1. 当前 `Fixed / Random / Parabolic` 三种语义为什么已经能对标 VSG60。
2. 哪些差异是导出/采样率/绝对相位参考造成的表象差异，不应被误判为算法错误。
3. 当前 SGStudio 的 playback/download 链路为什么不会改写 generator 的语义。
4. 后续真正值得继续优化的点是什么。

## 1. 本轮实际读取过的文件

本轮结论基于磁盘上**实际被读取**的文件，而不是仅基于对话附件预览。

### 1.1 VSG60 参考 CSV

- `C:\Users\jsl\Documents\SignalHound\vsg60\multitone1k.csv`
- `C:\Users\jsl\Documents\SignalHound\vsg60\multitone1krandom23.csv`
- `C:\Users\jsl\Documents\SignalHound\vsg60\multitone1KPara.csv`

### 1.2 SGStudio 生成 WAV

- `D:\development\vsg2.0\build\Qt_5_15_9_msvc2022_64-Debug\data\Multitone_1K.wav`
- `D:\development\vsg2.0\build\Qt_5_15_9_msvc2022_64-Debug\data\Multitone_1kRandom23.wav`
- `D:\development\vsg2.0\build\Qt_5_15_9_msvc2022_64-Debug\data\Multitone_para.wav`


## 2. 如何判断“数据不同但仍然正确”

判断 multitone 是否“对标 VSG60”时，不能只看导出的 raw IQ 数值是否逐点相同。更可靠的判断标准，是两边是否在同一组业务语义上描述了同一个连续时间信号。

对多音信号，连续时间表达可以写成：

$$
x(t) = \sum_k A_k e^{j(2\pi f_k t + \phi_k)}
$$

离散导出文件只是把它放到某个采样率网格上：

$$
s[n] = x\left(\frac{n}{F_s}\right) = \sum_k A_k e^{j\left(2\pi f_k \frac{n}{F_s} + \phi_k\right)}
$$

因此，只要 `F_s` 不同、段长不同、绝对相位参考不同，`s[n]` 的每一个样本值都可以不同；但只要下面这些核心量保持一致，它们仍然可以代表同一个业务语义上的 multitone：

1. active tone 集合是否一致。
2. notch 是否作用在同一频率位置。
3. `Fixed / Random / Parabolic` 的相对相位规律是否一致。
4. 当前离散段是否能 exact-period 闭合，避免循环播放时在段边界产生额外频谱。
5. playback/download 链路是否保持 generator 结果不变。

这也是本文判断“为什么虽然和 VSG60 数据不同，但当前实现仍然正确”的统一依据。

## 3. 当前算法与播放链路的职责边界

### 3.1 Generator 的职责

当前 generator 负责的是**定义波形本身**：

- 根据 `Count` 构造对称 baseband tone lattice。
- 根据 `NotchWidth` 对 tone lattice 做 DC 中心挖空。
- 根据 `PhaseMode` 分配相位：
  - `Fixed`：统一固定偏置。
  - `Random`：`Seed` 驱动的可复现独立随机相位。
  - `Parabolic`：按 Schroeder family 二次相位律分配。
- 先建立采样率下限：

$$
F_{s,lower} = clampSampleRate(1.25 \cdot Count \cdot FreqSpacing)
$$

  然后把最终 `sampleRate` 选成不低于该下限的 `fundamentalStep` 整数倍。
- 再求最小 exact-period 周期，并把最终 `sampleCount` 选成满足 playback 最小段长和目标 RBW 的最小工程友好 exact-period 段。
- 频域落 bin 后合成复基带，再量化成 int16 交织 IQ。

### 3.2 当前幅度口径

当前 htra multitone generator 在量化前会先计算整段时域波形的 `componentPeak = max(max(abs(I), abs(Q)))`，随后按

$$
scale = \frac{32767}{componentPeak}
$$

对整段 IQ 做统一缩放，再写出 int16 数据。产品语义上，这等价于默认打开 `AutoScale`，目标是尽量吃满 int16 动态范围且避免削顶。

这意味着当前口径**不是**固定 RMS，也**不是**固定单音功率。当 `Fixed / Random / Parabolic` 的 crest factor 不同时，最终导出的平均功率也会不同；这类差异当前应按“默认 AutoScale 打开”来理解，而不是误判为 playback 链路改写了 generator 结果。

这部分职责里最关键的，不是“导出多少个点”，而是：

1. tone 频率集合是不是对的。
2. notch 之后留下来的 active tones 是不是对的。
3. 相位模式到底表达了什么相对相位规律。
4. 这段波形在当前 sample rate 下是否能无缝循环。

就当前实现而言，上述四点都在 generator 内部决定，后面的 playback 链路不再重算这些内容。

## 4. 三组对比结论

## 4.1 Fixed

对比文件：

- `multitone1k.csv`
- `Multitone_1K.wav`

参数语义：

- `Count = 3`
- `FreqSpacing = 1 kHz`
- `NotchWidth = 1 kHz`
- `PhaseMode = Fixed`

### 4.1.1 当前 SGStudio 实际表达的信号

- 当前 generator 的 candidate tones 为 `{-1 kHz, 0, +1 kHz}`。
- notch 规则按 DC 中心挖空，`NotchWidth = 1 kHz` 时去掉 DC，只保留 `+-1 kHz`。
- `Fixed` 模式下，两根 active tone 使用相同相位偏置。

因此它表达的本质信号是：

$$
s[n] = A e^{j(\omega n + \phi_0)} + A e^{j(-\omega n + \phi_0)} = 2A e^{j\phi_0} \cos(\omega n)
$$

在默认固定相位偏置为 0 的场景下，结果就是一条没有 DC、Q 分量为 0 的纯实余弦。这与 VSG60 fixed 模式的业务语义一致。

### 4.1.2 为什么 raw IQ 看起来不同

- VSG60 当前 CSV 更接近“逻辑最小周期重复导出”。
- SGStudio 当前实现会先把采样率下限收进设备范围，再按 `fundamentalStep` 向上对齐采样率，然后按 playback 最小段长和目标 RBW 选 exact-period 导出段。

对这组 `+-1 kHz` 双音而言，当前实现会先把下限目标收口到设备最小采样率附近，再把最终 `sampleRate` 对齐到 `1 kHz` 的整数倍。按当前代码，这组参数下的 `sampleRate` 为 `196 kHz`，而 `sampleCount` 会扩到 `16464` complex samples，以同时满足 exact-period 和当前 playback 最小段长约束。

对这组 `+-1 kHz` 双音而言：

$$
\frac{f \cdot N}{F_s} \in \mathbb{Z}
$$

也就是说，当前 SGStudio 的判定标准不是“导出最短段”，而是“导出一段既满足 exact-period，又满足工程播放长度约束的合法播放段”。频率误差仍为 0，并且整段可以 exact-period 闭合。

这和 VSG60 用更短的最小周期点集导出，是两种不同的离散表示方式。因为采样率网格不同、段长不同，样本值逐点不一样是正常现象。

### 4.1.3 为什么仍然判定正确

这组 fixed 不应按“导出点数是否一样”来判定，而应按下面四件事判定：

1. active tone 集是同一组 `{-1 kHz, +1 kHz}`。
2. notch 的结果相同，DC tone 被移除。
3. 两根 tone 的相对相位相同，因此确实表达 fixed 两音。
4. 在 SGStudio 自己的 sample rate 下，这段波形 exact-period 闭合，循环播放不会引入额外边界谱线。

所以，虽然 CSV 与 WAV 的 raw IQ 长得不一样，但它们表达的是同一类 fixed multitone；差异来自导出采样网格和导出策略，而不是算法本身错误。

### 4.1.4 结论

`Fixed` 的 tone lattice、notch 语义和播放语义都是正确的；差异主要来自“VSG60 导出最小周期”与“SGStudio 面向设备 playback 的采样率选择”不同。

## 4.2 Random（Seed = 23）

对比文件：

- `multitone1krandom23.csv`
- `Multitone_1kRandom23.wav`

参数语义：

- `Count = 3`
- `FreqSpacing = 1 kHz`
- `NotchWidth = 1 kHz`
- `PhaseMode = Random`
- `Seed = 23`

### 4.2.1 当前 SGStudio 实际表达的信号

- 当前 generator 对每一根 active tone 独立生成随机相位，并用 `Seed` 初始化 `std::mt19937`。
- 对这组参数，active tones 仍是 `{-1 kHz, +1 kHz}`，区别只在于两根 tone 的相位不再要求相同。

它对应的信号可以写成：

$$
s[n] = A e^{j(\omega n + \phi_+)} + A e^{j(-\omega n + \phi_-)}
$$

只要 `\phi_+` 与 `\phi_-` 是独立随机相位，I/Q 就一般都会同时非零，不会再退化成 fixed 模式那种纯实余弦。这与 VSG60 random 模式的现象是一致的。

### 4.2.2 为什么 raw IQ 看起来不同

- 当前无法证明 SGStudio 与 VSG60 使用的是完全同一套 RNG、同一 double 取样口径和同一 tone 遍历顺序。
- 因而 `Seed = 23` 下，两边取到的具体相位数值未必完全相同，raw IQ 也就未必 bit-exact 相同。
- 此外，VSG60 仍按最小周期导出，SGStudio 仍按设备播放采样率导出。

对 random 模式来说，这一点尤其重要：只要任意一根 tone 的绝对相位不同，整段 IQ 的每个样本都会跟着变；因此“同名 seed”并不能自动推出“逐样本相同”。如果要做到这一点，必须连 RNG 算法、浮点映射方式、tone 遍历顺序都和 VSG60 完全一致。

### 4.2.3 为什么仍然判定正确

当前 random 之所以仍然可以判定正确，理由在于：

1. active tone 集与 notch 结果与 VSG60 对齐，仍然是同一组 `+-1 kHz` 两音。
2. 当前实现的业务语义是“seed 驱动的、可复现的、逐 tone 独立随机相位”，这正是 VSG60 random mode 的核心语义。
3. 当前 sample rate 与 sampleCount 仍然保证 exact-period 闭合，因此设备循环播放时不会引入额外边界失真。
4. playback/download 链路不会重算这些随机相位，因此设备实际下发的仍是 generator 给出的那一组随机相位结果。

换句话说，当前 `Random` 的正确性，不取决于能否复制 VSG60 内部 PRNG 的每一个细节，而取决于是否正确实现了“带 seed 的独立随机相位多音”这一产品语义。当前实现满足这一点。

### 4.2.4 结论

`Random` 已满足“可复现独立随机相位”的产品语义要求，可对标 VSG60 的 random mode；当前剩余差异主要是 RNG 口径是否需要继续追求 bit-exact，以及导出采样率策略不同。

## 4.3 Parabolic

对比文件：

- `multitone1KPara.csv`
- `Multitone_para.wav`

参数语义：

- `Count = 3`
- `FreqSpacing = 1 kHz`
- `NotchWidth = 1 kHz`
- `PhaseMode = Parabolic`

### 4.3.1 当前 SGStudio 实际表达的信号

- 当前 `Parabolic` 以 candidate lattice 的稳定序号为相位索引，并以原始 candidate tone 数量作为分母。
- 对 `Count = 3` 而言，candidate lattice 是 `{-1 kHz, 0, +1 kHz}`，索引分别是 `{0, 1, 2}`。
- `NotchWidth = 1 kHz` 去掉 DC 后，active tones 仍然保留它们原来的相位索引 `{0, 2}`。

当前相位律是：

$$
\phi_k = \pi \frac{k(k-1)}{N} + \phi_0
$$

此处 `N = 3`，因此两根 active tone 的相位是：

$$
\phi_0 = 0, \quad \phi_2 = \frac{2\pi}{3}
$$

也就是说，当前两根 active tone 的相对相位差是 `120°`。这正是这组两音特例下 Parabolic 语义真正应当体现的核心量。

### 4.3.2 为什么 raw IQ 看起来不同

- 当前这组 Parabolic 与 VSG60 的主要残余差异，不再是相对相位规律不同，而更接近于绝对相位参考不同。
- 对只剩一对等幅对称 tones 的场景，信号可以重写成：

$$
e^{j(\omega n + \phi_+)} + e^{j(-\omega n + \phi_-)}
= 2 e^{j(\phi_+ + \phi_-)/2} \cos\left(\omega n + \frac{\phi_+ - \phi_-}{2}\right)
$$

这个表达式里：

- $\frac{\phi_+ - \phi_-}{2}$ 决定相对相位模式。
- $e^{j(\phi_+ + \phi_-)/2}$ 只是在整个 IQ 平面上做全局复旋转。

因此，即便 SGStudio 与 VSG60 在绝对相位原点上不同，只要相对相位差仍然对应同一个 Parabolic 模式，raw IQ 的朝向不同也是允许的。

### 4.3.3 为什么仍然判定正确

当前 Parabolic 可以判定正确，原因在于：

1. active tone 集仍然是 `{-1 kHz, +1 kHz}`，DC notch 结果正确。
2. 当前相位律确实实现了这组参数下应有的 `120°` 相对相位差。
3. 对频谱而言，乘上一个统一的复常数只会整体旋转 IQ，不会改变各频点幅度；也就是说，tone 位置、功率分布和 notch 结果不会因此被破坏。
4. 对设备播放而言，playback 链路只是把这组当前相位结果原样下发，不会把 Parabolic 再改成别的模式。

所以，当前 Parabolic 的判定重点不应是“和 VSG60 的 IQ 朝向是否完全一致”，而应是“是否表达了同一个 candidate lattice 上的 Parabolic 相对相位规律”。当前实现满足这一点。

### 4.3.4 结论

当前 Parabolic 已经达到“相位模式语义对齐 VSG60”的目标；残余差异属于绝对相位参考，而不是模式逻辑错误。

## 5. 当前哪些现象不应误判为 bug

以下差异当前不应直接视为算法错误：

- VSG60 CSV 常按最小周期重复导出，而 SGStudio 当前按“对齐后的 sample rate + playback/RBW 驱动 exact-period sampleCount”导出。
- `Random` 模式下未确认与 VSG60 完全同一 RNG 口径，因此 raw IQ 未必逐样本一致。
- `Parabolic` 在两音特例下，当前仍可能与 VSG60 相差一个全局复旋转。
- 当前 htra multitone 默认等价于打开 `AutoScale`，因此不同 phase mode 的 crest factor 会映射成不同最终平均功率。
- playback 为满足设备最小 payload 约束而做整段重复补长，这会改变最终下载长度，但不会改变单个逻辑周期内部的 tone / phase 语义。

只要 tone 集、notch 结果、相对相位模式和播放链路保持一致，这些差异都属于可解释边界。

## 6. 哪些现象如果出现，才应判定为真正错误

与上面相反，下面这些现象如果出现，才说明当前实现真的有问题：

1. active tone 集与 VSG60 目标不一致，例如该保留的 tone 被删掉，或该删除的 DC tone 被保留下来。
2. `Fixed` 模式下本应等相位的 tones 被错误分配成不同相位。
3. `Random` 模式下相同 seed 不能稳定复现，或者所有 tones 实际共用同一个随机相位而退化成别的模式。
4. `Parabolic` 模式下 candidate lattice 的相对相位律不成立，导致相对相位差本身就错了。
5. sample rate 与 sampleCount 不能形成 exact-period 闭合，导致循环播放边界出现附加频谱。
6. playback/download 链路对 generator 的 IQ 做了重采样、重相位、重归一化或其他会改变模式语义的处理。

当前这三组对比里，没有观察到上述类型的问题。


## 7. 最终结论

基于本轮三组实际文件对比，可以得出以下结论：

- 当前 SGStudio htra 本地 multitone generator 已经正确实现了对标 VSG60 所需的核心相位模式、seed 和 notch 语义。
- `Fixed` 的差异主要来自采样率与导出段长不同；当前判断依据应是两边是否都表达了同一组等相位、去 DC 的 `+-tone`。
- `Random` 的差异主要来自 RNG 实现口径和采样率网格不同；当前判断依据应是是否实现了“seed 驱动、逐 tone 独立、可复现”的随机相位多音。
- `Parabolic` 的差异主要来自绝对相位参考；当前判断依据应是是否实现了同一个 candidate lattice 上的 Parabolic 相对相位规律。
