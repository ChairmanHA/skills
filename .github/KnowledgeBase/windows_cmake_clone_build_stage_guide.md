# Windows Clone 后 CMake 编译与 Stage 打包指南

本文档面向两类读者：

- 在新机器上第一次 clone 本仓库，需要尽快把工程编起来、跑起来、打出可交付包的人。
- 需要理解当前 Windows 打包链路到底是谁负责 Qt 依赖、谁负责第三方 DLL 的维护者。

本文只描述仓库当前已经实现并验证过的工作流，不讨论历史 qmake 流程。

## 1. 先说结论

当前 Windows 下的编译与打包策略是两段式：

- Qt 体系依赖：交给 Qt 自带的 `windeployqt`。
- 非 Qt 第三方依赖：交给 CMake 的显式 install/copy 与 `file(GET_RUNTIME_DEPENDENCIES)` 分析后再复制到包内。

这不是权宜之计，而是当前项目有意采用的做法。

原因是：

- `windeployqt` 最擅长处理 Qt 自身的 DLL、Qt plugins、translations、平台插件等依赖闭包。
- `xlsx.dll`、`libcurl.dll`、`zip.dll`、`zlib1.dll`、`h2_api.dll`、`gensignalwave.dll` 这类第三方 DLL，并不是 `windeployqt` 的职责范围。
- 如果把第三方 DLL 也强行喂给 `windeployqt`，容易得到不稳定或直接失败的结果。

所以当前项目的目标不是“一个工具全包”，而是“Qt 依赖和第三方依赖各走最合适的链路”。

## 2. 当前目录模型

仓库根目录下和 Windows 运行最相关的路径如下：

- `src/`：CMake source tree。
- `build/cmake-win-release/`：Release build tree。
- `build/cmake-win-debug/`：Debug build tree。
- `bin/`：本地直接运行 `SGStudio.exe` 的实际运行目录。
- `plugin/`：业务插件目录。
- `stage-assets/`：仅供 stage 打包使用的附加交付物清单目录。
- `stage/SGStudio_v..._Win64/`：stage 打包输出目录。

这意味着：

- 日常开发时，真正运行的是根目录 `bin/SGStudio.exe` 与根目录 `plugin/`。
- 交付包则来自 `stage/SGStudio_v..._Win64/`。

## 3. 新机器 clone 后的前置条件

新机器第一次使用前，至少需要满足下面这些前提。

### 3.1 必装软件

1. Visual Studio 2022，并安装 C++ 桌面开发工具链。
2. CMake。
3. Qt 5.15.9 `msvc2022_64`。
4. Windows PowerShell。

### 3.2 Qt 安装路径要求

当前 VS Code 任务和 stage 脚本默认使用下面这个 Qt 路径：

```text
C:/Qt/5.15.9/msvc2022_64
```

如果你的 Qt 不在这个位置，有两种做法：

1. 修改 [.vscode/tasks.json](../../.vscode/tasks.json) 中各个任务的 `CMAKE_PREFIX_PATH`，并在直接调用脚本时传入正确的 `-QtPrefixPath`。

### 3.3 3rdParty 第三方依赖要求

除 Qt 外，当前 Windows CMake 工作流要求第三方依赖统一放在仓库根目录 `3rdParty/` 下。

其中 `libcurl`、`libzip`、`xlsx`、`htra`、`modulation` 都通过各自目录中的 `cmake/*Config.cmake` + imported targets 接入，不再依赖 vcpkg 在线下载或自动补装。

新机器 clone 后，需要确认下面这些目录存在且内容完整：

- `3rdParty/libcurl`
- `3rdParty/libzip`
- `3rdParty/xlsx`
- `3rdParty/htra`
- `3rdParty/modulation`

### 3.4 clone 后不需要另外准备的内容

当前仓库已经自带若干第三方二进制，例如：

- `3rdParty/libcurl/bin/libcurl.dll`
- `3rdParty/libzip/bin/zip.dll`
- `3rdParty/xlsx/bin/xlsx.dll`
- `3rdParty/htra/h2_api.dll`
- `3rdParty/modulation/gensignalwave.dll`
- `stage-assets/h2_usbdriver.7z`
- `stage-assets/data/16QAM.wav`

因此新机器 clone 之后，不需要再单独手工编译这些仓库内已随源代码提供的第三方 DLL。

## 4. 第一次打开仓库后的推荐步骤

推荐直接打开工作区：

```text
vsg2.0.code-workspace
```

然后按下面顺序执行。

### 4.1 第一步：运行Cmake Stage Release

构建打包完成后，到Stage文件夹下，会找到相应的`SGStudio_v<版本>_Win64`文件夹，里面就是打包好的产物了, 可以直接运行 `bin/SGStudio.exe` 来验证。

然后，把`SGStudio_v<版本>_Win64/bin/`下的所有DLL(除了Qt DLL,本身已经有开发环境)都复制到根目录`bin/`下，覆盖原有文件。

以后日常就是ctrl+F5运行SGStudio.exe了,或者F5调试SGStudio.exe了。

## 5. 日常开发时当前的实际输出逻辑

### 5.1 主程序和普通共享库输出到哪里

当前工程会把主程序和普通共享库输出到根目录 `bin/`，例如：

- `bin/SGStudio.exe`
- `bin/Utils.dll`
- `bin/ExtensionSystem.dll`
- `bin/Controls.dll`
- `bin/Business.dll`

### 5.2 插件输出到哪里

插件 target 的真实输出先进入 build tree 的 `plugin-runtime/`，然后构建后再同步回根目录 `plugin/`。

这样做的目的是避免插件目标在构建时把附带运行库污染到最终插件目录。

### 5.3 本地直接运行时依赖什么

本地直接运行依赖三部分：

- 根目录 `bin/`
- 根目录 `plugin/`
- 根目录 `configuration/`

不要把 build tree 当成最终运行目录。

## 6. 当前 stage 打包逻辑到底怎么实现

当前 stage 打包的入口有两个等价方式：

1. VS Code 任务 `CMake Stage Release`
2. 脚本 [.vscode/cmake-stage-release.ps1](../../.vscode/cmake-stage-release.ps1)

脚本做的事情很直接：

1. 先重新 configure 当前 build tree。
2. 默认直接执行 `sgstudio_stage` 目标。

这里要特别注意：`sgstudio_stage` 不会把开发期根目录 `bin/` 整目录原样复制到 stage 包。当前 stage 包中的 `bin/` 是通过 install 规则、`windeployqt` 与运行时依赖扫描共同“重建”出来的最小可运行集合。

因此：

- stage 包中出现自己的 `bin/` 是预期行为；
- 但它与开发期根 `bin/` 不需要逐文件完全一致；
- `debug.log`、Debug DLL、`.manifest`、开发期辅助文件未进入 stage 包通常是正常的；
- 如果某个 stage 包只有在手工覆盖“上一次的 `bin/`”后才能运行，应优先检查 install/runtime 规则，而不是把手工覆盖当成正式发布流程。

如果显式传入 `-BuildBeforeStage`，脚本会在 configure 之后先执行一次 `ALL_BUILD`，再执行 `sgstudio_stage`。这个入口主要给需要覆盖 `SGS_PROJECT_VERSION` 一类编译期宏的 interactive stage 任务使用，避免 stage 包目录名更新了，但二进制仍沿用旧 build cache 的版本信息。

### 6.1 Qt 依赖怎么进包

现在不是只对 `SGStudio.exe` 跑一次 `windeployqt`，而是对下面这些产物一起处理：

- stage 包内的主程序 `SGStudio.exe`
- stage 包内 `bin/` 下的内部共享库
- stage 包内 `plugin/` 下的业务插件 DLL

这样做是因为 Qt 依赖未必只从主程序可见。

例如这次已经验证过的情况是：

- `Utils.dll` 直接依赖 `Qt5Xml.dll`
- 但 `SGStudio.exe` 本身并不直接依赖 `Qt5Xml.dll`

如果只对主程序跑 `windeployqt`，这种依赖就会漏掉。

### 6.2 第三方依赖怎么进包

第三方依赖分两类进入 stage 包：

#### A. 显式 install/copy

适合那些已经明确知道由哪个 target 负责的第三方 DLL。

例如 `Utils.dll` 依赖的 `xlsx.dll`，当前已经通过 [src/libs/utils/CMakeLists.txt](../../src/libs/utils/CMakeLists.txt) 明确加入 build copy 和 stage install。

#### B. 运行时依赖扫描补齐

stage 脚本在 `windeployqt` 完成后，会对下面的对象继续做运行时依赖分析：

- stage 包中的 `SGStudio.exe`
- stage 包中的所有业务插件 DLL

搜索目录会优先使用：

- `stage/.../bin`
- `stage/.../plugin`
- `3rdParty` 中的相关目录

然后把解析到的非 Qt、非系统 DLL 复制进 `stage/.../bin`。

这一步的职责是补齐 `windeployqt` 不负责的第三方依赖闭包。

### 6.3 为什么 Qt DLL 不交给第二段逻辑统一复制

因为 Qt DLL 的来源一致性非常重要。

当前策略是：

- 优先让 `windeployqt` 从同一套 Qt 安装目录部署 Qt 运行时。
- 运行时依赖分析只有在发现某个 Qt DLL 仍未进入 `stage/bin` 时，才允许补拷贝。

这比“无脑把所有 Qt 依赖也走通用复制逻辑”更稳，可以降低混装不同 Qt 版本的风险。

### 6.4 stage-assets 如何进入包

当前额外交付物不再散落在 VS Code task 或 PowerShell 包装脚本里，而是统一通过根目录 `stage-assets/` 下的清单接入。

当前已纳入的资源是：

- `stage-assets/h2_usbdriver.7z` -> `stage/SGStudio_v..._Win64/h2_usbdriver.7z`
- `stage-assets/data/16QAM.wav` -> `stage/SGStudio_v..._Win64/data/16QAM.wav`

实现边界如下：

- `.vscode/cmake-stage-release.ps1` 仍只负责 `configure + build sgstudio_stage`。
- 顶层 `CMakeLists.txt` 在 install 阶段执行 `stage-assets/install-stage-assets.cmake.in` 生成出的脚本，把这些文件复制进 stage 包。
- 如果这些必需资源缺失，stage 安装会直接失败，而不是静默产出一个不完整的发布包。

这套做法的目的，是把“发布包附带什么”固定在 CMake install 清单层，而不是让打包规则分散到多个脚本里。

## 7. 新机器上如何使用当前脚本完成打包

### 7.1 推荐方式：直接用 VS Code 任务

顺序如下：

1. `CMake Configure Release`
2. `CMake Build Release`
3. `CMake Stage Release`

打包结果会生成到：

```text
stage/SGStudio_v<版本>_Win64/
```

### 7.2 直接调用脚本的方式

如果不通过 VS Code 任务，也可以在仓库根目录下执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .vscode/cmake-stage-release.ps1 -SourceDir src -BuildDir build/cmake-win-release -QtPrefixPath C:/Qt/5.15.9/msvc2022_64
```

如果 Qt 安装路径不同，把 `-QtPrefixPath` 改成你的本机路径即可。

脚本也支持可选版本参数：

- `-Version`
- `-Timestamp`
- `-BuildBeforeStage`

不传则使用当前默认版本命名。

其中 `-BuildBeforeStage` 主要用于 VS Code 的 interactive stage 任务：先按输入 version 重新 configure，再完整构建 Release 二进制，最后再 stage，确保类似 `DeviceInfoWidget` 中 `SGS_VERSION` 这样的编译期版本宏与本次输入一致。

### 7.3 Neutralized branding 与默认 stage task 的关系

当前仓库已经支持 neutralized branding 的独立 configure/build task，但需要注意：

- `CMake Build Release Neutralized` 使用的是独立 build tree：
	- `build/cmake-win-release-neutralized/`
- 现有 `CMake Stage Release` 仍固定使用：
	- `build/cmake-win-release/`

因此，先执行 `CMake Build Release Neutralized`，再执行当前默认的 `CMake Stage Release`，**并不会**自动得到 neutralized 包。

当前默认 stage task 打出来的仍然是 official branding 的 Release stage 包。

如果未来需要 neutralized 包，应该新增与 `build/cmake-win-release-neutralized/` 对应的独立 stage task，而不是复用当前默认 stage task。

## 8. 当前已验证通过的结果

本轮实现已经验证过：

- stage 目录可以成功生成。
- `Qt5Xml.dll` 已进入 stage 包。
- `xlsx.dll` 已进入 stage 包。
- `h2_usbdriver.7z` 已进入 stage 包根目录。
- `data/16QAM.wav` 已进入 stage 包与 zip 包。
- 打包后的 `stage/.../bin/SGStudio.exe` 可以在当前机器上成功拉起。

这意味着当前工作流已经满足：

- Qt 依赖由 `windeployqt` 负责闭包。
- 第三方依赖由显式 install/copy 与运行时依赖分析补齐。

## 9. 新机器使用时最容易踩的坑

### 9.1 Qt 路径不一致

如果你的 Qt 不在 `C:/Qt/5.15.9/msvc2022_64`，而任务和脚本里仍然使用旧路径，那么 configure 或 stage 都会失败。

### 9.2 `3rdParty` 目录不完整

如果新机器缺少 `3rdParty/libcurl`、`3rdParty/libzip` 或其他仓库内第三方目录，configure 也许能通过，但链接、运行或 stage 补齐依赖时会失败。

### 9.3 手工清空 `plugin/` 后没有重新构建插件

根目录 `plugin/` 采用单目录覆盖部署模型。清空后如果没有重新构建插件，程序虽然能启动，但插件加载会失败。

### 9.4 把 build tree 当成运行目录

当前工程的最终运行目录是根目录 `bin/` 和 `plugin/`，不是 `build/...`。

### 9.5 stage 的未解析告警不等于一定不能运行

当前 stage 过程中仍可能看到个别未解析依赖告警。这些告警需要审查，但不代表每次都会阻塞主程序启动。

## 10. 推荐给维护者的原则

如果后续继续维护这条编译打包链路，建议坚持下面三条：

1. Qt 依赖继续交给 `windeployqt`，并且输入对象至少覆盖主程序、内部 Qt 库、业务插件。
2. 第三方 DLL 继续通过显式 install/copy 与运行时依赖分析补齐，不要混入 `windeployqt` 的职责。
3. 对那些业务上已明确长期存在的第三方 DLL，优先做显式 install，不要完全依赖运行时扫描兜底。

## 11. 相关文件

- [../../src/CMakeLists.txt](../../src/CMakeLists.txt)
- [../../.vscode/tasks.json](../../.vscode/tasks.json)
- [../../.vscode/cmake-stage-release.ps1](../../.vscode/cmake-stage-release.ps1)
- [../../src/libs/utils/CMakeLists.txt](../../src/libs/utils/CMakeLists.txt)
- [cmake_build_output_clean_run_workflow.md](cmake_build_output_clean_run_workflow.md)