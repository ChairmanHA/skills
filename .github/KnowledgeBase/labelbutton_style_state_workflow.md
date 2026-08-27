# LabelButton 样式状态模型与工作流

## 1. 目的

本文档沉淀 SGStudio 中 `LabelButton` / `InfoButton` 双行按钮的样式修改背景知识，重点说明：

- 为什么这类按钮的样式修改比普通 `QPushButton` 复杂；
- 在本仓库中，`RF`、`General Settings`、`Sweep` 三种按钮分别如何承载状态；
- 以后修改这类按钮时，状态应该放在哪一层，谁拥有它，样式如何稳定刷新；
- 在 Copilot 协作里，`instruction / skill / KnowledgeBase` 如何分层配合。

## 2. 控件结构

`LabelButton` 不是一个单层按钮，而是一个复合控件。

相关实现：

- `InfoButton`：上半行 `textLabel` + 下半行 `infoWidget`
- `LabelButton`：把 `infoWidget` 具体化为 `QLabel#infoLabel`

结构来源：

- `src/libs/controls/infobutton.cpp`
- `src/libs/controls/labelbutton.cpp`

因此一个 `LabelButton` 的视觉至少由三部分组成：

1. 按钮本体：背景、边框、hover、pressed、checked
2. `textLabel`：上半部分标题
3. `infoLabel`：下半部分状态字

这也是为什么“改一个按钮样式”往往不是改一条 QSS 就结束。

## 3. 为什么样式修改复杂

### 3.1 它是复合控件，不是单层按钮

普通按钮很多时候只要关心本体背景和文字色；`LabelButton` 要同时关心：

- 按钮本体背景
- `textLabel` 颜色
- `infoLabel` 颜色

如果只改了按钮本体，常见现象就是“背景变了，但上下两行文字没一起变”。

### 3.2 状态来源不止一个

同一个 `LabelButton` 可能同时受这些状态影响：

- `checked`
- `enabled / disabled`
- 当前页 selected
- 运行期临时 `pressed`
- 业务 property 回写
- 主窗口页面切换回写

如果不先把状态语义分清楚，就容易把两个本不该共用的状态塞进同一个载体。

### 3.3 Qt 对子控件样式刷新并不总是自动可靠

在本仓库里，下半行 `infoLabel` 的颜色经常依赖父按钮状态，例如 `parentChecked`。

这意味着：

- 程序里仅仅 `setChecked(true)`，并不保证 `infoLabel` 立刻刷新到目标样式。
- 如果使用动态属性表达当前页高亮，设置属性后也不一定自动把子 label 全部刷新到位。

因此当前仓库在 `CommonPanel` 里保留了显式的样式刷新辅助逻辑，用来统一 repolish 按钮本体、`textLabel`、`infoLabel`。

## 4. 本仓库的三种典型用法

### 4.1 RF 按钮：只有业务 enabled 语义

`RF` 的视觉语义很单纯：

- `checked == true`：表示 RF 已打开
- `checked == false`：表示 RF 已关闭

因此它最适合直接把业务状态放在 `checked` 上。

结论：

- 状态载体：`checked`
- 状态 owner：业务 property / property 回写链
- QSS 重点：`btnRf:checked` 背景 + `infoLabel[parentChecked="true"]`

### 4.2 General Settings 按钮：只有当前页 selected 语义

`General Settings` 只是一个导航入口，没有业务 ON/OFF 含义。

因此它可以直接把当前页 selected 语义映射到 `checked`：

- 当前页是 Device Settings：`checked == true`
- 当前页不是 Device Settings：`checked == false`

结论：

- 状态载体：`checked`
- 状态 owner：`MainWindow` 页面切换回写
- QSS 重点：`deviceSettings:checked` 背景 + 两行文字颜色

### 4.3 Sweep 按钮：既有业务 enabled，又有当前页 selected

`Sweep` 是最容易出错的一种。

它同时有两条语义：

1. Sweep 面板 `Enabled == true`
2. 当前正在查看 Sweep 页面

这两条语义如果都塞进 `checked`，就会打架：

- 进入 Sweep 页面时，你想显示“当前页蓝色高亮”
- 但真正 Enabled 后，你又想恢复“绿色 enabled 样式”

如果都共用 `checked`，后写进去的语义一定会覆盖先前那条。

本仓库当前结论：

- `checked` 继续保留给 Sweep 的业务 enabled 语义
- 当前页 selected 拆到独立动态属性，例如 `pageHighlighted`

结论：

- 业务 enabled 载体：`checked`
- 当前页 selected 载体：动态属性 `pageHighlighted`
- 状态 owner：
  - enabled 由 Sweep 面板 / 业务链回写
  - page selected 由 `MainWindow` 页面切换回写

这是本仓库当前最重要的一条经验：

**当同一个按钮存在两个会并存的状态轴时，不要强行共用 `checked`。**

### 4.4 外部托管 checked：不要 toggle 后再恢复

当按钮满足以下条件时，`checked` 属于 externally-owned 状态：

- `checked` 表达业务/property 的最终状态；
- 用户点击只表达导航或打开页面的意图；
- 点击本身不应改变业务状态。

此时不要让 `QAbstractButton` 先自动翻转 checked，再在 `clicked` 槽中调用 `setChecked(oldState)` 恢复。虽然这两个动作通常发生在同一次事件处理中，但中间仍存在真实的 checked 状态与样式刷新；Windows 可能合并绘制，而 Raspberry Pi 等平台可能把中间帧显示出来，表现为 checked 颜色短暂闪烁。

推荐模式是为这类按钮覆盖 `nextCheckState()`，阻止用户点击自动翻转；业务 owner 仍通过程序化 `setChecked()` 更新最终状态。这样点击信号、导航行为和业务高亮彼此独立，也不需要平台宏或 QSS 补丁。

## 5. 推荐工作流

以后修改 `LabelButton` 样式，建议固定按下面顺序做。

### Step 1. 先分类状态语义

先回答三个问题：

1. 这是业务 enabled / ON-OFF 吗？
2. 这是当前页 selected 吗？
3. 这是控件 disabled / unavailable 吗？

不要一开始就改 QSS。

### Step 2. 决定状态载体

- 只有一个业务开关态：优先 `checked`
- 只有一个导航选中态：可以 `checked`
- 两个状态轴会并存：
  - 核心业务态留给 `checked`
  - 另一个拆到动态属性
- 控件不可用：走 `setEnabled(false)`

### Step 3. 决定状态 owner

- property / business 负责业务态
- `MainWindow` / 页面切换负责当前页 selected
- 点击本身通常只表达意图，不直接拥有最终视觉状态

### Step 4. 同步代码与 QSS

改动通常要同时覆盖：

- 状态设置代码
- 样式刷新链
- 深色主题 QSS
- 浅色主题 QSS

### Step 5. 检查刷新链

程序化改状态后，要确认：

- 按钮本体刷新了没有
- `textLabel` 刷新了没有
- `infoLabel` 刷新了没有

如果依赖诸如 `parentChecked` 这种动态属性，通常需要显式 repolish。

### Step 6. 验证优先级

尤其要验证：

- 初始状态
- 点击切换后的状态
- 页面切换后的状态
- 业务回写后的状态

并明确回答：

**如果两个状态同时成立，谁覆盖谁？**

## 6. Skill 与知识库的分工

针对这类问题，本仓库建议把“流程”和“原理”拆开沉淀。

### 6.1 Skill 负责什么

skill 负责“怎么做”，例如：

- 先分类状态语义
- 决定状态载体
- 决定状态 owner
- 修改哪些代码和 QSS
- 如何检查刷新链

也就是说，skill 更像操作手册或 playbook。

### 6.2 KnowledgeBase 负责什么

知识库负责“为什么这样做”，例如：

- 为什么 `LabelButton` 比普通按钮复杂
- 为什么 `Sweep` 不能把 page selected 与 enabled 都塞进 `checked`
- 为什么程序化 `setChecked()` 后还要补刷新链

也就是说，知识库更像长期背景知识与踩坑总结。

## 7. 用这个例子看 instruction / skill / KnowledgeBase 如何配合

### 7.1 instruction

instruction 负责仓库级常驻规则，例如：

- 先读 KnowledgeBase 索引
- 写代码前先看最新文件内容
- 先写 TaskLog 再动代码
- 只读被 CMake 纳入项目的文件

它解决的是“这个仓库里做事的大框架”。

### 7.2 skill

当用户提出“改 LabelButton 样式”这类固定流程问题时，skill 负责把操作步骤拉起来：

- 分类语义
- 选状态载体
- 检查 `LabelButton / CommonPanel / theme.css / theme_light.css`
- 同步刷新链
- 做最小验证

它解决的是“这类问题的标准处理流程”。

### 7.3 KnowledgeBase

当 skill 需要理解为什么本仓库采用这套流程时，KnowledgeBase 提供背景：

- `RF` 为什么最适合用 `checked`
- `General Settings` 为什么可以把 selected 放进 `checked`
- `Sweep` 为什么必须拆成 `checked + pageHighlighted`

它解决的是“本仓库已知的设计原因与经验”。

### 7.4 三者配合的完整样子

以“修改 Sweep 顶部按钮样式，但不能影响 Enabled 绿态”为例：

- instruction 告诉 Copilot：先写 TaskLog、先读索引、按仓库规则工作。
- skill 告诉 Copilot：这类按钮先分清 enabled / selected / disabled，再决定 `checked` 还是动态属性。
- KnowledgeBase 告诉 Copilot：在本仓库里，`Sweep` 已经验证过不能把两种语义都塞进 `checked`。

因此三者不是重复关系，而是分层关系：

- instruction：常驻约束
- skill：按需流程
- KnowledgeBase：长期知识
