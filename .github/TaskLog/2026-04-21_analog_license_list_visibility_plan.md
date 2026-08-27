# Analog License List Visibility Plan

## Goal

把 Analog 许可证交互从“切设备立即全量禁用 panel”调整为更稳定的两层模型：

- 程序启动且尚未完成任何一次设备许可证判定时，视为 `Unknown`：
  - modulation list 保持完整显示；
  - Analog panel 允许浏览和编辑；
  - 仅禁用 `Save Data` 相关状态。
- 只有在设备 **open 成功** 并拿到真实 `model/uid` 后，才执行许可证判定，并据此决定是否从 modulation list 中移除 Analog 业务项。
- 设备切换中、连接中、断开后、无设备时，都 **不修改 list**，避免闪烁和反复重排。

用户确认后的最终交互约束如下：

1. 启动时 `Unknown`，显示全部 Analog item。
2. `Unknown` 阶段 Analog panel 只需要禁用 `Save Data`，不需要整 panel `setEnabled(false)`。
3. 设备 open 成功后，根据许可证结果决定是否 remove Analog items。
4. list 只在“post-open 判定结果落定”的时刻更新。
5. 设备断开或无设备时，不回滚 list，避免闪烁。

---

## Core Decision

必须把“许可证运行时状态”和“列表展示状态”拆开，不能再用一个 `bool` 同时表达两者。

### 1. Runtime validation state

建议在 `DeviceUtils::DeviceParamsManager` 中新增：

```cpp
enum class LicenseValidationState {
    Unknown,
    Licensed,
    Unlicensed
};
```

语义：

- `Unknown`
  - 启动后尚未完成首次设备许可证判定；
  - 或当前设备切换/连接过程中，旧设备判定结果已经失效，但新设备结果尚未返回。
- `Licensed`
  - 当前设备已 open，且 `model/uid` 校验通过。
- `Unlicensed`
  - 当前设备已 open，但 `model/uid` 校验失败。

### 2. List presentation state

list 展示状态不要直接跟着 `Unknown` 变化，而是保留“最后一次已落定的展示决策”。

可实现为一个简单规则，而不一定要单独建 enum：

- 默认初始展示：`AllVisible`
- 当收到一次 `Licensed` 结果：展示所有 Analog item
- 当收到一次 `Unlicensed` 结果：隐藏所有 Analog item
- 当状态回到 `Unknown`：**不改动当前 list 展示**

这正是避免闪烁的关键。

一句话概括：

- `Unknown` 只影响 Analog panel 内部交互状态；
- `Licensed/Unlicensed` 才影响 modulation list 的可见性。

---

## Event Policy

### 1. App startup, no device yet

- `FixedLic=false`
  - `LicenseValidationState = Unknown`
  - list = 全显示
  - Analog panel = 可编辑，`Save Data` 禁用
- `FixedLic=true`
  - 可以直接视为 `Licensed`
  - list = 全显示
  - Analog panel = 按已有数据 ready 状态恢复 `Save Data`

### 2. current device changed / openStateChanged(false)

这类事件表示：

- 设备切换开始；
- 或正在连接；
- 或旧设备已失效；
- 但还没有得到新设备最终身份。

此时策略：

- `LicenseValidationState -> Unknown`
- list 不变
- Analog panel `Save Data` 禁用
- 不在这个时刻 remove/re-add list item

### 3. device open success / deviceConnected

这是唯一允许更新 list 的时刻。

- 读取 `model/uid_h32/uid_l64`
- 做许可证校验
- 如果通过：
  - `LicenseValidationState -> Licensed`
  - show all Analog item
- 如果失败：
  - `LicenseValidationState -> Unlicensed`
  - hide all Analog item

### 4. device disconnected / no current device

此时策略：

- runtime state 可以回到 `Unknown`
- 但 list 保持“上一次已落定结果”
- 不做 remove/re-add

这意味着一个明确的产品取舍：

- 如果上一次验证结果是 `Unlicensed`，随后设备断开，Analog item 仍保持隐藏；
- 直到下一次设备 open 成功并完成许可证判定，list 才会重新变化。

这是用户已确认接受的稳定性优先策略。

---

## Why The Old Bool Is No Longer Enough

当前 `DeviceParamsManager::m_waveformGenerationAvailable` 是一个 `bool`，它在语义上混合了：

1. 当前设备是否已经有可用身份；
2. 当前许可证是否通过；
3. Analog UI 是否可用；
4. list 是否应该变化。

在旧方案里这还勉强成立，因为 false 基本都走“整体禁用”。

但新交互里：

- `Unknown` 和 `Unlicensed` 都可能映射成 `false`；
- 但它们的 UI 行为完全不同：
  - `Unknown`：panel 保持可见且 list 不动，只禁用 save；
  - `Unlicensed`：Analog item 从 list 隐藏，必要时关闭当前 Analog 选择。

因此必须显式建模三态，不能继续把所有决策挂在一个 `bool` 上。

---

## Component Plan

## 1. DeviceParamsManager

目标文件：

- `src/plugins/analog/deviceutils.h`
- `src/plugins/analog/deviceutils.cpp`

### 1.1 New API

建议新增：

```cpp
enum class LicenseValidationState {
    Unknown,
    Licensed,
    Unlicensed
};

LicenseValidationState licenseValidationState() const;
bool isLicenseConfirmed() const;
bool isWaveformGenerationLicensed() const;
```

信号建议改为：

```cpp
void licenseValidationStateChanged(LicenseValidationState state);
```

旧的 `waveformGenerationAvailabilityChanged(bool)` 可以：

- 删除并让 Analog UI 全部切换到新信号；或
- 暂时保留为兼容 helper，但不再驱动 panel enable/list visibility。

推荐：

- UI 逻辑只看 `LicenseValidationState`
- `bool` helper 只保留给真正需要“是否已明确拿到 license pass”的地方

### 1.2 State transitions

#### Constructor

- `FixedLic=true`
  - 初始 state = `Licensed`
- `FixedLic=false`
  - 初始 state = `Unknown`

#### onCurrentDeviceChanging()

- 清空缓存中的设备参数
- 状态改为 `Unknown`
- 发 `licenseValidationStateChanged(Unknown)`
- **不要在这里触发 list 更新**

#### onDeviceConnected(params)

- 做最终 post-open 校验
- 成功：state = `Licensed`
- 失败：state = `Unlicensed`
- 发 `licenseValidationStateChanged(...)`
- 若失败，通过 `DeviceInfoWidget::addWarnningMessage(...)` 写入 warning
- 当状态回到非失败态时，通过 `removeWarnningMessage(...)` 移除该 warning

### 1.3 Important behavior change

`Unknown` 不再被解释为“整体禁用 Analog panel”。

它只表示：

- 当前没有可确认的许可证结果；
- 这会影响 save/export/runtime-ready；
- 但不会立即影响 modulation list。

---

## 2. AnalogPlaybackBusiness

目标文件：

- `src/plugins/analog/analogplaybackbusiness.h`
- `src/plugins/analog/analogplaybackbusiness.cpp`

### 2.1 Replace bool-driven UI with state-driven UI

当前逻辑在 `refreshPlaybackPanelState()` 中直接做：

- `m_widget->setEnabled(available)`
- `m_widget->onDataStatusChanged(available && m_modulatorDataReady)`

这不再适合新交互。

建议改成三态映射：

#### Unknown

- `m_widget->setEnabled(true)`
- `m_widget->onDataStatusChanged(false)`

效果：

- panel 还能看、还能改参数；
- `Save Data` 禁用；
- 行为与 Streaming / Arb 更接近。

#### Licensed

- `m_widget->setEnabled(true)`
- `m_widget->onDataStatusChanged(m_modulatorDataReady)`

#### Unlicensed

- `m_widget->setEnabled(false)`
- `m_widget->onDataStatusChanged(false)`

说明：

- 虽然 Unlicensed 最终会把 item 从 list 隐藏掉；
- 但在隐藏动作和页面回退的瞬间，仍然建议把 panel 置为不可操作，防止短窗口内继续交互。

### 2.2 Data-ready handling

建议在 `Unknown` 和 `Unlicensed` 时都把：

- pending handover/trim 取消
- `m_modulatorDataReady = false`

原因：

- 这样可以防止上一个已连接设备的旧 ready 状态，在新设备 `Licensed` 后被直接复用；
- 一旦新设备最终变成 `Licensed`，由当前 active/visible 业务重新触发生成即可。

这比“保留旧 ready 但只禁 save”更安全。

### 2.3 Unknown scope

本期 Unknown 只改变：

- panel enable 语义
- `Save Data` 状态
- `m_modulatorDataReady` 的可用性解释

本期 **不额外收敛**：

- 参数编辑行为
- 本地 modulator 的数据生成机制
- 大波形对话框的出现时机

本期 **额外做一层安全保护**：

- `Unknown/Unlicensed` 时不再建立 trim / handover 的 pending-flow 状态；
- 避免 panel 可见但许可证未确认时，用户触发大波形路径后留下 busy/pending 残留。

也就是说：

- Unknown 期间仍允许用户编辑参数；
- 但 `Save Data` 不可用；
- 真正和设备许可证强绑定的能力仍以最终 `Licensed/Unlicensed` 为准。

这是当前范围内最小且符合用户要求的改法。

---

## 3. FancyTabWidget

目标文件：

- `src/plugins/core/fancytabwidget.h`
- `src/plugins/core/fancytabwidget.cpp`

### 3.1 Need a generic business visibility layer

当前 FancyTabWidget 只有：

- business registration order
- business enabled/disabled
- current item / selected business bookkeeping

但没有：

- “某些业务仍然存在，但临时不显示在右侧 modulation list 中”

因此需要新增独立能力，不能复用 `setBusinessUiEnabled()`。

### 3.2 Proposed API

建议新增：

```cpp
void setBusinessVisibleInList(IBusiness *business, bool visible);
void setBusinessesVisibleInList(const QList<IBusiness *> &businesses, bool visible);
bool isBusinessVisibleInList(IBusiness *business) const;
IBusiness *firstVisibleBusiness(const QStringList &preferredNames = {}) const;
```

内部建议维护：

```cpp
QSet<IBusiness *> m_hiddenBusinesses;
```

默认：

- 所有业务可见

### 3.3 Rebuild logic

`rebuildListWidget()` 仍然以 `m_businessOrder` 为唯一顺序基准，但只 append 可见业务。

这能保证：

- Analog item 被重新显示时顺序不变；
- 不需要额外保存插入位置；
- list remove/re-add 后仍能回到原始注册顺序。

### 3.4 Selection/current page fallback behavior

这是本次改动最关键的坑位，必须显式处理。

当某次 `rebuildListWidget()` 之后，发现：

- `selectedBusiness()` 已被隐藏；
- 或 `currentWidgetBusiness()` 已被隐藏；

必须做回退。

建议规则：

#### Selected business fallback

- 如果当前 selected 业务仍可见：保持不动
- 如果当前 selected 业务被隐藏：
  - 清掉旧 selected 状态
  - 不自动启用新的 fallback provider

这样做的原因是：

- 隐藏 Analog item 属于许可证门控，不应顺手把用户切到另一条 modulation provider；
- 自动选中新 provider 会隐式改动 pipeline 语义，风险高于收益。

#### Current widget fallback

- 如果当前显示的是 standalone page（如 Sweep / General Settings）：
  - 保持页面不跳转
  - 只修正 selected business
- 如果当前显示的正是被隐藏的 Analog page：
  - 跳转到 fallback visible business 的页面
  - 这里只回退“页面焦点”，不自动选中新 provider

### 3.5 Recommended fallback order

建议 fallback 顺序为：

1. `Playback`
2. `Streaming`
3. 第一个可见 business

原因：

- 用户已经明确希望把注意力聚焦到 `streaming` 和 `arb playback`；
- `Playback` 与 Analog 同属 `Playback provider`，语义更接近；
- 若团队更希望优先去 `Streaming`，这里只需要调换优先级列表，结构不变。

### 3.6 Guard existing APIs

以下 API 需要补 visibility guard：

- `getItemByName()`
- `setCurrentWidgetByBusiness()`
- `setBusinessSelected()`
- `setCurrentWidgetToFirstVisibleBusiness()`

否则会出现“业务已经从 list 消失，但仍被程序化选中/切页”的状态撕裂。

---

## 4. AnalogModulationPlugin

目标文件：

- `src/plugins/analog/analogmodulationplugin.h`
- `src/plugins/analog/analogmodulationplugin.cpp`

### 4.1 Track licensed businesses explicitly

当前注册方式是：

```cpp
Core::BusinessManager::registerBusiness(new Analog::AmplitudeModulation);
...
```

建议改为：

- 先创建对象
- 保存到插件成员列表
- 再注册

例如：

```cpp
QList<Core::IBusiness *> m_licenseControlledBusinesses;
```

这样 Analog 插件可以明确知道：

- 哪些业务受许可证控制
- 哪些业务隐藏/恢复时需要一起处理

### 4.2 One source of truth for list updates

Analog 插件应连接到：

- `DeviceParamsManager::licenseValidationStateChanged`

并采用如下规则：

#### state == Unknown

- 不更新 list
- 不 remove/re-add item

#### state == Licensed

- 把 `m_licenseControlledBusinesses` 全部显示回 list

#### state == Unlicensed

- 把 `m_licenseControlledBusinesses` 全部从 list 隐藏
- 若当前 selected/current page 命中隐藏业务，则执行 fallback

### 4.3 Bootstrap path

Analog 插件初始化后，如果当前已经有一个 open 的设备：

- 仍然复用现有 bootstrap 逻辑
- 这会立即产出一次 `Licensed` 或 `Unlicensed` 结果
- 从而在启动阶段就把 list 调整到正确状态

这条路径仍然保留，不需要改语义。

---

## 5. MainWindow / Settings Interaction

目标文件：

- `src/plugins/core/mainwindowsettingscontroller.cpp`
- 视实现需要，可能少量涉及 `src/plugins/core/mainwindow.cpp`

### 5.1 Why settings restore needs guarding

`loadSettingsFile()` 当前会：

- 按保存内容恢复 `activedBussiness`
- 按保存内容恢复 `currentWidgetBusiness`

如果这两个名字恰好是当前已被许可证隐藏掉的 Analog 业务，就会出现：

- list 中没有这个 item
- 但程序仍尝试把它选为 selected/current widget

因此需要在恢复时加一层：

- 若目标业务当前在 list 中不可见，则跳过这次恢复
- 使用与 FancyTabWidget 一致的 fallback 策略

### 5.2 Save behavior

保存逻辑可以保持原样：

- profile 仍然保存所有业务的 profile 数据
- 只是不再保存一个已经被隐藏但不该处于当前态的 business 作为 active/current

这不会引入格式迁移。

---

## Event Matrix

| 时刻 | Runtime State | List 是否更新 | Analog panel | Save Data |
| :--- | :--- | :--- | :--- | :--- |
| 启动，无设备，`FixedLic=false` | Unknown | 否，保持全显示 | 可编辑 | 禁用 |
| 启动，`FixedLic=true` | Licensed | 否，保持全显示 | 可编辑 | 按 ready 恢复 |
| `currentDeviceChanged` | Unknown | 否 | 可编辑 | 禁用 |
| `currentDeviceOpenStateChanged(false)` | Unknown | 否 | 可编辑 | 禁用 |
| `deviceConnected` 且 license pass | Licensed | 是，显示 Analog items | 可编辑 | 按 ready 恢复 |
| `deviceConnected` 且 license fail | Unlicensed | 是，隐藏 Analog items | 不可编辑 | 禁用 |
| 设备断开 / currentDevice=nullptr | Unknown | 否 | 保持当前 Unknown 语义 | 禁用 |

---

## Important Product Tradeoff

当前确认后的策略，优先的是 **list 稳定性**，而不是“无设备时回到默认完整列表”。

因此存在一个明确结果：

- 若某台设备校验失败，Analog item 被隐藏；
- 随后设备断开，Analog item 仍然保持隐藏；
- 直到下一次设备 open 成功并重新判定为 `Licensed`，它们才会重新出现。

这不是 bug，而是“避免断开/重连过程 list 闪烁”的直接代价。

这条 tradeoff 需要在实现前就明确接受。

---

## Validation Plan

### 1. Startup without device

- 启动后 list 中能看到所有 Analog item
- 进入任意 Analog page，参数可浏览和修改
- `Save Data` 按钮不可用

### 2. Connect licensed device

- 设备 open 成功后，list 不应闪动式重建多次
- Analog item 保持显示
- 当前 Analog 业务若 active/visible，应重新生成数据
- 有数据 ready 后，`Save Data` 恢复可用

### 3. Connect unlicensed device

- 设备 open 成功后，Analog item 一次性从 list 中消失
- 若当前 selected business 是 Analog，能自动回退到 `Playback` / `Streaming`
- 若当前停留在被隐藏的 Analog page，页面自动跳到 fallback visible business

### 4. Disconnect after unlicensed result

- list 不发生变化
- Analog item 不会因为断开而瞬间重新出现
- 不会出现 remove / add / remove 的闪烁

### 5. Switch unlicensed -> licensed

- 切换开始时 list 不动
- 新设备 open 成功后，Analog item 按原顺序重新出现

### 6. Load settings with hidden analog active business

- 若保存文件里记录了 `AM/FM/...` 为 active/current page
- 但当前 presentation 把它隐藏了
- 恢复流程不会把 UI 带到不可见业务上

---

## Explicit Non-Goals For This Round

本轮不打算顺手解决以下问题：

1. 不改大波形对话框的产品设计。
2. 不把 Unknown 期间的所有参数编辑都强制冻结。
3. 不改变现有 profile 文件结构。
4. 不把许可证逻辑扩展到 Streaming / Arb Playback。
5. 不修改 `TxOrchestrator` 的 pipeline 规则本体。

本轮只聚焦：

- 三态许可证状态建模
- list 可见性过滤
- hidden selected business 的回退
- Unknown 阶段 Save Data 语义

---

## Expected File Touch List

- `src/plugins/analog/deviceutils.h`
- `src/plugins/analog/deviceutils.cpp`
- `src/plugins/analog/analogplaybackbusiness.h`
- `src/plugins/analog/analogplaybackbusiness.cpp`
- `src/plugins/analog/analogmodulationplugin.h`
- `src/plugins/analog/analogmodulationplugin.cpp`
- `src/plugins/core/fancytabwidget.h`
- `src/plugins/core/fancytabwidget.cpp`
- `src/plugins/core/mainwindowsettingscontroller.cpp`

若实际实现中需要把 fallback helper 下沉到 core，也可能少量修改：

- `src/plugins/core/businessmanager.cpp`
- `src/plugins/core/mainwindow.cpp`

---

## Implementation Order

建议按下面顺序做，风险最低：

1. 先把 `DeviceParamsManager` 升级成三态，并保留现有 post-open 校验时序。
2. 再改 `AnalogPlaybackBusiness`，把 Unknown / Licensed / Unlicensed 的 panel 语义拆开。
3. 再给 `FancyTabWidget` 增加通用 visibility filter，先不接许可证。
4. 最后在 `AnalogModulationPlugin` 中把三态结果真正映射到 list 显示/隐藏。
5. 补 `MainWindowSettingsController` 的 hidden business 恢复保护。

这个顺序的优点是：

- 每一步都能单独验证；
- 出问题时更容易判断是状态机错了，还是 list fallback 错了；
- 不会在一开始就把“许可证、列表、当前页、配置恢复”四件事同时搅在一起。
