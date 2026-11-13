# 多音 RMS 保持与 Level 联动人工测试方案

日期：2026-10-23。状态：测试方案，未执行；本文的数值为条件明确的理论预期，不是实测结果。

2026-10-26 校准：version 5 持久化、明确 CW Level 成功后建立 RMS，以及对称显示按 [当前交互设计](rms_level_async_interaction_design.md)；所有新增项目同样待人工执行。

## 1. 要验证的行为

1. 未编辑 Startup、尚未应用 baseband 且模式支持全局功率时，RMS 镜像 Level；明确输入 Level/RMS 后，未知的另一派生字段显示 --，已知参考可预览，成功才建立保持目标。
2. 默认保持 RMS。未显式设置 RMS 时，首次波形保持启动恢复的 API Level，完成后建立实际 RMS；随后修改波形参数，使用新波形指标反推 Level。首次生成期间显式设置 RMS 则按该目标配置。
3. 用户主动修改 Level 时，本次 Level 生效后重新建立 RMS 目标；再改音数继续保持新的 RMS，不永久锁住该 Level。
4. 连续编辑与异步生成/下载只使用最新意图，不重新计算已经匹配的波形指标，不让旧完成覆盖新 RMS。
5. LScan/MScan 的全局 Level/RMS 入口置灰；FScan 允许编辑并按同一 baseband 的指标配置统一 Level；Streaming 的 RMS 入口置灰。
6. 主界面与 Minibar 一致，不增加 Hold、状态文字、tooltip 或 RMS 提示弹窗。

## 2. 三个功率量不能混淆

```text
F = 2 × 32767²
avgPower = 最终 int16 IQ 在实际播放周期上的平均 (I²+Q²)
peakPower = 最终 int16 IQ 的最大 (I²+Q²)
r = 10log10(avgPower / F)
p = 10log10(peakPower / F)
PAPR = p - r

RMS_dBm = Level_dBm + r
Level_dBm = RMS目标_dBm - r
实际波形 PEP_dBm = Level_dBm + p = RMS_dBm + PAPR
```

Level 是设备 API 的共享满幅参考：I、Q 同时达到 32767 时的参考 PEP。它不必等于实际波形的峰值功率，也不是单根 tone 的功率。

RMS 表示所有 tones 合计的平均功率。保持总 RMS 时，音数增加后每根等幅 tone 的功率会下降，不能把某一根谱线功率下降判断成 RMS 保持失败。

## 3. 为什么修改音数会使 Level 改变

当前 MultitoneGenerator 先合成等幅复数 tones，再以 `max(max|I|, max|Q|)` 做分量峰值归一化，最后量化到 int16。增加 tone 数时，未归一化的平均能量增加，但同相叠加的峰值增长更快，生成器因此把整个波形缩得更多。最终 IQ 平均功率相对满幅的比例下降，即 r 更负。

固定 RMS 时，`Level = RMS-r`，所以 r 更负就需要提高 Level。这是基带归一化和射频 Level 补偿的共同结果。

本测试选择以下可推导的条件：全部 tones 启用、等幅、Fixed 相位、统一偏置为默认 0；完整周期、频点互不碰撞、无额外补零。选奇数 Count=3、5、9，避开偶数中心音遮盖/半间隔差异。

设启用音数为 N。同相峰值处未归一化幅度为 N，完整周期平均功率为 N。按分量峰值缩放后：

```text
avgPower ≈ 32767²/N
peakPower ≈ 32767²
r ≈ -10log10(2N)
p ≈ -3.0103 dB
PAPR ≈ 10log10(N)
Level ≈ RMS目标 + 10log10(2N)
```

这里额外的 3.0103 dB 来自产品角点满幅参考 `2×32767²`；不能遗漏，也不能把 `Level=RMS+PAPR` 当作通用公式。

因此固定相位本组条件下：

```text
Level(N2)-Level(N1) ≈ 10log10(N2/N1)
```

一般情况下，音数变化不保证 Level 单调变化。Random/Parabolic 相位、启用音集合、归一化峰值、period 补零等都会改变 r；通用判据始终是 `ΔLevel=-Δr`。不要将本表推广到所有相位模式。

## 4. 测试准备

- 使用设备支持的固定中心频率，例如 1 GHz；先关闭扫描，采用固定 Playback、无限循环触发。
- Multitone 的 Tone Phase 选 Fixed；固定相位偏置保持默认 0。Freq Spacing 设为 100 kHz。
- 禁用 notch（宽度为 0）或在 Discrete Tone Mode 下启用全部 tones。每次改 Count 后重新确认所有 tone 都被启用，实际启用数必须等于 Count。
- Count 使用 3、5、9。采样率由现有能力约束解析，确认没有音被裁掉或发生 bin 碰撞，不额外引入播放周期补零。
- 为便于对表，明确输入 RMS=-20 dBm（生成前或下载后均可）。默认显示的 CW Level=-20 属于 Startup，不能代替明确设置；显式确认 CW Level 且成功后已建立 R=L，本表直接输入 RMS 以避免混淆。旧配置 Startup 分支见第 6 节。
- 记录软件版本、设备型号、中心频率、相位模式、音间隔、Count、实际启用数及采样率。

## 5. 基础用例：保持 RMS=-20 dBm，修改音数

| 实际音数 N | 理论 r（dB） | RMS（dBm） | 预期 Level（dBm） | 每根 tone 的平均功率（dBm） |
| --- | --- | --- | --- | --- |
| 3 | -7.7815 | -20.00 | -12.22 | -24.77 |
| 5 | -10.0000 | -20.00 | -10.00 | -26.99 |
| 9 | -12.5527 | -20.00 | -7.45 | -29.54 |

步骤：

1. 设置 Count=3，等待生成、下载和数值稳定，记录 RMS 与 Level。
2. 仅将 Count 改为 5，保持相位和全部启用条件；等待稳定并记录。
3. 改为 9，再改回 3，重复记录。
4. 在主界面和 Minibar 各重复一次。两处应显示同一组数值。

判定：RMS 保持约 -20.00；Level 按表变化并可逆返回。3→5 约增加 2.2185 dB，5→9 约增加 2.5527 dB，3→9 约增加 4.7712 dB。小波形的 UI 理论对表可先以 0.05 dB 为检查范围；实际最终量化指标和设备电平分辨率是权威依据，不能用仪器绝对精度替代此软件计算检查。

若表不符，先检查实际启用数、相位是否为 Fixed、固定偏置、tone bin、补零和量化，再判断 RMS 换算。不能只看 Count 输入框。

## 6. 启动、默认目标及 Level 手动重建目标

### A. 启动时没有显式 RMS 输入

1. 软件启动，尚无本次运行已应用的 baseband，RF/MOD 均可先关闭。
2. 记当前 Level 为 L0：RMS 应同样显示 L0，不能显示 --。
3. 本用例不要明确编辑/确认 Level 或 RMS，保持 Startup；需要指定 L0 时用旧配置恢复。显式 CW Level 成功将建立 R=L，不再属于本用例。
4. 未编辑功率，直接启用 Count=3 的多音。以 Startup Level=-20 为例，首次完整波形应用后 Level 仍为 -20，RMS≈-27.78；两者开始分离，实际 RMS 建立为后续目标。
5. 改 Count=5、9：RMS 仍≈-27.78，Level 分别≈-17.78、-15.23；回到 3 音时 Level≈-20。本用例的 RMS 不是第 5 节显式输入的 -20。
6. 正常退出保存 version 5 后连续重启两次：已保存 rms 应立即显示原目标，以同一波形恢复相同 Level，不重复补偿。另单独重复加载无 power 的旧配置：进入 Startup，首次仍按旧 API Level 应用并建锚。

version 5 保存 power 意图，rms 文件跨启动保持目标；无 power 的旧配置才按 Startup 兼容，不把旧 API Level 当作 RMS 再补偿。明确 CW Level 在成功后建立 R=L，即使从未下载过 baseband；后续启用调制应保持该 R 并反推 Level。

### B. 用户主动改 Level

1. Count=5、Fixed、全部启用，确认 r≈-10 dB。
2. 用户输入 Level=-15 dBm，等待完成。
3. 应得到 RMS≈-25 dBm，形成新的 RMS 目标。
4. 改 Count=9：RMS 仍约 -25，Level≈-12.45 dBm。
5. 改 Count=3：RMS 仍约 -25，Level≈-17.22 dBm。

不能出现 Level 永久停在 -15，或自动恢复手改前 RMS=-20 的情况。设备普通回写不得在每次刷新中继续改变目标。

### C. 波形未就绪时手动编辑

- 生成过程中确认 RMS=-20、-22、-18：最终按 -18 和最终波形 r 解析 Level。
- 生成过程中最后确认 Level=-15：新波形完成后按该显式 Level 应用，再建立 `R=-15+r`；例如 Count=3 时 R≈-22.78。
- 上述 Level 后又确认 RMS=-20：最终以 RMS=-20 为准；旧 Level 完成不得覆盖新 RMS 目标。
- 波形下载已开始后改变功率，允许已提交值短暂执行，再由现有串行队列收敛到最新目标，不中断/重传同一波形。

## 7. 连续参数与异常边界

- 连续将 Count 3→5→9，并穿插多次 RMS 编辑，最终只以最后的 Count/实际启用集合和最后功率意图为准。旧波形完成不能覆盖新波形。
- 固定 Count，切换 Fixed、Random、Parabolic：RMS 保持；Level 按实际 r 改变。此用例不使用第 5 节的固定数值表，也不要求音数不变时 Level 不变。
- 使全部 tones 关闭或 notch 覆盖全部 tones：Rms 意图仍显示设置目标，派生 Level=--；全零并不具有该有限输出功率。此时明确改 Level 则显示输入 Level、RMS=--，不建锚；恢复非零波形后按保留的最新意图处理。
- 将目标推至当前波形可达范围外：Level 限制在设备边界，RMS 数值直接变成可达值。回到原音数不自动恢复超限前的旧目标；不出现提示弹窗。
- 设备断开/重连、RF OFF/ON 不应把已建立的 RMS 目标错误改成最近一次设备 Level；首次下载前仍遵循启动镜像规则。

## 8. 扫描与 Streaming

### LScan / MScan

1. 先在固定多音下建立 RMS=-20。
2. 启用 LScan：主界面与 Minibar 的全局 Level/RMS 都置灰，点击不能打开编辑器。已有编辑器在模式切换后关闭。
3. 修改扫描自己的起止 Level/点表仍有效，普通功率输入不重启或改写扫描计划。
4. MScan 重复同样检查。扫描启用但 RF OFF 时入口也应置灰，避免启用状态与运行状态混用。
5. 在扫描模式中改 Count：点表/起止 Level 的含义仍是 API Level，扫描每点 RMS 会按新的 r 改变；本功能不暗中平移扫描电平。
6. 关闭扫描回固定/FScan：恢复编辑，使用之前保留的全局 RMS 与当前波形 r 解析 Level。

### FScan

1. 选择设备支持的窄频率区间（例如以 1 GHz 为中心），保持同一多音 baseband。
2. 全局 Level/RMS 均可编辑，保持 RMS=-20。
3. Count 3→5→9 时统一扫描 Level 按第 5 节变化；不需要为每个频点重扫 IQ。
4. 仅改扫频起止/步长，不应因为频点数量增加而重新统计同一 IQ。
5. 允许运行中改功率重配/重启 FScan；不将固定 Playback 的 FFM-only 无重传条件当成全部扫描模式的无中断承诺。

### Streaming

Level 可编辑，RMS 置灰；不从未知实时数据反推功率。返回有限 Playback 后按已保留的 RMS（或最后一次显式 Level 意图）处理。既有对外 SCPI Level 合同不因 UI 置灰而被隐式改变。

## 9. 频谱仪验证

可选的仪器检查用于验证 RF 总平均功率，软件数值与仪器精度分别判断：

- Count=9、100 kHz 间隔的 tone 最外侧为 ±400 kHz。选择覆盖全部 tones 的分析带宽，例如 1 MHz，频谱 span 可设 2 MHz；RBW 选择显著小于 100 kHz 的值以分辨谱线。
- 使用通道功率或等价的线性功率积分功能，覆盖完整多音信号；采用适合平均功率的检测/平均方式。
- Count=3/5/9 下，总平均功率应在仪器、信号源规格和链路损耗允许的误差内保持一致；线缆、衰减器及仪器配置必须一致。
- 等幅 tones 的单根功率约为 `RMS_total-10log10(N)`，对应第 5 节表。不能只用峰值 marker 读数代表总 RMS。
- 当前固定相位组的实际 PEP≈RMS+10log10(N)，Level 比实际 PEP 约高 3.0103 dB。比较 Level 与频谱仪平均功率时，应比较公式关系，不要求二者数值相等。
- 普通扫频频谱显示不等同于 RF 包络峰值测量；此测试不要求用它直接证明 PEP。

## 10. 记录模板与完成条件

| 序号 | 模式 | Count/实际音数 | 相位 | RMS输入或目标 | Level输入 | 稳定RMS | 稳定Level | 扫描入口状态 | RF总平均功率（可选） | 结论 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Fixed Playback | 3/3 | Fixed | -20 | — | | | 可编辑 | | |
| 2 | Fixed Playback | 5/5 | Fixed | -20 | — | | | 可编辑 | | |
| 3 | Fixed Playback | 9/9 | Fixed | -20 | — | | | 可编辑 | | |

完成条件：未编辑 Startup 镜像、明确输入后的对称占位/预览成立；固定 RMS 的表格与差值成立；成功后建锚与 version 5 恢复正确；异步最新意图、扫描/Streaming 入口策略一致；无新增提示。本文不表示上述测试已通过。

相关依据：[当前 Multitone 算法](htra_multitone_current_algorithm_and_vsg60_boundaries.md)、[RMS 满幅参考](htra_rms_power_multitone_pulse_principle.md)、[RMS 功能方案](rms_pep_simd_cross_platform_design.md)。
