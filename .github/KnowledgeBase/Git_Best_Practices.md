# Git 最佳工程实践与工作流指南

本文档总结了 Git 日常开发中的最佳实践命令与常用的工作流操作。

## 0. 获取代码库 (Cloning)

### 0.1 克隆仓库
首次获取代码时使用。

```bash
# HTTPS 方式
git clone https://github.com/username/repository.git

# SSH 方式（推荐，需配置 SSH Key）
git clone git@github.com:username/repository.git
```

## 1. 初始化与分支管理 (Branching)

### 1.1 保持基准分支同步
在开始新工作前，始终确保你的本地主分支（如 `main` 或 `master`）是最新的。

```bash
git checkout main
git pull origin main
```

### 1.2 创建新功能分支
不要直接在 `main` 上开发。为每个任务创建一个新分支。

**旧命令**:
```bash
git checkout -b feature/new-task
# 如果远程已经有了相应分支，那么一步到位
git checkout -b ＜new-localbranch＞ ＜existing-remotebranch＞
```

**推荐新命令 (Git 2.23+)**:
```bash
git switch -c feature/new-task
```

### 1.3 跟踪远程分支
如果你想检出一个远程已存在的分支并在本地跟踪：

```bash
git fetch origin
# 自动创建同名本地分支并跟踪
git switch --track origin/feature-branch
```

## 2. 文件操作与暂存 (Staging)

### 2.1 移动与重命名
使用 `git mv` 而不是操作系统的文件管理器，这样 Git 能保持文件历史追踪，而不是将其视为“删除旧文件 + 新增新文件”。

```bash
git mv old/path/file.cpp new/path/file.cpp
```

### 2.2 删除文件
```bash
git rm path/to/file.cpp
```

### 2.3 智能暂存 (Interactive Staging)
推荐使用交互式暂存，只提交相关的改动，而不是盲目 `git add .`。

```bash
git add -p
```
这会逐块询问是否从暂存区添加改动（y/n），非常适合检查代码。

## 3. 提交 (Committing)

### 3.1 提交信息规范 (Convention)
推荐使用 Conventional Commits 格式：`<type>(<scope>): <subject>`
### 3.1 提交信息规范 (Convention)
Subject:

```text
<type>(<scope>): <summary>
```

Types: `fix`, `feat`, `refactor`代码格式（不影响逻辑）, `perf`, `test`测试用例, `docs`, `build`, `ci`, `chore`（构建过程或辅助工具变动）, `revert`.

Body should stay concise, usually 6 to 10 lines:

```text
Problem: ...
Change: ...
Test: ...
```

Optional when useful:

```text
Root cause: ...
Risk: ...
```

**示例**:
```bash
git commit -m "feat(ui): add plot marker context menu"
```

### 3.2 修正上次提交
如果你刚提交完发现漏了文件或写错了消息（且尚未推送到远程）：

```bash
git add forgotten_file.cpp
git commit --amend
```
这将打开编辑器让你修改上次的提交信息。

## 4. 同步与变基 (Syncing & Rebasing)

### 4.1 拉取更新
推荐使用 `--rebase` 拉取更新，以保持提交历史线性整洁，避免无意义的 Merge Commit。

```bash
git pull --rebase origin main
```
*如果在功能分支上，这会将你的独特提交“重放”在最新的远程代码之上。*

### 4.2 变基 (Rebase) vs 合并 (Merge)
- **本地开发**: 经常使用 `rebase` 保持与 `main` 同步。
- **公共分支**: 一旦分支被多人共享，**慎用** `rebase`（因为它会重写历史，导致他人无法推送）。

**将当前分支变基到最新 main**:
```bash
git switch feature/my-branch
git fetch origin
git rebase origin/main
```

## 5. 推送 (Pushing)

### 5.1 首次推送并设置上游
```bash
git push -u origin feature/my-branch
```

### 5.2 常规推送
```bash
git push
```

### 5.3 强制推送
只有在你独自使用的分支上进行 Rebase 或 Amend 后才使用。
```bash
git push --force-with-lease
```
*注意：`--force-with-lease` 比 `--force` 更安全。如果远端有你不知道的新提交（例如同事提交了代码），它会阻止覆盖，避免丢失他人工作。*

## 6. 切换分支与堆栈管理

### 6.1 切换分支
```bash
# 切换到已存在分支
git switch other-branch

# 切换回上一个分支
git switch -
```

### 6.2 暂存现场 (Stash)
需要临时切走处理急事，但当前工作未完成：

```bash
# 保存现场
git stash push -m "wip: implementing marker logic"

# 恢复现场
git stash pop
```

## 7. 高级操作：合并、冲突与回退

### 7.1 合并 (Merge)
将功能分支合并回主分支。

```bash
# 1. 切换回目标分支
git switch main

# 2. 拉取最新代码
git pull origin main

# 3. 合并分支
git merge feature/my-branch
```
如果希望保持提交历史干净（避免 Merge Commit 且功能分支历史简单），可以使用 Rebase 策略，或者使用 Squash Merge 将多个小提交压缩为一个：
```bash
git merge --squash feature/my-branch
# 随后需要手动提交
git commit -m "feat: complete feature X"
```

### 7.2 冲突处理 (Conflict Resolution)
当 Pull、Merge 或 Rebase 遇到冲突时：

1. **查看状态**：`git status` 会显示冲突文件。
2. **编辑文件**：打开文件，寻找 `<<<<<<<`, `=======`, `>>>>>>>` 标记，手动修改内容并删除标记。
3. **标记解决**：`git add path/to/resolved_file.cpp`
4. **完成操作**：
   - 如果是 Merge 过程：`git commit`
   - 如果是 Rebase 过程：`git rebase --continue`

### 7.3 变基 (Rebase) 进阶
将你的分支“搬移”到最新的基准分支上。

```bash
# 假设你在 feature 分支
git fetch origin
git rebase origin/main
```
如果中途遇到冲突，解决后运行 `git rebase --continue`。如果想放弃，运行 `git rebase --abort`。

**交互式变基 (Interactive Rebase)**：清理本地提交历史（合并、修改、删除提交）的神器。
```bash
# 修改最近 3 次提交
git rebase -i HEAD~3
```
在编辑器中将 `pick` 改为 `squash` (合并到上一个)、`reword` (改消息) 或 `drop` (删除)。

### 7.4 回退与撤销 (Reset & Revert)

**场景 A：撒销工作区修改（还没 `git add`）**
```bash
# 恢复单个文件
git restore path/to/file.cpp
# 丢弃所有修改
git restore .
```

**场景 B：撤销暂存（已经 `git add` 但没 `commit`）**
```bash
git restore --staged path/to/file.cpp
```

**场景 C：撤销最近一次本地提交（保留修改在工作区）**
```bash
git reset --soft HEAD~1
```

**场景 D：彻底回退到某个版本（危险！丢失后续改动）**
```bash
git reset --hard HEAD~1
# 或回退到远程状态
git reset --hard origin/main
```

**场景 E：已推送到远程，需要回滚（安全方式）**
不要用 reset，用 revert 生成一个新的“反向”提交。
```bash
git revert <commit-hash>
```

## 8. 最佳实践总结 Checklist
1. **原子提交**: 每个 Commit 只做一件事，且能独立通过测试。
2. **提交前自测**: 永远不要提交无法编译的代码。
3. **勤 Pull**: 即使在自己的分支开发，也要经常从主分支 Pull 更新，减少最终合并时的冲突风险。
4. **清理**: 分支合并后，及时删除本地分支 `git branch -d feature/finished-task`。
