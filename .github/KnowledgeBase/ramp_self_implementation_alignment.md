# RAMP 自研实现与当前 HTRA 实现对齐说明

## 1. 目的

本文档回答三件事：

1. 当前在 `HTRA` 中新增的 `RampModulator` 到底是什么原理。
2. 这份实现是如何结合理论、参考数据和网上公开实现收敛出来的。
3. 后续继续补 business、panel 或导出链路时，哪些行为必须保持不变。

本文讨论的不是“未来可能扩展成什么样”，而是“当前已经写进 `src/plugins/htra/rampmodulator.cpp` 的实现到底是什么”。

当前直接相关的文件如下：

- `src/plugins/analog/analogmodulationplugin.cpp`：原始 `Ramp` 参数来源
- `src/plugins/analog/packing.cpp`：原始参数收口与采样率策略参考
- `src/plugins/analog/rampmodulator.cpp`：原第三方生成边界
- `src/plugins/htra/rampmodulator.h`
- `src/plugins/htra/rampmodulator.cpp`

其中，当前自研实现的核心锚点是 `src/plugins/htra/rampmodulator.cpp`。

---

## 2. 当前 HTRA 实现到底是什么

当前 `HTRA::RampModulator` 的本质，就是一个**以复基带 DC 为中心的单段上扫 LFM chirp 生成器**。

更具体地说：

- 它是 `complex baseband` 波形，不是单路实信号。
- 它的扫频区间由 `Span` 决定，映射为 `[-Span/2, +Span/2]`。
- 活动段是线性调频，也就是 `LFM chirp`。
- `Period > SweepTime` 时，活动段前后各带一个很短的 4-interval 半余弦窗边，然后补零尾段。
- `Period == SweepTime` 时，它表示一个长度为 `N = round(Fs * Period)` 的单周期唯一样本集合，不做端点复制。

所以如果用行业里最常见的术语来叫它，当前实现就是：

- `complex-baseband up-chirp LFM`

而不是：
- 任意起止频率 sweep generator
---

## 3. 参数模型与语义

当前实现仍然只接受三个参数：

- `Span`
- `SweepTime`
- `Period`

语义保持如下：

- `Span` 表示复基带总带宽
- `SweepTime` 和 `Period` 一律按秒解释
- 不引入额外的 `startFreq`、`stopFreq`、方向、重触发或尾段模式参数

这意味着当前 RAMP 不是“任意起止频率扫频器”，而是“由总扫宽和时间定义的、以 DC 为中心的单段上扫 chirp”。

---

## 4. 理论起点：它为什么是 LFM chirp

如果从连续时间理论描述当前实现，对应的是一个以 DC 为中心的线性扫频：

$$
f_{start} = -\frac{Span}{2}
$$

$$
f_{stop} = +\frac{Span}{2}
$$

$$
f(t) = -\frac{Span}{2} + \frac{Span}{SweepTime} t, \quad 0 \le t < SweepTime
$$

它的复包络可以写成：

$$
s(t) = A e^{j\phi(t)}
$$

$$
\phi(t) = 2\pi \left(-\frac{Span}{2} t + \frac{Span}{2\,SweepTime} t^2\right) + \phi_0
$$

从这个角度看，当前实现当然就是 `LFM chirp`。这一点不是类比，也不是近似，而是当前代码真实在实现的业务语义。

---

## 5. 当前代码真正采用的离散实现

### 5.1 为什么没有直接照抄连续时间公式

连续时间公式适合解释原理，但落到工程实现时，当前代码没有简单地在 `t = n / Fs` 上直接代值，而是采用了更贴近样本级行为的**离散相位递推**。

当前代码主干可以概括为：

1. `phase = 0`
2. `currentFreq = -Span / 2`
3. `N_sweep = round(Fs * SweepTime)`
4. `deltaFreq = Span / N_sweep`
5. 每个样本先写出：

$$
I[n] = round(32767 \cdot \cos(phase[n]))
$$

$$
Q[n] = round(32767 \cdot \sin(phase[n]))
$$

6. 然后执行离散更新：

$$
phase[n+1] = phase[n] + 2\pi \cdot \frac{f[n]}{F_s}
$$

$$
f[n+1] = f[n] + deltaFreq
$$

其中：

$$
f[0] = -\frac{Span}{2}
$$

$$
deltaFreq = \frac{Span}{N_{sweep}}
$$

这就是当前 `src/plugins/htra/rampmodulator.cpp` 里的离散生成主干。

### 5.2 它和离散闭式公式是什么关系

把上面的递推展开后，可以得到等价的离散闭式表达：

$$
\phi[n] = \frac{2\pi}{F_s} \left(n f_0 + \frac{n(n-1)}{2} \Delta f\right) + \phi_0
$$

其中：

$$
f_0 = -\frac{Span}{2}
$$

$$
\Delta f = \frac{Span}{N_{sweep}}
$$

所以当前代码虽然写成递推形式，本质上仍然是在实现一个离散时间的 LFM chirp，只不过用的是更工程化的写法。

---

## 6. 当前实现是如何收敛出来的

当前 HTRA 版本不是“拍脑袋从零写出来的”，而是由三类输入共同收敛出来的。

### 6.1 第一层：现有产品语义

来自当前仓库 `analog` 路径的约束有四条：

1. 参数只有 `Span / SweepTime / Period`
2. `Span` 应解释为总复基带带宽
3. `Period > SweepTime` 时应保留零尾段语义
4. 规范输出格式应保持为 `int16` 交织 IQ

这决定了自研实现不能改成任意起止频率 chirp，也不能改成实信号或别的输出格式。

### 6.2 第二层：理论原理

理论层面提供了两件事：

1. 当前波形本质上就是复基带 `LFM chirp`
2. 连续时间描述要落到样本级时，应改写为离散相位递推或等价的离散闭式公式

换句话说，理论决定了“是什么”，也决定了“代码不能只停留在连续时间公式说明上”。

### 6.3 第三层：VSG60 参考数据

`data/Ramp_1ms.csv` 提供了 `SweepTime < Period` 的参考：

- 活动段是以 DC 为中心的单段上扫 chirp
- 活动段后面是零尾段
- 活动段与零尾段交界处存在很短的 half-cosine / raised-cosine 风格边界窗

`data/ramp_1ms_1ms.csv` 提供了 `Period == SweepTime` 的参考：

- 单周期导出不做端点复制
- 首样本和末样本不必相同

这两份数据一起把实现边界钉住了：

- 主干必须是离散 LFM + 可选零尾段
- 不应再额外做“末样本强制等于首样本”的端点复制

### 6.4 第四层：网上公开实现的借鉴

之前参考过的网上公开实现，真正被吸收到当前代码里的主要只有两类思想：

1. 显式 LFM 相位公式与整数量化思路
2. 按“活动段 + 周期剩余段”来组织单周期缓冲区的结构

它们的作用主要是帮助确认：

- `LFM chirp` 的数学骨架应该怎么写
- `int16 IQ` 的量化链路应该怎么组织
- 单周期构造时应先把活动段定义清楚，再决定尾段如何处理

但下面这些内容没有进入当前实现：

- HFM
- 三角扫 / 双向扫
- 雷达回波、多普勒、匹配滤波链路
- 额外业务参数

---

## 7. 当前代码里的具体策略

### 7.1 参数收口

当前设备相关参数收口由 `RampModulation` business 完成，`RampModulator` 只保留设备无关基础合法化：

- `Span >= 1 kHz`
- `Period >= 1 us`
- `1 us <= SweepTime <= Period`

Span上限来自current Playback domain：model 132为100 MHz；扩展型号连续档为160 MHz，并额外允许精确320 MHz对应400 MSPS。

Ramp不再保留固定1秒Period上限。business先计算：

$$
F_{s,min,ramp}(Span)=\max(1.25\times Span, F_{s,device,min})
$$

$$
N_{max}=\left\lfloor\frac{maxWaveformBytes}{bytesPerComplexSample}\right\rfloor
$$

$$
Period_{max}(Span)=\frac{N_{max}}{F_{s,min,legal}(Span)}
$$

因此低Span/低采样率可以生成超过1秒的波形，高Span/高采样率会自动得到更短的Period上限。例如无带宽选件档在20 MHz Span、25 MSPS下约为1.31072秒；含带宽选件档同一Span下约为10.48576秒。最终点数和最小下载补齐仍由`makePayloadLayout()`执行不可绕过的硬容量检查。

### 7.2 采样率策略

当前采样率与离散时长由business统一解析：

1. Span先按current非连续domain收口，确定`minimumFs >= 1.25 * Span`。
2. 连续档中计算`N = ceil(minimumFs * Period)`与`Fs = N / Period`；若该Fs仍在current domain且N不超过容量，则精确保留用户Period。
3. 若上述候选越过连续档上边界或payload容量，则固定`minimumFs`，把Period量化回写为`N / Fs`。
4. 精确320 MHz Span固定使用400 MSPS离散点；普通160~320 MHz输入回写160 MHz连续档，不把空洞当连续能力。
5. SweepTime也量化为明确活动段样点数，并保证不超过Period点数。

business最终提交包含`sampleRate / activeSampleCount / periodSampleCount / payload layout`的Ramp专用plan。generator只消费该整数plan，不读取设备能力，也不再次通过double独立决定样点数。Streaming 62.5 MSPS不参与Ramp Playback决策。

### 7.3 `Period > SweepTime`

当 `Period > SweepTime` 时，当前实现语义是：

1. 先生成活动 chirp 段
2. 在活动段起止各施加一个 4-interval 半余弦窗边
3. 剩余样本全部补零

也就是：

$$
N_{sweep} = round(F_s \cdot SweepTime)
$$

$$
N_{period} = round(F_s \cdot Period)
$$

$$
x[n] = 0, \quad n \in [N_{sweep}, N_{period})
$$

其中，当 `Period > SweepTime` 时，活动段增益按边界距离再乘一个局部窗函数：

$$
g(k) = \frac{1}{2}\left(1 - \cos\left(\pi \frac{k}{4}\right)\right), \quad k = 0,1,2,3,4
$$

起始端按到活动段起点的距离取值，结束端按到活动段终点的距离取值；中间区域增益保持为 1。

这意味着当前实现不会在尾段保持末频点，也不会重复第二段 chirp，但会在“活动段 <-> 零尾段”的交界处做一个很短的幅度平滑。

### 7.4 `Period == SweepTime`

按对齐 VSG60 的最小修改，当前实现已经删除了 `strictClosure` 特判。

这意味着：

- `Period == SweepTime` 不再走单独的端点同值分支
- 不再把 `deltaFreq` 改成 `Span / (N - 2)`
- 不再把最后一个量化样本强制回写成首样本

现在它与普通活动段生成共用同一条主干：

- `deltaFreq = Span / N_sweep`
- 缓冲区表示一个长度为 `N` 的单周期唯一样本集合
- 一个周期按半开区间 `[0, T)` 理解

这更符合仪表行业里单周期波形导出的常见做法，也更符合离散时间周期序列的数学定义：

$$
x[n+N] = x[n]
$$

而不是要求：

$$
x[N-1] = x[0]
$$

### 7.5 输出格式

当前自研实现的规范输出仍然是 `int16` 交织 IQ：

- `I0, Q0, I1, Q1, ...`

完整IQ直接写入唯一`PlaybackPayloadBuilder`，发布后作为immutable payload用于metrics、下发与保存，不建立完整`QVector<float>`或第二份IQ副本。Preview只复制前65536个complex sample。

---

## 8. 当前样本校验结论

### 8.1 `data/Ramp_1ms.csv`

这份样本对应 `SweepTime < Period` 场景，支持下面两个结论：

1. 活动段是以 DC 为中心的单段上扫 chirp。
2. 活动段之后是零尾段。
3. 活动段与零尾段交界处带有很短的 half-cosine 风格边界窗。

这与当前 `HTRA` 实现的主干方向一致。

### 8.2 `data/ramp_1ms_1ms.csv`

这份样本是 `Period == SweepTime` 的单周期导出。直接可见：

- 总样本数：`50000`
- 首样本：`32767,0`
- 末样本：`-26509,-19262`

因此它说明了一件很重要的事：

- VSG60 的单周期导出并不要求首样本与末样本完全相同

从当前实现的视角看，这不是“波形错了”，而是“单周期导出没有做端点复制”。

也正因为如此，当前代码删除 `strictClosure` 才是对齐 VSG60 的最小修改。

---

## 9. 当前明确不做的事

当前实现故意不引入下面这些内容：

- `startFreq/stopFreq` 新参数
- 边界频率连续或斜率连续承诺
- HFM / 三角扫 / FMCW 雷达处理链

这些都不属于当前已落地实现的一部分。

---

## 10. 当前应保持不变的硬规则

后续如果继续补 business、panel 或导出链路，下面这些点不应被改掉：

1. `Span` 继续解释为总复基带带宽，并映射到 `[-Span/2, +Span/2]`。
2. `SweepTime` 和 `Period` 继续按秒解释。
3. 当前实现必须继续被理解为复基带单段上扫 `LFM chirp`。
4. `Period > SweepTime` 时，活动段与零尾段交界处继续保留 4-interval 半余弦窗边，尾段继续补零。
5. `Period == SweepTime` 时，继续按半开区间单周期导出理解，不做端点复制，也不引入零尾段场景专用 taper。
6. 规范输出继续保持为 `int16` 交织 IQ。
