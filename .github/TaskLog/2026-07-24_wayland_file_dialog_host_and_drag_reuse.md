# Wayland 文件对话框宿主修复与拖动复用

## Scope

- 修复 `UpdateDialog` 打开本地更新文件时，`PxOpenFileDlg` 以局部模态对话框作为 hosted parent，导致拖动范围错误或显示被裁剪的问题。
- 修复各 Modulation Panel 保存 IQ 文件时，`PxSaveFileDlg` 以业务面板作为 hosted parent，导致拖动失败或显示不全的问题。
- 让两个文件对话框的标题栏拖动复用 `LockWidget` 已使用的 `Controls::WidgetMouseMoveTool`。
- 保持 Win32 和非 hosted 平台现有顶层对话框行为不变。

## Observation

- `PxOpenFileDlg` 和 `PxSaveFileDlg` 均构造 `WaylandDialogMoveTool`；该工具在 Wayland（以及当前 aarch64 fallback）下把原本的 `Qt::Dialog` 改为 parent 内的 `Qt::Widget`。
- hosted child 的 parent 不只是 QObject owner，也是可见裁剪范围、坐标系和 `WidgetMouseMoveTool` 的边界。
- `UpdateDialog::loadLocalFile()` 当前创建 `PxOpenFileDlg(this)`；`UpdateDialog` 固定为 800 x 600。
- AM/FM/PM/Pulse/AWGN/Ramp/Multitone/Digital/DSSS/OFDM Panel 的保存入口均把 `this` 传给 `getSaveFilePath(...)`。
- `WaylandDialogMoveTool` 当前依赖 `Controls::TitleBar::mouseMoveEvent()` 完成拖动；`LockWidget` 已使用支持 Mouse/Touch 和 parent 边界限制的 `WidgetMouseMoveTool`。
- 2026-07-01 的 updater 修复把文件对话框 parent 到 `UpdateDialog`，发生在 hosted child 方案引入之前；其 callback 延后一轮事件循环和短生命周期对象处理仍然有效，但 parent 假设已不再适合当前实现。

## Inference

- hosted 文件对话框应统一归一到最外层应用 widget host，不能让任意调用控件直接成为几何宿主。
- `UpdateDialog` 若仍是独立顶层模态窗口，而内层文件对话框改为主窗口 child，会产生层级和模态边界冲突；因此 Wayland 下应让 `UpdateDialog` 也进入同一 hosted 模式，使两者成为同一主窗口下可排序的 sibling。
- Wayland 模拟模态和通用拖动是两个独立职责，不应合并成单个大类；`WaylandDialogMoveTool` 应组合 `WidgetMouseMoveTool`。

## Design

1. `WaylandDialogMoveTool::applyWidgetMode()` 沿 `parentWidget()` 链找到最外层 widget，并把 hosted dialog reparent 到该 host。
2. `WaylandDialogMoveTool` 为自定义标题栏和标题 `QLabel` 安装 `WidgetMouseMoveTool`：
   - 标题栏处理鼠标/合成鼠标拖动；
   - 只有标题 `QLabel` 显式接收原生 Touch，避免标题栏祖先抢占关闭按钮触摸。
3. `UpdateDialog` 构造时也创建 `WaylandDialogMoveTool`。该工具在非目标平台 no-op，因此不改变 Win32 路径。
4. 不修改各 Modulation Panel 的业务保存逻辑；通用 hosted parent 归一化一次覆盖所有现有及后续调用。
5. 保留 `UpdateDialog` 当前独立创建/销毁 `PxOpenFileDlg`、禁用 Browse 按钮以及 `QTimer::singleShot(0, ...)` 延迟加载文件的逻辑。

## Success Criteria

- Wayland 下，`UpdateDialog`、其打开的 `PxOpenFileDlg` 以及各 Panel 打开的 `PxSaveFileDlg` 都以最外层应用窗口作为 hosted geometry parent。
- 内层文件对话框可在应用窗口可见范围内拖动，不再受 800 x 600 的 `UpdateDialog` 或窄 Modulation Panel 裁剪。
- Open/Save 文件对话框与 `LockWidget` 共用 `WidgetMouseMoveTool` 的 Mouse/Touch 位移与边界限制。
- 文件对话框关闭按钮不被原生 Touch drag handle 覆盖。
- Win32 和不满足 hosted 条件的平台保持原有 `Qt::Dialog` 行为。

## Verification Level

- `static`

## Verification Checklist

- [x] 修改文件均由当前 CMake 目标包含。
- [x] `WaylandDialogMoveTool` 的 host 解析覆盖 UpdateDialog 与 Panel 两类 parent 链。
- [x] 文件对话框标题栏接入 `WidgetMouseMoveTool`，且关闭按钮未安装移动过滤器。
- [x] `UpdateDialog` callback 延后与对象生命周期逻辑保持不变。
- [x] `git diff --check` 通过。
- [x] 按仓库默认规则不执行编译或运行；Wayland 实机拖动、遮罩、关闭按钮和文件加载需后续回归。

## Verification Result

- `src/libs/controls/CMakeLists.txt` 包含 `widgetmousemovetool.cpp/.h` 与 `waylanddialogmovetool.cpp/.h`；`src/plugins/updater/CMakeLists.txt` 包含 `updatedialog.cpp/.h/.ui`。
- `resolveHostedDialogParent()` 沿 `parentWidget()` 链归一到最外层 widget：Panel 调用链与 `UpdateDialog -> PxOpenFileDlg` 调用链均落到同一个应用 host。
- 标题栏和其直接 `QLabel` 子控件使用同一个 `WidgetMouseMoveTool`；关闭 `QPushButton` 未安装该 event filter，标题栏本身也未新增 `WA_AcceptTouchEvents`。
- `UpdateDialog::loadLocalFile()` 仍保留短生命周期 `PxOpenFileDlg`、`QPointer` 保护、`deleteLater()` 与 `QTimer::singleShot(0, ...)` 延迟加载。
- `git diff --check -- src/libs/controls/waylanddialogmovetool.cpp src/plugins/updater/updatedialog.cpp .github/TaskLog/2026-07-24_wayland_file_dialog_host_and_drag_reuse.md` 通过。
- 未执行编译或运行验证；需在树莓派 Wayland 实机覆盖 Update Browse、各调制 Save、鼠标/触摸拖动、关闭按钮和取消/确认后的遮罩恢复。

## 2026-07-27 Updater 实机回归修正

### Field Evidence

- 树莓派 Wayland 实机确认：`UpdateDialog` 打开的 `PxOpenFileDlg` 仍被 `UpdateDialog` 边界截断，无法完整显示。
- 这否定了“只要 `WaylandDialogMoveTool` 在派生类构造体内沿 `parentWidget()` 向上 reparent，就足以稳定解除嵌套宿主”的假设。

### Updated Observation

- `UpdateDialog::loadLocalFile()` 仍从构造源头执行 `new Controls::PxOpenFileDlg(this)`。
- 因此 `Controls::Dialog/QDialog` 基类首先以 `UpdateDialog` 建立 parent；`WaylandDialogMoveTool` 只能在随后进入 `PxOpenFileDlg` 构造体时再做晚期 reparent。
- Updater 已能直接通过 `Core::ICore::dialogParent()` 取得主窗口；无需在这条已知调用链上依赖隐式祖先推导。

### Revised Design

- Linux 自定义打开对话框从构造开始就使用 `Core::ICore::dialogParent()` 作为 parent；若主窗口暂不可用，再回退到 `UpdateDialog::parentWidget()`。
- 保留 `UpdateDialog` 和 `PxOpenFileDlg` 的 `WaylandDialogMoveTool`，使二者在目标平台都是同一个主窗口 host 下的 sibling，并由内层遮罩与 `raise()` 保证显示顺序。
- 保留短生命周期文件对话框、`QPointer`、`deleteLater()` 和延迟一轮事件循环加载文件的防重入路径。

### Revised Success Criteria

- `UpdateDialog::loadLocalFile()` 不再把 `this` 作为 `PxOpenFileDlg` 的构造 parent。
- 文件对话框初始 parent 与 `UpdateDialog` 的 hosted parent 都是主窗口，而不是在构造后再从内层 dialog reparent。
- Win32 原生 `QFileDialog` 路径和文件加载 callback 时序保持不变。

### Follow-up Verification

- Linux 分支现在先取得 `Core::ICore::dialogParent()`，仅在其为空时回退到 `UpdateDialog::parentWidget()`，再构造 `PxOpenFileDlg`。
- `PxOpenFileDlg(this)` 已从 Updater 调用链移除。
- Updater 目标已链接 `Core`，新增的 `core/icore.h` 使用位于现有目标依赖边界内。
- `QPointer`、`deleteLater()`、`QTimer::singleShot(0, ...)` 和 Win32 分支均未修改。
- `git diff --check -- src/plugins/updater/updatedialog.cpp` 通过；按仓库默认规则未执行编译或运行。
