# 2026-03-16 去除 vcpkg 并统一 3rdParty CMake 接口

## 背景

- 当前除 Qt 外的第三方依赖应统一由仓库根目录 `3rdParty/` 托管。
- 现状中 `xlsx`、`htra`、`modulation` 已有本地二进制，但接口仍散落在根 `CMakeLists.txt`。
- `Updater` 仍直接依赖 `vcpkg` 的 `curl/libzip`，且 configure 阶段可能在线安装，受网络质量影响严重。

## 目标

- 在 `3rdParty/` 下为 `libcurl`、`libzip`、`htra`、`modulation`、`xlsx` 提供统一的标准 CMake package config 接口。
- `libcurl` 与 `libzip` 目录内直接放置头文件、导入库、运行库及 `cmake` 接口文件，不再依赖 vcpkg 在线安装。
- 项目各子模块统一通过 `find_package(... CONFIG ...)` + imported targets 引入依赖。
- 完成构建、stage 打包与程序启动验证，确认根运行目录可正常启动。
- 删除工程内 vcpkg 相关依赖与搜索路径。

## 设计

- 在 `3rdParty/<pkg>/cmake/` 下新增 `<Pkg>Config.cmake` 与 `<Pkg>Targets.cmake`。
- 统一暴露既有 target 风格：`SGS::xlsx`、`SGS::h2_api`、`SGS::genSignalWave`，并新增 `SGS::libcurl`、`SGS::zip`、`SGS::zlib`。
- package config 内统一声明：
	- include 目录
	- Debug/Release 导入库与运行库
	- 需要透传的附加运行库
- 根 `CMakeLists.txt` 提供第三方 package 查找辅助，并移除 vcpkg root/triplet 推断逻辑。
- `Updater` 改为链接本地 package targets，并从 package 暴露的运行库目录复制 DLL 到运行目录。
- stage 运行时搜索目录改为仅包含 `3rdParty` 下对应依赖目录，移除 vcpkg 搜索目录。

## 验证计划

- 构建 Release。
- 执行 stage 打包。
- 直接启动根目录 `bin/SGStudio.exe` 验证程序启动。
- 如启动失败，继续通过日志定位缺失 DLL 或插件装载问题并修正。

## 实施结果

- 已新增 `3rdParty/libcurl` 与 `3rdParty/libzip`，落入头文件、导入库、Debug/Release 运行库。
- 第三方接入方案已进一步收敛为：每个 `3rdParty/<pkg>/` 目录直接提供一个 `CMakeLists.txt`，由主工程通过 `add_subdirectory(...)` 纳入；不再依赖 `cmake/*Config.cmake` 与 `*Targets.cmake`。
- 根 `CMakeLists.txt` 已移除 vcpkg root/triplet 推断与 stage 中的 vcpkg 搜索目录。
- `Updater` 已改为链接 `SGS::libcurl`、`SGS::zip`、`SGS::zlib`，运行库列表由各第三方子目录的 `CMakeLists.txt` 直接回传给主工程。
- 期间发现一个 CMake 坑：`find_package` 包在 `function()` 内时，package config 设置的变量不会外溢到调用者；最终改为 `macro()` 解决。

## 验证结果

- `cmake -S . -B ../build/cmake-win-release ...` 重新 configure 成功。
- `cmake --build ../build/cmake-win-release --config Release --target ALL_BUILD --parallel` 构建成功。
- `cmake --build ../build/cmake-win-release --config Release --target sgstudio_stage --parallel` stage 成功，`stage/.../bin` 已包含 `libcurl.dll`、`zip.dll`、`bz2.dll`、`zlib1.dll`。
- 根目录 `bin/SGStudio.exe` 启动后保持运行 12 秒，`bin/debug.log` 持续输出业务日志，无缺失 DLL 崩溃。
- `stage/SGStudio_v1.1.1_Win64/bin/SGStudio.exe` 启动后保持运行 12 秒，`stage/.../bin/debug.log` 同样持续输出业务日志。

## 后续模板化方向

- 由于工程未来会扩展到跨平台，第三方模块的 `CMakeLists.txt` 将逐步对齐到 `ant_api` 风格：
	- `IMPORTED` 二进制目标 + `INTERFACE` 对外目标 + alias；
	- 在 Win32/MSVC 下走最短路径，不执行额外 deploy 脚本逻辑；
	- 仅在 Linux 分支中启用架构识别、`.so` 复制与软链接整理等附加逻辑；
	- 若当前仓库未提供对应平台二进制，则在该平台 configure 阶段明确报错，而不是让 Win32 分支承担额外维护复杂度。
