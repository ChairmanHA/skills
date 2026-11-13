# RMS/PEP 配置与大波形统计优化方案

日期：2026-10-22 初稿；2026-10-23 修订。状态：工作线 B（RMS 功率功能）已按本修订完成静态实现，尚未编译/运行验收；工作线 A 统计优化待实施。

2026-10-26 增量：功率意图持久化、旧配置/Reset、公开 RMS SCPI 及 RF OFF 参考联动已静态实施，详见 [Level/RMS 异步交互与保存设计第 12～13 节](rms_level_async_interaction_design.md#12-已确认调整rf-off-只关闭输出功率联动按-rf-on-解析)。下文 2026-10-23 的实施记录保留当时范围；当前合同以本增量和关联文档为准。

2026-10-26 对称显示增量：明确输入数值保留，未知派生字段显示 --；匹配参考已解析可先预览，成功才建立 RMS/改变保存参考。Startup 镜像仅限未编辑状态；无效/全零统计不能抹掉明确 RMS 设置目标。详见 [显示与实施审核](../TaskLog/2026-10-26_rms-level-symmetric-display-audit.md)，静态实施待人工验收。

2026-10-23 修订要点：

- 依据当前代码重新确定统计优化优先级：先测量、先削减内存遍数，再做 SIMD。
- RMS 反推 Level 改为在 Core 组装请求时 O(1) 解析，复用已随请求传递的波形指标，不新增指标缓存，不拆分执行阶段。
- 默认保持 RMS，启动未应用 baseband 时 RMS=Level；用户主动确认 Level 只应用一次，成功后建立新的 RMS 目标。
- 各 pipeline 的编辑策略：LScan/MScan 的全局 Level/RMS 入口置灰；Streaming 仅 RMS 置灰；FScan 按最终 baseband 指标反推统一 Level。

## 1. 目标与范围

针对最大约 1 GB 的 int16 交织 IQ 波形，在不改变数值契约的前提下降低精确功率统计耗时；并允许用户用 RMS 作为功率目标，由 Core 反推 API Level 下发。

分两条可独立交付的工作线：

- **A 统计计算**：遍数削减与 x86 SSE2/AVX2、AArch64 NEON、通用标量三条内核路径。
- **B 功率意图**：RMS/Level 双编辑、按 pipeline 解析或延迟生效、显示与失败处理。

保留当前 C++17、设备 API、IQ 量化与功率参考契约。不引入 DSP 库、GPU、OpenMP 或新的任务调度系统。Level 仍表示 API 满幅参考，现有 SCPI Level 输入仍为显式 Level；2026-10-26 增量保存 RMS/Level 意图。后续波形变化默认保持 RMS。

## 2. 实施前代码核对（2026-10-23 静态分析）

以下为代码观察，文件均已由对应 CMake 纳入。

| 位置 | 观察 |
| --- | --- |
| core/iplaybackbusiness.cpp `calculateInterleavedIqPowerMetrics` | 标量单遍融合 uint64 sum 与 uint32 peak；分量钳位 -32767。 |
| 同文件 `scaleInterleavedIqAndCalculatePowerMetrics` | AutoScale 时一遍原始峰值（不钳位），再一遍 double 缩放、限幅、截断量化并统计；scale==1 时只统计一遍。 |
| 各 provider（ARB、Multitone、AWGN、模拟调制等） | 只扫描源周期，以 period 作分母，之后才复制短波形尾部；短波形复制不重复统计。 |
| htra/arbdatagenerator.cpp payload 构建 | `readPlaybackWavIqData` 先把整个切片读入 payload，再调用缩放统计；IQS-WAV 强制 AutoScale。 |
| libs/utils/playbackwavreader.cpp、iqswavreader.cpp | 普通 WAV 按 16 MiB 块、IQS 按 packet 读入目标缓冲；全部读完后再整段执行一次小端转换循环。 |
| core/txsessionservice.cpp `buildRequest` | `common.level` 取 Level 属性；Playback provider 在 `buildPlaybackExecutionContext` 中同时填 payload 描述符与 `powerMetrics`。 |
| 同文件 `rmsPowerDisplayForRequest` | 仅 FixedCw、FScan CW（RMS=Level）与 FixedPlayback、FScan Playback（RMS=Level+r）有效；LScan、MScan、Streaming、Mute 显示无效。 |
| core/txpipelineruntime.cpp `requestApply` | 用整个 `TxApplyRequest` 判断重复请求和“已匹配硬件”，`common.level` 参与比较；已有 desired / in-flight / pending-latest。 |
| core/txpipelineexecutor.cpp `applySweepCw` / `applySweepPlayback` | 先用 `profile.level` 做 base configuration，再由 `setLevelSweep`、`setListSweep` 或扫描启动使用扫描计划的电平。 |
| core/mainwindow.cpp | `deviceConfigurationDone` 回写经 `CommonDeviceProfile::setProfile` 写回 Level 属性。 |
| Streaming | legacy bridge；`setBridgedApplyRequest` 收到的请求 level 来自 Level 属性；没有有限 payload 指标。 |

由此得出的推论：

1. **统计本身已是单遍融合**，但 1 GiB ARB/IQS 文件在统计前还有“读入 + 整段小端转换”，AutoScale 时再加一遍原始峰值。真正可削减的是多余的整段内存遍历。
2. **指标与 payload 身份已在同一请求中**。RMS → Level 只需在 `buildRequest` 中做一次减法，天然满足“指标与波形一致”，不需要另建 M(W) 缓存。
3. **LScan/MScan 下修改 Level 不改变 RF 输出**（扫描电平来自计划），但因 `common.level` 参与请求比较，会触发整套 base 配置并重启扫描，是一次无效果的中断；回写还会把 base level 写回 Level 属性。
4. 当前源代码为标量循环；是否已被编译器部分向量化需查看优化报告/汇编，不能仅凭写法断言。

## 3. 统一数值契约

```text
F = 2 × 32767²
q[n] = max(I[n], -32767)² + max(Q[n], -32767)²
S = sum(q[n])                  // uint64
K = max(q[n])                  // uint32
D = 当前播放语义确定的平均功率分母（含周期补零）
r = 10log10((S/D)/F)           // RMS 相对满幅参考偏移
p = 10log10(K/F)               // 实际峰值相对满幅参考偏移
PAPR = p - r
```

- D 保留现有选区、周期补零、重复语义，不能替换为本次扫描样点数。最终量化数据是权威来源。
- 单样点功率最大 2147352578，可放入 int32；累加必须先扩展到 uint64。1 GiB 输入的累计值小于 2^59，转 double 的相对误差约 2^-53，可忽略。
- AutoScale 原始峰值不钳位 -32768，(-32768,-32768) 的功率为 2^31，必须以 uint32 比较；不能复用统计钳位内核。
- 所有 ISA、所有分块方式得到的整数 S、K 必须完全一致（求和与取最大均满足交换律与结合律）。对数结果按数值容差比较。
- 全零、空数据、无效区间返回无效指标，不伪造 0 dB 偏移。

## 4. 工作线 A：统计计算优化

### 4.1 优先级：先测量，再减遍数，再 SIMD

1 GiB 数据的每一次整段遍历在树莓派上都是数百毫秒量级的内存带宽成本，读盘本身更慢。因此顺序为：

| 阶段 | 内容 | 完成条件 |
| --- | --- | --- |
| A0 基线 | 临时日志记录读盘、小端转换、原始峰值、缩放统计、下载各阶段耗时；x64 与树莓派各测 1000 MiB IQS 和普通 WAV，AutoScale 开/关；同时查看 MSVC `/Qvec-report:2` 与 GCC `-fopt-info-vec` | 得到各阶段占比，注明 MiB/GiB 单位；临时日志收敛后清除 |
| A1 小端转换 | 两个读取器的整段小端转换仅在大端主机编译（`Q_BYTE_ORDER`）；小端主机零成本 | 若 A0 证明编译器未消除该循环，即少一遍读写；结果逐字节一致 |
| A2 读块融合 | 读取器提供可选的逐块观察回调，在数据刚读入、仍在缓存中时累计：AutoScale 时累计原始峰值；scale==1 时直接累计 S、K | IQS/AutoScale 从“读+转换+峰值+缩放统计”降到“读(含峰值)+缩放统计”；S、K 与整段计算完全一致 |
| A3 SIMD 归约 | sum/peak 与原始峰值内核的 SSE2/AVX2/NEON 实现（见 4.3），供整段路径与 A2 回调共用 | 各 ISA 整数结果一致，无 O(N) 新分配 |
| A4 缩放量化 SIMD | 仅在 A0/A3 后缩放统计仍为瓶颈时实施 | int16 输出逐样点一致，指标一致 |
| A5 有界并行 | 仅在实测仍有收益时实施 | 无内存、取消、退出或 UI 响应退化 |

32768/131072 words 短波形不是优化目标（64/256 KiB）。

### 4.2 读块融合（A2）的边界

- 回调签名只依赖 `const qint16 *` 与字数，`libs/utils` 不依赖 Core；累计规则仍由 Core 的统计内核唯一实现，ARB provider 把 Core 累加器接到回调上。
- 回调在小端转换之后、下一块读入之前调用；块大小取缓存友好的量级（例如 256 KiB～1 MiB，以 A0 实测为准），而非当前 16 MiB。
- 普通 WAV 读入的是切片 `[sampleOffset, sampleOffset+samplesToUse)`，统计范围与当前一致；period 补零只进入分母，不需要扫描零。
- AutoScale 第二遍依赖全局峰值，在现有精确语义下不能与读取合并；保持 double 乘法、限幅、向零截断，不改 float，不先算未量化指标。
- 自主生成器（Multitone、AWGN 等）若在最终量化处能复用同一累加器，可省去生成后再扫描；不得把规则复制到每个 provider。

### 4.3 SIMD 内核

公共入口继续负责数据、范围和分母校验及最终指标；底层只返回精确 S/K。架构选择在调用边界，不进入逐样点循环。支持未按向量宽度对齐的输入，尾部用标量，不越界读取，不为对齐复制数据。内核同步借用 buffer。

**x86 SSE2/AVX2**：SSE2 每次 4 对 IQ、AVX2 每次 8 对；`max_epi16` 钳位 -32767；`madd_epi16(v,v)` 直接得到 I²+Q²；SSE2 峰值用比较/选择（不能误用 SSE4.1 `max_epi32`）；零扩展到 uint64 后累加；循环后合并通道再处理尾部。原始峰值路径中 madd 的 0x80000000 位模式按 uint32 的 2^31 解释，用无符号最大值或符号位偏置比较。AVX2 放独立编译单元，仅该单元开启 `/arch:AVX2` 或对应选项；运行时检查 AVX、AVX2、OSXSAVE 与 XCR0 的 XMM/YMM 状态后才调用。

**AArch64 NEON**（标准 Armv8-A，不依赖 SVE/dot-product）：

```text
vld1q_s16
  → vmaxq_s16（仅统计路径钳位 -32767）
  → vmull_s16 / vmull_high_s16
  → 解释为 uint32 → vpaddq_u32（I²+Q²）
  → vmaxq_u32（峰值）
  → vpadalq_u32（扩展累加至 uint64）
  → 循环后 vaddvq_u64 / vmaxvq_u32
```

可比较 2～4 组独立累加器与适度展开，以真机实测决定。

**缩放量化（A4）**：x86 用 `_mm256_cvttpd_epi32` / SSE2 `cvttpd`，AArch64 用 `vcvtq_s64_f64`（FCVTZS），均为向零截断，与标量 `static_cast` 一致；限幅先于转换。

### 4.4 编译与部署

按 CPU 架构选择实现，不按 Windows/Linux 区分。现有 scripts/aarch64.cmake 使用 aarch64-linux-gnu-g++（交叉 GCC 7），树莓派本机 GCC 12.2，树莓派与 RK3588 共用包：保持共同架构基线，不加 `-mcpu=native`，AArch64 不使用 ARM32 的 `-mfpu=neon`，整数归约不需要 fast-math。GCC 7 与 GCC 12 的默认向量化行为不同，A0 需分别确认。当前 Windows 配置为 x64。

### 4.5 内存

不产生 O(N) 中间功率、float 或 double 数组（1 GiB int16 转 float 额外约 2 GiB）。并行（A5）时各线程使用局部 uint64/uint32，结束后合并；任务 owner 负责等待完成、维持 buffer 生命周期并沿用既有取消/退出协议。

## 5. 工作线 B：功率量与公式

当前 API Level L 是共享满幅参考，不是实际波形 PEP：

```text
RMS = L + r
实际波形 PEP = L + p
PAPR = p - r

RMS 目标 R → API Level L = R - r
参考 PEP/既有 Level 目标 T → API Level L = T
```

例：r=-9 dB、p=-3 dB、RMS 目标 -20 dBm，则实际 PEP=-14 dBm，API Level=-11 dBm。

本方案保留 Level 输入的参考含义，只新增 RMS 目标。若产品要求“实际 PEP 目标”，应显式命名并另定兼容迁移。

## 6. 默认 RMS 与一次性 Level 意图（2026-10-23 用户修订）

默认持续保持 RMS，用户修改波形参数时使用新波形指标反推 Level。用户修改 Level 只针对本次当前/待生成波形；该请求成功应用后，用匹配的回写 Level+r 建立新的 RMS 目标，后续改波形继续保持这个新 RMS。没有长期保持 Level 的隐藏模式。

### 6.1 最小状态与启动显示

| 状态/数据 | owner | 含义 |
| --- | --- | --- |
| Startup | Core TxSessionService | 尚无有效 RMS 目标，下载前只显示 RMS=当前 Level；首个波形保持恢复的 API Level，完成后再建立 RMS |
| Rms + 目标 R | Core TxSessionService | 持续保持 RMS；显式 RMS 输入、有效 baseband 首次完成或手改 Level 完成后建立 |
| PendingLevel | Core TxSessionService | 用户主动确认 Level 的一次性待应用意图；Level 数值仍由现有属性持有 |
| 是否已成功应用过 baseband | Core TxSessionService | 本进程历史，用于区分启动镜像与已有波形的实际功率语义，不缓存 IQ/指标 |
| r、p 与 payload 身份 | provider execution context | 沿用现有请求，不增加指标缓存 |

软件启动，仍为未编辑 Startup 且未成功应用 baseband、模式支持全局功率时，RMS 镜像 Level。明确输入 Level 则进入 PendingLevel；参考未知时 RMS=--，有效成功后再建立保持目标。明确 RMS 输入显示该目标，未知派生 Level=--。

启动镜像是无 baseband 指标时的显示规则，不对首个波形自动补偿。例如初始 Level=-20，首次 3 音 Fixed 波形 r≈-7.78，首次完整配置仍保持 Level=-20，并以 RMS≈-27.78 建立后续目标。此后改变波形默认保持该 RMS。若用户在首次波形未就绪时主动确认 RMS，则立即建立该显式目标，待最终指标就绪后反推 Level；确认 Level 则按该次显式 Level 应用后建立相应 RMS，最后确认的字段优先。

旧配置只持久化 API Level，未保存独立 RMS。若将旧配置的 Level 直接当作首个波形的 RMS 目标，会在每次重启同一波形时重复增加 -r 的补偿，导致电平累加。Startup 首次应用后再建立实际 RMS 保留旧含义；version 5 有独立 power 时按其参考恢复。全零/无效参考时，明确输入仍显示设置数值，未知派生量为 --，不能由全零建立有限输出 RMS。普通回写不得使目标漂移。首次目标建立、手改 Level 成功应用和范围限制仍是既有建锚/修正边界。

### 6.2 输入与完成边界

- Level 的用户确认（主界面、Minibar、原 SCPI Level 命令）建立 PendingLevel；只打开编辑器或取消输入不生效。
- RMS 的用户确认建立 Rms 目标。Startup/CW 默认值和设备回写不冒充用户显式 RMS 输入。
- 在匹配的有效 FixedCw、FScan CW、FixedPlayback 或 FScan Playback 完成后，PendingLevel 转回默认 RMS；RF OFF 的对应参考也可在 Mute 成功后建立，显式 Level 不受首次下载标志限制。LScan/MScan、Streaming、无可用参考和无有效指标的波形不能用其临时 Level 建立全局 RMS 目标。
- 复用 runtime 的 generation/latest-intent 过滤；旧 Level 完成不能覆盖后来的 RMS 目标。普通 updateByExternal/profileChanged 不创建新的 Level 用户意图。

## 7. 按 pipeline 解析与编辑策略

Core 组装请求时唯一解析 `common.level`；Core 同时提供数值投影与编辑能力，主界面/Minibar 不各自决定业务规则。下表默认计算针对已经建立 RMS 目标的状态；Startup 在首次有效 baseband 完成前保持原 API Level。

| 模式 | Level/RMS 入口 | 默认计算 | 显式 Level |
| --- | --- | --- | --- |
| FixedCw、FScan CW | 均可编辑 | L=R，r=0 | 本次应用后 R=L |
| FixedPlayback、FScan Playback | 均可编辑 | 匹配指标就绪后 L=clamp(R-r)；Phase 1 保留原 Level | 本次按 Level，成功后 R=L+r |
| LScan、MScan | 两者置灰 | 只消费扫描起止/点表 Level，不改全局目标 | 旧后端入口可保存意图，不消费扫描电平、不重启扫描 |
| Streaming | Level 可编辑，RMS 置灰 | 按 Level 输出，有限波形 RMS 目标保留 | 本次按 Level；返回有限波形后按该意图建立 RMS |
| Mute | 未启用扫描/Streaming 时可编辑 | 按假设 RF ON 的 Fixed/FScan CW/有效 Playback 参考预置 Level，不发射 | 匹配有效参考的 Mute 成功后建立 RMS；无参考继续等待 |

### 7.1 LScan / MScan

- 按已启用的 carrier plan 置灰，不等到 RF 打开或扫描开始；主界面和 Minibar 一致。若切换时编辑器已打开，则关闭不再适用的编辑器。不显示禁用原因提示。
- base Level 从计划导出：LScan 取 start，MScan 取第一个点。设备 base 回写不覆盖全局 Level；扫描点电平仍由扫描面板/点表拥有。
- 后端继续保证扫描只消费计划，不通过控件 readOnly 隐式改变旧 SCPI 合同。
- 全局 RMS 目标跨扫描保留，退出扫描后用当前波形 r 解析。扫描中改变 baseband，不会暗中平移点表；各点 RMS=各点 API Level+r。
- 若以后要提供每点 RMS 扫描，需要显式定义扫描参考类型，不能复用一个全局 RMS 标量或静默改变点表含义。

### 7.2 FScan

同一最终 baseband 只计算一次 r，统一 `L=R-r` 后配置整个 FScan。仅改变频率范围/步长不需要重新统计同一 IQ；改变波形则使用新请求的指标。CW 的 r=0。现有扫频执行可能重配/重启，不承诺无中断。

### 7.3 范围、无效数据及 Streaming

- 使用同一请求中的最终量化指标和实际播放周期分母；解析 Level 按现有显示精度规范化。
- `L=clamp(R-r,Lmin,Lmax)`；超限时直接把 RMS 目标改写为可达的 R'=L+r，不显示通知，不恢复之前的不可达目标。保存未显示取整的边界目标以保证幂等。
- PendingLevel 只在匹配且有效的完成上重新建立 RMS；API 失败或波形无有效功率时不伪造目标。
- 设备正常取整不持续修改已保持的 RMS。Level/RMS 显示由 Core 当前意图和匹配参考投影，已知时允许预览，未知派生值为 --；内部 Level 仍按输入/成功回写更新，不被占位显示清零；无新增状态文字或 tooltip。
- Streaming 没有有限 payload 指标，RMS 不参与其输出且入口置灰；保留全局目标到支持的模式。若在 Streaming 主动改 Level，则保留该显式意图到可建立新 RMS 的模式。

## 8. 提交与时序

### 8.1 复用现有 runtime，不拆分执行阶段

现有机制已经覆盖连续编辑：

1. 用户确认 RMS/一次性 Level → `requestRefresh` → `buildRequest` 解析 L；有效 Level 完成后建立新 RMS。
2. 与 desired 相同则合并；有 in-flight 时替换 pending-latest，保持至多一个 in-flight 和一个待执行最新请求。
3. 同一波形已驻留时，新请求只差 `common.level`：执行器复用驻留波形，满足条件时走 FFM-only，不重新生成、不重扫、不重传。
4. 波形生成完成 → provider 通知刷新 → `buildRequest` 读取此刻最新的参考与目标解析 L，因此生成期间的多次编辑只按最后一次应用。

初稿中“准备驻留”和“最终配置/启动”两阶段拆分**首期取消**。代价是：长下载期间改了 RMS，下载完成后会先按提交时的 L 输出，再经 pending-latest 以 FFM-only 收敛到最新 L。这一短暂旧目标在初稿中已被接受；若真机验证认为不可接受，再单独设计 executor 阶段拆分，不在 UI 层补救。

### 8.2 必须成立的时序规则

- 生成期间（无 payload）请求只做 Phase 1，L 取 Level 属性值，不按新 RMS 目标对旧输出做补偿，界面只显示目标或 --，不新增等待提示。
- 新波形请求的 L 永远来自同一请求中的新指标，不会出现“新波形 + 旧补偿 Level”。
- 已开始执行的设备调用视为已提交；后来的编辑只成为下一次执行。收敛前保留最新用户目标，旧结果不能覆盖该目标；界面不显示应用状态文字。
- RF-off 走现有串行停止路径；旧完成不能重新启动输出。
- 仅编辑 RMS/Level 不使波形身份或指标失效；IQScale、AutoScale 等改变 IQ 的编辑仍产生新波形。RMS 保持会抵消 IQScale 带来的功率变化，界面不附加交互提示。

## 9. 界面

Level/RMS 只显示标题、数值和单位，不显示 Hold、Applied RMS、状态文字、tooltip 或提示弹窗。主界面公共栏维持 60/125 px；Minibar 无提示占位。

- 启动无显式 RMS、尚未应用 baseband：RMS=当前 Level。
- 保持 RMS：显示目标 R；参考已解析时预览派生 Level，未知时为 --。预览不表示波形已下载或设备已应用，不改变成功后建锚的保存合同。
- 用户手改 Level：当前有效应用完成后显示新的 RMS 目标；后续波形变化继续保持此值。
- 无效波形：Rms 意图显示设置目标、Level=--；PendingLevel 显示输入 Level、RMS=--。全零不能建立有限输出 RMS，已有目标仍保存。
- LScan/MScan：全局 Level/RMS 置灰，已有波形时 RMS 显示 --；退出恢复编辑。
- Streaming：Level 可编辑、RMS 置灰；已有 baseband 时 RMS 显示 --。

## 10. 持久化与 SCPI

2026-10-26 使用 formatVersion=5 的 power.reference/valueDbm 保存 startup/rms/level 意图，并新增公开 [SENSe:]POWer:RMS[?]。旧配置无 power 时进入 Startup；新配置按保存参考恢复。只有未编辑 Startup 镜像 RMS=Level，首次有效 baseband 保持恢复 Level 并在成功后建立 RMS。相同波形连续重启不重复补偿；保存的 rms 意图跨启动继续保持目标。

原 SCPI Level 命令仍代表显式 Level 输入；该 Level 在支持的模式应用后建立新的 RMS，后续改波形保持 RMS。UI 置灰不改变扫描点的 API Level 语义，也不新增外部命令。

## 11. 人工检查

完整步骤、理论推导、条件、误差边界和记录模板见 [多音 RMS/Level 人工测试方案](multitone_rms_level_test_plan.md)。重点包括启动镜像与首次建立实际目标、重启不累加电平、Fixed 3/5/9 音、手改 Level 后建锚、异步最新意图、扫描置灰/FScan 换算和 Streaming 往返。方案未执行，不表示测试已通过。

## 12. 交付顺序

- A 线：A0 → A1 → A2 → A3 →（按需）A4 → A5，见 4.1。
- B 线：
  - B1：LScan/MScan 请求 base level 由计划导出、回写不覆盖 Level 属性（不依赖 RMS，可先交付，消除无效扫描重启）。
  - B2：参考类型与 RMS 目标进入 TxSessionService，`buildRequest` 唯一解析函数与适用矩阵、范围限制与 RMS 回写。
  - B3：CommonPanel/Minibar 的 RMS 编辑入口与数值显示。
- A、B 两线互不依赖。

## 13. 依据与相关文档

- [当前 RMS/PEP 参考口径](htra_rms_power_multitone_pulse_principle.md)
- [大波形内存与下载](large_waveform_streaming_plan.md)
- [Center/Level 配置链路](CenterLevel配置链路重构设计.md)：FFM-only 现状与 LScan 普通 Level 延迟应用方向（本方案 7.1 落实）。
- [TX 请求、执行与回写](tx_execution_context_phase1_and_provider_migration.md)
- [AArch64 部署兼容性](raspberry_pi_132_build_host_250_compatibility.md)
- [Arm NEON intrinsics](https://arm-software.github.io/acle/neon_intrinsics/advsimd.html)
- [GCC AArch64 options](https://gcc.gnu.org/onlinedocs/gcc/AArch64-Options.html)
- [MSVC /arch](https://learn.microsoft.com/en-us/cpp/build/reference/arch-x86?view=msvc-170)
- [MSVC 向量化报告](https://learn.microsoft.com/en-us/cpp/build/reference/qvec-report-auto-vectorizer-reporting-level?view=msvc-170)

## 14. 工作线 B 实施记录（2026-10-23）

- TxSessionService 持有功率参考与 RMS 目标，使用本请求的 powerMetrics 在 buildApplyRequest/resolvePowerLevel 中 O(1) 解析；没有新增波形指标缓存或拆分 executor 阶段。启动历史规则见第 15 节。
- RMS 超限时按当前设备能力限制 Level，保存未显示取整的可达 RMS；比较 RMS 边界保证重复解析幂等。Level 请求按现有功率显示精度（metadata 未配置时 2 位）规范化。
- LScan/MScan 的 base Level 由扫描计划导出；转发设备回写时保留用户 Level。runtime 对相同 desired 的新快照复用 in-flight/已确认 applied，避免设备舍入导致无效重启；显式 forceExecution 仍执行。
- CommonPanel 和 Minibar 均新增 RMS 键盘/步进入口；按用户后续要求移除 Hold、状态文字、tooltip 和限制通知，仅显示标题、数值和单位。
- 主界面确认相同 Level 也创建一次性 Level 意图；完成后自动回到 RMS。Minibar 较早未发送意图互相替代，避免跨字段请求反序覆盖。
- 生成未就绪时沿用 Phase 1 原 Level，继续接受最新 RMS；有 payload 但无有效功率时显示 --。Streaming 保留 RMS 意图、只消费 Level；离开非适用模式自动解析当前参考。
- 新增的 SOUR:POW:RMS 仅为主进程与 Minibar 私有 IPC 命令；没有增加对外 SCPI 命令或 RMS 持久化。启动显示镜像 Level，首次波形保持恢复 Level 并建立实际 RMS。
- 主界面公共栏移除功率状态行，高度恢复为 60/125 px。Minibar 收起/展开均包含 RMS，移除展开区提示块。

本轮按用户要求仅做源码、CMake 纳入范围、声明/信号链与差异静态检查，未编译、运行或执行测试。设备 FFM-only 收敛、扫描不中断及树莓派触摸界面效果待用户实测。

## 15. 默认 RMS 与启动规则实施记录（2026-10-23）

Core 使用 Startup/Rms/PendingLevel 状态和“已成功应用过 baseband”历史标记。Startup 随当前 Level 显示；首次有效波形完成或显式 Level 完成后建立 RMS，普通回写不持续改变目标。数值显示与编辑能力由 Core 统一投影，主界面和 Minibar 同步消费。

LScan/MScan 全局功率字段置灰；Streaming 仅 RMS 置灰；FScan 沿用统一 baseband 指标反推 Level。模式切换关闭不适用的编辑器，未增加提示。移除旧窗口断开时强制写入无效 RMS 的入口，避免启动/未连接阶段出现 --。

本轮仅静态检查，未编译、运行或执行测试。用户人工测试文档为 multitone_rms_level_test_plan.md。

