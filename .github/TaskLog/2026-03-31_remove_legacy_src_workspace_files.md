# 2026-03-31 删除 src 遗留工作区文件

## 背景

- repo-root 工作区入口、根 `.vscode`、根 `.github` 已经建立并完成基本验证。
- `src/.vscode/` 与 `src/src.code-workspace` 仍然保留，但内容已经是旧的 `workspace root = src` 语义。

## 结论

- 当前运行中的根工作流不再依赖 `src/.vscode/` 或 `src/src.code-workspace`。
- `src/.vscode/` 中的 tasks / launch / settings / helper 脚本均为旧副本，继续保留只会形成双维护风险。
- 删除前需要修正仍面向当前使用者的知识库文档，把旧入口改到 repo-root 口径。

## 执行计划

1. 修正仍引用旧工作区入口的活文档。
2. 删除 `src/src.code-workspace`。
3. 删除 `src/.vscode/` 下的旧配置与脚本。
4. 验证 repo-root task、运行入口与 instruction 文件仍正常可用。

## 风险说明

- `.github/KnowledgeBase/repo_root_workspace_migration_plan.md` 与历史 TaskLog 中保留旧路径是预期行为，它们是历史迁移记录，不作为当前操作手册。
- 如果需要让根 `.github` 持续纳入版本管理，不应在 `.gitignore` 中忽略 `.github/`。