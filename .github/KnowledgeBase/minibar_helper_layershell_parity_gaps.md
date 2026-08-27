# Minibar Helper Layer-Shell 当前边界

> 状态更新：2026-07-21。Raspberry Pi Wayland 下 Sweep Type 与 MOD
> `EnumTextButton` 首次点击问题已解决；Win32 popup 定位与重复打开回归也已通过
> 实机测试。历史猜测、临时日志方案和已回退尝试不再保留在本文。

本文只记录 `SGStudioMiniBar` 当前仍需维护的平台契约。详细排障方法见
[Minibar Wayland / LayerShellQt 交互与 Debug 指南](minibar_wayland_layershell_debug_guide.md)。

## 窗口模型

### Base surface

- 只有 Wayland helper 进程调用 `LayerShellQt::Shell::useLayerShell()`。
- base 使用 `LayerOverlay`、`AnchorTop | AnchorRight`、`exclusiveZone=0` 和
  `KeyboardInteractivityNone`。
- 拖动通过 top/right margins 表示，不依赖 Wayland 下的 `QWidget::move()`。
- host 发布 `sgstudioLayerShellVisualTopLeft` 与
  `sgstudioLayerShellVisualSize`。layer-shell popup 和键盘以这组 visual
  geometry 为坐标事实源。

### Sweep、MOD 与键盘

- 在 LayerShell Wayland 上，Sweep/MOD 内容是 fullscreen transparent overlay
  host 内的 child widget；overlay 空白区负责 outside-click，内容区事件直接送达
  子控件。
- Linux X11 上 Sweep/MOD 都使用普通 top-level `Qt::Tool`，不创建 fullscreen
  transparent overlay。X11 是否能正确显示顶级窗口透明区域取决于 compositor/ARGB
  visual，不能把它作为弹层正确显示的前提。
- Linux X11 的 Sweep 外点关闭分为两类：helper 进程内点击由 application mouse
  event filter 关闭；点击桌面或其他应用时由 `ApplicationDeactivate` 关闭。后一种
  不拦截目标点击，只保持 Sweep 随外点收起的可见行为。
- Win32 上 MOD 保持普通 top-level `Qt::Tool`；Sweep 保留现有 fullscreen overlay
  及其 outside-click 行为。
- Linux X11 的 `TouchNumKeyboard` 使用普通不透明 top-level `Qt::Tool`；必须在首次
  `show()` 前完成最终位置，避免无 compositor 时新窗口 backing store 的中间帧残影。
  Linux Wayland 也保持不透明；Wayland 仍使用独立 managed overlay，并由 helper 跟踪
  单一 keyboard session。
- Linux Minibar 收起态保持不透明；只有 Win32 保留现有的 hover 半透明效果，避免
  compositor 能力差异造成同一包外观不一致。
- 收口顺序为最内层 popup/keyboard -> panel -> overlay host；hide、collapse、
  Restore、断连和 shutdown 使用相同清理入口。

### Menu 与 outside-click

- Wayland layer-shell `QMenu` 不能假定 compositor 提供 Win32 风格的桌面级
  outside-click grab。
- Remote MOD menu 使用同 output 的透明 fullscreen overlay 捕获空白区输入；
  menu 隐藏时同步隐藏 overlay。
- owned 判断同时识别 QObject parent chain、top-level widget 与 native
  `windowHandle()`；不能只靠 `frameGeometry()` 与全局坐标判断。

## EnumTextButton popup 契约

共享 `PopupWidget` 是 top-level transient。在 helper 的全局 layer-shell
integration 中，它也会获得 layer surface，因此必须在首次映射前完成配置。

当前显示顺序：

```text
PopupWidget::popup() 完成布局/尺寸
-> EnumTextButton 同步计算普通 QWidget 位置
-> Wayland-only prepareToShow 事件
-> RemoteMiniBarWindow 配置 layer/output/anchors/margins/size
-> QWidget::show()
```

关键规则：

1. `Controls` 只暴露平台无关的同步 pre-show 扩展点，不链接 LayerShellQt。
2. `RemoteMiniBarWindow` 只在 Wayland layer-shell active 且 popup 属于当前
   Sweep/MOD panel 时处理该事件。
3. `QEvent::Show` 可登记 popup ownership/session，但不能再延迟配置首次 native
   surface。
4. Win32 不发送 pre-show 平台事件，使用普通 QWidget 定位：ListMode 锚定按钮，
   IconMode 居中于 owner。
5. popup close 后的 200 ms 防抖以单调经过时间判断，不以
   `QTimer::isActive()` 代替时钟。
6. 外点先关闭最内层 enum popup，后续外点才关闭 Sweep/MOD panel。

## Remote MOD enum 数据边界

- 语义为 enum 的字段使用共享 `EnumTextButton`。
- 控件内部只保存整数 option index；写回时再映射到 profile 原始
  `QJsonValue`，局部 index 不进入 IPC request。
- Shape、Filter Type、Tone Phase 使用 ListMode；Digital Modulation Type 与
  Oversample 使用带标题的 IconMode。

## 仍需单独跟踪的事项

- hosted `PxOpenFileDlg` 的 fullscreen Wayland overlay 仍未收口。
- hosted Save dialog 上方的系统软键盘层级问题仍是独立事项。
- mouse drag 抖动与 enum popup 生命周期无关；如继续优化，应控制
  layer-shell margin commit 频率。

## 维护规则

1. helper 每新增一个 top-level widget，都先确认它在全局 layer-shell
   integration 下的 surface role，并在首次映射前完成配置。
2. Wayland layer-shell 几何使用 host 发布的 visual geometry；Win32/X11 继续使用
   QWidget global geometry。
3. 平台差异放在 host policy 层，公共 `Controls` 保持平台无关。
4. 不通过额外延时、重复 timer 或强制 `activateWindow()` 掩盖 surface
   生命周期问题。
