# High DPI 文档补充计划

## Background

- 当前工程在 Windows 下已启用 Qt High-DPI 属性，并通过 `qt.conf` 配置 `dpiawareness=2`，方向上已进入 Per-Monitor DPI Aware 路线。
- 仓库中已经积累了多屏拖动、无边框窗口、DPI 切换白边问题的处理经验，但缺少一份更高层的“业界实践 + 本项目后续演进”总结文档。
- 现有 QSS 指南偏重选择器、子控件和样式调试，尚未覆盖高 DPI、小尺寸高分屏、资源倍率、应用内 UI 缩放等主题。

## Goal

- 在 KnowledgeBase 中新增一篇面向本项目的 High DPI 开发实践文档。
- 在现有 QSS 指南中补充高 DPI / 小屏适配的最佳实践。
- 更新 KnowledgeBase 索引，确保后续能从总览快速找到相关文档。

## Scope

- 总结 Windows / Qt 5 下 DPI aware 与 DPI adaptive 的区别。
- 结合仓库现状，明确当前已经完成的平台层能力，以及仍需补齐的 UI 自适应能力。
- 补充以下未来优化方向：
  - QSS 尺寸策略与 token 化
  - 资源层的多倍率与矢量化
  - 主窗口 / 对话框的相对屏幕布局策略
  - 应用内 UI 缩放
  - 回归测试矩阵

## Deliverables

- `.github/KnowledgeBase/high_dpi_development_practices.md`
- 更新 `.github/KnowledgeBase/QSS_Best_Practices.md`
- 更新 `.github/KnowledgeBase/Index.md`

## Notes

- 文档重点是“方法论 + 本项目建议”，不是一次性改代码。
- 对已有 DPI 白边经验文档保持引用关系，不重复粘贴底层修复细节。