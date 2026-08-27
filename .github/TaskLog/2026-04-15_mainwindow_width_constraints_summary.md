# 主窗口宽度约束总结

## 目标

- 汇总本轮主窗口横向宽度约束的决定性因素。
- 记录当前工作树里已经落地的两条主要收缩手段：
  - 从 `FancyTabWidget` 布局中移除 `m_stackedLayout`
  - 调整 `SwitchButton` 的 size 策略
- 记录右侧调制列表当前的单列/双列切换策略。

## 一、主窗口宽度的决定性因素

主窗口当前不是被单一控件决定宽度，而是被几条约束链叠加后取最大值。

### 1. 中央区域整体公式

在主窗口采用“左列内容区 + 右列调制列表”的布局时，中央区域最小宽度可近似理解为：

`CentralWidget = max(顶部 CommonPanel, 左侧 FancyTabWidget 内容区) + 右侧 modulation dock`

也就是说：

- 只要 `CommonPanel` 仍然比左侧页面宽，左列瓶颈就是 `CommonPanel`；
- 一旦左侧某个业务页/standalone page 更宽，瓶颈就会转移到 `FancyTabWidget`；
- 右侧调制列表宽度则始终以固定目标宽度叠加到整窗横向下限中。

### 2. 历史上的主要瓶颈切换

这轮分析里，瓶颈先后出现过三类：

- `StepSweepPanel -> ListModePanel`：早期最宽链路，直接把 `QStackedLayout` 的最小宽度顶高。目前的业务逻辑反正暂时不可见，就干脆把它从布局里移除，这个目前不是主要问题。
- `CommonPanel + modulation dock`：在 `ListModePanel` 被移出布局后，整窗宽度主要由这一组重新接管。
- `SwitchButton` 主导的调制页：当 `m_stackedLayout` 恢复参与布局时，`AM` / `OFDM` 这类包含 `SwitchButton` 的页面会再次把左列最小宽度顶大。

### 3. `SwitchButton` 为什么是决定性因素

`SwitchButton` 的横向宽度约束来自三层叠加：

1. 外层是 `InfoButton` 的“标题 + 右侧内容区”布局。
2. 内层内容区是两个并排的 `QPushButton`（On/Off）。
3. 主题 QSS 原本对 `SwitchButton QPushButton` 统一写死了 `min-width: 100px`，代码里内部 `QHBoxLayout` 还带左右各 `10px` 边距。

因此：

- 一个 `SwitchButton` 本身就会带来较高的横向下限；
- `AM` 顶部一个 `enabled` 就足以成为页面宽度瓶颈；
- `OFDM` 同一行两个 `SwitchButton`（`btnNullDC` / `btnWindowed`）叠加后更容易成为最宽页面。

## 二、当前通过移除 `m_stackedLayout` 达到的效果

### 1. 当前工作树状态

在当前工作树中，`FancyTabWidget` 构造函数里的下面几行仍然保持注释状态：

```cpp
//hLayout->addLayout(m_stackedLayout);
//hLayout->addWidget(m_dockWidget);
//hLayout->setStretch(0, 0);
//hLayout->setStretch(1, 1);
//hLayout->addLayout(m_stackedLayout, 1);
```

这意味着：

- `m_stackedLayout` 仍然维护页面切换关系；
- 但它当前并不参与 `FancyTabWidget` 的可见布局；
- 因此业务页的 `minimumSizeHint()` 不会继续向主窗口冒泡，主窗口横向可以进一步收缩。

### 2. 直接效果

移除 `m_stackedLayout` 的直接收益不是“页面本身更窄”，而是：

- 左侧业务页完全退出主窗口横向约束链；
- 主窗口当前更像是只受 `CommonPanel` 与右侧 modulation dock 控制；
- 这使得窗口可以明显缩到比“页面参与布局时”更小的宽度。

### 3. 边界

这条做法的本质是把宽页面从主窗口最小宽度计算中摘掉，因此它是最强、最直接的收缩手段；
但它也意味着当前左侧真正的 stacked 页面并未作为正常可见内容区参与布局。

## 三、当前通过修改 `SwitchButton` size 策略达到的效果

### 1. 当前工作树状态

当前 `SwitchButton` 已增加 `compactMode` 属性与对应逻辑：

- `compactMode = true` 时，内部 `QHBoxLayout` 左右边距从 `10` 收缩到 `0`；
- 主题里为 `SwitchButton[compactMode="true"] QPushButton` 覆盖了 `min-width: 0px`；
- `FancyTabWidget` 里增加了统一递归函数，把承载页面中的 `SwitchButton` 切到 compact 模式。

### 2. 这条策略解决了什么

这条修改的核心作用是：

- 不再让 `SwitchButton` 内部两个按钮固定吃掉 `100 + 100` 的最小宽度；
- 在页面宽度缩窄时，允许开关内部按钮自身跟着压缩；
- 从而显著降低 `AM` / `OFDM` 这类页面重新参与布局时带来的横向下限。

### 3. 这条策略和移除 `m_stackedLayout` 的关系

- 如果 `m_stackedLayout` 当前仍然不参与布局，那么 `SwitchButton` 收缩策略不是决定主窗口当前宽度的唯一因素；
- 但它已经为“未来重新让 stacked 页面回到布局”提供了基础，否则一旦恢复页面布局，`AM` / `OFDM` 又会立刻把整窗重新顶宽。

也就是说：

- 移除 `m_stackedLayout` 是当前最直接的整窗收缩手段；
- `SwitchButton compact` 是未来恢复页面布局后避免重新变宽的必要前置条件。

## 四、调制列表当前的单列/双列策略

右侧调制列表当前采用固定宽度 + 滞回切换策略。

### 1. 关键常量

- 单列目标宽度：`125`
- 双列目标宽度：`205`
- 舒适内容宽度阈值：`520`
- 重新扩回双列的滞回量：`24`

### 2. 切换规则

当前逻辑等价于：

- 当列表当前处于双列时：
  - 如果 `FancyTabWidget::width() < 520 + 205`，切到单列；
  - 否则保持双列。
- 当列表当前处于单列时：
  - 只有当 `FancyTabWidget::width() >= 520 + 205 + 24`，才恢复双列；
  - 否则继续保持单列。

### 3. 仅固定 dock 宽度还不够

上面的规则只决定右侧 modulation dock 本身是 `125` 还是 `205`，并不直接决定 dock 内部一定排成几列。

在当前实现里，列表使用的是：

- `QListWidget::IconMode`
- `Adjust`
- `wordWrap = true`
- 未显式设置 `gridSize`

这意味着 Qt 会根据 item 自身的 `sizeHint().width()` 自动决定每个格子的宽度，并在当前 viewport 里尽量塞进更多列。

### 4. 中英文切换时为什么会出现“中文更窄”

中英文差异的关键不在 dock 总宽度，而在 item 的自动宽度计算：

- 不能简单归因于“英文更长、中文更短”，因为像 `AM -> 调幅` 这样的翻译并不符合这个假设；
- 更准确地说，`QListWidget::IconMode` 下 item 的自动 `sizeHint()` 会受翻译文本、字体度量、换行策略和 style hint 共同影响；
- 在同样的 `205` 宽 dock 内，这种自动几何一旦变小，Qt 就可能额外塞进第 3 列；
- 最终表现出来就是：中文不是把右侧列表“顶宽”，而是让自动布局结果变得更窄、更不稳定。

### 5. 当前修正方式

当前修正不改变 dock 的 `125 / 205` 两档宽度，而是新增一层“按英文基准固定 item size / gridSize”的控制：

- 单列宽度时，显式固定与双列一致的 item 宽高，只允许排 1 列；
- 双列宽度时，使用同一套 item 宽高，只允许排 2 列；
- 这样列表内部列数和视觉尺寸将不再跟随语言切换时的自动 `sizeHint()` 波动。
- 并且这套固定逻辑不能只在 dock 宽度变化时执行；首次启动业务列表重建后，即使宽度未变，也必须重新应用到新建 item 上。

### 6. 这样设计的目的

- 右侧调制列表在窗口缩窄时更早进入单列，降低对整体宽度的要求；
- 在宽度临界点附近引入滞回，避免拖拽时在单列/双列之间来回抖动；
- 在已进入某个模式后，用固定 `gridSize` 保证内部列数稳定；
- 单列模式下即使保留部分右侧留白，也优先保证 item 与双列模式拥有一致的视觉宽度；
- 单列时允许纵向滚动条按需出现，双列时关闭纵向滚动条，尽量维持稳定外观。

## 五、当前阶段结论

当前工作树里，主窗口横向已经形成两条互补手段：

1. 通过注释掉 `FancyTabWidget` 内部的 `m_stackedLayout` 布局接入，直接把左侧业务页从整窗最小宽度计算中摘掉。
2. 通过 `SwitchButton compactMode` 收缩页面内最顽固的横向刚性控件，为后续如果重新恢复 stacked 页面布局提供基础。

因此当前的实际状态可以概括为：

- 现在能缩得更窄，主要靠 `m_stackedLayout` 暂时不进布局；
- 以后若要把页面重新接回布局而又不显著反弹，关键前提是保留 `SwitchButton` 的紧凑尺寸策略；
- 右侧 modulation list 的单列/双列切换则是第三条辅助收缩手段，用来减少右列在临界宽度附近的额外占用；
- 而中英文切换造成的“中文更窄”问题，则需要通过固定 item `gridSize` 来解决，不能只靠 dock 总宽度阈值。