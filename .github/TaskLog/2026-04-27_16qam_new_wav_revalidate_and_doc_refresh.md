# 16QAM 新 WAV 复验与文档刷新计划

## 目标

- 基于用户替换后的 `16QAMOverSample4.wav` 和 `16QAMOversample32.wav`，用 Python 重新验证一次分析链。
- 重新生成一张新的对比 SVG。
- 彻底更新复现文档，移除已修复的旧 WAV padding bug 历史说明，只保留当前有效流程。

## 局部假设

- 由于 `Utils::WavHeader` 已修复 odd-sized unknown chunk padding，新文件应能被标准 Python `wave` 直接读取。
- 只要先验证 `wave` 可打开、再复用现有 16QAM 分析链，就能稳定得到新的对比图和新的量化结果。

## 最小实现

- 先确认新 wav 文件名和文件存在。
- 用 Python 直接读取两份 wav，验证 header/profile/采样数据。
- 重新生成新的 compare SVG。
- 将 KnowledgeBase 文档改为只描述当前文件、当前流程和当前结论。

## 校验

- 读取新生成 SVG 的文件存在性。
- 校对文档中的文件名、流程和结论不再引用旧文件名或旧 bug。