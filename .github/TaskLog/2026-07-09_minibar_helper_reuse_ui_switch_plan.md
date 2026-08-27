# Minibar Helper Reused-UI Switch

本文记录 `MainWindow <-> SGStudioMiniBar` 双进程显隐切换的当前实现。原始实施
计划已经完成，本文只保留产品决策、实际流程、失败边界和仍需明确的后续策略。

## 当前决策

1. 用户入口始终是 `SGStudio`；`SGStudioMiniBar` 是随主程序部署的私有
   companion executable，不创建桌面或开始菜单入口。
2. helper 由 main 通过命令行参数启动：

```text
--sgstudio-minibar-helper
--server <local-server-name>
--token <session-token>
--host-pid <main-pid>
```

3. 缺少这些参数时，helper 提示“由 SGStudio 自动启动”并退出，避免双击后没有
   反馈。
4. 首次切换时按需创建 helper 进程、窗口、server 和 socket；之后反复切换只做
   显隐，不销毁或重新创建。
5. 只有 SGStudio 明确退出、helper 崩溃或 IPC 失效时才结束 helper 进程。
6. main 是 visible-mode owner。helper 不跨进程控制 MainWindow。
7. Win32 和 Wayland 共用同一 lifecycle 状态机；差异只保留在 helper 的窗口
   surface/geometry 实现。
8. 旧 `--ui-mode=main|minibar` 重启路径代码仍作为 helper 启动失败时的应急
   fallback，但产品与新功能口径已经退役，只作为参考，不再要求功能等价。

## 实际流程

Main 切到 helper：

```text
user clicks the existing minibar action
  -> MinibarHelperController starts QLocalServer if needed
  -> starts SGStudioMiniBar if needed
  -> helper AUTH + READY
  -> main hides its existing MainWindow
  -> main sends SHOW_MINIBAR <top-right>
  -> helper shows its existing RemoteMiniBarWindow
  -> helper sends VISIBLE
```

Helper 恢复 main：

```text
user clicks Restore
  -> helper sends RESTORE_REQUEST
  -> main sends HIDE_MINIBAR
  -> helper hides the existing window and sends HIDDEN
  -> main restores the previous normal/maximized/fullscreen state
  -> process, socket and both window objects remain alive
```

SGStudio 退出：

```text
main enters ApplicationClosing
  -> sends SHUTDOWN when socket is connected
  -> bounds helper cleanup with terminate/kill fallback
  -> closes socket/server and continues normal plugin shutdown
```

Helper 异常：

```text
socket disconnect / process exit / transition timeout
  -> controller restores MainWindow when it had been hidden
  -> clears the broken helper session
  -> leaves device/runtime state untouched
```

## Lifecycle 状态

```text
MainVisible
StartingHelper
WaitingForVisibleAck
MinibarVisible
WaitingForHiddenAck
MainHiddenHelperHidden
ApplicationClosing
```

`MainHiddenHelperHidden` 用于设备断开后的 minibar 等待状态：main 继续隐藏，
helper 也隐藏，但进程和 IPC 保持存活；设备重连后可以显示同一个 helper 窗口。

## UI 复用现状

当前“复用”是视觉和交互约定复用，不是共享同一个 QWidget 类：

1. helper 使用独立 `RemoteMiniBarWindow`，复制了 collapsed/expanded 布局、
   控件顺序、尺寸、样式和拖动行为。
2. 旧 in-process `MiniBarWindow` 仍由 Core/runtime 驱动，不能直接链接进
   helper。
3. helper 已复用 `Controls::TouchNumKeyboard` 和 Business 单位 adapter，
   但不复用任何 Core/runtime/device 对象。
4. helper Sweep 已使用独立 `RemoteSweepPanel` child view + IPC DTO 实现；没有
   复用/复制旧 `StepSweepPanel` 的业务绑定。
5. 两套窗口代码仍存在，但 legacy 只承担应急兼容，不再作为新 helper 功能的
   第二实现目标。

不抽取共享 `MiniBarView`。legacy 已明确过期；新功能只进入 helper，旧
`MiniBarWindow` 仅用于视觉和已验证平台处理参考，避免为了应急 fallback 重构
旧 3000 行窗口或形成双业务真相。

## 平台边界

Win32：

1. helper 是 `WIN32_EXECUTABLE`，正常启动不出现控制台。
2. 使用普通 frameless/tool/stays-on-top QWidget。
3. main 仅恢复自己的窗口，不使用 HWND 跨进程控制。

Raspberry Pi Wayland：

1. 只有 helper 在创建顶层窗口前调用
   `LayerShellQt::Shell::useLayerShell()`。
2. main 始终保持普通 xdg-shell 语义。
3. helper base surface、拖动和数字键盘 overlay 由 layer-shell host 管理。

## 部署约束

1. `SGStudioMiniBar` 必须和 `SGStudio` 一起进入运行时 `bin/`。
2. launcher、desktop entry 和用户文档仍只指向 `SGStudio`。
3. helper 不加载 plugin 目录，不参与 USB owner、scanner 或多实例 gate。
4. helper executable 缺失时，main 保持/恢复可见，并可进入 legacy fallback。

## 尚未实现

1. main 启动时主动创建一个隐藏 helper session。
2. “main/helper 冷启动双隐藏，首次设备打开后显示 helper”的 startup policy。
3. 可配置的 headless failure recovery。

当前代码只能在用户已经进入 minibar 后支持设备断开时双隐藏、重连时重新显示。
不要把该行为等同于冷启动自动 minibar。

## 下一步

显隐生命周期本身不需要重写。进入下一阶段前只需：

1. 已明确：legacy fallback 只作应急兼容与参考，不承接新功能。
2. 若需要冷启动双隐藏，新增显式 startup visibility policy，并覆盖所有
   MainWindow startup `show()/showFullScreen()` 入口。
3. 已完成：IPC 协议已迁入 `MinibarIpc` static target，helper socket client
   已从 `main.cpp` 提取为 `MinibarClient`。
4. 保持“显隐不销毁、main 退出才统一销毁”作为后续功能的回归条件。
