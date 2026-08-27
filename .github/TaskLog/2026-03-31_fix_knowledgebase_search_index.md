# 2026-03-31 修复 KnowledgeBase 搜索索引缺失

## 背景

- 当前 repo-root 工作区已把知识库迁移到根 `.github/KnowledgeBase/`。
- 用户反馈在 VS Code 工作区中搜索文件时，知识库文档无法被列出。
- 根 `.vscode/settings.json` 未配置 `files.exclude`、`search.exclude` 或 `files.watcherExclude` 来隐藏知识库。

## 结论

- 根因是 repo 根 `.gitignore` 仍包含 `.github/`，导致整个 `.github` 目录被默认忽略。
- VS Code 工作区搜索默认遵循 ignore 规则，因此 `.github/KnowledgeBase/` 下的文件不会进入正常搜索结果。
- 这是迁移后的陈旧忽略规则，应直接移除，而不是通过关闭 `search.useIgnoreFiles` 绕过。

## 执行计划

1. 删除根 `.gitignore` 中的 `.github/` 条目。
2. 验证 `.github/KnowledgeBase/Index.md` 不再被 Git ignore。
3. 用默认遵循 ignore 规则的文件枚举方式确认知识库文件重新可见。

## 风险说明

- 移除 `.github/` 忽略后，根 `.github` 下的说明文档、TaskLog 和 agent 配置会进入正常版本管理视野，这是当前 repo-root 工作区的预期状态。
- 若后续确实存在不希望纳入版本管理的临时文件，应按具体路径单独忽略，不应再次整体忽略 `.github/`。