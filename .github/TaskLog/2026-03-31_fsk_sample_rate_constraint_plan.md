# FSK 数字调制采样率约束实现

## 背景

- 当前数字调制 packing 层的采样率约束只有 `Fs >= sps * Rb`，并通过 `Utils::checkSampleRate()` / `Utils::writeBackSampleRate()` 适配硬件档位。
- FSK 目前仅对 `MaxDF` 做 `1 <= MaxDF <= 15 * Rb` 的区间钳位，没有把频偏带来的额外采样率需求纳入约束。
- 实际生成链里，传给调制库的采样率就是 `sampleRate = Rb * sps`，因此新约束不能只改注释或文档，必须同时落到 packing 与 UI/Modulator 的 oversample 合法性判断中。

## 目标

- 为 FSK2 / FSK4 / FSK8 / FSK16 增加采样率约束：`Fs >= 4 * (MaxDF + Rb)`。


## 设计

- packing 层：
  - 非 FSK 保持现有逻辑不变，仍按 `sampleRate = Rb * sps` 
  - FSK 仍保留 `Rb` 与 `sps`。
  - 先按现有规则把 `MaxDF` 钳位到 `1~15*Rb`，再额外用 `125M` 上限推导可支持的最大 `MaxDF`，优先下压 `MaxDF`。
  - 实际用于生成波形的 `sampleRate` 单独计算：取满足 `Rb*sps` 与 `4*(MaxDF + Rb)` 的最小合法值；
- Modulator/UI 层：
  - `sampleRate` 展示值与实际下发值改为复用统一的数字调制采样率计算函数，确保 FSK 显示与生成一致。
- 文档：
  - 更新数字调制约束文档，补充 FSK 下 `Fs >= 4 * (MaxDF + Rb)` 以及“优先压 MaxDF、sampleRate 单独抬档”的行为。

## 验证

- 对修改文件做静态检查，确认没有引入编译错误。
- 重点检查以下路径的参数回写一致性：
  - 默认初始化进入 FSK。
  - 修改 `SymbolRate` 后自动同步 `MaxDF`。
  - 手动修改 `MaxDF` 后 sampleRate 抬档与 packing 钳位。