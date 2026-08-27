# 16QAM 数字调制 WAV 复现文档计划

## 目标

- 基于本次对话里已经验证过的两份 16QAM wav 分析结果，补一篇“可直接复现”的 Markdown 文档。
- 文档重点不是再讲 raw cloud 的概念，而是把复现时必须遵守的解析方式、抽样流程、匹配滤波流程、产物命名和预期结果写清楚。

## 局部假设

- 现有文档 [../KnowledgeBase/digital_16qam_raw_cloud_vs_symbol_constellation.md](../KnowledgeBase/digital_16qam_raw_cloud_vs_symbol_constellation.md) 主要解释现象与结论，缺少按步骤重跑的操作说明。
- 只要把两份 wav 的已验证流程沉淀为独立指南，并明确 `prof` chunk 无补齐、`wave` 模块不可依赖、Rectangular 与 RRC 两条分析链不同，后续 Copilot 就不需要重新摸索。

## 最小落地

- 新增一篇 KnowledgeBase 文档，内容包含：输入文件、解析坑点、推荐实现顺序、两条分析链、输出文件名、预期量化结果、复用检查单。
- 更新 KnowledgeBase 索引，保证后续能直接找到。

## 校验

- 文档新增后做一次最窄校验，确认新文件和索引更新都进入工作区改动。