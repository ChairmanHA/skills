# Wayland 弹窗层级梳理与最小修复方案

## Scope

- 静态梳理当前 CMake 实际纳入 SGStudio 主进程的弹窗/浮层类型。
- 重点分析 Raspberry Pi Wayland 下非模态窗口、文件对话框和自动错误弹窗混合时的层级与输入阻塞问题。
- 给出满足当前故障场景的最小修改方案；本轮不修改产品代码、不编译、不运行。

## Verification Level

- `static`

## Success Criteria

- 打开 `PxOpenFileDlg` / `PxSaveFileDlg` 时，后台设备 open error 或实时 error 对应的 `MessageDialog` 可见、可点击，并位于文件对话框及其输入遮罩之上。
- 关闭错误弹窗后，输入恢复到仍然打开的文件对话框，不留下不可见的模态屏障。
- About / Preference 等非模态辅助窗口不压过文件对话框或错误弹窗。
- `PxSaveFileDlg` 内的覆盖确认 `MessageDialog` 与文件对话框是同一 MainWindow host 下的 sibling，不受文件对话框几何裁剪。
- Win32 和非目标平台保持当前顶层窗口路径。
- 不破坏 Raspberry Pi 系统键盘 `QLineEdit -> QInputMethod/DBus -> squeekboard` 链路。

## Source Boundary

- `src/libs/controls/CMakeLists.txt` 实际包含：
  - `Dialog` / `MessageDialog`
  - `PxOpenFileDlg` / `PxSaveFileDlg`
  - `WaylandDialogMoveTool`
  - `PopupWidget` / `NotificationPopup`
  - `TouchNumKeyboard` / `Keyboard`
- `src/plugins/updater_bak/` 未被 `src/plugins/CMakeLists.txt` 纳入，不作为现行实现。
- `SGStudioMiniBar` helper 和 maintenance 是独立进程/可执行程序，不能用主进程 QWidget sibling stacking 统一排序，应单独看待。

## Current Popup Inventory

### 1. Wayland 下已 hosted、非模态

- `AboutDialog`
  - `runAsShow()`。
  - Wayland 下手工切为 MainWindow child `Qt::Widget`，无 overlay。
- `PreferenceDialog`、`BrightnessSettingDialog`
  - `show()`。
  - `WaylandDialogMoveTool(this, false)`，无 overlay。
- `GpioDialog`、`GpsInfoDialog`
  - `show()`。
  - Wayland 下各自手工切为 hosted child，无 overlay。
- `NotificationPopup`
  - MainWindow 内普通 `QFrame` child，非阻塞，显示时 `raise()`。

### 2. Wayland 下已 hosted、模拟模态

- `PxOpenFileDlg`、`PxSaveFileDlg`
  - `WaylandDialogMoveTool(this)`。
  - MainWindow host 下创建透明输入 overlay，再依次 `overlay->raise()`、`dialog->raise()`。
  - Linux helper 既存在异步 `open()` 路径，也存在同步 `exec()` 路径。
- `UpdateDialog`
  - `WaylandDialogMoveTool(this)`，与其内部 `PxOpenFileDlg` 归一到同一 MainWindow host。
- `TouchNumKeyboard`
  - 自有 host/overlay 模型；属于应用内数字键盘，不等同于系统 OSK。

### 3. 仍是顶层窗口的非模态对话框

- `EthConnectDialog`
  - `runAsShow()`；为保留 Raspberry Pi 系统输入法上下文，当前没有整体 hosted 化。
- `EditDialog`
  - `runAsShow()`，并可继续打开文件选择/数字键盘。
- `ReleaseNotesDialog`
  - 启动完成后 `show()`。
- `DigitalSpectrumDialog`、`MultitoneSpectrumDialog`、`RampPreviewDialog`
  - 明确 `Qt::NonModal`，通过 `show()/raise()/activateWindow()` 展示。

这些窗口目前不在同一个应用内 sibling stacking 体系中，因此“项目所有非模态窗口具有严格固定 Z 层”并不是当前事实。

### 4. 顶层模态/阻塞窗口

- `MessageDialog`
  - 默认 `showMessage() -> Dialog::runAsync() -> QDialog::open()`。
  - warning/error/question 还设置 `Qt::ApplicationModal`。
  - 构造函数已经设置 `Qt::WindowStaysOnTopHint`，但用户实机证据表明该 hint 没有解决 Wayland 层级问题。
- `QProgressDialog`
  - Digital 导出进度，`Qt::ApplicationModal`。
- `PostUpdateDialog`
  - 启动后自动 `open()`，构造中 `setModal(true)`。
- Win32 原生 `QFileDialog`
  - Linux 主业务路径改用 `PxOpenFileDlg/PxSaveFileDlg`。

### 5. 短生命周期 popup / 外部 surface

- `PopupWidget`、Windows 自绘 `Keyboard`、`QMenu/QComboBox` popup：
  - `Qt::Popup`，不是业务 dialog 层。
  - `MessageDialog::showMessage()` 当前会先关闭可见 `Qt::Popup`，避免 popup grab 吞掉消息框点击。
- Raspberry Pi `squeekboard`：
  - compositor/input-method 管理的独立 surface，不属于 SGStudio QWidget 层级。
  - 应用内 Z-order 不能承诺把消息框画到系统 OSK 之上；消息出现时应保证输入法焦点/可见性链路正确收口。

## Observation

1. 文件对话框已经是 MainWindow surface 内的 hosted child，并由应用内透明 overlay 模拟模态。
2. `MessageDialog` 仍是独立顶层 frameless `QDialog`。
3. 用户实机链路是：
   - 文件对话框的应用内 overlay 正在阻塞 MainWindow；
   - 后台设备状态产生 `MessageDialog`；
   - `MessageDialog` 在视觉上落到文件对话框下面；
   - 顶层 modal 仍阻塞输入，最终形成“看不到负责解除阻塞的窗口，整个软件无法响应”。
4. `MessageDialog` 已经使用 `Qt::WindowStaysOnTopHint`，因此继续追加同类 flag 不是新的修复证据。
5. `MainWindowDeviceController` 的 device-open error 和 realtime error 都统一创建 `Controls::MessageDialog(m_mainWindow)`。
6. `PxSaveFileDlg` 的文件覆盖确认也创建 `MessageDialog(this)`，是现成的 nested modal 场景。
7. 仓库已有 `WaylandDialogMoveTool` 可以：
   - 沿 parent 链归一到最外层应用 host；
   - 把 dialog 转成 hosted `Qt::Widget`；
   - 保留初始 hidden 状态；
   - 创建输入 overlay；
   - 按 overlay 后、dialog 前的顺序 raise。

## Inference

- 根因不是“模态级别不够高”，而是同一交互链混用了两套不可直接比较的堆叠域：
  - MainWindow surface 内可控的 QWidget sibling stacking；
  - Wayland compositor 管理的独立 top-level surface stacking。
- 模态只定义谁不能接收输入，不等于客户端可以决定独立 Wayland surface 的绝对 Z-order。
- `WindowStaysOnTopHint` 是向窗口系统提出的 hint；当前代码已经使用且实机失败，不能把它当成可靠协议。
- 对当前自定义 frameless 消息框，最小且可控的做法是让它加入现有 hosted 体系，而不是继续要求 compositor 对混合窗口模型排序。
- “自动弹出”不应直接成为层级类型。更稳定的产品语义是：
  1. 非阻塞辅助窗口；
  2. 用户流程模态窗口（文件选择、更新流程）；
  3. 必须处理的关键消息模态窗口。
  非阻塞自动通知仍不应该压过关键错误框。

## Desired Logical Layers

1. `AuxiliaryNonModal`
   - About、Preference、Brightness、GPIO、GNSS、viewer 等非阻塞辅助窗口。
2. `WorkflowModal`
   - Open/Save file、Update、导出进度等当前用户流程。
3. `CriticalModal`
   - `MessageDialog` 承载的 error/warning/question，尤其设备后台错误。
4. `TransientPopup`
   - Menu/Combo/Popup/tooltip；关键消息出现前关闭，不参与持久层级。

对当前故障，`CriticalModal` 必须与 `WorkflowModal` 处于同一个 MainWindow host，并按以下顺序堆叠：

```text
MainWindow host
├── auxiliary windows
├── workflow overlay
├── workflow dialog
├── critical overlay
└── critical MessageDialog
```

## Recommended Minimal Change

只修改 `src/libs/controls/messagedialog.cpp`：

1. 引入 `waylanddialogmovetool.h`。
2. 在 `MessageDialog` 构造函数中，保留现有非 Wayland `WindowStaysOnTopHint` 行为，然后创建：

```cpp
new WaylandDialogMoveTool(this);
```

调用顺序应让 Wayland helper 最后执行，以便它在目标平台把顶层 flags 收敛回 `Qt::Widget`；非目标平台 helper no-op。

不修改：

- `Controls::Dialog` 基类；
- `PxOpenFileDlg/PxSaveFileDlg`；
- 所有 `MessageDialog` 调用点；
- `EthConnectDialog` 和系统键盘路径；
- Win32 原生文件对话框；
- 业务错误去重与回调。

### Why This Is Sufficient For The Reported Failure

- 文件对话框和 `MessageDialog` 都会成为同一 MainWindow host 的 sibling。
- 文件对话框先显示时，其 `(overlay, dialog)` 在下；消息框后显示时，现有 helper 再创建并 raise `(critical overlay, MessageDialog)`，因此错误框可见且可交互。
- `PxSaveFileDlg -> MessageDialog(this)` 会在消息框构造时归一到 MainWindow，而不是成为文件对话框几何 child，避免裁剪。
- 消息框关闭后只移除自己的 overlay；文件对话框及其 overlay 仍在，可继续交互。
- About/Preference 没有 overlay，后打开的文件/消息 helper 会自然把它们压到下面。
- 当前自动设备错误的两个主入口都已经走 `MessageDialog`，无需散改业务层。

## Why Not Broaden The First Patch

- 不把 `WaylandDialogMoveTool` 下沉到全部 `Controls::Dialog`：
  - 既有树莓派实测已证明，整体 hosted 化会破坏 `EthConnectDialog` 等普通 `QLineEdit` 的系统键盘上下文。
- 不立即给所有非模态 viewer / Edit / ReleaseNotes 做统一层级迁移：
  - 它们不是当前“不可见 modal 阻塞”故障链；
  - 部分窗口依赖 native top-level 或系统输入法；
  - 会把两行级的关键修复扩展为跨插件窗口架构改造。
- 不新增定时 `raise()`、`activateWindow()` 或更多 topmost flags：
  - 仍然依赖 compositor 接受请求；
  - 不能把输入屏障与可见窗口作为一个原子层级对管理。
- 不使用 layer-shell 让消息框成为系统级 top layer：
  - 会覆盖其它应用，超出 SGStudio 应用内模态语义；
  - helper/主进程 surface ownership 和 focus 复杂度显著增加。

## Strict Ordering Follow-up Boundary

上述最小修改保证当前可达交互顺序和已报告故障，但不宣称已经给“所有窗口”建立永久、与显示先后无关的 Z-index。

如果产品要求即使低层窗口被代码在关键错误框之后强制 `show()/raise()`，它也绝不能越层，则需要单独的后续设计：

- 给 `WaylandDialogMoveTool` 增加明确的 `StackingLayer`；
- 每个 MainWindow host 维护可见 controller 列表；
- 每次 show/hide 后按 `(overlay, dialog)` 对统一 restack；
- 把所有 Wayland hosted 非模态窗口逐个接入；
- 对必须保留 native top-level 的系统键盘文本 dialog 明确排除。

这才是严格全局层级管理器，但不是修复当前设备错误弹窗死锁所需的最小改动。

## Static Verification Checklist For Implementation

- [ ] `messagedialog.cpp` 与 `waylanddialogmovetool.cpp` 均已由 `Controls` target 纳入。
- [ ] `MessageDialog` 在目标平台构造后为 MainWindow hosted child，初始仍保持 hidden。
- [ ] 非 Wayland 下 window flags 和现有展示路径不变。
- [ ] `showMessage()` 仍在展示前关闭 `Qt::Popup`。
- [ ] `runAsync()` / `execMessage()` 的 finished、按钮 result 和 `WA_DeleteOnClose` 语义不变。
- [ ] `git diff --check` 通过。

## Raspberry Pi Wayland Runtime Matrix

1. About 已打开 -> Open file：文件对话框在 About 上面。
2. Preference 已打开 -> Open file：文件对话框在 Preference 上面。
3. Open file 已打开 -> 注入 device-open error：错误框在最上面且按钮可点。
4. Open file 已打开 -> 注入 realtime error：同上。
5. 关闭错误框：仍可操作/取消/确认原文件对话框。
6. Save file -> 已存在文件 -> overwrite question：确认框在 Save file 上面；关闭后 Save file 恢复。
7. 重复两轮 3-6：无残留透明 overlay、无整应用输入失效。
8. Save file 文本输入拉起 `squeekboard`，关闭/提交路径不回归。
9. Win32 冒烟：About/Preference、原生 QFileDialog、MessageDialog 层级与输入不回归。

## External Semantics Checked

- Qt `QDialog` 文档：modal 决定输入阻塞；`open()` 为异步 window-modal，Qt 建议优先于嵌套事件循环的 `exec()`。
- Qt `QWindow` 文档：实际 window flags 可能不同于请求值，顶层位置/raise 等能力受窗口系统支持。
- Wayland `xdg-shell`：compositor 管理 top-level/popup surface；有 parent 的 xdg_toplevel 应在祖先之上，但这不是任意应用内全局 Z-index API。

References:

- https://doc.qt.io/qt-6/qdialog.html
- https://doc.qt.io/archives/qt-5.15/qwindow.html
- https://wayland.app/protocols/xdg-shell
