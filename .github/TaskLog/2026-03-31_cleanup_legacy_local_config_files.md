# 2026-03-31 清理遗留本地配置文件

## 背景

- 当前 repo-root 工作流已经稳定收敛到根 `.vscode/`、根 `vsg2.0.code-workspace`、根 `build/`、根 `bin/`、根 `plugin/`。
- 需要排查并删除仓库内残留的本地 IDE / 工作区用户配置，降低后续被旧路径、旧工作区或机器私有状态干扰的风险。

## 已核对事实

- `src/.vscode/` 与 `src/src.code-workspace` 当前都已不存在，说明旧 `workspace root = src` 的主入口已经删除。
- 当前根 `vsg2.0.code-workspace` 仍是 repo-root 工作区入口，不能删除。
- 当前根 `.vscode/` 下的 `tasks.json`、`launch.json`、`settings.json`、`cmake-stage-release*.ps1` 都属于现行工作流，不能删除。
- `3rdParty/.vscode/settings.json` 仅包含一个把 `cmake.sourceDirectory` 指向 `3rdParty/htra` 的孤立设置；当前 repo-root 工作流不会读取它。
- `src/CMakeLists.txt.user` 是 Qt Creator 生成的本机私有 CMake 工程状态文件，包含机器相关 build dir、toolchain、运行配置。
- `src/sgstudio.pro.user` 是 Qt Creator 生成的旧 qmake 工程状态文件，仍指向历史 `build-sgstudio-*` 目录，属于迁移前遗留物。

## 本次删除范围

1. `3rdParty/.vscode/settings.json`
2. `src/CMakeLists.txt.user`
3. `src/sgstudio.pro.user`

## 明确保留

- `vsg2.0.code-workspace`：当前 repo-root 工作区入口。
- 根 `.vscode/` 全部文件：当前 VS Code 工作流所需。
- `configuration/Profile.json`、`configuration/DeviceHistory.json`：它们属于运行期数据/用户状态，不属于“明确无用的遗留 IDE 配置”，本次不动。

## 风险说明

- 删除上述 `.user` 文件只会清除本机 Qt Creator 的私有项目状态；若后续再次用 Qt Creator 打开工程，IDE 会自动重建。
- `3rdParty/.vscode/settings.json` 删除后，不会影响当前 repo-root 工作区；仅意味着将来单独打开 `3rdParty/` 时不再自动把 CMake source 固定到 `htra`。