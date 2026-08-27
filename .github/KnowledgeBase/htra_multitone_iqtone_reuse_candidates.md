# HTRA Multitone 从 iqtone.m 仍可借鉴的点

本文档不把外部 `iqtone.m` 当作当前实现的强制规范，而是把它视为一个历史参考实现。目标是回答一件更具体的事：在当前 HTRA multitone 已经稳定收敛之后，原始 `iqtone.m` 里还有哪些机制值得未来继续借鉴，哪些点只应保留为参考而不应直接回抄。

## 1. 参考范围

本文结论基于下面这些输入：

- `C:\Users\jsl\Desktop\multitone\iqtone.m`
- `src/plugins/htra/multitonegenerator.h`
- `src/plugins/htra/multitonegenerator.cpp`
- `.github/KnowledgeBase/htra_multitone_current_algorithm_and_vsg60_boundaries.md`
- `.github/KnowledgeBase/multitone_vsg60_three_case_comparison_and_playback_consistency.md`
- `.github/TaskLog/2026-05-19_multitone_vsg60_algorithm_plan.md`

如果后续当前 generator 的参数模型或段长策略发生变化，应重新评估本文结论。

## 2. 当前已经吸收的核心思路

在评估“还要不要借鉴 `iqtone.m`”之前，先要避免把已经落地的东西误判成缺口。就当前仓库而言，下面这些主线已经被吸收，不建议再按旧实现回退：

- 当前主路线已经是“频域落 bin，再逆变换得到时域复基带”，与 `iqtone.m` 的合成方向一致。
- 当前已经具备 `Fixed / Random / Parabolic` 三种核心相位模式，其中 `Random` 为显式 `Seed` 驱动、同一实现内可复现。
- 当前最终缩放口径已经与 `iqtone.m` 的主边界对齐，即按 `max(abs(I), abs(Q))` 做统一缩放，而不是按复包络半径缩放。
- 当前 `sampleRate` / `sampleCount` 已经改造成“面向设备 playback 的 exact-period 合法段”，不再以旧式 ARB 段长规则为中心。
- 当前已经把 `Count`、`FreqSpacing`、`NotchWidth`、candidate lattice、DC-centered notch、Parabolic 相位索引基准等语义收敛到本地 generator 内部。

因此，后续真正值得借鉴的内容，重点不应是把 `iqtone.m` 原样复制进来，而应是提炼它里面仍有工程价值的扩展点和产品语义。

## 3. 仍值得未来借鉴的部分

## 3.1 优先级较高的借鉴点

### 3.1.1 Per-tone magnitude 向量

`iqtone.m` 支持 `magnitude` 向量，并通过 `fixlength()` 把较短输入重复扩展到 tone 数量。这个能力当前 HTRA 还没有，但它依然有明确的未来价值：

- 支持多音幅度包络、梳状谱整形、加权 tone set。
- 便于复现外部工具或历史测试数据里的非等幅多音场景。
- 若未来接入 imported tone template，`magnitude` 向量几乎是基础能力。

当前更建议把它理解成“高级 profile 能力”，而不是直接往现有 `Count + FreqSpacing` 的默认 UI 上堆字段。若未来要做，应优先保持：

- 默认面板继续走当前等幅 lattice 模式。
- 非等幅多音进入独立 advanced mode 或导入模式。
- 继续保留 `dB` 相对幅度语义，而不是改成难以对齐历史数据的线性比例输入。

### 3.1.2 显式 tone 向量输入

`iqtone.m` 的 `tone` 参数直接接受频率向量。这一点当前 HTRA 仍然没有，因为当前产品模型仍然是规则 lattice owner，而不是通用 tone list。

需要单独说明的是：当前 HTRA 已经新增了一个“离散保留音模式”，可以在现有 `Count + FreqSpacing` 生成的规则 candidate lattice 上逐个开关 tone，并满足最少两根 tone 的约束。这个能力已经覆盖了“只保留某些离散音”的工程需求，但它仍然不是 explicit tone vector。

但如果未来出现下面任一需求，`tone` 向量思路仍然值得借鉴：

- 不规则 multitone，而不是均匀 spacing 的对称 lattice。
- 从 CSV、脚本或测试模板导入指定 tone 列表。
- 需要表达“非 DC 中心连续 notch”或完全脱离规则 lattice 的 tone set。

这里最重要的结论不是“现在就改入口”，而是：如果以后真要支持不规则多音，最好新增一个独立的 custom-tone 模式，不要继续把当前 `Count / FreqSpacing / NotchWidth / 离散保留音` 语义硬拗成通用接口。

### 3.1.3 面向用户的 rounding warning

`iqtone.m` 在请求频率无法精确落到当前离散 bin 时，会给出明确 warning，而不是只在内部静默取整。当前 HTRA 已经能计算：

- `maxFrequencyError`
- `binCollisionCount`

但这些信息还主要停留在 generator metrics 层，没有形成用户可理解的提示。

这一点仍然非常值得借鉴，因为它直接影响用户对频谱差异的理解。后续如果要补，建议语义是：

- 只有在实际存在 bin 量化误差或 bin 冲突时才提示。
- 提示文案应使用当前产品术语，例如 `Count`、`FreqSpacing`、tone 数或采样率约束，而不是照搬 MATLAB 的 `Start/Stop frequency` 说法。
- warning 应与当前 preview / waveform details / save IQ 的信息口径一致，避免 UI 提示和实际导出行为脱节。

### 3.1.4 后端化的段长约束抽象

`iqtone.m` 里的 `arbConfig` 不只是一个旧接口参数，它实际上把几类后端约束集中管理起来：

- `defaultSampleRate`
- `minimumSegmentSize`
- `segmentGranularity`
- `maximumSegmentSize`

当前 HTRA 已经有自己的 playback 友好段长逻辑，因此不应直接搬回 `arbConfig` 的接口形状。但它背后的抽象仍值得保留：如果以后 multitone 要在多个设备、多个导出目标或多个 runtime 路径之间复用，最小段长、段长粒度、最大点数这些约束最好来自后端 capability/config，而不是散落成多个硬编码常量。

换句话说，值得借鉴的是“约束集中建模”这件事，而不是 `iqtone.m` 的参数外壳。

## 3.2 条件性借鉴点

### 3.2.1 `random-no-seed` 与额外 phase family

`iqtone.m` 除了 `Random`、`Parabolic` 之外，还支持：

- `random-no-seed`
- `zero`
- `increasing`
- 直接 phase 向量输入

其中：

- `zero` 当前已经等价于 `Fixed` 且 `FixedPhaseOffset = 0`。
- 统一固定相位也已经由当前 `Fixed + FixedPhaseOffset` 覆盖。

因此真正仍值得保留为 future option 的，是下面三类：

- `random-no-seed`：适合实验室、快速试波或“每次都生成不同 crest factor 样本”的工程模式。
- `increasing`：可作为低优先级的调试/分析预设，但产品价值明显低于 `Random / Parabolic`。
- 直接 phase 向量：只有在未来同时支持显式 tone 向量或 per-tone 编辑时才有意义。

结论是：这些内容可以保留为后续扩展库，但不建议现在就并入主面板。

### 3.2.2 任意 tone 集的通用 sample-count 求解思路

`iqtone.m` 在 `numSamples == 0` 时，会先做有理逼近，再通过分母公倍数和频率 gcd 推一个能闭合的段长；同时它还带了两个工程上很有价值的保护思路：

- 当分母公倍数增长过快时，主动停止继续放大。
- 在上限范围内再做 segment granularity 对齐和最大长度截断。

当前 HTRA 的 `sampleCount` 已经针对“规则 lattice + playback 友好 exact-period 段”做了专门优化，因此不建议直接改回 `iqtone.m` 那套旧逻辑。

但如果未来进入“显式 tone 向量 + 不规则频率集合”的模式，那么 `iqtone.m` 这套通用求解思路仍值得借鉴，尤其是：

- 不要无限追求巨大公倍数。
- 需要显式的增长保护和回退路径。
- 对齐粒度和硬上限应该在 solver 内部统一处理，而不是丢给上层 UI 猜。

### 3.2.3 `channelMapping` 的处理顺序

`iqtone.m` 里有一条很容易被忽略、但工程上很有价值的顺序约束：如果要做 I-only、Q-only 或 real/imag 抑制类输出，应该先把对应分量清零，再做后续 correction / normalize。原注释已经说明，原因是这样才能保证最终绝对幅度缩放口径仍然正确。

这意味着如果未来 HTRA 要支持：

- I-only / Q-only 导出
- 特定通道映射
- real-only 工程模式

那么值得借鉴的不是“把 `channelMapping` 参数原样搬进 UI”，而是保留这条处理顺序：

1. 先基于目标输出模式修正 I/Q 分量。
2. 再进入 correction 或其他后处理。
3. 最后再做统一归一化或量化。

### 3.2.4 correction / predistortion 的插入位置

`iqtone.m` 把 correction 放在波形合成之后、最终 normalize 之前。这一点仍值得保留，因为它明确表达了 correction 的职责边界：它是对“已经定义好的波形”做后处理，而不是反过来参与 tone lattice 或 phase mode 的定义。

如果未来 SGStudio 要引入 multitone 级 predistortion，这条边界可以直接沿用：

- tone、phase、notch 仍由 generator 主逻辑定义。
- correction 作为后处理阶段插入。
- 最终缩放与量化仍在 correction 之后完成。

但在当前阶段，不建议只因为 `iqtone.m` 里有这个选项，就把 correction 提前塞进 HTRA generator。先决条件应该是：

- correction 数据来源明确。
- owner 明确在设备/校准/后处理链路，而不是 UI 层临时拼装。
- 与当前 playback/download 责任边界不冲突。

### 3.2.5 normalize 开关

`iqtone.m` 允许关闭 normalize。这个点对当前主产品路径并不紧迫，因为当前 HTRA 的产品口径就是默认 AutoScale。

不过如果以后出现下面这些场景，这个思路仍然可参考：

- 导出给外部链路做二次幅度标定。
- 希望保留 correction 后的绝对幅度关系，而不是再顶满满幅。
- 做 purely analytical export，而不是直接面向设备回放。

结论是：`normalize` 更像 expert/export mode 选项，而不是当前标准 multitone 播放模式的必要参数。

## 4. 当前不建议直接照搬的部分

下面这些内容虽然存在于 `iqtone.m`，但当前不建议直接引入：

- MATLAB 风格 property/value 参数解析入口。
- `nargin == 0` 时弹 GUI 的壳层行为。
- 以旧式 ARB 段长规则为中心的 `numSamples == 0` 原样逻辑。
- 为了与外部工具完全一致而追求随机数 bit-exact。
- 在没有明确产品需求的情况下，把 `magnitude`、custom phase、channel mapping、correction、normalize 一次性堆到当前主面板。

这些内容的问题不在于“不能做”，而在于它们会把当前已经稳定的 HTRA multitone 参数模型重新拉回一个过度泛化、边界不清的状态。

## 5. 建议的后续采用顺序

如果未来确实要继续吸收 `iqtone.m` 的价值，建议优先级如下：

1. 先补 rounding warning，把当前已有 metrics 变成用户可理解的提示。
2. 再把段长约束、最小粒度、最大点数这些后端限制继续收敛成统一 capability/config 抽象。
3. 若出现真实业务需求，再新增 explicit tone list 或 per-tone magnitude 的 advanced mode。
4. 只有在输出路径、校准链路和 owner 已明确后，再评估 channel mapping 与 correction。
5. `random-no-seed`、`increasing`、custom phase vector、normalize toggle 应作为实验或专家能力，晚于前面几项。

这个顺序的核心原因是：前两项主要改善可解释性和架构可维护性，后几项才会真正扩大产品能力面。

## 6. 结论

当前 `iqtone.m` 里仍然值得借鉴的，不是它的 MATLAB 外壳，也不是把当前 HTRA 段长策略直接回退到旧 ARB 思路，而是下面这些更稳定的工程经验：

- 对高级多音场景，per-tone magnitude 与 explicit tone list 仍是最有价值的扩展方向。
- 对用户可解释性，rounding warning 仍然是当前最值得补的一项。
- 对架构边界，段长约束集中建模、channel mapping 的处理顺序、correction 的插入位置都值得保留。
- 对专家模式，`random-no-seed`、custom phase、normalize toggle 可以保留为将来选项，但不应抢在主路径之前引入。

因此，后续对 `iqtone.m` 的正确使用方式，不是“继续把它当成当前实现的规范源”，而是“把其中仍有长期价值的机制整理成受控的 future options，在有明确需求时再按当前仓库边界接入”。