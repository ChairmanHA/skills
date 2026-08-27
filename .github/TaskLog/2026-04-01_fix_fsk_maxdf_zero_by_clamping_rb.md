# FSK 在连续采样率模型下的 MaxDF/Rb 约束修复

## 问题

- 当前 `Digital_Configuraion()` 对 FSK 先按 `15 * Rb` 和 `125M / 4 - Rb` 限制 `MaxDF`。
- 当用户输入的 `Rb` 已经过大，导致 `125M / 4 - Rb <= 0` 时，`MaxDF` 会被写回 `0`。
- 这违反了当前业务约束里 `FSK MaxDF >= 1` 的要求，并且会把后续算法带入非法状态。

## 目标

- 在连续采样率模型下保持 FSK 约束 `Fs >= 4 * (Rb + MaxDF)`。
- 当 `Rb + MaxDF` 超出 `125M / 4` 时，不再把 `MaxDF` 压成 `0`。
- 对 UI 默认联动场景（修改 `Rb` 时 `MaxDF` 自动跟随 `Rb``），优先同步缩小 `Rb` 与 `MaxDF`，而不是只保留其中一个参数。
- 保持非 FSK 分支和现有 UI 回写闭环不变。

## 设计

- 在 `packing.cpp` 增加 FSK 约束归一化辅助函数：
  - 先统一钳位 `Rb >= 1`、`MaxDF >= 1`。
  - 先满足硬约束 `MaxDF <= 15 * Rb`。
  - 若 `Rb + MaxDF` 超出 `125M / 4`，则按统一比例同步缩小 `Rb` 与 `MaxDF`，尽量保留二者的相对关系。
  - 若缩放后某一项小于最小值，则把该项抬回最小值，再把另一项压回预算范围内。
  - 最终再次收口 `MaxDF <= 15 * Rb`，并保证 `Rb >= 1`、`MaxDF >= 1`、`4 * (Rb + MaxDF) <= 125M`。
- `Digital_Configuraion()` 与 `calculateDigitalSampleRate()` 复用同一归一化逻辑，确保回写参数、显示采样率和生成采样率一致。

## 验证

- 静态检查 `packing.cpp` / `digitalmodulator.cpp` 的回写链是否仍然通过 `param2Profile()` 同步到 UI。
- 运行文件级错误检查，确认没有引入语法或类型错误。

## 补充说明：FSK 的工程推荐值

- `MaxDF >= Rb` 适合作为当前产品通用 FSK 的默认/推荐值，但不应写成理论硬约束。
- 原因：
  - 对未知接收机、频率判决器、非相干接收或实验室联调场景，更大的频偏通常意味着更好的频率可分辨性和更稳健的判决裕量。
  - 当前 UI 已在 FSK 下实现“修改 `symbolRate` 时自动同步 `fskDeviation = symbolRate`”；这更适合作为保守缺省值。
  - 但行业中很多窄带或频谱效率优先的 FSK / GFSK / CPFSK 体制会故意选择 `MaxDF < Rb`，因此该建议只能写成工程推荐，不能升级为协议级硬约束。