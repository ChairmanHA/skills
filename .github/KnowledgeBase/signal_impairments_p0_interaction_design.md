# 信号损伤方案：AWGN 与 I/Q 损伤在前端 IQ 层实现

日期：2026-10-20；2026-11-04 统一 Level Offset 参考面/正负号；同日修订：Level Offset/Flatness 移至 Settings、修改 Offset 保持端口输出、首版 AWGN 只做 SNR + 带宽、频率误差只做 Hz；2026-11-10 AWGN/IQ 改为前端 IQ 层实现。状态：AWGN/IQ 的 IQ 处理尚未实施；既有 API 的接入状态见下文。

2026-11-06 实施说明：配套 [UI 页面设计](signal_impairments_ui_entry_design.md) 的 UI、User Flatness、Frequency Error（Hz）与 Sample Rate Error（ppm）已静态接入，未构建/运行；AWGN/IQ 仍未接入。Freq/Clock 通过 TX 快照进入设备层，只改变 API 频率/播放速率，不重采样或重新生成 IQ；Flatness 有效表格为 2–999 行。2026-11-04 已按用户调整为三组同时展示的单页与组 SwitchButton，移除派生读数、摘要、tooltip 和 UI simulation 说明。

2026-11-05 交互修订：删除总开关；CommonPanel Impair 为三组 State 的聚合 ON/OFF 与导航入口，Minibar 只读显示该聚合状态。Invert 在 I/Q 关闭时保留 ON 值但取消生效强调，I/Q Offset 显示 %FS；Freq/Clock 两项适用性分别判断。本轮按用户要求未编译、运行或测试。

2026-11-10 方向修订：**AWGN、Invert、I/Q 幅度/相位失衡、I/Q Offset 全部在前端 IQ 层实现，FPGA/API 就绪后再评估迁移。** 撤销 2026-10-20 版“FPGA 噪声源 + 扫描同步补偿”主线，原因见第二节。前端 IQ 链路的算法与接入点沿用 2026-10-10 版设计（skills 仓库 git `ebfeeb2`，第一、二层），本文不重复算法细节。已有 API 的项目（Level Offset、Amplitude Compensation、Frequency Error、Sample Rate Error、LO 模式）继续走 API，不受影响。

2026-11-10 实施（静态，未构建/运行，[TaskLog](../TaskLog/2026-11-10_awgn-iq-frontend-processing.md)）：按用户选择改为 **Core 集中处理**，而不是 3.1 所述的各来源 finalize 原地处理。业务照常发布 payload；`TxSessionService` 组 Playback 请求时交给 `SignalImpairmentProcessor`，在 Core 后台线程调用 `Utils::applyIqImpairments()` 生成带损伤副本，替换下发 payload 与功率指标。处理顺序、SNR 口径、统一缩放和 %FS Offset 与 3.1 一致；整个 payload 作为一条循环记录，噪声用循环 FFT 卷积，首尾无接缝，固定 seed 可复现。功率指标的峰值/PAPR 用最终样点，RMS offset 用缩放后的信号分量，Level/RMS 仍表示信号本身。处理中或失败时 provider 报告未就绪，不输出干净波形。代价：源与副本同时驻留；>125 MiB 波形与大波形独占租约冲突，直接报错。纯 AWGN 业务（`supportsIqImpairments()=false`）、CW、Streaming、ProgrammedArb 不处理，组标题显示 Not applied。参数非法（带宽 > Fs、过窄、零信号、Offset 无余量）时组标题显示错误。

全文范围是“简单版”：**所有扫描点使用同一组损伤参数**。每点参数不同（情况 1）和损伤随载波频率变化（情况 2）不支持，用 SCPI 逐点控制替代。固定噪声、改变信号电平的测试也用逐步控制实现（见 2.2），不用硬件 LScan。

## 一、需求场景与常用参数

### 1.1 典型场景

| 场景 | 典型客户 | 用到的功能 | 与扫描组合 |
| --- | --- | --- | --- |
| BER/PER/EVM 随 SNR 变化曲线 | 通信模组、IoT、无线芯片 | AWGN（载波参考） | 通常单频点；SCPI 逐点改 SNR |
| 标准解调性能测试：固定噪声功率，改变信号电平 | 蜂窝、Wi-Fi 等标准化产品 | AWGN（噪声参考） | 逐步改信号电平，噪声保持不变；每步测量时间长，用 SCPI/UI 逐步控制，不需要硬件 LScan（见 2.2） |
| 多信道解调一致性：每个信道同一 SNR | 多信道接收机、跳频设备 | AWGN（载波参考） | FScan/MScan，所有点同一 SNR |
| 多信道 + 固定噪底 | 同上 | AWGN（噪声参考） | 频率和电平都变、噪声不变；简单版不支持在一次 MScan 内实现，改用逐点控制 |
| 镜像抑制、I/Q 校准算法验证 | 射频芯片、SDR | I/Q 幅度/相位失衡、I/Q Offset | 通常单频点 |
| 频偏捕获和容限 | 几乎所有接收机 | Frequency Offset | 单频点为主 |
| 采样时钟恢复和容限 | 基带、调制解调器 | Sample Rate Error | 单频点 |
| 相噪容限 | OFDM、高阶 QAM | Phase Noise | 单频点 |
| 均衡器验证 | OFDM、宽带单载波 | Channel Filter（静态多径） | 单频点 |
| 线缆和夹具损耗补偿 | 所有测试台 | Level Offset、User Flatness | 所有模式 |

接收机灵敏度测试通常**不加 AWGN**，只降低信号电平，噪底由接收机自身的噪声系数决定，用现有 LScan 就能完成。需要固定噪声的，是第二、四行这类**解调性能测试**。

### 1.2 常用参数范围

下表是行业常见的测试设置经验值，不是本设备的规格。

| 参数 | 常用范围 | 说明 |
| --- | --- | --- |
| AWGN SNR | −10 ～ +40 dB | BER 曲线常用 0 ～ 30 dB |
| 噪声带宽 | 信号占用带宽的 1 ～ 1.5 倍，或接收机信道带宽 | 以载波为中心的复基带总宽度 |
| 噪声功率（噪声参考） | 按标准规定，例如 LTE 解调测试常用 Noc = −98 dBm/15 kHz | 需要换算到噪声带宽内的总功率 |
| I/Q 幅度失衡 | ±0.1 ～ ±2 dB | |
| I/Q 相位失衡 | ±0.5° ～ ±10° | |
| I/Q Offset（载波泄漏） | −20 ～ −60 dBc | |
| Frequency Offset | ±几 Hz ～ ±几百 kHz，或 ±1 ～ ±100 ppm | |
| Sample Rate Error | ±1 ～ ±100 ppm | |
| Phase Noise | 偏移 1 kHz ～ 1 MHz，−60 ～ −110 dBc/Hz | |
| 静态多径 | 2 ～ 6 径，时延扩展 100 ns ～ 5 µs | |

### 1.3 AWGN 的两种功率参考

| 参考方式 | 固定的量 | 扫描时的效果 | 对应场景 |
| --- | --- | --- | --- |
| **载波参考** | SNR | 噪声随信号电平一起变化，扫描时每个点的 SNR 相同 | BER 曲线、多信道同 SNR |
| **噪声参考** | 噪声绝对功率（dBm，在噪声带宽内） | 每次设置时由前端推导 SNR 和 Level，使噪声功率等于设定值；硬件扫描中不保持 | 标准解调性能测试、固定噪底找门限 |

噪声加在 IQ 里后，信号和噪声经过同一模拟增益，硬件扫描中 SNR 恒定，所以 IQ 层天然只支持载波参考的扫描。噪声参考只用于逐步控制：每一步改 SNR 并按 2.2 的公式下发 Level。首版 UI 只做载波参考（见 [UI 设计 3.3](signal_impairments_ui_entry_design.md)），噪声参考作为后续的前端换算功能，不需要新 API。

## 二、为什么撤销 FPGA 扫描同步方案

### 2.1 当时为什么把 AWGN 放到 FPGA

2026-10-20 版的推理链是：

1. 先认定“固定噪声、改变信号电平”必须用**一次硬件 LScan/MScan** 完成。
2. 噪声加在 IQ 里，和信号经过同一个模拟增益 `G_i`，输出 `G_i·(s+n)`，扫描中 SNR 恒定、噪底跟着电平移动，满足不了第 1 条。
3. 要在扫描中固定噪底，只能让 FPGA 在每个扫描点按 `level[i]` 反向调整数字信号/噪声配比，并和模拟增益同一时刻切换。前端做不到（Playback 扫描只有单个波形、点与波形无绑定、主机拿不到跳频时刻）。
4. 既然 FPGA 要做噪声源，又顺带列出了“噪声不重复、改 SNR 不重下、支持 CW/Streaming、不占主机 CPU”等收益，于是 AWGN 和 I/Q 损伤整体移到 FPGA。

推理本身没有错，问题在第 1 条前提：**这个场景不需要硬件扫描。** 第 2、3 条只有在前提成立时才构成“必须 FPGA”的理由；第 4 条是改善项，不是必需项。

### 2.2 固定噪声的测试用逐步控制即可

固定噪声、改变信号电平的是解调性能测试（例如固定 Noc，逐步改变信号功率找 BER/BLER 门限）：

- 每一步都要长时间统计（BER 1e-5 至少约 1e6～1e7 bit，吞吐量测试几秒到几十秒），硬件扫描的毫秒级 dwell 用不上。
- 每一步都要等 DUT 同步、开始统计、读结果；门限搜索常用二分法，下一点依赖上一点结果，预先排好的扫描列表做不到。
- 行业自动化脚本本来就是 SCPI 逐步设置信号源。

噪声合并进 IQ 后，每一步由前端重新生成 IQ（只改 SNR），同时按下式下发 Level，噪声功率就保持在设定值 Pn：

```text
SNR   = Ps − Pn
P_tot = Pn + 10·log10(1 + 10^(SNR/10))     // 合成波形总功率
Level = P_tot − RMS_combined(dBFS)          // 与现有 level/RMS 换算一致
```

Level Offset 按 3.3 先把 Ps、Pn 换到 API 参考面，比例不变。代价是每步要重新生成和下载 IQ，下载期间输出中断；测试本来每步都要等 DUT 同步，影响小。

### 2.3 IQ 层实现的已知限制

| 限制 | 影响 | 处理 |
| --- | --- | --- |
| 硬件扫描中 SNR 恒定 | 一次 LScan/MScan 内不能固定噪底 | 逐步控制（2.2）；真有毫秒级需求时再评估 FPGA |
| Playback 循环时噪声重复 | 独立噪声样本数受波形长度限制，低误码率统计可信度下降 | 用足够长的波形；文档和 UI 说明适用的误码率下限 |
| 改 SNR 或 I/Q 参数要重新生成和下载 | 大波形耗时，输出中断 | 接受；Apply 时提示重新下载 |
| 硬件 CW | 没有 IQ 通道，无法加噪或失衡 | 不支持；用户可用单音波形代替 |
| Streaming | 主机要实时处理，滤波状态和峰值余量无法预扫全记录 | 首版不支持 |
| 短记录的实际 SNR 波动 | 噪声功率有随机偏差 | 生成后按整条逻辑记录把噪声功率精确归一到目标值 |

这些限制是 FPGA 噪声源的真实收益，留作以后迁移的依据（见第七节）。

## 三、功能划分

| 功能 | 实现位置 | 当前状态 | 说明 |
| --- | --- | --- | --- |
| Level Offset | 前端换算，经 API 下发 | 已具备电平配置 API，Offset 换算待接入 | Offset 表示外部路径有符号增益；FFM/LScan/MScan 请求为用户目标减有效 Offset，查询回写同加。统一定义见 3.3 |
| User Flatness（Amplitude Compensation） | API `channel_config_amplitude_compensation` | 已接入（2026-11-05，静态） | 按 RF 频点线性插值，表外按相邻两点线性外推，叠加在工厂校准之上，支持 FScan/MScan；IQ 里的噪声与信号经过同一模拟链，按同一参考面补偿。前端只下发已提交表与开关，经 `TxCommonSettings.userFlatness` 在通用设备设置中、FFM/扫描配置之前下发 |
| Frequency Error | API：下发 `fc + Δf` 给 FFM/LScan | 已静态接入（2026-11-06） | 使用 Hz，不使用 `tx_stream.fc_offset` 数字搬移；载波真正移动后，镜像和噪声中心都以偏移后的载波为中心。Fixed/LScan 生效，FScan/MScan 旁路，名义频率回写减去同一误差 |
| Sample Rate Error | API：`realtime_srate` / `playback_srate` 下发 `Fs·(1+ε)` | 已静态接入（2026-11-06） | 分辨率约 0.1 Hz，下次启动输出时生效；400 MHz 孤立点以后会改为连续区间 |
| LO 模式（Low Spur） | API `channel_config_lo_mode` | 已具备 | 是性能选项，不是损伤 |
| **AWGN** | **前端 IQ 层** | UI 参数已有，处理未接入 | 首版载波参考（SNR + 带宽）；噪声参考为后续前端换算（2.2）。适用离线 Playback 单记录，不支持 CW、Streaming |
| **Invert、I/Q 幅度/相位失衡、I/Q Offset** | **前端 IQ 层** | UI 参数已有，处理未接入 | 与 AWGN 在同一次处理中完成，适用范围相同 |
| Phase Noise | 前端 IQ 层（后续可选） | 未计划 | 简单版不做 |
| Channel Filter（静态多径） | 前端 IQ 层 | 低优先级 | Playback 用循环卷积 |
| Timebase Offset | 无 | 缺接口 | 暂不做 |

### 3.1 处理顺序

```text
前端 IQ：原始 IQ / 原有 AutoScale、IQScale / 选区与 period 补零
        → ① Invert → ② I/Q 幅度/相位失衡 →（Phase Noise，后续）→（Channel Filter，P2）
        → ③ 带限 AWGN 定标与叠加 → ④ 统一缩放，为 Offset 预留余量
        → ⑤ I/Q Offset → ⑥ 最终 int16 量化 → 重算功率指标 → 发布 payload
API/RF：fc + Δf、Level − Oeff、Amplitude Compensation、Fs·(1+ε)、扫描调度
```

- Invert 在最前面，后续参数都在 RF 坐标系下定义：正频偏向上，Q Offset 的符号与输出一致。
- 失衡在加噪之前，只作用于信号，噪声保持圆对称。
- SNR 的信号功率 `Ps` 在失衡之后、加噪之前统计，按整条逻辑记录平均（含记录内空闲段和补零）。
- 信号和噪声共用一个缩放系数，不能各自做峰值归一化。
- I/Q Offset 在最终缩放之后施加，以 %FS（int16 满量程）定义，不被后续 AutoScale 消掉。
- 插入位置是“源 IQ 和原有缩放已确定、不可变 payload 尚未发布”的共享 finalize 阶段，不在设备 I/O 线程执行；各来源（HTRA 本地 builder、ARB/IQS 文件、Digital/DSSS/OFDM 外部 IQ）的接入点、大波形分块和内存约束沿用 `ebfeeb2` 版第二层。
- 所有 IQ 损伤关闭时保持原有直接生成/发布路径，输出与现在逐样点相同。

### 3.2 三种情况在简单版中的处理

| # | 情况 | 简单版 | 替代方式 |
| --- | --- | --- | --- |
| 1 | 一次硬件扫描中每个点的损伤参数不同 | 不支持 | SCPI 逐点：设频率/电平 → 设损伤 → Apply（重新生成和下载）→ 等待建立 → 测量 |
| 2 | 损伤随载波频率变化 | 不支持 | 同上；首版频率误差只做 Hz，不提供 ppm |
| 3 | 扫描中保持噪底固定 | 不支持在一次硬件扫描内实现 | 逐步控制：改 SNR + 推导 Level（2.2） |

### 3.3 Level Offset 参考面与正负号（统一定义）

2026-11-05 接入准备：下列公式用于说明用户面与端口面关系，不表示要在设备 API 前再换算一次。具体储存、Minibar 端口 IPC、共享显示/键盘适配和 API 回写边界统一见 [Level 参考面端口 IPC 与 API 接入准备](level_reference_plane_port_ipc_and_api_integration.md)。该 IPC 重构已静态实施，尚未构建/运行；本节 Noise 相关公式属于损伤提案，不代表当前已有 API 接入。

**定义：Level Offset 是外部路径的有符号增益 O，正值为外接放大器增益，负值为线缆/夹具损耗，不是“源输出额外增加 O dB”的功率扰动。** 旧稿同时使用“显示值 = API 电平 + Offset”和“下发 level + Offset”，混用了两种含义；本次保留有符号路径增益定义，纠正下发方向。

若另行定义“补偿量 C”，则 `C=-O`，`API 请求 = 用户目标 + C` 同样正确；线损 3 dB 对应 O=-3 dB、C=+3 dB。冲突不在加减法本身，而在旧稿把路径增益和反向补偿量都叫 Offset。UI、SCPI 与 API 换算统一只使用前一种 O 定义。

先以 Flatness 关闭、无附加噪声的 CW 为例：`DUT 功率 = RF 端口功率 + O`，所以要得到某个 DUT 目标功率，源端必须输出 `目标 - O`。Keysight ESG/PSG 的官方说明同样以外接放大器 +10 dB 为正 Offset，并给出“输出幅度 = 幅度设定 − Offset”的关系。[官方 Amplitude Offset 定义](https://helpfiles.keysight.com/csg/n5110b/esg_psg_signal_generator.htm)

| 外部路径 | 用户 Offset | 用户目标（CW） | API 电平请求 |
| --- | --- | --- | --- |
| 无增益/损耗 | 0 dB | -20 dBm | -20 dBm |
| 线损 3 dB | -3 dB | -20 dBm | -17 dBm |
| 外接增益 10 dB | +10 dB | -20 dBm | -30 dBm |

Level Offset 位于 Settings → RF Hardware，不由损伤组开关控制（见 [UI 设计第四节](signal_impairments_ui_entry_design.md#四level-offset-与-user-flatnesssettings--rf-hardware)），因此 `Oeff=O`；O=0 即无偏移。所有功率入口与回写只在统一边界换算一次：

```text
Level_api[i] = Level_user[i] - Oeff       // 固定电平、LScan 起止、MScan 各点
Noise_api    = Noise_user - Oeff          // 噪声参考（后续，前端推导 Level 时使用）：dBm 或 dBm/Hz
Power_user   = Power_api + Oeff           // 绝对功率查询/派生显示回到用户参考面
C/N_user     = C/N_api                    // 信号和噪声同移，比例不变
```

- `Level_user`、CommonPanel 的 Level/RMS、扫描编辑表及 Noise Power 都属于同一用户参考面。此处只变换参考面，不把既有 Level 的满幅参考定义改成信号 RMS，也不把信号功率改成加噪后的总功率。
- 以上公式中的 API 电平是 API 的逻辑参考电平，不是额外增加一个“实测端口功率”读数；工厂校准、User Flatness 与硬件增益规划仍由 API 负责。前端不再次把 Flatness 表值加到 level 或 noise_power，避免重复补偿。
- **O 改变时保持 RF 端口输出不变，只改变显示值**，与 Keysight Amplitude Offset 行为一致：API 电平请求不变，Level/RMS、LScan 起止、MScan 各点和 Noise Power 的显示同时平移 O。之后用户新输入的目标才按 `目标 − O` 换算。存储以 API 电平为准，显示由 `API + O` 派生，避免重复换算；回写不得覆盖仍在编辑的 MScan/Flatness 草稿。这样录入线损时端口功率不会突变，避免损坏后级放大器或 DUT。
- 例如信号目标 -20 dBm、噪声目标 -40 dBm，O=-3 dB 时，两者请求为 -17/-37 dBm，C/N 仍为 20 dB。该例以 CW 信号及匹配的噪声积分带宽说明参考面换算。
- User Flatness 同样位于 Settings，不由损伤组开关控制；工厂校准始终保持。

## 四、简单版的范围

- 所有扫描点使用同一组损伤参数：同一个 SNR、同一组失衡和 Offset。
- IQ 损伤只作用于离线 Playback 单记录：Digital、普通 ARB、Multitone 为首批接入与验收样本；AM/FM/PM、Ramp、Pulse、DSSS、OFDM、IQS 单记录完成同一入口接入和验收后开放。开放边界沿用 `ebfeeb2` 版 1.6 节。
- 不支持：硬件 CW、Streaming、内建纯 AWGN、ProgrammedArb/多波形序列（整序列的功率参考和噪声连续性未定义）。
- Playback 扫描沿用现有单波形约束；损伤在 FScan/LScan/MScan 下随波形生效，扫描中 SNR 不变（载波参考）。
- 不支持按点切换参数、点与波形绑定、随频率变化的损伤。

## 五、API 需求

### 5.1 首版：不需要新 API

IQ 损伤只改变下载的波形，下载和启动走现有 `tx_download_waveform` 与 Playback 流程。已有 API 项继续使用：

| 接口 | 用途 | 需确认的点 |
| --- | --- | --- |
| `tx_config_ffm` / `tx_config_lscan` / `tx_config_mscan` | `Level_user − Oeff`、Frequency Error 换算后下发 | 无新增 |
| `channel_config_amplitude_compensation` | Amplitude Compensation | 正负号与生效时机（已在 UI 设计中列为待确认） |
| `tx_stream.realtime_srate` / `playback_srate` | Sample Rate Error | 已确认分辨率和生效时机 |
| `channel_config_lo_mode` | LO 模式 | 无 |

### 5.2 FPGA 就绪后可向 API 方提出的需求（参考，不阻塞首版）

只有出现 2.3 中限制不能接受的需求时才推进，优先级由高到低：

1. **静态 AWGN 噪声源**：参数为开关、SNR（相对下载波形的 RMS）、带宽、seed；要求开启后信号 RMS 功率不变、噪声不重复、输出中修改即时生效。**不要求扫描同步补偿。** 解决噪声重复、改 SNR 要重下、CW/Streaming 不能加噪。
2. **I/Q 损伤寄存器**：Invert、幅度/相位失衡、I/Q Offset，叠加在工厂校准之上，FScan/MScan 每个频点都与该点的校准矩阵组合。解决单独扫失衡时要重下大波形的问题。若采用，要接受失衡同时作用于噪声（如果噪声仍在 IQ 中）或在 FPGA 内先失衡后加噪。
3. 能力查询函数，用于在同一 owner 中选择 IQ 实现或硬件实现。

2026-10-20 版的接口草案（`tx_awgn`、`tx_awgn_status`、`tx_iq_impairment`、`tx_impairment_capabilities`）在 skills 仓库 git `1b4d6d3` 中，可作为与 API 方沟通的起点；其中扫描同步补偿、每点可达 SNR、`AWGN_REF_NOISE` 相关部分已撤销。

## 六、前端工作

- **UI：** 沿用 [UI 设计](signal_impairments_ui_entry_design.md) 的三组单页（AWGN、I/Q、Freq/Clock），各组独立 SwitchButton，无总开关。AWGN 首版只提供 SNR 与噪声带宽（载波参考）。CommonPanel Level/RMS 仍表示信号本身。
- **IQ 处理：** 在公共库（例如 `src/libs/utils`）实现共享的 Invert/失衡/AWGN/Offset 处理，复用 `AwgnModulator` 的随机生成和 FIR；在各来源的发布前 finalize 阶段调用，不在设备 I/O 线程执行。
- **功率指标：** 加损伤后按最终样点重算 RMS/PEP，不保留加噪前的指标。同时记录加噪前的信号 RMS，保证 Level 换算仍以信号功率为准（开 AWGN 前后信号功率不变，总功率增加）。
- **Apply：** 修改 AWGN/I/Q 参数后，从原始生成参数或文件重新物化 IQ，不在上次已损伤的结果上继续叠加；重新下载并按现有流程重启输出。
- **适用性：** 当前路径不支持 IQ 损伤（CW、Streaming、未接入的来源）时，组开关可保留但明确显示未生效，不暗中替换为常数 IQ Playback。
- **换算：** Level Offset 定义见 3.3；Frequency Error 用 `fc + Δf`；Sample Rate Error 用 `Fs·(1+ε)`，`m_sampleRate` 保持名义值。损伤只由所属组开关控制，再分别判断业务适用性。
- **持久化、连接后恢复、SCPI：** 共用 `SignalImpairmentsSettings` owner 和校验规则；Minibar 不展示或编辑损伤；损伤 SCPI 后续补充，并给出情况 1、2、3 的逐步控制示例。
- **噪声参考（后续）：** 前端输入 Pn 和 Ps（或 Pn 和 SNR），按 2.2 推导 SNR 与 Level 并显示派生的总功率，不需要新 API。

## 七、FPGA 就绪后的迁移

- 迁移以能力查询结果为准：某项损伤由硬件支持后，同一个 owner 改走硬件，并删除对应的 IQ 实现，不长期保留两套实现。只有在售型号中确实存在不支持硬件损伤的设备时，才按能力查询在同一 owner 中二选一。
- UI 参数、持久化格式和 SCPI 语义保持不变，迁移只改变实现位置。为此首版参数定义（SNR 的信号功率口径、失衡正负号、Offset 的 %FS 参考）要与 5.2 的硬件定义一致。
- 迁移后的变化需要重新验收：噪声不重复、改参数即时生效、CW/Streaming 可用。

## 八、验收要点

| 场景 | 应有结果 |
| --- | --- |
| 载波参考，SNR 30 → 20 → 10 dB | 带宽内噪声/信号功率比符合设定，误差只来自滤波过渡带和量化 |
| 开关 AWGN，Level 不变 | 信号功率不变，总功率增加 `10·log10(1 + 10^(−SNR/10))` |
| 逐步控制：固定 Pn，Ps 每步改变 | 按 2.2 推导的 SNR 与 Level 下发后，各步噪声功率在规定误差内不变 |
| AWGN + FScan/MScan | 各点 SNR 相同；噪声与信号按同一参考面经 Amplitude Compensation 补偿 |
| 同一配置与 seed 重复 Apply | 输出可复现；不在上次已损伤结果上叠加 |
| I/Q 失衡 + Invert | 镜像位置和正负号约定与文档一致 |
| I/Q Offset | 直流分量符合 %FS 设定，不被 AutoScale 消掉 |
| 所有 IQ 损伤关闭 | 输出与未接入损伤前逐样点相同 |
| CW、Streaming、未接入来源 | 显示未生效，不改变输出 |
| CW 目标 −20 dBm，Level Offset=−3/+10 dB | 请求分别为 −17/−30 dBm；查询回写恢复用户参考面，目标值不被重复换算 |
| 输出中把 Level Offset 从 0 改为 −10 dB | RF 端口功率不变，Level/RMS/扫描点显示下移 10 dB，出现 OFST 标记 |
| 分组 OFF 后再 ON | 保留该组参数；其余组及 Level Offset、Flatness、工厂校准不变 |
| 只开 AWGN / 只开 I/Q / 同时开启 | 三个组 SwitchButton 独立，不清除被旁路组的参数 |
| 设备重连、preset | 前端恢复已保存的设置，UI 与实际输出一致 |

## 相关依据

- [信号损伤需求](信号损伤功能需求文档.md)
- [Impairments 页面 UI 设计](signal_impairments_ui_entry_design.md)
- [PEP 与 RMS 原理](htra_rms_power_multitone_pulse_principle.md)
- [固定 Playback 的 Center/Level 配置链](CenterLevel配置链路重构设计.md)
- [大波形内存与下载](large_waveform_streaming_plan.md)
- [本次任务记录](../TaskLog/2026-11-10_signal-impairments-iq-layer-revert.md)
- [FPGA 版任务记录](../TaskLog/2026-10-20_signal-impairments-fpga-plan.md)
- [IQ 层 P0 版任务记录](../TaskLog/2026-10-10_signal-impairments-p0-ui-design.md)

代码依据：HTRA `fancydevice.cpp` 中的 `startListSweepLocked`、`configurePlaybackStreamLocked`；`awgnmodulator.h`；当前 `3rdParty/h2_api/include/h2_api.h` 没有 AWGN 或 I/Q 损伤接口。
