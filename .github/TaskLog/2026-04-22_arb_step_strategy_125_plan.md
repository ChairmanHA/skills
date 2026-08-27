## 背景

- 当前软键盘在未显式配置 `metadata()->step()` 时，公共默认步长来自 `|value| / 10`。
- 这个默认对大范围离散参数不合适，尤其是 `decimals = 0` 的计数/长度类属性：
  - 例如 `Arb_Period = 1` 时，内部默认步长会变成 `0.1`。
  - 因为 property 最终是整数语义，用户需要多次点击才能从 `1` 到 `2`，交互明显失真。
- 对射频信号源这类跨度很大的参数，固定步长也不适合；更自然的交互是数量级自适应的 `1-2-5` 步进。

## 设计判断

- 这次不把 `125` 变成所有参数的强制默认值，而是恢复成一个可选的公共 `stepStrategy`。
- 选择策略的优先级：
  1. 显式 `metadata()->step()`：继续走固定步长。
  2. 显式 `metadata()->property("stepStrategy")`：使用对应步进控制器。
  3. 都没有时：仍保留固定步长控制器，但默认 seed 至少不低于显示精度量级，避免整数参数出现 `0.1` 之类不可见步长。
- `stepEditable` 场景优先保持“可编辑固定步长”语义，不和 `125` 自动策略混用。

## 落地方案

- 在 `NumericKeyboardConfig` 增加 `stepStrategy`。
- 在 `prepareNumericKeyBoard(property, triggerObj)` 中把 metadata 的 `stepStrategy` 透传到公共键盘组装层。
- 在 `createKeyboardBase()` 中：
  - 若 `stepEditable=false` 且 `stepStrategy=="125"`，改用 `Step125Controller`。
  - 默认 seed 改为“显式 step 或显示精度量级或 `|value|/10` 中较合理的一个”，避免整数参数默认出 `0.1`。
- `Step125Controller` 对 `value == 0` 补一个 seed fallback，保证从 0 也能步进出去。
- 先把 `Arb_Period` / `Arb_SampleOffset` / `Arb_SampleToUse` 接到 `125` 策略上。

## 预期交互

- `Arb_Period = 1` 时，步进下一次直接到 `2`，再到 `5`、`10`。
- 大范围参数不再依赖难以预测的 10% 相对步长，而是按数量级自然扩展。
- 需要固定细步长的参数仍继续显式 `setStep(...)`，不被本次改动影响。