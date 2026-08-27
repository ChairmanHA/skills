# 2026-06-01 DSSS SymbolLength Quality Plan

## 背景

- 当前 `src/plugins/analog/dsssmodulator.cpp` 在 DSSS 波形生成时，把 `symbolLength` 写死为 `32000`，只有超出 `MAXDOWNLOADSIZE` 时才向下截断。
- 默认参数来自 `DSSS_DeInit()`：`Rb=1e6`、`code=5`、`sps=4`、`span=8`、`filterType=RootRaisedCosine`。
- 用户实测：默认参数下把 `symbolLength` 从 `32000` 改成 `1024` 后，波形质量明显更好。

## 局部假设

1. 现有 `32000` 只满足“大小不超限”，没有反映 DSSS 作为循环 ARB 记录的工程边界。
2. DSSS 记录时长满足 `T = symbolLength / Rb`：
   - 默认 `32000` 对应 `32 ms`
   - 默认 `1024` 对应 `1.024 ms`
3. 对带成型滤波的 DSSS，记录只要明显长于滤波收敛区，就已经能稳定体现稳态统计特性；继续把随机记录拉长到 `32 ms` 往往只会增加不必要的记录长度、循环边界不确定性和工程代价，并不保证质量继续提升。

## 方案

- 在 `packing` 层新增 DSSS 专用 `symbolLength` 选择 helper，统一 DSSS 的生成口径。
- 选择规则：
  - 最小有效长度：`max(256, 16 * span)`，保证滤波收敛区之外仍有足够稳态符号。
  - 目标记录时长：约 `1.024 ms`，即 `targetSymbols = round(Rb * 1.024e-3)`。
  - 最大长度：`floor(MAXDOWNLOADSIZE / bytesPerSymbol)`。
  - 工程量化：收口到 `32` 个 symbol 的粒度，避免奇怪长度。
- `dsssmodulator` 的同步/异步两条 `GenerateDssWaveform(...)` 路径都改为调用同一个 helper。

## 预期结果

- 默认参数下自动选出 `1024` 附近的记录长度，而不是固定 `32000`。
- 当 `Rb`、`span`、`code`、`sps` 改变时，`symbolLength` 会随参数自动调整，同时仍受 `MAXDOWNLOADSIZE` 保护。