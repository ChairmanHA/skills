# RAMP 自研实现参考实现补充记录

## 本次输入

用户最初补充了 3 组从 GitHub 搜到的 LFM chirp 参考实现；当前已明确删除第一组，后续只保留后两组作为参考：

1. 显式 `LFM/HFM phase + 整数量化 + FFT/谱图验证` 的 Python 脚本
2. `complex triangular FMCW chirp + 回波/噪声/匹配滤波` 的 MATLAB 脚本

另有一个新的外部事实源：

- `data/Ramp_1ms.csv`，来自 Signal Hound VSG60 导出
- 参数：`Span=40 MHz`、`SweepTime=500 us`、`Period=1 ms`、`Fs=50 MHz`

## 本次目标

- 不改动当前文档已经定义好的 RAMP 兼容语义主线。
- 在 `.github/KnowledgeBase/ramp_self_implementation_alignment.md` 中补两类内容：
  - 从保留下来的两组参考实现中摘出真正可复用的核心代码骨架
  - 基于 `Ramp_1ms.csv` 判断当前实施方案是否走歪、哪些点可能过度设计
- 输出要分清：
  - 哪些部分可直接借鉴到当前 `Analog -> Ramp`
  - 哪些部分只能作为验证脚本或未来扩展参考
  - 哪些部分不应直接照搬到当前产品

## 归纳结论

### 参考实现 1：显式 LFM/HFM 相位 + 量化

- 这是最接近“自研生成内核骨架”的参考。
- 可直接借鉴：
  - `N = round(T * fs)` 的离散采样点数求法
  - 线性调频相位公式
  - 幅度裁剪与整数量化流程
  - FFT / dB FFT / spectrogram 的离线检查脚本
- 需要改造后才能用：
  - 当前代码要的是复基带 IQ，不是单路 `sin(phase)`
  - 当前语义用 `Span` 映射到 `[-Span/2, +Span/2]`
  - 当前要求 `Period > SweepTime` 时尾段补零
  - 当前规范输出是 `int16` 交织 IQ，而不是单声道整数数组
- HFM 属于未来扩展，不属于当前 RAMP 兼容目标

### 参考实现 2：MATLAB 三角 FMCW chirp

- 这是最接近“复杂 chirp 业务组织方式”的参考，而不是当前产品的直接模板。
- 可借鉴：
  - 复信号 chirp 的思路
  - 用分段门控把一个周期拆成多个子段再拼接
  - 周期级重复构造的组织方式
- 不应直接照搬：
  - 三角上扫/下扫 FMCW 语义
  - `fc` / `fo` 这类 RF/雷达中心频率语义
  - 噪声、回波、多目标、匹配滤波等雷达链路

### 基于两组参考实现，后续可直接摘用的核心代码

当前真正值得带进实现主线的，不是整份脚本，而是下面两段骨架。

第一段来自“显式 LFM 相位 + 整数量化”脚本，但已经按当前业务语义改写为复基带版本：

```python
import math

def generate_ramp_iq_core(fs_hz, span_hz, sweep_time_s, period_s, amplitude=0.99997):
  n_sweep = int(round(fs_hz * sweep_time_s))
  n_period = int(round(fs_hz * period_s))
  f0 = -0.5 * span_hz
  slope = span_hz / sweep_time_s
  max_int = 32767

  iq = [(0, 0)] * n_period
  phase = 0.0

  for n in range(n_sweep):
    i = round(math.cos(phase) * amplitude * max_int)
    q = round(math.sin(phase) * amplitude * max_int)
    iq[n] = (i, q)

    inst_freq = f0 + slope * (n / fs_hz)
    phase += 2.0 * math.pi * inst_freq / fs_hz

  return iq
```

这段代码保留了最该保留的东西：

- `N = round(Fs * T)` 的离散样本数口径
- 显式频率斜率 `slope = span / sweep_time`
- 整数量化输出

同时把它改成了当前真正需要的形式：

- `Span -> [-Span/2, +Span/2]`
- 复基带 `I/Q`
- `int16` 量化目标

第二段来自 MATLAB 的“分段构造周期”思路，用来表达当前 `SweepTime < Period` 的业务结构：

```python
def build_ramp_period(fs_hz, span_hz, sweep_time_s, period_s):
  iq = generate_ramp_iq_core(fs_hz, span_hz, sweep_time_s, period_s)

  n_sweep = int(round(fs_hz * sweep_time_s))
  n_period = int(round(fs_hz * period_s))

  for n in range(n_sweep, n_period):
    iq[n] = (0, 0)

  return iq
```

这段代码的价值不在“写法多高级”，而在它把当前产品语义拆得很清楚：

1. 前半段是活动 chirp
2. 后半段是零尾段
3. 最终返回的是一个完整周期

这个组织方式应保留。

---

### `Ramp_1ms.csv` 对当前方案的实际约束

对 `data/Ramp_1ms.csv` 的离线分析，当前已经能支持下面几条明确结论。

#### 1. 主语义没有走歪

`Ramp_1ms.csv` 的参数是：

- `Span = 40 MHz`
- `SweepTime = 500 us`
- `Period = 1 ms`
- `Fs = 50 MHz`

CSV 共 `50000` 行，正好对应：

$$
N_{period} = 50\text{M} \times 1\text{ms} = 50000
$$

其中从中点开始一直到末尾全部为 `0,0`，说明：

- `Period > SweepTime` 时，VSG60 确实采用“活动 chirp + 零尾段”
- 这一点与当前文档主线完全一致

进一步对活动段做相位导数估计，可见其瞬时频率轨迹近似为：

- 起点约 `-19.99 MHz`
- 中点过 `0 Hz`
- 终点约 `+19.99 MHz`

这和：

$$
f_0 = -\frac{Span}{2}, \quad f_1 = +\frac{Span}{2}
$$

完全对齐。因此，当前文档对 `Span` 的解释没有走歪。

#### 2. 对样本级实现，离散相位递推比连续时间直接采样更贴近 VSG60

若只看概念说明，前文使用的连续时间 LFM 相位公式没有问题；但若目标是更贴近 `Ramp_1ms.csv` 这样的导出文件，那么实现时更应该借鉴“离散递推积分”的写法。

对当前参数，VSG60 的主干样本与下面这个离散公式在去掉首尾极少数边缘样本后，可做到约 2 LSB 内一致：

$$
\phi[n] = 2\pi \left( \frac{f_0 n}{F_s} + \frac{k\,n(n-1)}{2F_s^2} \right) + \phi_0
$$

其中：

$$
k = \frac{Span}{SweepTime}
$$

这说明对“后续实际编码”来说，最值得摘用的核心不是：

- 直接把连续时间 `0.5kt^2` 在 `t = n/Fs` 处硬采样

而是：

- 按每个采样点的瞬时频率做离散相位累加
- 或等价地使用上面的离散闭式相位公式

这不是主语义改变，而是**实现细节的校正**。当前方案需要补上的，正是这一点。

#### 3. VSG60 在活动段首尾加了极短的边缘门控，但这不应成为第一阶段的主目标

`Ramp_1ms.csv` 还有一个很有价值的细节：

- 活动段开头不是立刻满幅，而是约 4 个样本从 0 平滑抬起
- 活动段末尾也不是直接硬切零，而是约 4 个样本平滑落下

对应的归一化幅度大致为：

- `0`
- `0.146446`
- `0.5`
- `0.853553`
- `1.0`

这非常像一个 4-sample 的半余弦/raised-cosine 边缘。

因此，若目标是更贴近 VSG60 导出文件，可额外提供一个**可选的短边缘 taper**；但这应被视为：

- 导出兼容优化
- 或减少 `chirp -> zero tail` 硬切边缘的微小修饰

而不是当前 RAMP 语义的核心定义。

换句话说，这部分**不值得在第一阶段过度设计**。先把下面三件事做对，比追 4 个样本的边缘形状更重要：

1. `Span -> [-Span/2, +Span/2]`
2. 离散相位递推的 LFM 主干
3. `SweepTime < Period` 时的零尾段

#### 4. 当前最可能过度设计的点

基于这份 VSG60 数据，当前方案里最值得收敛的点有两个。

第一，不要把 HFM、三角 FMCW、雷达回波链路提前带进实现。

这份 CSV 强烈说明当前主路径只是：

- 单段上扫复基带 chirp
- 后接零尾段

因此，前面保留那些内容只能作为“未来参考”，不应成为当前代码结构设计的驱动因素。

第二，不要把“周期边界相位闭合”当成当前第一优先级。

原因不是它错了，而是：

- `Ramp_1ms.csv` 这个例子本身带有零尾段
- 它只能验证 `Span` 语义、离散相位主干和零尾段
- 它无法验证 `Period == SweepTime` 场景下的首尾闭合是否一定要做专门求解

因此，对当前实现优先级更合理的排序应当是：

1. 先实现与 VSG60 主干一致的离散 LFM + 零尾段
2. 再视需要决定是否补短边缘 taper
3. 最后再考虑 `Period == SweepTime` 时是否需要更严格的首尾闭合策略

## 准备落到文档里的综合建议

- 当前自研 RAMP 最合适的合成方案是：
  - 用保留下来的 Python 参考实现里的 LFM 相位与量化骨架做生成内核
  - 但把相位离散化方式修正为更贴近 VSG60 的离散递推积分口径
  - 用 MATLAB 参考实现的“分段拼装周期”思路处理 `SweepTime` 与 `Period`
  - 把 VSG60 中观察到的 4-sample 边缘 taper 视为可选兼容层，而不是第一阶段核心需求
- 当前文档中应明确写出：
  - 真正可复用的是“方法”，不是“参数语义”或“完整业务结构”
  - 当前产品仍然是“单段上扫 chirp + 可选零尾段”，不是三角 FMCW
  - `Ramp_1ms.csv` 已经验证主语义正确，但也提示实现细节应优先采用离散相位递推

## 本次变更边界

- 只更新 `.github/KnowledgeBase/ramp_self_implementation_alignment.md` 与本 TaskLog
- 不恢复 `Index.md` 中已被用户撤销的改动