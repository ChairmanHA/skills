# 2026-03-31 KnowledgeBase 源码路径口径收口

## 目标

- 在保留根 `.github/` 本地忽略策略的前提下，把 `.github/KnowledgeBase/` 中仍然使用旧源码根口径的文档统一为 repo-root 视角。
- 删除已经完成阶段性使命的 `repo_root_workspace_migration_plan.md`。

## 已确认前提

- 当前 repo-root 工作区入口、根 `.vscode/` 任务、启动配置、CMake 设置都已经落地。
- 根 `.github/` 仍被 `.gitignore` 忽略是刻意保留的本地策略，不作为本次删除规划文档的阻塞条件。
- 本次真正需要收口的是 KnowledgeBase 中对源码路径的表达方式，而不是运行时目录 `bin/`、`plugin/`、`configuration/` 的描述。

## 修改范围

- 统一 `plugins/...`、`libs/...`、`app/...`、`tools/...`、`sgstudio.pro` 这类源码路径到 `src/...` 视角。
- 不改动指向运行时 Qt plugin 目录、部署目录、`bin/plugins/...` 或 Linux 安装产物目录的非源码路径表述。
- 删除 `.github/KnowledgeBase/repo_root_workspace_migration_plan.md`。

## 验收标准

1. `.github/KnowledgeBase/` 中不再把源码目录写成相对旧 `src` 工作区根的 `plugins/...`、`libs/...`、`app/...`、`tools/...`。
2. 仍然保留对运行时目录与部署路径的正确描述，不做误替换。
3. 迁移规划文档被删除，且检索不到它残留的旧路径口径作为“现行规划”。