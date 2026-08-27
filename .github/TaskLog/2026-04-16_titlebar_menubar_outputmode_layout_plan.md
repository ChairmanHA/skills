# TitleBar / QMenuBar / OutputMode 布局修复计划

## 问题现象

- 主窗口经过宽度压缩后，标题栏中的 QMenuBar 会出现右侧扩展按钮 `>>`。
- 当前 `Single` / `Continue` 不是 TitleBar 自己右侧布局的一部分，而是通过 `QMenuBar::setCornerWidget(..., Qt::TopRightCorner)` 挂在菜单栏右上角。
- 当标题栏可用宽度继续缩小时，QMenuBar 自己的 overflow/toolbutton 与 cornerWidget 会竞争同一段右端几何区域，表现为 `>>` 不明显、甚至视觉上与 `Single` 按钮发生重叠。

## 根因分析

### 1. 职责混放

- QMenuBar 本应只负责菜单项及其溢出逻辑。
- `Single` / `Continue` 属于标题栏业务操作按钮，不属于菜单系统。
- 现在把业务按钮塞进 `cornerWidget`，等于让“菜单溢出控件”和“业务按钮”共用 QMenuBar 的右端保留区，宽度临界时必然冲突。

### 2. TitleBar 现有布局没有给菜单栏正确分配弹性空间

- `titlebar.ui` 顶层 `horizontalLayout_3` 当前 stretch 为 `0,0,1`，额外空间主要给了最右侧窗口控制布局。
- 右侧布局内部还有一个 expanding spacer，因此菜单栏没有拿到中间那段应有的可伸缩宽度。
- 这会更早触发 QMenuBar 的溢出按钮，使 `>>` 在本来还可容纳更多菜单项时就提前出现。

## 目标结构

标题栏几何关系调整为：

`[logo][menubar stretch 1][output mode buttons][spacer][min][max][close]`

关键点：

- QMenuBar 作为中间自适应区域，独占菜单显示与 overflow 按钮。
- `Single` / `Continue` 迁出 `QMenuBar::cornerWidget`，进入 TitleBar 自己的右侧布局槽位。
- 窗口控制按钮仍保留最右固定区域。

## 实施方案

1. 修改 `titlebar.ui`
   - 顶层 stretch 调整为 `0,1,0`。
   - 在右侧窗口控制布局最前面增加一个空的 `outputModeLayout`，作为运行时宿主槽位。
2. 修改 `TitleBar`
   - 删除对 `menubarLayout` 的居中约束。
   - 给 QMenuBar 设置横向 `Expanding` size policy。
   - 新增 `setOutputModeWidget(QWidget *)`，把运行时创建的 `outputModeWidget` 插入到 `outputModeLayout`。
3. 修改 `MainWindow`
   - 保留现有 `Single` / `Continue` QAction 与 QPushButton 的业务绑定逻辑。
   - 删除 `menubar->menuBar()->setCornerWidget(...)`。
   - 改为 `m_titleBar->setOutputModeWidget(outputModeWidget)`。

## 预期收益

- `>>` 将只出现在菜单栏自己的有效区域内，不再与 `Single` / `Continue` 重叠。
- 菜单栏获得中间 stretch 后，overflow 触发点后移，视觉更稳定。
- 业务按钮和菜单系统彻底解耦，后续再做标题栏响应式调整时不会继续受 QMenuBar 内部实现牵制。

## 2026-04-16 扩展按钮图标与宽度补充方案

- Qt 5.15 的 menubar overflow 按钮不是文本 `>>`，而是内部 `qt_menubar_ext_button` 使用标准 icon 绘制。
- 因此纯 QSS 只能稳定控制按钮背景，不能可靠控制 `>>` 的亮度和大小。
- 本轮改动采用两步方案：
   1. 在 core 插件 qrc 中新增一对亮色/暗色 `>>` SVG 资源。
   2. 在 `TitleBar` 中捕获 `qt_menubar_ext_button`，根据当前主题切换 icon，并设置统一 `iconSize`。
   3. 给该内部按钮安装一个局部 `QProxyStyle`，仅对 `PM_ToolBarExtensionExtent` 增加少量宽度，避免图标放大后仍显得局促。

### 资源选择规则

- 深色主题使用亮色 `>>` 图标。
- 浅色主题使用暗色 `>>` 图标。

### 设计边界

- 不回退当前 TitleBar / OutputMode 的布局重构。
- 不继续依赖 `theme.css` / `theme_light.css` 的 `color` 去改 `>>`。
- 仅定向处理 menubar 内部扩展按钮，不影响普通 `QToolButton` 或其他 toolbar 控件。

## 2026-04-16 第二轮修正：图标发虚与 hover 背景异常

- 首轮实现里给 `qt_menubar_ext_button` 单独安装了 `QProxyStyle`，这会绕开该按钮原本走的样式表代理链，直接表现就是 hover 背景回退为平台默认样式，出现发白现象。
- 首轮 qrc alias 未保留 `.svg` 后缀，`QIcon` 走资源路径时不利于稳定命中 SVG icon engine，放大后容易表现得不够锐利。
- 第二轮修正改为：
   1. 删除按钮级 `setStyle()` 方案，恢复 menubar overflow 按钮继续走全局 QSS。
   2. qrc alias 改为显式 `.svg` 资源名，代码使用完整 `.svg` 路径加载图标。
   3. 略微加宽改为在 `theme.css` / `theme_light.css` 中定向约束 `QToolButton#qt_menubar_ext_button` 的最小宽度与左右 padding。
   4. SVG 本身改为填充式 chevron，避免细描边在缩放时发虚。

## 2026-04-16 第三轮收口：取消放大，仅保留提亮

- 运行截图表明 menubar overflow 按钮的几何空间基本由 Qt 内部布局决定，继续放大 icon 或额外加宽按钮只会让观感更挤，不会真正获得更大可视空间。
- 因此本轮收口方案是：
   1. 删除 `TitleBar` 中对 overflow 按钮的 `setIconSize()` 放大逻辑。
   2. 删除主题文件里对 `qt_menubar_ext_button` 的最小宽度和 padding 定向放大规则。
   3. 保留亮/暗主题图标替换，只解决 `>>` 不够亮的问题。