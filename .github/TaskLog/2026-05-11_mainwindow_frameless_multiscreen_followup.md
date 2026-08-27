# MainWindow Frameless 多屏后续修复

## 问题

当前 MainWindow 模式还有两个 Windows frameless 相关问题：

1. 最大化后顶部存在未充满区域；该区域仍然像标题拖拽区，按住拖动会立刻 restore。
2. 在两块 DPI 相同的屏幕之间拖动时，仍会出现 statusbar / DeviceInfoWidget 重影；手动 resize 一下即可恢复。

## 本地根因假设

### 1. 最大化顶部空带

当前实现把最大化修正主要放在 `WM_GETMINMAXINFO` 分支里，通过 `QMainWindow::setContentsMargins()` 注入 `calcMaximizedContentsMargins()` 的结果。

这会把 Win32 frame 修正错误地下沉到 QWidget 布局层，容易导致主窗口顶部出现一条“仍属于窗口但不属于实际内容”的空白区域。用户看到的未充满顶部，并不一定是窗口真的没到 `y=0`，更可能是窗口到了顶，但 Qt 内容被 top margin 顶下去了。

### 2. 同 DPI 跨屏重影

当前重影修复只挂在 `WM_DPICHANGED`。当两块屏的缩放比相同，跨屏拖动不会触发这个消息，于是现有的 `SetWindowPos(...FRAMECHANGED) + resize +/-1 + RedrawWindow` 链路完全不执行；用户手动 resize 之所以能恢复，是因为它恰好触发了 backing store 重建。

## 参考 qwindowkit 后的收敛策略

1. 借鉴 qwindowkit 的方向，把最大化 client rect 修正前移到 `WM_NCCALCSIZE` / `WM_GETMINMAXINFO`，在 Win32 几何层处理，而不是继续依赖 `QMainWindow::setContentsMargins()`。
2. 不再从 `AdjustWindowRectExForDpi()` 推导一个大的 top margin；最大化时只按 resize border thickness 修正 client rect。
3. 借鉴 qwindowkit 的经验，不再对 `WM_NCCALCSIZE` 返回 `WVR_REDRAW`，改为返回 `0`，避免 child/widget 错位与残影类问题。
4. 为同 DPI 跨屏新增 `QWindow::screenChanged` 刷新路径，并在 frame change 时加 `SWP_NOCOPYBITS`，减少旧客户端像素被复制导致的 ghost。

## 验证口径

1. 目标文件静态诊断无新增错误。
2. `Core` Debug 目标可通过编译。
3. 代码层确认：
   - 最大化不再通过 `setContentsMargins()` 注入顶部空带。
   - 同 DPI 跨屏切换能走到统一的 frame/backing-store refresh 逻辑。