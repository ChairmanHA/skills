# Windows 下 Qt 运行时混装导致 SVG 图标失效

本文记录一次 Windows / CMake / vcpkg 混合环境下的真实问题：Release 版本中，TitleBar 右上角最小化、最大化、关闭按钮的 SVG 图标不显示，但 Debug 正常。最终根因不是资源文件损坏，也不是单纯缺少 QtSvg 插件，而是运行目录中混装了两套不同版本的 Qt。

## 现象

- TitleBar 三个窗口控制按钮通过 QSS 的 `qproperty-icon` 指向 Qt 资源路径，例如 `:/Controls/Theme/min`。
- 图标资源本身是 `.svg`，位于 `src/libs/controls/resource/image/*.svg`，并通过 `controls.qrc` 暴露为 `:/Controls/Theme/*`。
- Debug 构建运行时图标正常显示。
- Release 构建运行时图标不显示。
- 用户确认资源文件本身没有问题。

## 初始误判

第一轮判断很容易落到“Release 少了 QtSvg 运行时或插件”：

- `Qt5::Svg` 未显式链接。
- `Qt5Svg.dll` 未显式复制到 `bin/`。
- `plugins/iconengines/qsvgicon.dll` 未显式复制到 `bin/plugins/iconengines/`。
- `plugins/imageformats/qsvg.dll` 未显式复制到 `bin/plugins/imageformats/`。

这条思路只对了一半。

补齐这些文件后，Qt 的确能枚举到 SVG 相关插件，但应用内图标仍然不显示，说明问题不只是“文件缺失”。

## 关键诊断现象

### 1. Qt 能发现插件，但插件在装载阶段失败

日志中可见：

- Qt 能发现 `qsvgicon.dll` 的 metadata。
- Qt 能发现 `qsvg.dll` 的 metadata。
- 但两者随后都在真正 `LoadLibrary` 时失败。

这说明问题不是插件目录没配对，而是插件依赖链或装载上下文有问题。

### 2. 手动 LoadLibrary 的最早失败点是 Qt5Svg.dll

在独立 PowerShell 中按顺序手动加载：

- `Qt5Core.dll` 成功
- `Qt5Gui.dll` 成功
- `Qt5Widgets.dll` 成功
- `Qt5Svg.dll` 失败，错误码 `127`
- `qsvg.dll` 失败
- `qsvgicon.dll` 失败

这里最关键的是：不是 `qsvg.dll` 先失败，而是 `Qt5Svg.dll` 自身先失败。既然 `qsvg.dll` 和 `qsvgicon.dll` 都依赖 `Qt5Svg.dll`，后续失败就是连带结果。

### 3. 运行目录中的 Qt 版本不一致

进一步比对 `bin/` 中各 DLL 的版本后，发现：

- `bin/Qt5Core.dll` 是 `5.15.13.0`
- `bin/Qt5Gui.dll` 是 `5.15.13.0`
- `bin/Qt5Widgets.dll` 是 `5.15.13.0`
- `bin/Qt5Svg.dll` 是 `5.15.9.0`
- `bin/plugins/imageformats/qsvg.dll` 是 `5.15.9.0`
- `bin/plugins/iconengines/qsvgicon.dll` 是 `5.15.9.0`

也就是说，核心 Qt 运行时来自一套版本，SVG 模块和插件来自另一套版本。

这是典型的 Qt 运行时混装。

## 根因

### vcpkg applocal 污染了 bin 目录

在当前工程里：

- CMake `find_package(Qt5 ...)` 明确命中 `C:/Qt/5.15.9/msvc2022_64`。
- 但构建阶段，vcpkg / MSBuild 的 applocal 机制仍会把另一套 Qt 运行时复制进 `bin/`。
- 结果 `bin/Qt5Core.dll`、`bin/Qt5Gui.dll`、`bin/Qt5Widgets.dll` 变成了 vcpkg 的 `5.15.13`。

随后又手工补了 `Qt5Svg.dll`、`qsvg.dll`、`qsvgicon.dll`，这些文件来自 `C:/Qt/5.15.9`。于是一个进程内同时存在：

- Qt 5.15.13 的 Core/Gui/Widgets
- Qt 5.15.9 的 Svg / qsvg / qsvgicon

这种组合不兼容。

### 为什么会报 127 而不是 126

Windows 常见装载错误中：

- `126` 更像“找不到指定模块”
- `127` 更像“找不到指定过程”

这里 `Qt5Svg.dll` 失败为 `127`，更符合“当前进程中已加载的是另一版本 Qt5Core/Qt5Gui/Qt5Widgets，导出符号或内部 ABI 不匹配”的情况，而不是单纯缺文件。

## 为什么 Debug 正常、Release 异常

本次问题里，Debug 与 Release 的差异不在资源文件本身，而在运行目录的实际 DLL 组合。

常见原因包括：

- Debug 和 Release 的部署动作不同。
- 一侧命中了 IDE / QtCreator / 开发环境 PATH 里的正确 Qt。
- 另一侧被 vcpkg applocal 或历史残留 DLL 污染。
- 插件目录或运行目录中同时残留 Debug / Release、旧版本 / 新版本 DLL。

因此“Debug 正常，Release 不正常”在 Qt 工程里并不能证明资源或代码逻辑有问题，优先要怀疑运行时版本一致性。

## 最终修复策略

修复不是“只补 QtSvg”，而是：

### 1. 统一整套 Qt 运行时来源

从 `find_package(Qt5 ...)` 命中的同一套 Qt 安装目录统一复制到 `bin/`：

- `Qt5Core/Gui/Widgets/Xml/PrintSupport/Svg/Network/Concurrent*.dll`
- `plugins/platforms/qwindows*.dll`
- `plugins/styles/qwindowsvistastyle*.dll`
- `plugins/imageformats/qgif/qico/qjpeg/qsvg*.dll`
- `plugins/iconengines/qsvgicon*.dll`
- `plugins/printsupport/windowsprintersupport*.dll`

关键点不是“复制更多文件”，而是“这些文件必须来自同一套 Qt”。

### 2. 用统一复制覆盖 applocal 残留

如果 vcpkg / MSBuild applocal 已经把另一套 Qt 复制进 `bin/`，那么 post-build 需要明确覆盖它，而不是只补一个缺少的 DLL。

### 3. 保留 Svg 显式依赖

Controls 仍应显式链接 `Qt5::Svg`，这样构建配置和运行时需求更清晰，也便于后续部署逻辑自动追踪 SVG 模块。

## 与 main.cpp 搜索路径修改的关系

`src/app/main.cpp` 中的 `configureRuntimeLibrarySearchPaths()` 会把 `../plugin` 放在 `bin` 前插入 `PATH`。

这段逻辑本身不是本次问题的最终根因，但它仍然是风险点：

- 如果 `plugin/` 被 applocal 污染进 Qt 或第三方 DLL，它可能改变优先加载顺序。
- 一旦存在同名 DLL 阴影，问题会非常难查。

因此结论不是“main.cpp 无关”，而是：

- 本次已证实的直接根因是 `bin/` 内 Qt 版本混装。
- `PATH` 搜索顺序修改会放大这类问题，后续仍要谨慎对待。

## 这类问题的排查清单

遇到 Qt 图标、样式、平台插件、图片格式插件在 Release 下异常时，建议按这个顺序查：

1. 先确认资源路径和 `.qrc` 映射是否正确。
2. 确认 Qt 是否能枚举到目标插件，而不是只看文件是否存在。
3. 手动 `LoadLibrary` 目标 DLL，找出最早失败的那一级。
4. 对比 `bin/` 中 `Qt5Core/Gui/Widgets/...` 的版本，确认是否来自同一套 Qt。
5. 检查 `bin/plugins/` 中对应插件版本是否与核心 Qt 一致。
6. 检查 `plugin/`、`bin/`、系统 PATH 中是否存在同名 DLL 阴影。
7. 若工程接入 vcpkg / applocal，优先怀疑运行目录被自动部署逻辑污染。

## 本仓库的经验结论

- Windows 下，Qt 运行时不能“半手工补丁式”部署。
- 只补单个 Qt 模块或单个插件非常危险，容易造成跨版本混装。
- 一旦项目同时使用 vcpkg 和外部 Qt 安装目录，必须明确谁是唯一真源。
- 真正可靠的做法是：从 CMake 当前命中的 Qt 安装目录统一复制整套关键运行时与插件，覆盖运行目录中的历史残留或 applocal 产物。

## 相关文件

- `src/CMakeLists.txt`
- `src/app/CMakeLists.txt`
- `src/libs/controls/CMakeLists.txt`
- `src/libs/controls/controls.qrc`
- `src/app/main.cpp`
