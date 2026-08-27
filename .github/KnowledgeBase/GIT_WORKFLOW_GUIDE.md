# SGStudio Git 版本维护与工作流指南

本文档旨在定义 SGStudio 项目的版本控制策略、分支管理模型及特定场景下的最佳实践。本指南基于 Git Flow 思想，并针对桌面客户端软件（Qt/C++）的特点进行了优化。

## 1. 核心分支策略

项目的 Git 仓库包含以下两类主要分支：

### 长期分支 (Long-lived Branches)
- **`main`**: **发布主线**。
    - 永远处于稳定状态（Production Ready）。
    - 该分支上的每一个 Commit 节点都**必须**都有一个对应的 Tag（如 `v1.0.0`, `v1.1.0`）。
    - 严禁直接在 `main` 上提交代码。
- **`develop`**: **开发主线**。
    - 所有新功能开发、非紧急 Bug 修复的汇聚地。
    - 代表了“下一个发布版本”的最新状态。

### 临时/辅助分支 (Supporting Branches)
- **Feature 分支** (`feature/xxx`): 用于开发新功能，从 `develop` 切出，合并回 `develop`。
- **Hotfix 分支** (`hotfix/xxx`): 用于紧急修复线上版本，从 `Tag` 切出，合并回 `main` **和** `develop`。
- **Support 分支** (`support/vX.X`): 用于长期维护某个旧版本，平行演进。

---

## 2. 版本号规范

遵循 **语义化版本 (Semantic Versioning)** 规范：`主版本号.次版本号.修订号` (Major.Minor.Patch)

- **主版本 (Major)**: `v1.0.0` → `v2.0.0`。具有破坏性更新、架构重构或不向下兼容的 API 变更。
- **次版本 (Minor)**: `v1.0.0` → `v1.1.0`。新增功能，且向下兼容。
- **修订号 (Patch)**: `v1.0.0` → `v1.0.1`。进行 Bug 修复，不包含新功能。
---

## 3. 标准工作流 (Workflows)

### 3.1 日常新功能开发 (Next Release)
这是最常见的工作模式，目标是开发 v1.1.0。

1.  **准备环境**: 切换到 `develop` 分支并更新。
    ```bash
    git checkout develop
    git pull origin develop
    ```
2.  **开始开发**: 创建特性分支。
    ```bash
    git checkout -b feature/waveform-optimization
    ```
3.  **提交代码**: 开发并完成本地测试。
4.  **合并**: 切回 `develop` 并合并（使用 `--no-ff` 保留特性历史）。
    ```bash
    git checkout develop
    git merge --no-ff feature/waveform-optimization
    git branch -d feature/waveform-optimization
    ```

### 3.2 紧急 Bug 修复 (Hotfix)
**场景**: v1.0.0 已发布，用户反馈严重崩溃，必须立即修复，不能等待 v1.1.0。

1.  **创建热修分支**: 从出问题的 Tag 切出。
    ```bash
    git checkout -b hotfix/v1.0.1 v1.0.0
    ```
2.  **修复**: 修改代码，并将 `src/sgstudio.pri` 版本号改为 `1.0.1`。
3.  **合并回主线 (发布)**:
    ```bash
    git checkout main
    git merge --no-ff hotfix/v1.0.1
    git tag -a v1.0.1 -m "Release v1.0.1: Fix critical crash"
    ```
4.  **同步回开发线 (关键)**: 防止 Bug 在下一版本复活。
    ```bash
    git checkout develop
    git merge --no-ff hotfix/v1.0.1
    ```
5.  **清理**: `git branch -d hotfix/v1.0.1`

### 3.3 旧版本长期维护 (Legacy Support)
**场景**: 当前最新也是 v1.2.0，但大客户只能用 v1.1.0 且需要修复 Bug。

1.  **建立平行宇宙**: 从 v1.1.0 建立长期支持分支。
    ```bash
    git checkout -b support/v1.1 v1.1.0
    ```
2.  **独立维护**: 在此分支上修改，并发布 `v1.1.1`, `v1.1.2` 等 Tag。
3.  **反向移植 (Backporting)**: **非常重要！**
    - 如果在 `support/v1.1` 修复了一个通用 Bug，必须将其移植回 `develop`。
    ```bash
    git checkout develop
    git cherry-pick <commit-hash-of-fix>
    ```

---

## 4. 依赖升级与杂项维护 (Chores)

针对第三方 API、SDK 升级或文档调整，这类任务虽然不属于业务功能，但也需要严格的版本控制。

### 4.1 第三方 API 大版本/破坏性升级（旧接口不兼容）

当上游 API 发布新版本，**新增接口且旧接口不兼容**（编译无法通过或行为不一致），建议将其视为“会影响整个产品交付节奏”的变更，按以下策略处理：

**分支策略（推荐）**
- **升级分支**：从 `develop` 切出 `chore/upgrade-<sdk>-vX`（或 `feature/api-vX-migration`），专门承载适配与重构。
- **必要时建立支持分支**：如果你们还需要在旧 API 上继续交付 Bugfix（例如客户仍在用旧硬件/旧 SDK），从当前线上 Tag 建立 `support/v<旧主版本>`，在该分支上发布 `vA.B.C` 的 Patch 版本。

**为什么不直接在 `develop` 上改**
- 破坏性升级通常会导致长时间不可编译/不可用，直接落到 `develop` 会阻塞其他功能开发与集成。
- 独立分支可以让迁移按阶段推进，同时通过频繁同步 `develop` 来降低最终合并成本。

**落地步骤（可执行）**
1. **创建升级分支**（从 `develop`）：
    ```bash
    git checkout develop
    git pull origin develop
    git checkout -b chore/upgrade-<sdk>-vX
    ```
2. **先做“隔离层/适配层”，再改业务调用**：
    - 尽量把新旧 API 差异收敛在少量文件/模块内（Wrapper/Adapter/Facade），避免全仓库到处散落条件编译。
    - 如果需要同时支持新旧 API，使用编译宏或构建开关做二选一（并明确默认值），直到旧版本下线。
3. **阶段性保证可编译**：迁移过程中尽量保持分支在关键里程碑可编译、可运行，减少“巨型一次性合并”。
4. **保持与 `develop` 同步**（降低漂移）：
    ```bash
    git checkout chore/upgrade-<sdk>-vX
    git merge --no-ff develop
    ```
5. **版本号策略**：
    - 如果升级导致对外行为/接口/兼容性变化，通常应按语义化版本做 **Major** 升级（例如 `1.x` → `2.0.0`）。
    - 若只是内部依赖升级但对外完全兼容，可按实际变更选择 Minor/Patch。
    - 版本号仍在 `src/sgstudio.pri` 的 `SGS_VERSION` 维护，发布前统一调整。
6. **合并与发布**：
    - 迁移完成并验证后，将升级分支合并回 `develop`（建议 `--no-ff` 保留迁移历史）。
    - 真正对外发布时，仍由 `main` 打 Tag（例如 `v2.0.0`）。

**补充建议（常见决策点）**
- **是否需要“双轨支持”**：如果客户现场同时存在新旧设备/SDK，优先做一段时间的双轨（适配层 + 构建开关）；如果不需要，尽量“一次迁移、尽快删除旧接口”，降低维护成本。
- **是否需要快速止血**：如果升级是为了修线上严重问题且必须立即发布，按 Hotfix 思路从 Tag 拉 `hotfix/...`，但依然建议在 hotfix 内尽量控制改动面（把破坏性重构留在升级分支）。

**操作步骤**：

1.  **创建专用分支**: 使用 `chore/` 前缀。
    ```bash
    git checkout develop
    # 例如：升级设备SDK到2.1版本
    git checkout -b chore/upgrade-device-sdk-v2.1
    ```
2.  **物理替换**: 替换 `.h` / `.lib` 文件，**务必清理**旧版本的残留文件。
3.  **验证与提交**: 确保编译通过且无运行时崩溃。
4.  **合并策略**:
    - **非紧急/非破坏性**: 合并到 `develop`，随下一个常规版本发布。
        ```bash
        git checkout develop
        git merge --no-ff chore/upgrade-device-sdk-v2.1
        git branch -d chore/upgrade-device-sdk-v2.1
        ```
    - **紧急/破坏性**: 如果必须立即上线，请参考 **3.2 紧急 Bug 修复 (Hotfix)** 流程。

---

## 5. 发布包命名规范

为了保证交付物的专业性与可识别性，禁止使用随意的文件名。

**推荐格式**: `<软件名>_<版本号>_<系统架构>_<类型>.<后缀>`

**示例**:
| 类型 | 推荐文件名 | 说明 |
| :--- | :--- | :--- |
| **正式发布包** | `SGStudio_v1.0.1_Win64.zip` | 标准格式，清晰包含版本与架构。 |
| **安装程序** | `SGStudio_Setup_v1.0.1.exe` | 明确标识为安装包。 |
| **测试预览版** | `SGStudio_v1.1.0-beta1_Win64_20260112.zip` | 增加日期后缀，便于测试人员区分每日构建。 |

> **提示**: 建议在构建脚本中自动化生成此文件名，避免手动重命名引入错误。

---

## 6. 高级场景与最佳实践

| 场景 | 挑战 | 最佳实践方案 | 禁忌操作 |
| :--- | :--- | :--- | :--- |
| **OEM / 定制化** | 不同客户要求不同 Logo/功能 | **代码即配置**。使用 `config.xml` 或编译宏 (`DEFINES`) 控制差异。 | 不要为每个客户拉一个永久分支 (Branch per Customer)，会导致无法合并。 |
| **长周期重构** | 开发耗时3个月，与主线冲突巨大 | **特性开关 (Feature Toggles)**。用 `if (newArch)` 隔离新代码，频繁合并主线。 | 不要做“憋大招”式的长期分支，最后合并时会非常痛苦。 |
| **发布回滚** | 刚发的 Tag 有严重问题 | **向前修复**。使用 `git revert` 撤销代码，发布新的 Patch 版本 (v1.0.2)。 | 严禁删除远程 Tag 并强制推送覆盖，这会破坏团队历史。 |
| **环境腐烂** | 3年后无法编译旧版本代码 | **环境归档**。打 Tag 时在 `BUILD_ENV.md` 记录编译器、Qt及库版本。 | 认为代码在，就一定能编译过。 |

---

## 7. Git 常用命令速查

```bash
# === 标签操作 ===
# 打附注标签 (推荐)
git tag -a v1.0.0 -m "Release version 1.0.0"
# 推送特定标签
git push origin v1.0.0
# 查看标签详情
git show v1.0.0

# === 分支操作 ===
# 创建并切换
git checkout -b <branch_name>
# 强制删除未合并分支
git branch -D <branch_name>

# === 历史操作 ===
# 查看分支图谱
git log --oneline --graph --decorate --all
# 移植单个提交
git cherry-pick <commit_id>
# 撤销某个提交 (生成新提交)
git revert <commit_id>
```

---
*文档生成日期: 2026-01-12*
