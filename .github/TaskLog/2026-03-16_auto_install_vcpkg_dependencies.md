# CMake 自动安装 vcpkg 依赖

## 目标
- 让新机器在已安装 vcpkg 且设置好环境变量后，无需手动执行 `vcpkg install curl libzip`。
- 保持当前工程非 manifest 模式不变，只在缺少 Updater 所需依赖时自动补齐。

## 现状
- 根 `CMakeLists.txt` 已能推断 `SGS_VCPKG_ROOT` 与 `SGS_VCPKG_TRIPLET`。
- `plugins/updater/CMakeLists.txt` 仅检查 `curl` / `libzip` 头文件是否存在；缺失时直接 `FATAL_ERROR`。
- 这要求开发机在 clone 后还要手动执行一次 `vcpkg install`，体验不完整。

## 方案
- 根 `CMakeLists.txt` 只保留通用的 `SGS_VCPKG_ROOT` / `SGS_VCPKG_TRIPLET` / `SGS_VCPKG_INSTALLED` 推断。
- `plugins/CMakeLists.txt` 用 `WIN32` 显式控制是否纳入 `updater` 子目录，避免非 Windows 配置误进入该插件。
- 将自动安装逻辑下沉到 `plugins/updater/CMakeLists.txt`：
  - 在插件内解析 `vcpkg` 可执行文件位置。
  - 在插件内检查指定 probe 文件是否已出现在 `installed/<triplet>` 下。
  - 若缺失且开启插件私有开关 `SGS_UPDATER_AUTO_INSTALL_VCPKG_DEPS`，则执行 `vcpkg install --disable-metrics <pkg>:<triplet>`。
- 保留开关，允许通过 CMake 选项关闭自动安装并回退到显式报错。

## 风险与约束
- 首次 configure 可能联网并花费更长时间。
- 依赖安装失败时仍应清晰报错，方便用户区分“未设置 vcpkg”与“网络/权限失败”。
- 这里通过直接调用 `vcpkg.exe` / `vcpkg.bat` 的 `install` 子命令，而不是走 PowerShell 脚本执行链路；正常情况下不会出现 PowerShell 的 `Y/N` 脚本确认提示。
- 目前只覆盖 Updater 需要的 `curl` 与 `libzip`；若后续其他模块也要自动安装，再抽回公共 cmake 模块文件比继续堆在根 `CMakeLists.txt` 更合适。

## 验证
- 重新执行 Release configure，确认 CMake 语法通过。
- 若目标机器缺少对应 ports，configure 阶段会自动触发安装。