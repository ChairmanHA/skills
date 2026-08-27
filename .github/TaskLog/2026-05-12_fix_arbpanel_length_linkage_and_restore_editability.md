# ArbPanel 时长联动与恢复可编辑性修复

## 现象

- Playback 面板中的 PeriodLen / SignalLen 没有分别跟随 `Arb_Period`、`Arb_SampleToUse`、`Arb_SampleRate` 联动，当前实现错误地统一使用 `Arb_SamplesInFile / Arb_SampleRate`。
- 加载文件后切换语言并重启，`Arb_Period`、`Arb_SampleToUse`、`Arb_SampleOffset` 会变成不可编辑。

## 局部假设

- 第一个问题直接由 `arbpanel.cpp` 的派生显示逻辑导致，需要把长度显示改成基于当前 property 值实时计算。
- 第二个问题的根因在 `ArbDataGenerator::restoreSettings()`：它直接写内部成员并请求重算，没有走 `setFileName()` / `handleFile()` 和各 property setter，因此 `samplesInFile`、只读状态以及 UI 依赖的 property change 信号没有被完整恢复。

## 最小修改

- 在 `ArbPanel` 中增加统一的时长刷新函数，并把 `sampleRate` / `period` / `samplesToUse` 三个 property 都接入同一个刷新闭环。
- 将 `ArbDataGenerator::restoreSettings()` 改为通过已有 setter 恢复 profile，确保文件恢复、editable 状态、samplesInFile 和相关信号一起恢复。

## 静态验证

- 通过代码级检查确认：`PeriodLen = period / sampleRate`，`SignalLen = samplesToUse / sampleRate`。
- 通过恢复链路检查确认：语言切换后的 profile restore 会重新触发 `fileNameChanged`、`samplesInFileChanged` 和 editable 状态同步，而不是停留在构造阶段的默认只读态。