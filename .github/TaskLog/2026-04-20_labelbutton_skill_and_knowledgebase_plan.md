# 2026-04-20 LabelButton Skill And KnowledgeBase Plan

## 背景

- `LabelButton` 样式修改在本仓库中反复出现，但它不是单纯改一段 QSS 就能稳定完成的问题。
- 同一个双行按钮可能同时涉及：
  - 业务 enabled / checked 语义
  - 当前页 selected / highlighted 语义
  - 控件 disabled / unavailable 语义
  - `textLabel` / `infoLabel` 的子控件样式刷新链
- 这些内容里，既有适合沉淀为“固定操作流程”的部分，也有适合沉淀为“长期背景知识”的部分。

## 目标

1. 创建一个 workspace 级 skill，专门处理 `LabelButton` / `InfoButton` 双行按钮样式修改任务。
2. 创建一篇知识库文档，沉淀 `LabelButton` 的状态模型、刷新机制、以及 `RF / General Settings / Sweep` 三种用法。
3. 用这组文件明确 `instruction / skill / KnowledgeBase` 三者在当前仓库中的分工。

## 设计结论

- skill 负责“如何做”：
  - 先分类状态语义
  - 决定状态载体
  - 决定状态 owner
  - 同步 QSS 与刷新链
  - 做最小验证
- KnowledgeBase 负责“为什么这样做”：
  - `LabelButton` 是复合控件，不是单层按钮
  - `checked / enabled / pageHighlighted` 不能混用时要拆状态轴
  - 为什么 `Sweep` 不能把 page selected 与 business enabled 都塞进 `checked`
- instruction 仍负责仓库级常驻规则，不在本次新增文件中重复定义。

## 计划文件

- skill: `.github/skills/labelbutton-style-workflow/SKILL.md`
- knowledge base: `.github/KnowledgeBase/labelbutton_style_state_workflow.md`
- index update: `.github/KnowledgeBase/Index.md`

## 非目标

- 不在本次新增新的仓库 instruction 文件。
- 不修改运行时代码，只补充协作与沉淀文档。
- 不把所有 UI/QSS 问题都抽象成 skill；只聚焦 `LabelButton` 双行按钮样式流程。