# 16QAM Raw Cloud 与符号星座图解释

本文档总结两份 16QAM 基带 wav 的离线观察结果，目的是把“raw cloud 是什么”“为什么 oversample=32 + RRC 会显得更 dense、更 smooth”“为什么最终仍能收敛回理想 16QAM”说明清楚，便于交给熟悉数字通信或射频基带的人进一步复核。

## 分析对象

- 文件一：[../../data/16QAMOverSample4.wav](../../data/16QAMOverSample4.wav)
  - 16QAM
  - SymbolRate = 1 MHz
  - FilterType = Rectangular
  - Oversample = 4
- 文件二：[../../data/16QAMOversample32.wav](../../data/16QAMOversample32.wav)
  - 16QAM
  - SymbolRate = 1 MHz
  - FilterType = RootRaisedCosine
  - FilterAlpha = 0.35
  - FilterLength = 12
  - Oversample = 32

本次离线产物：

- 新旧对比图（SVG）：[../../data/16QAMOversample32_vs_16QAMOverSample4_compare.svg](../../data/16QAMOversample32_vs_16QAMOverSample4_compare.svg)

相关导出代码：

- Digital Modulation wav 保存逻辑：[../../src/plugins/analog/digitalmodulation.cpp](../../src/plugins/analog/digitalmodulation.cpp)
- WAV header/custom chunk 写入逻辑：[../../src/libs/utils/wavheader.cpp](../../src/libs/utils/wavheader.cpp)

## 先区分两个概念

### 理想符号星座图

理想 16QAM 的符号集合本身是离散的 16 个点：

$$
a_k \in \{\pm 1, \pm 3\} + j\{\pm 1, \pm 3\}
$$

如果观察的是“每个符号在最佳判决时刻的复数值”，那么图上应当只剩这 16 个点附近的聚类。

### Raw Cloud

Raw cloud 不是判决点图，而是“整条成形后基带波形的全部 IQ 采样点，在复平面上的占据分布”。

也就是说，raw cloud 画的是：

$$
(\Re\{s[n]\}, \Im\{s[n]\})
$$

其中 $s[n]$ 不是理想符号序列，而是经过过采样与脉冲成形后的发端波形。

## Raw Cloud 的形成机制

在离散时间下，可以把发端基带写成：

$$
s[n] = \sum_k a_k g[n-kL]
$$

其中：

- $a_k$ 是第 $k$ 个 16QAM 复符号
- $g[\cdot]$ 是发端脉冲成形滤波器
- $L$ 是 oversample，也就是每个符号对应的采样点数

这意味着某个时刻的 IQ 样本一般不只由“当前符号”决定，还会受到前后多个符号的影响。

因此 raw cloud 里通常会同时存在两类点：

1. 接近理想 16QAM 点的位置：这些点通常来自符号中心附近，前后符号干扰较小。
2. 连接理想点之间的过渡轨迹：这些点来自波形从前一个符号平滑过渡到后一个符号的过程。

所以 raw cloud 的本质是“复平面中的轨迹占据图”，不是“最终星座判决图”。

## 为什么最终仍会收敛回理想 16QAM

理想 16QAM 的 16 个判决点并没有因为 oversample 或滤波器参数而改变。改变的是：

- 发端波形在符号之间如何过渡
- 接收端需要做多少恢复处理，才能把这些过渡重新压回判决点

对于 RootRaisedCosine，标准思路不是直接对发端输出做判决，而是做“匹配滤波 + 正确定时抽样”。

如果发端是 RRC，接收端再补一个同参数 RRC，则整体等效接近 Raised Cosine，在理想条件下可以实现符号时刻零 ISI。此时再做正确相位抽样，点就会重新收敛回 16QAM 的 16 个理想位置。

## 为什么 os=32 会更 dense

这是最直接的一点：同样一条波形轨迹，被采得更密了。

文件一每个符号只有 4 个采样点；文件二每个符号有 32 个采样点。若符号数相同，那么文件二仅从采样数量上就比文件一多 8 倍的点。

因此：

- 同一条过渡轨迹，os=4 只会留下少量离散点。
- 同一条过渡轨迹，os=32 会留下非常密集的点列。

所以 dense 首先是“采样更密”的直接结果，而不是星座本身更复杂。

## 为什么 RRC + os=32 会更 smooth

smooth 主要由滤波器决定，oversample 负责把这种平滑展示得更完整。

### Rectangular 的效果

Rectangular 更接近“保持当前符号值一段时间，再快速切换”。

因此它的 raw cloud 更像：

- 在理想点附近停留
- 在相邻点之间做较短、较硬的切换

### RootRaisedCosine 的效果

RRC 会主动抑制高频突变，把符号之间的跳变摊开成更平滑的时域过渡。于是 IQ 轨迹在复平面上不再像“硬跳”，而更像“沿一条连续曲线滑过去”。

再叠加 os=32：

- 每条平滑曲线被更细致地采样
- 相邻采样点之间差别更小
- 视觉上就会表现为更 dense、更 smooth、更 thick 的轨迹云

因此，文件二的 raw cloud 更 dense、更 smooth，不是因为它偏离了 16QAM，而是因为它把“成形后的连续轨迹”显示得更充分。

## 参数如何影响 Raw Cloud

### Oversample

Oversample 主要决定每个符号周期内，你观察到多少个时刻的样本。

- 增大 oversample：raw cloud 更密，时间细节更多，轨迹更连续。
- 减小 oversample：raw cloud 更稀，轨迹更像离散跳点。

Oversample 不改变理想 16QAM 的 16 个判决点，但会改变 raw cloud 的“采样密度”和“可见细节”。

### Filter Type

FilterType 决定符号间过渡的形状。

- Rectangular：停留型更明显，过渡更硬。
- Raised Cosine / RootRaisedCosine：过渡更平滑，轨迹更连续。
- Gaussian / HalfSine：也会显著改变轨迹形状，但它们对 QAM 星座恢复的直觉性通常不如 RC/RRC。

### Filter Alpha

Alpha 决定滚降大小，也影响时域尾巴长度。

- Alpha 较小：频谱更窄，时域脉冲尾巴更长，raw cloud 往往更拖尾、更依赖前后符号。
- Alpha 较大：时域收敛更快，轨迹更紧一些，但带宽更宽。

### Filter Length

FilterLength 决定滤波器跨越多少个符号。

- Length 更大：单个样本受更多邻居符号影响，raw cloud 更“厚”、更有记忆。
- Length 更小：过渡更短，但更接近截断滤波器，符号恢复余量可能更小。

## 本次两份文件的直接观察

### 文件一：Rectangular + os=4

- Raw 相位抽样最佳相位：1
- 简单归一化后到最近理想 16QAM 点的均方距离：0.0

这说明在该文件中，直接按最佳相位抽样就能落到理想点上。换句话说，这个场景下 raw 抽样已经足够代表最终星座。

### 文件二：RRC + os=32

- Raw 相位抽样最佳相位：0
- 未做匹配滤波时的最近理想点均方距离：约 0.184004
- 做 RRC 匹配滤波并做群时延裁剪后：约 0.002586

这说明文件二如果只看 raw 相位抽样，点云仍明显带有过渡/ISI 残留；但一旦补上合理的接收处理，星座会明显收紧并回到接近理想 16QAM 的状态。

## 对比图应如何解读

[../../data/16QAMOversample32_vs_16QAMOverSample4_compare.svg](../../data/16QAMOversample32_vs_16QAMOverSample4_compare.svg) 可以按下面的方式理解：

1. 左上：Rectangular + os=4 的全采样 raw cloud。
2. 右上：Rectangular + os=4 的符号抽样图，已经收敛到理想 16QAM。
3. 左下：RRC + os=32 的全采样 raw cloud，更密、更厚、更平滑。
4. 右下：RRC + os=32 在匹配滤波后的符号抽样图，重新收敛到很紧的 16QAM 点簇。

对专业人士来说，这组图更接近“发端波形成形视角”和“接收端判决视角”的对照，而不是两种不同调制方式的对照。

## 本次结论的边界与局限

以下几点需要单独说明，避免把这份分析误当成更严格的通信性能测试结论：

1. 文中使用的 MSE 不是标准 EVM。
   这里的数值是“归一化后到最近理想 16QAM 电平点的均方距离”，适合做相对对比，不等同于标准仪表或协议定义下的 EVM/BER 指标。

2. 本次分析对象是本项目导出的理想基带 wav。
   未额外引入载波频偏、相位噪声、IQ imbalance、采样时钟偏差、模拟链路失真等真实接收机问题。

3. 对 RRC 文件使用了离线匹配滤波和群时延裁剪。
   这是合理且接近行业习惯的做法，但仍是“离线理想恢复”，不是完整接收机同步链。

## 结论

这次对比图表达的核心不是“RRC + os=32 的 16QAM 更差”，而是：

- raw cloud 观察的是“成形后整条基带波形走过哪些 IQ 位置”
- 理想星座观察的是“最佳判决时刻的符号点”

因此：

1. os=32 让同一条轨迹被更密地采样，所以 raw cloud 更 dense。
2. RRC 让符号过渡更平滑，所以 raw cloud 更 smooth。
3. 这两者不会改变 16QAM 的理想判决点集合。
4. 当接收处理做对了，尤其是匹配滤波和正确定时抽样之后，点仍会收敛回理想 16QAM。

这也是为什么“raw cloud 看起来差别很大”，而“最终符号星座仍然可以很接近理想状态”。