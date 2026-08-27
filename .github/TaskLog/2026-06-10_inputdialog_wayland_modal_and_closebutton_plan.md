# 2026-06-10 InputDialog Wayland Modal And Close Button Plan

## Goal

- 修复 InputDialog 在树莓派上标题栏关闭按钮宽度异常的问题，使其与主窗口标题栏关闭按钮保持一致。
- 修复 InputDialog 在树莓派上点击对话框外部后直接消失，随后整应用鼠标点击失效的问题。

## Local Hypothesis

- 当前代码里没有任何显式 `hide()` / `close()` 会在外点时主动收掉 `InputDialog`；真正有问题的是 `DirWidget::createNewFolder()` 把它挂在了 `DirWidget` 这个内部子控件上，而不是宿主窗口 `PxSaveFileDlg`。
- 仅仅把 parent 改成 `parent->window()` 还不够；`InputDialog` 构造里又重复整包 `setWindowFlags(...)`，有机会把基类已经建立好的 dialog owner / transient 关系重置掉。这样外点时更像是窗口被压到宿主后面，而不是正常关闭。
- 要同时修正两层绑定：Qt 对象树上优先绑定最近的 `Dialog/QDialog` 祖先，native window 层再显式补一次 `QWindow::setTransientParent(...)`，确保 `InputDialog` 真正附着在 `PxSaveFileDlg` 之上。
- 关闭按钮宽度问题已经由当前代码里显式固定 `36px` 解决，这次只需要继续修正 InputDialog 的宿主窗口和模态边界。

## Plan

- 让 `InputDialog` 在构造时优先绑定最近的 `Dialog/QDialog` 祖先；找不到时再退回 `parent->window()` / main window。
- 删除 `InputDialog` 构造里重复的整包 `setWindowFlags(...)`，避免把 owner/transient 关系重置掉。
- 在 `runAsync()` 显示后显式把 native transient parent 绑定到宿主 window，并继续走独立的 application-modal 路径，防止外点时被宿主压到后面。
- 保持关闭按钮尺寸、标题栏、软键盘接入逻辑不变。

## Validation

- 先对 `src/libs/controls/dialog.cpp` 和 `src/libs/controls/DirWidget.cpp` 做静态错误检查。
- 再用现有 `build/cmake-win-debug` 对受影响链路做一次最小增量编译验证。