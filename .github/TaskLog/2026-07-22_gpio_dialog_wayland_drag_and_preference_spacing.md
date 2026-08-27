# GPIO Dialog Wayland Drag And Preference Spacing

## Scope

- 参考 `GpsInfoDialog`，让 `GpioDialog` 在树莓派 Wayland 桌面下作为主窗口内的 child widget 显示，并支持标题栏鼠标/触摸拖动。
- 保留非 Wayland 平台现有顶层 `Controls::Dialog` 行为。
- 保证标题栏关闭按钮仍可通过 touch 形成按钮点击并关闭窗口。
- 将 `PreferenceDialog` 按钮间距从 15px 调整为与 GPIO 按钮一致的 5px。

## Evidence And Assumptions

- 观察：`MainWindow` 使用 `new GpioDialog(this)` 创建 GPIO 对话框，但 `Controls::Dialog` 默认将其设为顶层 frameless dialog；Wayland compositor 可忽略顶层 surface 的客户端 `move()`。
- 观察：`GpsInfoDialog` 已在 Wayland + 有 parent 时改用 `Qt::Widget`，并用 `WidgetMouseMoveTool` 监听标题栏鼠标事件和标题 QLabel 的 Mouse/Touch 事件。
- 观察：GPS 的现场修复明确表明，不能让整个标题栏接受原生 Touch，否则标题栏祖先会抢占关闭按钮区域的触摸序列；仅标题 QLabel 设置 `WA_AcceptTouchEvents`。
- 观察：`GpioDialog` 与 `PreferenceDialog` 都由当前 `src/plugins/core/CMakeLists.txt` 纳入编译。
- 观察：GPIO 网格的水平和垂直 spacing 均为 5px，Preference 网格当前统一 spacing 为 15px。
- 假设：用户所说的 Preference “按钮之间的间距”指网格 item spacing，不包括现有内容边距。

## Success Criteria

- Wayland + 有 parent 时，GPIO 对话框作为 child widget，可通过标题栏空白区用鼠标拖动、通过标题文字区域用鼠标或触摸拖动。
- GPIO 首次显示时位于 parent 中部，拖动位置限制在 parent 可视范围内。
- GPIO 关闭按钮不接入拖动过滤器，其触摸仍走既有 `clicked -> reject` 路径。
- 非 Wayland 平台 GPIO 行为不变。
- Preference 网格按钮水平、垂直间距均为 5px。

## Verification Level

- `static`

## Verification Checklist

- [x] GPIO Wayland hosted flags 与非 Wayland路径明确分流。
- [x] 标题 QLabel 接受 Touch；标题栏自身和关闭按钮不接受/拦截原生 Touch。
- [x] GPIO 首次位置和拖动位置均受 parent 边界限制。
- [x] Preference 网格 spacing 与 GPIO 一致。
- [x] 目标文件通过 `git diff --check`。

## Verification Result

- 静态核对确认：GPIO 的 Wayland hosted 分支与 GPS 当前实现保持同一事件边界，`WidgetMouseMoveTool` 监听标题栏鼠标事件以及标题 QLabel 的 Mouse/Touch 事件，未监听 `m_pCloseButton`。
- `WidgetMouseMoveTool` 的现有实现会将 child widget 的目标位置 clamp 到 parent 尺寸内；GPIO 首次 show 也按相同 parent 坐标系居中并 clamp。
- `preferencedialog.ui` 可被 XML 解析，网格 spacing 已由 15px 改为 5px。
- 目标文件 `git diff --check` 通过；按仓库默认规则未编译或运行，仍需在树莓派 Wayland 实机回归触摸关闭和标题触摸拖动。

## Startup Visibility Bug Follow-up

### Field Evidence

- 现场观察：改成 hosted child widget 后，GPIO 对话框会在软件启动时自动显示。
- 代码观察：`GpioDialog` 在 `MainWindow` 尚未显示时构造；顶层 `QDialog` 默认不会随 parent 显示，但普通 child widget 如果没有显式隐藏，会在 parent 首次显示时随之可见。
- 代码观察：GPIO 菜单动作原本已通过 `m_gpioDialog->show()` 提供唯一的主动显示入口。

### Fix And Success Criteria

- Wayland hosted 分支在切换为 `Qt::Widget` 后显式调用 `hide()`，保留“启动隐藏、菜单动作显示”的原有对话框语义。
- 不修改 GPIO 菜单动作、拖动事件过滤器或 touch 关闭路径。
- 静态验证 `hide()` 位于 hosted 分支内，因此不影响非 Wayland 顶层 Dialog。

## Unified Dialog Margins Follow-up

### Scope

- 以 `PreferenceDialog` 当前按钮间距 5px 为唯一基准。
- 将 `PreferenceDialog` 与 `GpioDialog` 内容区的上、下、左、右边距统一为 5px。
- 将 GPIO 按钮的水平、垂直间距统一为同一个 5px spacing。
- 不修改按钮尺寸、状态语义、可见性或 Wayland 承载逻辑。

### Evidence And Assumptions

- 观察：`preferencedialog.ui` 当前网格按钮间距为 5px，但内容区边距是左 5px、上 15px、右 5px、下 5px。
- 观察：`GpioDialog` 当前按钮水平、垂直间距均为 5px，但未显式设置内容区四边边距。
- 假设：用户所说的整个 Dialog 上下左右边距，指两个 Dialog 的 `contentWidget()` 根布局内容边距，不包含 `Controls::Dialog` 标题栏自身的框架尺寸。

### Success Criteria

- 两个 Dialog 的内容区四边边距均为 5px。
- 两个 Dialog 的按钮水平、垂直间距均为 5px。
- GPIO 既有按钮布局、动态显隐以及 Wayland 行为保持不变。

### Verification Level

- `static`

### Verification Checklist

- [x] `PreferenceDialog` 根布局四边 margin 与 spacing 均为 5px。
- [x] `GpioDialog` 根布局四边 margin 与 spacing 均为 5px。
- [x] 目标文件通过 `git diff --check`，UI 文件可被 XML 解析。

### Verification Result

- `PreferenceDialog` 根网格的 left/top/right/bottom margin 与 spacing 均为 5px。
- `GpioDialog` 根网格通过 `kDialogSpacing = 5` 同时设置四边 contents margins 与水平/垂直 spacing。
- `Controls::Dialog` 的 content container 外层 margin 为 0，因此上述根布局边距就是标题栏下方内容区到按钮的实际四边留白。
- `preferencedialog.ui` XML 解析通过，目标源码通过 `git diff --check`；按仓库默认规则未编译或运行。
