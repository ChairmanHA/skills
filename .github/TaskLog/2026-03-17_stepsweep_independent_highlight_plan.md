# 2026-03-17 StepSweep Independent Highlight Plan

## Goal
- 只改 UI 高亮逻辑，不改底层 business active、设备配置、`selectBusiness2Work()` 决策语义。
- 让 `CommonPanel` 上的 `SWEEP` 拥有独立高亮状态，表达“sweep 功能已使能”。
- 让 `StepSweepPanel::enabledChanged(bool)` 镜像驱动 `CommonPanel` 的 `SWEEP` 高亮。
- 显式引入一条独立 UI 语义，用来表达“某个业务是否参与 FancyTabWidget 业务高亮互斥”。
- 保持 `FancyTabWidget` 中普通 business 的使能/高亮互斥逻辑不变。
- 明确暂不做初始化/配置恢复后的补同步；该部分留到后续 business 层改造时统一处理。

## Scope Boundary

### In Scope
- `CommonPanel` 增加 `SWEEP` 独立高亮接口与样式。
- `MainWindow` 在已解析到 `m_sweepBusiness` 之后，把 `StepSweepPanel` 的 `enabledChanged(bool)` 连接到 `CommonPanel` 的新接口。
- 为 FancyTabWidget/TabWidget 引入显式 UI 语义，允许 StepSweep 不参与普通 business 的互斥高亮链。
- 保持 `showSweepPage()` 只负责导航，不负责点亮或熄灭 `SWEEP`。

### Out of Scope
- 不修改 `BusinessManager` 的 active 互斥语义。
- 不修改 `MainWindow::selectBusiness2Work()` 的选择规则。
- 不修改 `FancyTabWidget::selectedBusiness()` 的含义。
- 不补“程序启动后 / loadSettings 后 / reset 后”的首帧状态同步。
- 不给 `SWEEP` 引入新的 property/business 注册体系。

## Current Status

### 1. `SWEEP` 目前只是导航按钮，没有独立状态接口
- `CommonPanel` 仅公开了 `sweepClicked()`，没有类似 `setSweepHighlighted(bool)` 的接口。
- `ui->sweep` 当前在 UI 文件中是普通 `QPushButton`，而不是可复用现有 checked 视觉语义的 `LabelButton`。
- 当前点击 `SWEEP` 只发导航意图，不表达“sweep 已使能”。

相关位置：
- `CommonPanel` 声明：[plugins/core/commonpanel.h](plugins/core/commonpanel.h)
- `sweepClicked()` 连接：[plugins/core/commonpanel.cpp](plugins/core/commonpanel.cpp#L42)
- `ui->sweep` 类型定义：[plugins/core/commonpanel.ui](plugins/core/commonpanel.ui)

### 2. StepSweep 已有可用的 enabled 信号源
- `StepSweepPanel` 已经把内部 `ui->enabled` 的状态变化转发成 `Panel::enabledChanged(bool)`。
- 这意味着 UI 层其实已经有“StepSweep 是否启用”的单一事实来源，不需要新造业务层状态。

相关位置：
- `StepSweepPanel` 定义：[plugins/sweep/stepsweeppanel.h](plugins/sweep/stepsweeppanel.h)
- `enabledChanged` 转发：[plugins/sweep/stepsweeppanel.cpp](plugins/sweep/stepsweeppanel.cpp#L18)

### 3. 普通 business 的高亮目前由 FancyTabWidget 独占，且继续保持互斥
- `FancyTabWidget` 当前用 `Qt::UserRole` 存储“业务已选中/已使能”高亮状态。
- `TabWidget` 通过 `Panel::enabledChanged` 驱动 `setSelected()`，再由 `FancyTabWidget::onBusinessSelectedStateChanged()` 保证业务高亮互斥。
- 这套逻辑适用于 AM/FM/ListMode 等列表内 business，也应该继续保持不动。
- 当前 StepSweep 虽然不显示在列表中，但仍沿用这条 `enabledChanged -> setSelected()` 链，因此它现在依然会抢占普通 business 的互斥高亮位；这是本轮方案后续要显式切断的耦合点。

相关位置：
- 列表项高亮判定：[plugins/core/fancytabwidget.cpp](plugins/core/fancytabwidget.cpp#L96)
- `Panel::enabledChanged -> TabWidget::setSelected`：[plugins/core/fancytabwidget.cpp](plugins/core/fancytabwidget.cpp#L552)
- 互斥高亮核心逻辑：[plugins/core/fancytabwidget.cpp](plugins/core/fancytabwidget.cpp#L357)

### 4. `showSweepPage()` 目前只切页，这个职责是正确的
- `MainWindow::showSweepPage()` 通过 `FancyTabWidget::setCurrentWidgetByBusiness(m_sweepBusiness)` 切到 sweep 页面。
- 它不参与高亮，也不应该在本次改造中承担高亮职责。

相关位置：
- `showSweepPage()`：[plugins/core/mainwindow.cpp](plugins/core/mainwindow.cpp#L1423)

## Current BusinessManager / MainWindow Behavior

### `BusinessManager` 现状
- `BusinessManager` 负责 active business 的全局互斥。
- 某个 business 进入 active 后，若不是当前 `activedBusiness`，则会把前一个 active business 关闭。
- 这套语义解决的是“设备当前工作在哪个业务模式”，不是“CommonPanel 某个全局按钮是否高亮”。

相关位置：
- 注册入口：[plugins/core/businessmanager.cpp](plugins/core/businessmanager.cpp#L90)
- active 互斥核心逻辑：[plugins/core/businessmanager.cpp](plugins/core/businessmanager.cpp#L185)

### `MainWindow` 现状
- `MainWindow` 目前同时维护三类连接：
  - `BusinessManager::currentActivedBusinessChanged -> onCurrentBusinessChanged()`
  - `RF/Mod property editingFinished -> selectBusiness2Work()`
  - `FancyTabWidget::currentSelectedBusinessChanged -> selectBusiness2Work()`
  - `CommonPanel::sweepClicked -> showSweepPage()`
- `onInitializationDone()` 会解析并缓存 `m_sweepBusiness`，这是连接 StepSweepPanel UI 信号的合适时机。

相关位置：
- 连接集中位置：[plugins/core/mainwindow.cpp](plugins/core/mainwindow.cpp#L285-L293)
- `onInitializationDone()`：[plugins/core/mainwindow.cpp](plugins/core/mainwindow.cpp#L1379)

### `selectBusiness2Work()` 现状
- `selectBusiness2Work()` 的职责是：根据 `RF`、`MOD` 和 `FancyTabWidget::selectedBusiness()` 的状态，决定哪个 business 进入 active。
- 当前规则：
  1. `RF == false`，进入 `Mute`。
  2. `RF == true` 且存在 `selectedBusiness()` 时，若该 business 的 `activationPrerequisite()` 满足，则激活它。
  3. 若没有可激活业务且 `MOD == false`，进入 `CW`。
  4. 若 `RF == true && MOD == true` 但当前 selected business 不可激活，则回退到 `Mute`。
- 这套规则依赖 `selectedBusiness()` 表达“普通业务高亮/选择”。
- 本次改造不应让 `SWEEP` 进入这条选择链路，否则会把“全局 sweep 高亮”与“business 互斥选择”重新耦合。

相关位置：
- `selectBusiness2Work()`：[plugins/core/mainwindow.cpp](plugins/core/mainwindow.cpp#L1577)

## Why Explicit UI Semantics Are Needed

### 不能直接用“hidden business”推导 UI 行为
- `showInFancyTabWidget()` 的现有语义只是“是否显示在 FancyTabWidget 列表中”。
- 它不应该被扩展解释为“是否参与普通 business 的互斥高亮”。
- 如果把“hidden”直接当成“不进入 `enabledChanged -> setSelected()` 链”的条件，会把两个原本正交的概念绑死：
  - 列表可见性
  - 业务互斥高亮参与权
- 这会让未来出现“隐藏但仍属于普通 business 域”的页面时，规则立即变得含混甚至错误。

### 推荐新增的独立 UI 语义
- 显式增加一个新的 UI 语义接口，用来表达：
  - 是否参与 FancyTabWidget 的业务高亮互斥。
  - 是否允许 `Panel::enabledChanged` 驱动 `TabWidget::setSelected()`。
  - 是否属于 `selectedBusiness()` 这条普通业务 UI 选择链。
- 默认值应保持普通 AM/FM/ListMode 等业务行为不变。
- StepSweep 显式返回 `false`，从普通 business 互斥高亮域中退出。

### 这个接口的角色定位
- 这是过渡期接口，不是最终架构终点。
- 它的价值在于：先把 UI 域拆开，再为后续“StepSweep 不再是普通 business”铺路。
- 等将来 StepSweep 真正退出普通 business 模型后，这个 override 可以自然删除，而不会在代码里留下“hidden 特判”这一类难以理解的历史耦合。

## Design Decision

### 核心判断
- `SWEEP` 高亮必须成为一条独立于 `FancyTabWidget::selectedBusiness()` 的 UI 状态线。
- 它表达的是“RF 打开后 sweep 行为当前已使能”，而不是“当前普通业务选择是谁”。
- 因此，`SWEEP` 不应复用 `FancyTabWidget` 的列表项高亮，不应接管 `selectedBusiness()`，也不应参与普通 business 的互斥逻辑。

### 新增判断
- `SWEEP` 不仅不应显示在 FancyTabWidget 列表中，还不应参与 FancyTabWidget 的 `enabledChanged -> setSelected()` 互斥高亮链。
- 这一点必须通过显式 UI 语义接口表达，而不是通过 `showInFancyTabWidget()` 的副语义推导。

### 为什么不把 `SWEEP` 做成 checked 导航按钮
- 如果把 `ui->sweep` 直接改成 checkable，并在点击时切换 checked：
  - 会把“导航”与“状态显示”耦合在一起。
  - 用户只是进入了 sweep 页面，并不等于 sweep 当前已使能。
  - 后续若从别处关闭 sweep，按钮 checked 状态又需要反向纠正，语义会变脏。
- 所以推荐把 `SWEEP` 的高亮设计为“显式设置的 UI 状态”，而不是按钮点击副作用。

## Implementation Plan

### Step 0. 显式引入独立 UI 语义

#### 0.1 接口目标
- 在普通 business 抽象层增加一个新的 UI 语义接口，名称可后续再定，但语义应稳定表达：
  - 是否参与 FancyTabWidget 业务高亮互斥。
- 默认返回 `true`。
- StepSweep 显式返回 `false`。

#### 0.2 使用位置
- FancyTabWidget/TabWidget 在建立 `Panel::enabledChanged -> TabWidget::setSelected()` 这条连接时，不再仅凭“是否存在 panel”决定是否接入。
- 而是依据这个新接口，决定该 business 是否允许其 panel enabled 状态驱动 FancyTabWidget 的 selected/highlight 语义。

#### 0.3 这样做带来的直接收益
- StepSweep 打开 enabled 时，不会再抢占 AM/FM 的业务高亮位。
- AM/FM 打开 enabled 时，也不会再通过 FancyTabWidget 反向把 StepSweep 的 panel enabled 改掉。
- `CommonPanel::SWEEP` 高亮可以独立存在，不受普通 business 互斥链影响。

#### 0.4 与未来重构的关系
- 这一接口是未来“StepSweep 不再是普通 business，只是 RF 之下的一层全局设置”之前的桥接措施。
- 它先切断 UI 耦合，再为后续从 business 模型中剥离 StepSweep 降低风险。

### Step 1. 给 `CommonPanel` 增加 `SWEEP` 独立高亮接口和样式

#### 1.1 接口层
- 在 `CommonPanel` 新增只表达 UI 状态的接口，例如：
  - `bool sweepHighlighted() const;`
  - `void setSweepHighlighted(bool highlighted);`
- 该接口只负责更新 `ui->sweep` 的视觉状态，不发业务信号，不触发导航，不读写 property。

#### 1.2 控件层
- 推荐把 `commonpanel.ui` 中的 `ui->sweep` 从 `QPushButton` 替换为 `LabelButton`。
- 原因：
  - `LabelButton` 已在 `CommonPanel` 中被大规模使用，视觉体系一致。
  - 它天然支持 checked 状态和现有 Light/Dark 主题样式。
  - 相比给 `QPushButton` 新增一套自定义属性选择器，这条路径改动更小，主题复用更直接。
- 但要注意：即使换成 `LabelButton`，也不要把它当导航 checkable 按钮使用；checked 只由 `setSweepHighlighted()` 驱动。

#### 1.3 样式层
- 若直接复用 `LabelButton:checked` 的主题色即可满足视觉一致性，则不需要新增太多 QSS。
- 若希望 `SWEEP` 和 `btnRf/btnMod` 一样在 `CommonPanel` 中有更明确的专属样式，可以只增补针对 `CommonPanel LabelButton#sweep` 的局部 QSS。
- 样式目标：
  - 未高亮：与普通 global button 保持一致。
  - 高亮：与当前系统中“已使能”的蓝色/高亮态一致，不追求和列表当前页选中色完全等价。

#### 1.4 设计约束
- `setSweepHighlighted()` 不应发 `sweepClicked()`。
- `sweepClicked()` 不应隐式调用 `setSweepHighlighted(true)`。
- 换句话说：导航行为与高亮状态必须双向解耦。

### Step 2. 把 `StepSweepPanel::enabledChanged(bool)` 镜像到 `CommonPanel`

#### 2.1 连接时机
- 在 `MainWindow::onInitializationDone()` 中，`m_sweepBusiness` 已通过 `BusinessManager::getBusinessByName("Freq Level Sweep")` 成功解析，这是建立 UI 镜像连接的最合适位置。

#### 2.2 连接路径
- 通过 `m_sweepBusiness->controlPanel()` 拿到实际 `Panel*`。
- 将其视为 `StepSweepPanel` 或至少 `Panel`，连接：
  - `Panel::enabledChanged(bool) -> CommonPanel::setSweepHighlighted(bool)`
- 这条连接是纯 UI 同步，不调用 `selectBusiness2Work()`，不调用 `setActive()`。

#### 2.3 为什么放在 MainWindow
- `CommonPanel` 属于 core UI。
- `StepSweepPanel` 属于 sweep 插件业务 UI。
- `MainWindow` 正好是当前唯一同时拥有两者的装配层。
- 把这条镜像连接放在 MainWindow，能维持现有依赖方向：
  - sweep 不依赖 CommonPanel 内部结构。
  - CommonPanel 不依赖 StepSweep 类型。

#### 2.4 连接后带来的行为
- 用户在 `StepSweepPanel` 上打开 enabled：`SWEEP` 高亮。
- 用户在 `StepSweepPanel` 上关闭 enabled：`SWEEP` 熄灭。
- 但如果尚未完成 Step 0，这时 StepSweep 仍会通过 FancyTabWidget 旧链路干扰普通 business 高亮。

### Step 2.5. 用新 UI 语义切断 StepSweep 与 FancyTabWidget 互斥高亮链

#### 2.5.1 切断点
- 关键不是禁止 hidden business 一律进入该链，而是只对“明确声明不参与 FancyTabWidget 业务互斥高亮”的业务生效。
- 也就是说，切断条件来自新接口，而不是来自 `showInFancyTabWidget()`。

#### 2.5.2 目标行为
- StepSweep enabled 时：
  - `CommonPanel::SWEEP` 高亮。
  - FancyTabWidget 中当前已高亮的 AM/FM 不受影响。
- AM/FM enabled 时：
  - 普通 business 之间继续互斥高亮。
  - 不再反向清除 StepSweepPanel enabled，也不影响 `CommonPanel::SWEEP` 高亮。

#### 2.5.3 为什么它仍属于 UI 层改造
- 该步骤不修改 `setActive()`、`BusinessManager`、设备配置，也不修改 `selectBusiness2Work()` 的规则本身。
- 它做的只是：把 StepSweep 从 FancyTabWidget 的普通 business UI 互斥链中剥离出去。
- 因此它本质上仍属于 UI 语义拆分，不是 business 层重写。

## Explicitly Deferred
- 不做首次同步：即不在建立连接后立刻读取 `StepSweepPanel::isBtnEnabledChecked()` 回灌 `CommonPanel`。
- 不做配置恢复后的补同步：即不处理 `loadSettingsFile()`、`reset()`、程序启动后已有状态的首帧反映。
- 不做 `SWEEP` 与 `selectBusiness2Work()` 的语义融合。
- 不做 `BusinessManager` / `FancyTabWidget` 数据模型升级。

## Expected UI Result

### 仅完成 Step 1 + 2 时
- `SWEEP` 具备独立高亮能力，但状态拆分尚未完全闭合。
- StepSweep 仍可能经由 FancyTabWidget 旧链路干扰普通 business 高亮。

### 完成 Step 0 + 1 + 2 + 2.5 后
- `SWEEP` 具备独立高亮能力。
- `SWEEP` 的高亮只跟随 `StepSweepPanel enabled`，不跟随页面切换。
- `AM` / `FM` / `ListMode` 等 business 的高亮继续在 FancyTabWidget 内互斥。
- `SWEEP` 高亮与 `AM` 高亮可同时存在，不互相覆盖。

## Validation Focus
- 点击 `SWEEP` 进入页面，但不修改 enabled 时，按钮不应被强制点亮。
- 在 `StepSweepPanel` 上切换 enabled，`CommonPanel` 上 `SWEEP` 应随之亮灭。
- 在仅完成 Step 1 + 2 的阶段，应预期仍存在互相干扰；这不是 bug，而是尚未完成 Step 0 / 2.5。
- 完成 Step 0 / 2.5 后：
  - 启用 AM 后，FancyTabWidget 的 AM 项高亮正常；此时若 StepSweep 仍 enabled，则 `SWEEP` 继续保持高亮。
  - 切换 FM 后，AM 高亮消失、FM 高亮出现；`SWEEP` 高亮不受影响。

## Risks
- 若把 `ui->sweep` 设计成“点击即 checked”，会重新把导航与状态耦合，后续很难收敛。
- 若把 `SWEEP` 继续留在 FancyTabWidget 的 `enabledChanged -> setSelected()` 互斥模型里，会同时污染 UI 高亮域和 `selectBusiness2Work()` 的输入语义。
- 若用“hidden business”直接代替新 UI 语义接口，会把列表可见性和高亮参与权错误绑死，给未来重构留下更大包袱。
- 若提前补初始化同步，容易在尚未改造 business 层之前引入“首帧状态为何如此”的额外歧义；本阶段刻意不做。
