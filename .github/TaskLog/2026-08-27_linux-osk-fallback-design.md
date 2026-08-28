# Linux 系统键盘能力检测与旧键盘回退设计

## 任务目标

实现普通文本输入在 Raspberry Pi Wayland 与 Linux X11/xcb 两类运行环境中的软键盘选择策略，并提供最小、可靠的共享层回退。

## 范围

- 共享入口：`src/libs/controls/Keyboard.h/.cpp`
- 现有调用方：`SaveFileDlg`、`EthConnectDialog`、`InputDialog`、`SCPIDialog`
- 只讨论普通 `QLineEdit` 文本键盘；不改变带单位语义的 `TouchNumKeyboard`
- 本轮验证级别：`static`
- 本轮修改共享键盘实现与对应 KnowledgeBase，不编译、不运行

## 修改前观察事实

- `Keyboard::showPopup()` 当前以 `Q_OS_LINUX` 为条件，所有 Linux 运行环境都会直接调用系统输入面板请求并返回。
- `setSystemKeyboardVisible()` 在 session bus、`sm.puri.OSK0` 服务或接口不可用时仅返回，不会显示旧的 `Keyboard.ui`。
- 现有代码没有依据 `QGuiApplication::platformName()` 区分 Wayland 与 xcb/X11。
- `SetVisible` 当前使用同步 D-Bus 调用，但忽略返回消息；服务已注册不等价于方法调用成功，也不等价于键盘已实际可见。
- 旧的自绘字母键盘仍由同一个 `Controls::Keyboard` 对象持有，可作为本地回退，不需要业务对话框另建路径。

## 产品决策

- Windows 不需要弹出旧的自绘键盘，本轮不为 Windows 选择键盘后端。
- Linux X11/xcb 始终使用旧的自绘键盘。
- Linux Wayland 在 `sm.puri.OSK0.SetVisible(true)` 调用成功时使用系统键盘，调用失败时回退到旧的自绘键盘。
- Raspberry Pi Wayland 当前依赖 `squeekboard` 的 `sm.puri.OSK0`；该接口不是通用 Wayland 标准。
- 当前没有可验证“Wayland 系统键盘缺失”分支的测试环境；源代码中保留待实机验证注释。

## 推荐设计

1. 在 `Controls::Keyboard` 内集中选择后端，不让业务对话框判断平台或 D-Bus。
2. Linux 运行时平台不是 Wayland 时直接显示旧键盘；不要仅以编译期 `Q_OS_LINUX` 选择系统键盘。
3. Wayland 下先完成系统键盘能力检测：session bus 已连接、服务已注册、接口有效。
4. `SetVisible(true)` 必须返回可检查的结果；调用失败时立即显示旧键盘。
5. 本轮不增加“方法返回成功但键盘未显示”的延迟探测；未来具备 Wayland 缺失场景测试环境后再验证是否需要读取 `org.freedesktop.DBus.Properties.Visible`。
6. 保存本次实际选择的后端状态；`hidePopup()` 只关闭已使用的后端，避免无条件 D-Bus 调用和主窗口全屏状态误切换。
7. Wayland 系统键盘路径保留既有的 `showNormal()` 后调用 OSK0 的顺序；若 `SetVisible(true)` 失败则立即恢复 `showFullScreen()` 并显示旧键盘。关闭已启用的系统键盘时恢复全屏。

## 成功条件

- Raspberry Pi 原生 Wayland 且 `sm.puri.OSK0` 可用：继续显示系统键盘，不叠加旧键盘。
- Linux xcb/X11：点击文件名等普通文本框时显示旧键盘。
- Wayland 但 session bus、服务、接口或 `SetVisible(true)` 不可用：显示旧键盘且不崩溃。
- 从一个文本框切换到另一个文本框时不会错误关闭当前键盘；对话框关闭时所用键盘能正确收起。
- Windows 调用共享入口时不显示旧键盘；带单位数值键盘行为保持不变。

## 静态验证清单

- [x] 后端选择只位于 `Controls::Keyboard`。
- [x] session bus、服务注册、接口和 `SetVisible(true)` 的失败分支均能到达旧键盘显示路径。
- [x] 系统键盘调用成功时先隐藏可能存在的旧键盘，两个后端不会由共享入口同时显示。
- [x] `hidePopup()` 根据记录的后端状态成对收口。
- [x] `Controls` 的 Qt DBus 条件链接保持不变。
- [x] Windows 的 `showPopup()` 路径为 no-op。
- [x] `git diff --check` 通过。

## 实现结果

- `QGuiApplication::platformName()` 包含 `wayland` 时才尝试 Qt 输入法与 OSK0；其他 Linux 平台显示旧键盘。
- OSK0 `SetVisible` 的同步 D-Bus 回复现在会被检查；错误回复触发旧键盘回退并恢复主窗口全屏。
- `ActiveBackend` 记录本次使用的 Linux 后端，关闭时不再无条件调用 OSK0。
- Wayland 缺失 OSK0 的分支已添加待未来实机验证注释。
- 按任务约定只完成静态验证，未编译、未运行。

## Ubuntu 18.04 X11 实测跟进

### 现场观察

- Linux xcb/X11 已能显示旧的自绘键盘。
- 点击旧键盘外部后，键盘视觉上消失，但应用不再接收鼠标输入。
- 文件名 `QLineEdit` 仍显示输入焦点，物理键盘也无法输入。

### 当前判断

- 保存对话框通过 `QDialog::open()` 运行在模态链中。
- 旧键盘是无 parent 的 `Qt::Popup` 顶层窗口；Qt 官方语义表明 popup 会隐式取得鼠标 grab，并在隐藏时释放。
- “焦点仍在 LineEdit，但鼠标和物理键盘同时失效”更符合 popup/原生 X11 grab 或 Qt active-popup 栈没有在外点关闭后完整释放，而不是 LineEdit 自身失焦。
- 目前还不能仅凭视觉消失区分 Qt popup 栈残留与 XCB native grab 残留，因此暂不加入强制 release 或改变窗口类型的修复。

### 临时诊断计划

- 在 `Controls::Keyboard` 的 X11 路径记录 Show/Hide/Close、焦点、窗口激活和鼠标按键事件。
- 每个关键事件前后记录 `mouseGrabber`、`keyboardGrabber`、`activePopupWidget`、`activeModalWidget`、`activeWindow` 与 `focusWidget`。
- 日志只在 Linux 非 Wayland 路径输出，不改变 Windows 与 Wayland 行为。
- 用户复测后依据日志决定最小修复：优先修正 popup owner/关闭路径；只有确认 grab 未释放时才做窄范围释放。

诊断日志已加入 `Controls::Keyboard`，并通过 `git diff --check` 静态检查；未在本机执行 Ubuntu/X11 运行验证。

### 实机日志结论与修复

`D:\development\vsg2.0\debug.log` 已确认根因：

1. `20:32:18.608`，外点以超出键盘 `860x322` 范围的局部坐标 `(736, 437)` 到达旧键盘，Qt 开始关闭 `Qt::Popup`。
2. 旧 popup 的 Hide 尚未完成，关闭该 popup 的同一个 mouse press 被回放到下层文件名 `QLineEdit`。
3. `QLineEdit` 的事件过滤器立刻再次调用 `showPopup()`；日志在 Hide 前出现第二组 `showPopup -> Show`。
4. 原关闭流程随后发出 Hide，把刚重新加入 active-popup 栈的同一对象隐藏。此后日志持续显示 `activePopup=Keyboard visible=0`。
5. 后续输入都被隐藏的 active popup 截获并重复 Close/Show/Hide，实机表现为触屏失效、焦点抖动以及键盘区域闪烁。日志共记录 5593 次 Show、5592 次 Hide，排除了单纯 X11 grab 未释放的假设。

最小修复：

- 旧键盘收到窗口范围外的 mouse press 时设置 `Qt::WA_NoMouseReplay`，不把负责关闭 popup 的同一按压回放给下层输入框。
- `showPopup()` 在旧键盘仍可见（包括 Close 已开始但 Hide 未到达）时拒绝重入。
- 旧键盘真正 Hide 后把活动后端清回 `None`。
- 移除临时高频诊断日志，保留针对已证实时序的代码注释。
