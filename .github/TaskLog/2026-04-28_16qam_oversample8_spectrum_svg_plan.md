# 2026-04-28 16QAM Oversample8 频谱 SVG 复现计划

## 目标

- 使用 `data/16QAMOversample8.wav` 离线生成一张与 VSG60 频谱预览外形一致的频谱图。
- 输出 SVG 产物，便于直接查看和后续复现。

## 已确认事实

- VSG60 截图显示 `RBW = 29.454688 kHz`，在 `Fs = 8 MHz` 下可对应到 `512` 点 FFT 乘以 `1.8851` 的 Blackman-Nuttall 3 dB RBW 系数。
- 对数字基带 IQ 波形，行业上常见做法是“复数基带分段 FFT + 窗函数 + 功率平均 + 频轴居中”。
- 输入数据是双通道 16-bit、I/Q 交织的 WAV PCM。
- 频轴以 `CenterFreq_Hz = sampleRate / 2` 做中心搬移，最终显示为基带 `[-Fs/2, Fs/2]`。
- 新输入 `data/16QAM.csv` 不是与 `data/16QAMOversample8.wav` 等价的未裁剪 IQ：其 `I/Q` 有约 `33%` 样本命中 `±32767/32768`，直接 FFT 会把带外抬到约 `-55 dBFS`。
- 对这份 csv，更接近 VSG60 截图的做法是先加一层很轻的零相位 IIR 重建平滑，再走统一的 analyzer 频谱估计链；本地扫描里 `cutoff ≈ 1.7 MHz / passes = 2` 时，csv 频谱量级接近截图。

## 当前假设

- 当前统一方案应拆成“源适配 + 统一 analyzer”：
- `csv` 先做轻度零相位重建平滑，抑制导出数据里的硬饱和边沿；
- `wav` 保持原始 IQ；
- 两者随后都走同一条“长记录 FFT + RBW 内积分 + 显示点 reducer”频谱绘制链。

## 最便宜的证伪检查

- 先读取 `data/16QAMOversample8.wav` 的 profile 与采样率，切到“长 FFT + RBW 积分 + 显示点 reducer”后重新生成一版频谱；若带外仍表现为随机尖噪而不是更规则的连续波纹，则当前 analyzer 视图假设错误，需要继续收敛。

## 执行步骤

1. 读取 WAV 头与 `prof` chunk，确认 `SymbolRate / Oversample / FilterAlpha / FilterLength`。
2. 配置 Python 环境并确认 FFT/SVG 依赖可用。
3. 写离线脚本：读取 IQ、做 Blackman-Nuttall 窗 FFT、生成 SVG。
4. 运行脚本产出 SVG，并检查结果是否与 VSG60 截图一致。
5. 如有必要，微调纵轴 offset、线宽和坐标样式，使视觉效果贴近截图。