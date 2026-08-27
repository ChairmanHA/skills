# 2026-03-31 本地忽略 .github 且不破坏 KnowledgeBase 搜索

## 背景

- 直接从根 `.gitignore` 移除 `.github/` 后，VS Code 搜索可以重新看到知识库文件。
- 但这样会让根 `.github/` 下的大量文档与 agent 资产出现在 Git 未暂存列表中，影响日常使用。

## 结论

- 这类需求应优先用本地 Git ignore 处理，而不是继续修改仓库级 `.gitignore`。
- 优先方案是把 `.github/` 放入 `.git/info/exclude`，只影响当前工作区本机的 Git 状态显示。
- 然后验证 VS Code 默认搜索是否仍能看到 `.github/KnowledgeBase/`；若本地 exclude 也会影响搜索，再补独立搜索覆盖规则。

## 执行计划

1. 在 `.git/info/exclude` 中加入 `.github/` 本地忽略规则。
2. 验证 `git status` 不再被 `.github/` 噪音淹没。
3. 验证默认搜索仍能命中 `.github/KnowledgeBase/` 内容。
4. 仅在搜索再次受影响时，才追加独立搜索覆盖文件。

## 风险说明

- `.git/info/exclude` 是本地 Git 配置，不会影响其他协作者。
- 若将来换机器或重建 `.git/` 目录，需要重新设置该本地 exclude。