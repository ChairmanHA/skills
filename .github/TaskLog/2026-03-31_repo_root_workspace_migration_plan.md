# Repo-root Workspace 迁移任务计划

## 目标

为 `d:\development\vsg2.0` 新建一个 repo-root 工作区入口，并输出一份详细迁移规划文档，指导后续把当前 `src/.vscode` 与 `src/.github` 下的资产迁到 repo 根，而不丢失现有构建、运行、调试与知识库体验。

## 当前约束

1. 当前 VS Code 主要从 `src/src.code-workspace` 打开，workspace roots 为 `src`、`3rdParty`、`configuration`。
2. 当前构建/启动配置都在 `src/.vscode/`，且多数任务默认假设 `${workspaceFolder}=src`。
3. repo 根已经存在 `.vscode/settings.json`，但只包含文件关联，不包含当前 `src/.vscode/settings.json` 中的 CMake 设置。
4. 当前 Copilot 自定义资产位于 `src/.github/`，repo 根不存在 `.github/`。
5. repo 根 `.gitignore` 当前包含 `src/.github/`，这会影响未来把 `.github` 提升到 repo 根后的整理策略。

## 本次输出

1. 新建 `d:\development\vsg2.0\vsg2.0.code-workspace`
2. 新增 `KnowledgeBase` 迁移规划文档
3. 更新 `KnowledgeBase/Index.md`

## 迁移原则

1. 先建立新的 repo-root 工作区入口，不立即破坏现有 `src/src.code-workspace`。
2. 先做路径与职责盘点，再迁移任务/启动配置，不直接盲搬。
3. `.github` 迁移采用“先复制到新根并验证，再删除旧路径”的双轨过渡，不在同一提交里同时改完所有资产。
4. 所有被 Git 跟踪的文件若后续真正迁移，应使用 `git mv`，不要删了重建。

## 后续建议顺序

1. 用 `vsg2.0.code-workspace` 打开 repo 根，验证 Copilot 本地索引恢复。
2. 按迁移规划补 root `.vscode/tasks.json` / `launch.json` / `settings.json`。
3. 把 `src/.github` 迁到 repo 根 `.github`，同步修正文档中的源码路径口径。
4. 等新工作区稳定后，再决定是否保留 `src/src.code-workspace` 作为过渡入口。