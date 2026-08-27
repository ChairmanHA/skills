# 连续采样率规则迁移计划

## 背景

- 旧实现把硬件采样率建模为三段离散合法区间，并在多个路径中显式写入 `DATA_MIN_2` / `DATA_MIN_3` / Gear 1/2/3 语义。
- 新业务规则变更为：
  - 通用采样率范围：`125M / 1024 ~ 125M`
  - Streaming 模式最大采样率：`62.5M`
  - 不再存在三档离散断档与“就近吸附到档位起点”的约束。

## 代码改造范围

1. 公共采样率 helper
   - `src/libs/utils/constants.h`
   - `src/libs/utils/constants.cpp`
   - 目标：把“档位合法性 / 回写”改为连续区间钳位，并补充 Streaming 上限常量。

2. HTRA 主线路径
   - `src/plugins/htra/streamingbussiness.cpp`
   - `src/plugins/htra/arbmodulation.cpp`
   - `src/plugins/htra/arbdatagenerator.cpp`
   - `src/plugins/htra/arbpanel.cpp`
   - 目标：移除 Streaming 手写断档吸附；统一边界到连续区间；修正用户可见 tooltip。

3. Analog 采样率算法
   - `src/plugins/analog/packing.cpp`
   - `src/plugins/analog/ofdmpanel.cpp`
   - 目标：去掉 Gear 1/2/3 搜索与第 1 档兜底；改成连续区间搜索，同时保留 62.5M 的 Streaming 语义。

## 实现原则

- 公共合法性判断：只认连续区间 `[125M/1024, 125M]`。
- `writeBackSampleRate()` 在连续规则下退化为普通钳位，不再做“就近档位”回写。
- Streaming 特有上限由单独常量承载，避免继续借用旧的 `DATA_MAX_1` 语义。
- size 降级策略仍保留，但目标从“降到第 1 档”改为“降到不超过 62.5M 的连续区间”。

## 风险点

- Digital / DSSS 的 oversample 可选项会因 `checkSampleRate()` 连续化而自动放宽，需要确认这正是期望行为。
- AM / FM / Pulse / Ramp 原先依赖档位边界做阶段性回退；改成连续区间后，必须保住整数周期 / 整数倍约束，不可只做简单钳位。
- HTRA Arb/Streaming 的 tooltip 与 metadata 若不一起改，会出现“实际可输连续值，但 UI 仍提示三档”的割裂。

## 验证

- 以静态编译/语义校验为主，确认 active 代码无新增错误。
- 不触碰 `.github/KnowledgeBase` 文档，由用户自行处理文档同步。