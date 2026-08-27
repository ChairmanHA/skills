# Win32 Qt 平台插件启动回归修复

## Scope

- 核对 2026-08-20 为 Linux aarch64 打包所做的 `src/app/main.cpp`、`src/app/etc/qt.conf` 与脚本修改。
- 修复 Qt Creator 的 Windows `build-tree` Debug 产物启动时无法定位 `qwindows` 平台插件的问题。
- 保持 Linux aarch64 包内 `bin/platforms` 的相对运行时布局，不改动业务插件加载和双运行布局约定。

## Observation

- 提交 `800fa5a8b2de1f2eb3d5501de7fa843dc6f7be32` 给 `src/app/etc/qt.conf` 新增了 `[Paths]` 段，将 Qt 插件根目录固定到当前运行布局的 `bin`。
- `src/app/etc.qrc` 会把该文件嵌入为 `:/qt/etc/qt.conf`；因此 Qt 在构造 `QApplication` 之前就使用它决定插件根路径。
- 同一提交对 `main.cpp` 的修改只设置 Wayland 重启子进程的环境变量，不参与 Windows 首次启动。
- Windows 运行时的平台插件按现有部署约定位于当前可执行文件目录的 `platforms/qwindows[d].dll`。

## Inference

- 新增的 `[Paths]` 映射会让 `build-tree/bin` 与 `repo-root/bin` 都只从各自运行布局加载 Qt 插件；Linux 包具备完整的 `bin/platforms`，而 Qt Creator build-tree 原先没有部署 `qwindowsd.dll`，因此 Windows 启动回归。
- 保留 `Prefix = ..`、`Plugins = bin` 等相对路径并在 Windows 构建期同步当前 Qt/当前配置的平台插件，可以同时满足 Qt Creator 与 Linux 包隔离要求。

## Success Criteria

1. `qt.conf` 同时保留 `WindowsArguments = dpiawareness=2` 和现有相对 Qt 运行时路径映射。
2. Windows Debug 完整构建成功，运行时能够加载当前 Debug 布局中的 `platforms/qwindowsd.dll`，不再出现 `Could not find the Qt platform plugin "windows" in ""`。
3. Linux aarch64 打包审计所要求的 `Prefix`、`Plugins`、`Libraries`、`LibraryExecutables` 配置继续成立。

## Verification Level

- `debug-run`
- 静态检查 `qt.conf`、资源嵌入和平台插件部署目录。
- 复用现有 Windows Debug build tree 做完整构建，并启动产物检查控制台/日志。

## Implementation

- 保留 2026-08-20 新增的包内相对 `qt.conf`，没有回退 aarch64 所需的 Qt 隔离。
- 在 `src/CMakeLists.txt` 的 `WIN32` 分支新增 `sgstudio_windows_qt_runtime`：
  - 从 CMake 当前 Qt 的 Windows QPA、SVG image format 与 SVG icon engine 导入目标取源文件；
  - 通过 generator expression 自动选择 Debug 或 Release 插件文件；
  - 分别复制到当前运行布局的 `${SGS_BIN_DIR}/platforms`、`imageformats` 与 `iconengines`；
  - `maintenance`、`SGStudio`、`SGStudioMiniBar` 均依赖该部署目标，Qt Creator 只构建/运行任一 GUI 目标时也会刷新平台插件。
- Linux/aarch64 不会创建该目标，现有库、插件、launcher 与 package audit 路径未改变。

## Verification Evidence

- Qt Creator build tree：`build/Qt_5_15_9_msvc2022_64-Debug`，缓存为 `Debug`、`Ninja`、`SGS_RUNTIME_LAYOUT=build-tree`、Qt `C:/Qt/5.15.9/msvc2022_64`。
- CMake 重新配置成功；加载 VS 2022 开发环境后完整 Debug 构建 `184/184` 成功。
- 生成文件：`build/Qt_5_15_9_msvc2022_64-Debug/bin/platforms/qwindowsd.dll`。
- 运行目录与 Qt 安装源的 `qwindowsd.dll` SHA-256 均为 `E8D628B1A0D79C9188D43AEF69894F0D46666DC1CBD32DB3ECE1441F9815CBB5`。
- 受控启动 `SGStudio.exe` 后进程持续运行并进入事件循环；随后只终止了该测试进程。
- 当前 Qt Creator Debug 实例来自同一 build tree，`tasklist /m` 确认加载的是 `qwindowsd.dll`，没有混入 Release `qwindows.dll`。
- Qt Creator Release build tree 也成功执行同一部署目标，自动选择 `qwindows.dll`；运行目录与 Qt 安装源 SHA-256 均为 `D80D9AA30DEAC8299ABF4278BE02A2618ABD39410D7679446E2F4251FCADAB6B`。
- `scripts/build.sh`、`scripts/build_pi.sh`、`scripts/launch_pi.sh`、`scripts/audit_linux_package_ui_runtime.sh` 通过 Git Bash `bash -n`。
- 静态核对确认：
  - `qt.conf` 保留 `Prefix=..`、`Plugins=bin`、`Libraries=lib`、`LibraryExecutables=bin`；
  - aarch64 构建脚本继续复制包内 `qt.conf`、Qt libraries 与 Qt plugin groups；
  - launcher 继续设置包内 `QT_PLUGIN_PATH`、`QT_QPA_PLATFORM_PLUGIN_PATH`、`LD_LIBRARY_PATH`；
  - package audit 继续强制检查 Qt Core/Gui/Widgets、Wayland/XCB 平台插件等关键运行文件。
- 本机是 Windows，无法替代真实 aarch64 无 Qt 环境的最终设备启动；现有打包闭包和审计门槛未被本修复改变。

## Follow-up: Win32 SVG Resources

### Observation

- 平台插件修复后，TitleBar 的关闭/最大化/最小化、最左侧 Logo 与 `DeviceInfoWidget` 的 GNSS 锁定图标仍为空。
- 对应资源分别来自 `src/libs/controls/controls.qrc`、`src/app/res_standard/app.qrc` 与 `src/plugins/core/core.qrc`，文件格式均为 SVG；资源本身仍被各自 target 编译进入 qrc。
- `qt.conf` 现在把 Qt 插件根目录固定到当前 `bin`，但 Qt Creator Debug build-tree 只有 `platforms/qwindowsd.dll`，没有 `imageformats/qsvgd.dll` 和 `iconengines/qsvgicond.dll`。
- 配置的 Qt 5.15.9 提供 `Qt5::QSvgPlugin` 与 `Qt5::QSvgIconPlugin` 导入目标，可自动区分 Debug/Release 文件名。

### Plan And Success Criteria

- 把 Windows 构建期部署目标从“仅 QPA”扩展为当前应用实际资源所需的 Qt 插件集合：Windows QPA、SVG image format、SVG icon engine、ICO image format。
- 所有插件必须来自 CMake 当前命中的同一 Qt 安装，并通过 generator expression 选择当前 Debug/Release 文件，禁止硬编码或跨版本复制。
- Debug build-tree 应生成 `platforms/qwindowsd.dll`、`imageformats/qsvgd.dll`、`imageformats/qicod.dll`、`iconengines/qsvgicond.dll`；Release 对应无 `d` 后缀。
- 完整 Debug 构建与启动通过；运行进程实际加载 Debug SVG 插件与 `Qt5Svgd.dll`。
- Linux/aarch64 分支、包内 Qt 路径、库/插件收集及 package audit 保持不变。

### Implementation And Static Verification

- `sgstudio_windows_qpa_runtime` 已扩展并更名为 `sgstudio_windows_qt_runtime`。
- 部署集合为：
  - `Qt${QT_VERSION_MAJOR}::QWindowsIntegrationPlugin` -> `bin/platforms`
  - `Qt${QT_VERSION_MAJOR}::QSvgPlugin` -> `bin/imageformats`
  - `Qt${QT_VERSION_MAJOR}::QSvgIconPlugin` -> `bin/iconengines`
  - `Qt${QT_VERSION_MAJOR}::QICOPlugin` -> `bin/imageformats`
- qrc 静态清单包含 78 个 SVG、23 个 PNG、4 个 ICO、3 个 TTF；其中需要动态 Qt image/icon plugin 的 SVG 与 ICO 均已覆盖，没有部署仓库未使用的 GIF/JPEG/TIFF/WebP 插件。
- CMake 对四个导入目标逐一做存在性校验，避免配置成功后静默漏插件。
- 源文件与目标文件名均由 Qt imported target/generator expression 提供，因此 Debug/Release 和 Qt 版本保持一致。
- 按用户要求，本 follow-up 不执行构建或运行；最终 UI 验证由用户完成。
