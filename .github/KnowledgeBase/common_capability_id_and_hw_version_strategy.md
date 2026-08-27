# 公共 capability ID 与硬件版本分档策略

本文记录 SGStudio 在支持同厂多型号设备时，对 `TriggerSource` / `RefClockSource` 等公共能力的推荐建模方式，以及为什么应优先迁移到公共语义 ID，再通过设备版本信息决定当前设备真正支持哪些 capability。

## 1. 适用问题

当前工程已经具备以下前提：

- App 启动时通过插件系统加载设备插件。
- `DeviceManager` 维护 `currentDevice`，UI 与业务围绕当前设备工作。
- 设备 capability 已经通过 `IDevice::FeatureSpec` 暴露给 Core / UI。

在这个架构下，如果未来接入的是“同一系列、差异不特别大的多型号设备”，真正需要解决的问题不是“要不要再起一个软件”，而是：

1. 哪些能力属于跨型号复用的公共语义。
2. 哪些差异应通过 capability profile 区分，而不是通过重新发明一套 ID 名字区分。

本文结论：

- `TriggerSource` / `RefClockSource` 应优先迁移到公共语义 ID。
- 不同型号之间的 capability 差异，优先通过 `model + hardware_version` 做 profile 分档。
- 配置持久化应保存稳定字符串 ID，而不是运行时 hash / 数值。

## 2. 为什么当前厂商命名不适合作为长期协议

当前 HTRA 侧已经使用类似如下命名：

- `SGStudio.Device.HTRA.TriggerSource.Bus`
- `SGStudio.Device.HTRA.ReferenceClockSource.Internal`

这种命名在单插件、单厂商阶段没有立即问题，但它会带来长期问题。

## 3. 推荐的 ID 分层

### 3.1 公共语义 ID

对跨型号可复用的能力，建议进入公共命名空间：

- `SGStudio.Device.Common.TriggerSource.Bus`
- `SGStudio.Device.Common.TriggerSource.External`
- `SGStudio.Device.Common.TriggerSource.XPps`
- `SGStudio.Device.Common.ReferenceClockSource.Internal`
- `SGStudio.Device.Common.ReferenceClockSource.External`

这些 ID 的含义应尽量保持稳定，不随具体插件或具体硬件版本变化。

### 3.2 厂商/型号私有 ID

只有在以下情况下，才继续保留厂商或型号命名空间：

- 该能力不是公共语义，而是某厂商特有行为。
- 不同型号虽然名字相似，但实际硬件语义并不等价。
- 该能力未来大概率不会跨设备复用。

当前 `LO mode` 更接近这一类，因此暂不建议强行公共化。

## 4. capability 应如何区分

### 4.1 不靠 ID 名称区分，而靠 capability profile 区分

推荐模型是：

- Core 层只认公共语义 ID。
- 具体设备插件决定当前设备支持哪些公共语义。
- 驱动内部再把公共语义 ID 映射到底层 SDK 枚举或配置序列。

因此，同一个 `TriggerSource.External`：

- 型号 A 支持：spec 列表包含它。
- 型号 B 不支持：spec 列表不包含它。

后者会把本来应该由 capability 层处理的问题，错误地扔给配置兼容层。

### 4.2 用什么维度生成 capability profile

对于 H2 系列，`device_info` 在设备 open 后已经给出：

- `model`
- `hardware_version`
- `mfw_version`
- `ffw_version`
- 其他总线/固件版本

如果未来型号差异不是特别大，优先建议使用：

- `model + hardware_version`

作为 capability profile 的主分档维度。

原因：

1. `model` 用来区分产品大类。
2. `hardware_version` 用来区分同型号下的板级能力差异。
3. 大多数 capability 差异本质上来自硬件设计，而不是临时运行状态。

只有当某个能力明显依赖固件版本时，再把 `mfw_version` / `ffw_version` 作为附加门槛，而不建议一开始就把 capability 全部绑在 firmware 版本上。

## 5. 推荐的 capability 生命周期

### 5.1 扫描阶段

扫描阶段通常能拿到 `model`，但未必已经拿到最终可用的硬件版本信息。

因此扫描阶段建议只做两件事：

1. 设备注册与 currentDevice 选择。
2. 生成一个粗粒度 baseline capability，最多只用于占位或早期 UI 默认状态。

不要在扫描阶段就把 capability 判断做死。

### 5.2 open 后阶段

设备 open 成功后，基于真实的 `device_info` 生成最终 capability profile：

- `TriggerSource` 的可选项
- `RefClockSource` 的可选项
- 未来 `LO mode`、`GNSS`、系统时钟输出等可选项

然后由当前设备通过 `IDevice::*Specs()` 输出这份最终能力快照，Core/UI 再按这份快照重建菜单和控件。

这个阶段的 capability 才应被视为“可用于下发、可用于冲突校验”的正式能力集合。

## 6. 对配置保存/恢复的要求

公共 ID 迁移以后，保存/恢复层也应同步调整，否则架构仍然不闭环。

### 6.1 配置应该保存什么

配置文件应该保存稳定字符串 ID，而不是运行时 hash 值。例如：

```json
{
  "common": {
    "triggerSource": "SGStudio.Device.Common.TriggerSource.External",
    "refClockSource": "SGStudio.Device.Common.ReferenceClockSource.External"
  }
}
```

不建议继续把 `qHash(Utils::Id)` 或运行时枚举数值当作长期协议值，因为它们更接近运行时绑定值，而不是语义稳定值。

### 6.2 未连接设备时加载配置的合理行为

当设备尚未连接时，系统应允许加载配置，但不要立即做 capability fallback。

合理行为是：

1. 保留用户请求的公共语义 ID。
2. 暂不根据空 capability 列表把值清空或替换成默认值。
3. 等当前设备 open 后，再按 capability profile resolve。

这样配置表达的是“用户想要什么”，而不是“当前设备临时允许什么”。

### 6.3 连接设备后如何 resolve

当设备 capability 快照到位后：

1. 若请求值被支持，直接采用。
2. 若请求值不被支持，按该字段语义决定是硬冲突还是软回退。

推荐约束：

- `TriggerSource` / `RefClockSource` 更接近硬语义，不应静默替换。
- `LO mode` 这类次级优化项，允许在明确规则下软回退。

## 7. 对插件层的实现建议

如果未来仍保持一个 HTRA 插件支持多个接近型号，建议插件内部显式引入 capability resolver，而不是把差异散落在各处 if/else。

推荐形式：

1. `CapabilityProfile`：只描述“支持哪些公共能力”。
2. `CapabilityResolver`：根据 `model + hardware_version` 产出 profile。
3. `SdkMapping`：把公共 ID 映射到底层 H2 SDK 枚举。

这样分层后：

- Core 看的是公共语义。
- 插件看的是当前硬件版本。
- 驱动调用看的是底层 SDK 枚举。

三层边界会比较清楚。

## 8. 迁移顺序建议

建议按以下顺序推进，而不是一次性重写：

1. 为 `TriggerSource` / `RefClockSource` 定义公共语义 ID。
2. 插件 spec 输出改为公共 ID。
3. 插件内部完成“公共 ID -> SDK 枚举”的映射。
4. 保存/恢复改为保存字符串 ID，并放弃旧厂商 ID/旧数值格式兼容。
5. 在 open 后引入 `model + hardware_version` capability resolver。
6. 再根据经验决定哪些能力可以继续公共化，哪些仍保留厂商私有命名。

## 9. 最终建议

对于“同厂多型号、硬件差异不特别巨大”的场景，推荐设计是：

- 一个软件。
- 一个主插件或少量插件。
- 公共语义 ID 统一收敛到 Core 可复用命名空间。
- capability 差异通过 `model + hardware_version` 解析。
- 配置始终保存用户请求的稳定语义 ID。

这样做的收益是：

1. 配置可跨型号迁移。
2. UI 菜单和控件可按 capability 动态重建。
3. 未来新增型号时，主要改动集中在插件 resolver，而不是整个 Core / UI / 配置系统。
