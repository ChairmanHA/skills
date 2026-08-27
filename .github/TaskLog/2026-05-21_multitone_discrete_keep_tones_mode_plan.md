# 2026-05-21 Multitone 离散保留音模式方案

## 目标

在当前 HTRA multitone 的规则 lattice 模型上，新增一个“离散保留音模式”：

- 仍由 `Count + FreqSpacing` 定义完整 candidate tone lattice。
- 新模式下允许用户逐个开关离散音，本质上是“只保留某些离散音”。
- 最终有效 tone 数最少仍为 2。
- 新模式与当前 `NotchWidth` 互斥。
- 保持当前 playback/download 链路与 waveform owner 不变。

## Owner 选择

本轮不引入通用 explicit tone vector，不把当前 multitone 入口扩成 custom-tone 模式。

本轮 owner 仍收敛在 `src/plugins/htra/multitonegenerator.{h,cpp}`：

- `Count + FreqSpacing` 继续定义规则 lattice。
- generator 内新增 `discreteKeepToneMode` 与 `enabledToneIndices` profile 字段。
- panel 只负责编辑这两个字段，不在 panel 侧重复实现 tone lattice 规则。

这样可以复用当前：

- phase mode
- sample rate / sample count 求解
- 频域落 bin + 逆变换
- save / restore
- playback provider 交付

## 参数语义

### 1. `discreteKeepToneMode`

- `false`：沿用当前模式，`NotchWidth` 生效。
- `true`：进入离散保留音模式，`NotchWidth` 在生成时忽略，UI 上禁用编辑。

### 2. `enabledToneIndices`

- 表示当前规则 lattice 中允许保留的 candidate tone 序号集合。
- 存储口径使用 candidate lattice 的稳定 `latticeIndex`，而不是表格显示顺序。
- 存储类型先使用 `QStringList`，每项为十进制索引字符串，便于 QJson 保存恢复。

## Canonical 规则

1. 先按 `Count + FreqSpacing` 生成完整 candidate lattice。
2. `enabledToneIndices` 只保留合法、去重后的 `latticeIndex`。
3. 若 `discreteKeepToneMode == false`，则生成阶段忽略 `enabledToneIndices`，但保留它的值，便于再次打开模式时恢复上次选择。
4. 若 `discreteKeepToneMode == true` 且合法保留音少于 2 个，则自动补齐到 2 个：
   - 优先保留用户已选择的合法 tone。
   - 再按距离 DC 最近、左右对称优先的顺序补齐。
5. 表格默认全开。

## Tone 排序与表格展示

panel 新增：

- 一个 `SwitchButton` 作为模式开关。
- 一个 `QTableWidget` 展示完整 lattice。

表格列：

- 音序号
- 频偏
- On/Off checkbox

表格展示顺序：

- 按距离 DC 中心的绝对值从小到大排序。
- 当绝对值相同，负频率排在正频率前。

注意：

- 表格展示顺序仅用于 UI。
- 后端真实 owner 仍使用 candidate lattice 的稳定 `latticeIndex`。

## 生成链路修改

当前生成链路：

- build candidate tones
- apply DC notch
- compute phase / sampleRate / sampleCount

本轮改成：

- build candidate tones
- 若 `discreteKeepToneMode == true`，按 `enabledToneIndices` 过滤 candidate tones
- 否则按当前 `NotchWidth` 走 DC notch
- 后续统一按 active tones 继续 phase / sampleRate / sampleCount / synthesize

## Random 相位稳定化

当前 `Random` 仍按 active tone 顺序逐个取随机相位；这样删掉一个较前的 tone 会导致后续 tone 的随机相位整体平移。

本轮改为：

- `Random` 先按完整 candidate lattice 的稳定顺序生成整套随机相位。
- active tone 再按各自 `latticeIndex` 取相位。

这样离散开关某个 tone 时，不会导致其他 tone 的随机相位重排。

## SampleRate 下限口径

当前下限按 `1.25 * Count * FreqSpacing`。

本轮改成按 active tone span 计算：

$$
F_{s,lower} = clampSampleRate(1.25 \cdot (f_{max} - f_{min} + \Delta f))
$$

其中：

- `f_max` / `f_min` 来自 active tones
- `\Delta f = FreqSpacing`

对当前普通 DC-notch 模式，这个口径与现有外圈 tone 不变的情况保持一致；对离散保留音模式，关闭外侧 tone 后可以自然收敛 sample rate。

## UI / Property / Modulation 边界

本轮不强行把表格编辑塞进通用 PropertyBindingManager。

建议边界：

- `Count / FreqSpacing / NotchWidth / Seed / TonePhase` 继续使用现有 property 绑定。
- 新增离散模式开关与 tone table 由 `MultitonePanel` 直接发信号给 `MultitoneModulation`。
- `MultitoneModulation` 调 generator setter，并把 generator 当前 candidate snapshot 回推给 panel。
- save / restore 仍由 generator profile 负责。

## 最小验证

1. `Count=2/3/8/9` 时表格行数与频偏正确。
2. 打开离散模式时 `NotchWidth` 禁用，关闭时恢复。
3. 关掉外侧 tone 后 sample rate 会下降，且有效 tone 仍不少于 2。
4. 同一 `Seed` 下，只切换某一个 tone 的 on/off，不应导致其他保留 tone 的随机相位重排。
5. save / restore 后表格勾选状态保持一致。
6. Debug 编译通过，并能在现有 SGStudio 运行时打开 multitone 面板查看表格与预览变化。

## 2026-05-21 样式微调补充

- 当前表格视觉行高过大时，优先检查 `QTableWidget` 字体与 `QTableWidget::item` 的垂直 padding，而不是继续下调 `defaultSectionSize`。
- 本轮微调目标是让数据行实际高度收敛到约 45px，同时保持 QuickWaveform 风格的一致性。