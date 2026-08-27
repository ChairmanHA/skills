# Dual Runtime Layout Option Plan

## 背景

当前 repo root 已经成为正式的 CMake source entry，但运行输出语义也被一起切到了 build tree：

- 顶层 [CMakeLists.txt](../../CMakeLists.txt) 现在把 `SGS_BIN_PATH` / `SGS_LIBRARY_PATH` / `SGS_PLUGIN_PATH` 绑定到 `CMAKE_BINARY_DIR`
- [configuration_files/CMakeLists.txt](../../configuration_files/CMakeLists.txt) 把 variant 配置和 fonts 拷贝到 `build/...`
- [3rdParty/h2_api/CMakeLists.txt](../../3rdParty/h2_api/CMakeLists.txt) 把 `CalFile` 复制到 `build/.../bin/CalFile`

这导致两个问题：

1. VS Code 现有 `Launch SGStudio Release` / `Debug SGStudio` 仍然固定从 repo root `bin/` 启动，但构建产物和运行资源已经转移到 build tree，工作流直接失配。
2. Qt Creator 如果继续沿用当前默认 build tree 布局，`configuration/`、`fonts/`、`bin/CalFile` 等资源又必须在多个 build dir 下各复制一份，形成维护负担。

用户要求的目标不是取消 Qt Creator 当前习惯，而是：

- **默认行为保留给 Qt Creator**：继续使用 build-tree 运行布局。
- **VS Code 显式选择 repo-root 运行布局**：恢复并固定到旧文档中的 `bin/` / `lib/` / `plugin/` / `configuration/` / `fonts/` 语义。
- **同一个 CMake 工程支持两套显式布局**，但不能依赖 IDE 自动检测；必须由缓存选项决定。

## 目标

1. 新增一个显式 CMake 选项，控制运行时输出布局。
2. 默认值保持 build-tree 布局，保证 Qt Creator 用户不开任何额外参数也能继续当前逻辑。
3. VS Code 所有日常开发任务显式传入 repo-root 布局，恢复旧工作流。
4. 运行时相关资源不再各自硬编码 `CMAKE_BINARY_DIR/...`，统一走布局变量。
5. `Launch SGStudio Release` / `Debug SGStudio` 名称、程序路径、cwd、用户使用习惯不变。

## 非目标

1. 本轮不处理 stage / packaging / maintenance 打包链。
2. 本轮不要求删除 build-tree 布局；只要求把它降级为显式可选、默认兼容语义。
3. 本轮不改应用内相对路径约定。应用仍然按 `../configuration`、`../fonts`、`bin/CalFile` 这套运行目录关系工作。

## 方案总览

### 1. 新增显式布局选项

在 repo root [CMakeLists.txt](../../CMakeLists.txt) 新增缓存字符串：

- `SGS_RUNTIME_LAYOUT`

建议候选值：

- `build-tree`
- `repo-root`

建议默认值：

- `build-tree`

原因：

- 这样 Qt Creator 用户在当前阶段不需要理解新的参数，也不会突然失去现有 build-tree 运行方式。
- VS Code 任务可以显式传 `-DSGS_RUNTIME_LAYOUT=repo-root`，确保 repo-root 工作流不受默认值影响。

### 2. 统一布局变量，而不是到处直接写路径

在 repo root [CMakeLists.txt](../../CMakeLists.txt) 中新增统一布局变量：

- `SGS_ROOT_DIR`
- `SGS_SOURCE_DIR`
- `SGS_3RDPARTY_DIR`
- `SGS_RUNTIME_BASE_DIR`
- `SGS_BIN_DIR`
- `SGS_LIB_DIR`
- `SGS_PLUGIN_DIR`
- `SGS_CONFIGURATION_DIR`
- `SGS_FONTS_DIR`

语义建议：

- 当 `SGS_RUNTIME_LAYOUT=build-tree` 时：
  - `SGS_RUNTIME_BASE_DIR = ${CMAKE_BINARY_DIR}`
  - `SGS_BIN_DIR = ${CMAKE_BINARY_DIR}/bin`
  - `SGS_LIB_DIR = ${CMAKE_BINARY_DIR}/lib`
  - `SGS_PLUGIN_DIR = ${CMAKE_BINARY_DIR}/plugin`
  - `SGS_CONFIGURATION_DIR = ${CMAKE_BINARY_DIR}/configuration`
  - `SGS_FONTS_DIR = ${CMAKE_BINARY_DIR}/fonts`
- 当 `SGS_RUNTIME_LAYOUT=repo-root` 时：
  - `SGS_RUNTIME_BASE_DIR = ${SGS_ROOT_DIR}`
  - `SGS_BIN_DIR = ${SGS_ROOT_DIR}/bin`
  - `SGS_LIB_DIR = ${SGS_ROOT_DIR}/lib`
  - `SGS_PLUGIN_DIR = ${SGS_ROOT_DIR}/plugin`
  - `SGS_CONFIGURATION_DIR = ${SGS_ROOT_DIR}/configuration`
  - `SGS_FONTS_DIR = ${SGS_ROOT_DIR}/fonts`

然后保留现有变量兼容名：

- `SGS_BIN_PATH = ${SGS_BIN_DIR}`
- `SGS_LIBRARY_PATH = ${SGS_LIB_DIR}`
- `SGS_PLUGIN_PATH = ${SGS_PLUGIN_DIR}`

这样下层已有 `SGS_PLUGIN_PATH` 使用点基本无需大改。

### 3. VS Code 不依赖默认值，显式固定 repo-root 语义

修改 [.vscode/settings.json](../../.vscode/settings.json)：

- `cmake.sourceDirectory` 改成 `${workspaceFolder}`
- `cmake.configureSettings` 中增加 `SGS_RUNTIME_LAYOUT=repo-root`

修改 [.vscode/tasks.json](../../.vscode/tasks.json)：

- 所有日常 configure 任务改成 `-S ${workspaceFolder}`
- 所有日常 configure 脚本调用改成 `-SourceDir ${workspaceFolder}`
- 所有 VS Code 使用的 configure 命令显式追加 `-DSGS_RUNTIME_LAYOUT=repo-root`

这样即使默认值仍是 `build-tree`，VS Code 也永远不会走错布局。

## 文件级修改方案

### A. repo root [CMakeLists.txt](../../CMakeLists.txt)

需要完成四件事：

1. 定义 `SGS_RUNTIME_LAYOUT` 缓存选项和候选值。
2. 计算 `SGS_ROOT_DIR`、`SGS_SOURCE_DIR`、`SGS_3RDPARTY_DIR`。
3. 按布局选项派生 `SGS_BIN_DIR`、`SGS_LIB_DIR`、`SGS_PLUGIN_DIR`、`SGS_CONFIGURATION_DIR`、`SGS_FONTS_DIR`。
4. 用这些变量重新设置：
   - `SGS_BIN_PATH`
   - `SGS_LIBRARY_PATH`
   - `SGS_PLUGIN_PATH`
   - `CMAKE_RUNTIME_OUTPUT_DIRECTORY*`
   - `CMAKE_ARCHIVE_OUTPUT_DIRECTORY*`
   - `CMAKE_LIBRARY_OUTPUT_DIRECTORY*`

额外建议：

- 增加 `message(STATUS ...)` 输出当前布局模式和关键目录，便于用户一眼确认 configure 结果。

### B. [configuration_files/CMakeLists.txt](../../configuration_files/CMakeLists.txt)

这是本轮必须改的核心文件。

当前问题：

- `CONFIG_DST_DIR` 写死为 `${CMAKE_BINARY_DIR}/configuration`
- fonts 写死复制到 `${CMAKE_BINARY_DIR}/fonts`

改法：

- `CONFIG_DST_DIR` 改为 `${SGS_CONFIGURATION_DIR}`
- fonts 复制目标改为 `${SGS_FONTS_DIR}`

这样同一份配置资源会跟随布局选项自动落到：

- Qt Creator 默认的 build tree
- VS Code 明确指定的 repo root

### C. [3rdParty/h2_api/CMakeLists.txt](../../3rdParty/h2_api/CMakeLists.txt)

当前问题有两类：

1. fallback 目标目录仍写死到 `CMAKE_BINARY_DIR/bin` 和 `CMAKE_BINARY_DIR/lib`
2. `CalFile` 直接拷贝到 `${CMAKE_BINARY_DIR}/bin/CalFile`

改法：

- fallback 从 `CMAKE_BINARY_DIR/...` 改成统一变量：
  - runtime fallback -> `${SGS_BIN_DIR}`
  - library fallback -> `${SGS_LIB_DIR}`
- `CalFile` 复制目标改成 `${SGS_BIN_DIR}/CalFile`

这样可以消除“必须手工往多个 build 目录各复制一份 CalFile”的痛点。

### D. [3rdParty/modulation/CMakeLists.txt](../../3rdParty/modulation/CMakeLists.txt)

虽然当前没有单独的额外资源目录，但它也保留了 `CMAKE_BINARY_DIR/bin/lib` fallback。

改法与 h2_api 一致：

- runtime fallback -> `${SGS_BIN_DIR}`
- library fallback -> `${SGS_LIB_DIR}`

目的不是修现有 bug，而是防止未来某次变量缺失时又悄悄退回 build tree。

### E. [.vscode/settings.json](../../.vscode/settings.json)

需要改两处：

1. `cmake.sourceDirectory=${workspaceFolder}`
2. `cmake.configureSettings` 中加入：
   - `SGS_RUNTIME_LAYOUT=repo-root`

可选但推荐：

- 若后续希望 CMake Tools 面板中的 Configure 也严格复现 VS Code 任务行为，可以把 repo-root 相关默认参数都放进 `configureSettings`，任务中只覆盖少量差异。

### F. [.vscode/tasks.json](../../.vscode/tasks.json)

本轮只聚焦日常开发链路，至少要改：

- `CMake Configure Release`
- `CMake Configure Debug`
- `CMake Build Release`
- `CMake Build Debug`
- `Rebuild Release After Plugin Clean`

修改原则：

1. `-S ${workspaceFolder}/src` -> `-S ${workspaceFolder}`
2. configure 命令追加 `-DSGS_RUNTIME_LAYOUT=repo-root`
3. 任务 label 保持不变
4. build dir 名称保持不变

关于 neutralized / russian / stage 任务 当前不做忽略：

- 这轮设计稿中先记录但不要求立即实施。
- 真正落代码时，如果这些任务仍保留在工作区中，建议一并补上 `-S ${workspaceFolder}` 和布局参数，避免后续有人误触时走回旧入口。

### G. [.vscode/launch.json](../../.vscode/launch.json)

原则上不改。

理由：

- 当前 `program` 已固定到 repo root `bin/SGStudio.exe` / `bin/SGStudiod.exe`
- `cwd` 已固定到 repo root `bin`
- 这正是 VS Code repo-root 工作流想保住的行为

前提是：

- VS Code 的 preLaunchTask 对应 configure/build 必须显式使用 `repo-root` 布局。

## 兼容性边界

### Qt Creator 用户

- 默认不传 `SGS_RUNTIME_LAYOUT` 时，仍然得到 build-tree 运行布局。
- `configuration/`、`fonts/`、`bin/CalFile` 都会自动同步到该 build tree。
- 不再需要手工往多个 Qt Creator build dir 下补 `CalFile`。

### VS Code 用户

- 通过 tasks/settings 显式传 `SGS_RUNTIME_LAYOUT=repo-root`
- 仍使用根目录 `bin/`、`plugin/`、`configuration/`、`fonts/`
- `Launch SGStudio Release` / `Debug SGStudio` 入口不变

### 风险边界

- 同一个 build dir 不允许混用两种布局配置。
- 如果同一个 `build/cmake-win-release` 先用 `build-tree` configure，再改成 `repo-root` configure，必须清理缓存后重来。
- 这不是缺陷，而是 CMake cache 的正常边界。

## 验证方案

### 验证 A：Qt Creator 兼容模式

目标：证明默认值仍可工作。

检查项：

1. 不传 `SGS_RUNTIME_LAYOUT`，直接 configure 一个新的 build tree。
2. 确认 `build/.../configuration/Settings.ini` 存在。
3. 确认 `build/.../fonts/` 存在。
4. 确认 `build/.../bin/CalFile` 存在。
5. 确认可执行文件、dll、plugin 仍落在同一个 build tree 语义下。

## 2026-04-29 后续实施补充

在上一轮把 `SGS_RUNTIME_LAYOUT` 与 plugin staging 收敛后，还需要补两件工程化收口工作：

### 1. VS Code launch 前置保护

问题边界：

- 当前 [.vscode/launch.json](../../.vscode/launch.json) 里的 `Launch SGStudio Release` / `Debug SGStudio` 仍直接依赖普通 `CMake Build Release` / `CMake Build Debug`。
- 一旦根目录 `plugin/` 混入另一配置残留，直接启动当前配置仍可能出现 `Cannot mix debug and release libraries`。
- 这个保护必须只加在 VS Code 的 launch 前置流程里，不能重新挂回插件 target 的默认构建依赖，否则会重回“Run 前把 plugin 清空但插件未重编”的旧坑。

实施策略：

- 新增一个 `.vscode/prepare-launch-runtime.ps1` 脚本，显式负责：
  - 停止 `SGStudio` 与 `SGStudiod`
  - 递归清理根目录 `plugin/`
  - 对目标 build tree 执行 configure，显式传 `-DSGS_RUNTIME_LAYOUT=repo-root`
  - 执行对应配置的 `cmake --build`
  - 构建成功后，若 `build/.../plugin-runtime/` 存在，则再显式把其中内容同步回根目录 `plugin/`
- 在 [.vscode/tasks.json](../../.vscode/tasks.json) 中新增面向 launch 的专用任务，而不是复用普通 build 任务。
- 在 [.vscode/launch.json](../../.vscode/launch.json) 中把两个 launch 配置的 `preLaunchTask` 切到新的“Prepare ... Launch Runtime”任务。

这样可以保证：

- 日常手工 build 仍保持增量行为，不强制每次清理。
- VS Code 点击启动/调试时，会先把根目录 `plugin/` 纠正到当前配置语义。
- 保护层只存在于 launch 入口，不污染 Qt Creator 默认行为。

### 2. 补正式 KnowledgeBase 文档

问题边界：

- 现有 [.github/KnowledgeBase/cmake_build_output_clean_run_workflow.md](../KnowledgeBase/cmake_build_output_clean_run_workflow.md) 解释了运行目录、plugin 清理边界和单目录覆盖部署，但没有把 `SGS_RUNTIME_LAYOUT=build-tree/repo-root` 的“双布局显式选择”写成正式约束。
- 当前知识库索引 [.github/KnowledgeBase/Index.md](../KnowledgeBase/Index.md) 里也没有一篇专门面向维护者的“repo-root vs build-tree”说明。

实施策略：

- 新增一篇独立文档，明确说明：
  - 默认值为何必须保留给 Qt Creator 的 `build-tree`
  - VS Code 为什么必须显式传 `repo-root`
  - 根目录 `bin/` / `plugin/` / `configuration/` / `fonts/` 与 build tree 对应目录的职责差异
  - `plugin-runtime` staging 与根目录 `plugin/` 的职责边界
  - launch 前自动清理只适用于 VS Code 的 repo-root 运行保护，不适合作为通用 CMake 依赖
  - 切换 source entry 或 runtime layout 后的 build tree 复用边界
- 把新文档加入 [.github/KnowledgeBase/Index.md](../KnowledgeBase/Index.md) 的“部署与工程”章节。

验收标准：

- VS Code 的 Release / Debug launch 前会自动做 plugin runtime 纠正。
- 根目录 `plugin/` 不再依赖用户手工清理来切配置。
- KnowledgeBase 中可以单独找到“repo-root 与 build-tree 双布局约束”的正式文档入口。

## 2026-04-29 构建回归补充

### 新发现的回归

在 repo-root 布局下，Release 构建实际失败点不是 configure，而是插件链接后的 applocal 阶段：

- 当前 [src/plugins/CMakeLists.txt](../../src/plugins/CMakeLists.txt) 直接把插件 target 的真实输出目录设置成 `${SGS_PLUGIN_PATH}`。
- 当 `SGS_RUNTIME_LAYOUT=repo-root` 时，这个目录就是仓库根 `plugin/`。
- vcpkg / MSBuild applocal 会把插件运行时依赖也尝试部署到该目录，最终在 Core 插件构建中生成了带通配符的 `libcrypto-*-x64.dll` / `libssl-*-x64.dll` 记录，触发 `MSB3541`。

### 修正策略

这部分不应通过禁用插件构建、修改 launch，或让 VS Code 回退旧入口来规避，而应恢复插件输出边界：

1. 新增统一的插件 staging 目录：`SGS_PLUGIN_BUILD_DIR=${CMAKE_BINARY_DIR}/plugin-runtime`
2. 插件 target 的真实链接输出固定落到 `plugin-runtime/`
3. 实际运行目录仍使用布局变量决定的 `${SGS_PLUGIN_DIR}`
4. 每个插件在 `POST_BUILD` 里只把插件 DLL 本体同步到 `${SGS_PLUGIN_DIR}`
5. 不把“清空根 plugin 目录”重新挂回常规构建链，避免 Qt Creator / Run 前增量检查再次清空运行目录

### 预期结果

- Qt Creator 默认的 build-tree 模式仍然从 build tree 的 `plugin/` 运行，不需要用户额外改参数。
- VS Code 显式 repo-root 模式仍然从仓库根 `plugin/` 运行。
- applocal 污染被重新限制在 `build/.../plugin-runtime`，不再阻塞 repo-root 日常构建。

### 2026-04-29 实施收口记录

实际落地时，单纯恢复 `plugin-runtime` staging 还不够：

- 在当前 Visual Studio + vcpkg 集成下，插件 target 即使输出到 `plugin-runtime/`，MSBuild 仍会对该 target 注入 applocal 复制，并生成 `libcrypto-*-x64.dll` / `libssl-*-x64.dll` 这类通配符路径，继续触发 `MSB3541`。
- 最终稳定解法是在插件公共 helper 中同时做两件事：
  1. 插件真实输出固定到 `${SGS_PLUGIN_BUILD_DIR}`，只把插件 DLL 本体同步到 `${SGS_PLUGIN_DIR}`
  2. 对 Visual Studio 生成器下的插件 target 显式设置 `VcpkgApplocalDeps=false` 与 `VcpkgXUseBuiltInApplocalDeps=false`

另外，验证阶段发现显式清理任务也需要同步修正：

- `Clean Plugin Folder` / `Rebuild Release After Plugin Clean` 需要使用 `process` 类型，避免 shell 二次转义破坏 PowerShell 参数。
- 清理逻辑必须递归删除根 `plugin/` 下的全部内容，不能只删顶层 `.dll/.manifest`，否则历史 `plugin/plugins/...` 子目录中的 Qt release 插件仍会污染 Debug 启动。

### 本轮验证结论

- `CMake Build Release` 已通过，`Core.vcxproj` 中确认写入 `VcpkgApplocalDeps=false`，不再出现 `MSB3541`。
- `CMake Configure Debug` 与 `CMake Build Debug` 已通过。
- 递归清理 `plugin/` 并重建 Debug 后，Debug 日志不再出现“Cannot mix debug and release libraries” 的旧污染症状。
- 通过 `Start-Process -PassThru` 校验，`SGStudiod.exe` 与 `SGStudio.exe` 均可在当前 repo-root 运行布局下成功拉起。

### 验证 B：VS Code repo-root 模式

目标：证明旧工作流恢复。

检查项：

1. `CMake Configure Release` 成功。
2. `CMake Build Release` 成功。
3. 根目录 `configuration/Settings.ini`、`fonts/`、`bin/CalFile` 都处于正确状态。
4. `Launch SGStudio Release` 可直接启动。
5. `CMake Configure Debug`、`CMake Build Debug`、`Debug SGStudio` 同理通过。

### 验证 C：不再需要双份资源

目标：证明用户痛点真正解决。

检查项：

1. VS Code 不再需要把 `CalFile` 复制到 Qt Creator 的各个 build dir。
2. Qt Creator 新建一个独立 build dir 后，`CalFile` 会自动到位。
3. VS Code repo-root 模式下，根目录 `bin/CalFile` 也自动到位。

## 建议实施顺序

1. 先改 repo root [CMakeLists.txt](../../CMakeLists.txt)，引入布局选项和统一目录变量。
2. 再改 [configuration_files/CMakeLists.txt](../../configuration_files/CMakeLists.txt)，让配置资源跟布局变量走。
3. 再改 [3rdParty/h2_api/CMakeLists.txt](../../3rdParty/h2_api/CMakeLists.txt) 和 [3rdParty/modulation/CMakeLists.txt](../../3rdParty/modulation/CMakeLists.txt)，消除 fallback 和 `CalFile` 的 build-tree 写死点。
4. 最后改 VS Code 的 [settings.json](../../.vscode/settings.json) 和 [tasks.json](../../.vscode/tasks.json)，把 repo-root 模式显式固定下来。
5. 清理 VS Code 的旧 build tree 后做 Release / Debug 回归。

## 需要用户确认的问题

### 1. 选项命名

建议名称：

- `SGS_RUNTIME_LAYOUT`

建议取值：

- `build-tree`
- `repo-root`

同意

### 2. 默认值

当前方案建议默认 `build-tree`，确保 Qt Creator 用户零改动继续工作。

请确认你是否接受这个默认值。接受

### 3. VS Code 覆盖范围

当前建议是：

- 先把日常开发链路的 VS Code 任务全部固定为 `repo-root`
- stage / neutralized / russian 任务暂不在本轮实施，但建议后续统一补齐

如果你希望本轮就把工作区里所有 CMake 任务都统一加上该选项，也可以直接一次性做完。
不要一次性做完，保证日常开发的任务即可，所有其他任务都没有必要在这轮改动中覆盖。
