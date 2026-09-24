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

### QWindowKit 与本次回归的因果边界

用户观察到问题是在引入 QWindowKit 后出现，这个时间关联成立，但不能表述为“QWindowKit 改写了 ListModePanel 的 `sizeHint()`”。迁移提交 `af5d6b71` 对扫描页、`ListModePanel`、`TitleBar.ui` 和 `layout()->setMenuBar(m_titleBar)` 没有改动；QWindowKit 源码也没有 `WM_GETMINMAXINFO` 或业务 panel `minimumSizeHint()` 的实现。

迁移前，SGStudio 自己的 `MainWindowChromeWin::handleGetMinMaxInfo()` 每次收到 Windows 最小跟踪尺寸请求时，会显式执行：

```text
MainWindow layout.activate()
centralWidget layout.activate()
window.minimumSize().expandedTo(window.minimumSizeHint())
→ 写入 ptMinTrackSize
```

迁移后，`nativeEvent()` 和这段 SGStudio 消息适配被删除，窗口交给 QWindowKit/Qt 默认消息链路。于是原本隐藏页面的延迟布局刷新不再被 SGStudio 的 native 适配器在拖动过程中主动“顺便”激活。首次显示 ListModePanel 时，Qt 才按正常 QWidget 显示流程激活隐藏页面及其嵌套布局，暴露出 `669x23 → 523x60` 的真实缓存更新。

所以更准确的结论是：**QWindowKit 没有改变按钮或列表页的尺寸计算公式，而是改变了最小尺寸约束的执行边界，暴露了原有隐藏布局缓存与标题栏宽度未汇总的问题。** 原来的 `setMenuBar()` 接入本身在迁移前后也未变，只是旧 native 适配和较大的旧缓存曾经掩盖了它。当前修复把 TitleBar 放进 central grid，使布局本身完整表达约束，不依赖 QWindowKit 或旧的 `WM_GETMINMAXINFO` 副作用。

该判断的证据边界：当前日志证明 QWindowKit 路径下 `MainWindow::minimumSize()`、`minimumSizeHint()` 与 `QWindow::minimumSize()` 一致，Windows 正确执行了 Qt 给出的值；没有 A/B 运行日志证明 QWindowKit 改变了 `QPushButton::sizeHint()` 的公式。因此知识库将本问题记录为“QWindowKit 迁移后暴露的布局合约回归”，而不是“QWindowKit 改变了 QWidget sizeHint”。

2026-09-20 的 List Sweep 回归日志确认，在本次复现场景中 Windows 正确执行了 Qt 的最小尺寸，问题出在布局输入而非 native 链路。仍需在不同 DPI、OFDM 等布局较重的页面验证拖动边界，不能把一次验证泛化为所有环境均无问题。

只有日志证实 native resize 没有遵守正确的 Qt 最小尺寸，才考虑聚焦的最小 track-size 适配器：读取实时 `minimumSize().expandedTo(minimumSizeHint())`，按当前 DPI 转换后只写 `ptMinTrackSize`。布局 hint 本身缺少约束时，应修布局而非加 native 拦截。不要恢复已由 QWindowKit 接管的 `WM_NCCALCSIZE`、`WM_NCHITTEST`、DWM、DPI、monitor 或 frame-refresh 代码，也不要重新写入 `ptMax*`。

## 2026-09-20：首次显示 List Sweep 后标题栏被压缩

两轮逐控件日志确认了两个独立层次：

1. 隐藏 ListModePanel 的底部 `horizontalLayout` 保留 Windows 默认样式下的 `669x23` 汇总。首次显示前，各按钮实际上已经更新为 HarmonyOS Sans SC 18px / QSS 尺寸，但嵌套布局汇总仍旧；首次显示后该行重新计算为 `523x60`。表格 `61x99` 和参数按钮在切换前后没有变化。
2. TitleBar 的 `minimumSizeHint()` 为 `820x50`，但旧接入方式 `layout()->setMenuBar(m_titleBar)` 不把其宽度纳入主窗约束。列表页旧缓存退场后，OFDM 的 `579` 与单列 dock 的 `125` 合为 `704`；主窗口 minimum/hint/QWindow minimum 一致为 `704`，因此 Windows 合法地把窗口缩到不足以容纳标题栏。

按钮行宽度的实测分解：旧值 `7*75 + 76 + 40 + 28 = 669`；主题生效后的正确值 `82 + 97 + 6*46 + 40 + 28 = 523`。不能把过期的 `669` 固定下来作为产品下限，也不能把此次变化解释成 OFDM 约束丢失。

Qt 5 的 [QLayout::totalMinimumSize()](https://codebrowser.dev/qt5/qtbase/src/widgets/kernel/qlayout.cpp.html#684) 对 menu-bar 槽位只累计高度。当前代码把完整 TitleBar 放入 central grid 的跨列首行，让普通布局自动汇总它的最小宽度；原 5px 间隔变为下一空行，CommonPanel/业务页/dock 整体下移行号，屏幕位置及总高度保持不变。截图通知备用定位直接从 TitleBar 映射坐标，不再依赖 menuWidget()。

最小宽度由 **标题栏与所有业务页/侧栏共同约束**，不承诺只由 OFDM 决定。菜单内部仍使用 QMenuBar 的原有 overflow 行为；QWindowKit 的 title-bar/system-button/hit-test 注册不变。该布局修复已实施并静态检查，运行回归由用户完成，尚未宣称修复后实测通过。

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
