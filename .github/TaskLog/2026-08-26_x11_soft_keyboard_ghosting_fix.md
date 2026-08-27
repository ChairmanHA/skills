# X11 软键盘残影修复（2026-08-26）

## Scope

修复 `TouchNumKeyboard` 在无 compositor 的 X11 环境中反复创建时出现的桌面残影，保持 Wayland overlay、键盘交互和外部点击关闭行为不变。

## Observation and evidence

- 现场机器 `192.168.1.12` 为 Ubuntu 18.04/aarch64、LXDE/Openbox；X11 Composite 扩展存在，但没有 `compton`、`picom` 或 `xcompmgr` compositor 进程/包。
- 软键盘每次重新创建；About 对话框启动时创建、之后只显示。残影中可见 RF 开关，说明新键盘在最终位置之外首次映射时暴露了旧窗口内容。
- `TouchNumKeyboard` 当前无条件请求 `windowOpacity(0.98)`，而主题本身已绘制不透明背景。
- X11 top-level 路径当前先 `setVisible(true)`，再 `move()` 到计算出的最终位置；Wayland overlay 也有独立的 hosted 生命周期，不在本次修改范围。

## Inference

在无 compositor 的 X11 上，顶层窗口透明度无法提供可靠的合成效果；新窗口先以继承/默认几何映射，再移动到键盘位置，会使非合成 X11 的 backing store 暴露中间帧，从而形成残影。该行为与 About 的预创建/只 show 差异一致。

## Minimal design

1. Linux X11 top-level keyboard 使用 `1.0` opacity；Wayland 和其他平台保留现有 `0.98`。
2. X11 top-level keyboard 在第一次 `setVisible(true)` 前完成最终 `move()`；显示后只执行 raise/activate。
3. 不增加延时、重绘、全局 OpenGL 设置、compositor 安装或键盘单例复用。

## Success criteria

- X11 top-level 键盘首帧直接出现在最终位置，不要求 compositor，也不留下 RF/桌面旧内容。
- Wayland keyboard overlay 的定位和输入行为无变化。
- X11 键盘仍由现有 application event filter 捕获外部点击并关闭。

## Verification

- Verification level: `static`（用户负责目标机运行回归）。
- `git diff --check`。
- 检查 diff 仅涉及键盘呈现顺序/opacity，以及本知识库中的平台契约说明。

## Implementation result

- `src/libs/controls/touchnumkeyboard.cpp` 新增平台相关 opacity 选择：Linux
  X11/Wayland 为 `1.0`，其他平台保持 `0.98`。
- `TouchNumKeyboard::setVisible(true)` 的 top-level 分支改为先 `move()`、后
  `QDialog::setVisible(true)`。
- `RemoteMiniBarWindow::applyCollapsedOpacity()` 在 Linux 统一使用 `1.0`；Win32
  仍使用原有 active `1.0` / inactive `0.65`。
- `src/libs/controls/CMakeLists.txt` 已确认包含该实现文件；静态 `git diff --check`
  通过（仅有仓库既有的行尾转换提示）。

## Follow-up: Linux Minibar collapsed opacity

现场对比显示，`RemoteMiniBarWindow::applyCollapsedOpacity()` 的 `0.65` 仅在有
compositor 的 Linux/X11 上生效；无 compositor 的 Debian/Raspberry Pi 上会退化为
不透明，导致同一包在 Linux 机器间外观不一致。该效果是纯视觉效果，Win32 仍保留
现有 hover 半透明，Linux（X11/Wayland）统一使用 `1.0`。
