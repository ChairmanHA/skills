# FancyTabWidget 调制列表响应式布局设计

本文记录 `FancyTabWidget` 右侧 modulation list 当前的响应式布局设计，重点说明：

- 为什么它不能只靠一个 `if (width < X)` 的阈值切换；
- 为什么 `MainWindow` 的布局结构会直接影响切换判定；
- 当前单列 / 双列、滚动条、固定 item 几何之间的职责边界；
- 后续修改这块 UI 时哪些量能改，哪些量不能混在一起改。


## 1. 布局结构先说清楚

这块响应式逻辑之所以容易改错，根本原因不是 `QListWidget` 难用，而是**右侧 modulation dock 不在 `FancyTabWidget` 内部布局里**。

当前主窗口中心区在 `MainWindow` 中是两列布局：

```text
左列: CommonPanel + FancyTabWidget
右列: FancyTabWidget::modulationWidget()
```

关键点：

- `FancyTabWidget::width()` 只代表左列宽度；
- 它**不包含**右侧 modulation dock 的宽度；
- 右侧 dock 一旦变窄，左列会立刻反向变宽。

这条结构事实决定了：

- 任何“根据 `FancyTabWidget::width()` 决定 dock 自己多宽”的逻辑，天然都有形成正反馈回路的风险。

## 2. 当前目标

右侧 modulation list 当前需要同时满足四个目标：

1. 主窗足够宽时，使用双列列表，减少纵向长度。
2. 主窗缩窄后，稳定切到单列，不在临界区来回抖动。
3. 单列模式下，当内容很多时允许纵向滚动条出现。
4. 单列模式下，当内容很少根本不需要滚动条时，不要保留多余右侧空带。

一句话概括：

- **列数切换、dock 宽度、滚动条是否出现，是三个相关但不能互相偷懒代替的问题。**

## 3. 当前几何基线

当前实现使用下面这组固定几何常量：

- `kModulationListItemWidth = 100`
- `kModulationItemMinHeight = 100`
- `kModulationListSingleColumnWidth = 125`
- `kModulationListTwoColumnWidth = 205`
- `kModulationContentComfortWidth = 520`
- `kModulationExpandHysteresis = 24`

它们的语义不是同一层：

### 3.1 item 几何基线

- `100 x 100` 是每个 modulation tile 的视觉基线；
- 这个宽高应尽量保持跨语言、跨主题稳定；
- 它不应该因为列表当前是单列还是双列就发生语义变化。

### 3.2 dock 宽度基线

- `205` 表示双列 dock 的目标宽度；
- `125` 表示“单列且需要滚动条”时的目标宽度；
- 单列无滚动条时，允许进一步收缩到 `100`，与 item 宽度对齐。

### 3.3 主窗舒适阈值

- `520` 表示左侧内容区的舒适宽度基线；
- `24` 是从单列恢复到双列时的滞回量，用来避免主窗拖拽时来回抖动。

## 4. 为什么旧做法会闪烁

最容易犯错的旧写法是：

```text
if (FancyTabWidget::width() < 520 + 205) -> 切到单列
else -> 保持双列
```

它的问题不在公式长短，而在它把：

- **判定输入**：`FancyTabWidget::width()`
- **判定输出**：dock 从 `205` 改成 `125`

耦合进了同一条反馈链。

一旦从双列切到单列：

1. dock 宽度从 `205` 变成 `125`；
2. 主窗总宽度不变时，左列会立刻变宽 `80px`；
3. 下一轮判定又读到这个更宽的 `FancyTabWidget::width()`；
4. 结果又满足“恢复双列”条件；
5. 进入 bounce-back 循环。

如果此时 hover 触发 list 重绘，或者纵向滚动条出现导致 viewport 再变化一次，这条回路就更容易被放大成明显闪烁。

因此这里的第一条硬约束是：

- **不要直接用当前左列宽度去判定会反向改写左列宽度的 dock 自身模式。**

## 5. 当前宽度判定的正确思路

当前实现不是直接看 `FancyTabWidget::width()`，而是先把它归一化为一个**以双列 dock 为基准的有效内容宽度**：

```text
effectiveContentWidth = FancyTabWidget::width()
                      + currentDockWidth
                      - twoColumnDockWidth
```

也就是：

- 如果当前就是双列，`effectiveContentWidth` 约等于当前左列宽度；
- 如果当前已经切成单列，就把那部分“因为 dock 变窄而白送给左列的宽度”扣回去；
- 这样读出来的量更接近“如果 dock 仍保持双列时，左内容区实际上有多宽”。

基于这个稳定量，当前规则等价于：

### 5.1 从双列收缩到单列

- 当 `effectiveContentWidth < 520 + 205` 时，切到单列。

### 5.2 从单列恢复到双列

- 只有当 `effectiveContentWidth >= 520 + 205 + 24` 时，才恢复双列。

这条设计的目的不是“数学更漂亮”，而是：

- **切断 dock 改宽后立即反向改写下一轮判定输入的正反馈回路。**

## 6. 列模式不能只靠宽度暗示

另一个常见误区是：

- 只把 dock 宽度从 `205` 收缩到 `125`，然后默认 `QListWidget::IconMode` 会自动收敛成单列。

这在 Qt 里不稳，因为：

- `IconMode`
- `Adjust`
- `wrapping`
- delegate `sizeHint`
- viewport 可用宽度

会共同决定最终列数。

所以当前实现把列模式显式化：

### 6.1 双列模式

- `ResizeMode = Adjust`
- `Flow = LeftToRight`
- `Wrapping = true`

语义：

- 保留网格型多列布局，让 viewport 按当前宽度自动排成两列。

### 6.2 单列模式

- `ResizeMode = Fixed`
- `Flow = TopToBottom`
- `Wrapping = false`

语义：

- 明确告诉 Qt：当前是单列纵向列表，不要再试图换出第 2 列。

这条边界非常重要：

- **单列 / 双列首先是布局模式，dock 宽度只是模式的一个结果，不是模式本身。**

## 7. item 几何为什么仍然固定

当前实现仍然会对每个 `QListWidgetItem` 统一设置：

- 固定 `gridSize`
- 固定 `sizeHint`

目的有两个：

1. 避免不同语言、字体度量、换行策略让 item 自动变窄或变宽。
2. 避免列表重建后，新 item 又回到 Qt 的自动几何路径，导致首次绘制和后续绘制不一致。

所以当前稳定约束是：

- **dock 宽度负责决定能排几列；item 几何负责保证每列里的 tile 长什么样。**

不要把这两个职责混在一起。

## 8. 单列为什么还要再分两种宽度

用户最终确认了一个非常关键的经验值：

- `125` 恰好适合“`100px` item + Windows 纵向滚动条”。

但这不意味着单列永远都该是 `125`。

因为当 item 很少时：

- 单列模式下内容高度并不会超过 viewport；
- 纵向滚动条根本不会出现；
- 这时继续保留 `125` 只会留下不必要的右侧空带。

所以当前单列模式被细分成两种子状态：

### 8.1 单列 + 需要滚动条

- dock 宽度：`125`

### 8.2 单列 + 不需要滚动条

- dock 宽度：`100`

这样单列宽度就能和 item 宽度完全对齐。

## 9. 如何判断单列是否真的需要滚动条

这一步不能依赖“当前滚动条是不是已经显示出来了”，因为那样会形成新的 UI 先后顺序依赖。

当前实现用的是更稳定的内容几何判断：

```text
contentHeight = itemCount * itemHeight + 行间距
if contentHeight > viewportHeight:
    需要纵向滚动条
else:
    不需要纵向滚动条
```

其中：

- `itemHeight` 使用固定 `gridSize().height()` 与最小高度基线；
- `viewportHeight` 直接读 list 当前 viewport；
- 没有 item 时直接视为“不需要滚动条”。

这条逻辑的边界是：

- 它只决定**单列时该用 `100` 还是 `125`**；
- 它**不参与**一列/两列模式切换的主阈值判定。

这点必须严格保持，否则很容易重新引入新的反馈回路。

## 10. 当前刷新顺序

`updateModulationListLayout()` 当前应保持下面这个顺序：

1. 计算目标 dock 宽度。
2. 如果宽度变化，更新 dock/list 的 fixed width。
3. 根据目标模式应用单列或双列的显式布局模式。
4. 重新应用固定 `gridSize` 与 item `sizeHint`。
5. 应用纵向滚动条策略：
   - 双列：AlwaysOff
   - 单列：AsNeeded
6. 刷新 item 布局与 viewport 几何。

这里的要点是：

- **模式、尺寸、滚动条策略、几何刷新必须作为一个完整事务一起重放。**

不要只改其中一步然后指望 Qt 自己补齐剩下几步。

## 11. 允许改的量与不建议改的量

### 11.1 可以改的量

- `kModulationContentComfortWidth`
- `kModulationExpandHysteresis`
- `kModulationListTwoColumnWidth`
- `kModulationListSingleColumnWidth`
- 单列无滚动条时的 `100px` 宽度

但每次改这些量时，都要连带验证：

- 主窗最小宽度；
- 单列触发点；
- 单列恢复双列的回程点；
- 单列有/无滚动条两种子状态；
- 中英文下 item 几何是否仍稳定。

### 11.2 不建议轻易改的边界

- 不要把单列/双列重新退回成“只靠 dock 宽度暗示”。
- 不要把 item 宽度改成跟随 dock 宽度自动浮动。
- 不要把“是否需要滚动条”的判断混进一列/两列主阈值里。
- 不要重新直接用 `FancyTabWidget::width()` 去判定 dock 自己的列模式。

## 12. 修改这块 UI 时的检查清单

以后如果还要改这块响应式 UI，至少逐项检查下面这些问题：

1. `MainWindow` 里 modulation dock 是否仍然是独立右列，而不是重新并回 `FancyTabWidget` 内部布局。
2. 一列/两列的模式判定是否仍然基于稳定量，而不是直接读取会被 dock 自己反向改写的宽度。
3. 单列模式是否仍然是显式的 `TopToBottom + Fixed + no wrapping`。
4. item 的 `gridSize` / `sizeHint` 是否仍然在每次 rebuild 后被重新应用。
5. 单列无滚动条时是否确实能收缩到 `100px`，而不是白白保留 `125px` 空带。
6. 单列有滚动条时是否仍然保留 `125px`，避免 item 与滚动条互相挤压。
7. hover、主题切换、列表 rebuild 后是否仍然不会触发列数闪烁。

## 13. 结论

当前稳定设计可以浓缩成三句话：

1. **一列/两列切换由主窗宽度阈值决定，但判定必须使用不会被 dock 自己反向污染的稳定量。**
2. **单列/双列必须是显式布局模式，不能只靠缩放 dock 宽度去暗示 Qt。**
3. **单列模式下再按是否真的需要纵向滚动条，在 `100` 和 `125` 之间细分宽度。**

后续只要保持这三条边界，修改视觉参数时就不容易再次掉回“能切但会抖”“能收但收不进去”“宽度对了但列数不对”的旧坑里。