# MiniBarWindow 悬浮球交互修订建议

## 结论

如果 `minibar` 的产品目标，从“宿主窗口边上的附着工具条”转成“常驻的快捷入口”，那么你现在提出的“悬浮球 + 点击展开 + 可拖拽 + 失焦半透明”交互，整体上比当前方案更自然。

原因不是它更炫，而是它更符合用户对这类入口的直觉：

1. 收起态是一个清晰的单点入口，认知成本低。
2. 可拖拽后，位置控制权回到用户，而不是强绑定宿主布局。
3. 失焦半透明可以降低遮挡感，适合长驻桌面。
4. 展开后承载少量高频动作，结构上也更像“快捷操作面板”，而不是“缩小版主窗”。

但这也意味着：**问题定义已经变了**。

原方案本质是：

- 宿主拥有位置控制权。
- minibar 是 `owned tool window` 倾向的附着窗。
- 几何规则围绕 `anchorRect` 展开。

新的悬浮球方案本质是：

- 用户拥有位置控制权。
- 宿主只提供建议位置、约束区或协同策略。
- 几何规则围绕“悬浮球当前位置”展开，而不是围绕宿主锚点展开。

所以不建议简单在原文上局部打补丁，而应把当前方案升级为“**两种交互策略并存**”：

1. `HostedAnchor`：保留原来的宿主附着工具条思路。
2. `FloatingOrb`：新增悬浮球思路，优先用于本地优先阶段与真实用户交互验证。

## 对现有方案的核心修改

### 一、目标定义要改

原文中的阶段 A 是：

- 本地独立 `MiniBarWindow`
- 三态：`Hidden / Collapsed / Expanded`
- 折叠态占一个按钮位，展开态向左展开
- 禁止用户拖拽改变位置

如果改成悬浮球交互，建议改成：

- 本地独立 `MiniBarWindow`
- 三态仍保留：`Hidden / Collapsed / Expanded`
- `Collapsed` 的视觉语义改为“悬浮球”
- `Expanded` 的视觉语义改为“悬浮球展开后的快捷操作面板”
- 允许用户拖拽 `Collapsed` 状态下的位置
- 位置持久化到本地配置
- 失焦时自动降低透明度，而不是改变主状态

这里最重要的一点是：**半透明不应该成为第四个窗口状态**，它只是一个视觉强调层。

### 二、设计原则要改

原文中的这些原则需要调整：

原原则：

1. 折叠与展开使用同一个窗口做尺寸切换。
2. “最小化”在产品语义上改名为“收起”。
3. 用户不能拖拽窗口位置。
4. 展开规则固定为“右边缘锚定，向左展开”。
5. 未来默认采用 `owned tool window`，而不是全局置顶。

建议修改为：

1. 仍优先使用同一个顶层窗口做 `Collapsed / Expanded` 切换，避免双窗口同步复杂度。
2. `Collapsed` 是悬浮球，不再等价于“宿主按钮位”。
3. 允许用户拖拽悬浮球；拖拽结果是有效业务配置，需要持久化。
4. 展开方向不再固定为 `ExpandLeft`，而应改为“基于当前悬浮球位置和工作区空间自适应展开”。
5. 失焦半透明、悬停恢复不透明，属于视觉策略，不进入主状态机。
6. `z-order` 不能再只靠 `owned tool window` 语义描述，必须显式定义策略：在宿主之上、全局置顶、普通顶层三者至少要可区分。
7. 如果产品要长期悬浮在其他软件之上，全局置顶应是可配置策略，而不应被写死为唯一策略。

### 三、术语要改

建议在原文术语区新增或替换为：

#### Floating Orb

收起态下的悬浮球入口。它是用户可拖拽的主要交互锚点。

#### Quick Action Panel

由 `Floating Orb` 点击展开的快捷操作面板，用于承载少量高频功能按钮。

#### Placement Policy

窗口定位策略。用于区分：

- 宿主强约束定位
- 宿主建议定位 + 用户可调整
- 完全自由悬浮定位

#### Visual Emphasis

视觉强调状态，用于描述：

- Active：悬停或聚焦，不透明
- Inactive：未悬停且未聚焦，半透明

这个术语要和主状态机拆开，避免把“看起来淡了”误建模成“业务状态变了”。

## 协议需要怎么改

### 一、初始化配置不能再以 anchor 为中心

原始 `MiniBarSessionConfig` 以这些字段为中心：

- `anchorRectLogical`
- `anchorMode`
- `expandDirection`
- `ownerMode`

这套字段适合“宿主按钮位锚定”，不适合“用户拖拽悬浮球”。

建议改成“位置策略 + 推荐初始位置 + 用户持久位置”的模型。

建议结构体改为类似：

```cpp
struct MiniBarSessionConfig
{
    quint32 protocolVersion;
    quint64 hostHwnd;
    quint32 hostDpi;
    QRect   hostWorkAreaLogical;
    QRect   recommendedCollapsedRectLogical;
    QSize   collapsedSizeLogical;
    QSize   expandedSizeLogical;
    QPoint  persistedCollapsedTopLeftLogical;
    quint32 startupCommand;
    quint32 placementMode;
    quint32 zOrderMode;
    quint32 expansionPolicy;
    bool    allowUserDrag;
    bool    autoFadeWhenInactive;
    quint32 activeOpacityPermille;
    quint32 inactiveOpacityPermille;
    bool    persistUserPosition;
    bool    hideWhenHostMinimized;
    bool    trackHostWorkArea;
};
```

说明：

1. `recommendedCollapsedRectLogical` 替代原来的 `anchorRectLogical`。
2. `persistedCollapsedTopLeftLogical` 表示用户上一次拖拽后保存的位置。
3. 真正显示时，优先级应是：用户持久位置 > 宿主推荐位置 > 系统默认角落位置。
4. `hostWorkAreaLogical` 不再表示“必须贴住的锚点”，而表示“宿主希望 minibar 活动的建议工作区域”或约束区域。

### 二、枚举要改

原枚举：

```cpp
enum MiniBarAnchorMode : quint32 {
    AnchorTopRightLocked = 0
};

enum MiniBarExpandDirection : quint32 {
    ExpandLeft = 0
};

enum MiniBarOwnerMode : quint32 {
    NoOwner = 0,
    OwnedToolWindow = 1
};
```

建议改为：

```cpp
enum MiniBarPlacementMode : quint32 {
    PlacementHostLocked = 0,
    PlacementHostSuggestedUserFree = 1,
    PlacementFreeFloating = 2
};

enum MiniBarZOrderMode : quint32 {
    ZOrderAboveHost = 0,
    ZOrderTopMost = 1,
    ZOrderNormalTopLevel = 2
};

enum MiniBarExpansionPolicy : quint32 {
    ExpandAutoFitWorkArea = 0,
    ExpandPreferLeftThenRight = 1,
    ExpandPreferRightThenLeft = 2
};
```

这几个枚举背后的意思是：

1. “放哪儿”不能只由宿主决定。
2. “在谁上面”不能只用 owner 语义表达。
3. “往哪展开”不能再固定死。

### 三、运行时命令也要补充

原命令集合仍然可保留大部分，但至少建议新增：

```cpp
enum MiniBarCommandType : quint32 {
    CommandShowCollapsed = 0,
    CommandShowExpanded = 1,
    CommandHide = 2,
    CommandToggleExpanded = 3,
    CommandSyncSessionConfig = 4,
    CommandHostMinimized = 5,
    CommandHostRestored = 6,
    CommandShutdown = 7,
    CommandResetPosition = 8,
    CommandSetZOrderMode = 9
};
```

其中：

1. `CommandResetPosition` 用于回退到宿主推荐位置或默认位置。
2. `CommandSetZOrderMode` 用于宿主运行时切换“只压在宿主上方”还是“全局置顶”。

## 状态机需要怎么改

### 一、主状态机其实可以继续保留三态

这是原方案里最值得保留的一点。

主状态仍然可以是：

1. `Hidden`
2. `Collapsed`
3. `Expanded`

只是语义要重命名理解：

1. `Hidden`：完全隐藏。
2. `Collapsed`：悬浮球可见。
3. `Expanded`：快捷操作面板展开。

### 二、要增加两个“正交子状态”，但不要升格为主状态

建议新增：

```cpp
enum MiniBarVisualEmphasis {
    EmphasisActive,
    EmphasisInactive
};

enum MiniBarInteractionSubState {
    InteractionIdle,
    InteractionDragging
};
```

用途：

1. `MiniBarVisualEmphasis` 负责透明度表现。
2. `MiniBarInteractionSubState` 负责拖拽过程中的鼠标捕获与几何更新。

这样既保住了三态主状态机的简洁性，也能容纳新交互。

### 三、转移规则要改

建议在原状态表基础上补这些事件：

| 当前状态 | 事件 | 下一个状态 | 说明 |
| --- | --- | --- | --- |
| `Collapsed` | `HoverEnter` | `Collapsed` | 仅把 emphasis 切到 `Active` |
| `Collapsed` | `HoverLeave` | `Collapsed` | 若未聚焦，emphasis 切到 `Inactive` |
| `Collapsed` | `BeginDrag` | `Collapsed` | interaction sub-state 变为 `Dragging` |
| `Collapsed` | `DragMove` | `Collapsed` | 更新球位置，不改变主状态 |
| `Collapsed` | `EndDrag` | `Collapsed` | clamp 后持久化位置 |
| `Expanded` | `ClickOutside` | `Collapsed` | 面板收回到球 |
| `Expanded` | `FocusOut` | `Expanded` 或 `Collapsed` | 是否自动收回需单独定策略 |

这里要特别说明：

- “失焦半透明”建议只作用在 `Collapsed`。
- `Expanded` 状态如果也直接半透明，体验通常不稳定。
- `Expanded` 更常见的行为不是半透明，而是“外部点击后收回”。

## 几何规则需要怎么改

### 一、Collapsed 几何不再从 anchor 推导

原规则是：

- `Collapsed` 的右上角锚到 `anchorRectLogical` 的右上角。

悬浮球模式下建议改成：

1. 若存在持久化位置，则用持久化位置。
2. 否则若宿主提供推荐位置，则用推荐位置。
3. 否则落在当前屏幕工作区右侧中上区域或右下安全区域。
4. 最终都需要 clamp 到当前屏幕工作区。

### 二、Expanded 几何应围绕球而不是宿主

原规则固定：

- 右边缘对齐宿主锚点
- 顶边对齐宿主锚点
- 向左展开

悬浮球模式建议改成：

1. 以球的当前矩形为展开锚点。
2. 优先向工作区空余更多的一侧展开。
3. 若左右都不足，再考虑向上或向下微调。
4. 展开后仍 clamp 到工作区。

这意味着 `ExpandLeft` 不再是协议常量，而只是默认偏好之一。

## 宿主适配边界要怎么改

如果采用悬浮球交互，宿主程序仍然可以参与适配，但职责要改：

### 宿主仍然适合负责

1. 给出推荐初始位置。
2. 给出建议活动区域或避免遮挡区域。
3. 决定宿主最小化时 minibar 是否隐藏。
4. 根据场景切换 z-order 策略。
5. 在特定页面或特定操作期间要求 minibar 临时隐藏。

### 宿主不再适合强控制

1. 不再持续拥有球的精确坐标。
2. 不再假定展开方向固定。
3. 不再把 `Collapsed` 视为某个固定按钮坑位。

一句话概括：

**宿主从“几何主控者”降级为“协作者”。**

## 实施顺序建议

如果要按这个方向推进，建议实施顺序也要改。

### 第一阶段

先做纯本地悬浮球：

1. `Hidden / Collapsed / Expanded`
2. 点击展开、外部点击收回
3. 失焦半透明
4. 可拖拽
5. 位置持久化

### 第二阶段

再接宿主协同：

1. 宿主推荐初始位置
2. 宿主最小化联动
3. 宿主工作区约束
4. z-order 策略切换

### 第三阶段

最后再讨论进阶行为：

1. 右键菜单
2. 靠边吸附
3. 自动贴边隐藏
4. 多显示器恢复策略
5. 动画与手感优化

## 最后的判断

如果你的目标是：

- 让 minibar 成为一个高频、轻量、跨场景可用的入口；
- 不再强依附某个宿主窗口的按钮位；
- 希望先验证真实可用性，而不是先把 IPC 和 owner 绑定做死；

那么“悬浮球 + 展开面板”的交互，确实比原来的“宿主锚点工具条”更合理。

但更准确地说，它不是原方案的小修小补，而是把原方案从：

- `host anchored utility strip`

改成：

- `user positioned floating entry`

因此最好的文档策略不是直接覆盖旧方案，而是：

1. 保留原文作为 `HostedAnchor` 方案。
2. 新增本文件作为 `FloatingOrb` 修订方案。
3. 后续总方案统一成“MiniBar 支持多种 placement policy 与 z-order policy”。
