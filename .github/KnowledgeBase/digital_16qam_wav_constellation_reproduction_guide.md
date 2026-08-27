# 16QAM 数字调制 WAV 星座图复现指南

本文档记录当前两份 16QAM 数字调制 wav 的最小复现流程，目标是让后续 Copilot 或开发者直接用 Python 重跑验证，并重新生成对比图。

本指南偏“怎么做”；如果要看“为什么 raw cloud 会变 dense/smooth、为什么匹配滤波后又会收敛”，请同时参考 [digital_16qam_raw_cloud_vs_symbol_constellation.md](digital_16qam_raw_cloud_vs_symbol_constellation.md)。

## 适用对象

- 第一份文件：[../../data/16QAMOverSample4.wav](../../data/16QAMOverSample4.wav)
- 第二份文件：[../../data/16QAMOversample32.wav](../../data/16QAMOversample32.wav)

当前对比图产物：

- [../../data/16QAMOversample32_vs_16QAMOverSample4_compare.svg](../../data/16QAMOversample32_vs_16QAMOverSample4_compare.svg)

## 当前输入事实

### 导出代码位置

- 数字调制 wav 的保存入口在 [../../src/plugins/analog/digitalmodulation.cpp](../../src/plugins/analog/digitalmodulation.cpp)。
- WAV header 和自定义 chunk 写入逻辑在 [../../src/libs/utils/wavheader.cpp](../../src/libs/utils/wavheader.cpp)。

### 文件结构

这两份 wav 都满足下面的结构约束：

1. `data` chunk 前面带有一个自定义 `prof` chunk，内容是 JSON profile。
2. PCM 主体是双通道 16-bit，按 I/Q 交织保存。
3. `sampleRate = SymbolRate * Oversample`。

### 当前验证结果

本次已直接用标准 Python `wave` 成功打开这两份文件，结果如下：

1. `16QAMOverSample4.wav`
   - `channels = 2`
   - `sample_width = 2`
   - `sample_rate = 4000000`
   - `frame_count = 131072`
   - `FilterType = Rectangular`
   - `Oversample = 4`

2. `16QAMOversample32.wav`
   - `channels = 2`
   - `sample_width = 2`
   - `sample_rate = 32000000`
   - `frame_count = 1048576`
   - `FilterType = RootRaisedCosine`
   - `FilterAlpha = 0.35`
   - `FilterLength = 12`
   - `Oversample = 32`

## 推荐运行策略

### 依赖策略

优先使用 Python 标准库实现。当前流程只需要：

- `array`
- `json`
- `math`
- `struct`
- `pathlib`
- `wave`

SVG 可以直接手写，不依赖 `matplotlib`。

### 推荐实现顺序

建议稳定拆成下面 7 步：

1. `parse_profile(path)`
2. `read_frames(path)`
3. `normalize_to_16qam(symbols)`
4. `constellation_mse(symbols)`
5. `pick_best_raw_phase(frames, sps, edge_trim_symbols=0)`
6. `design_rrc(alpha, sps, span_symbols)`
7. `pick_best_mf_phase(frames, sps, taps, edge_trim_symbols)`

## 步骤 1：读取 profile 和 PCM 主体

### 读取 `prof` chunk

标准 `wave` 能读取 PCM 主体，但不会把自定义 `prof` chunk 直接暴露给调用方，所以 profile 仍建议做一次轻量 RIFF 遍历：

```python
from pathlib import Path
import json
import struct

def parse_profile(path: Path):
    raw = path.read_bytes()
    pos = 12
    profile = {}
    while pos + 8 <= len(raw):
        chunk_id = raw[pos:pos + 4]
        chunk_size = struct.unpack_from('<I', raw, pos + 4)[0]
        start = pos + 8
        if chunk_id == b'prof':
            profile = json.loads(raw[start:start + chunk_size].decode('utf-8'))
        pos = start + chunk_size + (chunk_size & 1)
        if chunk_id == b'data':
            break
    return profile
```

### 读取 PCM 帧

```python
from array import array
import wave

def read_frames(path):
    with wave.open(str(path), 'rb') as wf:
        channels = wf.getnchannels()
        sample_width = wf.getsampwidth()
        frame_count = wf.getnframes()
        sample_rate = wf.getframerate()
        raw_frames = wf.readframes(frame_count)

    if channels != 2 or sample_width != 2:
        raise RuntimeError(f'unexpected wav format: {path}')

    samples = array('h')
    samples.frombytes(raw_frames)
    frames = [complex(samples[i], samples[i + 1]) for i in range(0, len(samples), 2)]
    return frames, sample_rate
```

## 步骤 2：定义 16QAM 归一化与误差指标

### 理想电平

本次分析统一把理想 16QAM 电平看成：

$$
\{-3,-1,1,3\}
$$

### 归一化方法

```python
import math

def normalize_to_16qam(symbols):
    if not symbols:
        return [], 1.0
    avg_power = sum((s.real * s.real + s.imag * s.imag) for s in symbols) / len(symbols)
    scale = math.sqrt(avg_power / 10.0) if avg_power > 0 else 1.0
    if scale == 0.0:
        scale = 1.0
    return [complex(s.real / scale, s.imag / scale) for s in symbols], scale
```

### 误差指标

```python
LEVELS = (-3.0, -1.0, 1.0, 3.0)

def nearest_level(value):
    return min(LEVELS, key=lambda x: abs(x - value))

def constellation_mse(symbols):
    err = 0.0
    for s in symbols:
        di = s.real - nearest_level(s.real)
        dq = s.imag - nearest_level(s.imag)
        err += di * di + dq * dq
    return err / len(symbols)
```

这里的 MSE 仅用于离线相对对比，不是标准 EVM。

## 步骤 3：第一份文件的处理链

### 文件特征

- 文件：[../../data/16QAMOverSample4.wav](../../data/16QAMOverSample4.wav)
- FilterType：Rectangular
- Oversample：4

### 处理原则

这份文件直接做 raw phase search 即可，不需要先做匹配滤波。

### 相位遍历方法

```python
def pick_best_raw_phase(frames, sps, edge_trim_symbols=0):
    best = None
    for phase in range(sps):
        symbols = frames[phase::sps]
        if edge_trim_symbols and len(symbols) > 2 * edge_trim_symbols:
            symbols = symbols[edge_trim_symbols:-edge_trim_symbols]
        normalized, scale = normalize_to_16qam(symbols)
        mse = constellation_mse(normalized[:min(len(normalized), 12000)])
        record = {
            'phase': phase,
            'scale': scale,
            'mse': mse,
            'symbols': normalized,
        }
        if best is None or record['mse'] < best['mse']:
            best = record
    return best
```

### 当前验证结果

- 最佳 raw 相位：`1`
- raw MSE：`0.0`

## 步骤 4：第二份文件的处理链

### 文件特征

- 文件：[../../data/16QAMOversample32.wav](../../data/16QAMOversample32.wav)
- FilterType：RootRaisedCosine
- FilterAlpha：0.35
- FilterLength：12
- Oversample：32

### 处理原则

这份文件需要同时观察两层结果：

1. raw phase search
2. RRC matched filter 之后的 symbol sampling

### raw phase search 的当前结果

- 最佳 raw 相位：`0`
- raw MSE：`0.184004`

### RRC taps

```python
def design_rrc(alpha, sps, span_symbols):
    taps = []
    n_max = span_symbols * sps // 2
    for n in range(-n_max, n_max + 1):
        t = n / float(sps)
        if abs(t) < 1e-12:
            val = 1.0 - alpha + (4.0 * alpha / math.pi)
        elif alpha > 0 and abs(abs(t) - 1.0 / (4.0 * alpha)) < 1e-12:
            val = (alpha / math.sqrt(2.0)) * (
                (1.0 + 2.0 / math.pi) * math.sin(math.pi / (4.0 * alpha))
                + (1.0 - 2.0 / math.pi) * math.cos(math.pi / (4.0 * alpha))
            )
        else:
            num = math.sin(math.pi * t * (1.0 - alpha)) + 4.0 * alpha * t * math.cos(math.pi * t * (1.0 + alpha))
            den = math.pi * t * (1.0 - (4.0 * alpha * t) * (4.0 * alpha * t))
            val = num / den
        taps.append(val)
    energy = math.sqrt(sum(v * v for v in taps))
    return [v / energy for v in taps]
```

### 匹配滤波抽样

```python
def mf_symbol_samples(frames, taps, sps, phase, start_symbol, symbol_count):
    out = []
    taps_len = len(taps)
    group_delay = taps_len // 2
    frames_len = len(frames)
    for sym in range(start_symbol, start_symbol + symbol_count):
        center = group_delay + phase + sym * sps
        acc = 0j
        for tap_index in range(taps_len):
            src_index = center - tap_index
            if 0 <= src_index < frames_len:
                acc += frames[src_index] * taps[tap_index]
        out.append(acc)
    return out

def pick_best_mf_phase(frames, sps, taps, edge_trim_symbols, search_symbols=2048):
    total_symbols = len(frames) // sps
    usable = max(0, total_symbols - 2 * edge_trim_symbols)
    probe = min(search_symbols, usable)
    best = None
    for phase in range(sps):
        raw_symbols = mf_symbol_samples(frames, taps, sps, phase, edge_trim_symbols, probe)
        normalized, scale = normalize_to_16qam(raw_symbols)
        mse = constellation_mse(normalized)
        record = {'phase': phase, 'scale': scale, 'mse': mse}
        if best is None or record['mse'] < best['mse']:
            best = record
    full_symbols = mf_symbol_samples(frames, taps, sps, best['phase'], edge_trim_symbols, usable)
    normalized, scale = normalize_to_16qam(full_symbols)
    best['symbols'] = normalized
    best['scale'] = scale
    return best
```

### 当前验证结果

- matched-filter 后最佳相位：`0`
- matched-filter 后 MSE：`0.002586`

## 当前对比图生成规则

当前 compare SVG 采用四宫格布局：

1. `16QAMOverSample4.wav` 全采样 raw cloud
2. `16QAMOverSample4.wav` 最佳 raw 相位抽样
3. `16QAMOversample32.wav` 全采样 raw cloud
4. `16QAMOversample32.wav` matched-filter 后最佳相位抽样

输出文件固定为：

- [../../data/16QAMOversample32_vs_16QAMOverSample4_compare.svg](../../data/16QAMOversample32_vs_16QAMOverSample4_compare.svg)

## 复现完成后的检查单

重跑后至少核对下面 8 项：

1. `16QAMOverSample4.wav` 的 `sample_rate = 4000000`。
2. `16QAMOversample32.wav` 的 `sample_rate = 32000000`。
3. 第一份文件 `FilterType = Rectangular`，`Oversample = 4`。
4. 第二份文件 `FilterType = RootRaisedCosine`，`FilterAlpha = 0.35`，`FilterLength = 12`，`Oversample = 32`。
5. 第一份文件 raw best phase 为 `1`。
6. 第一份文件 raw MSE 为 `0.0`。
7. 第二份文件 raw best phase 为 `0`，raw MSE 为 `0.184004`。
8. 第二份文件 matched-filter best phase 为 `0`，matched-filter MSE 为 `0.002586`。

如果 5 到 8 大幅偏离，优先检查：

1. 是否正确读取了 `prof`。
2. 是否按 `sampleRate / SymbolRate` 正确得到 `sps`。
3. `RRC` taps 是否做了单位能量归一化。
4. matched filter 阶段是否重新做了 phase search。

## 最终结论

当前这两份文件的复现链路已经稳定，可直接按下面方式记忆：

1. 用标准 Python `wave` 读 PCM 帧。
2. 用轻量 RIFF 遍历读取 `prof` JSON。
3. `16QAMOverSample4.wav` 只做 raw phase search，预期 `phase=1, MSE=0.0`。
4. `16QAMOversample32.wav` 先看 raw，再补 RRC matched filter，预期 raw `phase=0, MSE=0.184004`，matched-filter 后 `phase=0, MSE=0.002586`。
5. 最终输出当前 compare SVG：[../../data/16QAMOversample32_vs_16QAMOverSample4_compare.svg](../../data/16QAMOversample32_vs_16QAMOverSample4_compare.svg)。