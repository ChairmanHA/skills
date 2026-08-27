# MainWindow Panel 最小高度与 Wayland 全屏边界

本文记录一次已在 Raspberry Pi Wayland 真机确认的问题：一个当前不可见的 Quick Waveform 页面通过 `QStackedLayout` 抬高了整个主窗口的最小高度，使 Qt 顶层窗口达到 `805px`，而 Wayland 全屏输出只有 `800px`，最终导致底部状态栏被截断 `5px`。

这不是 Quick Waveform 独有规则。以后新增任何 business panel 或 standalone page，都必须检查它对 `FancyTabWidget`、`CentralWidget` 和顶层 `MainWindow` 的 `minimumSizeHint()` 传播结果。

## 1. 已验证的运行时现象

树莓派运行环境：

- Wayland compositor：labwc。
- 物理输出经过旋转后的逻辑尺寸：`1280 x 800`。
- 主窗口使用 frameless + full-screen 路径。

真机截图表现为：

- 状态栏内部已经按代码设置为 `27px`。
- 屏幕上只能看到约 `22px`。
- GNSS 图标和 Charging 文本因此看起来偏下或底部被截断。

GDB 对真实运行进程读取到的几何为：

| 对象 | 运行时几何 |
| :--- | :--- |
| `MainWindow` | `1280 x 805`, `y=0` |
| `TitleBar` | `1280 x 50`, `y=0` |
| `CentralWidget` | `1280 x 728`, `y=50` |
| `FancyTabWidget` | `1075 x 663`, `y=65` |
| `QStatusBar` | `1280 x 27`, `y=778` |

状态栏实际覆盖 `y=778..804`，但输出只能显示到 `y=799`，所以最后 `5px` 位于 Wayland 输出之外。

## 2. 805px 是怎样算出来的

当前主窗口的垂直约束链为：

```text
TitleBar                                      50
CentralWidget top margin                       5
CommonPanel row                               60
FancyTabWidget minimumSizeHint height        663
QStatusBar                                    27
------------------------------------------------
MainWindow effective minimum height          805
```

也就是：

```text
CentralWidget minimum = 5 + 60 + 663 = 728
MainWindow minimum    = 50 + 728 + 27 = 805
```

`centraLayout->setRowStretch(1, 1)` 只能分配超过最小尺寸后的剩余空间，不能把 row 1 压到低于 `FancyTabWidget::minimumSizeHint()`。因此增加 stretch 并不能让 CentralWidget 自动归还这 `5px`。

## 3. 为什么当前页面不是 Quick Waveform，仍被它撑高

`FancyTabWidget` 内部使用 `QStackedLayout` 承载所有 business 和 standalone 页面。

这里最重要的 Qt 行为是：

- stacked layout 的尺寸提示会综合所有已加入页面，而不只考虑当前可见页面；
- 宽度和高度还可能分别来自不同页面；
- 隐藏页面因此仍能改变顶层窗口的最小尺寸。

真机逐页读取到的关键结果是：

| 页面 | `minimumSizeHint()` | 作用 |
| :--- | :--- | :--- |
| Quick Waveform | `370 x 663` | 提供最大高度 `663` |
| OFDM | `579 x 567` | 提供最大宽度 `579`，高度仍有余量 |
| 当前 Sweep 页面 | `375 x 233` | 当前可见，但不是 stacked 最大值 |

所以最终 `FancyTabWidget::minimumSizeHint()` 是 `579 x 663`：宽度来自 OFDM，高度来自 Quick Waveform。

排查这类问题时，只观察当前页面必然会漏掉根因。必须枚举 stacked layout 中的全部页面。

## 4. Windows 与 Wayland 是两个不同合同

### Windows frameless resize

Windows 问题关注的是：native resize 是否尊重 Qt 已经算出的最小尺寸。

当前 `MainWindowChromeWin` 会把：

```text
window->minimumSize().expandedTo(window->minimumSizeHint())
```

写入 `MINMAXINFO::ptMinTrackSize`，避免用户把窗口拖到业务控件坍塌。

### Raspberry Pi Wayland full screen

Wayland 问题关注的是：Qt 算出的最小尺寸本身是否已经超过固定 full-screen surface。

当业务页面把 Qt 顶层最小高度抬到 `805px` 时，目标输出仍只有 `800px`。不能依赖 compositor 提供额外空间，也不能用 Windows 的 `ptMinTrackSize` 路径解决。

两者的共同原则是“尊重真实业务最小尺寸”，但检查方向相反：

- Windows：防止 native window 小于 Qt minimum。
- Wayland 固定全屏：防止 Qt minimum 大于 native surface。

## 5. 新增或修改任何 Panel 的强制检查清单

### 5.1 静态检查

- 先从对应 `CMakeLists.txt` 确认 `.ui/.cpp` 确实属于当前活动 target。
- 检查 panel 根控件、子控件、layout 是否存在：
  - `minimumSize` / `minimumHeight`；
  - `setFixedHeight()`；
  - QSS `min-height` / `max-height`；
  - 多行固定高度控件及 layout spacing/margins。
- 检查所有 spacer：
  - 装饰性底部空白应使用纵向 `Expanding`；
  - 只有真实业务下限才使用纵向 `Minimum` / `Fixed`；
  - 不要让 Qt Designer 的设计占位高度无意进入运行时 minimum。
- 对 `.ui` 使用项目实际 Qt 主版本的 `uic`，检查生成的 `QSpacerItem(width, height, hPolicy, vPolicy)`，不要只看 Designer 中的箭头方向。
- Qt 5 项目优先使用已验证的 `Qt::Vertical` / `Qt::Horizontal` 枚举写法；修改后必须以生成代码确认最终方向。
- 计算 panel 外层包装成本，例如 `TabWidget` 的页面标题高度；不要只记录 control panel 自身高度。

### 5.2 Stacked layout 检查

- 枚举 `QStackedLayout` 中所有页面的 `minimumSizeHint()`，包括当前隐藏页面。
- 分别找出最大宽度页和最大高度页；它们可能不是同一个页面。
- 新增页面后重新检查 `FancyTabWidget::minimumSizeHint()`，不能只验证新页面本身“看起来能显示”。
- 页面被列表隐藏、切到后台或当前 index 不是它，都不代表它不再参与 stacked minimum。

### 5.3 当前 1280x800 产品基线

在当前主窗口结构和状态栏高度下：

```text
TitleBar        = 50
StatusBar       = 27
Central margin  = 5
CommonPanel     = 60
Wayland surface = 800
```

因此当前约束应满足：

```text
max stacked page minimum height <= 800 - 50 - 27 - 5 - 60
                                <= 658
CentralWidget minimum height    <= 723
MainWindow effective minimum    <= 800
```

任何常量发生变化后都要重新计算，不要把 `658/723/800` 当成永久魔数。

### 5.4 运行时检查

- Windows：检查普通窗口、最大化和 frameless 拖拽最小尺寸。
- Raspberry Pi Wayland：检查真实 `1280 x 800` full-screen surface，不以 Win32 正常显示作为通过依据。
- 同时读取或记录：
  - `MainWindow` geometry / minimumSizeHint；
  - TitleBar、CentralWidget、QStatusBar geometry；
  - `FancyTabWidget` minimumSizeHint；
  - stacked layout 每个页面的 minimumSizeHint。
- 对比 QWidget 顶层几何与 Wayland 输出几何；若 QWidget 大于 surface，优先定位是哪一个 panel 抬高了 minimum。
- 切换到其他页面后仍要检查，因为根因可能来自不可见页面。

## 6. 修复优先级

发现 panel 抬高顶层最小尺寸时，按下面顺序处理：

1. 先移除或修正无业务意义的设计占位、错误 spacer 方向和过期固定高度。
2. 再检查局部 margins、spacing 和 QSS 是否重复叠加。
3. 如果最小高度确实来自不可压缩的真实控件，重新设计该 panel 的滚动、分页或响应式布局。
4. 不要优先给 `CentralWidget` 设置 `QSizePolicy::Ignored`，也不要强制突破真实 `minimumSizeHint()`；这会把“状态栏被截断”变成“业务控件重叠或坍塌”。
5. 不要为了一个 panel 全局缩小 `QPushButton` / `LabelButton` 高度；应先做 panel-local 修复。

## 7. 相关文件与文档

- `src/plugins/core/mainwindow.cpp`
- `src/plugins/core/fancytabwidget.cpp`
- `src/plugins/quickwaveform/quickwaveformpanel.ui`
- `src/plugins/core/mainwindowchrome_win.cpp`
- [MainWindow Frameless Minimum Size Contract（Windows）](mainwindow_frameless_minimum_size_contract.md)
- [FancyTabWidget 调制列表响应式布局设计](fancytabwidget_modulation_list_responsive_layout.md)

