# Waveform Parameters Constraints 文档重写计划

## 目标

- 将 `.github/KnowledgeBase/Waveform_Parameters_Constraints.md` 重写为“发射机技术手册”风格文档。
- 以当前 active 代码为唯一事实来源，彻底移除旧的“三档采样率 / Gear 1/2/3 / 档位空洞”表述。

## 必须反映的最新规则

- 通用合法采样率范围：`125M / 1024 ~ 125M`
- Streaming 推荐/受限范围：`125M / 1024 ~ 62.5M`
- `Utils::checkSampleRate()` 现在只校验连续区间，不再校验离散档位。
- `Utils::writeBackSampleRate()` 现在等价于连续区间钳位。
- `Utils::clampStreamingSampleRate()` 专门用于 Streaming 模式。

## 文档写法要求

- 使用技术手册体例：范围、术语、总规则、分波形章节、结论。
- 区分“公共平台规则”和“各波形专属规则”。
- 公式、上下限、默认值必须可直接对应到当前实现。
- 明确说明“当前版本不再采用三档离散采样率模型”。

## 需核对的代码来源

- `src/libs/utils/constants.{h,cpp}`
- `src/plugins/analog/packing.cpp`
- `src/plugins/analog/digitalmodulator.cpp`
- `src/plugins/analog/dsssmodulator.cpp`
- `src/plugins/analog/analogmodulationplugin.cpp`
- 必要时补充 `src/plugins/analog/ofdmmodulator.cpp`

## 交付边界

- 仅重写 `Waveform_Parameters_Constraints.md`
- 不顺手修改其他知识库文档，避免把多份文档耦合进同一变更