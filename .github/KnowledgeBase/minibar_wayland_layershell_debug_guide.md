# Minibar Wayland / LayerShellQt 交互与 Debug 指南

> 状态更新：2026-07-21。当前产品路径只有独立 `SGStudioMiniBar` helper；legacy
> in-process `MiniBarWindow` 已删除。Sweep/MOD `EnumTextButton` 的 Wayland
> 首击问题和随后出现的 Win32 popup 回归均已解决并完成实机验证。

本文保留长期有效的窗口模型、已证实根因和回归方法。排查过程中的猜测、临时
日志字段、失败 patch 清单与已删除实现不再作为知识库正文。

## 1. 先确定 surface，而不是先猜控件

在 helper 进程调用 `LayerShellQt::Shell::useLayerShell()` 后，每个 top-level
`QWindow` 都可能进入 layer-shell integration。按钮、panel 内容本身可以只是
child widget，但以下对象通常拥有独立 native surface：

| 对象 | Wayland 形态 | 责任方 |
| :--- | :--- | :--- |
| minibar base | top-level layer surface | `RemoteMiniBarWindow` |
| Sweep/MOD host | fullscreen transparent layer surface | 对应 overlay host |
| 数字键盘 | managed overlay layer surface | keyboard host |
| MOD menu | top-level menu + outside-click overlay | menu host |
| `EnumTextButton::PopupWidget` | top-level transient layer surface | popup owner + helper host |

诊断任何“点不到、全屏、错位、首次失败、外点不关闭”时，先记录：

- 事件实际到达哪个 QObject/QWindow；
- 该对象是否 top-level、是否已有 `windowHandle()`；
- native surface 在 `show()` 前还是之后完成 role/geometry 配置；
- owner、overlay 与 transient 的关闭顺序。

## 2. 坐标契约

普通 Win32/X11 popup 可以使用 `mapToGlobal()`、`move()` 与窗口 geometry。
Wayland layer-shell 的最终位置由 compositor 根据 output、anchors、margins 和 size
决定，Qt 的 global/frame geometry 不能单独作为事实源。

当前 host 统一发布：

```text
sgstudioLayerShellVisualTopLeft
sgstudioLayerShellVisualSize
```

Wayland popup/keyboard 用 owner 的 visual top-left 加 anchor 的窗口内局部坐标，
再按目标 screen available geometry 裁剪。top-right anchor 的横向位置必须换算为
right margin。

## 3. Hosted panel 与外点

- Sweep/MOD panel 是 fullscreen overlay 内的 child，不依赖普通 top-level dialog
  获取桌面级 grab。
- 内容区事件直接送达 panel；overlay 空白区消费 mouse/touch 并关闭当前 panel。
- owned-interaction 判断同时覆盖 QObject parent chain、widget top-level 与 native
  `windowHandle()`。
- 关闭顺序从最内层开始：enum/menu/keyboard -> panel -> overlay host。
- `ApplicationDeactivate` 只能作为辅助信号，不能代替明确的对象归属与
  outside-click 判定。

## 4. EnumTextButton 首击问题：最终结论

### 已观察事实

Raspberry Pi 日志显示，失败的第一次点击中：

- touch 与 Qt 合成 mouse 都到达目标按钮；
- `pressed`、`released`、`clicked`、`onClicked()` 均执行；
- `PopupWidget::popup()` 与 `show()` 均执行；
- 首个 popup surface 随后快速经历 `FocusIn -> FocusOut -> Close`；
- 第二次显示复用同一个已配置 surface，因此正常。

所以问题不是“touch 少发了一次点击”，也不是 Sweep/MOD 控件构造差异。

### 根因

旧实现直到 `QEvent::Show` 后的 `QTimer::singleShot(0)` 才配置 layer-shell
transient。此时首个 native surface 已被创建并映射；补配 role/geometry 太晚，
compositor 已按错误的初始状态处理它。

Sweep panel 不重复创建，所以只在第一次出现；MOD editor 每次新建，新的 popup
surface 每次都会再次经历该问题。

### 当前顺序

```text
更新 popup 内容
-> PopupWidget::popup() 确定最终尺寸
-> updatePopupPosition() 计算普通平台位置
-> Wayland-only prepareToShow（同步配置 layer surface）
-> show() 首次映射
```

`RemoteMiniBarWindow` 在 `prepareToShow` 中确认 popup 属于当前 Sweep/MOD owner，
然后设置 output、layer、top/right anchors、margins、size、scope、exclusive zone
和 keyboard interactivity。Show event 仍可登记 session，但不再承担首次 surface
配置。

## 5. 为什么 Wayland 修改影响了 Win32

第一版修复把 `prepareToShow` 放在共享 `EnumTextButton` 的所有平台路径中。
最初认为这是 Win32 回归的直接原因，但把事件限制为 Wayland-only 后，Win32
现场仍然复现，证明该判断不完整。真正关键的是修改位于所有页面共用的
`EnumTextButton::onClicked()`，并且普通 popup 定位仍依赖映射后的 Show event。
日志显示首个 native popup 已在 `(0,0)` 出现，而 Show 路径没有产生定位记录。

此外，旧 close guard 使用 200 ms single-shot `QTimer`，并用 `isActive()` 判断
是否仍需屏蔽点击。Win32 原生鼠标 press/release 处理中，timeout 事件可能晚于
真实截止时间投递；timer 因此仍报告 active，把后续点击继续拒绝。

最终修复分别收口两个问题：

1. 普通 popup 在 `show()` 前同步定位；特殊 pre-show 事件只在 Wayland 发送。
2. 200 ms guard 用 `QElapsedTimer` 的单调经过时间判断，不再把事件是否投递当作
   时钟。

这说明平台宏/运行时分支应围住真正的平台 policy，而不是改变整个共享控件的
普通生命周期。

## 6. 输入问题的取证顺序

mouse 与 touch 在 Qt/Wayland 下不是同一条输入路径：touch 可能被 Qt 合成为
mouse，compositor 的 grab、focus 与 surface routing 也可能不同。不能仅凭现象
把问题归结为“touch 没有 click”。

最小日志时间线应覆盖：

1. native `TouchBegin/Update/End` 与 `MouseButtonPress/Release` 的 receiver；
2. 按钮 `pressed/released/clicked`；
3. popup `Show/Hide/Close/Focus/PlatformSurface`；
4. `windowHandle()`、visibility、geometry 与 visual geometry；
5. layer-shell 配置相对 `show()`/首次 mapping 的顺序。

如果 `clicked/onClicked()` 未执行，检查输入路由；如果已执行但 popup 立刻关闭，
检查 surface role、focus/grab 与 ownership；如果只错位，检查坐标系和配置时机。

## 7. 不要重复采用的方向

- 仅增加 `singleShot(0)`、更长延时或重试，不能修复“首次映射前必须配置”的
  协议顺序。
- 把 `QMenu` 换成 `QDialog` 不会改变 top-level surface 仍被 layer-shell
  integration 接管的事实。
- 强制 `activateWindow()`、扩大 deactivate guard 或增加 close timer，缺少日志
  证据时只会模糊 ownership 问题。
- 只用 `frameGeometry()` 与 mouse global position 判断 layer-shell 内外点击，
  可能混用不同坐标语义。
- 把 Wayland overlay 无条件带到 Win32 会改变 Win32 原生 top-level 交互。

## 8. 回归矩阵

至少验证以下组合：

| 维度 | 用例 |
| :--- | :--- |
| 平台 | Win32；Raspberry Pi Wayland |
| 输入 | mouse；touch（Wayland） |
| popup | Sweep Type ListMode；MOD ListMode/IconMode |
| 生命周期 | 冷启动首次打开；关闭重开；重新创建 MOD editor |
| 几何 | 屏幕边缘；目标屏幕；无 active window fallback |
| 层级 | popup 内选择；第一次外点；panel 外点；keyboard 共存 |

通过标准：

- 第一次物理点击即可显示 popup；
- popup 位置正确且不短暂出现在 `(0,0)`；
- 关闭后超过 guard 时间可以再次打开；
- 外点只关闭当前最内层对象；
- Win32 不创建或执行 Wayland overlay/layer-shell policy。

## 9. 相关入口

- `src/libs/controls/enumtextbutton.cpp`
- `src/libs/controls/popupwidget.{h,cpp}`
- `src/app/minibarhelper/remoteminibarwindow.{h,cpp}`
- [Helper 当前边界](minibar_helper_layershell_parity_gaps.md)
- [LayerShellQt 构建与集成](minibar_wayland_layer_shell_qt_integration.md)
- [多屏 popup 几何](multiscreen_popup_geometry_and_screen_topology.md)
