# 2026-05-19 Multitone VSG60 状态与下一阶段

## 目标

Multitone 目标收敛为更接近 VSG60 的产品语义：

- `Fixed / Random / Parabolic` 三种 phase mode
- `Seed` 驱动的可复现随机相位
- `Notch Width` 的真实 tone-lattice 挖空语义
- 与当前仓库一致的 sample rate clamp、保存恢复、property/UI 绑定和 playback 交付链路

## 已实现

### 1. 参数模型

当前 generator 已具备以下参数：

- `PhaseMode`：`Fixed / Random / Parabolic`
- `FixedPhaseOffset`
- `Seed`
- `NotchWidth`
- `Count`
- `FreqSpacing`

### 2. 与 iqtone / VSG60 主目标一致的部分

- `phase mode` 已做成显式参数，不再复用单个 `TonePhase`
- `Random` 已改为显式 `Seed` 驱动的可复现随机相位
- `Parabolic` 已按 Schroeder family 二次相位律实现
- `Count` 按对称 baseband tone lattice 生成 candidate tone 集
- `NotchWidth` 当前固定定义为“以 DC 为中心的对称 tone-lattice 挖空带宽”
- `effective tone count >= 2` 已在 generator 侧守住
- `SampleRate` 已按当前仓库规则实现：

$$
F_s = clampSampleRate\left(2 \cdot FreqSpacing \cdot (Count - 1)\right)
$$

- `numSamples == 0` 的自动求段长目标已接入，当前使用本地 rational solver 自动求可闭合周期长度
- 生成主路线已收敛为“频域落 bin + 逆变换”；如果样本数刚好是 $2^n$，走本地 IFFT，否则回退到通用逆变换
- 归一化/缩放已与 `iqtone.m` 的主边界对齐：按 `max(abs(I), abs(Q))` 做最终缩放，而不是按复包络半径缩放

### 3. 当前结果对象已暴露的信息

当前 generator 已能给出：

- `sampleRate`
- `sampleCount`
- `effectiveToneCount`
- `effectiveNotchWidth`
- `peakMagnitude`
- `rmsMagnitude`
- `crestFactor`
- `appliedScale`
- `maxFrequencyError`
- `binCollisionCount`
- resolved tone list（请求频率、实际频率、相位、bin）

## 还没做

### 1. 配置收口

当前还没有做完整的 packing / writeback 规则：

- `Seed` 的 UI/保存边界
- `NotchWidth` 与 `Count / FreqSpacing` 的联动收口
- 超大 notch 请求时的 UI 级反馈
- `FixedPhaseOffset` 是否需要重新作为独立可编辑参数暴露到 panel

### 2. iqtone 中有、当前没做的部分

当前还没有实现以下能力：

- `magnitude` 向量输入与 per-tone amplitude
- `correction` / predistortion
- `channelMapping`
- `random-no-seed`
- dialog 级别的 rounding warning；当前只记录 `maxFrequencyError` 与 `binCollisionCount`

## 下一阶段

### 2026-05-19 补充修正：Parabolic 相位索引基准

- 当前 htra `Parabolic` 的相位分配仍按 `notch` 之后的紧凑 tone 序号与 `effectiveToneCount` 计算。
- 这会把被 notch 删除的 tone 从相位序列中一并抹掉；在 `count=3` 且 notch 去掉 DC 的场景下，会把 `Parabolic` 退化成与 `Fixed` 同类的 `[0, 0]` 相位。
- 本轮做最小修正：保留 pre-notch candidate lattice 的稳定序号，并以原始 candidate count 作为 `Parabolic` 分母；notch 只负责过滤 tone，不再重排 `Parabolic` 的相位基准。
- 本轮不改 sample rate、notch 判定、Random/Fixed 相位语义，也不改 playback/download 链路。

### 2026-05-19 补充修正：generator canonical profile 回写

- 当前 htra `MultitoneGenerator` 在生成时会使用 sanitize 后的局部 profile，但成员变量仍可能保留用户原始输入。
- 这会导致 UI/property 层通过 getter 或 change signal 读到的是“编辑值”，而不是真正参与生成的“生效值”。
- 典型场景是 `Count` 与 `FreqSpacing` 存在交叉约束：当 `Fs = clampSampleRate(2 * FreqSpacing * (Count - 1))` 触碰硬件上限时，合法 `FreqSpacing` 需要随 `Count` 一起收敛。
- 本轮方案对齐旧 analog multitone 的 `Multitone_Configuraion` 语义：generator 内部统一持有 canonical profile，并在 setter / restore / reset 后把收敛结果写回自身，再由既有 property 绑定把真实值回写到界面。
- 本轮不新增 panel 控件，不改 playback provider，只修正 generator 与 property/UI 之间的 writeback 边界。

### Phase 1. 保存恢复与运行时验证

- 新 profile 保存恢复回看
- `Tone Phase` 三种枚举在实际设备下的行为验证
- `Seed / Notch Width / Count / FreqSpacing` 的组合验证

### Phase 2. 细节补齐

- 评估是否需要把 `FixedPhaseOffset` 放回 UI
- 评估是否需要物理删除 analog / htra 目录下已退出编译链的旧 multitone 源文件
- 补必要的联动回写和提示

最少需要验证：

- 相同 `Seed` 下逐样本一致
- `Fixed / Random / Parabolic` 的 crest factor 差异
- odd/even `Count` 下 tone lattice 正确性
- `NotchWidth` 的量化后实际 hole 宽度
- save/restore 后结果一致

## 当前无需做

以下内容和本轮“对标 VSG60 多音主能力”关系不大，当前不建议扩：

- `correction` / predistortion
- `channelMapping`
- real-only / I-only / Q-only 输出模式
- `random-no-seed` 的用户级暴露
- per-tone magnitude 外部 UI
- 完整复刻 `iqtone.m` 的参数解析/GUI 壳层

## 当前结论

- 当前主路线仍然成立：**在 SGStudio / htra 本地接管 multitone 合成，而不是扩 `3rdParty/modulation` ABI**
- 当前已完成的是“本地 generator + htra property/UI/business 接线 + current playback provider 对接”
- analog multitone 已不再参与编译、property 创建和 business 注册
- 当前还没完成的是“保存恢复回看 + packing/联动收口 + 运行时验证”
- `NotchWidth` 目前已明确固定为 DC 中心 notch；若未来产品要求非 DC notch，再单独引入 `NotchCenter`

## 2026-05-19 临时对比测试切换

- 当前 htra multitone generator 的量化口径等价于默认打开 `AutoScale`：在写出 int16 IQ 前，会按整段波形的 `max(abs(I), abs(Q))` 做满幅缩放。
- 为便于用户对比旧 analog multitone 与当前 htra generator 的差异，本轮临时恢复 analog multitone 的 CMake / property / business 接线。
- 同时临时注释 htra plugin 里的 multitone property 创建与 business 注册，但保留 `src/plugins/htra` 下 multitone 源文件继续参与 CMake 编译，避免丢失后续对比基线。
- 本轮目标是“入口切换用于 A/B 对比”，不是回退 htra multitone 算法文件本体；算法源码仍保留在 htra 目录中。

## 2026-05-19 文档产出

- 本轮新增一份 KnowledgeBase 文档，汇总 fixed / random(seed=23) / parabolic 三组与 VSG60 的对比结论，以及当前 SGStudio playback/download 链路为何能保持生成器语义。
- 文档需要明确列出本轮实际读取的参考文件与生成文件，避免后续再把“对话附件预览”和“磁盘上实际被读取的文件”混为一谈。
- 用户后续又对文档做了收缩编辑，因此本轮需要再补一版“只谈当前实现、不回顾历史修正”的详细说明，把三组对比中“raw IQ 有差异但仍然正确”的理由写成可复核的信号级论证。
- 这一版文档会补足两条主线：一条是 generator 与 playback/download 的职责边界；另一条是 Fixed / Random / Parabolic 三组在 tone 集、notch、相对相位律和 sample-rate 解释下为何仍与 VSG60 对齐。

实际读取的参考数据目录：

- `C:\Users\jsl\Documents\SignalHound\vsg60`
- `D:\development\vsg2.0\build\Qt_5_15_9_msvc2022_64-Debug\data`
