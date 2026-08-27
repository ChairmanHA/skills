# 无边框主窗口多屏不同 DPI/缩放下右/下白边问题总结（Windows）

## 背景与现象
- 现象：Windows 下 Qt 无边框主窗口（Frameless）在 **多显示器且缩放/DPI 不一致** 的情况下，窗口右侧与底部出现“白边/空白区”。
- 触发特征：
  - 单屏通常不出现。
  - “在笔记本小屏启动程序，但窗口显示在外接大屏上”更容易出现。
  - **并非一定最大化**：常见是恢复上次窗口大小（例如 1280×800）。
  - 白边出现后，**轻微拖动/移动窗口一下**（哪怕很小）白边立即消失。

> 关键线索：”动一下就消失“强烈暗示问题与 **首次显示时机**、**非客户区（NCA）重新计算**、或 **DWM/Qt 绘制刷新** 相关，而不只是单纯的几何计算错误。

## 影响范围
- OS：Windows
- 框架：Qt 5（qmake 项目）
- 场景：多显示器 + 不同缩放比例（例如 100%/150%）
- 窗口类型：无边框（nativeEvent 处理 NCA）

## 相关代码位置
- 主逻辑文件：`src/plugins/core/mainwindow.cpp`
- 头文件声明：`src/plugins/core/mainwindow.h`

## 已尝试的修复方向与实现（按时间顺序）

### 1) 修正 WM_GETMINMAXINFO 的 margins 计算错误
- 发现：旧 `WM_GETMINMAXINFO` 分支里 margins 计算存在明显 bug（top/bottom 使用混乱），且 DPI 相关计算方式不稳。
- 处理：修正 margins 逻辑，目标是让最大化时 client area 与可用工作区对齐，避免多余空白边。
- 结果：用户反馈在目标场景“无明显改善”，问题依旧。

### 2) 引入按窗口 DPI 的 frame 计算（优先 DPI-aware API）
- 新增 helper（Windows 下）：
  - `windowDpi(HWND)`：优先 `GetDpiForWindow`，回退 `GetDeviceCaps(LOGPIXELSX)`。
  - `adjustWindowRectForDpi(...)`：优先 `AdjustWindowRectExForDpi`，回退 `AdjustWindowRectEx`。
  - `calcMaximizedContentsMargins(...)`：用于最大化时计算 Qt `setContentsMargins` 所需值。
- 增加 `WM_DPICHANGED` 处理：DPI 变化时（尤其跨屏）在最大化状态重算 margins。
- 结果：仍未解决“非最大化首次 show 右/下白边，移动后消失”的核心现象。

### 3) 强制刷新非客户区：SWP_FRAMECHANGED
- 在窗口 style 调整后调用 `SetWindowPos(..., SWP_FRAMECHANGED)`，强制让 Windows 重新计算非客户区。
- 目的：避免首次显示时 NCA 状态未刷新导致的边缘异常。
- 结果：单独使用仍不足以覆盖问题。

### 4) 加入 showEvent 延迟一帧刷新（时机修复）
- 新增 `MainWindow::showEvent(QShowEvent*)`（Windows only）
- 通过 `QTimer::singleShot(0, ...)` 延迟到下一事件循环/下一帧执行：
  - `SetWindowPos(... SWP_FRAMECHANGED)`
  - `RedrawWindow(... RDW_FRAME | RDW_INVALIDATE | RDW_ALLCHILDREN)`
- 同时处理：
  - 最大化：重算并设置 margins。
  - 非最大化：清零 margins。
- 目的：针对“首次 show 时 frame/绘制未完成，之后移动触发系统重算”的现象，尝试在 show 后主动补齐一次“系统级刷新”。
- 结果：用户当前反馈“似乎还是没解决”。

### 5) 在非最大化 show 后模拟用户操作：1px move 抖动
- 逻辑：在 `showEvent` 的非最大化分支里执行：
  - `oldPos = pos()`
  - `move(oldPos + QPoint(1,1))`
  - `move(oldPos)`
- 设计动机：
  - 用户手动“挪一下窗口”会触发 Windows 的一系列位置/大小变更路径（如 `WM_WINDOWPOSCHANGING/CHANGED`、可能的 NCA re-eval、DWM 重新合成），从而让白边消失。
  - 该策略等价于程序自动触发一次“最小移动”，希望复现用户动作带来的系统重算副作用。
- 风险/副作用：
  - 可能存在轻微视觉抖动（取决于机器/合成器/刷新率）。
  - 若窗口初始位置贴边或受约束，可能被系统修正（不过 1px 通常影响很小）。
- 结果：目前用户反馈仍未完全解决（需结合具体测试场景继续定位）。

## 期间遇到的构建问题
- 链接错误：`GetDeviceCaps` unresolved external（LNK2019）。
- 处理：补充链接 `gdi32.lib`（当前已解决该编译问题）。

## 可能的关键环境因素（待确认/可能影响根因）
- `src/app/etc/qt.conf` 中存在：
  - `WindowsArguments = dpiawareness=0`
- 推测影响：
  - 若进程被强制设置为 DPI-unaware（或与实际 Qt/manifest DPI 策略冲突），跨屏 DPI 行为可能被 Windows 虚拟化，从而导致首次显示时的坐标/尺寸/DWM 合成结果与预期不一致。
- 当前状态：仅做了读取与分析，未改动该配置。

### 补充：`dpiawareness` 参数含义（用于定位/验证）
在 Qt Windows 平台插件里，`WindowsArguments = dpiawareness=<n>` 通常对应进程 DPI Awareness 模式：
- `0`：DPI-unaware（不感知 DPI）
  - Windows 更倾向走 **DPI 虚拟化/位图拉伸** 路径；跨屏不同缩放时更容易出现“右/下差几像素的空白/白边”，并且“轻微拖动一下就好”（触发系统重算/Qt backingstore 重建）。
- `1`：System DPI aware（系统 DPI 感知）
  - 仅按启动时/主屏 DPI 缩放一次；跨屏到不同缩放屏时通常不会真正切换缩放，稳定性比 `0` 好，但可能出现比例不一致/略糊。
- `2`：Per-monitor DPI aware（每显示器 DPI 感知）
  - 跨屏会触发 `WM_DPICHANGED`，并要求重排/重绘；这是多屏不同缩放的推荐模式，通常也更利于排查/修复此类边缘错位。

## 当前结论（阶段性）
- 纯粹修正最大化分支的 margins/DPI 计算 **不足以覆盖** “非最大化首次显示白边、移动后消失”的场景。
- 目前的多项尝试（DPICHANGED、FRAMECHANGED、show 后强刷、1px move）都属于“让系统重算/重绘”的方向，但在用户机器上仍未稳定消除问题，说明：
  - 根因可能更偏向 **DPI 虚拟化/进程 DPI awareness 配置**、或 Qt 与 DWM 在首次 show/restoreGeometry 时机上的交互问题。

## 建议的下一步（需要进一步验证/补充信息）
1. 明确进程 DPI awareness：确认是否由 `qt.conf` 的 `dpiawareness=0` 造成 DPI 虚拟化路径（建议优先验证这一点）。
2. 把“刷新/重算”触发点从 `showEvent` 扩展到“屏幕切换/显示器变化”相关事件：
   - 当窗口从一块屏切到另一块屏、或初次确定所在屏幕后，再补一次 frame refresh。
3. 若问题仅在 restoreGeometry 恢复窗口大小后出现：需要把刷新放在 restoreGeometry 后的合适时机（可能要延迟多一帧或重复触发一次）。

### 建议优先验证的结论（与“按顺序触发”现象对应）
- 当前窗口会在关闭时保存 `saveGeometry()`，启动时执行 `restoreGeometry()`；若关闭所在屏与下次显示所在屏的 DPI/缩放上下文不一致，再叠加无边框 NCA 重算时机，可能造成 **系统最终合成区域** 与 **Qt 首帧绘制缓冲** 在右/下边界出现 1~N 像素不一致，从而表现为白边。
- 因此建议优先把 `dpiawareness` 从 `0` 改为 `2` 做回归验证，观察“拖到大屏关闭→再打开在大屏出现白边”的概率是否显著下降。
- 如需进一步钉死根因，可在 `showEvent/closeEvent` 记录：所在 `QScreen`、`devicePixelRatioF()`、窗口 `geometry/frameGeometry`，对照白边出现时是否存在边界偏差与 DPI 上下文切换。

## 备注：本次变更集中点
- `src/plugins/core/mainwindow.cpp`
  - `nativeEvent`：补强 `WM_GETMINMAXINFO`、新增 `WM_DPICHANGED` 的处理策略。
  - 构造/显示阶段：增加 `SWP_FRAMECHANGED` 强制刷新。
  - `showEvent`：show 后延迟刷新，并在非最大化时加入 1px move 抖动。
- `src/plugins/core/mainwindow.h`
  - Windows 下声明 `showEvent(QShowEvent*) override`。
