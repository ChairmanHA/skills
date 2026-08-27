# MainWindow Frameless Minimum Size Contract（Windows）

本文记录 Windows 无边框主窗口的最小尺寸约束问题：业务 panel 的 Qt 布局仍然能给出 `minimumSizeHint()`，但 native resize 没有使用这个值时，窗口仍可被系统拖到内容坍塌。

Raspberry Pi Wayland 固定全屏下还有相反方向的合同：所有 stacked panel 汇总后的 Qt 最小高度不能超过 native surface。Quick Waveform 隐藏页曾把主窗口抬到 `805px`，导致 `800px` 输出截断状态栏。详见 [MainWindow Panel 最小高度与 Wayland 全屏边界](mainwindow_panel_minimum_height_wayland_contract.md)。

## 问题现象

OFDM 页面在主窗口中被缩到过小时，内容会明显坍塌：

- 最小高度原本应由 OFDM panel 的多行控件和布局决定。
- 最小宽度原本主要由 OFDM 行内两个 `SwitchButton` 决定。
- 出问题后，窗口可以继续被拖小，`SwitchButton`、`LabelButton` 和两列布局被压到不可用状态。

在当前这次定位中，问题出在主窗口的 Windows frameless native resize 合约。

## 关键事实

### 1. OFDM panel 没有显式最小尺寸

`src/plugins/analog/ofdmpanel.ui` 没有给 panel 写死 `minimumSize`。

旧行为能保持最小尺寸，是因为 Qt 布局会从子控件递推出当前页面的 `minimumSizeHint()`。例如：

- 全局 QSS 中普通 `SwitchButton QPushButton` 有 `min-width: 72px`。
- OFDM 的 `btnNullDC` 和 `btnWindowed` 位于同一行，各自包含 On/Off 两个子按钮。
- 因此这一行会成为 OFDM 页面横向最小尺寸的重要来源。

### 2. `minimumSizeHint()` 语义

语义分工是：

- `minimumSizeHint()`：Qt 根据 widget/layout/style 当前状态给出的建议最小尺寸。
- `setMinimumSize()`：显式写死某个 widget 的最小尺寸。
- Windows `MINMAXINFO::ptMinTrackSize`：native 拖拽时系统允许窗口缩到的最小 track size。

本次修复没有调用 `setMinimumSize()`，也没有“设置 minimumSizeHint”。它是读取 Qt 当前布局算出的 `minimumSizeHint()`，再把这个结果写入 Win32 的 `ptMinTrackSize`。

### 3. Frameless 主窗口接管了 native resize

Windows 下主窗口使用 frameless chrome：

- `MainWindow` 设置 `Qt::FramelessWindowHint`。
- `MainWindowChromeWin` 处理 `WM_NCCALCSIZE`、`WM_NCHITTEST`、`WM_GETMINMAXINFO` 等 Win32 消息。
- 一旦 `WM_GETMINMAXINFO` 被应用接管，系统拖拽边界就不能只假设 Qt 顶层布局会自动限制住窗口。

旧代码在 `MainWindowChromeWin::handleGetMinMaxInfo()` 中处理了最大化工作区：

- `ptMaxPosition`
- `ptMaxSize`
- `ptMaxTrackSize`

但没有写 `ptMinTrackSize`。这使得 native resize loop 可以把 frameless 主窗口拖到小于 Qt 当前内容最小 hint 的尺寸。

## 根因

根因不是 OFDM 没有写死尺寸，也不是需要新增 `setMinimumSizeHint()`。

根因是：

1. 当前主窗口是 Windows frameless 窗口。
2. 应用处理了 `WM_GETMINMAXINFO`。
3. 该处理路径没有把 Qt 当前布局的最小尺寸同步到 `MINMAXINFO::ptMinTrackSize`。
4. Windows 原生拖拽因此可以继续缩小窗口。
5. Qt 布局被迫在小于自身有效最小尺寸的空间内布局，最终表现为业务 panel 坍塌。

这类问题的典型信号是：

- 页面内部控件仍有合理的 `minimumSizeHint()` 来源。
- 手动拖拽窗口时，顶层窗口却能突破这个边界。
- 问题集中出现在 frameless/nativeEvent 自定义窗口上。

## 当前修复

当前修复位于：

- `src/plugins/core/mainwindowchrome_win.cpp`

实现策略：

1. 在 `WM_GETMINMAXINFO` 处理期间激活当前 Qt 布局。
2. 读取 `window->minimumSize().expandedTo(window->minimumSizeHint())`。
3. 用当前 `devicePixelRatioF()` 转换为 native 像素。
4. 写入 `MINMAXINFO::ptMinTrackSize`。
5. 保留既有最大化工作区处理。

这等价于把 Qt 布局系统的当前最小尺寸，翻译给 Windows 原生 resize 系统。

## 与 minibar compact 的关系

仓库当前仍保留 minibar host 侧的 `SwitchButton::compactMode` helper。

这条 compact 语义用于轻量 host 下收紧开关内部 margin 和子按钮最小宽度，不是本次主窗口坍塌的最终修复点。主窗口问题的关键是：无论当前 panel 的 minimum hint 是大是小，Windows native resize 都必须尊重这个 hint。

因此边界是：

- `SwitchButton::compactMode`：控件级/host 侧紧凑视觉适配。
- `minimumSizeHint()`：Qt 当前布局给出的最小尺寸建议。
- `ptMinTrackSize`：Windows 拖拽窗口时必须遵守的 native 最小尺寸。

不要把这三层混成一个问题。

## 后续维护检查清单

修改主窗口尺寸、frameless chrome、业务 panel 布局或 `SwitchButton` 样式时，至少检查：

- `MainWindowChromeWin::handleGetMinMaxInfo()` 是否仍写入 `ptMinTrackSize`。
- 写入值是否来自当前 Qt 布局，而不是过期缓存。
- DPI 转换是否仍用当前窗口的 `devicePixelRatioF()`。
- 最大化工作区逻辑是否仍只负责 `ptMax*`，不要覆盖最小 track size。
- 新增业务 panel 如果有特殊尺寸需求，优先确认其 `minimumSizeHint()` 是否合理，再看顶层 native resize 是否尊重它。

## 相关文件

- `src/plugins/core/mainwindowchrome_win.cpp`
- `src/plugins/core/mainwindow.cpp`
- `src/plugins/analog/ofdmpanel.ui`
- `src/libs/controls/switchbutton.cpp`
- `configuration/theme.css`
- `configuration/theme_light.css`
- `mainwindow_panel_minimum_height_wayland_contract.md`
