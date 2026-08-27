# Arb 截取与周期语义收口

## 问题

- 现有 `ArbDataGenerator` 把 `period` 约束成不小于 `samplesInFile`，这与信号源常见 ARB 回放语义不一致。
- 按 VSG60 文档，`sampleOffset` 是起始点，`samplesToUse` 是从该起点截取的有效波形长度，`period` 是最终周期长度；只有当 `period > samplesToUse` 时才需要在尾部补零。
- 因此真正的核心约束不是 `period >= samplesInFile`，而是 `sampleOffset + samplesToUse <= samplesInFile` 且 `samplesToUse <= period`。

## 设计

- 在 `ArbDataGenerator` 内统一约束：
  - `sampleOffset` 只表示起始点，不因 `samplesToUse` 或 `period` 的变化被反向改写。
  - `samplesToUse` 是可裁剪的有效长度，受文件剩余长度与 `period` 双重上限约束。
  - `period` 只表示一个周期的总长度，允许小于整文件长度，但不能小于 1。
- 当 `period` 缩小时，只收缩 `samplesToUse`；不改 `sampleOffset`。
- 当 `sampleOffset` 增大导致剩余样本不足时，只收缩 `samplesToUse`；不改 `period`。
- `handleData()` 内再做一次防御性收口，确保即便外部传入组合不一致，也只会得到“截取后补零”的结果，不会越界。

## 结果

- 普通 wav 路径现在更接近信号源软件的用户心智：
  - 先从文件里截取 `[sampleOffset, sampleOffset + samplesToUse)`。
  - 再按 `period` 决定是否补零到一个完整周期。