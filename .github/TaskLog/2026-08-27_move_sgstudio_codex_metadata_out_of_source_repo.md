# 将 SGStudio 协作元数据移出代码仓库

## 目标

将 SGStudio 使用的 KnowledgeBase、TaskLog、skills 及相关 `.github` 协作元数据集中维护在 `D:\development\skills\.github`，代码仓库仅保留指向该目录的 `AGENTS.md` 规则，避免代码仓库被协作文档和技能文件污染。

## 已确认事实

- `D:\development\skills` 已是 `https://github.com/ChairmanHA/skills.git` 的本地克隆，当前工作区干净。
- 当前仓库 `.github/KnowledgeBase`、`.github/TaskLog`、`.github/skills`、`.github/prompts`、`.github/agents` 与目标仓库对应目录逐文件 SHA-256 校验一致，因此不需要重复复制。
- 当前仓库 `.github` 下还存在 `dependency.zip`；它属于协作元数据目录，将随 `.github` 一并移除出代码仓库。

## 变更边界

1. 更新 SGStudio 根目录 `AGENTS.md`，所有 KnowledgeBase、TaskLog、skills、prompts 和 agents 的读取/写入路径改为 `D:\development\skills\.github`。
2. 删除当前仓库 `.github/` 下已同步的协作元数据副本。
3. 不修改 SGStudio 源码、构建配置或目标 skills 仓库已有内容；不执行构建和运行。

## 成功标准与验证

- `AGENTS.md` 不再要求读取或写入代码仓库内的 `.github/KnowledgeBase`、`.github/TaskLog` 或 `.github/skills`。
- 当前仓库不再存在 `.github/KnowledgeBase`、`.github/TaskLog`、`.github/skills`、`.github/prompts`、`.github/agents` 及其副本文件。
- 目标目录仍包含完整协作元数据，且目标 Git 工作区保持可提交状态。
- 通过静态搜索检查路径引用和 Git 状态；不编译、不运行。

## 实施结果

- 已将 SGStudio 根目录 `AGENTS.md` 的协作元数据路径改为 `D:\development\skills\.github`。
- 已删除 SGStudio 代码仓库中的整个 `.github` 副本（包括 `agents`、`KnowledgeBase`、`prompts`、`skills`、`TaskLog` 和 `dependency.zip`）。
- 已通过文件计数与 SHA-256 校验确认目标目录保留了原有协作元数据；未执行构建或运行。
