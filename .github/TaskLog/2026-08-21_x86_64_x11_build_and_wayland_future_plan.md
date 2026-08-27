# x86_64 固定 X11 构建与未来 Wayland 恢复计划

## Scope

- 将 `scripts/build.sh` 的 `gcc` / `gcc_x86_64` / `x86_64` 分支固定为
  Linux x86_64 X11/xcb 构建和运行包。
- x86_64 构建关闭 vendored LayerShellQt，不再要求 QtWayland private API、
  `qtwaylandscanner`、`wayland-scanner` 或 layer-shell 协议生成依赖。
- x86_64 归档继续保留现有 launcher 文件名，但改用独立 X11 launcher
  模板，明确设置 `QT_QPA_PLATFORM=xcb`。
- aarch64 分支继续使用现有 Wayland/layer-shell 构建和 `launch_pi.sh`，不改变
  Raspberry Pi 发布语义。
- 新增长期 KnowledgeBase 文档，记录未来 x86_64 适配 labwc/wlroots
  Wayland 时的构建、打包、运行时和 UI 验收步骤。

## Success Criteria

1. x86_64 CMake configure 参数明确包含
   `SGS_ENABLE_VENDORED_LAYER_SHELL_QT=OFF`。
2. x86_64 构建不执行 layer-shell 专用 Wayland scanner/pkg-config 前置检查，
   但会检查 Qt xcb platform plugin 是否存在。
3. x86_64 归档 launcher 明确设置 `XDG_SESSION_TYPE=x11` 和
   `QT_QPA_PLATFORM=xcb`，并清除 Wayland shell integration 环境变量。
4. aarch64 继续明确使用 `SGS_ENABLE_VENDORED_LAYER_SHELL_QT=ON` 和原有
   Wayland launcher。
5. 文档明确区分当前已实现的 X11 路径与未来建议的独立 Wayland artifact，
   不把尚未存在的命令描述为当前可用功能。

## Verification Level

- Static only in this turn.
- Run Bash syntax checks for all changed shell scripts.
- Run `git diff --check` and inspect the generated launcher substitution path.
- The user will perform the x86_64 build and X11 UI runtime test on host 250.

## Runtime Acceptance On Ubuntu X11

- Main window starts through the packaged launcher with Qt platform name `xcb`.
- Minibar helper inherits xcb, becomes visible only through the existing explicit
  Minibar action, remains a normal `Qt::Tool` top-level window, and supports
  placement, dragging, collapse/expand, Restore, Sweep/MOD popup, and numeric
  keyboard interaction.
- No `libLayerShellQtInterface.so.5` or `liblayer-shell.so` is required by the
  x86_64 helper.

## Implementation And Verification Status

- `scripts/build.sh` now selects `SGS_ENABLE_VENDORED_LAYER_SHELL_QT=OFF`,
  requires the Qt xcb platform plugin, skips layer-shell Wayland scanner checks,
  and renders `scripts/launch_linux_x11.sh` for all canonical x86_64 aliases.
- The new X11 launcher fixes the child environment to xcb and removes inherited
  Wayland shell selection. Existing archive launcher filenames are preserved for
  deployment compatibility.
- aarch64 still selects LayerShellQt `ON` and the existing Wayland launcher.
- Added `linux_x86_64_x11_and_future_labwc_wayland.md`, updated the KnowledgeBase
  index, and corrected the Linux packaging guide's x86_64 acceptance statement.
- Bash syntax checks passed for both launcher templates and `build.sh`; rendered
  X11 launcher syntax, launcher help output, static content assertions, trailing
  whitespace checks, and `git diff --check` passed.

