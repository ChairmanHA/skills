# FM 复基带自研实现与参考行为对齐说明

## 1. 目的

本文档用于明确 FM 复基带生成在 SGStudio 中的目标实现边界，回答三件事：

1. 当前 FM 波形在业务语义上到底是什么。
2. 自研算法应如何从参数生成离散 IQ。
3. 后续替换第三方 `GenerateFmWaveform(...)` 时，哪些行为必须保持不变。

本文基于静态分析和参考 CSV 反推整理，不包含编译或运行验证结论。

---

## 2. 当前链路与实现边界

当前 FM 生成路径锚点如下：

- `src/plugins/analog/fmmodulator.cpp`
- `src/plugins/analog/packing.cpp`

当前 worker 链路核心是：

1. 对 `Rate / Deviation / Shape` 做参数收口。
2. 计算 FM 采样率。
3. 调用第三方 `GenerateFmWaveform(...)` 得到 `int16` 交织 IQ。

第三方仅暴露接口，不提供源码：

```cpp
struct FMModParams
{
    double Rate;
    double Deviation;
    ModShape Shape;
};

int GenerateFmWaveform(int objId, const FMModParams *paramsIn,
                       double sampleRate, short **iq, int32_t *lenOut);
```

因此本文给出的“算法行为”是本地可落地的对齐目标，不是第三方内部实现源码释义。

---

## 3. 当前 FM 波形到底是什么

当前 FM 在本工程中的正确语义是复基带恒包络 IQ，相位由瞬时频偏积分得到。

离散模型核心是：先生成 `shape[n]`，再把频偏积分为相位。

$$
f_{offset}[n] = Deviation \cdot shape[n]
$$

$$
\phi[n] = \phi[n-1] + 2\pi \cdot \frac{f_{offset}[n]}{F_s}
$$

$$
I[n] = round(32767 \cdot cos(\phi[n])),\quad Q[n] = round(32767 \cdot sin(\phi[n]))
$$

该语义对应复基带调频，不在 IQ 文件内生成 RF 载波。

---

## 4. 参数模型与量化口径

### 4.1 参数归一化

$$
rate = clamp(Rate, 1\ \text{Hz}, 10\ \text{MHz})
$$

$$
deviation = clamp(Deviation, 1\ \text{Hz}, 10\ \text{MHz})
$$

### 4.2 输出幅度口径

FM 输出采用恒包络满幅近似：

$$
amplitude = 32767
$$

因此应保持：

$$
I[n]^2 + Q[n]^2 \approx 32767^2
$$

这与 AM 的 `baseScale=16384` 口径不同。

---

## 5. Shape 离散定义

令 $u = n/N$，`N` 为单周期 complex sample 数。

### 5.1 Sine

$$
shape = sin(2\pi u)
$$

当 $\beta = Deviation/Rate$ 时，相位趋势可由下式校验：

$$
\phi(u) \approx \beta \cdot (1 - cos(2\pi u))
$$

### 5.2 Square

$$
shape = \begin{cases}
+1, & 0 \le u < 0.5 \\
-1, & 0.5 \le u < 1
\end{cases}
$$

语义是先正频偏、后负频偏。

### 5.3 Triangle

$$
shape = \begin{cases}
-1 + 4u, & u \le 0.5 \\
3 - 4u, & u > 0.5
\end{cases}
$$

用于先上升后下降的线性频偏轨迹。

### 5.4 Ramp

$$
shape = -1 + 2u
$$

语义是上升锯齿频偏；当前参考行为不做离散均值校正。

---

## 6. 采样率与长度策略

建议显式区分策略，避免“参考对齐”与“本机质量优先”混用。

### 6.1 Vsg60Exact（用于对齐 CSV）

- `FsMax = 50 MS/s`
- `PreferredSamplesPerPeriod = 3000`
- `MinSamplesPerPeriod = 4`
- `MinTotalComplexSamples = 1024`

每周期点数：

1. `N_raw = min(PreferredSamplesPerPeriod, floor(FsMax / Rate))`
2. `N_period = floor_to_even(N_raw)`
3. `N_period = max(N_period, 4)`
4. `Fs = Rate * N_period`

总长度：

- 若 `N_period >= 1024`，则 `N_total = N_period`
- 否则整周期重复到 `N_total >= 1024`

因此：

- `Rate=1k` 时，`N_period=3000`，`Fs=3MS/s`
- `Rate=10M` 时，`N_period=4`，`Fs=40MS/s`，`N_total=1024`

### 6.2 Tx125Quality（用于本机高保真）

- 使用本机上限 `125 MS/s`
- 仍保持同一 FM 相位积分模型
- 不承诺逐点匹配 VSG60 CSV

### 6.3 CurrentMaxRate（兼容当前行为）

- 沿用 `calculateFMSampleRate(rate, deviation)`
- 保持既有系统行为

---

## 7. 参考样本对齐结论

已用于收敛当前模型的样本：

- `data/FM1K.csv`
- `data/FMSqure.csv`
- `data/FMRamp.csv`
- `data/FMDevication10K.csv`
- `data/FMRate10M.csv`

关键结论：

1. FM 是恒包络复基带，首点可落在 `(32767,0)`。
2. `Sine` 的相位趋势与积分模型一致，可解释为 `beta*(1-cos(...))`。
3. `Square` 为先正后负频偏，半周期附近可观察到相位换向。
4. `Ramp` 为上升锯齿频偏，首尾样点可一致。
5. 高频率下每周期点数会收敛到 4，并以周期重复扩展到至少 1024 点。

---

## 8. 当前明确不做的事

当前 FM 自研对齐范围不包含：

1. 在 IQ 内生成 RF 载波或 passband 调制。
2. 引入额外形状参数（偏置、平滑、预失真等）。
3. 在本次文档中承诺未采样验证的极端边界频谱指标。

---

## 9. 必须保持不变的硬规则

后续落地代码时，下列行为应视为硬边界：

1. 输出格式保持 `int16` 交织 IQ：`I0,Q0,I1,Q1...`。
2. FM 业务语义保持为复基带频偏积分相位模型。
3. `Rate/Deviation/Shape` 参数面保持与当前 `FmModulator` 一致。
4. `Sine/Square/Triangle/Ramp` 的离散定义保持本文口径。
5. 采样率策略要可配置区分（至少能区分对齐策略和本机质量策略）。
6. 若替换第三方 API，应先完成 CSV 对齐验证，再切默认路径。

---