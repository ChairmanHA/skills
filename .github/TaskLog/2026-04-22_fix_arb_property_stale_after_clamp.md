# Arb 参数裁剪后 UI 残留旧值修复

## 现象

- 用户把 `period` 改为 `1` 后，理论上 `samplesToUse` 应同步压成 `1`。
- 实际界面仍显示旧的 `samplesToUse`，说明 UI property 显示值与 `ArbDataGenerator` 内部值可能已脱钩。

## 局部假设

- `ArbDataGenerator` 当前的裁剪逻辑本身是对的，但 `arbmodulation.cpp` 在 `editingFinished` 后没有强制把 property 回写为 generator 的最终值。
- 一旦用户输入一个越界值，而 generator 收口后的结果恰好等于“当前内部值”，setter 会直接 `return`，不会再发变化信号；这会导致 property 保留用户输入的非法显示值。
- 之后再修改其他联动参数时，如果内部值没有进一步变化，UI 就不会收到新的刷新信号，于是看起来像“联动失效”。

## 最小修复

- 在 `sampleOffset` / `samplesToUse` / `period` 的 `editingFinished` 回调里，调用 generator setter 后，立即把对应 property 回写成 generator 当前值。
- 这样即使 setter 因“裁剪后结果等于当前内部值”而不发信号，UI 也会被同步回合法显示值。