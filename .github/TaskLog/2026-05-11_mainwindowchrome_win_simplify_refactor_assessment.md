# MainWindowChromeWin 简化重构评估

## 结论

可以做简化重构，但不建议把方向理解为“把 qwindowkit 的 Win32 处理整套搬进来”。

更合适的方向是：

1. 继续保留当前 `QMainWindow::nativeEvent -> MainWindowChromeWin` 这条接入方式。
2. 借鉴 qwindowkit 的**职责拆分方式**与少量 Win32 处理原则。
3. 不引入 qwindowkit 的 hooked wndproc、全局 native filter、Qt private/QPA hack 这一整层复杂基础设施。

## 为什么不能直接照搬 qwindowkit

qwindowkit 对应用侧 API 看起来简洁，但其内部 Win32 实现本身并不简单：

1. hooked wndproc
2. 全局 native event filter
3. Qt private/QPA 自定义 margins 同步
4. Snap Layout / system menu / caption button / theme material 等一整套窗口框架能力

这套设计适合做一个通用库，不适合当前 SGStudio 只为 `MainWindow` 做一次“简化”。如果把这层引进来，MainWindowChromeWin 表面上可能变薄，但仓库整体复杂度会上升。

## 当前代码里哪些复杂度是必要的

以下复杂度属于 frameless Win32 + Qt 5 的硬成本，基本无法真正删除，只能重组：

1. `WM_NCCALCSIZE`
2. `WM_NCHITTEST`
3. `WM_GETMINMAXINFO`
4. `WM_DPICHANGED`
5. `QWindow::screenChanged` 或等价的跨屏刷新路径
6. 首次 show / window state change 后的 frame + backing store refresh

也就是说，“简化”不能理解成把这些分支去掉，而应理解成：

1. 把几何修正收敛在 Win32 几何层。
2. 把渲染刷新收敛成单一路径。
3. 把消息处理函数拆小，避免一处堆所有补丁。

## 当前代码里可以继续收敛的复杂度

### 1. 把 Win32 metrics/helper 从消息处理里分离

建议抽出内部 helper/struct：

- `WindowFrameMetrics`
  - dpi
  - resizeBorderThickness
  - monitorRect
  - workRect
- `FrameRefreshCoordinator`
  - frame change
  - backing store rebuild
  - redraw

这样 `WM_GETMINMAXINFO`、`WM_NCCALCSIZE`、`screenChanged`、`WM_DPICHANGED` 都只依赖统一 helper，而不是各自重复拼接 API 调用。

### 2. 把 native message switch 改成小 handler

建议从一个大 `switch` 收敛成：

- `handleNcCalcSize(...)`
- `handleNcHitTest(...)`
- `handleGetMinMaxInfo(...)`
- `handleDpiChanged(...)`
- `handleWindowPosChanging(...)`

主 `handleNativeEvent(...)` 只保留分发，不再承载具体逻辑。

### 3. 把“刷新窗口以消除 ghost/白边”的策略统一

当前正确方向已经出现：`resetFramelessWindowFrameAndBackingStore(...)`。

继续重构时，应把以下入口统一指向同一个 refresh coordinator：

1. `WM_DPICHANGED`
2. `screenChanged`
3. `handleWindowStateChange()`
4. `handleFirstShow()`

目标是：以后不再出现“每个问题各写一段单独的 `SetWindowPos/RedrawWindow/resize +/-1`”。

### 4. 明确分离“几何层修正”和“QWidget 布局层修正”

这次最大化问题已经证明：

- Win32 frame / maximized rect 的问题，应在 `WM_NCCALCSIZE` / `WM_GETMINMAXINFO` 解决。
- 不应再回到 `QMainWindow::setContentsMargins()` 去补偿原生窗口几何。

这个边界应在后续重构中固化成约束。

## 可以借鉴 qwindowkit 的点

### 建议借鉴

1. helper 分层：monitor / dpi / frame metrics 不要散在消息分支里。
2. `WM_NCCALCSIZE` 返回 `0` 而不是 `WVR_REDRAW` 的保守策略。
3. 把 maximized 修正放在原生 client rect 层，而不是 Qt layout 层。
4. 使用 `SWP_NOCOPYBITS` 减少旧像素复制引发的残影。

### 不建议借鉴

1. hooked wndproc
2. 全局 native filter
3. Qt private / QPA 自定义 margins 同步
4. Snap Layout / system menu / caption button 全家桶式迁入

这些能力对通用库有价值，但对当前 SGStudio 的“简化重构”目标不划算。

## 建议的重构切片

### Phase 1：无行为变化的结构重构

目标：只拆职责，不改行为。

1. 抽 `WindowFrameMetrics` helper
2. 抽 `FrameRefreshCoordinator` helper
3. 拆小 `handleNativeEvent()` 的消息分支
4. 保持当前所有消息语义不变

### Phase 2：命中测试与几何策略收敛

目标：把 `WM_NCHITTEST` 和 maximized 相关逻辑统一成可读规则。

1. 标题栏 draggable 区域判定单独成函数
2. resize border 判定单独成函数
3. maximized / fullscreen / normal 三种状态下的命中测试规则表述清楚

### Phase 3：只在确有收益时再评估更深层改造

只有在后续明确需要以下能力时，才考虑继续向 qwindowkit 靠拢：

1. Windows 11 Snap Layout
2. 更完整的 system caption button 语义
3. 深度系统边框保留策略

否则不建议继续扩大基础设施层。

## 最终建议

可以做简化重构，但建议目标明确为：

1. **结构简化**
2. **职责边界清晰**
3. **统一刷新路径**

而不是：

1. 对齐 qwindowkit 的全部窗口系统能力
2. 引入更深的 Win32 hook/QPA 私有依赖

一句话总结：

**可以借鉴 qwindowkit 的处理原则，但不应把它的库级复杂度搬到 SGStudio 里。当前最合理的重构，是在现有 `MainWindowChromeWin` 框架内做“本地收敛”，而不是做一次底层替换。**