# Wayland 树莓派下接入系统键盘的实现说明

本文档记录 SGStudio 在 Raspberry Pi Wayland 桌面环境下，如何把系统软键盘整合到普通 `QLineEdit` 输入流程中。本文关注的是“文本输入框如何唤起系统键盘并在对话框关闭时收起”，不是历史数字单位键盘体系本身。

如果需要了解仓库中原有 `TouchNumKeyboard / BaseUnitAdapter / StepController` 的设计边界，请先阅读 `soft_keyboard_architecture.md`。如果问题涉及 Wayland 下无边框 overlay / popup 的窗口协议，请另读 `WAYLAND_FRAMELESS_OVERLAY_PATTERN.md`。

## 1. 目标场景

当前接入方案服务于以下场景：

- Raspberry Pi 桌面环境。
- Wayland 会话，当前实测环境为 `labwc + wf-panel-pi + squeekboard`。
- SGStudio 本体运行在 Qt Wayland 平台插件上，而不是 XWayland/xcb。
- 输入控件是普通 `QLineEdit`，例如：
  - `EthConnectDialog` 的 IP / Port。
  - 文件保存对话框中的文件名输入。

不在本文范围内的场景：

- Windows 外接键盘输入。
- 频率/时间/功率等“带单位语义”的数值编辑。
- 基于自定义 overlay 的历史数字键盘 UI 设计。

## 2. 现象与根因

### 2.1 初始现象

第一版实现已经做到以下几点：

- 给目标 `QLineEdit` 设置 `WA_InputMethodEnabled`。
- 在 `FocusIn / MouseButtonPress / TouchBegin` 时发送 `QEvent::RequestSoftwareInputPanel`。
- 调用 `QGuiApplication::inputMethod()->show()`。

代码层面看起来已经“请求了系统键盘”，但树莓派实机测试中：

- `requestSoftwareInputPanel()` 确实执行。
- 输入框焦点也正常落在 IP / Port 上。
- 屏幕上仍然看不到系统键盘。

### 2.2 排查后确认的事实

远程运行态诊断确认了以下几点：

1. SGStudio 不是跑在 XWayland/xcb 上，而是原生 Qt Wayland。
2. `squeekboard` 进程已启动，并且与 SGStudio 位于同一个 Wayland 会话。
3. 手动调用 DBus 服务 `sm.puri.OSK0.SetVisible(true)` 后，系统键盘可以立刻显示。
4. 因此问题不是“键盘被主窗口挡住”，而是“Qt 输入法请求没有把当前桌面会话里的系统键盘切到可见态”。

### 2.3 根因总结

在当前 Raspberry Pi Wayland 桌面组合下：

- `QInputMethod::show()` 是必要动作，但并不充分。
- 仅依赖 Qt 输入法请求，`squeekboard` 的 DBus 可见状态仍可能保持为 `false`。
- 想要稳定唤起系统键盘，需要在 Qt 请求链之外，再补一条面向当前桌面环境的显式可见性回退。

## 3. 最终方案概览

最终实现没有把 DBus 逻辑散落到每个业务对话框里，而是收敛到共享抽象 `Controls::Keyboard`：

- Linux xcb/X11 下，`Keyboard::showPopup(QLineEdit*)` 显示旧的自绘字母键盘面板。
- Linux Wayland 下，它优先走“系统输入面板请求链”。
- 在 Qt 输入法请求之后，再调用树莓派桌面当前可用的 DBus 服务 `sm.puri.OSK0.SetVisible(true)`。
- session bus、OSK0 服务、接口或 `SetVisible(true)` 调用不可用时，立即回退到旧的自绘字母键盘。
- 关闭或失焦时，再通过同一条共享路径执行 `SetVisible(false)`。
- Windows 下该共享入口不弹出旧键盘。

这样做的直接收益是：

- `EthConnectDialog`、`PxSaveFileDlg`、`InputDialog` 这类仍复用 `Controls::Keyboard` 的普通文本输入，不需要各自重复实现 DBus 细节。
- 在没有系统键盘或没有该 DBus 服务的 Wayland 环境下，会安全回退到旧键盘，不会崩溃。

## 4. 关键代码位置

当前实现的关键落点如下：

- 共享键盘入口：`src/libs/controls/Keyboard.h/.cpp`
- 文件保存对话框输入触发：`src/libs/controls/SaveFileDlg.cpp`
- ETH 对话框输入触发与关闭收口：`src/plugins/core/ethconnectdialog.h/.cpp`
- Qt DBus 构建依赖：
  - 仓库根 `CMakeLists.txt`
  - `src/libs/controls/CMakeLists.txt`

## 5. 共享键盘路径如何工作

### 5.1 show 路径

Linux Wayland 下，`Controls::Keyboard::showPopup(QLineEdit*)` 的系统键盘路径职责是：

1. 校验目标输入框仍然存在、可见、可用。
2. 确保目标控件开启 `WA_InputMethodEnabled`。
3. 如果尚未获得焦点，则先把焦点切到该输入框。
4. 延迟到下一事件轮次，等待焦点稳定。
5. 发送 `QEvent::RequestSoftwareInputPanel`。
6. 调用 `QGuiApplication::inputMethod()->show()`。
7. 调用 DBus：`sm.puri.OSK0.SetVisible(true)`。
8. 若 DBus 调用失败，隐藏 Qt 输入法并显示旧的自绘字母键盘。

第 7 步是树莓派 Wayland 环境下的显式系统接口，第 8 步是该接口失败后的应用内回退。Linux xcb/X11 不进入这条路径，直接显示旧键盘。

### 5.1.1 X11 旧键盘的外点关闭契约

旧键盘使用 `Qt::Popup`。Qt 默认会关闭被外点命中的 popup，并把该 mouse press 回放给下层控件。文件名 `QLineEdit` 本身又会在 press/touch/focus 事件中调用 `showPopup()`，因此必须避免在旧 popup 的 Close 已开始、Hide 尚未完成时用同一个按压重开同一对象。

当前约束是：

- 窗口范围外的按压关闭旧键盘前设置 `Qt::WA_NoMouseReplay`，消费这次关闭手势；用户需要下一次独立按压才能操作下层控件。
- 旧键盘仍可见时，重复的 `showPopup()` 请求直接返回。
- Hide 完成后清理旧键盘后端状态。

否则会形成 `Close -> replay to QLineEdit -> Show -> old Hide` 的重入链，使 `QApplication::activePopupWidget()` 指向已经隐藏的 Keyboard；后续鼠标、触摸与焦点事件持续被隐藏 popup 截获，表现为输入失效与键盘区域闪烁。

### 5.2 hide 路径

Linux 下，`Controls::Keyboard::hidePopup()` 会根据本次实际选择的后端成对收口：

1. 清理当前 pending 输入目标。
2. 系统键盘后端：调用 `QGuiApplication::inputMethod()->hide()` 和 DBus `sm.puri.OSK0.SetVisible(false)`，再恢复主窗口全屏。
3. 旧键盘后端：只隐藏应用内自绘键盘，不调用 OSK0，也不切换主窗口全屏状态。

这样可以保证：

- 对话框主动关闭时，系统键盘跟着收起。
- 用户把焦点切走时，不会把键盘一直留在桌面上。

### 5.3 自动收口

共享 `Keyboard` 还会监听已接入输入框的：

- `FocusOut`
- `Hide`
- `Close`

在下一事件轮次检查当前 `QApplication::focusWidget()`：

- 如果新焦点仍然是启用了 `WA_InputMethodEnabled` 的 `QLineEdit`，说明用户只是从一个文本框切换到另一个文本框，不应收起键盘。
- 如果已经没有合适的输入焦点，则执行 `hidePopup()`。

这保证了“字段内切换不断键盘、对话框退出时自动收口”的体验。

## 6. 对话框侧如何接入

### 6.1 ETH Connect 对话框

`EthConnectDialog` 的做法是：

1. 给 `m_ipEdit / m_portEdit` 开启 `WA_InputMethodEnabled`。
2. 设置更合适的输入法 hint：
   - IP：`ImhPreferNumbers + ImhNoPredictiveText`
   - Port：`ImhDigitsOnly + ImhNoPredictiveText`
3. 安装 `eventFilter`。
4. 在 `FocusIn / MouseButtonPress / TouchBegin` 时调用共享键盘入口。
5. 在 `hideEvent()` 中调用 `Controls::Keyboard::instance()->hidePopup()`。
6. 在“连接”动作提交前，也先收起系统键盘。

这里的核心原则是：

- 业务对话框只负责“什么时候要键盘、什么时候要收起”。
- 真正的 Wayland/DBus 细节只保留在共享键盘路径里。

### 6.2 文件保存对话框

`PxSaveFileDlg` 中的文件名输入同样使用：

- `FocusIn`
- `MouseButtonPress`
- `TouchBegin`

来调用 `Keyboard::showPopup(ui->name)`。

Wayland + OSK0 可调用时，文件保存流程复用系统键盘；Linux xcb/X11 或 Wayland 下 OSK0 调用失败时，文件保存流程回退到旧的自绘字母键盘。

## 7. DBus 回退为何放在 `Controls::Keyboard`

把 `sm.puri.OSK0.SetVisible(true/false)` 放到 `Controls::Keyboard`，而不是直接写进 `EthConnectDialog`，有几个原因：

1. 这是当前仓库里“普通文本输入调用键盘”的共享抽象。
2. `SaveFileDlg`、`InputDialog` 等历史路径已经通过 `Keyboard::showPopup()` 进入。
3. 若把 DBus 回退写死在单个业务对话框里，后续其它文本输入还会重复踩坑。
4. 在没有 squeekboard 的环境里，共享实现可以统一做“服务不存在就安全跳过”的降级处理。

## 8. 构建与依赖要求

为了在 Linux 上调用 `sm.puri.OSK0`，需要引入 Qt DBus：

- 仓库根 `CMakeLists.txt`：UNIX 下 `find_package(Qt... DBus)`
- `src/libs/controls/CMakeLists.txt`：UNIX 下给 `Controls` 链接 `Qt::DBus`

注意：

- 这个依赖是 Linux/UNIX 条件下启用的。
- Win32 不需要引入 Qt DBus。

## 9. 运行时诊断手册

当“代码看起来在请求系统键盘，但屏幕上没有键盘”时，建议按下面顺序排查。

### 9.1 先确认 SGStudio 运行在哪个显示协议上

检查：

- `DISPLAY`
- `WAYLAND_DISPLAY`
- `XDG_SESSION_TYPE`
- `XDG_CURRENT_DESKTOP`

再看进程已加载的库或打开的 fd，确认是否真的加载了 Qt Wayland 平台插件，例如：

- `libQt5WaylandClient`
- `libqwayland-generic`

如果程序其实跑在 xcb/XWayland 上，调试方向会完全不同。

### 9.2 检查 squeekboard 是否在当前会话中运行

可以检查进程和 DBus 名称，例如：

- `pgrep -af squeekboard`
- `busctl --user list | grep squeek`

当前树莓派实测环境下，关键 DBus 名称包括：

- `sm.puri.OSK0`
- `sm.puri.SqueekDebug`

### 9.3 读取当前可见状态

使用 DBus Properties 读取：

```bash
gdbus call --session \
  --dest sm.puri.OSK0 \
  --object-path /sm/puri/OSK0 \
  --method org.freedesktop.DBus.Properties.Get \
  sm.puri.OSK0 Visible
```

如果输入框已有焦点，但这里仍然是 `false`，说明键盘没有真正进入显示态。

### 9.4 手动强制显示验证

可以直接验证系统键盘本身是否正常：

```bash
gdbus call --session \
  --dest sm.puri.OSK0 \
  --object-path /sm/puri/OSK0 \
  --method sm.puri.OSK0.SetVisible true
```

如果这一步可以把键盘弹出来，说明：

- 问题不是窗口遮挡。
- 问题在于业务代码没有把系统键盘状态切到 visible。

### 9.5 屏幕截图确认是否遮挡

在 Wayland 会话中可用 `grim` 截图：

```bash
grim /tmp/sgstudio-osk.png
```

如果截图中键盘正常显示，说明层级是对的；如果手动 `SetVisible(true)` 也仍然看不到，再去怀疑 layer-shell / compositor 问题。

## 10. 未来新增普通文本输入框时的接入规则

如果以后还要在 Wayland 树莓派上新增“普通文本输入框 + 系统键盘”场景，建议直接照以下步骤接入：

1. 目标控件使用普通 `QLineEdit`。
2. Linux 下设置 `WA_InputMethodEnabled`。
3. 给该输入框安装一个局部 `eventFilter`。
4. 在 `FocusIn / MouseButtonPress / TouchBegin` 时调用 `Controls::Keyboard::instance()->showPopup(lineEdit)`。
5. 在对话框 `hideEvent()` 或提交动作前调用 `Controls::Keyboard::instance()->hidePopup()`。
6. 不要在业务对话框里重复实现 DBus `sm.puri.OSK0` 细节。

## 11. 边界与限制

### 11.1 这是树莓派桌面当前可用的具体回退，不是通用 Wayland 标准接口

`sm.puri.OSK0` 来自当前系统键盘实现 `squeekboard`。这不是所有 Wayland 环境都统一支持的接口。

因此：

- 在这台树莓派的桌面镜像上，这条路径是可靠的。
- 换成其它输入法/桌面组合时，可能要替换成别的系统接口。

### 11.2 无系统键盘环境必须允许安全降级

例如没有安装 squeekboard 的 Wayland Linux 设备，`Keyboard` 必须允许 Qt 输入法或 DBus 调用失败，并回退到旧键盘。X11/xcb 不尝试调用 OSK0，直接使用旧键盘。

当前没有“Wayland 下 OSK0 缺失/调用失败”的实机测试环境。代码已按 DBus 返回结果实现回退并保留 TODO 注释；未来具备环境后，需要验证系统键盘与旧键盘不会同时显示、主窗口全屏状态正确恢复，以及字段切换和对话框关闭时键盘能正常收口。

### 11.3 带单位的数值输入不要直接改成这套普通文本键盘逻辑

频率、时间、功率等输入仍然有它们自己的单位语义与适配器协议，这一套系统键盘文档不覆盖那些业务规则。

## 12. 总结

在 Raspberry Pi Wayland 桌面环境下，把系统键盘真正接进 Qt Widgets 普通文本输入流程，需要同时满足两层条件：

1. Qt 侧：输入框获得焦点，发送 `RequestSoftwareInputPanel`，调用 `QInputMethod::show()`。
2. 桌面侧：如果当前 compositor / keyboard 组合不会因为 Qt 请求而自动切 visible，就必须补显式系统接口回退。

在当前仓库里，这个“系统接口回退”已经收敛为：

- Linux 共享路径：`Controls::Keyboard`
- 树莓派具体回退：`sm.puri.OSK0.SetVisible(true/false)`

今后新增 Wayland 文本输入框时，优先复用这条共享路径，而不是在每个业务对话框里重新发明一套系统键盘控制逻辑。

## 13. x86_64 physical-input policy

As of 2026-08-28, x86_64 is treated as a physical keyboard-and-mouse product. `Controls::Keyboard::showPopup()` is intentionally inert on x86_64, independent of whether Qt uses xcb/X11 or Wayland. IP, port, file-name, and generic text fields must retain ordinary `QLineEdit` mouse and physical-keyboard behavior and must not depend on the legacy application keyboard.

This policy overrides older statements in this document that described the X11 legacy-keyboard fallback as applying to every Linux architecture. The Wayland system-keyboard request and legacy fallback remain an embedded non-x86_64 policy, currently used by Linux aarch64. SCPI ports are a separate aarch64 numeric-input case and use `TouchNumKeyboard` on both xcb/X11 and Wayland.
