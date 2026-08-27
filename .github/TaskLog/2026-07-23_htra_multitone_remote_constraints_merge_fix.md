# HTRA Multitone Remote Constraints Merge Fix

## Scope

- 修复 `MultitoneModulation::remoteEditorState()` 对已从算法层删除的
  `MultitoneGenerator::maxCountForFreqSpacing()` /
  `MultitoneGenerator::maxFreqSpacingForCount()` 的遗留调用。
- 保持设备能力决策位于 `MultitoneModulation` 业务层，不恢复 generator 的设备能力接口。
- 本轮只做静态验证；按用户要求不编译。

## Evidence

- `MultitoneGenerator` 当前声明中不存在上述两个静态接口。
- `multitonemodulation.cpp` 匿名命名空间已经提供接收
  `maximumSampleRate` 的业务层约束函数，`resolveAndGenerate()` 也在使用它们。
- `7c117252` 删除了 generator 的旧接口并把约束上移到业务层，但此前加入的
  remote editor 状态仍保留旧调用，属于合并后的遗漏。
- 当前 KnowledgeBase 明确：`MultitoneModulation` 负责设备能力约束，
  `MultitoneGenerator` 只负责设备无关的数学要求和波形生成。

## Implementation

1. 在 `remoteEditorState()` 中取得 current playback capability；无可用设备快照时沿用
   `GeneratedPlayback::effectiveCapabilities()` 的既有 fallback。
2. 使用业务层本地约束函数和 capability 的最大采样率计算
   `countMaximum` / `freqSpacingMaximum`。
3. 静态确认 HTRA 中不再调用 generator 上已删除的两个接口，并执行 diff whitespace 检查。

## Success Criteria

- `multitonemodulation.cpp` 不再引用不存在的 generator 成员。
- remote editor 的两个最大值继续基于当前设备能力计算。
- 不向算法层重新引入 `PlaybackCapabilities` 或等价设备依赖。
- 静态检查通过；不运行编译。

## Result

- `remoteEditorState()` 已通过 current playback capability 的最大采样率调用业务层
  `maxCountForFreqSpacing()` / `maxFreqSpacingForCount()`。
- HTRA 源码中已无对 generator 上两个已删除接口的调用。
- `git diff --check` 通过（仅输出工作区既有的 LF/CRLF 转换提示）。
- 按用户要求未编译、未运行。
