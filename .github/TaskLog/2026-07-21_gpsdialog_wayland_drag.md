# GpsInfoDialog Wayland 拖动支持

## Scope

- 参考 `LockWidget`，让 `GpsInfoDialog` 在树莓派 Wayland 桌面下可通过自定义标题栏拖动。
- Wayland 下将 GPS 对话框保持为主窗口内的 `Qt::Widget` 子控件，使用现有 `WidgetMouseMoveTool` 处理 Mouse/Touch。
- 保持标题栏关闭按钮、GPS 配置控件和数字键盘流程不变；Windows 继续使用现有顶层 `Qt::Dialog`。

## Evidence and assumptions

- 观察：`GpsInfoDialog` 由主窗口作为 parent 创建，但 `Controls::Dialog` 构造函数把它设置为顶层 `Qt::Dialog | Qt::FramelessWindowHint`。
- 观察：`Controls::TitleBar` 当前依赖 `QCursor::pos()` 与顶层 `move()`；Wayland compositor 可以忽略顶层 surface 的客户端定位。
- 观察：`LockWidget` 已验证的路径是 Wayland hosted `Qt::Widget` + `WidgetMouseMoveTool`，后者已支持 Mouse/Touch 事件坐标和 parent 边界限制。
- 假设：用户期望从 GPS 自定义标题栏拖动，不应让内容区的 ComboBox、SwitchButton、LineEdit 等交互控件成为拖动手柄。

## Success criteria

- Wayland 下 `GpsInfoDialog` 是 MainWindow 内的 child widget，能够通过标题栏鼠标或触摸拖动。
- GPS 对话框不能被拖出主窗口可视范围，首次显示时位于主窗口中部。
- 标题栏关闭按钮保持可点击，不被拖动过滤器拦截。
- 内容区 GPS 配置、数字键盘和状态显示事件路径不变。
- 非 Wayland 平台保持现有顶层 `Controls::Dialog` 行为。

## Verification level

- `static`

## Verification checklist

- [x] Wayland hosted flags 与非 Wayland 顶层行为明确分流。
- [x] 标题栏空白区和标题 label 接入 Mouse/Touch 拖动，关闭按钮未接入。
- [x] 首次显示位置与拖动位置均限制在 parent 范围内。
- [x] GPS 与移动工具源文件均由当前 CMake 目标纳入。
- [x] `git diff --check` 通过。

## Verification result

- 静态验证通过：Wayland + 有 parent 时改用 `Qt::Widget` hosted 形态，其他平台仍沿用 `Controls::Dialog` 的顶层 flags。
- `WidgetMouseMoveTool` 只监听 GPS 标题栏和其直接 QLabel 子控件；关闭按钮及内容区没有安装该过滤器。
- `WidgetMouseMoveTool(QWidget *)` 原构造入口保留，新重载只用于选择是否自动监听整个移动目标，避免改变既有调用与导出符号。
- 首次 show 以 parent 中心为目标并 clamp；后续拖动继续由移动工具按 parent 尺寸 clamp。
- GPS 与 Controls 相关源文件已由现有 CMake 目标纳入，目标文件 `git diff --check` 通过。
- 按仓库默认规则未编译或运行；需在树莓派 Wayland 实机回归鼠标/触摸标题栏拖动、关闭按钮和数字键盘弹出。

## Field follow-up

- 树莓派 Wayland 实测发现关闭按钮触摸只有全局震动反馈，未形成按钮点击；此前“关闭按钮未安装移动过滤器即可保持可点击”的静态判断不完整。
- 后续修复记录见 `2026-07-21_gpsdialog_wayland_close_button_fix.md`：根因是接受 Touch 的标题栏祖先抢占了关闭按钮区域的触摸序列。
