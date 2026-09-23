# MainWindow Frameless Minimum Size Contract（Windows）

本文记录 Windows 无边框主窗口的最小尺寸约束问题，以及迁移到 QWindowKit 后需要重新验证的运行时合约：业务 panel 的 Qt 布局仍然能给出 `minimumSizeHint()`，但 Windows native resize 是否采用这个值，取决于最终的 `WM_GETMINMAXINFO` 消息链路。

Raspberry Pi Wayland 固定全屏下还有相反方向的合同：所有 stacked panel 汇总后的 Qt 最小高度不能超过 native surface。Quick Waveform 隐藏页曾把主窗口抬到 `805px`，导致 `800px` 输出截断状态栏。详见 [MainWindow Panel 最小高度与 Wayland 全屏边界](mainwindow_panel_minimum_height_wayland_contract.md)。

## 问题现象

OFDM 页面在主窗口中被缩到过小时，内容会明显坍塌：

- 最小高度原本应由 QuickWaveform panel 的多行控件和布局决定。
- 最小宽度原本主要由 OFDM 行内两个 `SwitchButton` 决定。
- 出问题后，窗口可以继续被拖小，`SwitchButton`、`LabelButton` 和两列布局被压到不可用状态。

历史问题出在主窗口的 Windows frameless native resize 合约。2026-09-18 迁移到 QWindowKit 后，SGStudio 已移除自己的消息拦截,测试已经通过；



## QWindowKit 迁移后的当前状态

2026-09-18 的 Win32 迁移删除了 `MainWindowChromeWin`，当前边界是：

- `MainWindow` 在 Windows 下创建并持有 `QWK::WidgetWindowAgent`。
- QWindowKit 接管 native frame、resize/caption hit-test、系统按钮命中、DPI 与窗口过程生命周期。
- `MainWindow` 不再设置 `Qt::FramelessWindowHint`，也不再覆写 `nativeEvent()`。
- SGStudio 不再处理 `WM_GETMINMAXINFO`，历史 `ptMinTrackSize` 和 `ptMax*` 写入均已移除。
- QWindowKit 1.5.1 自身不处理 `WM_GETMINMAXINFO`；未处理消息会继续交给 Qt 原窗口过程。

因此当前不是“确认不需要最小尺寸合约”，而是有意恢复 Qt 默认消息链路后等待测试。必须在 OFDM 等布局较重的页面拖动到最小尺寸，观察 Windows 是否停在实时 `minimumSizeHint()` 边界。

如果测试仍允许内容坍塌，后续只增加一个聚焦的最小 track-size 适配器：读取实时 `minimumSize().expandedTo(minimumSizeHint())`，按当前 DPI 转换后只写 `ptMinTrackSize`。不要恢复已由 QWindowKit 接管的 `WM_NCCALCSIZE`、`WM_NCHITTEST`、DWM、DPI、monitor 或 frame-refresh 代码，也不要重新写入 `ptMax*`。

## 与 minibar compact 的关系

仓库当前仍保留 minibar host 侧的 `SwitchButton::compactMode` helper。

这条 compact 语义用于轻量 host 下收紧开关内部 margin 和子按钮最小宽度，不是本次主窗口坍塌的最终修复点。主窗口问题的关键是：无论当前 panel 的 minimum hint 是大是小，Windows native resize 都必须尊重这个 hint。

因此边界是：

- `SwitchButton::compactMode`：控件级/host 侧紧凑视觉适配。
- `minimumSizeHint()`：Qt 当前布局给出的最小尺寸建议。
- `ptMinTrackSize`：如果 Qt 默认链路不能落实当前布局 hint，SGStudio 聚焦适配器需要补齐的 native 最小尺寸。

## 标题栏垂直带内的非标题栏控件命中

instrument 模式的右侧 modulation dock 从 central-layout 的 `y=0` 开始，顶部浮动滚动按钮因此会落在自定义 TitleBar 的同一垂直命中带内。Win32 `WM_NCHITTEST` 不能只用 `TitleBar::childAt()` 判断该区域：右侧 dock 不属于 TitleBar 子树时，空结果会被误判为 `HTCAPTION`，按钮双击就会触发窗口最大化。

迁移后由 QWindowKit 负责标题栏 hit-test。SGStudio 负责声明哪些现有控件必须保持 client input：

- TitleBar 内的 menu bar；
- TitleBar 内的 output-mode action group；
- 不属于 TitleBar 子树、但顶部会与标题栏垂直带重叠的 modulation dock。

这些 widget 通过 `WidgetWindowAgent::setHitTestVisible()` 注册；未被注册的 TitleBar 空白仍由 QWindowKit 作为可拖动区域处理。最小化、最大化和关闭按钮则通过 `setSystemButton()` 注册，在保留 SGStudio 原有 click 行为的同时获得系统按钮命中语义。这套注册由 Windows 与 Linux x86_64 共用。

Linux x86_64 在 Qt 5.15 下由 QWindowKit 通用 `QtWindowContext` 调用 Qt 的 system move/resize API，同时仍显式保留 `1280x800` 产品最小尺寸。固定全屏的 aarch64 Wayland 路径不使用该 agent。Windows、x86_64 X11 和 aarch64 Wayland 仍应分别在目标设备上做真实触摸/鼠标回归。

不要把这三层混成一个问题。

## 后续维护检查清单

修改主窗口尺寸、frameless chrome、业务 panel 布局或 `SwitchButton` 样式时，至少检查：

- QWindowKit agent 是否仍在窗口早期完成 `setup()`，且每个受支持的桌面主窗口只有一个 chrome owner。
- TitleBar、三个系统按钮、menu bar、output-mode widget 和 modulation dock 是否仍正确注册。
- 新增业务 panel 如果有特殊尺寸需求，先确认其 `minimumSizeHint()` 是否合理，再在 Windows native resize 中实测是否被遵守。
- 若已经因为测试失败加入聚焦的 `WM_GETMINMAXINFO` 适配器，确认它只写 `ptMinTrackSize`，值来自当前 Qt 布局而不是缓存，DPI 转换使用当前窗口倍率。
- 不要在 SGStudio 中重新实现 QWindowKit 已负责的 frame、hit-test、DWM、DPI、monitor 或 refresh 分支。

## 相关文件

- `src/plugins/core/mainwindow.cpp`
- `src/plugins/core/titlebar.cpp`
- `3rdParty/qwindowkit/src/widgets/widgetwindowagent.cpp`
- `3rdParty/qwindowkit/src/core/contexts/win32windowcontext.cpp`
- `3rdParty/qwindowkit/src/core/contexts/qtwindowcontext.cpp`
- `src/plugins/analog/ofdmpanel.ui`
- `src/libs/controls/switchbutton.cpp`
- `configuration/theme.css`
- `configuration/theme_light.css`
- `mainwindow_panel_minimum_height_wayland_contract.md`
