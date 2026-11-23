# TrueArb 假设下的硬件契约、AWG 波形算法与 Arb 编辑业务

> 状态：硬件契约分析、算法背景与产品调研，更新于 2026-10-19。Amplitude、DC Offset、Output Load 及导出与播放的关系见第 1.5 节。波形编辑器的具体交互、数据状态和阶段设计见 [SGStudio Arb 编辑器设计](awg_truearb_arb_editor_design.md)。下文的“当前代码”指工作区实现，不代表已接入设备或已验证模拟输出。算法依据是 [C0 重构 TaskLog](../TaskLog/2026-09-29_refactor-awg-truearb-c0.md)。

## 1. 一条共同的播放契约

SIGLENT SDG6000X 手册把 Arb 分成 DDS 与 TrueArb。TrueArb 的定义是按设定输出样点率逐点输出记录；可选 0 阶保持或线性插值，示例机型允许 2–20 Mpts、1 μSa/s–300 MSa/s（本地 `C:\Users\jsl\Desktop\SignalDocs\SIGLENT_SDG6000X_UserManual_E01A.pdf`，PDF 页 21、47–49）。**这些是 C0 离线模型的参照值，不是 SGStudio 目标硬件已经证实的规格。**

当前 C0 把所有波形最终收敛成：

```text
归一化记录 d[0 ... L-1] + 输出样点率 SRate + 插值选择 + 电平/负载设置
                     ↓
每隔 1/SRate 秒读下一个点；读完 L-1 后从 0 开始
```

因此 `t[n]=n/SRate`，`T_record=L/SRate`，`f_repeat=SRate/L`。末点到下一轮首点也占一个采样间隔；生成一周期时不能在数组末尾再复制一次首点。`f_repeat` 只是**整段记录重复率**：Arb 文件含 10 个周期时，其中可见信号的基频可以是 `10·f_repeat`。SIGLENT 的 [EasyWave 双音实例](https://www.siglenteu.com/application-note/easywave-two-tone-arbitrary-waveform/)也明确将 Arb 的 Frequency 解释为整个内存波形的重复率。代码中的 [AwgPlaybackPlan](../../../vsg2.0/src/plugins/awg/awgplaybackplanner.h)直接给出这两个派生量。

这个契约成立的前提是：目标设备确实能保存这段记录，以可设置的**记录点更新率**按序循环读出，并把数字记录送到可控的 LF 模拟输出。所谓“逐点”约束的是每个原始记录点的读取顺序和时间语义；设备内部即使采用更高的 DAC 时钟、插入插值点，也可以满足这一外部契约。它不要求某种特定的 FPGA 或 DAC 时钟架构。[SDG6000X E01A 手册](https://siglentna.com/wp-content/uploads/dlm_uploads/2017/12/SDG6000X_UserManual_UM0206X-E01A.pdf)说明参考机型以设定采样率逐点输出；[SIGLENT 产品说明](https://siglentna.com/waveform-generators/sdg6000x-series-pulsearbitrary-waveform-generator/)明确称其不会跳过原始点，提供 0 阶保持和线性插值。这是参考机型的能力，尚非 SGStudio 目标设备的结论。

当前 [能力表](../../../vsg2.0/src/plugins/awg/awgwaveformcapabilities.h)采用手册参照的 `2≤L≤20,000,000`、`1e-6≤SRate≤300e6`。所有七类均由 [规划器](../../../vsg2.0/src/plugins/awg/awgplaybackplanner.cpp)验证，再由 [生成器](../../../vsg2.0/src/plugins/awg/awgwaveformgenerator.cpp)交付数字记录。此处的“生成完成”只是离线记录可预览、可导出；[面板](../../../vsg2.0/src/plugins/awg/awgpanel.cpp)仍提示 `C0 debug preview only - no device output`。

当前新建波形使用 **50 Ω 预期负载和 Linear 线性播放插值**，面板移除 Output Load、Custom Load 和 Interpolation 的编辑控件，在摘要只读显示当前有效值。内部开路 Amplitude 默认仍为 4 Vpp、Offset 为 0 V，因此 50 Ω 下的新建面板幅度与预览为 2 Vpp，即满幅形状的 −1～+1 V；本轮没有把内部幅度同步加倍。完整 SGStudio CSV 仍恢复文件中的合法负载与插值；已有 HighZ、自定义负载或零阶保持记录不会因新建默认值而改变。

线性插值连接相邻原始样点，也连接记录末点到下一轮首点；它不增加原始点数、不改变采样率，也不执行 FIR 重采样。相邻不同电平点在 `1/SRate` 内完成理想直线过渡，其 10%–90% 边沿为 `0.8/SRate`。低—高—低中只有一个高样点时，连接结果是三角形，没有高平台。窄脉冲、毛刺和单路码型必须检查实际采样网格及边沿，不能把数字电平值理解为已保证理想阶跃输出。

### 1.1 `L` 与 `N`：记录长度和每周期点数不是同一概念

`L` 是**最终交给播放模型的整段记录的点数**，对应 [AwgPlaybackPlan::recordLength](../../../vsg2.0/src/plugins/awg/awgplaybackplanner.h)；有效生成结果要求它等于 `samples.size()`。所有波形都有 `L`；播放索引是 `0…L-1`，每读完 `L` 点回到开头。`L` 是无量纲的点数，本身既不是时长，也不是信号频率。给定有效记录点率 `SRate` 后，点距是 `1/SRate`，整段时长才是 `L/SRate`，整段重复率才是 `SRate/L`。播放插值可以改变相邻原始点之间的模拟形状，但不增加 C0 记录的 `L`；预览为了绘图抽点也不改变它。

`N` 是本文对 [AwgProfile::periodPoints](../../../vsg2.0/src/plugins/awg/awgprofile.h) 的数学记号，意思是**软件准备用多少个点描述一个信号周期**。只有 Sine、Square、Pulse、Ramp 这四种“按频率 `f` 生成周期形状”的波形输入 `N`。当前 [规划器](../../../vsg2.0/src/plugins/awg/awgplaybackplanner.cpp)恰好只生成一个周期，所以令 `L=N`、`SRate=f·N`；[生成器](../../../vsg2.0/src/plugins/awg/awgwaveformgenerator.cpp)在 `n=0…N-1` 上按 `u_n=fract(n/N+φ/360°)` 求值。由此 `T_record=N/(fN)=1/f`，`f_repeat=f`。数组里没有第 `N` 个“重复的首点”；从 `N-1` 到下一轮的 `0` 仍占一个点距。这里的 `L=N` 是**当前 C0 只交付一周期**的设计选择，不是 TrueArb 硬件规定所有记录必须只含一周期。

| 波形类别 | `L` 从哪里来 | `N` 是否有定义 | `SRate/L` 此时表示什么 |
| --- | --- | --- | --- |
| Sine、Square、Pulse、Ramp | 用户设定 `periodPoints=N`，规划器令 `L=N` | 有：每个信号周期的生成网格点数 | 一周期记录的重复率，等于请求的信号频率 `f`（只在设备精确接受 `SRate=fN` 时也是实际端口频率）。 |
| Arb | 导入文件后的**有效样点数**；当前整点起点移位不改变 `L` | 当前没有独立 `N`：文件可能是一次瞬态，也可能在整段中包含一个或多个内部周期、多种频率分量 | 整个文件记录的重复率，不自动等于其中某个可见分量的频率。 |
| Noise | 用户设定的 `noiseRecordLength`，用于生成一段有限随机序列 | 没有：不是“每个噪声周期的点数” | 有限序列被循环重放的次数/秒；不是噪声的某个信号频率或带宽。 |
| DC | 规划器固定为 `L=2`，两个数字点都是 `0` | 没有：恒定电平不存在需要采样的一周期 | 两点记录的数学重复率；输出仍是 DC，不会因此产生 `SRate/2` 的信号频率。 |

`AwgProfile` 是共享请求结构，因此其中的 `periodPoints` 字段在选择 Arb、Noise、DC 时仍然存在；但当前规划器在这三类分支**不读取它**。“没有 `N`”指它不属于该波形的播放语义，而不是说结构体里没有这个字段。规划完成后，统一的播放计划也只保留 `recordLength=L`，不保留一个独立的 `N`。

**周期波形的 `N` 决定离散表达精度，`f` 决定时间刻度。**例如 Sine 取 `N=8`、`f=1 kHz`，C0 生成 `L=8` 个点，请求 `SRate=8 kSa/s`；点时刻为 `0、0.125、…、0.875 ms`，下一轮首点在 `1 ms`，而不是把首点再放入 `d[8]`。固定 `f` 增加 `N` 时，C0 会相应提高请求 `SRate`，使记录时长仍为 `1/f`，但相位、Duty、拐点和边沿有更细的点网格；设备采样率范围、记录长度与具体形状的可表达性仍可能使该组合无效。固定 `N` 改 `f` 时，点值对纯相位形状可保持同一网格，播放点率和周期一起变化；Pulse 的脉宽/边沿是以秒输入，改变 `f` 还会改变这些时长在周期中的相对比例，可能需要重新生成或因点距不够而被拒绝。

**Arb 与 Noise 的 `L` 只定义一段记录，不定义 `N`。**例如两者都取 `L=1,000`、`SRate=1 MSa/s`，则整段都长 `1 ms`、每秒重复 `1,000` 次。Arb 若文件恰好由十个完全相同的 100 点片段组成，且片段本身的最短周期确为 100 点，才可从内容推知信号周期为 100 点、基频为 `10 kHz`；C0 并不检测或保存这个“推知的 `N`”。一次瞬态没有可供参数波形使用的内部周期；双音记录即使整段具有共同重复周期，也不能随意用其中一个单音的周期充当统一的 `N`。WAV 头里的源采样率、CSV 的时间列也没有自动设置 C0 播放 `SRate`，因此相同 `L` 的 Arb 文件可按不同速度播放。

Noise 的 `L=1,000` 在上述例子中还使频谱离散频点间隔为 `Δf=SRate/L=1 kHz`，第 `j` 个正频点位于 `j·Δf`；最高可表征频率受 `SRate/2` 限制。固定 `SRate` 增大 `L`，记录更长、重复更慢、频点更密；固定 `L` 增大 `SRate`，记录更短、重复更快，频点间隔和 Nyquist 上限同时增大。当前带限 Noise 的规划器据 `floor(带宽/Δf)` 选可保留的频点。无论点数多大，只要设备循环同一段有限噪声，它就会按 `L/SRate` 重复，不能称为无限长随机过程。DC 则即使 `L=2`、`SRate=1 MSa/s` 算出 `f_repeat=500 kHz`，两个点相同也不会产生 500 kHz 的波动。

因此，编辑器或未来设备接口改变 Arb/Noise 的 `L` 时，应展示**整段时长和重复率**的变化，Noise 还应展示频谱网格的变化；不要把 `L` 改名为“每周期点数”。若 Arb 编辑后要保持整段重复率，须显式请求新的 `SRate=f_repeat·L_new`。周期波形则由 `N` 和 `f` 共同导出 `L=N`、`SRate=fN`，不能把 B0 的 `M`（目标缓冲长度）或 `K`（其中的整数周期数）带回 C0 的参数定义。

### 1.2 C0 对目标硬件的最小可观察契约

以下是**由 C0 软件产物推导出的接入条件**，不是目标硬件已具备的功能清单。只有这些行为成立，`d[n]` 才能被称为设备将按预期播放的记录。

| 设备能力 | C0 为什么依赖它 | 接入时需要确认的事实 |
| --- | --- | --- |
| 独立 LF 模拟输出路径 | C0 交付的是单通道实值电压形状，不是 RF 的交织 IQ 样点。数字数组本身不会形成端口电压。 | LF 端口或明确的路由、通道归属、DAC/输出级、与 RF 资源的关系。不能拿现有 RF IQ 下载 API 当作 LF 上传证据。 |
| 上传并保存恰好 `L` 个原始点 | Arb 文件点列就是播放内容；周期形状也只生成一个周期。设备若强制补点、截点或只接受固定长度，`T_record` 和样点形状会变化。 | 记录内存容量、有效 `L` 集合与对齐、每通道/共享容量、上传完成及错误反馈；C0 假设的 2–20 Mpts 是否真的成立。主机 `float32` 只是内部表示，不能视为设备协议。 |
| 按序读每个记录点，并在 `L-1→0` 连续循环 | C0 的 `T_record=L/SRate`、Arb 不重采样、周期形状只存一周期都依赖这个行为。若用 DDS 相位跳点，点列细节及时间关系可能改变。 | 是否跳点/丢点、循环边界是否插入停顿或额外首点、更新与换表会否造成断裂。0 阶保持时，末点到首点也应占一个样点间隔。 |
| 可设置记录点更新率 `SRate` | Sine/Square/Pulse/Ramp 用 `SRate=f·N` 设信号频率；Arb/Noise 用 `SRate/L` 设整段重复率。若只有固定 125 MSa/s，C0 的直接交付不成立。 | 合法范围、步进/时钟精度、可用的 `SRate` 与 `L` 组合，以及设置后可回读的实际值。物理 DAC 可在内部使用另一时钟，关键是记录点的有效时间间隔。 |
| 数字码值到电压的确定映射 | C0 的 `d[n]∈[-1,1]` 与 Vpp/偏移分离；设备适配层必须把样点量化并配置输出级，才能让预览和端口电平对应。 | DAC/上传码宽、符号编码、字节序、零码、端点量化、可用电压范围、偏移与幅度耦合、校准和模拟带宽。16-bit 是 SIGLENT 示例机型事实，不是目标格式。 |
| 输出控制与可判定的执行结果 | “文件已导入”“设备已接收记录”“已开始输出”是不同状态；未来界面不能用离线生成成功代替硬件成功。 | 启停、选择记录/通道、配置生效顺序、返回错误与实际设置值。C0 当前只假设连续循环；触发、Burst、无缝热切换不是本阶段最低要求。 |

当前面板新建默认线性插值，文件契约仍支持恢复零阶保持或线性。未来设备接入必须兑现实际记录的插值和 `L-1→0` 连接语义；不支持某值时应明确拒绝对应输出，不能只改变预览或静默替换文件设置。SDG6000X 的 [编程指南](https://int.siglent.com/upload_file/user/SDG6000X/SDG_ProgrammingGuide_PG_E03B.pdf)把 Arb 数据命令和 TrueArb 的采样率/插值设置作为设备控制项；[官方编程示例](https://siglentna.com/application-note/16-bit-step-arb-sdgx/)也先下发点列，再设置 TrueArb 样点率。这证明参考机型有相应控制面，**不**规定 SGStudio 的未来命令、码值格式或设备能力。

DC 是容易遗漏的特例：当前生成器交付两个数字零点，目标直流电压另在 `dcLevelV` 中。未来设备适配层必须实际施加对应的模拟电平或等效 DAC 常值；只上传两个零点会输出零附近，而不会自动得到用户设定的 DC。Vpp、偏移和 HighZ/负载显示换算也须由目标输出级规格确认。若硬件只支持别的电平单位或端口阻抗，应在设备边界换算，并用测量验证，不能直接套用 SDG6000X 的 50 Ω 理想模型。

### 1.3 采样率与长度的边界不能只看两个最大值

当前 C0 [规划器](../../../vsg2.0/src/plugins/awg/awgplaybackplanner.cpp)只检查 `L`、`SRate` 各自是否落在参照区间，没有设备能力查询，也没有二者组合限制。真实设备可能只支持离散时钟、特定长度、不同通道共享内存，或者对高采样率和长记录施加联合限制。两项范围单独合法，**不等于其任意组合都可播放**。例如按当前纯数学模型，`L=20,000,000`、`SRate=1 μSa/s` 会使一次循环长达约 63.4 万年；这是公式结果，不是对参考机型或目标机型实用工作范围的断言。

尤其要区分请求值与设备接收值。E01A 手册的 TrueArb 菜单说明，无效新参数可被设为最近的有效值；这提醒我们不能假定任意浮点 `SRate` 都能精确实现。若周期波形请求 `f`、生成 `N` 点，软件算出 `S_req=f·N`；设备实际采用 `S_actual` 时，端口基频应是 `f_actual=S_actual/N`，Arb 整段重复率则是 `S_actual/L`。未来适配层应读取或取得被设备确认的实际样点率及设置结果；若设备无法提供足够信息，界面就不能把请求频率标成已验证的输出频率。需要近似时可以明确报告误差，但这仍是**核实设备时钟的误差处理**，不是恢复 B0 的固定时钟 `M/K` 搜索。

接入验收可用一份短而不对称的记录逐项检查：下载后读回长度和点码（若接口提供）；固定 `L` 改两档 `SRate`，用示波器确认整段时长按 `L/S_actual` 变化、每个原始点依序出现、循环边界无额外间隔；分别加载保持和线性记录观察点间形状；再分别测端口 Vpp、偏移、DC 和真实负载。长记录、高更新率、双通道同时播放与换表时的资源/吞吐则需按目标设备规格单独测。上述是**未来硬件验收建议**，并非已经执行的测试。

### 1.4 为什么 C0 的软件交付算法比 B0 简单

[过时的 B0 方案](awg_model_b0_lf_chain_and_waveform_processing.md)假设硬件以固定 `F_s=125 MSa/s` 循环读取 `M` 点（`64≤M≤1,048,576`，16 点对齐）。为了让缓冲首尾相位相接，软件须在缓冲里安排整数 `K` 个周期，求 `f_actual=K·F_s/M`，搜索合法 `M/K` 并报告频率误差。B0 的 Arb 文件 `L` 点被解释成“一周期的形状”，再经源频谱提取、可播放谐波选择、目标频谱布局、IFFT 与幅值处理，交付通常**不是原文件**的 `M` 点缓冲。Square/Pulse/Ramp 也按 B0 的软件截止重合成有限谐波。这些步骤服务于 B0 的固定时钟和当时选择的数字带限策略。

| 交付问题 | B0 固定时钟模型 | C0 TrueArb 假设 |
| --- | --- | --- |
| 设备最终读什么 | 软件合成的 `M` 点最终缓冲，可能含 `K` 个周期 | 原始或直接生成的 `L` 点记录，连续循环 |
| 频率怎样落到设备 | 固定 `F_s` 下搜索 `M/K`；`f_actual=K·F_s/M` | 周期形状取 `L=N`、请求 `SRate=f·N`；Arb 的整段重复率为 `SRate/L` |
| Arb 文件怎样处理 | 把 `L` 点当一周期形状，提取频谱后重建到 `M` 点网格；源高频可能被删 | 校验并保留文件的 `L` 点和值序；仅显式起点移位改变顺序，不做隐式重采样或带限 |
| Square/Pulse/Ramp 怎样生成 | 分段参考形状 → 傅里叶系数 → 截止/网格选谐波 → IFFT → 峰值缩放 | 对 `n=0…N-1` 直接按相位与形状公式写每个输出样点 |
| 电平怎样进入记录 | B0 把数字幅度/偏移以及必要的防越界缩放写入缓冲 | C0 记录只存归一化形状；Vpp、偏移、DC 电平作为独立输出设置 |
| 软件仍需做什么 | 规划、导入、合成、带限、缩放、预览与导出 | 当前负责导入、生成形状、检验 `L/SRate` 与可表达性、预览和导出；未来设备适配还需量化/传输；可选带限 Noise 仍用 IFFT |

例如用户要 1 kHz 正弦，C0 选 `N=16,384` 点时直接计算一周期记录并请求 `SRate=16.384 MSa/s`，理论上循环 1 ms；B0 在固定 `125 MSa/s` 下可选 `M=250,000`、`K=2` 才恰好得到 1 kHz，软件先找这组合法格点，再把两个周期写满缓冲。若请求 100 Hz，B0 的最大 `M=1,048,576` 使其最低一周期频率约为 119.209 Hz，无法给出周期计划；C0 同样用 `N=16,384` 时只需请求 `SRate=1.6384 MSa/s`，在**离线假设**中可表示，并不保证目标硬件实现。对 Arb，若导入 `L=1,000` 点并设 `SRate=1 MSa/s`，C0 交付仍为这 1,000 点、1 ms 一轮；B0 则必须先决定目标 `M/K`，再把来源当形状变成目标缓冲。这里简化的核心是**把时间控制从“软件选固定时钟网格并改写波形”移到“设备按指定记录点率读原记录”**。

从计算步骤看，当前 C0 的周期形状生成通常是遍历 `N` 点并直接求值；Arb 导入后保持 `L` 点，整点移相时才另做一次循环复制。B0 除 `M/K` 搜索外，Arb 还需处理源 `L` 点频谱及目标 `M` 点频谱，再作逆变换；Square/Pulse/Ramp 也有系数、谐波上限和逆变换路径。因此减少的是频率落格、频域重建以及为目标缓冲改点数的**交付步骤**，并非声称所有 C0 工作都是常数开销或省去了文件读取、内存分配。当前带限 Noise 仍是频域例外。

简化有明确条件与代价。设备/固件需要承担可变有效点率、存储与循环寻址、点间插值、码值到模拟电平的映射；软件仍负责生成正确的离散序列、拒绝无法表示的脉宽/占空比、处理导入格式，并在真实接口前量化/传输。C0 删除 B0 对 Square/Pulse/Ramp/Arb 的傅里叶截止，**不意味着硬件自动替它完成抗混叠**：陡边、窄脉冲及 Arb 文件中的高频成分仍受 `SRate/2`、DAC、插值与模拟带宽约束，采样前已经折回的频谱不能靠输出滤波器修复。需要受控带宽时，应作为明确的生成/编辑选项定义，不应在 Arb 文件导入时偷偷改变点列。带限 Noise 的 IFFT 则仍是当前软件明确保留的业务算法。

### 1.5 Amplitude DC Offset 与 Output Load

**点列保存形状，Amplitude 和 DC Offset 定义电压映射，Output Load 指定按什么外部负载显示电压。** 修改这三个参数会改变预期电压或其显示换算，归一化点列仍保持原值。当前 AWG 是离线预览与 CSV 导出；下文的播放电压是 C0 的理想目标，实际设备接入仍须满足第 1.2 节的契约。本节适用于 LF AWG，不把 RF 链路的功率设置套入这里。

面板显示的是**当前预期负载下的电平**；[AwgProfile](../../../vsg2.0/src/plugins/awg/awgprofile.h) 内部保存开路电平，由 [AwgPanel](../../../vsg2.0/src/plugins/awg/awgpanel.cpp) 换算显示和输入。设面板 Amplitude 为 `A`，DC Offset 为 `O`，则非 DC 波形的理想负载电压为：

```text
V[n] = O + A/2 × d[n]                 d[n] ∈ [-1,1]

k = 1                                HighZ
k = R/(R+50 Ω)                       有限负载 R
A = k × 开路 Amplitude
O = k × 开路 DC Offset
```

这里已经使用负载换算后的面板值，计算 `V[n]` 时不能再乘一次 `k`。

#### 1.5.1 Amplitude 决定相对偏移的电压跨度

Amplitude 的单位是 **Vpp，即峰峰值**；它定义归一化 `−1` 到 `+1` 对应的完整电压跨度。对于覆盖这两个端点的波形，上电平为 `O+A/2`，下电平为 `O−A/2`。Vpp 不等于单边峰值，也不等于有效值 RMS。

| 面板 Amplitude | 面板 DC Offset | d=−1 对应电压 | d=+1 对应电压 |
| --- | --- | --- | --- |
| 4 Vpp | 0 V | −2 V | +2 V |
| 2 Vpp | 0 V | −1 V | +1 V |
| 2 Vpp | 1 V | 0 V | 2 V |
| 3.3 Vpp | 1.65 V | 0 V | 3.3 V |

调大 Amplitude 会扩大各点相对 `O` 的距离；调小则收缩。点的先后顺序、采样率、点数和记录时长不受影响。

**设置的 Amplitude 不一定等于整条记录实际达到的峰峰值。** Arb/Noise 若只覆盖 `d=−0.5～+0.5`，在 4 Vpp、0 V 偏移下实际显示 −1～+1 V，即 2 Vpp；不会自动拉满到 −2～+2 V。一般有 `实际记录 Vpp = A/2 × (max(d)−min(d))`，极值来自实际点列。

#### 1.5.2 DC Offset 定义数字零对应的电压

DC Offset 将整条波形上下平移：`d=0` 对应 `O`，相同 Amplitude 下每个点增加相同电压。它不会改变归一化形状，也不会把脉冲低电平自动固定在 0 V。例如要得到 0～3.3 V 的满幅脉冲，应同时设置 Amplitude=3.3 Vpp、DC Offset=1.65 V。

**DC Offset 不一定等于整条波形的平均电压。** 实际均值为 `mean(V)=O+A/2×mean(d)`。例如实际占空比为 20% 的 0～3.3 V 方波，Offset 为 1.65 V，平均电压却是 `3.3×20%=0.66 V`。非对称脉冲、有偏置的 Noise 和任意 Arb 都需要区分这两个量。

选择 DC 波形时，面板隐藏通用波形的 Amplitude 和 Offset 控件，显示专用的 **DC Offset (Vdc)**；内部仍以独立 `dcLevelV` 保存恒定电平。数字记录只有两个 `0`，理想电压为 `k×dcLevelV`。用户直接设置该直流电平，不需要通过 Amplitude 与通用波形 Offset 拼出一条直线。

#### 1.5.3 Output Load 表示预期的外部负载

当前 C0 假设信号源有固定 **50 Ω 输出阻抗**。Output Load 表示外部接收端或负载的阻值，用于计算负载端电压；HighZ 不会把信号源输出阻抗切换成高阻，50 Ω 也不会在外部自动接入终端电阻。新建默认 50 Ω，面板不再提供负载选择或自定义阻值输入；以下合法值保留在模型和 CSV 契约中，用于准确恢复已有文件，摘要显示实际值。

| 模型／CSV 的负载值 | 含义与使用场景 | 当前换算 |
| --- | --- | --- |
| HighZ | 预期外部输入阻抗远大于 50 Ω，例如示波器的高阻输入 | 按开路处理，k=1；内部 loadOhms=0 是 HighZ 标记，不表示短路 |
| 50 Ω（新建默认） | 预期外部确有 50 Ω 终端，例如示波器的 50 Ω 输入或外接终端电阻 | k=50/(50+50)=0.5，显示电压为开路的一半 |
| 其他合法有限阻值 | 文件声明自定义外部电阻 R；当前允许 50 Ω～100 kΩ | k=R/(R+50 Ω)；1 kΩ 时约为 0.952381 |

一根标称 50 Ω 的同轴线，本身不等于接收端已经设置了 50 Ω 终端。预期负载须与实际接线一致；有限负载中的较大阻值逐渐接近 HighZ 的显示结果。

**相同开路 Amplitude/Offset 在不同预期负载下，显示电压不同。** 例如同一数字记录采用开路 4 Vpp、Offset=1 V。下表比较文件中不同负载值的含义及面板输入换算，实际面板按当前显示精度取舍：

| 设置情形 | 内部开路 Vpp / Offset | 面板 Vpp / Offset | 满幅记录的预期电压范围 |
| --- | --- | --- | --- |
| 文件预期 HighZ | 4 Vpp / 1 V | 4 Vpp / 1 V | −1～3 V |
| 相同开路值，文件预期 50 Ω | 4 Vpp / 1 V | 2 Vpp / 0.5 V | 按 50 Ω 负载计算为 −0.5～1.5 V |
| 相同开路值，文件预期 1 kΩ | 4 Vpp / 1 V | 约 3.809524 Vpp / 0.952381 V | 按 1 kΩ 负载计算约为 −0.952381～2.857143 V |
| 在 50 Ω 下重新输入 4 Vpp、Offset=1 V | 改为 8 Vpp / 2 V | 4 Vpp / 1 V | 按 50 Ω 负载计算为 −1～3 V |

前三行使用相同开路设置，最后一行增加了内部电平要求。面板的 Amplitude/Offset 输入始终按当前有效负载换算；恢复文件中的负载值本身不会改变实际接线或外部电阻。

#### 1.5.4 电平与负载怎样保存在 CSV 中

[AwgCsvExporter](../../../vsg2.0/src/plugins/awg/awgcsvexporter.cpp) 当前保存紧凑 CSV 1.0：素材表为 `asset_id,sample_index,digital_normalized`，分段表按素材 ID、局部范围和重复次数引用，不写重复时间列。归一化值 `d[n]` 的单位不是 V；Amplitude、DC Offset 和 Output Load 写入 `#` 元数据，不乘进素材数据。相同形状与起点下，仅改电平/负载不会改变归一化形状。

| CSV 字段 | 保存的内容 |
| --- | --- |
| open_circuit_amplitude_vpp | 最终记录的开路 Vpp；DC 转 Arb 交换记录使用 20 Vpp |
| open_circuit_offset_v | 最终记录的开路偏移；DC 转 Arb 交换记录为 0 |
| expected_load_ohms | 预期外部负载；0 表示 HighZ |
| display_amplitude_vpp | 所选负载下的面板 Vpp |
| display_offset_v | 所选负载下的记录偏移；DC 常值可以由归一化样点承载，不应只看该字段判断常值电压 |
| sample_rate_sa_per_s、interpolation | 样点率与点间连接方式 |

例如一周期 4 点的 1 kHz 方波，占空比 50%、相位 0°，开路 4 Vpp、开路 Offset=1 V、预期负载 50 Ω，相关字段摘录如下；完整导出还包含格式版本、长度等其他元数据：

```csv
# open_circuit_amplitude_vpp=4
# open_circuit_offset_v=1
# expected_load_ohms=50
# display_amplitude_vpp=2
# display_offset_v=0.5
# sample_rate_sa_per_s=4000
# section=assets
asset_id,sample_index,digital_normalized
high,0,1
low,0,-1
# section=segments
segment_id,asset_id,begin_index,end_index_exclusive,repeat
s1,high,0,1,2
s2,low,0,1,2
```

这些 `+1/−1` 对应所选 50 Ω 负载下的 **1.5 V / −0.5 V**。只看数字列会看到相同满幅方波，要得到电压必须同时使用电平设置。该记录长 1 ms，展开 4 点，素材表只存 2 点；repeat=2 在这里各表示两个高/低样点，仍是一个方波周期。不在末尾增加 t=1 ms 的下一轮首点。

[导入器](../../../vsg2.0/src/plugins/awg/awgfileimporter.cpp) 通过普通 Load File 读取当前 SGStudio 记录，为正确预览恢复采样率、电平、负载与插值；紧凑格式按分段展开，Arb 起点设为 0。Save CSV 只保存一份素材/分段 CSV，不保存图上选区或额外生成说明。通用规则见唯一的 [CSV 数据契约 1.0 Markdown](../../../vsg2.0/package-info/support/SGStudio_AWG_CSV_Contract_1.0.md)，format_version=1；用户在消息中指定时间或展开后的全局点索引。无 SGStudio 元数据的普通 CSV 时间列仍只校验、不自动设置播放速度。

内部 DC 生成仍是两个数字零和独立 dcLevelV。新的 AI 交换导出把它转换为合法 Arb 映射：开路 20 Vpp、偏移 0、`d=dcLevelV/10`，负载不变，使常值电压进入实际样点，便于局部编辑；会有 float32 表示精度。只含两个零而没有这些元数据的普通 CSV 仍不能自行确定原直流电平。

基础周期波形（Sine/Square/Pulse/Ramp）导出恰好一周期；Arb 导出当前完整有限记录，可包含多次出现和多个形状。两者统一写 waveform=Arbitrary、素材表与分段表，source_waveform 仅标记来源。repeat 重复的是所引用范围，只有范围恰为一周期时才等于周期数；方波平台可用单点素材重复表示。加载一律读取真实样点为 Arb，不按来源名称重建。细节与完整方波示例见 [CSV 契约的来源说明](../../../vsg2.0/package-info/support/SGStudio_AWG_CSV_Contract_1.0.md#13-基础波形与-arb统一格式不同记录内容)。

#### 1.5.5 最终播放电压取决于完整设置和实际负载

符合 C0 契约的设备需要同时接收点列、采样率、插值和电平设置，并实际施加对应输出电平。只上传归一化数组，无法确定目标 Vpp 或 DC 电压。当前这些参数没有改变点数、采样率或循环次数；Amplitude/Offset 决定电压映射，SRate 决定每个点持续多久。

在当前 50 Ω 信号源模型下，实际外部负载为 `R_actual` 时：

```text
V_actual,ideal[n] = R_actual/(R_actual+50 Ω)
                   × (开路 Offset + 开路 Vpp/2 × d[n])
```

实际负载与当前 Output Load 一致，面板电压才与该理想目标一致。例如预期负载为 50 Ω 时输入 4 Vpp、Offset=1 V，需要开路 8 Vpp、2 V 偏移；若外部实际为高阻输入，模型得到的电压会接近 −2～6 V，即 8 Vpp，而不是面板的 −1～3 V。实际硬件的输出阻抗、量程、校准、带宽和 DAC 量化决定最终测量值，不能由当前离线预览代替设备确认。

当前 [规划器](../../../vsg2.0/src/plugins/awg/awgplaybackplanner.cpp) 采用的离线电平范围是：开路 Vpp 为 0.001～20 V，且 `|开路 Offset|+开路 Vpp/2≤10 V`。因此 50 Ω 下的最大 Vpp 在零偏移时为 10 Vpp，增大偏移后允许的幅度会减小；DC 的开路电平为 −10～+10 V，对应 50 Ω 下 −5～+5 V。这些是现有 C0 模型约束，尚非已确认的目标硬件输出规格。

## 2. Arb 之外的六类波形怎样得到记录

Sine、Square、Pulse、Ramp 让用户输入信号频率 `f` 和每周期点数 `N`。软件生成恰好一周期，即 `L=N`，并令 `SRate=f·N`。这样在合法范围内，改 `f` 可保持相同点数和形状网格，更新输出样点率；超出范围则报错，不偷偷改点数或近似频率。每点相位 `u_n=fract(n/N+φ/360°)`，其中 `n=0…N-1`。以下是 [当前生成器](../../../vsg2.0/src/plugins/awg/awgwaveformgenerator.cpp)和 [C0 方案](../TaskLog/2026-09-29_refactor-awg-truearb-c0.md)的规则：

| 波形 | 样点算法 | 为什么这样做，以及离散限制 |
| --- | --- | --- |
| Sine | `d[n]=sin(2πu_n)` | 直接求每个输出点，完整记录就是一周期；无额外谐波合成。当前规划器至少要求 4 点/周期，以免极少点退化。 |
| Square | `u_n<duty` 时 `+1`，否则 `-1` | Duty 是阈值分段而非预先合成的谐波谱。高、低段都须能落到样点；否则拒绝。实际高点数为整数，Duty 会量化，不能把请求百分比当成无损输出。 |
| Pulse | 一个周期内分为低平台、上升沿、高平台、下降沿；上升 50% 交点在周期起点，下降 50% 交点在 `pulseWidthSeconds·f` 处 | 手册把脉宽定义为两处 50% 交点的间隔，把边沿定义为 10%–90%（本地手册 PDF 页 39–42）。线性模型的完整上/下沿宽分别为所设边沿时间除以 `0.8`；0 表示生成模型中的样点边界阶跃，新建记录的线性播放连接仍有样点间过渡。规划器检查平台、沿宽和点距，无法表征时拒绝或提示量化。 |
| Ramp / Triangle | `u_n<symmetry` 为从 `-1` 到 `+1` 的直线；余下区间从 `+1` 回到 `-1`。0%、100% 是单向斜坡 | `symmetry` 直接决定上升段占周期的比例；50% 就是三角波。拐点和两段时长必须可由所选 `N` 表示。无需旧 B0 的 Ramp 专属谐波截止。 |
| Noise | 生成 `L` 个高斯随机数；可选在 FFT 频域保留带宽内正负共轭频点，IFFT 后调整数字均值/标准差，并对峰值裁剪 | 当前 C0 的有限记录由用户指定 `SRate` 和 `L`，没有“单一信号频率”；这不是厂商基础 Noise 菜单的通用输入要求。频点间隔是 `Δf=SRate/L`，截止须低于 `SRate/2`；当前规划器要求至少 4 个可表示的正频点。种子用于同一实现下复现；裁剪次数和实际统计由生成结果报告。有限记录循环后会出现周期性，不能等同仪器的连续随机噪声。 |
| DC | 两个相同的归一化 `0` 样点，物理电平由单独 `dcLevelV` 设置 | DC 没有信号频率，也不需要把 `f=0` 送进周期规划。两点仅满足 C0 最短记录；导出 CSV 的 `SRate/2` 是记录重复率，不是 DC 信号频率。未来若只上传数字零而不施加电平设置，不会得到目标直流电压。 |

Noise 的 FFT 仅服务于**带限噪声**，不应重新成为全部形状的公共合成路径。当前实现使用 `std::mt19937` 和 C++ 标准库正态分布；种子可在同一实现内重现生成过程，不保证不同标准库逐样点一致。Pulse 默认 1 kHz、200 μs 对应 20% Duty，不能沿用旧实现的 50% Pulse 默认。上述算法按数字记录解释，模拟端的实际频谱、边沿、幅度仍需设备测量。

电平与形状分离也是这个模型的重要原因。数字 `d[n]∈[-1,1]` 是记录的形状，不含 Vpp；理想显示电压为 `V[n]=V_offset,display+(V_pp,display/2)·d[n]`。按手册固定 50 Ω 输出阻抗的显示模型，HighZ 系数为 1、负载 `R` 的系数为 `R/(R+50Ω)`；预期负载影响显示换算，不改记录（本地手册 PDF 页 135–136）。当前代码将开路 Vpp/Vdc、负载及 DC 电平放在 [AwgProfile](../../../vsg2.0/src/plugins/awg/awgprofile.h) 中。参数含义、输入换算、CSV 保存及播放电压详见第 1.5 节。这只是 C0 理想模型，真实输出量程及校准尚待硬件确认。

### 2.1 Pulse 参数输入约束与自动修正（已实现，静态核对）

2026-10-19 用户确认的现场条件是：Pulse占空比显示100%时提示记录无法表示；用户明确允许自动修正为合法值并显示调整结果。现有面板通过`AwgPlaybackPlanner::pulseEditRange`展示动态范围，以`adjustPulse`显式修正候选计时参数；生成成功后才一起提交参数、记录与revision。失败时保留原有效设置及预览，并显示本次未应用。当前仅静态核对，尚未构建或运行验收。

厂商已有可核实的联动政策：[RIGOL DG5000 Pro Programming Guide](https://www.rigol.com/dam/global/downloads/brochures/en/program-guide/waveform-generators/DG5000Pro_ProgrammingGuide_EN.pdf?t=1761022282157)，PDF187/印刷173页明确修改Pulse周期时自动调整Width与Edge；没有公布具体调整次序。[SIGLENT SDG6000X E01A](https://siglentna.com/wp-content/uploads/dlm_uploads/2017/12/SDG6000X_UserManual_UM0206X-E01A.pdf)，PDF40-41/印刷30-31页规定非法Width/Rise输入取最近合法值。不能把这些政策泛化为所有型号的统一算法，也不能照搬参考机型固定纳秒指标。

按当前C0规划器，设`dt=1/(fN)`、`T=1/f`、`E=(tr+tf)/1.6`，其中tr/tf为10%-90%时间。保留当前沿时合法Width满足`dt+E<=W<=T-dt-E`，并与能力表范围取交；Duty由`W*f`派生。非零沿还须至少为`0.8dt`。若允许编辑Width时同步缩沿，Width输入可采用零沿可行域`[dt,min(WidthMax,T-dt)]`，提交时再拟合边沿；不能只显示旧沿下的窄范围而阻止本可通过联动实现的输入。

当前调整顺序为：频率/点数改变时保留能表达的旧沿、必要时按比例缩沿（缩后不足最小非零沿则归0），再夹Width；Width/Duty改变时优先保留合法的新Width并按需缩沿；直接改一个沿时保留Width及另一沿，仅调整当前沿。切换到Pulse也处理隐藏旧参数。Pulse键盘不提前夹值，以保留原请求供调整说明使用；文本提交与步进共用一条编辑链，最终数值同步回写键盘，范围及调整说明展示在现有状态区和tooltip。Pulse Duty和时间均匹配键盘的9位单位内小数并去掉尾零，足以区分当前N上限下的合法最大Duty与100%。生成器/导入仍严格校验，不执行UI校正政策；几何边界在owner共享`16*epsilon*T`舍入容差处理浮点端点。非单点零沿Width编辑域还向内留出同一浮点余量，防止W*f阈值舍入抹掉唯一高/低样点；N=2等单点域保留中点。未引入固定占空比余量或重试。完整调整优先级、低采样率/WidthMax空域和N=2/3边界见[Pulse UI约束设计及实施TaskLog](../TaskLog/2026-10-19_awg-pulse-ui-constraints-design.md)。

AWG全部可编辑数值显示通过局部`numericText`复用与键盘相同的`UnitAdapterFactory`和精度。所有波形的Frequency为Frequency/9位，SRate为SampleRate/9位，Noise Bandwidth为Frequency/3位；Pulse Width/Rise/Fall及Record Duration为Time/9位；其余值由Base adapter按键盘的0/1/2/3/9位格式化，面板只添加Vpp/Vdc/%/°等含义后缀。SampleRate直接提供Sa/s及k/M/G前缀，面板与软键盘同类型，不再把Hz后缀替换为Sa/s；数字选区、单位快捷键、裸Enter及步进均能识别完整采样率单位，频率控件仍使用Hz单位族。周期波形SRate=f*N继续只读，Arb/Noise可编辑，DC隐藏SRate及记录时长并使用固定内部记录时间基准。面板不再使用独立的normalizeFrequency/normalizeTimeSpan，避免低SRate显示0、频率丢小数及时间单位边界分歧。复核清单见[AWG参数格式一致性TaskLog](../TaskLog/2026-10-19_fix-awg-parameter-display-consistency.md)及[SRate软键盘TaskLog](../TaskLog/2026-10-19_awg-srate-keyboard.md)。

### 2.2 基础 DC / Noise 的产品参数与 C0 记录参数（DC已实施，静态核对；Noise保留现状）

2026-10-19 核对本地 `SIGLENT_SDG6000X_UserManual_E01A.pdf` 的 PDF43/46页和 `RIGOL_DG5000Pro_UserGuide_EN.pdf` 的 PDF61–62页：**基础 DC、Noise 均没有用户可配置的 SRate 或记录点数。** SIGLENT 基础 Noise 设置 Stdev、Mean、Bandwidth，基础 DC 设置 DC Offset；上述 RIGOL 基础 Noise 设置幅度、偏置，DC 设置偏置。SRate 的用户编辑入口属于 Arb / TrueArb 或波形序列，不能用标称 DAC 采样率代替其范围。手册未说明厂商原生 Noise 的完整内部生成架构，不能仅凭菜单推断时钟、记录长度或随机源实现。

当前 DC 使用独立 `dcLevelV` 和两个数字零，预览电压为 `loadFactor()*dcLevelV`。2026-10-19 按用户确认方案完成最小参数收敛：专用控件显示为 **DC Offset (Vdc)**，软键盘标题沿用该标签；DC隐藏SRate和Record Duration标签/值。DC计划使用能力表默认SRate（当前16.384 MSa/s）作为明确的C0内部记录时间基准，不读取也不覆盖Arb / Noise的共享SRate；切回其他波形恢复原有可见性/编辑规则。两点与现有电平/负载映射保留，无需重写数值波形。CSV仍是通用Arb记录，从最终计划获取SRate并保留必需的时间元数据；沿用 `convertDcToArbitrary()` 的满量程常值编码。当前仅静态核对，未编译或运行UI；实现及检查见 [DC参数收敛TaskLog](../TaskLog/2026-10-19_awg-dc-offset-parameters.md)。

Noise 的结论须按当前产品边界区分：**现阶段 C0 有限噪声记录推荐保留 SRate 和 Noise Record Points 可编辑，其他已有属性及含义保持不变。** F决定点距与时间尺度，L决定循环记录长度，二者共同决定 `T=L/F`、`Δf=F/L`。带限分支要求 `B<F/2` 且 `4<=floor(BL/F)<=floor((L-1)/2)`。例如 F=16.384 MSa/s、B=1 kHz 时，L=16384只有1个正频点、不合法；L=65536得到4个正频点和4 ms记录、合法。即使关闭带宽限制，也不能删掉记录的时间刻度和长度。

基础 Noise 界面可以隐藏这两项，但前提是另行明确内部宽带时钟、窄带降速、目标重复时长/频谱网格和内存边界；带宽本身不能唯一决定F与L。直接固定300 MSa/s与最大20 Mpts仍无法表示当前可通过降低F获得的1 Hz带限记录。本轮不引入任意倍率的自动选值、另一套Noise模式或持续生成路径。此前直接建议Noise隐藏SRate只反映厂商菜单，不能作为当前C0算法的完整修改方案。

后续若补齐Noise编辑联动，组合范围与校正规则应放在规划器，由面板候选生成成功后提交并显示调整，不复制一套UI公式。已识别的相关边界包括奇数L=9的4个正频点（当前面板上限公式较规划器保守）、窄带大记录和非2幂FFT内存。完整研究、DC改动清单、Noise方案比较及静态验收算例见 [DC / Noise 参数与算法调研 TaskLog](../TaskLog/2026-10-19_awg-dc-noise-parameter-research.md)。**DC参数收敛已经实施；Noise自动规划/编辑联动仍为研究建议，本轮未改Noise算法或参数。**

## 3. Arb：文件样点就是播放内容

与参数波形不同，Arb 的形状由样点序列定义。用户选择文件后，[导入器](../../../vsg2.0/src/plugins/awg/awgfileimporter.cpp)先把它变成归一化 `[-1,1]` 点列，生成入口以有效点数作为 `L`。Arb 独立输入 `SRate`；改变 `SRate` 只改变每点持续时间和 `f_repeat`，不改点值、顺序或长度。当前生成器仅允许按 `phaseDegrees` 选整点循环起点：`shift=round(φL/360°)`，`out[n]=in[(n+shift) mod L]`，并报告实际起点角。此“相位”是 **SGStudio 的离线起点规则**，手册未规定真实设备的 Arb 相位如何作用于记录。

| 输入来源 | 当前实际处理 | 不能据此推断的行为 |
| --- | --- | --- |
| 单声道 16-bit PCM WAV | 解码整数 PCM 成 `[-1,1]` 浮点点列；拒绝其他声道数、编码或位数 | WAV 头里的源采样率**没有**用来设 C0 `SRate`。 |
| CSV/TXT 单列 | 非空数值行按顺序成为样点；跳过注释和首个数据前的一行表头；越出 `[-1,1]` 拒绝 | 不把列值当 V、mV 或 DAC 码自动换算。 |
| CSV/TXT 两列 | 第一列时间、第二列归一化值；检查时间严格递增且间隔近似一致 | 不据普通时间列 `Δt` 猜测播放 SRate。记录时长是 `L/SRate`，不是 `t_last-t_first`。 |
| 紧凑 CSV 1.0 | 原始素材样点＋有序分段表；先截取范围再重复，校验后展开为点列，保留结构用于再次保存 | 不把存储素材点数当作记录点数；repeat 不是硬件 Burst 或停止指令。 |
| 厂商内置或保存的任意波 | 如果已经转换为上述可接受格式，才可走同一导入入口 | SIGLENT 的内部 `.bin`、RIGOL 的 `.arb` 并非当前导入格式；“内置波形文件”并不自动表示可互通。 |

当前文件 Arb 没有自动重采样、滤波、谐波裁剪、补点或峰值归一化。原因是这些操作会改变用户交付的点列或其时间语义。例如 `L=1000`、`SRate=1 MSa/s` 给出 `T_record=1 ms`、`f_repeat=1 kHz`；若任意补到 1024 点而保持 SRate，重复率会变成 976.5625 Hz。修复文件首尾不连续、抑制混叠或改变点数应是用户显式选择的编辑操作，并展示新的 `L`、`T_record`、`f_repeat`。

拟议编辑范围是可编辑模板／快速波形库入口与 A–E 五类编辑任务，具体重采样条件见[需求设计 §2.1](arb_editor_requirements_and_segment_table_design.md#21-当前范围内何时需要重采样)。完整参数模板可在公共网格直接生成；同网格局部替换、裁剪和电平变换保留原样点；只有已有 Data 需要进入另一采样网格并保留指定时间尺度时才涉及重采样。内置库、分段编辑及多相 FIR 均未在当前面板实现。

现阶段的 [面板](../../../vsg2.0/src/plugins/awg/awgpanel.cpp) 提供普通文件加载、点数、SRate、起点、电平、预览和单 CSV 保存；负载与插值在摘要只读显示，没有对应编辑控件，也没有内置波形目录或点列编辑器。面板拥有已校验的点列及素材/分段结构，生成器使用该数据，不在刷新时重读文件。普通加载按完整 SGStudio 元数据预览记录，无元数据 CSV/WAV 仍使用面板设置。ARB 总览、主图与选区使用同一电平映射显示 V；图上选区与蓝色查看范围均不写入 CSV，用户自行在 AI 消息中描述时间或展开后的全局点索引。AI 处理和回传格式统一见 [CSV 数据契约 1.0](arb_ai_exchange_template_and_preview_design.md)。当前加载不恢复编辑选区、不做专门 AI 结果比较；数据契约的加载有效性不等于 LF 硬件输出已验证。

## 4. EasyWave 的主要编辑业务：从来源到一段可播放记录

用户所说 Arb 常从内置波形或文件开始，可作为产品使用场景；当前没有 SGStudio 使用频率统计可量化其占比。SIGLENT E01A 手册确实区分内置波形和用户波形，并说明可从外部 `.csv`/`.dat` 导入并转为内部 `.bin`，或由 PC 版 EasyWave 编辑后下载到仪器内部存储（本地手册 PDF 页 47、110）。由此可见，**来源选择、记录编辑、保存/传输、播放设置**是连续但不同的业务步骤。

SIGLENT 官方的旧版 EasyWave 资料能直接证实两条有代表性的制作路线：

1. **坐标/关键点绘制**：新建波形时先设名称和内存记录的周期/重复频率；在 Coordinate Draw 表中指定样点编号、X 时间、Y 幅值，用连线构成脉冲串等形状。该 [SIGLENT 坐标绘制实例](https://siglentna.com/operating-tip/coordinate-mode-with-easywave/)使用固定 16,384 点的 SDG1000X，示例里的固定深度不能搬到 C0；能借鉴的是“用少量精确关键点产生点列”的编辑业务。实例还示范检查预览、通过 USBTMC 选设备和通道下载，仪器内部保存为 `.bin`。
2. **方程绘制**：新建记录并给定整段重复率，然后输入数学表达式生成点列；[SIGLENT 双音实例](https://www.siglenteu.com/application-note/easywave-two-tone-arbitrary-waveform/)用 100 Hz 记录重复率构造 700 Hz 与 1900 Hz 两个分量，之后预览、选择 USB 设备/通道、发送并在仪器上调播放参数。这说明编辑器的“波形频率”应与生成点列内的实际谱成分分开理解。

SIGLENT 后续 [SDG6000X/SDG6000X-E 手册 EN02C](https://int.siglent.com/u_file/download/25_09_22/SDG6000X_UserManual_EN02C.pdf)描述 **EasyWaveX** 还可用手绘、线段、坐标、方程和波形属性中的逐点表格创建记录；其中数字时钟示例用 30 点逐点赋值，并在预览中选择 0 阶保持。这是**后续软件/手册版本**的证据，不应反推 E01A 随附的旧版 EasyWave 每项操作都相同。本地 `EasyWaveX_1.1.0.25_EN.zip` 没有完整帮助文档，本文不以它推断更多工具或算法。

这些资料对应的核心业务并非某个单一绘图算法，而是：选来源或新建 → 以手绘、关键点、方程或逐点表把意图变成有限样点 → 检查长度、幅值及重复接缝 → 预览 → 保存记录或送到仪器 → 设置该记录的输出样点率、电平和插值。对 C0 而言，编辑器的产物应是明确的 `d[0…L-1]`；绘图方法只是生成或修改它的输入方式。**把“插值用于编辑器生成样点”和“记录采用的点间播放插值”分别命名**，否则用户无法判断是否真的改变了 `L` 个数字点。

## 5. RIGOL DG5000 Pro 的 Arb Build 对照

[RIGOL 官方 DG5000 Pro User Guide](https://download.rigol.com/en/Manual/Waveform%20Generator/DG5000%20Pro/DG5000Pro_UserGuide_EN.pdf) 第 9、10 章给出两个层面。Advanced Arb 允许按 `Sample Rate / Waveform Points` 计算整段输出重复频率；内置波形固定 16,384 点，外部文件依 `.arb`、`.txt`、`.csv` 有不同长度上限。仪器上的 **Arb Build** 提供新建基础波形/设长度与循环数、两游标圈选区间、剪切/复制/粘贴、水平/垂直翻转、一次撤销/重做、保存 `.arb`。手册也建议用 PC 版 Ultra Station 处理更复杂的编辑和传输。以上在本地手册 `C:\Users\jsl\Desktop\SignalDocs\RIGOL_DG5000Pro_UserGuide_EN.pdf` 的 PDF 页 102–103、125–127 可复核。

| 业务问题 | SIGLENT EasyWave / EasyWaveX 资料展示 | RIGOL DG5000 Pro Arb Build 展示 | C0 应借鉴的原则 |
| --- | --- | --- | --- |
| 从零生成 | 坐标、方程；EasyWaveX 后续资料还列出手绘、线段、逐点表 | 选基础波形并设置长度/循环数 | 内置模板和新建生成都应落到同一份点列。 |
| 局部修改 | 官方实例重点展示精确点位、表达式和整体预览；不据此推断完整选区操作清单 | 双游标选区；剪切、复制、粘贴、翻转 | 最小编辑器可以先支持可观察、可撤销的局部点列变换。 |
| 交付 | PC 软件预览、选设备/通道并下载；仪器支持存储文件 | 仪器本机保存 `.arb`；复杂制作交给 PC 软件 | “编辑保存成功”与“设备开始输出”应分开报告。 |
| 与 TrueArb 时间的关系 | 旧例中有固定深度，不能直接当 C0 长度规则；整段重复率的语义一致 | Advanced Arb 明确 `f_repeat=SRate/L` | 改点数或 SRate 后，必须重新展示时长及重复率。 |

“Arb Build 比 EasyWave 简单”在这里指**上述手册展示的仪器内编辑能力范围**：它偏向基础波形和选区变换，而 EasyWave 的官方案例还包括坐标、方程生成。不能仅凭公开实例断言任一软件没有其他功能，也不能把 RIGOL 的存储长度或滤波模式移植到 SGStudio C0。

## 6. 如果扩展 SGStudio 的 Arb 编辑，哪些变化需要显式呈现

这部分是**基于现有 C0 契约的产品建议，尚未实现**。用户打开内置模板或文件后，工作对象应是一份待编辑的点列；原来源只说明它从哪里来。编辑器提交时统一调用现有 C0 长度/数值校验，并显示编辑后的 `L`、`SRate`、`T_record`、`f_repeat`。改动规则可以保持简单：

| 操作 | 点列与时间结果 | 需要向用户说明 |
| --- | --- | --- |
| 改 `SRate`、输出 Vpp/偏移；从文件恢复负载、播放插值 | 不改 `d[n]`、`L`；前者改变 `T_record` 与 `f_repeat`，电平仅影响理想输出刻度 | 这是播放设置，不是波形重采样；当前 UI 不提供负载／插值编辑。 |
| 改起点、水平翻转、垂直翻转、按索引覆盖样点 | 通常保持 `L`；起点/水平翻转改变时间顺序，垂直翻转改变数值 | 更新首尾接缝、预览和点列统计。垂直翻转应定义为 `d→-d`，不能暗改物理 DC 偏移。 |
| 裁切、插入、删除、粘贴不同长度片段、复制循环 | 改变 `L`；若保持 `SRate`，整段时长与重复率随之改变 | 操作前后都给出 `L` 和 `f_repeat`；越过 2–20 Mpts 时拒绝。 |
| 坐标/方程生成或手绘 | 先选明确的样点索引区间及写入方式，计算新点值；可能保长，也可能显式新建记录 | 编辑器需固定表达式自变量、坐标映射和端点约定，避免首尾重复点或隐含重采样。 |
| 归一化、平滑、滤波、重采样 | 明确改变数值，部分操作也改 `L` | 只有用户显式选择时执行；展示幅值缩放、点数和可能的混叠/带宽变化。 |

若编辑 `L` 后用户要求**保持整段重复率**，则必须明确把 `SRate` 更新为 `f_repeat·L`，并重新校验 C0 采样率范围；若保持 `SRate`，则接受重复率随长度变化。20 Mpts 的单份 `float32` 点列约占 80 MB，剪贴板、撤销和多次全量复制会继续增加内存占用；未来编辑器需要按真实工作负载决定数据与撤销策略，不能把当前的离线预览等同于可流畅编辑 20 Mpts。

第一步产品边界可从“导入文件/内置模板 → 预览整段和接缝 → 以样点索引选区并做简单变换 → 导出可再导入的点列”开始。方程、关键点绘制、跨设备 `.bin/.arb` 兼容和真实下载各自需要独立的输入格式与接口定义。当前 C0 删除了 AWG Profile 持久化，故现有 CSV 只是**数字记录及分析元数据**，不是可继续编辑的工程文件；若未来需要保存编辑历史或配方，应先定义独立业务需求，不把注释元数据误认为已实现的恢复功能。

## 7. 当前可确认的边界与待核实处

- **已观察**：CMake 纳入当前 AWG 规划器、生成器、导入器、面板、预览和 CSV 导出；旧形状合成与 Profile 持久化源文件已从当前目标移除。算法分析以静态阅读为依据；紧凑 CSV 的契约与本轮验证范围见 [CSV 数据契约 1.0](arb_ai_exchange_template_and_preview_design.md#3-验证状态)。
- **设计假设**：C0 的 `SRate`/`L` 范围、点间插值和 50 Ω 显示换算参照 SDG6000X；真实 SGStudio LF 设备能否上传、支持哪些长度/样点率组合、时钟粒度与回读、DAC 编码、电压约束、循环接缝和硬件相位尚需接口确认。第 1.2–1.3 节是接入条件和验收建议，不是已测结果。
- **当前限制**：Arb 只支持 WAV/CSV/TXT 受限格式；未内置波形目录、未实现 EasyWave 式编辑。完整 SGStudio CSV 已恢复记录参数，WAV 头和无元数据 CSV 仍不自动设置播放速度。当前交换按 [CSV 数据契约 1.0](arb_ai_exchange_template_and_preview_design.md) 只保存素材/分段 CSV，通用说明由一份独立 Markdown 提供；不保存编辑范围或创建导出快照。当前契约 1.0 的代码/文档/桌面附件已静态核对，本轮未编译或运行；大文件性能尚未运行评估。
- **静态细节**：当前生成器已在含相位偏移的离散点上统计 Square 实际高点数，并据此报告量化后的 Duty。当前预览已将 `L-1→0` 的循环接缝接到画布右边；编辑器还需支持将接缝放大到单样点，便于检查跳变。

## 资料入口

- 本地基准手册：`C:\Users\jsl\Desktop\SignalDocs\SIGLENT_SDG6000X_UserManual_E01A.pdf`，PDF 页 21、39–42、47–49、110、135–136。
- 在线可核实的参考机型资料：[SDG6000X E01A 用户手册](https://siglentna.com/wp-content/uploads/dlm_uploads/2017/12/SDG6000X_UserManual_UM0206X-E01A.pdf)、[SDG6000X 产品说明](https://siglentna.com/waveform-generators/sdg6000x-series-pulsearbitrary-waveform-generator/)、[SIGLENT 采样率/插值编程指南](https://int.siglent.com/upload_file/user/SDG6000X/SDG_ProgrammingGuide_PG_E03B.pdf)、[16-bit Arb 下发示例](https://siglentna.com/application-note/16-bit-step-arb-sdgx/)。这些文献不能证明 SGStudio 目标硬件规格。
- 仓库方案：[2026-09-29 C0 重构 TaskLog](../TaskLog/2026-09-29_refactor-awg-truearb-c0.md)；旧 B0 实现背景可查 [过时文档](awg_model_b0_lf_chain_and_waveform_processing.md)。
- SIGLENT 官方：[EasyWave 坐标绘制](https://siglentna.com/operating-tip/coordinate-mode-with-easywave/)、[EasyWave 方程双音实例](https://www.siglenteu.com/application-note/easywave-two-tone-arbitrary-waveform/)、[SDG6000X/SDG6000X-E EN02C 的 EasyWaveX 章节](https://int.siglent.com/u_file/download/25_09_22/SDG6000X_UserManual_EN02C.pdf)。
- RIGOL 官方：[DG5000 Pro User Guide，第 9–10 章](https://download.rigol.com/en/Manual/Waveform%20Generator/DG5000%20Pro/DG5000Pro_UserGuide_EN.pdf)。
