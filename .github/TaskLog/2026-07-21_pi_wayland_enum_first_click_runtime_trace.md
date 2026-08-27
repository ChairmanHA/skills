# EnumTextButton 首次打开与 Win32 回归收口

## 范围与验收标准

- 修复 Raspberry Pi Wayland 下 Sweep Type 与每次新建的 MOD editor 中
  `EnumTextButton` 第一次点击不显示 popup。
- 不改变 Win32 普通 popup 的定位和重复打开行为。
- 删除临时运行日志，只保留可复用的生命周期结论。

验证级别：Raspberry Pi Wayland 与 Win32 实机回归。

## Wayland 证据与根因

运行日志确认，第一次点击时 touch、Qt 合成 mouse、`pressed`、`released`、
`clicked` 和 `onClicked()` 均正常到达。问题不在输入分发，也不在按钮的
200 ms close guard。

失败发生在 popup 的第一个原生 Wayland surface：

1. `PopupWidget::popup()` 完成内容布局和尺寸计算。
2. 旧实现调用 `show()`，surface 已经创建并映射。
3. helper 直到 `QEvent::Show` 后的 `singleShot(0)` 才配置 layer、output、
   anchors、margins 和 size。
4. 首个 surface 在配置前已按错误角色映射，随后快速经历
   `FocusIn -> FocusOut -> Close`。
5. 第二次显示复用了已配置的 native surface，所以表现正常。

Sweep panel 长期存在，只在第一次遇到该状态；MOD editor 每次新建，因而每次
都重新暴露同一个问题。

## Wayland 修复

`PopupWidget` 提供同步的 `prepareToShow` 事件。`EnumTextButton` 在 popup
尺寸确定后、`show()` 前发送该事件；`RemoteMiniBarWindow` 只在 Wayland
layer-shell 路径处理它，并在首次映射前同步完成 surface 配置。

公共 `Controls` 不依赖 LayerShellQt；具体的 surface policy 仍由 helper host
负责。`QEvent::Show` 只用于 ownership/session 登记，不再承担延迟配置。

## Win32 回归与根因

最初怀疑是 `prepareToShow` 在所有平台发送，随后把它限制为 Wayland-only，
但 Win32 现场仍然复现。因此“Win32 收到了自定义事件”不是最终根因，只是平台
边界确实过宽。

Win32 日志最终暴露了共享 popup 路径中的两项问题：

1. 普通定位依赖后续 Show event，但日志中首个 native popup 已在 `(0,0)` 显示，
   Show 路径没有产生定位记录。最终位置不能依赖映射后的 event filter。
2. popup 关闭后的 200 ms 防抖用 `QTimer::isActive()` 判断。Win32 原生鼠标处理
   期间 timeout 事件可能延迟投递；真实时间已经超过 200 ms 时，timer 仍可能
   报 active，后续点击便被错误拒绝。

Wayland layer-shell 配置并没有在 Win32 生效。Win32 会受影响，是因为修改点位于
所有页面共用的 `EnumTextButton::onClicked()`，不是 helper 私有实现；新的显示
时序暴露了公共路径中“Show event 一定负责定位”和“timer active 等价于 200 ms
尚未经过”这两个错误假设。

## 最终实现

打开顺序固定为：

```text
更新宽度/当前项 -> PopupWidget::popup() -> 同步计算普通位置
-> Wayland-only prepareToShow -> show()
```

- ListMode 锚定按钮并裁剪到当前屏幕；IconMode 居中于 active/modal owner，
  缺少 active window 时回退到按钮所属窗口。
- `prepareToShow` 仅在 Qt platform name 为 Wayland 时发送。
- 关闭防抖保留 200 ms 语义，但改用单调时钟记录 close 时间，不再依赖 timeout
  是否已经被事件循环投递。
- 临时 `[ENUM_TRACE]` / `[ENUM_WIN_TRACE]` 及专用日志文件代码均已删除。

## 验证结果

- Raspberry Pi Wayland：Sweep Type 和新建 MOD editor 的 enum popup 首次点击可见。
- Win32：popup 位置恢复正常，关闭后可再次打开；用户实机测试通过。
- Win32 Debug `Controls` target 编译通过。

相关提交：`2b49b3a6`（Wayland 首次映射修复）、`fdc7a224`（Win32 公共路径收口）。
