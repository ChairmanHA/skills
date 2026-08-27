# SCPI 接入静态审阅与当前收口方案

日期：2026-08-14
## 结论

当前 SCPI 接入已经具备可复用的 parser / transport 基础，但产品集成层仍主要是“SCPI 远程驱动 UI + 写 Property”的过渡实现，而不是稳定的仪器控制服务。

对一个简单信号源来说，现有 `TxSessionService / TxApplyRequest / TxPipelineRuntime` 机制不需要为了第一阶段 SCPI 做大改。`Center`、`Level`、`RF`、`Mod`、参考时钟、触发、RF Port 等公共参数已经有共享 Property、UI binding、`CommonDeviceProfile`、`TxSessionService::requestRefresh()` 和 runtime writeback 链路。只要 SCPI 在 GUI 线程上按现有语义写这些共享状态，并触发已有 completion signal，UI 同步和 runtime apply 都有现成路径。

真正需要收口的是 SCPI 命令层的边界、错误语义、完成语义和安全/配置策略。Claude Opus5 的审核结论与这个判断基本一致，并补充了几个更务实的落地判断：当前命令面不必立即砍到 Core-only；在明确“UI 必须存在、命令成功表示意图被接受”的过渡契约后，可以保留已有命令面，按族逐步迁移到 UI 无关 service。

## 证据来源

当前本地代码复核过的关键位置：

- `src/plugins/scpi/scpi.json`
- `src/plugins/scpi/register/scpiregister.cpp`
- `src/libs/scpi/src/parser/parser.cpp`
- `src/libs/scpi/src/transport/transport_tcp.cpp`
- `src/libs/business/numericproperty.h`
- `src/plugins/core/txsessionservice.cpp`
- `src/plugins/core/commondeviceprofile.cpp`
- `src/libs/business/propertybindingmanager.cpp`

关联架构文档：

- `tx_execution_context_phase1_and_provider_migration.md`
- `ui_independent_runtime_and_minibar_design.md`
- `plugin_metadata_and_loading_architecture.md`
- `cmake_thirdparty_module_best_practices.md`
- `.github/TaskLog/2026-08-14-scpi-p0-fixes.md`

## 已确认的可复用部分

SCPI 基础设施本身方向是合理的：

- 使用成熟 SCPI parser，而不是手写协议 parser。
- `src/libs/scpi` 与 `src/plugins/scpi` 分层，transport/parser 没有直接放进业务插件。
- parser worker 队列串行处理命令。
- 触碰 Qt object 前通过 `QMetaObject::invokeMethod(..., Qt::BlockingQueuedConnection)` 切回 GUI 线程。
- `SCPIDialog` 作为启停和日志入口可以保留，但它不应成为命令行为 owner。

这些优点不要求保留 UI 自动化式命令实现。长期稳定形态仍应把命令映射到 UI 无关的 intent/service。

## 当前仍存在的问题

### 1. `scpi.json` 元数据和运行时依赖仍不准确

观察：

```json
{
    "Name": "SCPI",
    "Dependencies": [ "Core"],
    "Description": "The SCPI plugin for SAStudio4."
}
```

当前命令实现访问了：

- `BusinessManager::getBusinessByName(...)`
- Analog / HTRA / QuickWaveform 业务和 Property
- `MainWindow`
- `CommonPanel`
- `StepSweepPanel`
- `DeviceSettingPanel`

风险：

- metadata 只声明依赖 `Core`，但命令面实际假设多个业务插件已经注册完成。
- 描述仍是 SAStudio4，和当前产品不一致。
- 依赖不准确会让插件加载顺序、功能可用性和问题定位变得偶然。

建议：

- 把描述改为 SGStudio。
- 二选一：收窄命令面到真实 Core-only，或声明当前命令面真正依赖的业务插件。
- 如果保留完整命令面，至少在文档中明确这些命令的插件依赖和不可用时的错误返回。

### 2. SCPI 命令仍依赖 UI 对象和可见主窗口

观察：

`scpiregister.cpp` 仍直接调用 `MainWindow::instance()->commonPanel()`、`sweepPanel()`、`showSweepPage()`、`deviceSettingPanel()`，并通过 `Panel::triggerBtnEnabledChecked(...)` 触发业务 panel 上的 enabled switch。

当前这条路径在“主 UI 一定存在、SCPI 从 UI 中启停、允许 SCPI 同步改变 UI 显示状态”的约束下可以工作，但它是过渡契约，不应成为长期架构。

风险：

- MainWindow 或 panel 未构造时会崩溃或返回不明确失败。
- UI 翻页、按钮点击、modal dialog 等视觉行为进入协议路径。
- 隐藏 UI、helper/headless、插件加载顺序变化时风险变大。
- 命令实现 include widget 类型名，违背 `ui_independent_runtime_and_minibar_design.md` 的方向。

建议：

- 近期先补齐 `MainWindow::instance()`、`commonPanel()`、`sweepPanel()`、`deviceSettingPanel()`、`business->controlPanel()` 的空指针检查。
- 中期把 Common、Sweep、调制业务逐步迁移到 UI 无关 intent/service。
- 当前保留 UI 触发路径时，必须在 SCPI 手册中写清楚“UI 必须存在”。

### 3. 命令错误没有进入 SCPI 错误队列

观察：

- `PRPOERTY_NULL_CHECK` / `PANEL_NULL_CHECK` 失败主要 `qDebug()` 后返回失败。
- 产品侧命令失败没有统一调用 `SCPI_ErrorPush`。
- `*ERR?` 主要只能读到 libscpi parser 自身的通用错误，读不到“无设备、业务不可见、参数越界、内部对象未初始化”等真实原因。

风险：

- 自动化客户端无法可靠定位失败原因。
- 同一类失败在不同 handler 中表现不一致。
- `std::nullopt` 或空响应不足以表达仪器协议错误。

建议：

- 建立一个产品侧错误上报 helper，把内部失败映射为 SCPI 错误队列。
- 至少区分：无设备、功能不可用、参数非法/越界、状态冲突、内部错误。
- 所有命令失败路径先推错误，再返回 `SCPI_RES_ERR` 或等价失败结果。

### 4. `*OPC?` / `*WAI` 没有真实完成语义

观察：

- `Parser::init()` 中 `*OPC?` 仍硬编码返回 `"1"`。
- `ScpiRegister::onWait()` 直接 `return true`。
- set 命令当前多为“属性已写入 / UI switch 已触发”即成功，不代表设备配置已经完成。

风险：

- 自动化脚本看到 success 时，设备可能还没 apply 完。
- `*OPC?` 和 `*WAI` 不能用于可靠排序。
- 用户会把“意图接受”和“硬件已完成”混为一谈。

建议：

- 短期明确文档契约：当前 set 命令成功表示“SGStudio 已接受控制意图”，不表示设备已经应用。
- 长期把 `*OPC?` / `*WAI` 接到 `TxSessionService` 的 apply 完成状态，例如 `appliedStateChanged`、`deviceConfigurationDone`、`sweepConfigurationDone`。
- 推荐长期语义：set 命令入队后可快速返回，`*OPC?` / `*WAI` 等待 apply 队列空闲并能读取最近一次 apply 结果。

### 5. parser 中固定 `QThread::msleep(100)`

观察：

`src/libs/scpi/src/parser/parser.cpp` 在命令处理循环里固定 sleep 100 ms。

风险：

- 吞吐被硬限制到大约 10 cmd/s。
- 这个 sleep 不能替代真正的 apply 完成同步。
- 它可能掩盖时序问题，同时拖慢正常自动化。

建议：

- 在完成语义明确前，不要把 sleep 当作同步机制。
- 建议在接入真正的 command/apply 状态后删除。

### 6. `*RST` 可能被 UI modal 阻塞

观察：

`onReset()` 触发 `ACTION_PRESET`。SCPI worker 通过 `Qt::BlockingQueuedConnection` 等待 GUI 线程执行 handler。

风险：

- 如果 Preset 触发模态确认框，SCPI worker 会阻塞直到用户点击。
- 自动化环境下这会表现为命令卡死。

建议：

- 为 SCPI 提供不弹窗的 preset/reset intent。
- 或把 `*RST` 明确标注为 UI 交互命令，不推荐自动化使用。

### 7. TCP 绑定地址和安全策略不明确

观察：

`TransportTCP::start()` 使用 `QHostAddress::Any` 监听控制端口。

风险：

- 默认向所有网卡开放 5025。
- 当前没有认证、访问控制或显式 LAN opt-in。

建议：

- 产品定位如果是本机自动化，默认改为 `QHostAddress::LocalHost`。
- 如果产品定位就是网口仪器，保留 `Any` 也可以，但 UI 和文档必须明确“已对局域网开放且无鉴权”。
- 后续可增加 bind address 配置和持久化。

### 8. 端口配置未持久化，数据通道策略不清晰

观察：

- `SCPIDialog` 可编辑 TCP/data 端口，但当前没有落 `Settings`。
- 默认端口仍由 `ScpiRegister::init()` 设置。
- 当前 `ScpiEngine::start()` 已能在控制通道失败时返回 false，但 data channel 失败时是否允许降级仍缺少产品策略。

风险：

- 用户重启后端口配置丢失。
- 自动化部署不能稳定依赖 UI 中改过的端口。
- data channel 失败的告警/降级语义不明确。

建议：

- TCP/data 端口都走 `Utils::Settings` 持久化，并做范围和冲突校验。
- 明确 data channel 是可选通道还是必需通道；若可选，应在 UI 显示 degraded 状态。

### 9. 参数越界被全局 `NumericProperty` clamp 掩盖

观察：

`NumericProperty::setValue()` 会按 metadata min/max 对所有写入方 clamp。

风险：

- SCPI 发越界参数时可能被静默夹到边界并返回成功。
- 这不符合常见 SCPI 行为，自动化客户端无法知道请求值无效。
- 这个变化影响所有 UI/业务调用方，不只是 SCPI。

建议：

- SCPI handler 在写 Property 前先按 metadata 校验。
- 越界直接推 `-222 Data out of range` 或项目定义的等价错误。
- 全局 clamp 是否保留应作为 UI/Property 行为单独评审，不应作为 SCPI validation 策略。

### 10. 业务查找依赖显示名

观察：

部分命令通过 `BusinessManager::getBusinessByName(...)` 查找业务，名称包含 UI 显示字符串，例如带换行或空格的调制业务名。

风险：

- i18n、文案、空格、换行调整都可能破坏 SCPI。
- 失败没有编译期保护，运行时也不易定位。

建议：

- 给业务引入稳定 ID。
- SCPI 使用稳定 ID 或业务注册表，不使用显示名。

### 11. 第三方 parser vendoring 较宽，但不是当前功能阻塞项

观察：

`3rdParty/scpi-parser-2.3` 包含 examples、tests、`.github`、CVI 工程等上游文件，但本项目 CMake 只编译 `libscpi/src`。

影响判断：

- 对当前编译和运行通常没有直接影响。
- 主要问题是代码审查噪声、仓库体积、供应链/许可证审计、未来升级 diff 可读性。
- 因此它不是 P0 correctness bug，可以后续单独做第三方依赖卫生清理。

建议：

- 后续单独提交裁剪或重整，只保留产品构建需要的 `libscpi/inc`、`libscpi/src`、LICENSE、版本说明和本项目 CMake wrapper。
- 如果保留完整上游树，也应补充 vendoring note，说明哪些目录不参与产品构建。

## 当前推荐落地顺序

### 近期：保留命令面，补齐产品可用性

当前不建议立刻删除大量已注册调制命令。更务实的方式是：

1. 明确过渡期契约：SCPI 需要主 UI 存在；命令成功表示意图接受，不表示设备 apply 完成。
2. 补齐 `MainWindow` / panel / business 的空指针检查。
3. 给所有失败路径接入 SCPI 错误队列。
4. 修正 `scpi.json` metadata 和真实依赖。
5. 为 `*RST` 提供不弹窗路径，或在文档中限制自动化使用。
6. 去掉 parser 固定 sleep 前，先设计真实完成语义。
7. 明确 bind address、安全提示、端口持久化和 data channel 降级策略。

### 中期：引入 `ScpiIntentService`

目标是让命令实现不再 include widget 类型：

```text
Core::ScpiIntentService
```

职责：

- 校验 SCPI 参数。
- 写共享 Property 或业务 intent。
- 触发已有 semantic completion signal。
- 触发 `TxSessionService::requestRefresh()` 或等价路径。
- 读回共享状态或 runtime snapshot。
- 把内部失败翻译成 SCPI 错误。

第一批接口可以很小：

```text
idn()
setCenterFrequency(double hz)
centerFrequency() const
setLevel(double dbm)
level() const
setRfEnabled(bool enabled)
rfEnabled() const
setModEnabled(bool enabled)
modEnabled() const
resetPreset()
```

### 长期：按族迁移 UI 依赖

Step A：Core/Common 命令

- `Center`、`Level`、`RF`、`Mod`、参考时钟、触发、RF Port。
- 这些已经是共享 Property，最容易迁移。
- `OUTP:MOD:STAT` 应从 `commonPanel()->triggerModButton()` 改为写 `Mod` 属性。

Step B：Sweep 命令

- 引入 UI 无关的 sweep enabled 和参数 owner。
- `StepSweepPanel` 只显示状态和处理用户输入，不作为 SCPI command target。
- 去掉 `showSweepPage()` 这类视觉行为在协议路径中的必要性。

Step C：调制业务命令

- 给业务层增加 UI 无关 enable/disable intent。
- Panel 只做显示与用户输入。
- SCPI 不再点击 `SwitchButton`。
- 逐族替换 `getBusinessByName(displayName)` 为稳定业务 ID。

## 后续 SCPI patch 审查清单

1. 命令实现里是否出现 widget 类型名？出现则需要说明为何仍处于过渡期。
2. 命令写的是共享 Property / typed intent，还是在点击 UI？
3. UI 是否通过现有 binding/writeback 更新，而不是被 SCPI 特判刷新？
4. `TxSessionService` 是否只收到一条明确 refresh 路径？
5. 命令成功表示“意图接受”还是“设备 apply 完成”？文档是否同步？
6. 无设备、功能不可用、参数越界、内部错误是否进入 SCPI 错误队列？
7. `*OPC?` / `*WAI` 是否与实际完成语义一致？
8. 插件 metadata 是否声明真实依赖？
9. TCP bind address、安全提示、端口持久化是否符合产品定位？
10. 第三方依赖是否说明了构建使用范围和许可证来源？

## 底线

当前 SCPI 支持可以作为“UI 可见条件下的过渡控制通道”继续推进，但不要把 UI 自动化路径包装成最终架构。正确方向不是重写 `TxApplyRequest` 或 `TxPipelineRuntime`，而是新增一层窄的 SCPI intent/service：它复用现有 Property、Profile、TxSessionService 和 runtime writeback，同时提供仪器协议需要的参数校验、错误队列、完成语义和安全配置。
