# SGStudio CMake 构建、输出、清理与运行流程

本文档只描述本仓库当前实际采用的 CMake 工作流，目标是让后续维护者能快速回答四类问题：

- 工程怎么 configure / build。
- 构建产物最终落到哪里。
- 什么可以安全清理，什么不能挂到日常构建链路里。
- 为什么 VS Code、Qt Creator、直接双击 `bin/SGStudio.exe` 看起来相近，但行为边界并不完全一样。

## 1. 总体目录模型

当前工程已经收敛到“仓库根作为 CMake 入口，`src/` 作为主要代码子树，运行目录可在 `build-tree` / `repo-root` 两种布局间切换”的模型：

- 根目录 `CMakeLists.txt`：当前主 CMake 入口。负责 `SGS_RUNTIME_LAYOUT`、`SGS_BIN_DIR`、`SGS_LIB_DIR`、`SGS_PLUGIN_DIR`、`SGS_CONFIGURATION_DIR` 等全局运行时目录，并再 `add_subdirectory(src)` 进入业务代码子树。
- `src/`：主程序、共享库、插件等主要源码子树，不再是 VS Code 日常 official workflow 的默认 configure 入口。
- `configuration_files/`：运行时配置与内嵌字体的源目录。`<packet>_<language>` 变体在 configure 阶段复制到当前布局下的 `configuration/`；`configuration_files/fonts/` 作为编译输入被打进 first-party Qt 资源，而不再复制出独立 `fonts/` 目录。
- `build/...`：不同生成器、不同配置、不同 branding 各自独立的 build tree。
- 根目录 `bin/`：`repo-root` 布局下应用主程序、共享库、Qt runtime、Qt plugins 的实际运行目录。
- 根目录 `plugin/`：`repo-root` 布局下业务插件运行目录，当前采用单目录覆盖部署模型。
- 根目录 `lib/`：`repo-root` 布局下 Windows `.lib` import libraries。
- 根目录 `configuration/`：`repo-root` 布局下的运行时配置副本目录。
- 根目录 `stage-assets/`：仅在 `sgstudio_stage` 安装阶段使用的附加交付物清单目录。

这意味着：

- VS Code 日常开发显式使用 `SGS_RUNTIME_LAYOUT=repo-root`，因此运行期主要依赖 repo 根的 `bin/`、`plugin/`、`configuration/`。
- Qt Creator 默认应保持 `build-tree`，对应的 `bin/`、`plugin/`、`configuration/` 都在当前 build tree 内。
- repo 根 `configuration/` 是运行时复制结果，不是源资产目录；静态配置与字体源内容仍以 `configuration_files/` 为准。

## 2. 顶层 CMake 入口的职责

当前真正决定运行时布局和全局输出边界的是 [../../CMakeLists.txt](../../CMakeLists.txt)，而 [../../src/CMakeLists.txt](../../src/CMakeLists.txt) 继续负责 app / libs / plugins 子树本体。

### 2.1 运行时布局与统一输出目录

根 CMake 入口先根据 `SGS_RUNTIME_LAYOUT` 计算：

- `SGS_BIN_DIR`
- `SGS_LIB_DIR`
- `SGS_PLUGIN_DIR`
- `SGS_CONFIGURATION_DIR`

其中：

- `build-tree`：这些目录都落在当前 `CMAKE_BINARY_DIR` 下。
- `repo-root`：这些目录都落在仓库根目录下。
- 插件 target 的真实输出仍先进入 `${CMAKE_BINARY_DIR}/plugin-runtime` staging 区，再同步回最终运行目录 `${SGS_PLUGIN_DIR}`。

这样做的目的不是“看起来整齐”，而是把“真正运行目录”和“构建系统可能自动附带的副作用目录”隔离开。

### 2.2 运行时配置复制与内嵌字体资源

[../../configuration_files/CMakeLists.txt](../../configuration_files/CMakeLists.txt) 负责两件事：

- 把 `configuration_files/<packet>_<language>/` 下的配置文件在 configure 阶段复制到 `${SGS_CONFIGURATION_DIR}`。
- 保留 `configuration_files/fonts/` 作为 first-party Qt 资源输入，由已编译的程序从内嵌资源加载字体。

因此：

- 在 VS Code 的 `repo-root` 日常布局下，目标目录就是根目录 `configuration/`。
- 在 Qt Creator 的 `build-tree` 布局下，目标目录就是当前 build tree 内的 `configuration/`。
- `Settings.ini` 虽然位于运行目录，但它承载的是本机运行态配置；静态默认资产仍以 `configuration_files/` 为源。
- 字体不再以独立运行目录存在；当前 built app/helper 会从各自已链接的一方 Qt 资源中注册字体。

### 2.3 统一 Qt 运行时部署

构建完成后，Qt runtime 和 Qt plugins 会被复制到 `${SGS_BIN_DIR}` 以及 `${SGS_BIN_DIR}/plugins/...`。

这意味着：

- 在 VS Code `repo-root` 布局下，它们进入根目录 `bin/`。
- 在 `build-tree` 布局下，它们进入当前 build tree 的 `bin/`。
- 关键约束不是“多复制一些 Qt 文件”，而是“复制必须来自 `find_package(Qt5 ...)` 实际命中的同一套 Qt 安装目录”。

### 2.4 插件 staging 再同步

插件链路当前的关键边界是：

- 每个业务插件先构建到 `build/.../plugin-runtime/`。
- 插件单独关闭 `DEBUG_POSTFIX d`，因此 Debug 与 Release 都使用相同文件名。
- 构建完成后，只把插件 DLL 本身同步到最终运行目录 `${SGS_PLUGIN_DIR}`。
- 同一次同步还会清理旧的 `*d.dll` 以及历史 `plugin/debug`、`plugin/release` 子目录残留，避免旧布局持续干扰。

这个设计是为了隔离 vcpkg / MSBuild / Qt Creator 可能追加的 applocal 行为。即使构建系统给插件 target 自动部署了额外 DLL，被污染的也优先是 build tree staging 区，而不是实际运行目录。

### 2.5 stage 附加资源清单

`stage-assets/` 只服务于 `sgstudio_stage` 安装 / 打包链路，不属于开发期日常运行布局。

当前规则是：

- 根目录 `stage-assets/` 存放只给 stage 包使用的附加交付物。
- `stage-assets/install-stage-assets.cmake.in` 作为这部分资源的 manifest，被生成进 build tree 的 install script。
- `sgstudio_stage` 执行 `cmake --install` 或对应 stage 目标时，manifest 中的资源会被复制到最终 stage 包。

当前已验证的落位包括：

- `stage-assets/h2_usbdriver.7z` -> stage 包根目录
- `stage-assets/data/16QAM.wav` -> stage 包 `data/`

这部分内容不会进入开发期运行目录，只属于最终交付包。

## 3. Configure 和 Build 流程

### 3.1 VS Code 流程

当前工作区里，和日常 official workflow 直接相关的任务是：

- `CMake Configure Release`
- `CMake Build Release`
- `CMake Configure Debug`
- `CMake Build Debug`
- `Prepare Release Launch Runtime`
- `Prepare Debug Launch Runtime`
- `Clean Plugin Folder`
- `Rebuild Release After Plugin Clean`
- `Build Release Validate`
- `Tail debug.log`

其中日常主链路是：

1. 用仓库根作为 source 目录执行 configure：`-S ${workspaceFolder}`
2. 生成到 `build/cmake-win-release` 或 `build/cmake-win-debug`
3. 显式传入 `-DSGS_RUNTIME_LAYOUT=repo-root`
4. build `ALL_BUILD`
5. 让 Qt runtime、业务插件、`configuration/` 一起同步到 repo-root 运行布局

而 VS Code 的启动 / 调试入口又会额外通过 [../../.vscode/prepare-launch-runtime.ps1](../../.vscode/prepare-launch-runtime.ps1) 做一次“启动前纠偏”：

1. 停止 `SGStudio` 与 `SGStudiod`
2. 递归清理根目录 `plugin/`
3. 对当前目标 build tree 再执行一次 configure，继续显式传 `-DSGS_RUNTIME_LAYOUT=repo-root`
4. 执行当前配置的 build
5. 把 `build/.../plugin-runtime/` 再同步回根目录 `plugin/`

这样做的目的不是取代普通 build，而是保证“切 Debug / Release 启动”时，根目录 `plugin/` 始终对应当前 launch 配置。

branding / stage 相关 helper 脚本是独立的辅助链路，不应和这里描述的日常 official workflow 混为一谈。

### 3.2 Qt Creator 流程

Qt Creator 当前也使用 CMake，但它的默认假设仍然应该是 `build-tree`：

- 以仓库根 CMake 项目作为 configure 入口
- 使用自己维护的 build directory，例如 `build/Qt_5_15_9_msvc2022_64-Release`
- 运行目录通常应对应当前 build tree 下的 `bin/`，除非你明确要切到 repo-root 运行布局
- 在点击 Run 之前先做一次增量构建检查

这意味着 Qt Creator 的 Run 不是单纯“直接启动 exe”，而是“先确认当前 target 是否需要构建，再启动”。

这个行为对普通 target 没问题，但如果你把危险的清理动作挂进无输出 custom target，就会在 Run 前被再次触发。

## 4. 两种布局下的运行时输出

当前工程同时支持两种运行时布局：

| 语义 | `build-tree` | `repo-root` |
| :--- | :--- | :--- |
| 可执行文件与普通共享库 | `build/.../bin` | 根目录 `bin/` |
| import library | `build/.../lib` | 根目录 `lib/` |
| 业务插件运行目录 | `build/.../plugin` | 根目录 `plugin/` |
| 运行配置目录 | `build/.../configuration` | 根目录 `configuration/` |
| 插件 staging 区 | `build/.../plugin-runtime` | `build/.../plugin-runtime` |

注意：

- `plugin-runtime/` 永远只是 build tree 内的 staging 区，不是最终运行目录。
- VS Code 日常使用的是右列的 `repo-root`。
- Qt Creator 默认应保持左列的 `build-tree`。

## 5. 运行时搜索路径与插件加载

应用启动入口在 [../../src/app/main.cpp](../../src/app/main.cpp)。Windows 下启动时会基于“当前可执行文件所在目录”推导相邻的运行时目录，而不是硬编码 repo 根路径。

### 5.1 先修改 PATH

`configureRuntimeLibrarySearchPaths()` 会把：

- 当前应用目录
- 当前应用目录的同级 `../plugin`

插入到现有 `PATH` 前面。

因此：

- 在 `repo-root` 布局下，它们分别对应根目录 `bin/` 与 `plugin/`。
- 在 `build-tree` 布局下，它们分别对应当前 build tree 的 `bin/` 与 `plugin/`。

这样做的目的，是让业务插件和程序主目录下的运行库优先被找到。但副作用也很明显：

- 如果当前布局下的 `plugin/` 被污染了同名 Qt / 第三方 DLL，优先级反而会更高。
- 因此最终运行目录里的 `plugin/` 必须尽量保持“只放插件 DLL”。

### 5.2 字体资源、配置与插件扫描目录

启动阶段还会基于应用目录加载：

- `../configuration/Settings.ini`
- `../plugin/`

也就是说：

- `Utils::loadBundledFonts()` 会从 first-party Qt 资源中注册内嵌 `.ttf`
- 全局设置会从当前布局对应 `configuration/Settings.ini` 读取
- `ExtensionSystem::PluginManager` 会把当前布局对应的 `plugin/` 作为插件扫描目录

所以一个最小可运行条件是：

- 当前布局对应的 `bin/SGStudio*.exe` 在
- 当前布局对应的 `plugin/` 下业务插件 DLL 在
- 当前布局对应的 `configuration/` 已被复制出来
- 当前布局对应的 `bin/` 下 Qt runtime 与第三方依赖完整且版本一致

## 6. 清理策略

清理是这套流程里最容易被误用的部分。

关于 `SGS_RUNTIME_LAYOUT=build-tree/repo-root` 的双布局选择，以及为什么 VS Code 的 launch 保护可以做成“显式准备任务”而不是通用 CMake 依赖，详见 [runtime_layout_repo_root_vs_build_tree.md](runtime_layout_repo_root_vs_build_tree.md)。

### 6.1 可以安全做的清理

- 删除单个 build tree，例如 `build/cmake-win-release`、`build/cmake-win-debug` 或 Qt Creator 自己的 build 目录
- 删除当前运行布局 `bin/` 中已经确认是污染的 Qt runtime，再重新 build 让 post-build 重新覆盖
- 显式执行 `sgstudio_reset_plugin_directory`，在你明确知道接下来要重新构建全部插件时，清空当前运行布局对应的 `plugin/`

### 6.2 不能自动挂进日常构建链路的清理

最重要的结论：

- 最终运行目录 `plugin/` 的整目录清理，不能作为每次 build 或每次 Run 前检查都会触发的自动步骤。

原因是：

- Qt Creator 点击 Run 前会先做增量构建检查。
- 如果清理动作被放在一个无输出 custom target 里，并被插件 target 依赖，那么它会被视为总是需要执行。
- 一旦插件 target 本身没有重编译，POST_BUILD 就不会重新同步插件 DLL。
- 结果就是 Run 之前先把最终运行目录 `plugin/` 删空，程序随后因插件缺失直接崩溃。

当前策略改为依赖同名覆盖部署，而不是在默认链路里先清空整目录。保留显式人工清理目标仍然可以，但绝不能重新挂回常规插件依赖链。

## 7. 两类典型坑位

### 7.1 Qt 运行时混装

详见 [windows_qt_runtime_mixing_pitfall.md](windows_qt_runtime_mixing_pitfall.md)。

简化结论是：

- `find_package(Qt5 ...)` 命中的是哪套 Qt，最终复制到当前运行布局 `bin/` 的核心 DLL 和 Qt plugins 就必须来自同一套 Qt。
- 只补 `Qt5Svg.dll` 或只补 `qsvg.dll` 这种半补丁式做法会制造更隐蔽的跨版本混装。

### 7.2 单目录覆盖部署的边界

当前插件策略不是“同时保留 Debug 和 Release 两套插件”，而是“最后一次构建的配置覆盖当前最终运行目录的 plugin 目录”。

这意味着：

- 如果你在 Qt Creator 或 VS Code 中点击 Run / Ctrl+F5，当前配置会先构建，因此插件目录会被刷新为正确版本。
- 如果你刚构建了 Debug，然后直接双击 Release exe 而不重新构建 Release，插件会不匹配。

这不是 bug，而是单目录覆盖部署的自然边界。

### 7.3 Run 前增量构建不是“什么都不做”

VS Code 和 Qt Creator 都可能在启动前做构建检查。

因此判断一个清理动作是否安全时，不能只问：

- “它在 full rebuild 时有没有问题？”

还要问：

- “如果本次点击 Run 没有任何源码变化，它会不会仍然执行？”
- “如果它执行了，而真正的插件 target 没有重编，运行目录还能恢复吗？”

这两个问题只要有一个答案是不确定，这个清理动作就不该挂进默认链路。

## 8. 推荐日常操作顺序

### 8.1 VS Code 日常开发

推荐顺序：

1. 用 `CMake Configure Release/Debug` + `CMake Build Release/Debug` 做日常增量开发
2. 需要启动或调试时，优先使用 `Launch SGStudio Release` 或 `Debug SGStudio`
3. 让对应的 `Prepare ... Launch Runtime` 任务在启动前自动纠正 repo-root 的 `plugin/`
4. 只有在你确认当前配置刚刚构建完成时，才直接双击根目录 `bin/SGStudio.exe` 或 `bin/SGStudiod.exe`

### 8.2 Qt Creator 日常开发

推荐顺序：

1. 使用默认 `build-tree` configure
2. 保持当前 build tree 自己的 `bin/`、`plugin/`、`configuration/` 运行语义
3. 不要依赖 VS Code 的 repo-root launch 保护脚本来修复 Qt Creator 的运行目录

### 8.3 怀疑 Qt runtime 被污染

如果出现 Qt plugin 发现正常但 LoadLibrary 失败、图标消失、平台插件异常：

1. 先检查当前运行布局 `bin/` 中 Qt DLL 版本是否一致
2. 检查当前运行布局 `bin/plugins/...` 中 Qt plugin 版本是否与核心 Qt 一致
3. 再检查当前运行布局 `plugin/` 是否被混入同名 Qt / 第三方 DLL
4. 必要时重新 configure + build，让运行时复制逻辑重新覆盖当前布局

## 9. 对未来修改者的硬约束

如果你后续要改这套 CMake 运行时布局，至少遵守下面五条：

- 不要把 `SGS_RUNTIME_LAYOUT` 的默认值改成 `repo-root`。
- 不要让 VS Code 依赖默认值；相关任务必须继续显式传 `repo-root`。
- 不要让插件 target 直接把真实输出落到最终运行目录 `plugin/`。
- 不要把最终运行目录 `plugin/` 的整目录清理绑定到常规构建或 Run 前检查。
- 不要把最终运行目录 `plugin/` 当成普通 DLL 垃圾桶，它应尽量只放业务插件本体。

## 10. 相关文件

- [../../CMakeLists.txt](../../CMakeLists.txt)
- [../../configuration_files/CMakeLists.txt](../../configuration_files/CMakeLists.txt)
- [../../src/CMakeLists.txt](../../src/CMakeLists.txt)
- [../../src/app/main.cpp](../../src/app/main.cpp)
- [../../.vscode/tasks.json](../../.vscode/tasks.json)
- [../../.vscode/launch.json](../../.vscode/launch.json)
- [../../.vscode/prepare-launch-runtime.ps1](../../.vscode/prepare-launch-runtime.ps1)
- [windows_qt_runtime_mixing_pitfall.md](windows_qt_runtime_mixing_pitfall.md)
- [runtime_layout_repo_root_vs_build_tree.md](runtime_layout_repo_root_vs_build_tree.md)
- [../TaskLog/2026-03-13_fix_cmake_plugin_runtime_layout.md](../TaskLog/2026-03-13_fix_cmake_plugin_runtime_layout.md)
- [../TaskLog/2026-03-12_vscode_cmake_run_tasks.md](../TaskLog/2026-03-12_vscode_cmake_run_tasks.md)