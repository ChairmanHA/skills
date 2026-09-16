# List Mode SCPI 基础设施实施准备

## 目标

在不重构现有全部 SCPI 业务机制的前提下，确定实现 R&S 兼容 List Mode 所必需的 SCPI 底层改造，并给出可直接实施的文件级步骤、边界、风险和验证清单。

## 已确认前提

- Center、Level 等现有控制意图最终由异步业务/runtime 链路处理；parser 中固定 100 ms 延时不是设备完成条件，也不是必需的主线程同步条件。
- 本任务聚焦 transport、parser、参数解析、结果输出和错误边界，不借机迁移现有全部 UI/Property 型 SCPI handler。
- 保留当前 parser 单工作线程和 FIFO 命令队列，除 List Mode 必需内容外不改变既有命令注册模型。
- 初始阶段为实施准备；2026-09-04 用户已授权实施下文第 1～4 项并生成 Python 测试脚本。List handler 和业务服务接线仍不在本轮范围内。
- 2026-09-04 用户进一步授权实施第 5 项的命令注册层。当前源码中尚无 `MScanTableService`，因此本次只落地完整 R&S 命令头、Source 通道校验、参数适配以及无需业务表即可成立的兼容状态；不调用 `ListModePanel`，也不在 `ScpiRegister` 保存 Draft/Applied 三列。

## 需要核对的代码

- `src/libs/scpi/src/transport/transport_tcp.*`
- `src/libs/scpi/src/parser/parser.*`
- `src/libs/scpi/src/engine/scpi_engine.*`
- `src/libs/scpi/src/helper/helper.*`
- `src/libs/scpi/src/scpi_context.cpp`
- `src/libs/scpi/include/scpi/scpi_context.h`
- `src/libs/scpi/include/scpi/details/defs.h`
- `src/plugins/scpi/register/scpiregister.*`
- `3rdParty/scpi-parser-2.3/libscpi` 中 `SCPI_Parse`、arbitrary block 和错误队列接口

## 设计问题

1. TCP 如何在分包、粘包和二进制 payload 含换行字节时识别一条完整 SCPI program message？
2. 如何绕过当前 1024 字节 `SCPI_Input` 累积缓冲，又不替换 libscpi？
3. 三个 List 数据列如何共用 ASCII/definite block 参数解析，并保持单位规则？
4. 查询如何输出大数组且不误用独立的 5125 广播数据端口？
5. 100 ms 删除后如何保持 parser FIFO 语义，同时不扩张为全局完成机制重构？
6. 哪些失败属于 transport 致命错误，哪些应进入 SCPI 错误队列？

## 静态分析结论

### 100 ms 的真实作用

- `QThread::msleep(100)` 位于 `SCPI_Input()`、handler 和 reply callback 之后，因此它没有等待本条命令完成，只是在处理下一条 FIFO 命令前空等。
- 现有设备控制最终进入异步业务/runtime 链路，100 ms 既没有 completion token，也没有设备回读条件，不能证明 Center、Level 或 MScan 已经生效。
- 删除后保留 parser 单工作线程和 FIFO；SCPI handler 的先后顺序不变，只去掉固定吞吐上限约 10 条/秒。
- List handler 必须同步完成 Draft 修改或不可变 apply 快照生成，不能把正确性寄托在命令间 sleep。

### 必须改造的四个底层问题

1. 当前 `TransportTCP` 在第一个 LF 处分帧，会把含 LF 字节的 definite block 拆坏。
2. 当前 `SCPI_Input()` 受 `kScpiInputBuffer[1024]` 限制，无法接收大列表；transport 已有完整报文，应直接使用 `SCPI_Parse()`。
3. 当前 reply callback 从 parser 工作线程直接调用属于 transport 线程的 `QTcpSocket::write/flush`，大查询返回前必须改为排队回 socket 线程。
4. 当前 `scpi::context` 没有 arbitrary block、数组和 Source 数字后缀 helper，List handler 无法可靠复用解析逻辑。

## 文件级实施计划

### 1. `src/libs/scpi/src/parser/parser.cpp`

- 每条报文开始前清空结果缓冲并重置 `mIsByteArray`。
- 补齐 LF 后调用 `SCPI_Parse()`，不再调用 `SCPI_Input()`。
- 删除 `kCommandDelayTimes` 和 `QThread::msleep(100)`。
- 保留单 worker/FIFO、现有 callback 类型和 `SCPI_Init()` 所需的 1024 字节内部数组；该数组不再承载完整 TCP 报文。

### 2. `src/libs/scpi/src/transport/transport_tcp.h/.cpp`

- 将每连接 `QByteArray` 扩展为包含缓冲与扫描位置的接收状态。
- 识别 `#<1..9><length><payload>`，按声明长度跳过 payload；处理头/payload 分包和多报文粘包。
- 字符串中的 `#` 不作为块头；`#H/#Q/#B` 保持普通 SCPI 数值前缀。
- 支持 LF 和 CRLF；只在块和引号外结束 program message。
- 设置 16 MiB 单连接缓冲和声明 payload 上限。
- `#0`、坏长度字段、溢出和超限关闭违规连接，不跨线程写 libscpi error queue。
- 用 queued invocation 把响应写回 socket 所在线程，并移除强制 `flush()`。
- 日志只记录命令头和字节数，不打印完整二进制缓冲。

### 3. `src/libs/scpi/include/scpi/scpi_context.h`、`src/libs/scpi/src/scpi_context.cpp`

- 封装 `SCPI_CommandNumbers()`，为 `[SOURce#:]` 返回默认/实际通道号。
- 增加 List 数值数组解析：ASCII 逐项单位解析；block 使用 `SCPI_ParamArbitraryBlock()`。
- 首版 block 固定为 IEEE-754 `REAL,64` 大端网络字节序，长度必须为 8 的整数倍，不猜测 `REAL,32/64`，也不按主机字节序直接解释。
- 频率统一 Hz、功率统一 dBm、逐点 dwell 裸值按微秒处理并转换为业务层单位。
- 拒绝空数组、NaN/Inf、错误量纲和第 100001 个元素；返回临时 vector，不直接修改 Draft。

### 4. `src/libs/scpi/src/engine/scpi_engine.cpp`

- block 和超长控制消息只向命令监视器报告方向、命令头和总字节数。
- 超长 ASCII 查询响应同样限长展示，实际 socket 响应保持完整。
- 保持 public callback 签名和 5025/5125 分工不变。

### 5. `src/plugins/scpi/register/scpiregister.*`

- 基础设施完成后使用 `[SOURce#:]LIST:...` 注册第 4 节全部 R&S 命令头。
- List handler 只做 SCPI 参数适配、错误映射和服务调用，不保存 Draft/Applied 表。
- 省略 Source 或 `SOURce1` 进入通道 1；不存在的 Source 通道稳定报错，不静默降级。

本次注册层实施成功标准：

- 第 4 节 36 个设置、查询和事件命令头全部注册，长短助记符、可选 `SOURce` 前缀和 `SOURce1` 数字后缀由 libscpi 正常匹配。
- `SOURce2` 等当前不存在的通道统一写入 `-241 Hardware missing`，不进入 handler，也不降级到通道 1。
- `LIST:MODE AUTO|STEP|INDex` 均接受，查询恒为 `AUTO`；`INDex/RESet`、`RMODe` 和文件容器外观提供稳定往返状态。
- 依赖尚未落地的 MScan 表服务或 runtime 的命令返回明确的 `-241`，不借此重新直连 UI；List 数据参数仍完整解析，确保块和单位错误优先按协议报告。
- 仅做静态验证：注册清单与第 4 节逐项比对、方法声明/定义检查和 diff 审查；不在本步骤构建或连接设备。

## 完成语义决定

- 本轮不实现真实设备 completion 等待。
- `LIST:APPLy`、`LIST:LEARn`、`FREQuency:MODE LIST` 在 handler 返回前完成同步校验并提交不可变快照；设备下发和回读仍异步。
- `*OPC?` 保持“前序 handler 已处理/请求已接受”语义，`*WAI` 保持当前兼容 no-op。
- 异步设备失败不从 runtime 线程直接写 libscpi error queue；真实完成/失败查询作为后续独立设计。

## 分阶段实施

1. Parser：直接 Parse + 删除 100 ms，回归现有短命令。
2. Transport：块感知分帧、限制、queued reply。
3. Engine：大数据日志收敛。
4. Context：通道和数组 helper。
5. List handler：完整命令面接入 `MScanTableService`。

阶段 1 至 4 是 List Mode SCPI 的底层前置；阶段 5 才进入命令业务接线。各阶段不顺带重构旧 handler。

## 本轮实施结果

- [x] `Parser` 改用 `SCPI_Parse()`，删除每条 program message 后的固定 100 ms，并在解析前重置结果状态。
- [x] `TransportTCP` 增加每连接扫描状态、definite block 感知分帧、16 MiB 上限和 fatal framing close。
- [x] 查询响应通过 queued invocation 回到 `QTcpSocket` 所在线程，不再强制 `flush()`。
- [x] `scpi::context` 增加 `[SOURce#:]` 数字读取和 ASCII/`REAL,64` 大端 List 数组解析。
- [x] `ScpiEngine` 对 definite block、非文本和超过 4096 字节的监视数据输出摘要。
- [x] 新增并扩展 `scripts/test_scpi_foundation.py`，覆盖现有命令、R&S List Mode 查询/设置命令头、Source 1/2 路由、LF/CRLF、分包、粘包、100 ms 删除、超过 1024 字节的复合报文、块内 LF、`#H` 和 fatal framing 后重连。
- [x] `ScpiRegister` 注册第 4 节全部 36 个 R&S List Mode 命令头；省略 Source 和 `SOURce1` 均指向通道 1，其他通道统一报告 `-241`。
- [x] `LIST:MODE` 按设计接受 `AUTO/STEP/INDex` 且查询恒为 `AUTO`；`INDex/RESet`、`RMODe`、触发源和文件容器兼容状态提供确定往返结果。
- [x] 数据列、`LEARn`、软件触发和切入 `FREQuency:MODE LIST` 在 `MScanTableService` 尚未实现时报告 `-241`；没有回退到 `ListModePanel`，也没有在注册层保存 Draft/Applied 点表。
- [ ] Debug 构建与 TCP 运行测试；等待用户启动包含本次修改的 SGStudio 并开启 SCPI 后执行。

测试脚本默认执行不制造错误的正向 transport/parser 测试、`*IDN?` 加 87 条现有业务查询，以及 13 条短格式/`SENSe:` 别名检查。业务模块在当前 UI/设备状态下不可用时记为跳过，但命令头返回 `-113` 仍判为失败：

```powershell
uv run --no-project python scripts/test_scpi_foundation.py
```

要求所有业务查询均必须有响应时使用严格模式：

```powershell
uv run --no-project python scripts/test_scpi_foundation.py --strict-business
```

需要覆盖代表性设置命令时，可显式执行数值原值回写与读回；`*RST` 仍需另外添加 `--include-reset` 才会执行：

```powershell
uv run --no-project python scripts/test_scpi_foundation.py --write-roundtrip
```

需要核对全部 97 条可安全探测的设置命令头时使用 `--probe-set-headers`，该模式会故意产生并读取预期的 `-109 Missing parameter`。List Mode 的 4 条无参数事件命令不由通用脚本主动执行。未注册头、`#H`、`#0` 和超长块头等负测试使用 `--negative-protocol`；其中 `#0` 和超长块头预期服务器主动关闭违规测试连接，配合 `--skip-fatal` 可跳过断连项。

## 风险与对应验证

| 风险 | 验证 |
|---|---|
| 删除 100 ms 后高频命令暴露隐含时序依赖 | 连续发送 Center/Level 设置查询以及 List 三列 + Apply，确认 FIFO 和同步快照边界 |
| payload 内 LF 被误切 | 构造包含 LF/CR/NUL 的 `REAL,64` block 并按多个 TCP 包发送 |
| 块头跨包或粘连下一命令 | 在 `#`、长度位数、长度字段和 payload 中间分别断包；一个 write 粘连多条命令 |
| 大报文仍落入 1024 buffer | 发送 100000 点列并确认走 `SCPI_Parse()`，无 input buffer overrun |
| 跨线程 socket 写入 | 大查询、客户端中途断开、连续多查询，确认无 Qt thread warning、崩溃或响应乱序 |
| 失败导致 Draft 部分更新 | 错误出现在列表末项、块长度错误、NaN/Inf 时确认原列完全不变 |
| 日志复制/显示大 payload 卡顿 | 二进制写入和大查询只显示摘要，实际网络数据完整 |

## 成功标准

- 列出最小必改文件和每个文件的具体职责变化。
- 明确保留项与不做项，避免扩大到现有全部 SCPI handler 重构。
- 覆盖 ASCII 大列表、IEEE 488.2 definite block、分包、粘包、CRLF、块内换行和超限输入。
- 明确删除 100 ms 的时机、影响和验证方式。
- 给出分阶段提交顺序，使每一步都可独立静态审查和测试。

## 验证级别

当前完成 `static`：代码差异检查、残留旧符号检查、Python AST/参数入口检查。未执行 C++ 构建，也未连接运行中的 SGStudio；Debug 构建与 TCP 联调待用户下一步通知。

## 现有命令回归脚本扩展（2026-09-04）

### 范围与成功标准

- 从当前 `ScpiRegister::init()` 的实际注册项建立查询命令目录，覆盖基础射频、Sweep、参考源、触发、LO/RF、数字调制、DSSS、OFDM、AM/FM/PM/PULM、多音、Ramp、AWGN 和 R&S List Mode。
- 默认逐条发送全部只读查询；有响应时校验响应非空且错误队列为零。业务插件、面板或设备状态导致 handler 不可用时，允许记为 `SKIP`，但 `-113 Undefined header` 必须判为失败。
- 对所有有设置接口的业务命令，在默认模式下发送“缺少参数”的无副作用探测，确认命令头仍已注册；预期得到参数错误或业务不可用错误，绝不允许 `-113`。
- 增加代表性的短格式、大小写不敏感和可选 `SENSe:` 前缀测试。
- 设备写入测试必须通过显式参数开启；使用查询到的当前数值原值回写，不改变目标值，并在写后重新查询。
- 保留原有 transport/parser 基础测试和 fatal framing 测试。

### 验证级别

- [x] Python AST 与命令行参数入口检查通过。
- [x] 脚本目录已扩展为 105 条业务查询和 `*IDN?`，以及 97 条无副作用缺参设置探测；List Mode 的 4 条事件命令通过静态注册清单核对，不在通用脚本中主动触发设备行为。
- [x] 默认测试增加 List Mode Source 路由：省略 Source 与 `SOURce1` 均返回 `AUTO`，`SOURce2` 必须返回 `-241 Hardware missing`。
- 不连接当前 SCPI 服务、不执行设备写入；运行验证等待用户启动包含本次修改的 SGStudio 后进行。

## 下一阶段：真正接入 MScan 业务层（2026-09-07）

### 阶段目标

把第 5 步已经注册的 R&S List Mode 命令从“协议面 + 临时兼容状态”接到 Core 业务层，使 SCPI 和 `ListModePanel` 使用同一个 MScan Draft，设备执行继续复用现有 `TxSessionService -> TxPipelineRuntime -> TxPipelineExecutor -> IDevice` 链路。

本阶段完成后：

- `ScpiRegister` 不再保存任何 List 状态，也不再返回 `MScanTableService is not available`。
- SCPI 写入三列后，UI 表格能看到同一份 Draft；UI 修改后，SCPI 查询能读取相同结果。
- `LIST:LEARn` 和 `FREQuency:MODE LIST` 使用提交时生成的不可变 MScan 快照，不受随后 Draft 修改影响。
- 设备成功后才更新 Applied；异步失败保留上一份 Applied。
- SCPI 不调用 `ListModePanel`、`StepSweepPanel`、`MainWindow` 或 Property UI 绑定对象。

### 当前代码事实

1. 当前没有 `MScanTableService`。List 数据仍由 `ListModePanel::m_model/m_timeModel`、`m_globalDwellTime` 和 range Property 持有。
2. `ScpiRegister` 已注册 36 个 R&S 命令头，但三列、`LEARn`、软件触发和 `FREQuency:MODE LIST` 仍停在 `-241` 边界；兼容标量暂存在 `mListCompatibilityState`。
3. `TxSessionService` 当前通过 `MainWindow` 注入的 `StepSweepPanel::fillCarrierPlanContext()` 获取 MScan 点表，仍然以 UI 为 MScan runtime 输入源。
4. MScan 设备执行链已经存在：`TxCarrierPlanContext::mscanPoints`、`TxPipelineExecutor::applySweepCw/applySweepPlayback`、`IDevice::setListSweep/getListSweep` 和 HTRA `FancyDevice` 都已接通。
5. `TxPipelineRuntime` 已提供串行异步、latest-intent、generation/epoch 过滤和成功 applied request；本阶段不新建第二套设备队列，也不直接调用 H2。
6. 设备能力已经通过 `CurrentDeviceCapabilitySnapshot::device.txCarrier` 提供频率、功率和 dwell 范围，可作为提交校验依据。

### 明确不做

- 暂不实现磁盘 `save/load`，R&S 文件容器仍是单内存表兼容外观。
- 暂不重构 Center、Level、调制等既有 SCPI UI 依赖命令。
- 暂不改变 5025/5125、parser、transport 和二进制块格式。
- 暂不把 `*OPC?/*WAI` 扩展成等待设备完成；成功仍表示同步解析、校验和请求提交完成。
- 暂不扩展 Playback/Streaming 的逐行波形选择。
- 不在 SCPI handler、UI model 和 runtime 中保留三份互相同步的表。

### 目标所有权

新增 `Core::MScanTableService`，由 `CoreRuntimeServices` 创建并持有，作为以下状态的唯一 owner：

| 状态 | 内容 | 更新时机 |
|---|---|---|
| Draft | 三个独立列、全局 dwell、dwell 模式、0-based start/stop | UI 或 SCPI 编辑时同步更新 |
| Submitted | 从某一 Draft revision 校验并切片得到的不可变 `QVector<TxMScanPoint>` | Apply、Learn 或切入 LIST 前 |
| Applied | 最近一次 runtime 成功接受并完成的 MScan request/回读快照 | `TxSessionService::appliedStateChanged` 成功后 |
| Compatibility | logical index、RMode、R&S logical filename | 对应兼容命令设置时 |

内部统一单位：频率 Hz、功率 dBm、dwell 秒、索引 0-based。UI 的 1-based range 只在 `ListModePanel` 适配层转换。

三列 Draft 允许暂时长度不一致；Submitted 必须满足：

- 三列非空且长度完全一致；
- Draft 三列各不超过 100000 点，所选 `[start, stop]` 区间不超过 32767 点；
- `0 <= start <= stop < pointCount`；
- 所有值有限；
- 当前设备和 `txCarrier` 能力可用；
- 频率、功率和最终生效 dwell 均位于当前设备能力范围内。

校验失败不得改变 Submitted、Applied 或 runtime desired request。

### 建议的 Core API

文件：`src/plugins/core/mscantableservice.h/.cpp`。

建议保持窄接口，不让 SCPI 认识 UI Profile：

```cpp
class CORE_EXPORT MScanTableService : public QObject
{
    Q_OBJECT
public:
    static MScanTableService *instance();

    MScanDraftSnapshot draft() const;
    MScanSubmittedSnapshot submitted() const;
    MScanAppliedSnapshot applied() const;
    MScanCompatibilitySnapshot compatibility() const;

    MScanResult replaceFrequencies(const QVector<double> &hz);
    MScanResult replaceLevels(const QVector<double> &dbm);
    MScanResult replaceDwellTimes(const QVector<double> &seconds);
    MScanResult replaceDraft(const MScanDraftSnapshot &draft);

    MScanResult setGlobalDwell(double seconds);
    MScanResult setDwellMode(MScanDwellMode mode);
    MScanResult setRange(int startIndex, int stopIndex);
    MScanResult setStartIndex(int startIndex);
    MScanResult setStopIndex(int stopIndex);

    MScanResult prepareSubmittedSnapshot();
    bool fillSubmittedCarrier(Core::TxCarrierPlanContext *carrier) const;
    MScanResult recordApplied(const MScanSubmittedSnapshot &submission,
                              const Core::TxCarrierPlanContext &carrier);

signals:
    void draftChanged(quint64 revision);
    void compatibilityStateChanged();
    void appliedChanged();
};
```

`MScanResult` 使用 Core 枚举表达错误类别和短消息，不引用 libscpi 错误号；SCPI 插件负责最终错误映射。`draft()` 返回值快照，避免 UI 或 SCPI 获得内部可写引用。

### Draft、Submitted、Applied 语义

1. 三列 setter 先在 SCPI parser 中完成语法、单位、block、NaN/Inf 和 100000 点 Draft 上限检查，再由服务原子替换单列。
2. 单列更新不要求另外两列等长，因此上传顺序可以是 frequency → power → dwell。
3. `prepareSubmittedSnapshot()` 在主线程同步校验完整 Draft 和当前 capability，按闭区间 `[start, stop]` 生成独立 `QVector<TxMScanPoint>`，记录 Draft revision。
4. `TxSessionService` 只能读取 Submitted，不得在异步执行期间读取可变 Draft。
5. Draft 后续变化只增加 Draft revision，不修改已提交快照；下一次 Apply/Learn/LIST 才生成新 Submitted。
6. runtime 成功时，用 `TxSessionService::appliedRequest().context.carrier` 更新 Applied；失败或 stale completion 不覆盖旧 Applied。
7. 设备回读点数和值如果与请求不同，Applied 保存有效回读；Draft 是否回写由 UI 明确显示，不在 SCPI handler 中暗改。

### 触发和模式映射

List trigger 不保存在 MScan 服务中。H2 只有一套 Trigger In 寄存器，`CommonDeviceProfile` 是唯一 owner；`LIST:TRIGger:SOURce` 直接原子更新公共 trigger source/count/action：

| SCPI | `CommonDeviceProfile` 中的行为 |
|---|---|
| `LIST:TRIGger:SOURce AUTO` | BUS，repeat/response count 为连续值 `-1` |
| `LIST:TRIGger:SOURce SINGle` | BUS，repeat/response count 为 `1`；`EXECute` 调用现有 `requestSweepExecution()` |
| `LIST:TRIGger:SOURce EXTernal` | EXTERNAL，单个外部事件启动一次整表；边沿沿用公共 trigger edge |

设备成功 writeback 继续回写同一个 `CommonDeviceProfile`，不再存在 request-local decorator 和公共 Trigger 两套状态。查询从公共状态反向推导；XPPS 无法表示为 R&S 的三个枚举时返回状态冲突，不伪装成 EXTERNAL。

`LIST:MODE AUTO/STEP/INDex` 继续全部接受，effective mode 恒为 `AUTO`。`STEP/INDex` 不改变点表和硬件执行方式。

`FREQuency:MODE LIST` 的同步顺序：

1. 校验并生成 Submitted；
2. 通过 `TxSessionService` 的统一 CarrierPlan 意图入口选择 `CarrierPlanKind::MScan`；
3. 由该入口发出状态变化并请求 refresh；
4. handler 返回“请求已接受”，不等待设备线程。

`FREQuency:MODE CW` 通过同一入口切回 Fixed carrier。`TxSessionService` 是 CarrierPlan 意图的唯一 owner；UI 只提交显式用户操作并监听变化回显，`MainWindow::syncTxSessionState()` 不再从面板状态重新推导 CarrierPlan。

`LIST:LEARn` 调用与后续 SG `LIST:APPLy` 相同的内部 prepare/apply 操作：只重新生成 Submitted；如果当前已处于 LIST，则请求 refresh，否则仅准备快照，不提前启动 RF 扫描。本阶段不顺带新增 `LIST:APPLy` 命令头。

`LIST:TRIGger:EXECute` 只通过 `TxSessionService` 的串行执行入口触发，不允许从 SCPI 或 MScan 服务直接调用 `IDevice/FancyDevice/channel_bus_trigger()`。若当前不是 LIST、没有有效 Submitted 或触发状态不允许执行，返回状态冲突。

### UI 接入规则

`ListModePanel` 变为服务客户端，而不是业务数据 owner：

- 构造后读取 Draft snapshot 填充 `m_model/m_timeModel`；这些 model 只作为显示和编辑缓存。
- UI 的填充、插入、删除、清空、文件读取、dwell 和 range 修改最终调用服务；不再仅靠 `emit argsChanged()` 把表隐式交给 runtime。
- 监听 `draftChanged(revision)`，把 SCPI 修改回写到界面；使用现有写回 guard 防止 signal 回环。
- UI 整表编辑只能调用 `replaceDraft()`，服务一次性替换；SCPI 单列写入仍调用独立列接口。
- 当 SCPI 三列暂时不等长时，UI 可以只显示完整行，但不得因刷新或无关 UI 事件截断服务中的较长列；只有明确的 UI 整表提交才能替换三列。
- `StepSweepPanel::fillCarrierPlanContext()` 不再为 MScan 从 `ListModePanel` 组点；FScan/LScan 仍可暂时沿用当前 provider。
- `listModeProfileMap()` 和 restore 改为与服务 snapshot 转换，使持久化包含 SCPI 编辑后的 Draft。

### `TxSessionService` 接入规则

- `fillCarrierPlanContext()` 遇到 `CarrierPlanKind::MScan` 时直接调用 `MScanTableService::fillSubmittedCarrier()`；FScan/LScan 继续使用现有 UI provider。
- CarrierPlan 由 `TxSessionService` 持有并对 UI/SCPI 暴露同一个语义入口；普通 refresh 只消费已有意图。
- `buildApplyRequest()` 始终从 `CommonDeviceProfile` 获取唯一的 trigger source/count/action，不调用 List trigger decorator。
- 保留当前 `TxPipelineRuntime` 的 immutable request、latest-intent 和 generation 过滤，不新增等待或重试。
- runtime 成功后通知 MScan 服务确认 Applied；失败只记录业务失败状态，绝不从异步线程直接写 libscpi error queue。
- `RUNNing?` 基于当前成功 applied request 是否为有效 MScan Sweep pipeline，以及 `TxSessionService::carrierPlan()` 是否为 MScan；不得只看 UI 按钮。

### SCPI handler 改造

`src/plugins/scpi/register/scpiregister.*` 保留现有 36 个注册项和 Source validator，替换 `handleListCommand()` 内部实现：

| 命令族 | 服务调用 |
|---|---|
| 三列设置/查询/点数 | `replaceXxx()` / `draft()` |
| dwell、mode、range | 服务标量 setter/query |
| `INDex/RESet`、`RMODe` | 服务 compatibility state |
| `LEARn` | `prepareSubmittedSnapshot()`，激活时 refresh |
| trigger source | 原子更新/查询 `CommonDeviceProfile` |
| trigger execute | `TxSessionService::requestSweepExecution()` |
| `RUNNing?` | 查询 `TxSessionService` 当前成功 Applied pipeline |
| 文件容器 | 服务 logical filename façade |
| `FREQuency:MODE` | 查询/修改 `TxSessionService` CarrierPlan intent |

完成接线后删除：

- `ScpiRegister::ListCompatibilityState`；
- `mListCompatibilityState`；
- `serviceUnavailable` lambda；
- 数据列固定返回 0 或 `-241` 的占位路径。

数组查询使用 C locale 和 `std::numeric_limits<double>::max_digits10` 格式化 ASCII 逗号列表，在原 5025 socket 返回。不要把大数组转到 5125 广播端口。

### Core 错误到 SCPI 错误映射

| Core 失败 | SCPI 错误 |
|---|---|
| 无当前设备/能力不可用 | `-241 Hardware missing` |
| 数值或索引超设备范围 | `-222 Data out of range` |
| Draft 列超过 100000 点，或提交范围超过 32767 点 | `-223 Too much data` |
| 三列不等长、空表、range 冲突、未准备即启动 | `-221 Settings conflict` |
| 非法枚举/文件名 | `-224 Illegal parameter value` |
| 内部不变量或请求提交失败 | `-200 Execution error` |

libscpi 已经产生的 `-109/-131/-161` 不要再次包装成第二个业务错误。

### 文件级实施顺序

#### 6.1 新增 Core 表服务

- 新增 `src/plugins/core/mscantableservice.h/.cpp`。
- 在 `src/plugins/core/CMakeLists.txt` 纳入构建。
- 在 `CoreRuntimeServices` 中创建服务并公开只读 getter；生命周期早于 UI 和 SCPI handler 使用。
- 先实现状态、原子列替换、revision、完整校验、Submitted/Applied，不做 UI 或设备调用。

#### 6.2 接入 TxSession

- 修改 `txsessionservice.*`：MScan context 从服务 Submitted 获取；将 CarrierPlan 提升为服务权威意图并提供变化信号。
- 把 runtime 成功 applied request 回传给服务。
- 不修改 `txpipelineexecutor.*`、`idevice.*` 和 `fancydevice.*`，除非构建或实测证明现有 MScan 链缺少必要契约。

#### 6.3 迁移 UI owner

- 修改 `listmodepanel.*`：初始化、编辑发布、SCPI 回写和 feedback guard。
- 修改 `stepsweeppanel.*`：删除 MScan runtime 取数对 UI model 的依赖，保留 FScan/LScan 当前实现。
- 修改 `runtimeprofilepersistence.cpp` 和必要的 profile 转换，确保启动恢复走服务。
- 修改 `mainwindow.cpp` 的 MScan writeback/模式同步；删除 `syncTxSessionState()` 中由 UI 反推 CarrierPlan 的逻辑。

#### 6.4 接通 SCPI

- 修改 `scpiregister.*`：删除临时状态，所有 List handler 调服务并统一错误映射。
- 保留现有 parser worker → GUI/main thread blocking invocation；服务方法必须在返回前完成 Draft/Submitted 的同步修改。
- 扩展查询格式化和真实点数返回。

#### 6.5 测试

- 扩展 `scripts/test_scpi_foundation.py`，增加三列 ASCII/REAL64 写入、点数、读回、range、dwell、MODE 降级、RMODE、文件外观和 Source2 错误。
- 设备写入测试继续使用显式参数开关，默认不主动启用 RF。
- 增加 Core 单元级测试仅在仓库已有合适测试 target 时进行；不为本任务新造测试框架。

### 实施提交边界

上述 6.1～6.4 应在同一功能分支完成后再作为可运行版本交付。中间提交可以编译，但不能把“UI 表”和“SCPI 表”长期留成两个独立 owner。推荐提交顺序：

1. service 数据模型与纯状态校验；
2. TxSession 消费 Submitted；
3. UI 迁移到 service；
4. SCPI 删除占位并接 service；
5. 测试和文档收口。

### 验证等级与成功标准

下一阶段要求 `debug-build + debug-run`，不能只做静态检查。

静态：

- `ScpiRegister` 的 List 路径不包含 widget/MainWindow/PropertyManager 访问。
- `StepSweepPanel::fillCarrierPlanContext()` 不从 UI model 构造 MScan runtime 点表。
- `ListCompatibilityState` 和全部 `-241` 占位分支已删除。
- MScan 只有一个 Draft owner，runtime 只读取 Submitted 快照。
- CarrierPlan 只有 `TxSessionService` 一个 owner，普通 UI refresh 不覆盖 SCPI 意图。
- List trigger 不存在 request-local owner，设置和查询都使用 `CommonDeviceProfile`。

Debug build：

- Core、SCPI、HTRA 插件正常构建，moc/metatype 和跨 DLL 导出无错误。

无设备运行：

- 三列设置、查询、点数、range、MODE/RMODE 和文件外观可用。
- SCPI 修改后 UI 可见，UI 修改后 SCPI 可读。
- `FREQuency:MODE LIST` 在无设备时返回 `-241`，Draft 保持不变。

有设备运行：

- CW MScan 全表及 `[start, stop]` 子集均能执行。
- AUTO/SINGLE/EXTERNAL 映射按整表触发语义工作。
- 快速发送三列 → Learn/List → 再改 Draft 时，设备使用提交时快照。
- 成功后 `RUNNing?` 和 Applied 更新；失败后保留上一份 Applied。
- `STEP/INDex` 设置成功且 `MODE?` 返回 `AUTO`，错误队列保持 0。
- Draft 第 100001 点、提交范围第 32768 点、列长冲突、越界值和非法 Source 返回约定错误，进程和后续连接保持可用。

### 下一步开始实施前的检查点

- 先重新读取上述所有待改文件，防止覆盖 2026-09-04 之后的用户修改。
- 先确认当前工作树中 `scpiregister.*` 和测试脚本的 staged/unstaged 状态，不改写用户已有暂存边界。
- 首个代码改动从 `MScanTableService` 和 Core 生命周期开始；在服务接口稳定前不继续扩张 SCPI switch。
