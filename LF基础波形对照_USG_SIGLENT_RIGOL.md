# RIGOL DG1000Z、SIGLENT SDG1000X Plus 与 AWG Debug 波形对照

日期：2026-09-29

本表比较两台函数/任意波形发生器的**输出波形选择项**与我们 AWG Debug 的软件波形类型，并补充正弦谐波子功能。✓ 表示商用机手册列有该项，或 AWG Debug 已有对应类型；“—”表示相应手册或软件类型列表没有单列此波形/功能，不代表不能用任意波数据合成相似信号。调制、扫频和 Burst 是工作模式，不作为波形类型统计。

依据：[RIGOL DG1000Z User’s Guide](<C:/Users/jsl/Desktop/SignalDocs/RIGOL_DG1000Z_UserGuide_EN.pdf>) 与 [SIGLENT SDG1000X Plus User Manual](<C:/Users/jsl/Desktop/SignalDocs/SIGLENT_SDG1000X_Plus_UserManual_EN01B.pdf>)。以下页码均指 PDF 阅读器显示的页码。

## 输出波形与高级功能

| 波形/功能 | RIGOL DG1000Z | SIGLENT SDG1000X Plus | 我们的 AWG Debug（调试分支） |
| --- | --- | --- | --- |
| 正弦波 | ✓ `Sine` | ✓ `Sine` | ✓ `Sine` |
| 方波 | ✓ `Square` | ✓ `Square` | ✓ `Square` |
| 斜波/三角波 | ✓ `Ramp`，可设对称度 | ✓ `Ramp`，可设对称度 | ✓ `Ramp`，50% 为三角波 |
| 脉冲波 | ✓ `Pulse`，可设脉宽及前/后沿 | ✓ `Pulse`，可设脉宽及上升/下降沿 | ✓ `Pulse` 可设脉宽及上升/下降沿|
| 噪声 | ✓ `Noise`，高斯噪声 | ✓ `Noise`，可设均值、标准差和带宽 | ✓ `Noise` 可设均值、标准差和带宽 |
| 直流 | ✓ `Arb → Select Wform → DC` | ✓ `DC` | ✓ `Constant` |
| 任意波 | ✓ `Arb`，可选内置或用户波形 | ✓ `Arb`，可选内置或已存波形 | ✓ `Arbitrary` |
| 正弦谐波（子功能） | ✓ `Sine → Harm` | ✓ `Sine → Harmonics` | — |
| 多脉冲 | — | ✓ `Multi Pulse`，可设脉冲个数和各脉冲参数 | — |
| PRBS | — | ✓ `PRBS`，可设比特率和序列长度 | — |
| 序列波 | — | ✓ `Sequence`，按用户设定的波形段和顺序播放 | — |

**菜单口径。** SDG1000X Plus 的 `Waveforms` 菜单有 10 项：Sine、Square、Ramp、Pulse、Noise、DC、Arb、Multi Pulse、PRBS、Sequence（第 8.1 节，PDF 第 28 页）。DG1000Z 将 Sine、Square、Ramp、Pulse、Noise 列为五种 Basic Waveforms（PDF 第 40–41 页）；`Arb` 是单独的前面板波形键，`DC` 从 Arb 菜单选择（PDF 第 26、55–57 页）。表中的“正弦谐波”是两机在 Sine 下的子功能，不计入 SDG 的 10 项。

**高级项依据。** DG1000Z 的谐波设置见 PDF 第 68–70 页；SDG1000X Plus 的谐波、PRBS、Sequence、Multi Pulse 分别见 PDF 第 35–36、40–41、49–51、52–53 页。DG1000Z 手册的波形键、Arb 菜单和第 2 章功能目录未列独立的 Multi Pulse、PRBS 或 Sequence，因此这些格子标“—”；其普通 Pulse 或 Burst 不等同于可逐个设置参数的 Multi Pulse。

**AWG Debug 依据。** `integration/awg-disabled` 分支的 `src/plugins/awg/awgprofile.h` 中，`AwgWaveformKind` 只有 Sine、Square、Pulse、Ramp、Constant、Noise、Arbitrary 七类，`awgpanel.cpp` 的菜单与之相同；没有独立的谐波、多脉冲、PRBS 或序列波类型。这一列仅表示该分支已有的软件生成/预览能力，面板明确标示无设备输出；当前 `5inchUISample` 分支的 `src/plugins/CMakeLists.txt` 未纳入 AWG 插件。

**形状与预置波形。** DG1000Z 的 `Ramp` 对称度可设 0%～100%，50% 为三角形，两端可形成锯齿形状（PDF 第 49 页）。SDG1000X Plus 手册将 `Ramp` 作为三角/斜波并列出对称度参数（PDF 第 28、32 页），但所核对页没有给出端点范围；两机均没有独立的 `Sawtooth` 波形选择项。DG1000Z 的 `Arb` 内含 160 种预置波形，手册表 2-2 逐名列出（PDF 第 58–62 页）；SDG1000X Plus 手册第 9.5.3 节只列内置波形类别（PDF 第 45 页）。这些预置都归在 `Arb` 下，不能因一份手册未逐名列出就将对应预置记为“—”。
