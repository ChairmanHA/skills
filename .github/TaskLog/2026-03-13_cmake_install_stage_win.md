# Windows CMake Install 打包落地

## 目标
- 不再参考旧的 PowerShell staging 脚本行为，改为由 CMake install() 直接产出上一级 stage 目录下的可运行包。
- Windows staging 输出目录命名遵循 `SGStudio_v<version>_Win64`；若显式提供时间戳则为 `SGStudio_v<version>_Win64_<timestamp>`。
- 安装后的目录结构保持为 `bin/`、`plugin/`、`configuration/`、`fonts/`，并保留当前运行所需的 `bin/plugins/`、`bin/CalFile/` 等子目录；`data/` 不再复制测试文件内容。
- 打包完成后，从 stage 目录直接启动程序，验证可正常启动并稳定保持 7 秒；同时在设备连接信号处补一条日志，通过 `debug.log` 确认运行路径闭环。

## 方案
- 在顶层 CMake 增加 `SGS_PROJECT_VERSION` cache 入口：
  - 未传入时，回退到 `project(... VERSION ...)` 中的默认版本号。
  - 传入 `-DSGS_PROJECT_VERSION=...` 时，把它视为外部版本字符串，而不是直接传给 `project()`，从而支持 `1.1.0-beta1` 这类预发布标签。
  - `project(... VERSION ...)` 继续保留纯数字版本，满足 CMake 对 VERSION 字段的格式要求。
- 在顶层 `CMakeLists.txt` 中新增 staging 命名规则：
  - `SGS_PACKAGE_VERSION_LABEL`：默认动态跟随“外部版本字符串或 project(VERSION)”，仅在显式传参时覆盖。
  - `SGS_PACKAGE_TIMESTAMP`：默认空，显式传入时追加到目录名。
  - `SGS_STAGE_ROOT_DIR`：固定为仓库根目录下的 `stage/`。
- 把现有“构建后复制到根运行目录”的辅助函数扩展为“同时声明 install 规则”：
  - 共享库安装到 `bin/`。
  - 插件安装到 `plugin/`。
  - 第三方 DLL、`CalFile`、配置与资源目录通过 install(FILES/DIRECTORY) 安装到对应位置。
  - `CalFile` 的 Windows 来源以当前稳定运行目录 `../bin/CalFile/` 为准，不再假设来自 `3rdParty/htra/CalFile/`。
  - Qt 运行时与 Qt plugins 复用当前 CMake 已知路径，改为在 install 阶段按配置安装，而不是依赖外部脚本扫描。
- 为 `SGStudio` 可执行文件补 install(TARGETS)。
- 增加一个便捷的 staging 目标，内部调用 `cmake --install ... --prefix <stage package dir>`，用于产出标准包目录。
- 在 VS Code tasks 中保留一个版本化打包入口：
  - `CMake Stage Release`：直接提示输入版本参数，然后在同一个任务里串行执行 configure 与 `sgstudio_stage`。
  - 删除冗余的 `CMake Configure Release With Version`，避免和 `CMake Stage Release` 的职责重叠。

## 2026-03-13 增补：runtime 依赖收敛策略
- `stage` 目录不再依赖手工枚举的第三方 DLL install 规则；install 阶段只先落主程序、自研共享库、插件与静态资源。
- Windows runtime 补齐拆成两段：
  - Qt 与 MSVC 运行时由 `windeployqt` 针对 stage 中的 `bin/SGStudio.exe` 直接补齐。
  - 非 Qt 运行库通过 CMake `file(GET_RUNTIME_DEPENDENCIES)` 做递归依赖解析；在 MSVC/Windows 下该路径等价于基于 `dumpbin` 后端解析 PE 依赖。
- 递归依赖分析的 root 输入至少包含：
  - stage 中的 `bin/SGStudio.exe`；
  - stage 中 `plugin/` 下全部插件 DLL。
- 递归依赖的搜索目录至少覆盖：
  - 仓库根 `bin/`、`lib/`、`plugin/`；
  - `3rdParty/htra/`、`3rdParty/modulation/`；
  - vcpkg `installed/<triplet>/bin` 与 `debug/bin`（若存在）。
- 过滤策略：
  - Qt DLL、Qt plugin、MSVC/UCRT/system DLL 不由递归复制阶段处理，避免与 `windeployqt` 职责重叠；
  - 插件 DLL 自身保持在 `plugin/`，即使被解析为依赖也不复制到 `bin/`；
  - 插件依赖到的自研/第三方非插件 DLL 统一收敛到 `bin/`。
- 为兼顾现有开发运行目录，根运行目录 `bin/` 的 POST_BUILD 拷贝逻辑保留；仅 stage install 规则切换为“编译产物 + 递归依赖闭包”。

## 2026-03-13 收敛结果
- `CMake Stage Release` 的配置脚本改为始终显式传入 `SGS_PROJECT_VERSION` 与 `SGS_PACKAGE_TIMESTAMP`，即使用户直接回车也会把 cache 置空，不再沿用上一次构建残留值。
- stage 目录名增加版本标签归一化：若输入 `v2.0.1`，目录名会生成为 `SGStudio_v2.0.1_Win64`，不会出现 `vv` 前缀；若留空，则回退到 `project(... VERSION ...)` 的默认版本。
- Windows runtime 闭包的搜索目录最终收敛为：
  - stage 内的 `bin/`、`plugin/`；
  - `3rdParty/htra/`、`3rdParty/modulation/`、`3rdParty/xlsx/bin/`；
  - vcpkg `installed/<triplet>/bin` 与 `debug/bin`（仅用于非 Qt 第三方库，例如 `libcurl.dll`、`zip.dll`、`zlib1.dll`）。
- Qt 冲突规避已实测确认：
  - stage 中 Qt 运行时由 `C:/Qt/5.15.9/msvc2022_64/bin/windeployqt.exe` 部署；
  - 递归依赖复制阶段按名称过滤全部 `Qt5*.dll`，不会从 vcpkg bin 再复制一套 Qt；
  - 实测 `stage/bin/Qt5Core.dll` 的 SHA256 与 `C:/Qt/5.15.9/msvc2022_64/bin/Qt5Core.dll` 一致，且与 `D:/vcpkg/installed/x64-windows/bin/Qt5Core.dll` 不一致，确认不存在 vcpkg Qt 混装。
- 当前仍会提示少量未解析的系统可选组件 DLL（如 `azureattestmanager.dll`、`wpaxholder.dll`），但不影响 stage 包启动；这些项属于非阻塞 warning，而非 Qt/runtime 缺失。

## 验证
- 重新执行 Release 构建，确保新增日志与 install 规则进入产物。
- 执行 `cmake --install` 安装到 `../stage/SGStudio_v..._Win64`。
- 直接从 stage 的 `bin/SGStudio.exe` 启动，等待至少 7 秒后关闭。
- 检查 stage 目录下的 `bin/debug.log`，确认：
  - 程序已正常启动；
  - 设备连接日志已输出；
  - 未出现缺失 DLL / 插件加载失败导致的启动中断。
- 额外确认：
  - 对比 `stage/bin/Qt5Core.dll`、`C:/Qt/5.15.9/msvc2022_64/bin/Qt5Core.dll`、`D:/vcpkg/installed/x64-windows/bin/Qt5Core.dll` 的 SHA256；stage 与 Qt 5.15.9 一致，与 vcpkg 不一致。
  - 空输入版本号/时间戳后重新执行 `CMake Stage Release`，目录名应回退为 `SGStudio_v1.1.0_Win64`，且不再保留旧时间戳。