# 2026-04-16 CommonPanel / DeviceInfoWidget 响应式布局实施计划

## 目标

- 让 `CommonPanel` 参考 `FancyTabWidget` 右侧调制列表的“带滞回切换”思路，在宽度不足时从单行横排切到双行布局。
- 切换到双行后：
  - 第一行只放 `freq` 和 `level`
  - 第二行只放四个固定 `100px` 按钮：`RF`、`MOD`、`Sweep`、`Device Settings`
- 让 `DeviceInfoWidget` 在窗口继续缩窄前，先进入 compact 显示模式，只保留“连接状态 + 温度”，释放 status bar 对主窗口横向最小宽度的约束。
- 当窗口重新变宽时，两者都能稳定恢复原布局，不在临界宽度附近来回抖动。

## 当前基线与约束

### 1. 主窗口横向宽度的真实约束链

当前主窗口可近似理解为：

`MainWindow = max(CommonPanel, 左侧 FancyTabWidget 当前参与布局的内容) + 右侧 modulation dock + status bar 子项`

已知结论：

- `FancyTabWidget` 右侧调制列表已经采用“固定目标宽度 + 滞回”的模式切换。
- `CommonPanel` 当前仍是左列顶部的主要宽度瓶颈之一。
- `DeviceInfoWidget` 虽然已经去掉过硬编码的大最小宽度，但它内部仍然有完整详情区，会继续参与 `QStatusBar` 的 `minimumSizeHint()` 计算。

## 方案结论

### 零、这两个控件应直接去掉 .ui，改为纯代码布局

结论先行：

- **对 `CommonPanel` 和 `DeviceInfoWidget`，纯代码布局比继续改 `.ui` 更简单，也更稳妥。**

原因不是“代码布局天然更高级”，而是这两个控件刚好都满足下面三个条件：

1. 这次改动的核心是**运行时动态重排**，不是静态排版微调。
2. 两个控件都需要引入**两态模式**、成组隐藏、阈值切换和几何刷新。
3. 当前 `.ui` 已经把不少结构性约束写死在 Designer 里，继续在 `.ui` 上修补会形成“双重真相”：
  - 一部分结构在 `.ui`
  - 一部分结构在 `.cpp`

对这次任务来说，`.ui` 的主要拖累是：

- `CommonPanel` 需要从单一顶层 `QHBoxLayout` 改成可重排的双行结构；继续留在 `.ui` 中，会让“静态排版”和“运行时重排”混在一起。
- `DeviceInfoWidget` 需要把 `temperature` 从详情区拆出来并新增多个 group 容器；这类结构性拆分在 Designer 中做完，最终仍然要在代码里维护模式切换，收益不高。
- 当前业务代码已经大量在构造函数里二次覆盖 `.ui` 设置，例如 `CommonPanel` 的高度、stretch、对齐和 `DeviceInfoWidget` 的温度宽度保留；说明这两个控件本来就不是“纯 Designer 驱动”。

但去掉 `.ui` 时有一个硬约束必须保留：

- **必须保留现有关键子控件的 `objectName` 和控件类型语义。**

原因：当前主题样式明确依赖这些选择器：

- [configuration/theme.css](configuration/theme.css#L174)
- [configuration/theme.css](configuration/theme.css#L329)
- [configuration/theme_light.css](configuration/theme_light.css#L760)
- [configuration/theme_light.css](configuration/theme_light.css#L928)

尤其需要保留的名字包括：

- `CommonPanel`：`btnRf`、`btnMod`、`sweep`、`deviceSettings`
- `DeviceInfoWidget`：`deviceState`

如果实现时把这些 objectName 改掉，QSS 会先坏，视觉回归会比布局问题更快出现。

因此建议的落地方式不是“边保留 `.ui` 边补代码”，而是：

- 直接把这两个控件改为纯代码建树
- 同步移除 `ui_commonpanel.h / ui_deviceinfowidget.h` 依赖
- 同步从 [src/plugins/core/CMakeLists.txt](src/plugins/core/CMakeLists.txt) 里移除这两个 `.ui`
- 在代码中原样恢复必要的 `objectName`

### 一、CommonPanel 采用“宽 / 窄”两态响应式布局

建议新增：

```cpp
enum class CommonPanelLayoutMode {
    Wide,
    Compact
};
```

布局语义：

- `Wide`：保持现有单行视觉语义
- `Compact`：切成上下两行

切换原则：

- 参考 `FancyTabWidget` 的做法，采用**阈值 + 滞回**，而不是单纯 `if (width < X)`
- 由 `CommonPanel` 自己维护当前模式和阈值判断
- 模式变化时只在状态边界切换一次，避免拖拽窗口时抖动

### 二、DeviceInfoWidget 采用“完整 / 紧凑”两态显示模式

建议新增：

```cpp
enum class DeviceInfoDisplayMode {
    Full,
    Compact
};
```

显示语义：

- `Full`：维持当前完整显示
- `Compact`：只保留 `deviceState` 和 `temperature`

切换原则：

- 同样采用**阈值 + 滞回**
- 但它的切换阈值要**先于 CommonPanel 折行触发**
- 目的不是视觉优先，而是先释放 status bar 宽度链，给主窗继续缩小创造条件

### 三、两者不要各自“盲切”，要有一个主窗口层的协调点

虽然每个控件都应当有自己的 `apply...Mode()`，但最终建议由 `MainWindow` 统一调度响应式更新，原因有两个：

- `CommonPanel` 的有效宽度不是整窗宽度，而是“左列宽度”
- `DeviceInfoWidget` 的有效宽度接近整窗宽度，且它应该先切换

因此建议在 `MainWindow` 增加一个统一入口，例如：

```cpp
void MainWindow::scheduleResponsiveUiUpdate();
void MainWindow::updateResponsiveUi();
```

触发时机：

- `resizeEvent()` 后用 queued call 触发一次
- 初始化完成后再补触发一次
- 必要时在 modulation dock 宽度发生档位切换后补触发一次

调度顺序：

1. 先更新 `DeviceInfoWidget` 的显示模式
2. 再更新 `CommonPanel` 的布局模式

这样可以显式保证“先释放 status bar，再判断 CommonPanel 是否需要折行”。

## 详细设计

### 一、CommonPanel 的布局重构方式

不建议继续依赖当前 `.ui` 里的单个顶层 `QHBoxLayout`，而应该直接在代码里明确拆成两个逻辑行：

- `valueRow`：承载 `freq` / `level`
- `buttonRow`：承载 `btnRf` / `btnMod` / `sweep` / `deviceSettings`

建议结构：

- 顶层改成 `QGridLayout` 或 `QVBoxLayout + 可重排的子容器`
- `valueRow` 单独一个容器和布局
- `buttonRow` 单独一个容器和布局

推荐选型：

- **优先用一个顶层 `QGridLayout` + 两个子行容器**

原因：

- `Wide` 模式下可以把 `valueRow` 和 `buttonRow` 放在同一行不同列
- `Compact` 模式下只需要把 `buttonRow` 挪到第二行
- 不需要在两套 page 间复制按钮控件，也不需要反复 reparent 单个按钮
- 代码中直接持有布局和容器指针，比让 Designer 先生成固定树、再在运行时拆树更清晰

### 二、CommonPanel 模式切换时必须同步改的约束

#### 1. 高度

当前 `setFixedHeight(60)` 必须取消，改成模式驱动：

- `Wide`：`60px`
- `Compact`：`120px + 行间距/边距`

实现上建议：

- 不要继续在构造里永久 `setFixedHeight(60)`
- 改成 `applyLayoutMode()` 里按模式设置固定高度或最小高度

#### 2. freq / level 的最大宽度

这是本次方案里最容易漏掉、但必须处理的点。

如果还保留：

- `freq max = 243`
- `level max = 150`

那么切到双行后，第一行即使拿到整行宽度，也仍然只会停在 `243 + 150` 附近，达不到这次改动的目标。

因此必须改成：

- `Wide`：保留当前上限，维持现有视觉比例
- `Compact`：解除或显著放宽 `freq / level` 的最大宽度

推荐做法：

- `Wide` 模式下恢复 `243 / 150`
- `Compact` 模式下设为 `QWIDGETSIZE_MAX`
- 继续保留最小宽度 `100 / 60`
- 顶部 `valueRow` 的 stretch 继续使用接近当前的比例，让频率比分贝占更多空间

#### 3. 按钮宽度

第二行四个按钮继续保持：

- 每个固定 `100px`
- 不跟随 expand 拉伸
- 行内 spacing 保持和现有视觉一致

这样第二行的理论基线宽度约为：

`4 * 100 + 3 * spacing + 左右 margin`

也就是大约 `420~440px` 的量级。

这正是第一行能拿到 `400+px` 可用宽度的基础。

### 三、CommonPanel 的阈值策略

建议常量采用和调制列表类似的命名方式：

```cpp
static const int kCommonPanelWideComfortWidth = ...;
static const int kCommonPanelExpandHysteresis = 24;
```

推荐判定逻辑：

- 当前是 `Wide` 时：
  - 如果 `CommonPanel::width()` 小于 `kCommonPanelWideComfortWidth`，切到 `Compact`
- 当前是 `Compact` 时：
  - 只有当 `CommonPanel::width()` 大于等于 `kCommonPanelWideComfortWidth + kCommonPanelExpandHysteresis` 时，才恢复 `Wide`

阈值来源不要拍脑袋，建议直接用当前设计宽度推导：

- `freq wide max`
- `level wide max`
- `4 * 100px 按钮`
- 行内 spacing
- 左右 margins

这样后续若按钮宽度或 spacing 变了，只需要同步调整推导常量。

### 四、DeviceInfoWidget 的布局拆分方式

建议把当前结构改成三个可独立控制的子区域：

- `stateGroup`
  - `deviceState`
- `statusGroup`
  - `bandwidth`
  - `warnningStatus`
- `detailGroup`
  - `uid / model / hardware / mfw / ffw / api / gui / sampleRate`
- `temperatureGroup`
  - `temperature`
  - `temperature_1`

其中最关键的是：

- **temperatureGroup 必须从当前 centerWidget 内独立出来**

实现方式建议直接改成代码建树，不再保留 `centerWidget` 这种纯 Designer 历史命名。

否则 compact 模式无法做到“隐藏所有详情但保留温度”。

### 五、DeviceInfoWidget 紧凑模式下的处理方式

`Compact` 模式下只保留：

- `deviceState`
- `bandwidth`
- `temperatureGroup`

隐藏：
- `warnningStatus`
- `detailGroup`

同时注意两点：

1. 不能依赖“文本设空”来压缩宽度，必须直接 `setVisible(false)` 到组容器级别。
2. 中间的 spacer 不要继续用一个带巨大设计宽度的占位结构去表达，建议改成 stretch 或一个可自然收缩的 spacer。

### 六、DeviceInfoWidget 是否需要“移出布局”

结论：

- **不需要把 DeviceInfoWidget 从 QStatusBar 里拿出去**
- **也不需要在模式切换时 removeWidget / addWidget**

原因：

- Qt 的 `QBoxLayout` / `QStatusBar` 对隐藏的子 `QWidget` 会重新计算布局
- 真正的问题不是“它在 status bar 里”，而是“它内部当前没有按功能拆组”

所以正确做法是：

- 保持 `MainWindow -> QStatusBar -> DeviceInfoWidget` 结构不变
- 只在 `DeviceInfoWidget` 内部重组布局并按组切换可见性

### 七、DeviceInfoWidget 的阈值策略

建议同样引入滞回，避免恢复时闪烁。

```cpp
static const int kDeviceInfoCompactThreshold = ...;
static const int kDeviceInfoExpandHysteresis = 24;
```

这里不建议只看 `DeviceInfoWidget` 自己的静态 `sizeHint()`，而要结合“它应当先于 CommonPanel 折行”这个目标统一校准。

推荐策略：

- 由 `MainWindow::updateResponsiveUi()` 先根据整窗宽度或 status bar 可用宽度判断 `DeviceInfoWidget` 是否进入 compact
- 阈值设定为：**比 CommonPanel 折行对应的整窗宽度更早一点触发**

这样可以保证：

- 在主窗口继续缩小时，先释放底部 status bar 的宽度链
- 然后左列顶部 `CommonPanel` 再在更小宽度进入双行模式

## 推荐实施步骤

### Step 1. 重构 DeviceInfoWidget 的 UI 结构

修改文件：

- `src/plugins/core/deviceinfowidget.h`
- `src/plugins/core/deviceinfowidget.cpp`
- `src/plugins/core/CMakeLists.txt`

动作：

- 去掉 `deviceinfowidget.ui` 和 `Ui::DeviceInfoWidget` 依赖，改为 `buildUi()` / `buildLayouts()` 纯代码建树
- 把温度从当前 `centerWidget` 拆成独立 `temperatureGroup`
- 把详情字段并入独立 `detailGroup`
- 把 `bandwidth + warnningStatus` 合并成独立 `statusGroup`，但是两者可分别控制，目前的策略是compact模式下隐藏 `warnningStatus`，保留 `bandwidth`
- 把中间 spacer 改成自然可收缩的 stretch
- 为关键子控件恢复现有 `objectName`，确保主题样式不回归
- 新增 `DeviceInfoDisplayMode`、当前模式字段和 `applyDisplayMode()`

预期结果：

- `DeviceInfoWidget` 在 `Compact` 模式下可以稳定只显示“连接状态 + 温度”
- 不需要脱离 `QStatusBar`
- 不再受 `.ui` 固定树结构牵制

### Step 2. 给 DeviceInfoWidget 增加响应式切换逻辑

动作：

- 增加阈值常量和滞回常量
- 增加 `updateResponsiveDisplayMode(int availableWidth)` 或无参版本
- 模式变化时调用 `updateGeometry()` 和必要的布局刷新

预期结果：

- status bar 宽度缩小时，`DeviceInfoWidget` 先切到 `Compact`
- 重新放大后再恢复 `Full`
- 临界点附近不会快速来回切换

### Step 3. 重构 CommonPanel 的布局结构

修改文件：

- `src/plugins/core/commonpanel.h`
- `src/plugins/core/commonpanel.cpp`
- `src/plugins/core/CMakeLists.txt`

动作：

- 去掉 `commonpanel.ui` 和 `Ui::CommonPanel` 依赖，改为 `buildUi()` / `buildLayouts()` 纯代码建树
- 顶层改单行 `QHBoxLayout` 为可双态重排的结构
- 拆出 `valueRow` 和 `buttonRow`
- 去掉构造里的永久 `setFixedHeight(60)`
- 为 `btnRf`、`btnMod`、`sweep`、`deviceSettings` 恢复现有 `objectName`
- 新增 `CommonPanelLayoutMode`、当前模式字段、`applyLayoutMode()`

预期结果：

- `CommonPanel` 具备“保持单行”与“切到双行”两套确定布局
- 布局结构完全由代码掌控，不再被 `.ui` 静态树反向限制

### Step 4. 给 CommonPanel 增加响应式切换逻辑

动作：

- 引入阈值和滞回常量
- `Wide` 模式下恢复 `freq / level` 的原上限
- `Compact` 模式下放开 `freq / level` 的最大宽度
- `Compact` 模式下把 `buttonRow` 放到底部，按钮保持固定 `100px`
- 模式切换后刷新 geometry

预期结果：

- 顶部第一行可利用整行宽度给 `freq / level`
- 第二行只承担四个按钮，不再挤占第一行宽度

### Step 5. 在 MainWindow 增加统一响应式调度

修改文件：

- `src/plugins/core/mainwindow.h`
- `src/plugins/core/mainwindow.cpp`

动作：

- 新增 `scheduleResponsiveUiUpdate()` / `updateResponsiveUi()`
- 在 `resizeEvent()` 或等价的统一入口中触发 queued 更新
- 初始化完成后补触发一次
- 更新顺序固定为：
  1. `DeviceInfoWidget`
  2. `CommonPanel`

预期结果：

- 可以明确保证“先 compact status bar，再切 CommonPanel 双行”
- 响应式逻辑集中，不会分散在多个控件里互相打架

### Step 6. 校准阈值

这一步不要一开始就写死拍脑袋常量，建议按下面顺序校准：

1. 先让 `DeviceInfoWidget` 的 compact 切换明显早于 `CommonPanel`
2. 再观察 `CommonPanel` 切换前，`freq / level` 是否已经开始出现肉眼明显截断
3. 再微调两个阈值之间的间隔，直到：
   - status bar 能先释放宽度
   - CommonPanel 能稳定进入双行
   - 放大时两者能顺序恢复

## 验收标准

### 1. 缩小窗口时

- `DeviceInfoWidget` 先进入 compact，只剩连接状态和温度
- 主窗口还可以继续缩小
- `CommonPanel` 在下一档宽度进入双行
- 双行后 `freq / level` 明显比原单行模式拥有更多可显示宽度

### 2. 放大窗口时

- `CommonPanel` 先从双行恢复单行
- 再进一步变宽后 `DeviceInfoWidget` 恢复完整显示
- 恢复过程无明显抖动、无布局错乱、无控件重叠

### 3. 业务行为不变

- `RF / MOD / Sweep / Device Settings` 的点击行为不变
- `freq / level` 的属性绑定和软键盘逻辑不变
- `DeviceInfoWidget` 的状态刷新、温度刷新、warning 队列逻辑不变，只改变显示模式

## 风险与注意事项

### 1. compact 模式下 warning 将不可见

这是用户当前方案的直接结果，因为要求“除了连接状态、温度外全部隐藏”。

需要明确接受的语义是：

- warning 队列仍然照常维护
- 但在窗口过窄时，warning 不显示

如果后续认为这会影响排障，可以再单独设计“compact 模式下只保留一个短 warning 提示”的折中方案，但这不是本次第一版计划的目标。

### 2. 不要只改 CommonPanel，不改 DeviceInfoWidget

如果只做 `CommonPanel` 双行，而底部 status bar 仍保持完整详情，主窗口有可能先被 `DeviceInfoWidget` 卡住，导致 CommonPanel 的阈值根本到不了。

### 3. 不要只改布局位置，不改 freq / level 上限

如果不放开 `freq / level` 的最大宽度，折到双行后的收益会很有限，达不到“让数值有明显更多显示空间”的目标。

### 4. 不要只靠 resizeEvent 里直接同步重排

主窗口 resize 时，子控件几何有时还没稳定；建议用 queued update 或单次延迟调度，在布局完成后统一判断，避免用到旧宽度。

### 5. 不要保留 `.ui` 和代码布局并存

如果最终决定纯代码布局，就不要再让 `.ui` 继续留在 CMake 里参与生成，否则后续维护很容易变成：

- 树结构改了代码但忘了删 `.ui`
- 新同事打开 Designer 看到旧布局，以为它仍然是真实来源

这类双源维护的成本，会比这次一次性去掉 `.ui` 更高。

## 实施优先级

建议顺序：

1. 先做 `DeviceInfoWidget` 拆组和 compact/full 切换
2. 再做 `CommonPanel` 双态布局
3. 最后加 `MainWindow` 协调与阈值校准

原因：

- 先解除 status bar 宽度链，才能更真实地看到 CommonPanel 的切换边界
- 否则前两步会互相干扰，阈值很难调准

## 已实施结果

### 1. CommonPanel

- 已去掉 `.ui` 依赖，改为纯代码布局。
- 已新增 `CommonPanelLayoutMode { Wide, Compact }`。
- `Wide` 模式保持原先单行语义，`Compact` 模式切为“数值行 + 按钮行”两行。
- `freq / level` 在 `Wide` 下保留原先上限，在 `Compact` 下放开最大宽度。
- 原有样式依赖的 `objectName` 已保留：`freq`、`level`、`btnRf`、`btnMod`、`sweep`、`deviceSettings`。

### 2. DeviceInfoWidget

- 已去掉 `.ui` 依赖，改为纯代码布局。
- 已将显示结构拆为 `statusGroup`、`detailGroup`、`temperatureGroup` 等可独立控制区域。
- 已新增 `DeviceInfoDisplayMode { Full, Compact }`。
- `Compact` 模式下仅保留连接状态和温度，`status/detail` 整组隐藏。
- `deviceState` 等关键样式入口已保留，主题样式可继续命中。

### 3. MainWindow 协调调度

- 已新增 `resizeEvent()` 响应式刷新入口。
- 已新增 `scheduleResponsiveUiUpdate()`，使用 queued single-shot 避免在子控件几何未稳定时误判。
- 已新增 `updateResponsiveUi()`，固定按“先 DeviceInfoWidget，再 CommonPanel”的顺序调度。
- 已在 `initialize()` 和 `onInitializationDone()` 末尾补充首轮调度。
- 阈值常量和注释已落在 `mainwindow.cpp`，便于后续手工调参和运行时观察。


## 2026-04-16 commonpanel样式收敛补充

- 继续分析后确认，上述三列 stretch 方案仍可能把可用宽度按比例分给 spacer 列，导致 value 区在空间足够时依然拿不到完整旧设计宽度。
- 最终修复策略：
  - 回到更接近旧版的两列结构：左侧 `valueRowWidget`、右侧 `buttonRowWidget`。
  - 保留宽模式下两组分别左贴、右贴的布局语义。
  - 为 `valueRowWidget` 增加“首选宽度提示”能力：宽模式下 `sizeHint.width = 243 + 150 + spacing`，紧凑模式下取消该提示。
  - 这样 Qt 在空间足够时会优先把 value 区放到旧设计宽度；而由于 minimum size 仍不被抬成 fixed width，缩小时又不会重新阻塞两行模式。

## 2026-04-16 DeviceSettingPanel 宽度瓶颈修正

- 继续实测后，当前“主要横向瓶颈”判断需要再修正一次：在本轮代码状态下，`OFDM` 不是最主要限制项，真正先卡住主窗口缩窄的是 `DeviceSettingPanel`（实现文件为 `devicesettingdialog.cpp`，运行时类名是 `DeviceSettingPanel`）。
- 现象：该页面当前最小宽度实测仍在 `600px+`，已经高于本轮讨论中的 `CommonPanel` 紧凑目标，也高于此前关注的 `OFDM` 页收缩区间，因此它才是当前阶段更靠前的宽度瓶颈。
- 根因更偏向**容器结构**而不是单个按钮：
  - 顶层 `rootLayout` 是 `2 x 2` 分布，左右两列同时参与横向约束。
  - `ReferenceClock`、`Trigger In`、`Trigger Out` 三个 group 内部又各自使用 `2` 列 `QGridLayout`。
  - 运行时效果等价于“外层两列 group + 内层双列字段”的四列语义，而不是单纯几个按钮的自然换行。
- 结合当前实现，`EnumTextButton / LabelButton` 虽然都被限制了 `maximumWidth = 160`，但 group 间距、group 内双列、顶层双列共同叠加后，页面整体仍然会把左侧内容区最小宽度抬到大约 `600px+`。
- 因此目前更准确的结论应为：
  - `CommonPanel` 已不再是第一瓶颈。
  - `OFDM` 仍可能偏宽，但不是当前最先触发的主限制项。
  - `DeviceSettingPanel` 的四列组布局才是当前主窗口横向最小宽度的主要来源之一，且优先级高于本轮此前记录的 `OFDM` 判断。
- 曾尝试过一版 `FancyTabWidget` 托管页统一“可压缩按钮”策略：让 `InfoButton` 体系控件在横向布局中忽略原始 `sizeHint`，并显式允许 `minimumWidth = 0`。
- 该尝试已回滚。原因不是实现错误，而是产品语义不可接受：按钮宽度继续收缩后会触发 `LabelButton` 现有的省略逻辑，DeviceSettingPanel中这类关键字段标题被截断，用户无法稳定区分参数含义。
- 后续可接受方向应转向结构性方案，例如：仅对个别页面重排为不同断点布局、把长标题拆分为更稳定的两行语义展示、或只压缩不承载关键语义的控件，而不是全局压缩业务字段标题。
- 后续如果要继续收窄主窗口，优先应从 `DeviceSettingPanel` 做结构性调整，例如：group 在窄宽度下改为单列堆叠、group 内双列改为单列断点布局、或减少同时并排展示的配置块数量；不应再优先尝试通过截断字段标题来换宽度。