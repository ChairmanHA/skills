# 修复 Qt Creator build-tree 的 xcb 插件路径

## Scope

- 修复虚拟机 `/home/harogic/Desktop/sgstudio` 当前分支中的 Linux 启动环境准备逻辑。
- 保留打包产物对相邻 `bin/platforms` 的运行时隔离。
- 当 Qt Creator build-tree 未部署相邻平台插件时，不覆盖 Kit/Qt 自身的插件搜索路径。
- 不改变 `SGS_RUNTIME_LAYOUT`、构建输出目录、插件部署结构或 Wayland/LayerShellQt 编译边界。

## Observation

- Qt Creator Debug build-tree 的 `bin/` 中没有 `platforms/libqxcb.so`。
- x86_64 Kit 的 xcb 插件存在于 `/opt/Qt/5.15.18/gcc_x86_64/plugins/platforms/libqxcb.so`，依赖链完整。
- `prepareLinuxDirectLaunchEnvironment()` 在 `QApplication` 前无条件把 `QT_QPA_PLATFORM_PLUGIN_PATH` 改为 `<executable-dir>/platforms`。
- `QT_DEBUG_PLUGINS=1` 复现了 `Could not find the Qt platform plugin "xcb" in ".../bin/platforms"`，随后 Qt 主动 abort。

## Inference

- 直接原因不是工作目录、Qt5 核心库缺失或 xcb 系统依赖缺失，而是打包布局的插件路径隔离被错误应用到未部署 Qt 平台插件的 build-tree。

## Plan

1. 读取并保留远端 `src/app/main.cpp` 当前完整版本。
2. 仅在相邻 `platforms` 中存在 xcb 或 Wayland 平台插件时，覆盖 `QT_PLUGIN_PATH`、`QT_QPA_PLATFORM_PLUGIN_PATH` 并执行包内 QPA 自动选择。
3. 相邻平台插件不存在时保留调用方/Qt Kit 的插件环境，仅继续清除主进程不应继承的 `QT_WAYLAND_SHELL_INTEGRATION`。
4. 在现有 Qt Creator Debug build tree 中只重编 `SGStudio`，再做短时 X11 启动验证。

## Success Criteria

- Qt Creator build-tree 启动不再从不存在的 `bin/platforms` 强制查找 xcb。
- `QApplication` 成功创建，日志不再出现 `no Qt platform plugin could be initialized`。
- 打包布局中存在相邻平台插件时，原有包内插件隔离和 X11/Wayland选择保持不变。
- 远端工作区除 `src/app/main.cpp` 外不产生非构建产物源码修改。

## Verification Level

- `debug-run`
- 现有 Debug build tree 增量构建 `SGStudio`。
- 使用登录桌面的 X11 环境短时启动，检查 xcb 插件来源与退出行为。
- 检查远端 git diff 和工作区状态。

## Verification Update

- 第一版补丁仅在包内插件存在时覆盖路径；增量构建成功，但 Qt Creator 没有提供外部 `QT_PLUGIN_PATH`，Qt 仍只检查空的 build-tree `bin/platforms` 后 abort。
- `/opt/Qt/5.15.18/gcc_x86_64/bin/qmake -query QT_INSTALL_PLUGINS` 返回 `/opt/Qt/5.15.18/gcc_x86_64/plugins`。
- 修复补充为：包内插件缺失时，通过 `QLibraryInfo::PluginsPath` 使用当前已加载 Qt 的同源插件根；仅在调用方未显式提供对应环境变量时设置 fallback。
- 运行验证发现该 Qt 的 `QLibraryInfo::PluginsPath` 仍携带历史部署前缀，解析为 `/home/harogic/Desktop/bin`；这不是当前 Kit 的真实插件目录。
- 最终方案改为在 CMake configure 时调用当前 Kit/qmake 的 `-query QT_INSTALL_PLUGINS`，把权威开发插件目录作为仅供 build-tree 使用的 compile definition；包内插件存在时仍优先使用相邻目录。

## Final Verification

- 现有 Qt Creator Debug build tree 增量构建 `SGStudio` 成功。
- `QT_DEBUG_PLUGINS=1` 显示成功加载 `/opt/Qt/5.15.18/gcc_x86_64/plugins/platforms/libqxcb.so`。
- 8 秒短时启动由 `timeout` 终止，退出码为 `124`；未再出现平台插件 abort，未残留 SGStudio 进程。
- 用户随后在 Qt Creator 中确认程序已成功启动。
- 远端 `git diff --check` 通过；源码修改仅为 `src/app/CMakeLists.txt` 与 `src/app/main.cpp`。
