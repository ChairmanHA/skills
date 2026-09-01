# Repo-Root 与 Build-Tree 运行时布局约束

本文档专门说明当前仓库同时支持的两套运行时布局，以及为什么必须把它们区分为：

- Qt Creator 默认使用的 `build-tree`
- VS Code 日常开发显式使用的 `repo-root`

如果你只需要了解 CMake 构建、输出、清理与运行总览，先看 [cmake_build_output_clean_run_workflow.md](cmake_build_output_clean_run_workflow.md)。本文只收敛“双布局选择”和“切配置启动保护”这两个边界。

## 1. 结论先行

当前仓库的硬约束是：

- `SGS_RUNTIME_LAYOUT` 的默认值必须保持 `build-tree`。
- 当前 official VS Code configure / launch 入口是仓库根 `CMakeLists.txt`，相关任务不能再把 `src/` 当成默认 source entry 去描述。
- VS Code 相关任务与 launch 必须显式传 `SGS_RUNTIME_LAYOUT=repo-root`。
- 根目录 `plugin/` 仍然是单目录覆盖部署，不同时保留 Debug / Release 两套业务插件。
- 插件 target 的真实输出仍应先落到 build tree 的 `plugin-runtime/`，再同步回实际运行目录 `plugin/`。
- repo-root 下的 `configuration/` 是由 `configuration_files/` 复制出来的运行时目录，不是源资产目录；字体源文件仍保留在 `configuration_files/fonts/`，但运行时改为内嵌资源加载。
- “切配置时清理根目录 plugin” 只能放在 VS Code 的 launch 前置保护层里，不能重新挂回通用 CMake target 依赖。

## 2. 为什么必须保留两套布局

根因不是“有人偏好不同目录”，而是两个使用场景的默认假设不同。

### 2.1 Qt Creator 的默认假设

Qt Creator 更自然的工作方式是：

- 以一个 build tree 作为当前 configure 结果
- 继续沿用那个 build tree 的资源与运行语义
- 用户通常不希望额外理解 repo 根运行目录的覆盖策略

因此默认值必须保持 `build-tree`，否则 Qt Creator 用户即使不传任何额外参数，也会在不知情的情况下把输出改写到 repo 根运行目录。

### 2.2 VS Code 的默认假设

本仓库现有 VS Code 日常开发链路是围绕 repo 根运行目录建立的：

- 启动目标是根目录 `bin/SGStudio.exe` / `bin/SGStudiod.exe`
- `PATH` 前置的是根目录 `bin/` 与 `plugin/`
- 运行期配置依赖根目录 `configuration/`
- first-party 字体资源已编译进程序本体

而这些目录并不是硬编码出来的；[../../src/app/main.cpp](../../src/app/main.cpp) 仍会按“可执行文件目录的相邻 `../plugin`、`../configuration`”来定位运行资源，而字体则通过已链接的一方 Qt 资源加载。之所以 VS Code 要显式用 `repo-root`，是因为它的 launch 目标本来就指向 repo 根 `bin/`。

因此 VS Code 不能依赖 CMake 默认值，而必须在 configure / launch 相关入口中显式声明 `repo-root`。

## 3. 两套布局的目录职责

`SGS_RUNTIME_LAYOUT` 当前支持两个值：

- `build-tree`
- `repo-root`

它们对应的职责如下。

| 语义 | `build-tree` | `repo-root` |
| :--- | :--- | :--- |
| 可执行文件运行目录 | `build/.../bin` | 根目录 `bin/` |
| import library 输出 | `build/.../lib` | 根目录 `lib/` |
| 业务插件运行目录 | `build/.../plugin` | 根目录 `plugin/` |
| 运行配置目录 | `build/.../configuration` | 根目录 `configuration/` |

注意：

- `repo-root` 只决定“最终运行目录”落在 repo 根。
- 它不改变插件 target 真实链接/输出先进入 `plugin-runtime/` staging 的事实。
- `configuration/` 在两种布局下都只是运行时副本；静态配置与字体源内容仍在 `configuration_files/`。

## 4. plugin-runtime 与根目录 plugin 的边界

当前插件链路的关键约束是：

- 每个业务插件 target 的真实输出先进入 `${CMAKE_BINARY_DIR}/plugin-runtime`
- Visual Studio 生成器下，插件 target 还会关闭 `VcpkgApplocalDeps` 和 `VcpkgXUseBuiltInApplocalDeps`
- 构建完成后，只把插件 DLL 本身同步回最终运行目录 `${SGS_PLUGIN_DIR}`

这么做是为了隔离两类污染：

1. vcpkg / MSBuild applocal 自动复制出来的额外 DLL
2. 切换 Debug / Release 时根目录 `plugin/` 中遗留的另一配置残留文件

对维护者来说，最重要的判断规则是：

- `plugin-runtime/` 是 build tree staging 区
- 根目录 `plugin/` 才是应用真正加载插件的运行目录

不要把这两个目录混为一谈。

## 5. 为什么 launch 前清理只能放在 VS Code 层

过去踩过的坑是：

- 如果把“清空根目录 plugin”放进默认 CMake target 依赖链，它会在 Run 前增量构建检查时被执行
- 一旦本次点击 Run 没有触发插件 target 真实重编，POST_BUILD 也不会重新同步插件 DLL
- 结果就是启动前把 `plugin/` 删空，然后程序因插件缺失或配置混装而直接失败

因此当前允许的自动保护只有一种：

- 在 VS Code 的 launch `preLaunchTask` 里，使用显式的准备任务完成“停进程 -> 清 plugin -> configure/build -> 再同步 plugin-runtime”这整条恢复链

这和“把清理动作挂进通用构建依赖”是两回事。

## 6. 当前 VS Code 的约束

当前 VS Code 约束已经收敛为：

- 普通 configure 任务 `CMake Configure Release` / `CMake Configure Debug` 已直接使用仓库根作为 `-S ${workspaceFolder}`，并显式传 `-DSGS_RUNTIME_LAYOUT=repo-root`
- 普通构建任务仍保留 `CMake Build Release` / `CMake Build Debug`
- 启动前保护改由 `Prepare Release Launch Runtime` / `Prepare Debug Launch Runtime` 承担
- 这两个任务调用 [.vscode/prepare-launch-runtime.ps1](../../.vscode/prepare-launch-runtime.ps1)
- `Clean Plugin Folder` 与 `Rebuild Release After Plugin Clean` 是显式恢复入口，不是默认日常链路

该脚本当前负责：

1. 停止 `SGStudio` 与 `SGStudiod`
2. 递归清理根目录 `plugin/`
3. 以仓库根为 `-S` 对目标 build tree 执行 configure，显式传入 `-DSGS_RUNTIME_LAYOUT=repo-root`
4. 执行对应配置的 build
5. 构建结束后，再把 `build/.../plugin-runtime/` 的内容显式同步回根目录 `plugin/`

这样做的目的不是取代普通 build，而是保证“切配置启动”时根目录 `plugin/` 始终被纠正到当前 launch 配置。

## 7. 当前 Qt Creator 的约束

Qt Creator 用户的默认行为仍然应该是：

- 不显式传 `SGS_RUNTIME_LAYOUT`
- 继续使用 `build-tree`
- 把当前 build tree 的 `bin/`、`plugin/`、`configuration/` 当作自己的运行布局

因为启动代码会按可执行文件相对路径去查找 `../plugin`、`../configuration`，所以 Qt Creator 只要保持 `build-tree` 默认语义，运行时资源就应该和当前 build tree 绑定，而不是默认回到 repo-root。

Linux Qt 平台插件需要单独区分开发与打包布局：

- 如果可执行文件相邻 `platforms/` 中存在 xcb 或 Wayland 插件，启动代码按打包布局隔离到相邻 Qt 插件目录。
- Qt Creator build-tree 默认不复制 Qt 平台插件；`src/app/CMakeLists.txt` 在 configure 时通过当前 Kit/qmake 的 `QT_INSTALL_PLUGINS` 查询同源插件根，作为 build-tree fallback 编译进应用。
- 不要把 `/opt/Qt/...` 硬编码进 `main.cpp`，也不要只依赖 `QLibraryInfo::PluginsPath`；可重定位 Qt 安装可能保留历史部署前缀，而 qmake query 才是当前 Kit 的权威路径。
- build-tree fallback 只在相邻包内插件不存在且调用方没有显式提供插件环境时生效，不改变正式归档的包内运行时隔离。

如果你要为 Qt Creator 新增任何 helper target 或自动清理逻辑，必须先回答两个问题：

1. 它会不会在没有源码变化的 Run 前检查里仍然执行？
2. 如果执行了，而插件 target 没有实际重编，它能不能自行把运行目录恢复回来？

只要有一个问题回答不清楚，就不要把它挂进 Qt Creator 默认链路。

## 8. build tree 复用边界

同一个 build tree 不能混用下面这些关键维度：

- 当前 official 仓库根入口与其他历史 / 辅助 configure 入口
- `SGS_RUNTIME_LAYOUT=build-tree` 与 `SGS_RUNTIME_LAYOUT=repo-root`
- 不同 branding profile

如果这些维度发生切换，正确做法是：

1. 删除旧的 `build/cmake-win-*` 或对应 IDE build tree
2. 重新 configure
3. 再 build

不要试图在同一个缓存目录里来回切换后继续相信旧的 configure 结果。

## 9. 推荐操作

### 9.1 VS Code 日常开发

推荐顺序：

1. 使用普通 configure / build 任务进行日常增量开发
2. 点击 `Launch SGStudio Release` 或 `Debug SGStudio` 时，让 launch prepare 任务自动纠正根目录 `plugin/`

### 9.2 Qt Creator 日常开发

推荐顺序：

1. 使用默认 `build-tree` configure
2. 保持 build tree 内的运行资源与输出目录语义
3. 不要依赖 VS Code 的 repo-root launch 保护脚本来修复 Qt Creator 的运行目录

## 10. 对后续修改者的硬约束

如果你后续还要改这套机制，至少遵守下面五条：

1. 不要把 `SGS_RUNTIME_LAYOUT` 的默认值改成 `repo-root`。
2. 不要让 VS Code 依赖默认值，相关任务必须继续显式传 `repo-root`。
3. 不要让插件 target 重新直接输出到最终运行目录 `plugin/`。
4. 不要把“清空根目录 plugin”重新挂回通用 CMake target 依赖。
5. 不要把 repo-root `configuration/` 当成源目录维护；静态配置与字体源内容仍在 `configuration_files/`。
6. 只要切了 source entry、runtime layout 或 branding profile，就不要复用旧 build tree。
