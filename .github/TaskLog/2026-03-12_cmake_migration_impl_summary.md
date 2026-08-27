# SGStudio CMake 改造实施总结

## 本次目标
- 将当前 active qmake 工程迁移为可用的 CMake 构建。
- 在 Windows 下完成实际 configure、build、run 验证。
- 修复 Analog 插件在不同启动方式下的加载不稳定问题。

## 已完成范围

### 顶层构建迁移
- 新增顶层 [src/CMakeLists.txt](src/CMakeLists.txt)，覆盖当前 active 子工程：
  - libs
  - plugins
  - app
- 未纳入 tools，保持与 [src/sgstudio.pro](src/sgstudio.pro) 当前 active 范围一致。
- CMake 固定使用 C++17。
- 启用了 AUTOMOC、AUTOUIC、AUTORCC。

### 内部库迁移
- 已为以下内部库添加独立 CMakeLists：
  - [src/libs/utils/CMakeLists.txt](src/libs/utils/CMakeLists.txt)
  - [src/libs/controls/CMakeLists.txt](src/libs/controls/CMakeLists.txt)
  - [src/libs/business/CMakeLists.txt](src/libs/business/CMakeLists.txt)
  - [src/libs/extensionsystem/CMakeLists.txt](src/libs/extensionsystem/CMakeLists.txt)
- 输出布局与原工程保持一致：
  - 可执行文件和运行时 DLL 输出到仓库根 bin
  - import lib 输出到仓库根 lib

### 插件迁移
- 已为以下 active 插件添加独立 CMakeLists：
  - [src/plugins/core/CMakeLists.txt](src/plugins/core/CMakeLists.txt)
  - [src/plugins/analog/CMakeLists.txt](src/plugins/analog/CMakeLists.txt)
  - [src/plugins/htra/CMakeLists.txt](src/plugins/htra/CMakeLists.txt)
  - [src/plugins/sweep/CMakeLists.txt](src/plugins/sweep/CMakeLists.txt)
  - [src/plugins/updater/CMakeLists.txt](src/plugins/updater/CMakeLists.txt)
- 插件运行时输出目录固定为仓库根 plugin。

### 主程序迁移
- 已为 [src/app/CMakeLists.txt](src/app/CMakeLists.txt) 建立宿主程序构建。
- [src/app/main.cpp](src/app/main.cpp) 对应的运行期布局仍保持：
  - 主程序位于 bin
  - 插件位于 plugin
  - configuration 位于仓库根 configuration

### Windows 资源与界面图标修复
- 已修正主程序图标嵌入与运行时窗口图标的职责分离：
  - [src/app/app.rc](src/app/app.rc)
  - [src/app/app.qrc](src/app/app.qrc)
  - [src/app/CMakeLists.txt](src/app/CMakeLists.txt)
  - [src/app/main.cpp](src/app/main.cpp)
- 已修正 TitleBar 窗口按钮图标的资源归属与运行时刷新逻辑：
  - [src/plugins/core/titlebar.cpp](src/plugins/core/titlebar.cpp)
  - [src/plugins/core/titlebar.h](src/plugins/core/titlebar.h)
  - [src/libs/controls/thememanager.cpp](src/libs/controls/thememanager.cpp)
  - [src/libs/controls/thememanager.h](src/libs/controls/thememanager.h)

核心变化：
- Windows Explorer 看到的 exe 图标，改由 rc 资源嵌入，不再误以为 `app.setWindowIcon()` 可以替代 PE 资源图标。
- 运行时窗口图标改为从 qrc 资源读取，避免依赖磁盘相对路径。
- TitleBar 的最小化、最大化、还原、关闭按钮图标不再静态写死在 ui 文件里，而是统一由 Controls 层的 `ThemeManager` 提供。
- `btnMax` 图标改为根据窗口状态在最大化图标与还原图标之间动态切换，并在主题切换时同步刷新。

## 第三方依赖处理

### 已接入
- xlsx
- htra
- modulation
- updater 所需的 curl/libzip/vcpkg

### 当前策略
- xlsx 通过预编译 import lib + DLL 接入。
- htra 通过 h2_api.lib + 相关运行库接入。
- modulation 通过 genSignalWave.lib + genSignalWave.dll 接入。
- updater 的 curl/libzip 继续沿用本机 vcpkg 约定。

## 路径与启动方式修复

### 已修复问题
此前程序对当前工作目录有隐式依赖，导致不同启动方式下行为不一致：
- 从 bin 目录启动时插件可加载。
- 从仓库根目录或双击类场景启动时，Analog 插件可能丢失。

### 本次修复点

#### 1. 统一路径基准为 applicationDirPath
- 修改 [src/libs/utils/fileutils.cpp](src/libs/utils/fileutils.cpp)
- 修改 [src/libs/utils/settings.cpp](src/libs/utils/settings.cpp)
- 修改 [src/app/main.cpp](src/app/main.cpp)

核心变化：
- 默认 absolutePath 不再优先依赖 currentPath。
- Settings.ini、Language.xlsx、translations、plugin 路径改为基于 exe 所在目录推导。

#### 2. 启动时显式收敛 DLL 搜索路径
- 修改 [src/app/main.cpp](src/app/main.cpp)

核心变化：
- Windows 启动时，把以下目录显式注入当前进程 PATH：
  - bin
  - plugin

目的：
- 降低插件及其依赖 DLL 对启动 cwd 的敏感性。
- 将“插件目录”和“普通运行库目录”的职责重新拉开：
  - plugin 仅承载插件 DLL
  - bin 承载宿主程序与插件依赖运行库

#### 3. 固定日志输出目录
- 修改 [src/app/filelogger.cpp](src/app/filelogger.cpp)

核心变化：
- debug.log 不再落在当前工作目录。
- 统一写到 applicationDirPath 对应目录。

#### 4. 统一资源路径策略，尽量避免磁盘相对路径参与 UI 资源加载
- 修改 [src/app/main.cpp](src/app/main.cpp)
- 修改 [src/libs/controls/thememanager.cpp](src/libs/controls/thememanager.cpp)
- 修改 [src/plugins/core/titlebar.cpp](src/plugins/core/titlebar.cpp)

核心变化：
- 应用图标、TitleBar 图标、窗口控制按钮等稳定资源优先走 qrc。
- 只有明确允许用户覆盖的资源才继续走磁盘路径，例如自定义 Logo。
- UI 布局仍可保留 `.ui`，但涉及主题、窗口状态、运行时切换的图标选择应放回代码侧处理。

## Analog 插件加载问题分析结论

### 现象
- [plugin/AnalogModulation.dll](plugin/AnalogModulation.dll) 能成功编译生成。
- 但某些启动方式下，程序运行时不显示 Analog 业务入口。

### 排查结论
- 真正根因不是“modulation DLL 必须和 Analog 插件放在同目录”。
- 真正根因是启动阶段存在基于当前工作目录的路径解析，导致不同启动方式下运行环境不一致。
- 在排障过程中还叠加过一次临时状态：bin 下的 genSignalWave.dll 曾缺失，这会放大 Analog 插件的失败现象，但这不是最终部署模型本身的要求。

## 对 Analog 的部署修复

### 真正的修复点
- 修改 [src/app/main.cpp](src/app/main.cpp)
- 修改 [src/libs/utils/fileutils.cpp](src/libs/utils/fileutils.cpp)
- 修改 [src/libs/utils/settings.cpp](src/libs/utils/settings.cpp)
- 修改 [src/app/filelogger.cpp](src/app/filelogger.cpp)

核心变化：
- 所有关键运行时路径统一改为基于 exe 目录推导，而不是基于当前工作目录。
- Windows 启动时显式收敛进程 PATH，使 bin 中的运行库在不同启动方式下都能稳定被找到。

结论：
- 当前已确认 plugin 目录只保留插件 DLL 也可以正常运行。
- genSignalWave.dll 保持放在 bin 即可，不需要作为最终规则复制到 plugin。

## CMake 下路径问题的归纳结论

### 运行布局必须先固定，再写路径代码
这次迁移里，真正稳定下来的运行布局是：
- `bin/` 放主程序与普通运行库
- `plugin/` 放 Qt 插件 DLL
- `configuration/` 放外部配置

只要这个布局不先固定，代码里所有相对路径推导都会反复漂移。

### `currentPath()` 不能作为桌面程序运行期的主路径基准
这次 Analog 插件问题已经证明：
- 从 IDE 启动
- 从资源管理器双击启动
- 从任意工作目录启动

这三种方式的 cwd 可能完全不同。

经验结论：
- 宿主程序、插件、翻译、日志、配置、运行库搜索路径，只要属于“安装布局的一部分”，都应基于 `applicationDirPath()` 或其相邻目录推导。
- 只有用户主动选择的外部文件，才应该允许脱离安装布局。

### qrc 与磁盘路径要严格区分职责
这次迁移里有两个很典型的误区：
- `app.setWindowIcon()` 影响的是运行时窗口图标，不会给 exe 嵌入 Explorer 图标。
- `.ui` 中的静态 icon 属性适合固定资源，不适合依赖主题或窗口状态切换的图标。

经验结论：
- 稳定内置资源优先走 qrc。
- 需要跟随主题切换、状态切换的图标，由代码动态设置。
- 需要被 Windows 识别为文件图标的资源，必须走 rc 资源编译。

### CMake 构建通过，不等于终端环境正确
本轮实际验证里，同一个 build 目录在两种环境下表现不同：
- 普通 PowerShell 里，MSVC 标准库和 Windows SDK 未就绪时，会出现 `kernel32.lib`、`type_traits` 缺失。
- 进入 Visual Studio Dev Shell 后，同一套 Ninja 工程即可正常增量构建。

经验结论：
- Windows 下命令行构建必须保证 MSVC 编译器、标准库、Windows SDK 在环境中已完整注入。
- Qt Creator 或 VS Code 的 CMake 上下文如果没有正确恢复，不能直接把“工具配置失败”误判为“工程配置失败”。
- 当现有 build 目录已经完整存在时，可以优先复用该目录做增量验证，先把工程问题和环境问题拆开。

### 插件目录不要混入普通依赖 DLL
经验结论：
- 插件目录只放插件本体，结构更清晰，也更接近 Qt 插件系统的职责边界。
- 插件依赖的普通运行库应通过 `bin/` 和进程搜索路径解决，而不是把所有依赖都混入 `plugin/`。

## 实际验证结果

### CMake configure
已验证成功：

```powershell
cmake -S src -B build/cmake-win-release -G "Visual Studio 17 2022" -A x64 -DCMAKE_PREFIX_PATH="C:/Qt/5.15.9/msvc2022_64" -DCMAKE_BUILD_TYPE=Release
```

### CMake build
已验证 active 目标可构建并生成：
- SGStudio.exe
- Core.dll
- AnalogModulation.dll
- HTRA.dll
- StepSweep.dll
- Updater.dll

另外，已验证基于既有 Debug build 目录的增量构建可以完成：
- 在普通 PowerShell 中会因缺少 MSVC/Windows SDK 环境失败。
- 切换到 Visual Studio Dev Shell 后，Debug 增量构建成功。

### 运行验证
已验证以下启动方式下，Analog 插件能够进入进程模块：
- 从非 bin 工作目录启动 exe
- plugin 目录只保留插件 DLL 时仍可正常加载
- 当前运行期模块检查可见：
  - AnalogModulation.dll
  - genSignalWave.dll
  - Core.dll
  - HTRA.dll
  - StepSweep.dll
  - Updater.dll

另外，已验证 Debug 版宿主程序可正常启动，主窗口进程状态正常响应：
- 进程：`SGStudiod.exe`
- 主窗口标题：`Mute - SG Studio`

说明当前运行态修复有效。

## 当前遗留问题

### 1. ExtensionSystem.dll 偶发被占用
- 当前环境中，重建 [bin/ExtensionSystem.dll](bin/ExtensionSystem.dll) 时仍偶发出现 LNK1104。
- 这不是源码逻辑问题，而是本机运行/IDE/构建进程对 DLL 的占用问题。

### 2. 仍存在 C4819 编码告警
- 若干老文件包含非当前代码页可表示字符。
- 当前不影响功能，但会污染 MSVC 输出。

### 3. Release 下 SVG/图标链路仍需继续收口
- 当前窗口按钮资源归属已经理顺到 Controls + ThemeManager。
- 但若后续要彻底收口 Release 下 SVG 资源显示一致性，仍需继续确认 Qt5Svg 的渲染与部署链路。
- 这个问题属于“资源渲染链路完整性”，不再是路径归属问题。

## 值得记录的坑和心得

### 坑 1：把“路径问题”和“部署问题”混在一起，会误导排障方向
这次 Analog 插件问题最初看起来像“DLL 没放对目录”，但最终根因是运行期路径基准不稳定。

心得：
- 先判断失败是“找不到文件”，还是“找错了基准目录”，再决定是改部署还是改代码。

### 坑 2：UI 文件很容易掩盖资源边界错误
`titlebar.ui` 里静态写了 Core 前缀的按钮图标，看上去只是 Designer 配置，但本质上是模块边界写错了。

心得：
- `.ui` 适合描述结构，不适合承载跨模块资源策略。
- 涉及主题、状态、资源归属的内容，放回代码里更可控。

### 坑 3：Windows 上“能链接”和“能双击运行”是两个问题
构建成功只说明编译器和链接器工作了；双击运行是否稳定，取决于：
- exe 附近布局是否稳定
- PATH 是否补齐
- 插件目录是否纯净
- 配置和资源是否基于 exe 目录推导

心得：
- Windows 桌面程序迁移到 CMake 时，必须把 configure、build、run、double-click run 当成四个独立验证面。

### 坑 4：不要把 IDE 工具状态误当成源码问题
`ExtensionSystem.dll` 被占用、CMake Tools 无法恢复当前配置、普通终端缺少 VS 环境，这些都可能表现成“构建失败”。

心得：
- 先区分是源码层失败、构建脚本层失败，还是 IDE / shell 环境失败。
- 这一步不先做，后面的修复动作很容易越改越偏。

## 建议的后续实施顺序

### 第一阶段：固化构建稳定性
- 彻底排除 ExtensionSystem.dll 占用源。
- 完成一次无手工干预的全量 Release 重建。
- 验证 bin/plugin 下所有依赖均由构建后处理自动补齐。

### 第二阶段：补齐工程化能力
- 增加 Windows staging/install 逻辑到 CMake。
- 使 CMake 直接生成可分发目录，而不是依赖额外手工拷贝。
- 逐步替代现有 qmake Windows 打包路径。

### 第三阶段：清理技术债
- 统一路径访问策略，避免继续直接写相对路径字符串。
- 统一日志输出和插件错误报告。
- 视需要清理编码告警与旧 qmake 辅助复制逻辑。

## 当前可用结论
- 当前 CMake 迁移已不是骨架，而是已能真实 configure、build、run。
- 当前 Analog 插件加载问题已定位并完成运行态修复。
- 后续重点不再是“能否迁移”，而是“如何把构建和部署彻底固化”。