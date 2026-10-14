# Auto Mod 用户意图边界与 FancyTabWidget 信号职责

本文记录当前 Auto Mod 偏好的稳定设计边界，重点说明三件事：

- Auto Mod 属于哪一层语义。
- FancyTabWidget 当前哪些信号代表“用户主动开关业务”。
- 为什么不能把“用户主动关业务 -> 关 Mod”挂在 `currentSelectedBusinessChanged(nullptr)` 上。

本文描述当前代码现状。2026-10-13 增加 Instrument 外部 MOD 直接使能 Home Waveform 的入口；面板内部 Auto Mod 策略和桌面 main 行为保留。

## 1. 先说结论

当前 Auto Mod 必须被理解成一条 **UI 用户动作增强策略**，而不是：

- pipeline / request 状态；
- profile 持久化状态；
- selected business 的通用同步策略。

因此当前稳定基线是：

1. 用户主动打开一个 `providerKind() != None` 的业务时，如果 Auto Mod 偏好开启，则自动把公共 `Mod` 打开。
2. 用户主动关闭当前这个业务时，如果 Auto Mod 偏好开启，则自动把公共 `Mod` 关闭。
3. 配置恢复、preset/reset、程序化选中/取消、页面切换、selected business bookkeeping 都不能被重新解释成“用户主动开关业务”。

一句话概括：

- **Auto Mod 只绑定 user-initiated business enable/disable，不绑定广义的 selected business 变化。**

## 2. Auto Mod 当前落在哪一层

当前实现落在：

- `FancyTabWidget`：负责从 UI 选择链里区分“用户主动动作”和“程序化状态同步”；
- `MainWindow`：在收到用户主动动作后，根据偏好和 business scope 同步公共 `Mod` property；
- `MainWindow::updateSweepAndBusinessAvailability()`：统一裁决 MOD availability；main 看已启用基带，Instrument 看 Home 展示的 Waveform 与其原使能前提；
- 后续业务裁决仍然走原有 `selectBusiness2Work() -> updateOrchestrator()` 链。

这意味着 Auto Mod 不直接参与：

- `TxOrchestrator` 的规则本体；
- `TxApplyRequest` 的持久化；
- runtime / device 直接调用；
- `Profile.json` 的业务配置语义。

## 3. 当前调用链

以列表内普通业务为例，当前链路是：

```text
Panel::enabledChanged(bool)
  -> TabWidget::setSelected(bool)
     -> emit selectedStateChanged(actualSelected, sender() == panel)
        -> FancyTabWidget::onBusinessSelectedStateChanged(selected, userInitiated)
           -> emit businessEnabledByUser(...) / businessDisabledByUser(...)
           -> emit currentSelectedBusinessChanged(...)
              -> MainWindow::selectBusiness2Work()
                 -> TxSessionService::requestRefresh()
```

这里有两个关键点。

### 3.1 `sender() == panel` 是当前唯一可靠的“用户动作来源”判定

`TabWidget::setSelected()` 当前会把：

- `sender() == panel`

编码进 `selectedStateChanged(bool selected, bool userInitiated)` 的第二个参数里。

这条信息非常关键，因为它把：

- 用户点击 panel enabled 开关；

和：

- 程序调用 `setSelected()` / `setBtnEnabledChecked()` / 状态恢复；

明确区分开了。

### 3.2 `MainWindow` 不直接改 runtime，只同步公共 `Mod` property

面板内部的 Auto Mod 路径只在收到：

- `businessEnabledByUser(IBusiness *)`
- `businessDisabledByUser(IBusiness *)`

时，根据下面三条规则同步公共 `Mod`：

1. Auto Mod 偏好已开启。
2. `business != nullptr`。
3. `business->providerKind() != BasebandProviderKind::None`。

随后仍然由 `currentSelectedBusinessChanged(...)` 触发原有裁决链，不会直接调用 runtime / device API。

## 4. 当前信号职责分工

### 4.1 `businessEnabledByUser`

语义：

- 用户主动打开某个 business。

当前用途：

- Auto Mod 偏好开启时，同步把公共 `Mod` 置为 `true`。

### 4.2 `businessDisabledByUser`

语义：

- 用户主动关闭当前已选中的 business。

当前用途：

- Auto Mod 偏好开启时，同步把公共 `Mod` 置为 `false`。

注意这里强调的是：

- **当前已选中业务被用户主动关闭**。

这不是一个通用“业务不再 selected”的广播信号，而是一条更窄的用户动作语义。

### 4.3 `currentSelectedBusinessChanged`

语义：

- FancyTabWidget 当前 selected business 状态发生变化。

当前用途：

- 触发 `MainWindow::selectBusiness2Work()`；
- 更新 provider execution context 的连接绑定；
- 维护上层对“当前业务是谁”的通用感知。

因此它本质上是一条：

- **选择态 bookkeeping / orchestrator 输入更新信号**，

而不是：

- 用户主动开关业务的专用信号。

### 4.4 `updateSweepAndBusinessAvailability`

语义：

- 根据当前选中的 business/provider 能力，统一刷新 CommonPanel 上依赖业务上下文的按钮可用性。

当前用途按 UI 模式区分：

- 无已启用 provider 时，两种模式都让公共 Mod 保持 OFF。
- main 仍要求已启用 provider 才能操作 MOD，保持原流程。
- Instrument 的 MOD OFF 时以用户当前选择的 Waveform 为目标，普通基带无需先使能 Panel；三个文件类型则要求原使能控件可用。MOD ON 时以已启用 Waveform 为目标，仍允许关闭。
- 面板主动使能的自动联动继续受 Auto Mod 偏好控制。Instrument 外部 MOD 是明确的用户命令，不依赖该偏好。

这条规则是 **UI availability / 状态收敛**，不是 Auto Mod 偏好本身。

## 5. 为什么不能复用 `currentSelectedBusinessChanged(nullptr)`

这次需求最容易犯错的地方就在这里。

如果把“用户主动关业务 -> 关 Mod”直接挂到：

- `currentSelectedBusinessChanged(nullptr)`

就会把两类完全不同的语义混在一起：

1. 用户真的主动把当前业务关掉。
2. 程序为了维护 selected business 状态、恢复配置、preset/reset、程序化切换页面而发出的 selected-state 变化。

这会带来两个直接问题。

### 5.1 它把 Auto Mod 和 broader selection bookkeeping 耦合在一起

`currentSelectedBusinessChanged` 的职责是让 orchestrator 和上层状态知道“当前选中的 business 变了”。

如果在这里直接开/关 `Mod`，那么 Auto Mod 就不再是“用户动作增强”，而会变成“selected business 广义变化副作用”。这会污染：

- 配置恢复；
- preset/reset；
- 程序化选中/取消；
- 未来任何 selected business bookkeeping 调整。

### 5.2 它会威胁“打开新业务时自动关闭旧业务”的现有链路

普通业务当前仍保持互斥高亮 / 互斥选择语义。

切换到新业务时，旧业务的关闭是框架内部的选择同步动作，不应被重新解释成“用户要关 Mod”。

当前代码专门通过两层手法隔离这件事：

1. `TabWidget::setSelected()` 只把 `sender() == panel` 视为 `userInitiated=true`。
2. `FancyTabWidget::onBusinessSelectedStateChanged()` 在切换新业务时，对旧业务 `prev->setSelected(false)` 使用 `QSignalBlocker`，避免把旧业务关闭再向上广播成新的用户动作语义。

因此面板内部 Auto Mod 的稳定约束是：

- **Auto Mod Off 只能接 `businessDisabledByUser`，不能接 `currentSelectedBusinessChanged(nullptr)`。**

## 6. 当前稳定基线

后续维护时，请把下面这些规则视为硬边界。

### 6.1 可以做的事

- 在 `businessEnabledByUser` / `businessDisabledByUser` 上扩展“用户动作增强策略”。
- 继续把 Auto Mod 偏好保留在 `MainWindow + globalSettings` 这一层。
- 继续让 `currentSelectedBusinessChanged(...)` 只负责 orchestrator 输入更新和上下文连接维护。
- 将模式对应的可用性规则统一放在 `updateSweepAndBusinessAvailability()`：main 继续要求已启用 provider；Instrument 按 Home 目标与原使能前提判断。无已启用 provider 时 MOD 状态仍为 OFF。

### 6.2 不要做的事

- 不要把 Auto Mod 的开/关挂到 `currentSelectedBusinessChanged(...)`。
- 不要把 Auto Mod 语义塞进 `TxOrchestrator` / `TxApplyRequest` / runtime。
- 不要把 Auto Mod 理解成 profile restore 语义的一部分。
- 不要把“selected business 变化了”直接等价成“用户主动开/关了业务”。

## 7. 修改这条链路时的检查清单

如果以后还要改 FancyTabWidget / Auto Mod / business 选择语义，至少检查下面几项：

1. 用户主动打开业务时，Auto Mod 是否只在 `providerKind() != None` 范围内生效。
2. 用户主动关闭当前业务时，是否只通过 `businessDisabledByUser` 触发关 `Mod`。
3. 切换到新业务时，旧业务关闭是否仍然不会误触发 Auto Mod Off。
4. 配置恢复、preset/reset、程序化 `setBusinessSelected()` 是否仍然不会被重新解释成用户动作。
5. MOD availability 是否仍在 `updateSweepAndBusinessAvailability()` 收口，并区分 main / Instrument 的选定规则。
6. `MainWindow::selectBusiness2Work()` / `TxSessionService::requestRefresh()` 是否仍然保持为唯一裁决入口，而不是在 Auto Mod 逻辑里直接下发 runtime。

## 8. 结论

当前 Auto Mod 的核心不是“自动帮用户改了一个 Mod 开关”，而是：

- 在不污染既有业务裁决链的前提下，给“用户主动开关业务”补一层很窄、很明确的 UI 语义增强。

所以真正需要保护的不是某一行 `setValue(true/false)`，而是这条边界：

- `businessEnabledByUser` / `businessDisabledByUser` 表达用户意图；
- `currentSelectedBusinessChanged` 表达选择态变化；
- 二者不能混用。

## 9. Instrument 外部 MOD 请求与 Home 概要

Instrument CommonPanel 的 MOD 点击不先翻转 checked 或写公共 Mod，而是发出 instrumentModulationRequested。MainWindow 以 InstrumentHomeWidget::waveformBusiness() 为唯一目标来源，通过原 Panel::triggerBtnEnabledChecked() 执行用户使能入口，保留互斥选择、文件约束及确认。

Panel::isBtnEnabledAvailable() 表达原使能控件是否接受 ON。Playback、QuickWaveform、Streaming 直接读取已有控件可用性，文件加载/清空后发 enabledAvailabilityChanged；MainWindow 不按业务名称或文件字段复制规则。

外部请求由一条 MainWindow context 的 enabledChanged 完成连接等待实际回写。Streaming 取消时发布恢复状态；Digital 当前大波形告知提示关闭后沿用原提交流程。完成连接在接受/取消、替换请求或对象销毁时结束，成功后才按实际 selectedBusiness 设置 Mod。外部显式命令不改变 Auto Mod 偏好，也不把页面切换解释为用户使能。

| 当前状态 | Home 概要 / 点击进入 / 外部 MOD 的目标 | 概要状态 |
| --- | --- | --- |
| MOD ON，已启用 A，正在编辑 B | A | ON |
| MOD OFF，当前选择 B | B | OFF，即使另一 Panel 仍有选择状态 |
| B 未加载所需文件或取消确认 | B 保持未使能 | OFF |

概要继续读取目标 Panel 的原 LabelButton/EnumTextButton 格式化参数，新增 LabelButton::labelTextChanged 的通用值通知，不依赖隐藏 Panel 的布局刷新；参数、状态、目标切换与回 Home 时同步。状态表达配置使能，不表示 RF 已开启或执行已完成。

本轮仅静态检查，未编译/运行；实施和待验收场景见 [TaskLog](../TaskLog/2026-10-13_fix-instrument-mod-waveform-link.md)。
