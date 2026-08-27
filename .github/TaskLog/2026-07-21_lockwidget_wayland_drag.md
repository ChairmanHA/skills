# LockWidget Wayland 拖动修复

## Scope

- 参考 `TouchNumKeyboard` 的 Wayland 拖动路径，补齐 `LockWidget` 的鼠标与触摸拖动。
- 保持 `LockWidget` 为主窗口内的子控件，并将拖动位置限制在 parent 可见区域内。
- 保留现有交互：未锁定时允许拖动；锁定时禁止移动；短按锁图标仍切换锁定状态。

## Evidence and assumptions

- 观察：`TouchNumKeyboard` 使用事件自身的全局坐标记录拖动起点和位移，并显式处理 Mouse/Touch；Wayland 下以 `Qt::Widget` 子控件形态移动。
- 观察：`LockWidget` 已由 `MainWindow` 作为 parent 创建，但其 `WidgetMouseMoveTool` 使用 `QCursor::pos()`，没有 Touch 事件路径，也没有直接监听铺满控件的 `QLabel`。
- 推断：树莓派 Wayland 下拖动失败来自输入坐标/触摸事件链不完整，而不是锁定状态逻辑；修复应收敛在现有移动工具和 `LockWidget` 的 drag handle 接入处。

## Success criteria

- `LockWidget` 未锁定时，可通过鼠标或触摸拖动，并随输入点实时更新位置。
- 拖动位置不会越出 parent widget 的可见范围。
- `LockWidget` 锁定时，鼠标或触摸移动不会改变位置。
- 未形成拖动的短按仍只触发一次锁定/解锁切换。
- Windows 现有 child-widget 拖动与点击语义保持不变。

## Verification level

- `static`

## Verification checklist

- [x] `LockWidget` 与内部 label 均接入 Mouse/Touch 拖动事件。
- [x] 拖动坐标来自 `QMouseEvent` / `QTouchEvent`，不再依赖轮询 `QCursor::pos()`。
- [x] 未锁定/锁定分支与点击判定保持互不冲突。
- [x] 修改文件均已由当前 CMake 目标纳入编译。
- [x] `git diff --check` 通过。

## Verification result

- 静态验证通过：`LockWidget` 在 Wayland 使用 `Qt::Widget` child flags，移动工具覆盖 label 上的 Mouse/Touch，并按 parent 尺寸 clamp。
- `m_moveEnable` 继续由 `m_checked` 控制；锁定手势不移动，解锁后的有效拖动不会误判成点击。
- `widgetmousemovetool.cpp` 与 `lockwidget.cpp/.h/.ui` 均已由当前 CMake 目标纳入。
- 按仓库默认规则未执行编译或运行验证；需在树莓派 Wayland 实机回归触摸拖动、鼠标拖动、锁定后禁止拖动三种路径。
