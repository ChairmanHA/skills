# AM 复基带自研实现与参考行为对齐说明

## 1. 目的

本文档用于明确 AM 复基带生成在 SGStudio 中的目标实现边界，回答三件事：

1. 当前 AM 波形在业务语义上到底是什么。
2. 自研算法应如何从参数映射到离散 IQ。
3. 后续替换第三方 `GenerateAmWaveform(...)` 时，哪些行为必须保持不变。

本文基于静态分析和参考 CSV 反推整理，不包含编译或运行验证结论。

---

## 2. 当前链路与实现边界

当前 AM 生成路径锚点如下：

- `src/plugins/analog/ammodulator.cpp`
- `src/plugins/analog/packing.cpp`

当前 worker 链路核心是：

1. 对 `Rate / Depth / Shape` 做参数收口。
2. 计算 AM 采样率。
3. 调用第三方 `GenerateAmWaveform(...)` 得到 `int16` 交织 IQ。

第三方仅暴露接口，不提供源码：

```cpp
struct AMModParams
{
    double Rate;
    double Depth;
    ModShape Shape;
};

int GenerateAmWaveform(int objId, const AMModParams *paramsIn,
                       double sampleRate, short **iq, int32_t *lenOut);
```

因此当前文档中的“算法行为”属于可落地的对齐目标，不是第三方内部源码释义。

---

## 3. 当前 AM 波形到底是什么

当前 AM 在本工程中的正确语义是复基带包络，不是 passband 实载波。

也就是说，生成器应输出：

$$
x(t) = A \cdot \left(1 + m \cdot shape(2\pi \cdot Rate \cdot t)\right)
$$

$$
I(t) = x(t), \quad Q(t) = 0
$$

而不是在 IQ 文件内再构造 `fc` 载波。

这与发射机链路分工一致：RF 搬移由设备频率、DUC 和射频链路负责。

---

## 4. 参数模型与量化口径

### 4.1 参数归一化

$$
rate = clamp(Rate, 1\ \text{Hz}, 10\ \text{MHz})
$$

$$
depth = clamp(Depth, 1\%, 100\%),\quad m = depth / 100
$$

### 4.2 输出幅度口径

参考数据收敛到：

$$
baseScale = 16384
$$

量化前包络：

$$
envelope[n] = 1 + m \cdot shape[n]
$$

量化后：

$$
I[n] = clip\left(round\left(16384 \cdot envelope[n]\right), -32768, 32767\right),\quad Q[n] = 0
$$

当 `Depth = 100%` 时，正峰会触及 `32768`，需要裁剪到 `32767`，这是正常的 `int16` 边界行为。

---

## 5. Shape 离散定义

令 $u = n/N$，`N` 为单周期 complex sample 数。

### 5.1 Sine

$$
shape = sin(2\pi u)
$$

### 5.2 Square

$$
shape = \begin{cases}
+1, & 0 \le u < 0.5 \\
-1, & 0.5 \le u < 1
\end{cases}
$$

### 5.3 Triangle

$$
shape = \begin{cases}
-1 + 4u, & u \le 0.5 \\
3 - 4u, & u > 0.5
\end{cases}
$$

### 5.4 Ramp

$$
shape = -1 + 2u
$$

该定义对应上升锯齿：从最小包络线性上升到接近最大包络，在周期边界回跳。

---

## 6. 采样率与每周期点数策略

当前推荐在工程上显式区分三种策略，避免“匹配参考设备”与“利用本机最高采样率”混用。

### 6.1 Vsg60Exact（用于对齐 CSV）

- `FsMax = 50 MS/s`
- `MaxSamplesPerPeriod = 16384`
- `MinSamplesPerPeriod = 4`
- 点数按 `floor_power_of_two(FsMax / Rate)` 取整，再夹紧到 `[4, 16384]`

因此：

- `Rate = 1 kHz` -> `N = 16384`, `Fs = 16.384 MS/s`
- `Rate = 1 MHz` -> `N = 32`, `Fs = 32 MS/s`
- `Rate = 10 MHz` -> `N = 4`, `Fs = 40 MS/s`

### 6.2 Tx125Quality（用于本机高保真）

- `FsMax = 125 MS/s`
- shape 与量化模型不变
- 高频率段会得到更大的 `N`，不追求逐点匹配 VSG60 CSV

### 6.3 CurrentMaxRate（兼容当前行为）

- 沿用 `calculateAMSampleRate(rate)`
- 保持既有系统策略，不改变历史行为

---

## 7. 参考样本对齐结论

已用于收敛当前模型的样本：

- `data/amDefault.csv`（`Rate=1k, Depth=50%, Sine`）
- `data/am1M.csv`（`Rate=1M, Depth=90%, Triangle`）
- `data/amSquare.csv`
- `data/amramp.csv`
- `data/am10M.csv`

关键结论：

1. `Q` 路为 0，AM 数据是包络型复基带。
2. `baseScale=16384` 与 `Depth` 百分比定义可稳定解释峰值和均值。
3. `Sine` 起点是均值，不是最小值。
4. `Square` 首半周期高电平。
5. `Ramp` 是上升锯齿。
6. 高 `Rate` 下每周期点数会收敛到 4，而不是固定 16384。

---

## 8. 当前明确不做的事

当前 AM 自研对齐范围不包含：

1. 在 IQ 数据中生成 RF 载波 `fc`。
2. 引入额外形状参数（偏置、相位偏移、非线性斜率等）。
3. 在本次文档中承诺所有设备型号下的运行时频谱指标。

---

## 9. 必须保持不变的硬规则

后续落地代码时，下列行为应视为硬边界：

1. 输出格式保持 `int16` 交织 IQ：`I0,Q0,I1,Q1...`。
2. AM 业务语义保持为复基带包络，`Q=0`。
3. `Rate/Depth/Shape` 参数面保持与当前 `AmModulator` 一致。
4. `Sine/Square/Triangle/Ramp` 的离散定义保持本文口径。
5. 采样率策略要可配置区分（至少能区分对齐策略和本机质量策略）。
6. 若替换第三方 API，应先完成 CSV 对齐验证，再切默认路径。

---

## 10. 迁移建议（实现顺序）

1. 先新增本地 helper，不直接移除第三方路径。
2. 以 `Vsg60Exact` 做离线样本对齐。
3. 在调试开关下支持第三方与本地算法切换。
4. 待偏差稳定后再替换默认生成实现。
