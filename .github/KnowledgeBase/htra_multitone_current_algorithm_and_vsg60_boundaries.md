# HTRA Multitone 当前算法、参数语义与 VSG60 边界

本文档只描述当前仓库里的 htra multitone 实现，不回顾已经废弃的旧公式或旧入口。目标是把下面几件事一次说清楚：

1. 当前 multitone 到底有哪些参数，它们各自控制什么。
2. 当前 generator 如何从参数生成最终 IQ 波形。
3. 当前最终量化口径为什么等价于默认开启 AutoScale。
4. 当前频谱预览用的是什么分析口径。
5. 当前实现与 VSG60 哪些地方已经对齐，哪些差异仍然属于允许边界。
6. 当前采样率和参数限制到底是什么。

## 1. 代码范围

本文结论基于当前代码路径：

- `src/plugins/htra/multitonegenerator.h`
- `src/plugins/htra/multitonegenerator.cpp`
- `src/plugins/htra/multitonemodulation.h`
- `src/plugins/htra/multitonemodulation.cpp`
- `src/plugins/htra/multitonespectrumdialog.h`
- `src/plugins/htra/multitonespectrumdialog.cpp`
- `.github/KnowledgeBase/multitone_vsg60_three_case_comparison_and_playback_consistency.md`

如果后续这些文件发生变化，应以最新代码为准重新更新本文。

### 1.1 当前分层边界

当前 Multitone 已把设备决策与波形算法分开：

- `MultitoneModulation` 消费 UI profile 和 current `PlaybackCapabilities`，负责 Count/FreqSpacing 收口、非连续 sample-rate domain 搜索、capacity 限制、参数回写和设备切换后的 Enabled 策略。
- `MultitoneGenerator` 只根据 profile 报告 `minimumSampleRate / fundamentalStep / preferredSampleCount` 等设备无关的数学要求，并接收 business 已解析的精确 generation plan。
- generator 不再 include、缓存或查询 `PlaybackCapabilities`，也不决定125/1000 MiB或125/200/400 MSPS设备档位。
- tone lattice、notch/discrete selection、phase、bin 碰撞合并、FFT/直接合成、DC 修正与 AutoScale 仍完全属于 generator，本次分层不改变这些算法语义。

## 2. 当前参数模型

当前 generator 暴露的 profile 参数是：

- `PhaseMode`
- `FixedPhaseOffset`
- `Seed`
- `NotchWidth`
- `Count`
- `FreqSpacing`
- `DiscreteKeepToneMode`
- `EnabledToneIndices`

它们的含义如下。

### 2.1 PhaseMode

当前支持三种相位模式：

- `Fixed`：所有 active tones 使用同一个固定相位偏置。
- `Random`：每根 active tone 使用独立随机相位，随机数由 `Seed` 驱动，因此同一实现内可复现。
- `Parabolic`：按 Schroeder family 二次相位律分配相位。

### 2.2 FixedPhaseOffset

这是一个统一加到最终 tone 相位上的固定偏置。

- `Fixed` 模式下，它直接就是所有 tone 的相位。
- `Random` 模式下，它会叠加到每个随机相位上。
- `Parabolic` 模式下，它会叠加到抛物线相位律的结果上。

当前实现会先把它归一化到 $[0, 2\pi)$。

### 2.3 Seed

只在 `Random` 模式下参与运算。当前实现使用 `std::mt19937` 和 $[0, 2\pi)$ 上的均匀分布来生成每根 tone 的独立随机相位。

这意味着：

- 在当前实现内部，同一组输入能稳定复现。
- 但它不自动保证与 VSG60 的随机数实现 bit-exact 一致，因为两边未必使用同一 RNG、同一浮点映射方式和同一 tone 遍历顺序。

### 2.4 NotchWidth

当前 `NotchWidth` 固定表示“以界面逻辑 center 为中心的对称 RF notch 带宽”。奇数 `Count` 下，逻辑 center 与设备基带 DC 重合；偶数 `Count` 下固定采用中心音遮盖策略，两者相差 `FreqSpacing / 2`。

实现方式不是任意频率位置挖空，而是：

- 先生成 candidate tone lattice。
- 奇数 `Count` 下，删除满足 $|f_{baseband}| < \frac{NotchWidth}{2}$ 的候选 tone。
- `Count` 为偶数时，FixedPlayback 和 Playback LScan 设备 center 为 `logicalCenter + FreqSpacing / 2`，因此先换算

$$
f_{logical} = f_{baseband} + \frac{FreqSpacing}{2}
$$

  再删除满足 $|f_{logical}| < \frac{NotchWidth}{2}$ 的候选 tone。
- 若 notch 覆盖到全部 candidate tones，则当前实现允许 active tones 变成 `0 tone`；生成阶段会把它解释为静默 playback waveform，而不是空 payload。

因此，当前 notch 的产品语义始终围绕用户看到的逻辑 center；它不是任意中心频率 notch。偶数 `Count` 只改变判断所用的坐标，不改变 tone 实际用于 IQ 合成的基带频率。当前源码不在选音后另行平移奇数 active tones。

### 2.5 Count

`Count` 控制候选 tone lattice 的离散个数，当前产品范围为 `2..1024`，但 odd/even 的排列方式不同。

2026-10-26：Main 与 Minibar 的 Count 输入范围统一为 `2..1024`。Minibar 不再把当前 spacing 下可容纳的最大音数（例如 101）当成键盘输入上限；输入后仍由 Main business 解析设备能力并规范化 spacing。helper 候选表按显示快照去重、复用单元格；单音等值确认无需重建，latticeIndex 身份和生成算法保持不变。用户构建复测正常，日志确认 1000 音改单音后响应/事件约 7 ms 完成且未重复同步表格；该无响应场景已修复，临时诊断代码已移除。此次 UI/IPC 复测不代表全范围硬件发射验收。

2026-10-27：用户确认 Minibar 的 1024 音改单音正常，但 Main 相同操作仍无响应。核对发现前述表格优化只覆盖 helper；Main 在同步业务回填时仍因 enabled 与旧显示缓存不同而替换整表 item。本轮 Main 同样在发出完整使能集合前按 latticeIndex 同步现有显示缓存，等值候选回填直接跳过刷新；确有候选变化时复用单元格并只写变化的行数、文本、latticeIndex 和勾选。生成/规范化仍归业务 owner；本轮不改算法、中心补偿或功率设置。用户随后构建实测确认 Main 修复有效，已核对两端表格处理规则及 Main 业务入口一致，本次无响应问题关闭；按用户要求不再追查历史原因，不将该结论扩大为全部硬件组合验收。

- 当 `Count` 为奇数时，候选 tone 是整数倍 `FreqSpacing`，因此包含 DC。
- 当 `Count` 为偶数时，候选 tone 固定采用整数倍 `FreqSpacing` 并包含 DC；设备中心补偿半间距后，逻辑 RF 频偏仍落在 $(n + 0.5) \cdot FreqSpacing$。

更具体地说：

- odd `Count = 2m + 1` 时，candidate tones 为

$$
\{-m\Delta f, \ldots, 0, \ldots, +m\Delta f\}
$$

- even `Count = 2m` 时，candidate tones 为

$$
\{-m\Delta f, \ldots, -\Delta f, 0, \ldots, +(m-1)\Delta f\}
$$

这里 $\Delta f = FreqSpacing$。

odd `Count` 始终采用上面的对称整数 lattice；当前实现不根据中心音是否被禁用额外平移 active tones。

### 2.6 FreqSpacing

`FreqSpacing` 是 tone lattice 的相邻频率间隔。当前实现中它至少为 `1 kHz`，并且还会和 `Count` 一起受采样率上限约束。

### 2.7 DiscreteKeepToneMode

当前实现新增了一个规则 lattice 内的“离散保留音模式”。

- `false`：切回 notch 模式，`NotchWidth` 表示逻辑 center 中心 notch。
- `true`：进入离散保留音模式，active tones 不再由 `NotchWidth` 决定，而是由 `EnabledToneIndices` 决定；当前 UI 默认开启该模式。

这不是通用 explicit tone vector，也不是不规则 multitone；它仍然建立在当前 `Count + FreqSpacing` 生成的规则 candidate lattice 上。

### 2.8 固定中心音遮盖策略

2026-11-12：用户确认默认启动后保存为 true，但 Preset 后保存为 false。当前分支的根因是构造函数单独开启开关，而默认 profile/Reset 为 false。按用户最终要求，保留唯一内部开关 `inline constexpr bool kEvenCountCenterToneMask = true`，程序运行期间不可修改；删除旧 `EvenCountCenterToneMask` 的 profile 属性、可变成员、getter/setter、信号及 JSON 持久化，不对外开放，也不再依赖构造函数修正默认值。

- 偶数 `Count`：固定把对称 half-spacing lattice 整体偏移 `-(FreqSpacing / 2)`，使一根候选 tone 落到基带 DC；设备中心偏移固定为 `FreqSpacing / 2`，与 active 选择无关。
- 奇数 `Count`：保留现有对称整数 lattice，设备中心偏移为零。本分支没有选音后移动奇数 active tones 的逻辑，之前关于该算法及结果内 `deviceCenterOffset` 的记载不适用于当前源码。
- 新保存配置和 Save IQ 的 prof 不再包含旧键。旧文件中无论该键为 true、false 或缺失，加载时都由现有已知属性遍历自然忽略，不需要迁移分支。正常启动、Preset 和加载配置使用同一策略。
- 旧 false 文件会有意改变其偶数基带 IQ、预览、采样率/段长和导出样点；设备中心补偿后，逻辑 RF tone 位置保持原意。维护中的 testcase JSON 已删除该键，人工回归 K4 覆盖 Preset 和旧 false 文件。

中心音遮盖使偶数基带 IQ 故意偏离 SMA / VSG60 的对称排列，Save IQ 和频谱预览同样体现该基带偏移。被遮盖的 tone 会与 LO 泄漏矢量叠加，存在幅度和相位误差。此次只固定现有开启路径；离散选音仍可禁用 DC candidate，notch 仍按逻辑中心选音，不新增动态重选遮盖音算法。

HTRA Multitone provider 从当前 generator 的 `Count/FreqSpacing` 请求 `TxProviderExecutionContext::fixedPlaybackCenterOffsetHz`：偶数为 `FreqSpacing/2`，奇数为零。补偿规则只归属 Multitone，其他 provider 默认值为零。`TxPipelineExecutor::applyFixedPlayback()` 在设备配置时把实际下发中心频率改成 `logicalCenter + FreqSpacing / 2`，并在 `deviceConfigurationDone` writeback 前恢复 logical center。

此次开关删除已静态核对，未构建/运行或执行实机频谱验收。

`TxPipelineExecutor::applySweepPlayback()` 在 Playback LScan 下复用该偏移：基础 FFM 配置和 `startPlaybackLevelSweep()` / `tx_config_lscan()` 使用同一个补偿后 center。基础配置回写、无波形等待路径，以及扫描查询成功/失败的最终回写都保持逻辑 center，避免 UI / `CommonDeviceProfile` 漂移或重复加偏移。

2026-11-12 更新：Playback FScan 同样消费该偏移，`startPlaybackFrequencySweep()` 下发的 start/stop 均为逻辑扫描端点加 `fixedPlaybackCenterOffsetHz`，step 不变。查询成功后，在 executor 的 Playback 调用边界给设备实际返回的 start/stop 减去同一偏移，再发布日志、界面回写和 effective request；SDK 的实际修正值及错误/告警语义保持原样。FScan 的基础 FFM 保持既有逻辑 center，后续由补偿后的 FScan 计划替代。该补偿适用于 Single/Continue 和 Hop/Sweep；BUS + Hop 配置一致且已在设备上生效时，重复 Single 沿用已有 BUS-only 路径，保留驻留的补偿扫描计划。此次已静态实施，未构建/运行；CW 和 Streaming 不消费该偏移，不修改用户配置或全局中心频率。

2026-11-12 MScan 补齐：`applySweepPlayback()` 复制逻辑 `cp.mscanPoints` 到局部设备点表，遍历整表给每个 frequency 加相同 provider offset，再同步传给 `startPlaybackListSweep()`；level/dwell、点序、点数和 repeat 保持。查询成功后遍历全部返回点，各自减去相同 offset，再发布日志、effective request 和 Applied；不改 Draft/Submitted、提交版本或查询失败路径。按当前分支 provider 实现，偶数 mask 的 offset 为 `spacing/2`，奇数 Count 和其他无偏移 Playback provider 为 0；切换后从逻辑提交表重新下发整表，切到 CW 则走原 SweepCw 的整表下发，均不沿用之前补偿后的设备表。相同配置的重复 Single 仍可仅 BUS 推进；Hop/Sweep 执行动作保持原设置。修复仅静态核对，待设备频谱和偶数 → 奇数/CW 切换验收。

2026-06-30 revision: FixedPlayback still restores `writeback.center` to `logicalCenter` before publishing `deviceConfigurationDone`, so the temporary device offset does not accumulate through `CommonDeviceProfile`. The ready-payload success path now emits only one final `deviceConfigurationDone`; the Phase 1 writeback is kept only for the no-payload early-return path.

### 2.9 EnabledToneIndices

`EnabledToneIndices` 表示当前规则 candidate lattice 里哪些 tone 被保留。

- 它使用 candidate lattice 的稳定 `latticeIndex` 作为 owner。
- 它不是表格显示顺序，也不是任意频率向量；当前面板表格按相对界面逻辑 center 的频偏从低到高展示。
- 奇数 `Count` 下，表格频偏与实际 IQ/baseband `requestedFrequency` 相同。
- 偶数 `Count` 下，表格显示 `requestedFrequency + FreqSpacing / 2`，使用户看到的逻辑 RF 频偏保持左右对称；generator、bin mapping 和 IQ 合成仍使用未改写的 `requestedFrequency`。
- canonical 规则只做合法 `latticeIndex` 过滤、去重与稳定排序，不再强制补齐到至少 2 根。
- 默认或未显式初始化时，candidate tones 全部开启；但在离散保留音模式下，若显式保存空集合，则表示 `0 tone`。
- `0 tone` 不表示空 payload，而是要求 generator 产出一段合法的静默 playback waveform。
- 当前 UI / profile 在 candidate lattice 扩展时，会把新增 tone 默认设为 enabled，避免用户逐个重新勾选新增 tone。

## 3. 当前参数约束与采样率规则

### 3.1 当前采样率目标

当前采样率解析分两步：generator 报告数学要求，business 把要求与设备能力求交。

第一步先求一个带业务裕量的下限目标。当前实现按当前 active tones 在复基带中的最外侧绝对频偏求：

$$
F_{s,lower} = 1.25 \cdot 2 \cdot \max(|f_{min}|, |f_{max}|)
$$

其中：

- `f_max` / `f_min` 来自当前 active tones
- generator 只返回这个未夹带设备语义的下限；business 再与 current domain 的最小值、连续区间和离散点求交

这个口径不是简单的 active tone span `f_{max} - f_{min}`。原因是离散保留音模式下，active tones 可以不再关于 DC 对称；如果只按 span 求下限，会在只保留单边音时低估 Nyquist 边界。

第二步由 business 按当前 active tones 的 `fundamentalStep` 在 current domain 中选最终采样率，优先取不低于下限的最小合法 fundamentalStep 整数倍：

$$
F_s = \min \left\{ n \cdot fundamentalStep \mid n \in \mathbb{Z}_{\ge 1},\ n \cdot fundamentalStep \ge F_{s,lower} \right\}
$$

如果 current domain 边界不是 fundamentalStep 的整数倍，business 会保留满足带宽的最低支持采样率，再由 sampleCount solver 判断 `fundamentalStep / sampleRate` 是否能在 current capacity 内表示成精确有理周期。例如 `Count=10/11, FreqSpacing=10 MHz` 在无带宽选件档使用125 MSPS；`10 MHz / 125 MHz = 2/25`，因此 sampleCount 取25的整数倍即可精确闭合，不需要把参数错误回退。

整数倍优先的目的不是单纯“多留一点带宽”，而是优先获得简单、稳定的整周期段；边界有理周期则用于保留合法的设备极限组合。

采样率上限不再是 generator 内的硬编码125 MHz：无带宽选件档使用current连续上限125 MHz，含带宽选件档使用连续200 MHz加精确400 MHz单点。200~400 MHz空洞中的参数会由business回写到连续低档，只有精确格点可保留400 MHz。

对奇数 `Count` 的完整对称 lattice，这个口径会退化成：

$$
1.25 \cdot (Count - 1) \cdot FreqSpacing
$$

也就是说：

- `Count = 3` 时，下限为 `1.25 * 2 * FreqSpacing`。
- `Count = 101` 时，下限为 `1.25 * 100 * FreqSpacing`。

偶数 `Count` 固定采用中心音遮盖，其完整 lattice 的最外侧基带频偏为 $\frac{Count}{2}\Delta f$，因此下限为：

$$
1.25 \cdot Count \cdot FreqSpacing
$$

而在离散保留音模式下，关闭外侧 tone 后，采样率下限会随当前有效最外侧频偏自然收敛；若只保留单边音，这个口径也仍能守住 `F_s / 2 >= max |f|` 的基础边界。

若离散保留音模式下显式把所有 tone 都关闭，则当前 active tone 集为空，频谱占用下限会退化到设备允许的最小采样率；随后当前实现仍会按 profile 对应的 `fundamentalStep` 对齐最终 `sampleRate`，并继续生成非空的静默 IQ 段。这样 playback/download 链路看到的仍是一个合法波形，只是其复基带样值全为 `0`。

因此对无带宽选件档，奇数 `Count` 的“完整规则 lattice”业务约束为：

$$
1.25 \cdot (Count - 1) \cdot FreqSpacing \le 125\ MHz
$$

等价地：

$$
(Count - 1) \cdot FreqSpacing \le 100\ MHz
$$

偶数 `Count` 的完整 lattice 约束为：

$$
Count \cdot FreqSpacing \le 100\ MHz
$$

这就是当前 `Count` 和 `FreqSpacing` 联动收敛的核心下限边界。最终 `sampleRate` 可能会因为 `fundamentalStep` 对齐而略高于这个下限，但不会超过设备上限。

### 3.2 当前 business 与 generator 如何分工收敛参数

当前 profile 会先做 sanitize：

- `PhaseMode` clamp 到合法枚举范围。
- `2 <= Count <= 1024`
- `FreqSpacing >= 1 kHz`
- `NotchWidth >= 0`
- `FixedPhaseOffset` 归一化到 $[0, 2\pi)$。
- 中心音遮盖由内部常量固定为 true，不属于 profile 参数，Preset 和旧 JSON 不能关闭它。
- `EnabledToneIndices` 过滤为合法 `latticeIndex` 集合。
- 若未显式提供 `EnabledToneIndices`，则沿用“默认全开”；若在离散保留音模式下显式提供空集合，则允许 zero-tone，并在生成阶段输出静默 waveform。
- notch 路径不再强制保留外侧两根 tone；若 notch 把 active tones 全部清空，也同样进入静默 waveform 语义。

上述 sanitize 和 candidate index canonicalization 属于 generator 的固有 profile 语义。`1024` 是独立于设备带宽的产品上限，旧 preset、远程编辑和普通 UI 输入都必须先经过该上限收口。之后的设备交叉约束由 `MultitoneModulation` 完成：

1. 先用当前 `Count/FreqSpacing` 直接求 plan；精确可用的 400 MHz lattice 在这一步保留。
2. 原组合无 plan 时保持 `Count`，按 current continuous maximum 和完整 lattice 估算可用的最大 `FreqSpacing`，永久回写较小间距；偶数使用 `Count` 个占用步长，奇数使用 `Count-1`。
3. 只有该间隔低于 1 kHz 时，才固定 `FreqSpacing = 1 kHz` 并降低 `Count`；`setCount()` 继续按 multiplier 映射能保留的 tone 选择。
4. 按 current capacity 计算 sampleCount/layout，并向 generator 提交精确 `sampleRate + PayloadLayout`。

隐藏 tone 或扩大 notch 只会减少 active tones，因此不会触发旧配置回滚。普通编辑发生上述回写时保持 Enabled；设备能力变化导致回写时关闭 Enabled。

因此当前 UI / property 层看到的最终生效值，应理解为“业务能力收口 + generator 固有 canonical 语义”的结果，而不是用户刚输入的原始编辑值。

## 4. 当前 tone lattice 与 notch 语义

### 4.1 Candidate lattice

当前实现先根据 `Count` 和 `FreqSpacing` 生成候选 tone 集，再按请求频率从低到高排序，并为每根 candidate tone 保留稳定的 `latticeIndex`。

这个 `latticeIndex` 不是多余信息，它直接用于 `Parabolic` 相位分配。

### 4.2 Active tones

当前 active tones 有两种来源：

- notch 模式：由 `NotchWidth` 在界面逻辑 center 删除 candidate tone；奇数 Count 下该 center 就是基带 DC，偶数 Count 下按 `f_baseband + FreqSpacing / 2` 换算后判断。
- 离散保留音模式：由 `EnabledToneIndices` 直接筛出保留的 candidate tone。

无论哪种来源，active tone selection 都不负责重排 candidate 的相位基准。

这点尤其重要，因为：

- `Fixed` 和 `Random` 只关心 active tones 本身。
- `Parabolic` 还关心 tone 在原始 candidate lattice 中的稳定序号。

因此当前实现里，`Parabolic` 使用的是：

- candidate lattice 的稳定序号 `latticeIndex`
- 原始 candidate tone 总数作为相位分母

而不是 notch 之后重新压缩的 active tone 序号。

## 5. 当前相位分配规则

### 5.1 Fixed

`Fixed` 模式最直接：所有 active tones 使用同一个 `FixedPhaseOffset`。

### 5.2 Random

`Random` 模式使用：

- `std::mt19937(seed)`
- $[0, 2\pi)$ 的均匀分布
- 先按完整 candidate lattice 的稳定顺序生成随机相位，再按 active tone 的 `latticeIndex` 取相位

再把 `FixedPhaseOffset` 叠加上去。

所以当前 `Random` 的正确语义是：

- 带 seed
- 可复现
- 逐 tone 独立随机相位

而不是“所有 tone 共用一个随机相位”。

### 5.3 Parabolic

当前 `Parabolic` 使用的相位律是：

$$
\phi_k = \pi \frac{k(k-1)}{N} + \phi_0
$$

其中：

- $k$ 是 candidate lattice 的稳定序号
- $N$ 是原始 candidate tone 总数
- $\phi_0$ 是 `FixedPhaseOffset`

这保证了 notch 删除 tone 之后，剩余 active tones 仍然保持原本 candidate lattice 上应有的抛物线相位关系，而不会因为压缩序号而退化成别的模式。

## 6. 当前 sampleCount 的选择逻辑

当前 generator 提供 exact-period / 回放友好点数的纯数学 helper，`MultitoneModulation` 把 current capacity 反推得到的 `maximumComplexSamples` 传入 helper，并将最终 `sampleCount` 写入 generation plan。generator 在合成时不再自行查询设备容量。

### 6.1 基本目标

当前目标是让这段离散 IQ 在当前采样率下尽量 exact-period 闭合，这样循环播放时不会因为段边界不闭合而引入额外频谱。

### 6.2 Fundamental step

当前实现会先根据 active tones 求一个 fundamental step。

- 中心音遮盖内部常量固定为 true，因此 odd/even lattice 的 base unit 都是 `FreqSpacing`。
- 当前源码不对选音后的奇数 lattice 额外平移。
- 再对 tone multiplier 取 gcd，得到真正的频率基本步长

### 6.3 Sample count solver

当前 solver 会先对

$$
\frac{fundamentalStep}{sampleRate}
$$

做有理逼近。如果能在允许样本数范围内找到足够精确的分母，这个分母就是**最小 exact-period 周期长度**。

但当前实现不会再把这个最小分母直接作为最终导出长度，而是会再做一步“回放友好长度”扩展。当前 `sampleCount` 先满足一个动态下限：

$$
N_{min} = \max( minimalPeriod,\ MinimumPlaybackComplexSamples,\ \lceil F_s / 100\ Hz \rceil )
$$

这里的含义分别是：

- `minimalPeriod`：保证 exact-period 不被破坏
- `MinimumPlaybackComplexSamples`：对齐当前设备短波形下发补长规则
- `F_s / 100 Hz`：把导出段长与采样率挂钩，控制等效频率分辨率在大约 `100 Hz` 或更细，而不是写死一个固定点数

在得到这个动态下限后，当前实现按下面规则选最终 `sampleCount`：

- 如果 `minimalPeriod` 本身就是 2 的幂，则直接取**不小于** `N_{min}` 的最小 2 的幂 `sampleCount`
- 否则取**不小于** `N_{min}` 的最小 `minimalPeriod` 整数倍
- 同时继续受 `MaxComplexSamples` 上限约束

因此，当前 `sampleCount` 的语义已经从“接近某个固定首选点数”变成了“满足 exact-period、播放最小段长和目标 RBW 的最小工程友好段长”。

如果做不到 exact-period，就回退到一个仍然合法的估计值。当前实现还受最大复采样点数约束：

$$
MaxComplexSamples = \frac{MAXDOWNLOADSIZE}{2 \cdot sizeof(short)}
$$

因此当前 `sampleCount` 的首要目标不是“越短越好”，而是“在当前 playback 语义下闭合、稳定，并随着 `sampleRate` 自动扩展到合适的 power-of-two 或 exact-period 长度”。

## 7. 当前波形合成路线

当前 htra multitone 先按最终 `sampleCount = N` 把 tone 解析并合并到离散 bin，再约简共同周期 `P`，根据 `P` 选择原地 IFFT 或单遍直接时域合成，最后重复填充 N 点 IQ。

### 7.1 Tone 落 bin

对于每根 active tone，当前实现会根据当前 `sampleRate` 和 `sampleCount` 计算：

$$
bin = round\left(\frac{f \cdot N}{F_s}\right)
$$

然后得到每根 tone 的：

- requested frequency
- effective frequency
- resolved bin index
- resolved phase

同时统计：

- `maxFrequencyError`
- `binCollisionCount`

### 7.2 频域合成

当前实现先在一个大小只随 tone 数量增长的 bin 列表中，按 natural-bin 顺序合并同 bin tone：

$$
X[bin] += e^{j\phi}
$$

这里还没有做最终 int16 缩放。该小型列表同时保留旧算法的同 bin 幅相合并语义，但不再为非幂次路径分配一个 `sampleCount` 大小的完整频域数组。

### 7.3 时域恢复

对合并后的 natural bin `k_i`，先计算：

$$
g = gcd(N, k_1, k_2, \ldots),\qquad P = N/g
$$

仅把内部合成列表的 bin 改为 `k_i/g`，外部 tone 的原始 bin、effective frequency、phase、频率误差和碰撞统计仍基于 N。因为 `k_i/N = (k_i/g)/P`，P 点构成原离散波形的一个共同周期；负频率使用 natural bin 同样成立。仅 DC 或 active tones 为空时自然退化为 P=1。合并后幅度恰为零的 bin 仍参与 gcd，不改变现有合并语义，最多只减少约简收益。

唯一 `complex<double>` 工作区现在只分配 P 点：P 为 2 的幂时，在其中原地执行本地 IFFT，按 P 完成归一化与幅度恢复；否则按约简后的 natural-bin 顺序直接合成一个周期。两条路径随后都只对 P 点做目标感知 DC 修正、峰值/能量统计和量化，RMS 的分母使用 P。

量化后的首个 P 点直接写入唯一最终 Builder 缓冲，再按已初始化的整周期前缀倍增复制到 N 点；每次 memcpy 最多 1024 个复样点并检查 generation revision，源/目标区间不重叠。最后仍通过原有 publishPayload 做 allocatedWordCount 的下载补齐，不增加第二份完整 IQ。

主要驻留内存由原先 `4N+16N` 降为 `4N+16P` 字节（不计下载补齐和小型 tone/频谱数组）。相对于最终有效 IQ 的比例为 `1+4/g`，g 较大时才接近一份最终 payload；g=1 时 P=N，保留原完整合成路径。直接合成计算量变为约 `P*K'`，但最终 N 点缓冲的分配与填充仍为 O(N)。

上述周期等价是数学结论；FFT 长度、递推长度及浮点求和顺序改变后，新旧 metrics/IQ 的数值容差仍需对照验证，不能仅凭数学等价保证所有参数下逐点误差不超过 1 LSB。当前实现仅完成静态核对。

### 7.4 目标感知去直流

当前实现在时域 waveform 恢复后、进入最终量化前，会先做一次“目标感知”的残余 DC 消除。

这里不是无条件把整段波形强制改成零均值，而是先按当前参数语义确定当前**应有的目标均值**：

- 若当前 active tones 合法包含中心 `0 Hz` tone，则保留该中心 tone 本应贡献的复常量分量。
- 若当前 active tones 不包含合法中心 tone，则目标均值为 `0`。

随后当前实现计算一个 P 点共同周期的实际复均值，只减去相对该目标均值的残余；重复周期的 N 点均值与其相同：

$$
\mu_{act} = \frac{1}{P}\sum_{n=0}^{P-1} x[n]
$$

$$
\epsilon = \mu_{act} - \mu_{target}
$$

$$
y[n] = x[n] - \epsilon
$$

这样做的目的，是同时守住两件事：

- 合法中心 tone 仍然作为 wanted signal 保留下来。
- 非目标的数字 DC 残余不会继续占用后续 autoscale 的动态范围。

若后续扩展 `per-tone magnitude`，这里的目标均值也应同步扩展为“中心 tone 的目标复幅度”，也就是从当前等幅实现下的

$$
e^{j\phi_0}
$$

扩展成：

$$
A_0 e^{j\phi_0}
$$

其中 `A_0` 是中心 `0 Hz` tone 的目标幅度。

### 7.5 异步生成与连续编辑语义

当前波形合成运行在 `MultitoneGenerator` 的专用 worker 线程，不在 Qt GUI 线程。GUI 线程只负责参数规范化、设备能力解析、property 回写和 tone table 同步。

异步生成采用单调递增的 generation revision：

- 每次提交新的 generation plan，先递增 revision、清空旧 ready payload 并把 Save IQ Data 置为不可用。
- worker 启动一轮计算时同时快照 profile、plan 和 revision。
- 新请求到来后，旧 revision 会在直接合成、FFT、DC 修正、统计、量化或周期填充的取消点退出，不再等待整段旧波形自然算完。
- 只有 revision 仍为最新且没有 pending profile 的结果可以提交；被取消的任务不发布部分 payload，也不发 ready/error 结果。
- 析构同样先使当前 revision 失效，再等待 worker 退出，避免被大规模直接合成长时间拖住。

前端与 worker 的事务顺序固定为：

1. 规范化参数并完成 current device capability plan 解析。
2. 先提交新 plan/revision，使旧任务立即失效并禁用 Save IQ Data。
3. 再按同一份规范化 profile 同步 property 和 tone table。
4. worker 只生成并提交最新 revision。

主界面不会在 `resolveAndGenerate()` 外重复同步 tone table；`MultitonePanel` 对相同 candidate snapshot 直接跳过重建，并在确实需要批量填表时屏蔽 `itemChanged` 和 repaint。C/S helper 的 remote panel 也只在 `candidateTones` snapshot 变化时重建。这个边界保证表格始终立即反映当前用户参数，同时避免 status/result 回报把同一批行反复创建。

## 8. 当前最终量化口径：等价于默认开启 AutoScale

这是当前多音最容易被误解的一点。

### 8.1 当前并不是固定 RMS 输出

当前 generator 在最终写出 int16 IQ 之前，会先在时域恢复后扣除相对目标均值的残余 DC，然后遍历一个 P 点共同周期，计算：

- `peakMagnitude`
- `rmsMagnitude`
- `crestFactor`
- `componentPeak = max(max(abs(I), abs(Q)))`

随后按下面这个比例缩放整段波形：

$$
scale = \frac{32767}{componentPeak}
$$

然后把该周期缩放后的 I/Q 写成 int16 交织数据，重复填充为 N 点输出。

### 8.2 这为什么等价于 AutoScale

这个口径的含义就是：

- 不是固定某个 RMS
- 不是固定某个单音功率
- 而是让整段波形的 I 或 Q 分量峰值尽量吃满 int16 动态范围，同时避免削顶

从产品语义上看，这就是“默认打开 AutoScale”。

### 8.3 这会带来什么结果

因为不同 `PhaseMode` 的 crest factor 可能不同，所以在 AutoScale 口径下：

- 峰值通常都会被顶到接近 `0 dBFS`
- 但平均功率不一定一样
- crest factor 越大，平均功率越低

因此，`Fixed / Random / Parabolic` 三种模式即使 tone 数相同，最终平均功率也可能不同。这不是 playback 改写了 generator，而是当前 AutoScale 口径的自然结果。

### 8.4 为什么 `Count = 2` 时 `Fixed` 与 `Parabolic` 功率不变，而 `Count = 3` 时 `Parabolic` 可能更高

这是当前实现和当前 AutoScale 口径共同作用下的**预期现象**，不应直接判为 bug。

先看 `Count = 2`。当前 `Parabolic` 相位律是：

$$
\phi_k = \pi \frac{k(k-1)}{N} + \phi_0
$$

当 `N = 2` 时，只有 `k = 0, 1` 两个 tone，它们的相位分别是：

$$
\phi_0, \phi_0
$$

也就是说，当前实现里 `Count = 2` 时，`Parabolic` 在数学上就直接退化成 `Fixed`。因此两种模式下：

- tone 相位完全相同
- waveform 完全相同
- `componentPeak` 完全相同
- `appliedScale` 完全相同

所以频谱仪上看到的 tone 功率不会变化，这是正确行为。

再看 `Count = 3`。此时当前 candidate lattice 为三个 tone，`Parabolic` 的相位不再退化成全相等，相对 `Fixed` 会改变 tone 之间的相位关系。对当前实现而言，这会通过两条路径影响最终频谱仪上看到的功率：

1. 它可能降低 waveform 的峰值占用，使 `componentPeak` 下降。
2. 当前 AutoScale 使用的是

$$
scale = \frac{32767}{\max(|I|, |Q|)}
$$

也就是按 I/Q 分量峰值吃满动态范围，而不是按固定 RMS 或固定总功率归一化。

因此当 `Parabolic` 让 `componentPeak` 比 `Fixed` 更小的时候，最终 `appliedScale` 会更大，整段 waveform 会被整体放大；一旦整段被整体放大，所有保留 tone 的线功率都会一起上升。

例如在 `Count = 3` 且保留全部三根等幅 tone 的典型场景下：

- `Fixed` 可写成 `1 + 2 cos(\omega n)`，当前 `componentPeak = 3`
- `Parabolic` 当前会把其中一根 tone 旋转到 `120°`，其 `componentPeak` 会下降到 `2`

因此当前实现下，`Parabolic` 的 AutoScale 会比 `Fixed` 多放大：

$$
20 \log_{10} \left(\frac{3}{2}\right) \approx 3.52\ dB
$$

这时频谱仪上看到每根 tone 更高，是当前 AutoScale 语义的自然结果，而不是 phase mode 算错了。

即便 `Count = 3` 但 notch 后只剩对称双音，`Fixed` 与 `Parabolic` 在连续时间信号语义上主要体现为时间平移和整体复旋转；但由于当前 AutoScale 采用的是 `max(|I|, |Q|)` 这种**与复平面旋转有关**的分量峰值口径，而不是旋转不变的 `|x|` 峰值或固定 RMS，实际 `appliedScale` 仍可能不同，因此频谱仪上线功率也仍可能出现小幅差异。

如果未来扩展 `per-tone magnitude`，这种现象会进一步一般化：

- phase mode 会改变 waveform 的峰值占用
- tone magnitude mask 也会改变 waveform 的峰值占用
- 只要系统仍保持当前 AutoScale 口径，最终线功率就仍会随 `phase set + magnitude set` 联动变化

因此，若未来产品希望做到“切换 phase mode 时 line power 尽量不变”，那需要调整的不是当前 `Fixed / Parabolic` 相位公式本身，而是归一化口径：例如改成固定 RMS、固定总功率，或引入独立于 phase mode 的 power normalization 选项。

## 9. 当前 preview 频谱口径

当前 HTRA multitone 预览不是把 UI 做成另一个生成器，而是直接基于 generator 当前产出的 IQ 计算 analyzer-style spectrum snapshot。

### 9.1 当前 FFT 参数

当前预览频谱的主要口径是：

- `FFT size = 512`
- `Average count = 1`
- `Display points = 512`
- `Video smoothing bins = 1`
- Blackman-Nuttall window
- `RBW factor = 1.8851`

因此当前风格是刻意向 VSG60 的尖锐谱线靠拢，而不是沿用 earlier digital preview 那种较重的平滑口径。

### 9.2 为什么 odd count 不再画成平线

当前预览输入不是“短周期数据 + 大量补零”，而是对当前逻辑周期做循环展开：

$$
sourceIndex = (segmentStart + index) \bmod complexSampleCount
$$

这样做的目的，是让 analyzer FFT 看到的是一个真正的周期序列，而不是一段一次性短脉冲。对 odd `Count`、极短周期的 multitone 预览，这一点尤其关键。

### 9.3 当前 Waveform Details 的功率解释

当前预览窗口里的 `Waveform Details` 是基于 generator metrics 和当前 AutoScale 口径解释出来的：

- `Peak Power` 按 `0 dBFS` 展示
- `PAPR = 20 log10(crestFactor)`
- `Average Power = -PAPR dBFS`

这不是单独再次测了一遍频谱平均功率，而是与当前“峰值已被 AutoScale 顶到满幅”的量化口径保持一致。

## 10. 当前与 VSG60 的一致点

当前实现已经与 VSG60 对齐的核心点，主要在“语义”而不是“逐样本导出完全一致”。

### 10.1 Tone lattice 语义对齐

当前实现已经明确：

- odd / even `Count` 的 tone lattice 如何构造
- `NotchWidth` 如何在逻辑 center 挖空，并在偶数 mask 模式下补偿 FixedPlayback center offset
- 不论来自 notch 还是离散保留音，`0 tone` 都被定义为静默 playback waveform，而不是生成失败或空 payload

当前软件固定采用中心音遮盖：偶数 `Count` 的基带 tone 集会故意偏离 VSG60，RF tone 位置经设备中心补偿后回到逻辑位置。奇数 `Count` 保持原有对称整数 lattice。对齐应区分逻辑 RF tone 集与基带 IQ 排列，不能通过 JSON 关闭该策略。

### 10.2 Phase mode 语义对齐

当前 `Fixed / Random / Parabolic` 已分别表达：

- 等相位多音
- 带 seed 的可复现独立随机相位多音
- 按 candidate lattice 稳定序号定义的抛物线相位多音

这已经覆盖了对标 VSG60 所需的主相位模式语义。

### 10.3 Playback 语义对齐

当前 generator 先定义波形，再把 IQ 结果交给后续 playback/download 链路。后续链路的职责应是搬运和下发，不应重新定义 tone、phase、notch 或 AutoScale 语义。

因此只要后续链路不重采样、不重相位、不重归一化，就应把当前 generator 的多音语义原样带到设备侧。

## 11. 当前与 VSG60 允许存在的差异

下面这些差异当前不应直接判为 bug。

### 11.1 Raw IQ 不必逐点一致

即使两边表示的是同一个业务语义的 multitone，只要下面任一项不同，raw IQ 就可以完全不同：

- 采样率不同
- 导出段长不同
- 绝对相位参考不同
- RNG 实现细节不同

因此多音是否正确，不能只靠“逐样本比较导出文件”来判断。

### 11.2 VSG60 更可能导出逻辑最小周期

VSG60 的导出结果往往更接近逻辑最小周期，而 SGStudio 当前更强调“满足设备采样率约束并尽量 exact-period 闭合的 playback 段”。

所以两边导出长度不同，本身不是问题。

### 11.3 固定中心音遮盖策略会故意偏离 VSG60

内部中心音遮盖开关固定为 true，偶数 `Count` 的 lattice 会整体偏移 `-(FreqSpacing / 2)`，让一根候选 tone 落到设备基带 DC。FixedPlayback 下设备中心频率下发为 `logicalCenter + FreqSpacing / 2`；Playback F/L/MScan 也使用相同补偿，让最终 RF tone 位置保持逻辑语义。notch 选音使用相反坐标补偿，继续围绕 `logicalCenter` 对称。奇数 `Count` 不增加设备中心偏移。该策略用于遮盖中心频率本振泄露，不能通过 Preset 或配置文件关闭。

### 11.4 Random 不必与 VSG60 bit-exact 同 seed

当前 `Random` 的正确性标准是：

- 同一实现内可复现
- 每根 tone 独立随机相位
- seed 真正参与生成

而不是必须复制 VSG60 内部 PRNG 的每一个实现细节。

### 11.5 Parabolic 可能只差一个全局复旋转

对对称双音或更一般的复基带多音来说，若只差一个全局绝对相位参考，两边 raw IQ 的朝向可以不同，但 tone 的位置和相对相位模式仍然可以是同一类信号。

## 12. 当前哪些现象才算真正的错误

与上面相反，下面这些现象如果出现，才说明当前实现真的出了问题：

1. active tone 集与目标参数语义不一致。
2. 逻辑 center notch 位置不对，或者在 center offset 模式下删错了 tone。
3. `Fixed` 实际没有做到等相位。
4. `Random` 实际没有做到逐 tone 独立随机，或者相同 seed 不能复现。
5. `Parabolic` 实际没有保持 candidate lattice 上的相对相位律。
6. 当前 `sampleRate` 与 `sampleCount` 不能形成足够好的闭合周期，导致循环播放边界出现额外谱线。
7. 后续 playback/download 链路重新改写了 generator 的 tone、phase 或幅度语义。

## 13. 当前边界与未实现项

当前实现已经覆盖了主要多音语义，但还没有扩展到所有可能功能。

当前仍未覆盖或未暴露的方向包括：

- per-tone magnitude 向量：若未来加入，需要同步定义其与 target-aware DC cancellation、AutoScale 以及 line power 解释之间的关系
- correction / predistortion
- channel mapping
- random-no-seed 暴露
- 显式 tone 向量输入
- 非 DC 中心连续 notch 参数
- 更完整的 rounding warning / UI 提示

因此当前文档不应被理解成“已经完整复刻 iqtone 或 VSG60 的全部外壳能力”，而应理解成“当前 htra multitone 在 SGStudio 内部的实际、稳定、可维护实现边界”。

## 14. 结论

当前 htra multitone 可以概括成下面这条主线：

1. 先把 `Count` 收口到产品范围 `2..1024`，并把 `FreqSpacing` 收口到不低于 `1 kHz`；再生成整数倍间距的 baseband tone lattice。奇数 Count 关于 DC 对称，偶数 Count 采用包含 DC 的半间距平移排列。
2. 中心音遮盖只保留固定 true 的内部常量，不是 profile 或 JSON 参数。偶数 `Count` 的基带排列配套设备中心 `+FreqSpacing/2` 补偿，正常启动、Preset 和旧配置加载使用同一策略。
3. 根据当前模式，要么用 `NotchWidth` 在逻辑 center 做 lattice-aware notch（偶数 Count 下先把基带频偏加上 `FreqSpacing / 2`），要么用 `EnabledToneIndices` 直接筛出离散保留音；两条路径都允许 active tones 为空，对应 silent IQ waveform。奇数 Count 保持原有基带排列，不在选音后额外平移。
4. 用 `Fixed / Random / Parabolic` 定义每根 active tone 的相位；当 active tones 为空时，相位分配自然退化为空集。
5. 先用

$$
F_{s,lower} = 1.25 \cdot 2 \cdot \max(|f_{min}|, |f_{max}|)
$$

建立采样率下限；对完整奇数 lattice，它等价于 `1.25 * (Count - 1) * FreqSpacing`；对完整偶数 lattice，它等价于 `1.25 * Count * FreqSpacing`。business 先尝试当前组合，优先选择 `fundamentalStep` 整数倍，也允许 current capacity 内可精确闭合的有理周期边界；无 plan 时保持 Count、优先降低 FreqSpacing，只有 1 kHz 仍不满足才降低 Count。
6. 先求最小 exact-period 周期，再把最终 `sampleCount` 选成满足 playback 最小段长和目标 RBW 的最小工程友好 exact-period 段。
7. 先按 N 点坐标的 natural bin 合并同 bin tone，再用 `gcd(N, bins)` 约简为 P 点共同周期。P 为 2 的幂时在唯一 P 点工作区原地 IFFT，否则直接合成 P 点，然后按当前参数语义做目标感知的残余 DC 消除；主要驻留内存为唯一最终 IQ 加 P 点浮点工作区，即约 `4N+16P` 字节。
8. 按

$$
scale = \frac{32767}{\max(|I|, |Q|)}
$$

对周期做统一缩放并量化成 int16 IQ，再通过可取消的分块复制填充 N 点，这等价于默认开启 AutoScale。
9. 用 512 点、单段、弱平滑的 analyzer 口径生成当前频谱预览；离散音表格按相对逻辑 center 的频偏从低到高展示，偶数 mask 模式只在表格显示层补回 `FreqSpacing / 2`，不改写实际 IQ 频偏。
10. 异步生成按 generation revision 执行 cooperative cancellation；业务先使旧 revision 失效并禁用 Save IQ，再同步一次前端 table，只有最新 revision 可以进入 ready。

因此，当前实现的正确性应主要按 tone 集、notch、phase mode、exact-period 闭合和 playback 语义保持来判断，而不是按是否与 VSG60 的导出文件逐点完全相同来判断。
