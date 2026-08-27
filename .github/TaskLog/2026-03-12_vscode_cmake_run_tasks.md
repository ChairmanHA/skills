# VS Code CMake 构建/启动任务补充

## 目标
- 为当前 CMake 工程补齐 VS Code 下可直接使用的构建、启动、日志查看与插件目录清理入口。
- 保持当前运行时布局不变：根目录 `bin/`、`plugin/`、`configuration/`。
- 不修改业务代码，只补工作区脚本与编辑器配置。

## 路径约定
- `src/` 作为 CMake source 目录。
- `build/cmake-win-release` 用于 Release 构建目录。
- `build/cmake-win-debug` 用于 Debug 构建目录。
- 可执行文件固定为根目录 `bin/SGStudio.exe` 或 `bin/SGStudiod.exe`。
- 插件输出固定为根目录 `plugin/`。
- 日志文件固定为根目录 `bin/debug.log`。

## 新增配置

### .vscode/tasks.json
- `CMake Configure Release`：生成 Release 工程。
- `CMake Build Release`：构建 Release，作为默认 build 任务。
- `CMake Configure Debug`：生成 Debug 工程。
- `CMake Build Debug`：构建 Debug。
- `Clean Plugin Folder`：清理根目录 `plugin/` 下构建产物，减少旧插件残留干扰。
- `Rebuild Release After Plugin Clean`：先清理 `plugin/`，再重新 configure + build Release。
- `Tail debug.log`：在终端持续跟踪 `bin/debug.log`。

### .vscode/launch.json
- `Launch SGStudio Release`：构建后直接启动 Release 程序。
- `Launch SGStudio Release After Plugin Clean`：先清理插件目录再启动 Release。
- `Debug SGStudio`：构建 Debug 后启动调试版程序。

### .vscode/settings.json
- 增加 CMake 默认 source/generator/buildDirectory。
- 固定 `CMAKE_PREFIX_PATH` 为本机已验证可用的 `C:/Qt/5.15.9/msvc2022_64`。

## 设计取舍
- 没有把“清理 plugin 目录”强制绑定到每次启动，避免日常调试时每次都触发额外重建。
- 仍提供单独的“清理后重建启动”入口，方便遇到插件冲突时快速切换。
- 任务优先直接调用 `cmake`，不强依赖 VS Code 的 CMake Tools 命令面板，降低环境差异影响。

## 2026-03-12 补充修正
- `Launch SGStudio Release` 的 `preLaunchTask` 是 `CMake Build Release`，因此点击启动前会先执行一次增量构建；如果没有源码变化，CMake/MSBuild 只会做最小必要检查，不会等价于全量重编。
- `Launch SGStudio Release After Plugin Clean` 初版会在 SGStudio 仍在运行时失败，因为插件 DLL 可能被占用而无法删除。
- 修正后，清理任务会先停止已有 `SGStudio` 进程，再删除 `plugin/` 下的运行时产物（`.dll`、`.manifest`），然后重新 configure + build Release。