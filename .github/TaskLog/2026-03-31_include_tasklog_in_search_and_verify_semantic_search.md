# 2026-03-31 将 TaskLog 纳入搜索并验证语义搜索

## 背景

- 当前 repo-root 工作区下，默认搜索已经可以看到 `.github/KnowledgeBase/`。
- 用户希望 `.github/TaskLog/` 也进入默认搜索，因为其中保留了大量迁移、排障和设计记录。
- 此前还出现过“语义搜索不可用”的提示，需要确认历史根因并验证当前状态已恢复。

## 已核对事实

- 当前根 `.gitignore` 仍忽略 `.github/`，这是为了避免大量说明文档进入 Git 未暂存列表。
- 当前根 `.ignore` 已通过 re-include 规则把 `.github/KnowledgeBase/` 重新暴露给 VS Code 搜索。
- repo-root 迁移文档已明确记录：历史上 `workspace root != git root` 时，Copilot 索引、Git 语义和搜索边界都无法稳定覆盖完整仓库。
- 当前安装的 Copilot Chat 扩展中，`github.copilot.chat.codesearch.enabled` 默认值是 `false`。
- 当前安装的 Copilot Chat 扩展中，`github.copilot.chat.workspace.maxLocalIndexSize` 默认值是 `100000`，描述为“Maximum size of the local workspace index.”。
- 当前 repo 根目录总体积约为 `3.2 GB`；虽然索引通常不会纳入全部忽略文件，但默认阈值偏保守，容易在大仓库上触发本地索引能力缺失。

## 结论

- `TaskLog` 应与 `KnowledgeBase` 一样，通过 `.ignore` 局部 re-include 纳入默认搜索，而不是整体放开 `.github/`。
- 历史上的“语义搜索不可用”最可能根因是迁移前工作区根在 `src/`，而 Git 根在 repo-root，导致 Copilot 无法以完整仓库视角建立稳定索引。
- 除工作区根不一致外，`#codebase` 所依赖的 agentic codesearch 默认关闭，也是“语义搜索不可用”的直接风险源之一。
- 对当前这种较大的单仓库，显式开启 `github.copilot.chat.codesearch.enabled` 并提高 `github.copilot.chat.workspace.maxLocalIndexSize`，可以减少再次触发“语义搜索不可用”的概率。

## 执行计划

1. 扩充根 `.ignore`，把 `.github/TaskLog/` 也加入搜索白名单。
2. 在工作区 `.vscode/settings.json` 中显式开启 Copilot codesearch，并提高本地工作区索引大小阈值。
3. 验证默认文件搜索和文本搜索都能命中 TaskLog。
4. 重新触发一次本地工作区索引构建，确认当前不再落入“语义搜索不可用”的配置状态。

## 风险说明

- 该方案仍保持 `.github/agents/`、`.github/copilot-instructions.md` 等非文档目录默认排除，避免搜索噪音重新扩大。
- 若将来还需要搜索其他 `.github` 子目录，应继续按目录白名单方式增量放开，而不是取消 `.github/` 整体忽略。