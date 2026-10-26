# HTRA Reference Clock Integration

本文档总结当前 HTRA 参考时钟接入到 SGStudio 的现状、设计取舍、完整配置链路、当前前端实现以及剩余约束。

注：当前实现已经把 `SystemClockOut (RefOut)` 从“UI 改动即触发公共重配”的链路中拆出，改为设备快捷控制；`RefClockSource / RefClockFrequency` 仍保留在公共配置链路中。设备查询回写使用完整参考时钟状态，覆盖 source / frequency / output。

## 1. 目标与边界

- 目标：让真正影响波形配置的参考时钟参数进入当前 `Property -> CommonDeviceProfile -> IBusiness -> Core::IDevice::Profile -> FancyDevice::configuration()` 配置链路，同时把仅影响时钟输出开关的 `SystemClockOut` 单独下沉为设备快捷控制。
- 当前范围：`RefClockSource / RefClockFrequency` 已完成设备层、Profile 层、Property 层和前端联动；`SystemClockOut` 已改为设备直配。普通配置在时钟配置后、载频与输出配置前查询真实状态；External 查询返回失锁 warning 15 时单次切回 Internal，再确认并回写。时钟查询或恢复失败会中止本次配置。
- 当前设备：HTRA 插件 `FancyDevice`。

## 2. 当前接口模型

### 2.1 底层 H2 API

第三方 API 定义在 `3rdParty/h2_api/include/h2_api.h`：

- `device_config_clock(void** device, uint8_t state_ext_ref, double fref, uint8_t state_ref_out)`
- `device_query_clock(void** device, uint8_t* state_ext_ref_o, double* fref_o, uint8_t* state_ref_out_o, uint8_t* is_locked_o)`

其中：

- `source`：参考时钟源，当前为 `REF_INT` / `REF_EXT`
- `fref`：参考时钟频率
- `out`：参考时钟输出开关，底层为 `REF_OUT_OFF` / `REF_OUT_ON`（0 / 1）
- `locked`：参考时钟监控锁定状态，当前作为查询态保留，暂未进入 UI 展示

### 2.2 Core 抽象层

当前 `Core::IDevice::Profile` 中相关字段为：

- `Utils::Id refClockSource`
- `double refClockFrequency`
- `bool systemClockOut`

当前 `Core::IDevice::ReferenceClockState` 用于设备快捷查询回写：

- `Utils::Id source`
- `double frequency`
- `bool outputEnabled`
- `bool locked`

设计取舍：

- `refClockSource` 保留为 `Utils::Id`
  - 原因：它属于“能力枚举值”，适合和 `FeatureSpec` 联动。
- `systemClockOut` 收敛为 `bool`
  - 原因：它在业务语义上是简单开关，不应把 HTRA 的 `state` 直接泄漏到 Core 层。

## 3. FeatureSpec + Id 的作用

虽然 `systemClockOut` 的值模型已经收敛为 `bool`，但能力模型仍然保留 `FeatureSpec + Utils::Id`：

- `FancyDevice::refClockSourceSpecs()` 返回参考时钟源候选项
- `FancyDevice::sysClockOutSpecs()` 返回系统时钟输出候选项

这套模型的意义不是“保存当前值”，而是：

1. 设备声明自己支持哪些选项
2. UI 可以动态渲染选项显示文案
3. 不同设备可提供不同能力集合而不改 Core 公共接口
4. 设备能力的显示文案和稳定标识解耦

当前 HTRA 提供的能力项：

- `SGStudio.Device.HTRA.ReferenceClockSource.Internal`
- `SGStudio.Device.HTRA.ReferenceClockSource.External`
- `SGStudio.Device.HTRA.SystemClockOut.Disable`
- `SGStudio.Device.HTRA.SystemClockOut.Enable`

注意：

- 对于能力层来说，`systemClockOut` 仍保留 Enable/Disable 两个 FeatureSpec
- 对于当前前端交互来说，ReferenceDialog 的按钮状态文案使用 `ON/OFF`
- 对于回写到 `Profile` 的值，只保留为 `bool`

## 4. 当前设备层实现

### 4.1 能力声明

`FancyDevice` 当前在构造函数中声明：

- 参考时钟源：Internal / External
- 系统时钟输出：Disable / Enable

并通过 `RefClockSourceMapping` 将 `Utils::Id` 映射到底层 `uint8_t` 参考源值。

### 4.2 查询缓存

`FancyDevice` 维护三个查询缓存：

- `source_query`
- `refFreq_query`
- `clockOut_query`

当前默认值为：

- `source_query = REF_INT`
- `refFreq_query = 100E6`
- `clockOut_query = STATE_OFF`

其中 `100E6` 是当前实现对内参考默认频率的工程假设。

## 5. 当前配置流程

当前完整链路如下：

1. `CorePlugin` 启动时创建三个 property：
  - `RefClockSource`
  - `RefClockFrequency`
  - `SystemClockOut`
2. Device Settings 页面中：
  - `RefClockSource` / `RefClockFrequency` 继续通过 property binding 进入 `CommonDeviceProfile`
  - `SystemClockOut` 改为直接调用当前 `IDevice::configureSystemClockOut()`
3. `CommonDeviceProfile` 只将 `RefClockSource / RefClockFrequency` 纳入 `CommonProfileGroup`；`SystemClockOut` 只保留为运行时镜像值
4. `IBusiness::getCurrentProfile()` / `TxPipelineRuntime::fillDeviceProfile()` 仍会读取当前镜像值，保证其它公共参数重下发时不会顺手把 RefOut 冲掉
5. `FancyDevice::configuration()` 在普通 apply 里仍会把 `Profile.systemClockOut` 透传给 `device_config_clock()`
6. `FancyDevice::fillWritebackProfileLocked()` 在完整 common/mode 配置和其他参数回读之后调用 `queryReferenceClockLocked()` 查询，使用返回的实际时钟填充 writeback，完成信号再同步到 `CommonDeviceProfile`。`TxPipelineExecutor` 同时把实际参考源、频率与 RefOut 同步到 effective request；External+15仅安排稍后的单次Internal配置
7. Device Settings 页面在设备切换、open 状态变化、用户点击后，会主动 query 完整 `ReferenceClockState` 并刷新 `RefClockSource / RefClockFrequency / SystemClockOut`

补充边界：

- `FancyDevice::open()` 不再执行 `device_query_clock()` / `device_config_clock()` 的预热配置。
- 设备刚打开后的参考时钟真正配置，依赖后续 `CommonDeviceProfile -> runtime -> FancyDevice::configuration()` 这条统一链路。
- `FancyDevice` 内部的 `source_query/refFreq_query/clockOut_query` 以 `Internal / 100 MHz / Off` 作为默认起点，后续由 `configuration()` 或显式 `queryReferenceClock()` 更新。

其中 `FancyDevice::configuration()` 中当前参考时钟处理顺序为：

1. 根据 `input.refClockSource` 计算 `targetRefClockSource`
2. 根据 `input.refClockFrequency` 或缓存值计算 `targetRefClockFrequency`
3. 根据 `input.systemClockOut` 计算 `targetClockOut`
4. 调用 `device_config_clock()`
5. 若成功或 warning，则更新 `source_query/refFreq_query/clockOut_query`
6. 完成 common 和模式/输出配置，再执行 writeback 中既有的载波、端口、LO 等回读
7. 在原有时钟回读位置调用 `queryReferenceClockLocked()`：查询External返回 `H2_WARNING_SYSCLK_UNLOCKED (15)` 时，保留活动告警和原warning码，启动延迟计时；不在查询中立即配置或二次确认
8. 使用查询返回的实际时钟填充writeback，15仍按正数warning成功返回；时钟查询失败向configuration返回false，不发布成功回写。没有writeback的配置仍在common/mode配置结束后查询

设置页的 `queryReferenceClock()` 复用同一采样规则，返回当次实际值，通过 `setReferenceClock()` 同步；查询本身不配置硬件。恢复等待门限已按用户要求降为100ms：既有实时状态线程在过门限的下一合法采样周期，先单次配置 `REF_INT / 100 MHz / 当前RefOut`，使Playback apply snapshot失效，再走该周期原本的普通clock查询。没有专门恢复确认查询或阻塞等待；实际动作仍随约1秒状态轮询触发，不是100ms主动回调，也不再因1000ms门限多等一轮。Streaming/低功耗/错误时延后。

原始warning 15仍沿用既有warnningCode，不被其他普通正数覆盖，getRealTimeStatus消费一次提示后清零；活动15独立保留，普通查询正常/锁定后自然移除。状态栏沿用原15报警及RLO，不增加externalReferenceClockUnlock历史事件或“已切回内部”提示，也不手工定时清除活动项。

2026-10-21新增非Streaming、已上电且本轮无已记录错误时的周期时钟检测，并按用户要求拆出延迟恢复；不改变配置后查询位置。每轮有效普通clock观测附带UID/能力revision，通过TxSessionService/TxPipelineRuntime同步变化后的已应用/稳定desired状态和CommonDeviceProfile；同值不重复发布，新请求待刷新或在途时不覆盖用户意图。恢复只尝试一次，失败继续保留告警和查询；显式clock配置/会话清理或有效锁定恢复后允许新尝试，显式查询和RefOut开关不重置次数。新的clock配置、正常锁定或已离开External会取消旧待执行动作。低功耗不被轮询唤醒，Streaming仍不新增硬件query。详情见[实时warning重构](htra_runtime_warning_polling_design.md)。仅静态检查，尚未构建或实机验收。

2026-10-08 现场回归：将时钟查询提前至 `device_config_clock()` 紧后，导致用户反复切换时通常没有告警/恢复；恢复原 writeback 查询位置并加入诊断日志后，17:20:11–17:20:35 的 8 次 External 请求均返回 `status=15/source=1/locked=0`，恢复确认均为 `status=0/source=0/locked=1`，原 warning 15 保留到回写。配置返回日志到查询前日志间隔 0–7 ms，查询前/返回日志间隔 295–302 ms。日志同步加锁写文件，因此这些时间不是纯 API 耗时；本轮同时调整查询位置和日志，尚不能完全排除日志的时序影响，也不能据此宣称固件存在固定等待要求。后续如需区分，应保持 SDK 顺序，只移除查询前诊断输出做对照。详见 [查询顺序回归修正 TaskLog](../TaskLog/2026-10-08_reference-clock-query-timing-regression.md)。

随后已按用户要求删除全部新增 `[RefClock]` 诊断输出，保持 SDK 调用顺序与恢复规则，供用户进行无额外日志的对照测试；该轮结果待反馈。

而当前 `RefOut` 按钮自身的点击路径不再经过这条顺序；它只复用设备当前缓存的 `source / fref` 切换输出开关。

## 6. 当前前端实现

### 6.1 Property 层

当前 `CorePlugin` 已新增三个 property：

- `RefClockSource`：`NumericProperty<unsigned int>`，通过 `qHash(Utils::Id)` 和设备能力项桥接
- `RefClockFrequency`：`NumericProperty<double>`，带 Frequency unit / step / decimals 元数据
- `SystemClockOut`：`NumericProperty<int>`，当前只保留为运行时镜像值；Device Settings 页面不再用它直接触发公共重配

### 6.2 CommonDeviceProfile

`CommonDeviceProfile` 当前负责：

1. 保存参考时钟源、外参考频率和 RefOut 运行时镜像值
2. 管理 Internal 固定 100 MHz 有效值与 External 可编辑频率之间的语义分离
3. 将 `RefClockSource / RefClockFrequency` 纳入 `ExternalPropertyMapping`
4. 将 `SystemClockOut` 保留为运行时镜像值，但不再让它的 UI 改动触发 `profileChanged()`
5. 在设备 writeback 成功后刷新 UI
6. 在保存/恢复配置时保持真正影响参考环境的字段一致

当前 `RefClockFrequency` property / 前端控件只表示外参考频率，默认 `10 MHz`。Internal 的 `100 MHz` 不再写入或显示到这个控件，而是由 `CommonDeviceProfile::refClockFrequency()` 在需要下发设备 Profile 时作为有效值返回。

### 6.3 Device Settings Reference Clock

当前前端入口为集中 Device Settings 页面中的 Reference Clock group，包含三个控件：

- `Reference`：`EnumTextButton`，绑定 `RefClockSource`
- 外参考时钟频率：`LabelButton`，绑定 `RefClockFrequency`，点击后走数值键盘编辑；仅当 `RefClockSource == External` 时可编辑
- `RefOut`：`LabelButton`，当前显示 `ON/OFF`，点击后直接调用当前 `IDevice` 进行配置

当前实现约束：

- 不使用 `.ui`，全部通过代码布局
- 尺寸策略参考 `CommonPanel`：
  - `Reference` 对齐 `TriggerSource`
  - `RefFreq` 对齐 `Frequency`
  - `RefOut` 对齐 `RF`
- 已补齐暗色与亮色主题样式
- 设备打开、设备切换和 `RefOut` 延迟回读时，会通过 `IDevice::queryReferenceClock()` 同步完整参考时钟状态；`querySystemClockOut()` 仅作为 Core 基类兼容 helper，由 `ReferenceClockState::outputEnabled` 派生。

### 6.4 保存与恢复

当前参考时钟字段已经进入 `CommonPanelProfile` 的 JSON 保存/恢复链路：

- `RefClockSource`
- `RefClockFrequency`，其语义为外参考频率控件值

`SystemClockOut` 当前不再进入 `Profile.json` 保存/恢复。

因此配置保存到 `Profile.json` 后，重新恢复时只会同步真正影响参考环境的字段；RefOut 保持当前设备状态，不会因加载业务配置而被隐式改写。
### 6.5 当前持久化格式与兼容性边界

当前 `RefClockSource` 的 JSON 落盘格式是 `unsigned int` 数值，而不是稳定字符串 ID。

这一点需要特别注意：

- `RefClockSource` 在 property / `CommonPanelProfile` / `Profile.json` 里保存的是数值。
- 该数值来自 `qHash(Utils::Id)`。
- 但 `Utils::Id` 当前并不是“基于字符串内容计算出的稳定协议哈希”，而是进程内首次看到某个字符串时分配的运行时编号。

这意味着当前数值格式的真实语义是：

- 它更接近“当前版本、当前初始化顺序下的运行时枚举编号”。
- 它不应被视为跨版本、跨插件初始化顺序变化时仍然稳定的长期持久化协议值。

因此当前实现的兼容性结论应当写清楚：

- 在同一套程序、相近初始化顺序下，当前数值持久化通常可以正常恢复。
- 但如果未来 `Utils::Id` 分配顺序变化、插件注册顺序变化，或新增更早初始化的 `Id`，旧 `Profile.json` 中的 `RefClockSource` 数值就可能无法再正确映射回当前能力项。

这一风险并不是 `RefClockSource` 独有问题，`TriggerSource` 当前也是同类实现，因此两者的持久化兼容性边界是一致的。

当前文档结论：

- 现状允许继续使用该数值格式。
- 但该格式暂不作为“长期向后兼容”的承诺。
- 后续若决定提升配置文件跨版本兼容性，应优先考虑改为持久化稳定字符串 ID，并在恢复时兼容旧数值格式。
- 本轮只记录风险，不在当前任务中修改实现。

## 7. 行业语义约束

### 7.1 内参考

- 内参考下，设备有效参考频率按固定 `100 MHz` 理解
- 对用户来说，内参考不是“可调时钟源”，而是设备内部固定时基
- 前端不再把 Internal `100 MHz` 显示到外参考频率控件；该控件只读并保留当前外参考频率值

### 7.2 外参考

- 外参考下，`refClockFrequency` 应可编辑，但必须受支持值约束
- `100 MHz` 对外参考来说是合理值，但不是唯一合理值
- 行业内也常见 `10 MHz` 外参考
- 是否成功取决于外部参考输入是否真实存在、频率是否匹配、设备是否锁定成功

### 7.3 系统时钟输出

- 业务值用 `bool`
- 能力层仍保留 Enable / Disable 两个 FeatureSpec
- 当前前端按钮状态文案使用 `ON/OFF`

## 8. 当前 RefOut 语义边界

当前 `RefOut` 的设计边界是：

- UI 点击时直接走设备快捷接口，不再通过 `profileChanged()` 驱动整条 apply。
- `CommonDeviceProfile` 仍缓存当前 `systemClockOut`，供普通 apply 做“状态保持”。
- `SystemClockOut` 不参与 `Profile.json` 保存/恢复，避免加载业务配置时隐式改写设备时钟输出状态。

## 9. 当前失败路径的语义

当前 `device_config_clock()` 执行后：

- 配置返回 `>= H2_NOERROR` 时更新接受的配置缓存
- 在载频/输出配置前查询实际状态，必要时单次恢复 Internal 并确认；最终回写使用实际时钟缓存
- 查询、恢复配置或恢复确认失败时返回 false；不使用缓存伪造一次成功回写

因此 External 查询返回正数 15 是有明确恢复策略的 warning：单次切回 Internal，确认成功后继续本次配置。其他正数 warning 不触发切源；负数 API 错误及恢复确认未切为 Internal 都使本次配置失败。

## 10. 当前前端行为约束

当前实现应当遵守以下行为：

1. 参考时钟源选项由 `refClockSourceSpecs()` 动态驱动
2. 当 `refClockSource == Internal` 时：
  - 设备有效参考频率固定为 `100 MHz`
  - 外参考频率控件保留外参考频率值，不显示 Internal `100 MHz`
  - `RefClockFrequency` property 设为只读
3. 当 `refClockSource == External` 时：
  - `RefClockFrequency` 允许编辑
  - `RefClockFrequency` 默认值为 `10 MHz`
  - 用户修改后的外参考频率直接保存在该 property 中
4. `RefOut` 当前是简单开关：
  - UI 文案显示 `ON/OFF`
  - 业务值写回 `bool`
  - 页面点击后直接调用当前设备，而不是触发公共重配
5. 如果底层参考时钟配置失败，当前 UI 不应把一次点击直接理解为“设备实际已经切换成功”

## 11. 当前状态总结

已经完成：

- `Profile` 层接入 `refClockSource/refClockFrequency/systemClockOut`
- `FancyDevice` 层接入 `device_config_clock()`
- `FancyDevice` 已补 `queryReferenceClock()` / `configureSystemClockOut()` 直配接口
- `PropertyManager` 层新增 `RefClockSource/RefClockFrequency/SystemClockOut`
- `CommonDeviceProfile` 已纳入参考时钟字段，其中 `SystemClockOut` 只保留为运行时镜像值，不再触发 `profileChanged`
- `IBusiness::getCurrentProfile()` 已下发参考时钟字段
- `deviceConfigurationEnd` 已把 writeback 回刷到 `CommonDeviceProfile`
- Device Settings 页面中的 RefOut 已切换为直配按钮
- `IDevice::queryReferenceClock()` 暴露完整参考时钟查询结果，Device Settings 页面 query 后同步 `RefClockSource / RefClockFrequency / SystemClockOut`
- `FancyDevice::configuration()` 在输出配置前查询/恢复参考时钟，writeback 与 runtime effective request 都同步设备实际状态
- External 查询 warning 15 的单次 Internal 恢复及参考时钟失败即中止已完成（2026-10-08，静态检查；硬件行为待实测）
- Internal `100 MHz` 有效值策略已实现，且不再写入外参考频率控件
- External 默认 `10 MHz` 策略已实现
- 保存/恢复配置链路已覆盖 `RefClockSource / RefClockFrequency`，但不再覆盖 `SystemClockOut`
- 暗色/亮色主题样式已补齐
- 参考时钟源使用 `FeatureSpec + Id`
- 系统时钟输出值模型收敛为 `bool`

尚未完成：

- 外参考频率候选值约束

## 12. CommonDeviceProfile 频率状态约束

当前前端默认值策略只需要 CommonDeviceProfile 中一份外参考频率状态：

- 外参考控件值：`m_externalRefClockFreq`

Internal 的 `100 MHz` 不再作为控件值缓存，也不再通过 `RefClockFrequency` property 表示。需要下发设备 Profile 时，`CommonDeviceProfile::refClockFrequency()` 根据当前 source 计算有效频率：

- `Internal`：返回固定 `100 MHz`
- `External`：返回 `m_externalRefClockFreq`，无效值回落到 `10 MHz`

因此需要遵守以下约束：

- `setProfile()` 仅在接收 External writeback 后更新 `m_externalRefClockFreq`
- `setReferenceClock()` 仅在设备快捷 query 回写 External 状态后更新 `m_externalRefClockFreq`
- `restoreSettings()` 恢复的是外参考频率控件值
- Internal writeback 不应覆盖外参考频率，避免设备返回的 Internal `100 MHz` 污染外参考控件

## 13. 当前文档结论

截至当前实现，HTRA 参考时钟已经不是“仅设备层接入”的状态：`RefClockSource / RefClockFrequency` 完成了从 Property、CommonDeviceProfile、IBusiness 到 `FancyDevice::configuration()` 的闭环，而 `RefOut` 则被拆分为设备快捷控制。

当前剩余工作主要集中在“更严格的工程正确性”而非“基础链路是否存在”，即：

- 外参考频率候选值限制
