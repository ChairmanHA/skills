# Linux x86_64 X11 基线与未来 labwc/wlroots Wayland 适配

## 当前发布决策

Linux x86_64 包当前固定以 X11/xcb 为运行基线。该决策只针对
`scripts/build.sh` 的 `gcc`、`gcc_x86_64` 和 `x86_64` 入口，不改变
aarch64 Raspberry Pi 的 Wayland/layer-shell 发布路径。

当前 x86_64 构建约束：

1. CMake 显式使用 `SGS_ENABLE_VENDORED_LAYER_SHELL_QT=OFF`。
2. 不构建或链接 `LayerShellQtInterface` 与 `liblayer-shell.so`。
3. 不要求 `Qt5::WaylandClientPrivate`、`qtwaylandscanner`、
   `wayland-scanner` 或 layer-shell 协议生成依赖。
4. 构建前检查 `${QT_PLUGIN_DIR}/platforms/libqxcb.so`。
5. 归档使用 `scripts/launch_linux_x11.sh` 生成 launcher；launcher 设置
   `XDG_SESSION_TYPE=x11`、`QT_QPA_PLATFORM=xcb`，并清除
   `WAYLAND_DISPLAY` 与 `QT_WAYLAND_SHELL_INTEGRATION`。
6. 为兼容既有部署入口，归档内 launcher 文件名仍保留
   `launch_sgstudio_pi.sh` 或 `launch_pi.sh`；应以 launcher 输出的
   `[launch-x11]` 和 `qt-platform: xcb` 判断实际后端，不应再从文件名推断。

LayerShellQt 是否需要与 CPU 架构无关。当前关闭它，是因为 x86_64 目标
明确选择 X11，而不是因为 x86_64 本身不支持 layer-shell。

## 没有 LayerShellQt 时的 Minibar 行为

`SGStudioMiniBar` 仍会被构建。CMake 只在 `LayerShellQt::Interface` 存在时
定义 `SGS_HAVE_LAYER_SHELL_QT`；关闭该选项后，helper 使用已有普通窗口路径：

- `Qt::Tool | Qt::FramelessWindowHint | Qt::WindowStaysOnTopHint`；
- `show()` / `raise()` 显示；
- `setGeometry()` / `move()` 定位和拖动；
- Sweep、MOD、QMenu 和数字键盘使用普通 QWidget/X11 顶层窗口语义。

这条路径适合 X11 window manager。它不构成 Wayland 下永远悬浮、精确全局
定位或桌面级 outside-click 的保证。

## 当前 x86_64 构建和测试入口

在 x86_64 Linux 构建机上：

```bash
bash scripts/build.sh gcc_x86_64 standard cn SGStudio --watermark off
```

归档默认输出到：

```text
build/linux_x86_64/standard/cn/SGStudio.tar.gz
```

目标 Ubuntu X11 验收前先确认登录会话：

```bash
echo "$XDG_SESSION_TYPE"
```

通过归档根 launcher 启动后，检查输出包含：

```text
[launch-x11] qt-platform: xcb
```

UI 验收至少覆盖：主窗口启动、进入 Minibar、collapsed/expanded、拖动、
Restore、RF/Center/Level、Sweep/MOD panel、菜单、数字键盘和退出恢复。

## 何时需要恢复 x86_64 Wayland

只有在目标产品明确固定为 labwc/wlroots Wayland，并要求 Minibar 在普通窗口
或全屏窗口上方持续悬浮、按 top/right anchor 精确定位、拖动和 popup/overlay
层级保持一致时，才恢复 x86_64 layer-shell 构建。

不要仅凭“系统是 Ubuntu”启用。恢复前必须在目标桌面会话确认 compositor
发布 `zwlr_layer_shell_v1`：

```bash
echo "$XDG_SESSION_TYPE"
wayland-info | grep zwlr_layer_shell_v1
```

如果协议不存在，vendored integration 本身没有显式的 xdg-shell 自动回退；
不能把“库可以成功编译”当作“运行时可以显示”。

## 推荐的恢复方式

未来不要直接把当前 `gcc` 基线整体切回 Wayland。建议新增独立、显式的构建
入口，例如 `gcc_x86_64_wayland`，并使用独立 build/package 目录和 archive
标识。这样 X11 发布包仍然可复现，Wayland 包也不会靠目标机环境变量碰运气。

建议的 Wayland 分支配置：

1. `DISPLAY_BACKEND=Wayland`、`QT_QPA_PLATFORM=wayland`。
2. `SGS_ENABLE_VENDORED_LAYER_SHELL_QT=ON`。
3. 使用独立 Wayland launcher 模板；主进程启动前清除外部
   `QT_WAYLAND_SHELL_INTEGRATION`，只允许 `SGStudioMiniBar` helper 在创建
   native surface 前调用 `LayerShellQt::Shell::useLayerShell()`。
4. 恢复 layer-shell 专用前置检查：`qtwaylandscanner`、
   `wayland-scanner`、`pkg-config --exists wayland-client wayland-protocols
   xkbcommon`。
5. 不复用 `linux_x86_64` X11 输出目录，避免两类包互相覆盖。

上述入口名称是未来建议，当前尚未实现，不能直接作为现有命令使用。

## Wayland 构建依赖和 Qt ABI 边界

使用 `/opt/Qt/5.15.18/gcc_x86_64` 时，下面内容必须来自与 Qt Core/Gui/
WaylandClient 完全一致的 Qt 构建：

- `Qt5WaylandClientConfig.cmake`；
- `Qt5::WaylandClientPrivate` 和 QtWayland private headers；
- `qtwaylandscanner`；
- xkbcommon private/support target（如 wrapper 要求）；
- Wayland platform 和 shell integration plugins。

系统 Debian/Ubuntu Qt 与 `/opt/Qt/5.15.18` 不能混合使用 private headers、
platform plugin 或 `libQt5WaylandClient.so.5`。LayerShellQt 链接 QtWayland
private API，版本号相近不代表 private ABI 可混用。

系统侧通常还需要：

```text
libqt5waylandclient5-dev
qtwayland5-private-dev
qtwayland5-dev-tools
libwayland-dev
libwayland-bin
wayland-protocols
libxkbcommon-dev
pkg-config
```

如果继续使用自建 `/opt/Qt`，包名只用于说明能力需求，真正取用的 CMake
target、headers、scanner 和运行库仍应全部来自同一 `/opt/Qt` 前缀。

## Wayland 包必须包含的运行时

未来 Wayland 包至少验证以下目标架构文件：

```text
bin/platforms/libqwayland-egl.so
bin/platforms/libqwayland-generic.so
bin/wayland-shell-integration/liblayer-shell.so
bin/wayland-shell-integration/libxdg-shell.so
lib/libLayerShellQtInterface.so.5
lib/libQt5WaylandClient.so.5
```

同时保留 Qt Core/Gui/Widgets、xkbcommon、Wayland client 和 compiler runtime
的正常依赖。使用 `file`、`readelf`、`ldd` 和
`scripts/audit_linux_package_ui_runtime.sh --expected-arch x86_64` 检查：

- 所有 ELF 都是 x86_64；
- Qt 库和 plugins 来自同一个 Qt 版本；
- 没有 `not found`；
- `wayland-shell-integration` 没有意外的嵌套同名目录。

## Wayland 运行时分流边界

当前 helper 只根据“编译时存在 LayerShellQt”与“Qt platform name 包含
wayland”调用 `useLayerShell()`；它没有先确认 compositor 协议能力。因此未来
有两种安全选择：

1. 推荐：Wayland artifact 只投放到已经确认支持 `zwlr_layer_shell_v1` 的
   labwc/wlroots 设备。
2. 如果同一 artifact 必须兼容未知 Wayland compositor：在 helper 创建任何
   native surface 前增加可靠的协议能力探测，并让不支持的环境保持普通
   xdg-shell；同时测试普通 Wayland 回退的定位和层级限制。

不要在 helper 已创建 native surface 后再切换
`QT_WAYLAND_SHELL_INTEGRATION`。

## labwc/wlroots UI 验收矩阵

恢复 Wayland 后必须在真实 compositor 上验证，而不只做编译检查：

1. Minibar 在普通窗口和全屏窗口上方持续可见，不进入普通任务栏。
2. 初始位置、collapsed/expanded 和 Restore 正确。
3. 鼠标和触摸拖动实时更新 top/right margins，释放后位置不跳变。
4. Sweep/MOD fullscreen transparent overlay 的内容区可交互，空白区只关闭
   当前最内层对象。
5. QMenu、EnumTextButton popup 首次和重复打开都位置正确。
6. 数字键盘不会被 Minibar client area 裁剪，关闭顺序正确。
7. 多屏、屏幕边缘、1280x800、窗口重开和 helper 断连/重启均通过。
8. 主进程仍使用普通 xdg-shell；只有 helper 使用 layer-shell。

更深入的 surface、visual geometry、popup 和 overlay 排障规则见
`minibar_wayland_layershell_debug_guide.md` 与
`minibar_wayland_layer_shell_qt_integration.md`。
