# 信号损伤方案：FPGA 噪声源与扫描同步补偿

日期：2026-10-20。状态：设计方案，尚未实施；API 部分是向 API/FPGA 方提出的需求草案。

本文替代 2026-10-10 版“P0 前端 IQ 层 AWGN”方案（旧版见 skills 仓库 git 历史 `ebfeeb2`）。方向变化：**AWGN 不再以前端 IQ 处理为主线，改由 FPGA 噪声源实现，并由 FPGA 在扫描时同步做数字补偿。** 原则保持不变：能用 API 的一定用 API，只有确实只能在前端做的才处理 IQ。

全文范围是“简单版”：**所有扫描点使用同一组损伤参数**。每点参数不同（情况 1）和损伤随载波频率变化（情况 2）暂不支持，用 SCPI 逐点控制替代。

## 一、需求场景与常用参数

### 1.1 典型场景

| 场景 | 典型客户 | 用到的功能 | 与扫描组合 |
| --- | --- | --- | --- |
| BER/PER/EVM 随 SNR 变化曲线 | 通信模组、IoT、无线芯片 | AWGN（载波参考） | 通常单频点；SCPI 逐点改 SNR |
| 标准解调性能测试：固定噪声功率，改变信号电平 | 蜂窝、Wi-Fi 等标准化产品 | AWGN（噪声参考） | **LScan 改信号电平，噪声保持不变** |
| 多信道解调一致性：每个信道同一 SNR | 多信道接收机、跳频设备 | AWGN（载波参考） | FScan/MScan，所有点同一 SNR |
| 多信道 + 固定噪底 | 同上 | AWGN（噪声参考） | MScan 频率和电平都变，噪声保持不变 |
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
| **载波参考** | SNR | 噪声随信号电平一起变化，每个点的 SNR 相同 | BER 曲线、多信道同 SNR |
| **噪声参考** | 噪声绝对功率（dBm，在噪声带宽内） | 信号电平变化，噪声不变，每个点的 SNR = 信号功率 − 噪声功率 | 标准解调性能测试、固定噪底找门限 |

**两种方式都只用一组参数。** 噪声参考虽然每个点的 SNR 不同，但用户设置的是一个固定的噪声功率，属于简单版范围，不属于情况 1。

## 二、为什么需要 FPGA 的“数字补偿 + 与扫描同步”

### 2.1 信号链决定了前端做不到

```text
数字基带（信号 s，FPGA 内可叠加噪声 n）→ DAC → 调制器 → 模拟增益/衰减（由 level[i] 控制）→ RF 输出
```

LScan/MScan 每一步改变的是模拟增益 `G_i`。只要噪声在模拟增益之前叠加，输出就是 `G_i · (s + n)`：信号和噪声一起缩放，SNR 不变，噪底跟着电平移动。这只能满足载波参考，不能满足噪声参考。

要让噪声功率固定，就必须让每个扫描点的数字信号/噪声比例跟着 `G_i` 反向变化：

```text
SNR_i = (level[i] + 信号 RMS) − 噪声功率
数字支路：信号与噪声按 SNR_i 配比，总量保持固定的数字余量
模拟增益：按 level[i] 和总功率设置
```

前端 IQ 层做不到，原因是：

1. Playback 扫描只能用一个预先下载的波形，`startListSweepLocked` 明确报错“Playback MScan currently supports a single waveform only”，而这里每个点需要不同的信号/噪声比例。
2. 扫描点和回放波形之间没有绑定关系，主机也拿不到跳频时刻：没有跳频事件，也没有时间戳。Streaming 模式即使实时生成 IQ，也无法与毫秒级 dwell 对齐。
3. 预先加在 IQ 里的噪声和信号经过同一个 `G_i`，无法让它们分开变化。

### 2.2 为什么补偿必须由 API/FPGA 计算，而不是前端算好每点增益再下发

- **只有 API/FPGA 知道真实的增益分配。** 每个点的输出功率由衰减器步进、按频率和温度的工厂校准、User Flatness 共同决定。如果前端计算每点的噪声增益，就要复制一份校准规则，产生第二份业务实现。
- **补偿必须与模拟增益和本振切换在同一时刻生效。** FPGA 本身就在执行扫描调度，它能在每个点的切换和建立时间内同时改变数字配比；主机的软件延时和缓冲做不到这一点。
- **前端下发每点增益数组，本质上就是“每点参数不同”的接口。** 那是情况 1 的基础设施，不属于简单版。

因此简单版的接口是：前端只下发**一个**噪声功率或 SNR，FPGA 根据 `level[i]` 和自身的增益规划，在每个扫描点内部计算并切换补偿。

### 2.3 能力边界

- **可达 SNR 有上限。** 设备自身的本底噪声（DAC 量化噪声、模拟噪声）会随电平一起变化。当 `SNR_i` 接近设备自身的 SNR 时，噪声就无法再保持固定。
- **可达 SNR 有下限。** SNR 很低时，信号在数字域只占很小的比例，受 DAC 动态范围和余量限制。
- **信号功率不变需要模拟余量。** FPGA 为噪声留出数字余量后，需要用模拟增益补回信号功率；最大电平附近可能达不到，需要报 UNLEVEL。

这些范围由 API 在配置时针对每个扫描点预先检查，并通过查询接口返回，不允许静默钳位。

### 2.4 FPGA 噪声源的其他收益

即使不考虑扫描，FPGA 噪声源也明显优于前端 IQ 加噪：

- 噪声不重复：Playback 循环时噪声每次都不同，长时间 BER 统计才可靠。
- 修改 SNR 不需要重新生成和下载波形，大波形尤其受益。
- CW、Playback、Streaming 和各种扫描都能使用。
- 不占用主机 CPU（树莓派上很重要），也不需要为加噪重新做峰值归一化。

## 三、FPGA 具备该能力后各功能的划分

| 功能 | 实现位置 | 当前状态 | 说明 |
| --- | --- | --- | --- |
| Level Offset | 前端换算，经 API 下发 | 已具备 | 名义电平加 offset 后下发给 FFM/LScan/MScan；**噪声参考的噪声功率也要加同一个 offset** |
| User Flatness | API `channel_config_amplitude_compensation` | 已具备 | 按 RF 频点线性插值，表外按相邻两点线性外推，叠加在工厂校准之上，支持 FScan/MScan；噪声和信号经过同一模拟链，按同一参考面补偿 |
| Frequency Offset | API：下发 `fc + Δf` 给 FFM | 已具备 | 推荐 `fc + Δf`，而不是 `tx_stream.fc_offset` 数字搬移：载波真正移动后，镜像和噪声中心都以偏移后的载波为中心，与真实的本振误差一致。扫描模式下默认不叠加用户频偏 |
| Sample Rate Error | API：`realtime_srate` / `playback_srate` 下发 `Fs·(1+ε)` | 已具备 | 分辨率约 0.1 Hz，下次启动输出时生效；400 MHz 孤立点以后会改为连续区间 |
| LO 模式（Low Spur） | API `channel_config_lo_mode` | 已具备 | 是性能选项，不是损伤 |
| **AWGN** | **FPGA** | 需新增 API | 支持载波参考和噪声参考，扫描时 FPGA 同步补偿 |
| **Invert、I/Q 幅度/相位失衡、I/Q Offset** | **FPGA（建议与 AWGN 一起做）** | 需新增 API | 成本很低：FPGA 已有工厂校准用的 I/Q 矩阵和直流修正，增加一组用户参数即可。需要 API 方确认 |
| Phase Noise | FPGA（后续可选） | 未计划 | 简单版不做；放在 FPGA 后，CW 也能加相噪 |
| Channel Filter（静态多径） | 前端 IQ | 低优先级 | 高采样率下复数 FIR 占用大量 FPGA 资源；Playback 用循环卷积 |
| Timebase Offset | 无 | 缺接口 | 暂不做 |

如果 FPGA 不做 Invert/失衡/Offset，这几项就退回前端 IQ 层实现，处理顺序见 3.1。

### 3.1 处理顺序

```text
前端 IQ：  原始 IQ →（Channel Filter，P2）
FPGA：    Invert → I/Q 幅度/相位失衡 → I/Q Offset →（Phase Noise，后续）→ 叠加 AWGN → 工厂校准 → DAC
API/RF：  fc + Δf、level + Level Offset、Flatness、扫描调度
```

- 用户的失衡和 Offset 作用在**信号支路**，在叠加噪声之前生效，所以噪声保持圆对称，SNR 的定义不受失衡影响。如果 FPGA 是直接修改工厂校准矩阵来实现失衡，噪声也会被一起处理，API 方需要说明采用哪种方式。
- Invert 放在最前面，后续参数都在 RF 坐标系下定义：正频偏向上，Q Offset 的符号与输出一致。
- Channel Filter 在前端执行，所以它在 FPGA 的失衡之前。真实链路中信道应该在发射机缺陷之后，这一差异作为产品定义接受，等 Channel Filter 排期时再评估。

### 3.2 三种情况在简单版中的处理

| # | 情况 | 简单版 | 替代方式 |
| --- | --- | --- | --- |
| 1 | 一次硬件扫描中每个点的损伤参数不同 | 不支持 | SCPI 逐点：设频率/电平 → 设 SNR → 等待建立 → 测量。有了 FPGA AWGN，修改 SNR 不需要重新下载，逐点循环很快 |
| 2 | 损伤随载波频率变化 | 不支持 | SCPI 逐点设置；ppm 型频偏由前端换算成每点 `fc[i]`，不属于此情况 |
| 3 | 扫描中保持噪底固定 | **支持（依赖 FPGA）** | 噪声参考 + FPGA 同步补偿 |

## 四、简单版的范围

- 所有扫描点使用同一组损伤参数：同一个 SNR 或同一个噪声功率、同一组失衡和 Offset。
- 扫描基带沿用现有约束：Playback 扫描只用单个波形，也可以使用 Streaming 或 CW。
- AWGN 在 CW、Playback、Streaming 以及 FScan/LScan/MScan 下都可用，具体以 API 能力查询结果为准。
- 不支持按点切换参数，不支持“点与波形绑定”，不支持随频率变化的损伤。

## 五、API 需要提供的接口

下面的结构体和函数名是需求草案，沿用 h2_api.h 的命名和 `extension1/extension2` 风格，最终以 API 方的定义为准。建议做成**通道级**的配置函数，与 `channel_config_amplitude_compensation` 一致，不依附 `tx_stream`，这样重新配置 stream 时损伤设置不会丢失。

### 5.1 已有接口：沿用并确认

| 接口 | 用途 | 需确认的点 |
| --- | --- | --- |
| `tx_config_ffm` / `tx_config_lscan` / `tx_config_mscan` | Level Offset、Frequency Offset 换算后下发 | 开启 AWGN 后的最大可用电平（见 5.3 的合同） |
| `channel_config_amplitude_compensation` | User Flatness | 已确认；噪声与信号按同一参考面补偿 |
| `tx_stream.realtime_srate` / `playback_srate` | Sample Rate Error | 已确认分辨率和生效时机 |
| `channel_config_lo_mode` | LO 模式 | 无 |

### 5.2 新增：能力查询

```c
typedef struct
{
    uint8_t  awgn_supported;           // 是否支持 AWGN
    uint8_t  awgn_noise_ref_supported; // 是否支持噪声参考（扫描同步补偿）
    uint32_t awgn_tx_modes;            // 支持的模式位掩码：TX_CW / TX_REALTIME / TX_PLAYBACK
    double   awgn_bandwidth_min;       // 噪声带宽下限（Hz）
    double   awgn_bandwidth_max_ratio; // 噪声带宽上限，相对当前采样率的比例
    float    awgn_snr_min;             // SNR 下限（dB）
    float    awgn_snr_max;             // SNR 上限（dB）

    uint8_t  iq_impairment_supported;  // 是否支持 Invert / 失衡 / Offset
    float    gain_imbalance_max;       // 幅度失衡绝对值上限（dB）
    float    phase_imbalance_max;      // 相位失衡绝对值上限（度）
    float    iq_offset_max;            // I/Q Offset 绝对值上限（满量程比例）

    void* extension1;
    void* extension2;
} tx_impairment_capabilities;

H2_API int channel_tx_query_impairment_capabilities(channel* ch, tx_impairment_capabilities* capabilities_o);
```

单独提供查询函数，不修改现有 `tx_channel_capabilities` 的布局。前端只根据这个查询结果开放 UI，不按型号硬编码。

### 5.3 新增：AWGN

```c
#define AWGN_REF_CARRIER   0   /* 载波参考：SNR 固定 */
#define AWGN_REF_NOISE     1   /* 噪声参考：噪声功率固定 */

typedef struct
{
    uint8_t  state;              // STATE_OFF / STATE_ON
    uint8_t  reference;          // AWGN_REF_CARRIER / AWGN_REF_NOISE
    double   bandwidth;          // 噪声总带宽（Hz），以信号中心为中心
    float    snr;                // 载波参考：带宽内的信号/噪声功率比（dB）
    float    noise_power;        // 噪声参考：带宽内噪声总功率（dBm），与 level 同一参考面
    float    stream_signal_rms;  // TX_REALTIME：主机提供的信号 RMS（dBFS）
    uint64_t seed;               // 0：自动；非 0：可复现

    void* extension1;
    void* extension2;
} tx_awgn;

typedef struct
{
    double bandwidth_actual;     // 实际生效的噪声带宽（滤波器量化后）
    float  snr_min_actual;       // 当前载波计划各点中的最低 SNR（dB）
    float  snr_max_actual;       // 当前载波计划各点中的最高 SNR（dB）
    float  noise_power_actual;   // 实际噪声功率（dBm）
    uint32_t clip_count;         // 数字饱和计数

    void* extension1;
    void* extension2;
} tx_awgn_status;

H2_API int tx_config_awgn(channel* ch, const tx_awgn* setting);
H2_API int tx_query_awgn(channel* ch, tx_awgn* setting_o, tx_awgn_status* status_o);
```

需要 API 方明确的合同：

1. **信号功率不变。** 同一个 level、同一个波形，开启 AWGN 前后信号支路的 RF 功率相同，噪声额外叠加，总功率增加。FPGA 内部的数字余量和模拟补偿由 API 负责，前端不关心。
2. **信号 RMS 从哪里来。** Playback 建议由 API 在 `tx_download_waveform` 时计算每个波形的 RMS（全记录平均，包含空闲段和补零），并通过查询接口返回，供前端显示和核对。多波形序列中每个波形使用各自的 RMS。Streaming 模式无法预先统计，由主机通过 `stream_signal_rms` 提供（AutoScale 时已知）。CW 的信号 RMS 按 0 dBFS 处理。
3. **噪声参考与扫描。** FScan/LScan/MScan 下，FPGA 根据每个点的 `level[i]`、工厂校准和 Flatness 计算该点的信号/噪声配比，并与该点的频率和电平切换同时生效。配置时对所有点预先检查，任何一点超出可达 SNR 范围就返回明确的告警或错误，不静默钳位。
4. **参考面。** `noise_power` 与 `level` 在同一参考面上：已包含 Flatness，不包含前端的 Level Offset（Level Offset 由前端对两者做同样的换算）。
5. **噪声中心。** 噪声以信号中心为中心。如果使用 `tx_stream.fc_offset` 做数字搬移，需要说明噪声是在搬移之前还是之后叠加。
6. **生效时机。** 希望输出过程中修改 SNR 或噪声功率能立即生效，不需要重启输出。如果只能在下次启动时生效，需要明确说明，前端会按此提示用户。
7. **不重复。** 噪声发生器的周期远大于任何实际测试时长；`seed` 非 0 时，同一配置的输出可以复现。
8. **带宽。** 实际带宽会因滤波器而量化，通过 `bandwidth_actual` 返回。修改采样率后，带宽仍然按绝对 Hz 定义，超出范围时报错。

### 5.4 新增：I/Q 损伤（建议与 AWGN 一起做）

```c
typedef struct
{
    uint8_t state;              // STATE_OFF / STATE_ON
    uint8_t invert;             // 频谱反转：Q 取反
    float   gain_imbalance;     // dB；正值表示 I 路增益相对 Q 路更大
    float   phase_imbalance;    // 度；正值表示 I/Q 基向量夹角大于 90°
    float   i_offset;           // I 路直流偏置，相对信号满量程参考的比例
    float   q_offset;           // Q 路直流偏置，同上

    void* extension1;
    void* extension2;
} tx_iq_impairment;

H2_API int channel_config_iq_impairment(channel* ch, const tx_iq_impairment* setting);
H2_API int channel_query_iq_impairment(channel* ch, tx_iq_impairment* setting_o);
```

需要 API 方明确：

- 正负号约定与上面的注释一致，UI 和 SCPI 使用同一约定。
- 作用在信号支路，在叠加噪声之前（见 3.1）。
- Offset 以信号满量程为参考，开启 AWGN 后 FPGA 内部的数字余量不会改变 Offset 对应的 dBc。前端负责把 Offset 换算成相对信号 RMS 的 dBc 显示给用户。
- 叠加在工厂校准之上，不改写工厂校准值。

### 5.5 后续可选：Phase Noise

简单版不做。如果以后做，建议的接口是 `channel_config_phase_noise(ch, state, offsets[], dbc_hz[], points)`，即一条与载波频率无关的附加相噪曲线。

### 5.6 通用要求

- `channel_preset` 把所有损伤恢复为关闭或零；`device_close` 后不保留设置，由前端在连接设备后恢复。
- 新增告警码，并在 `device_query_discription_retval` 中提供描述：SNR 在某个扫描点超出可达范围、数字饱和、开启 AWGN 后电平达不到（UNLEVEL）。
- 所有新接口都有对应的查询函数，前端用查询结果回写 UI，不以请求值代替实际值。

## 六、前端工作

- **UI：** AWGN 页提供开关、参考方式、SNR 或噪声功率（支持 dBm 和 dBm/Hz 两种输入，按带宽换算）、噪声带宽。根据 level、信号 RMS 和查询结果显示派生值：噪声参考下显示各点的 SNR 范围，载波参考下显示噪声功率和总功率。I/Q 页提供 Invert、失衡和 Offset。输出补偿页（Level Offset、Flatness）独立于损伤页。
- **换算：** Level Offset 同时作用于 level 和 noise_power；频偏使用 `fc + Δf`；采样率误差使用 `Fs·(1+ε)`，`m_sampleRate` 保持名义值。
- **Apply：** FPGA 负责的损伤只是硬件配置，不重新生成或下载 IQ。生效时机以 API 合同为准，状态以查询结果为准。
- **能力开放：** 只按 `channel_tx_query_impairment_capabilities` 的结果开放功能，不支持的模式明确提示原因。
- **持久化、连接后恢复、SCPI、Minibar：** 共用同一个参数 owner 和校验规则。
- **前端 IQ 层只保留 Channel Filter（P2）。** 如果 FPGA 不做 I/Q 损伤，再加入 Invert、失衡和 Offset。
- **SCPI 逐点流程：** 在文档中给出情况 1、2 的逐点示例，说明每步的生效和等待条件。

## 七、FPGA 就绪之前的过渡

FPGA 接口就绪前，是否先做前端 IQ 层 AWGN 由产品排期决定：

- 如果先做，只能支持载波参考，不能支持扫描时固定噪底，并且有噪声循环重复和需要重新下载的问题。实现可以参考旧版方案（git `ebfeeb2`）。
- FPGA 就绪后删除 IQ 层 AWGN，不长期保留两套实现。只有当在售型号中确实存在不支持 FPGA AWGN 的设备时，才保留 IQ 实现，并由同一个 owner 按能力查询结果选择其中一种。

## 八、验收要点

| 场景 | 应有结果 |
| --- | --- |
| 载波参考，SNR 30 → 20 → 10 dB | 带宽内噪声/信号功率比符合设定；修改不需要重新下载 |
| 开关 AWGN，level 不变 | 信号功率不变，总功率增加 `10·log10(1 + 10^(−SNR/10))` |
| 噪声参考 + LScan | 各点噪声功率在规定误差内保持不变，信号按 level 变化 |
| 噪声参考 + MScan（频率和电平都变，Flatness 开启） | 各频点噪声功率在同一参考面上保持不变 |
| 某扫描点超出可达 SNR | 配置时返回明确告警，UI 显示具体的点 |
| Playback 长时间输出 | 噪声不随波形周期重复 |
| CW + AWGN、Streaming + AWGN | 按能力查询结果可用 |
| I/Q 失衡 + Invert | 镜像位置和正负号约定与文档一致 |
| 开启 AWGN 时接近最大电平 | 返回 UNLEVEL，不静默降电平 |
| 设备重连、preset | 前端恢复已保存的设置，UI 与查询结果一致 |

## 相关依据

- [信号损伤需求](信号损伤功能需求文档.md)
- [PEP 与 RMS 原理](htra_rms_power_multitone_pulse_principle.md)
- [固定 Playback 的 Center/Level 配置链](CenterLevel配置链路重构设计.md)
- [大波形内存与下载](large_waveform_streaming_plan.md)
- [本次任务记录](../TaskLog/2026-10-20_signal-impairments-fpga-plan.md)
- [上一版任务记录](../TaskLog/2026-10-10_signal-impairments-p0-ui-design.md)

代码依据：HTRA `fancydevice.cpp` 中的 `startListSweepLocked`、`configurePlaybackStreamLocked` 和 `configureRealtimeStreamLocked`，以及当前 `3rdParty/h2_api/include/h2_api.h`（api 39，没有 AWGN 或 I/Q 损伤接口）。本文中的新接口都是需求草案，尚未实现。
