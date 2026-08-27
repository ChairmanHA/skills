# RF Port 能力链路与 UI EnumTextButton 接入说明

本文档聚焦两件事：

1. 当前 RF Port 如何在设备 open 后根据能力查询动态生效。
2. UI 层如何新增一个 EnumTextButton 并绑定 `RfPort` property。

## 1. 目标语义

当前 RF Port 的业务语义是：

- `Standard`：默认端口，所有设备都应可用。
- `High Power`：可选端口，只有设备 open 后查询到 `OPTION_MEDIUM_POWER`（当前值为 `10`）才可用。

因此，RF Port 是一个“先有默认值，后有动态能力覆盖”的设置项。

## 2. 端到端链路（open 后能力刷新）

### 2.1 Core 预注册 property（启动阶段）

在 `CoreRuntimeServices::initializePropertySchema()` 中，Core 预先创建 `RfPort`：

- 类型：`NumericProperty<unsigned int>`
- 默认值：`qHash(Utils::Id(Core::Constants::DEVICE_RF_OUTPUT_PORT_STANDARD))`
- 初始状态：`readOnly=true`
- 初始 `enumDisplayOptions`：固定包含 `Standard` 与 `High Power` 两项，其中 `Standard.enabled=true`、`High Power.enabled=false`

这样即便设备尚未 open，属性系统也已具备稳定入口。

### 2.2 设备层声明与能力探测（FancyDevice）

`FancyDevice` 中有三层关键点：

1. 固定映射
- `RFPortMapping`：`Standard -> 0`，`HighPower -> 1`（用于下发到 H2 API）

2. 候选项容器
- 构造时在 `m_rfPortSpecs` 中按值创建 Standard、High Power 两项
- 默认调用 `refreshRfPortSpecs(false)`，初始就返回固定全集 `Standard + High Power`，其中 High Power 仅以 `enabled=false` 表示不可选

3. open 后探测
- `open()` 成功后调用 `device_query_options(&device, options, &optionCount)`
- 同一次循环完整扫描返回项：`OPTION_MEDIUM_POWER` 决定 High Power，`OPTION_BW_320M_TX` 决定 Playback 采样率/容量档位，不能识别到前一个选件后提前退出
- 调用 `refreshRfPortSpecs(highPowerEnabled)` 更新 `m_rfPortSpecs`

这一步是“设备真实能力”进入 Core 的源头。

### 2.3 Runtime Bridge 把设备能力注入 Property

`DeviceRuntimeBridge` 在两种时机调用 `refreshDeviceFeatureSpecs(device)`：

- `currentDeviceOpenStateChanged(false)`
- `currentDeviceOpenStateChanged(true)`

在 `refreshDeviceFeatureSpecs()` 内，会从设备取：

- `device->rfPortSpecs()`

然后统一传给 `CommonDeviceProfile::setFeatureSpec(...)`。

`FeatureSpec` 在这条边界上使用值快照：设备 getter 返回
`QList<IDevice::FeatureSpec>`，`DeviceRuntimeBridge` 和 `CommonDeviceProfile`
各自保存列表副本，不借用设备内部对象地址。这样设备注销后即使析构在 I/O
线程异步执行，UI 也不会留下悬空 spec 指针。

### 2.4 CommonDeviceProfile 生成 enumDisplayOptions 并做回退

`CommonDeviceProfile::setFeatureSpec()` 对 `RfPort` 的处理是：

1. 生成固定两项的 `enumDisplayOptions`：
- `Standard`
- `High Power`
2. 对于设备实际返回在 `rfPortList` 中的项：
- 用设备 spec 刷新对应项的 `display/value`
- 并直接采用 `spec->enabled()` 作为可选状态
3. 对于设备未返回的项：
- 保留默认 `display/value`
- 并设置 `enabled=false`
4. 当没有任何已启用项时，把 `RfPort` 设为只读；否则允许下拉打开。
3. 校验当前值是否仍在可选集合里：
- 在集合内：保持当前值。
- 不在集合内：优先回退到已启用的 `Standard`；若 `Standard` 也未启用，则回退到首个已启用项；若一个都没有，再退回 `Standard`。
4. 把回退后的值同步到 `m_rfPort`、`m_profile.rfPort` 和 property 当前值。

这一步保证了“能力变化后 UI 和运行时值不会失配”。

## 3. 端到端链路（下发与写回）

### 3.1 快照进入 Tx 请求

`TxSessionService::buildApplyRequest()` 会把 `CommonDeviceProfile::rfPort()` 写入：

- `req.context.common.rfPort`

### 3.2 Runtime 转为设备 Profile

`TxPipelineRuntime::fillDeviceProfile()` 会把：

- `c.rfPort -> profile->rfPort`

### 3.3 设备下发

`FancyDevice::applyCommonDeviceSettingsLocked()` 会把 `profile->rfPort` 映射为硬件端口号并下发：

- `channel_config_port(&channels[0], rfPortValue)`

当前实现策略：

- 输入值有效且在映射中则使用输入值。
- 否则使用 Standard。

### 3.4 设备写回

`FancyDevice::fillWritebackProfileLocked()` 通过：

- `channel_query_port(&channels[0], &queriedRfPort)`

把设备实际端口回写到：

- `writeback->rfPort`

随后由现有 `deviceConfigurationEnd -> CommonDeviceProfile::setProfile` 链路同步回属性系统与 UI。

## 4. UI 新增 EnumTextButton 绑定 RfPort 的推荐做法

下面以 `DeviceSettingPanel` 为例（与当前 Trigger/RefClock 的绑定模式保持一致）。

### 4.1 布局层新增控件

在 RF 分组中新增一个 `EnumTextButton`，建议命名：

- `m_rfPortBtn`

并沿用现有按钮初始化工具（如 `setupValueButton`）保证风格一致。

### 4.2 initializeBindings() 取 property 并绑定

在 `DeviceSettingPanel::initializeBindings()` 中增加：

```cpp
auto rfPortProperty = PropertyManager::instance()->getProperty("RfPort");
```

把它纳入空指针前置校验，然后绑定：

```cpp
PropertyBindingManager::instance()->bindEnumTextButtonToProperty(m_rfPortBtn, rfPortProperty);
```

### 4.3 不要在 UI 手写枚举选项

RF Port 选项应完全来自 property 的 `enumDisplayOptions`（由 `CommonDeviceProfile::setFeatureSpec` 在设备能力刷新时写入）。

这意味着 UI 不应写死：

- `Standard/High Power` 文案列表
- 端口值映射
- open 后能力切换逻辑

UI 只做“展示 + 编辑”，能力与回退策略留在 Core/Device。

### 4.4 只读状态由 property 驱动

`RfPort` 何时可编辑由 property 的 `readOnly` 控制：

- 没有任何已启用选项时只读
- 只要存在已启用项就可编辑；未启用项仍可显示在下拉中，但不可选

绑定后按钮会随 `readOnlyChanged` 自动体现可用态，无需额外手写 enable/disable 逻辑。

## 5. 最小接入清单（UI）

1. 在 `DeviceSettingPanel` 增加 `EnumTextButton *m_rfPortBtn` 成员。
2. 在 RF 区域创建并加入布局。
3. 在 `initializeBindings()` 获取 `RfPort` property。
4. 调用 `bindEnumTextButtonToProperty(m_rfPortBtn, rfPortProperty)`。
5. 不写任何硬编码选项，依赖 open 后能力刷新自动更新。

## 6. 常见误区

1. 在 UI 里手写 `Standard/High Power`：会和设备真实能力脱节。
2. 在 UI 里直接调 `channel_config_port`：会绕开 TxSnapshot/Runtime，破坏统一配置链路。
3. 忽略 open 后刷新：如果只在构造时初始化，High Power 永远不会出现。
4. 能力收缩时不做回退：会出现“当前值不可选”导致 UI 与设备状态不一致。

## 7. 调试建议

1. 观察 open 后日志，确认 `device_query_options` 是否成功。
2. 检查 `RfPort` property 的 `enumDisplayOptions` 是否从空列表变为设备能力列表。
3. 切换 `RfPort` 后检查是否进入 `TxSessionService -> TxPipelineRuntime -> FancyDevice::applyCommonDeviceSettingsLocked`。
4. apply 后检查 `channel_query_port` 写回是否与 UI 一致。

---

结论：

当前架构已经把 RF Port 作为“能力驱动的公共配置项”打通。UI 层只需新增并绑定一个 EnumTextButton 到 `RfPort` property，即可无缝接入 open 后动态能力与统一下发链路。
