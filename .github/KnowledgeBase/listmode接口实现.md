# 最终设计：SGStudio `SOURce:LIST` 子系统（R&S 兼容）

### 0. 依据（观察）

- H2 只有 `tx_config_mscan(fc[], level[], dwell[], int16 n)` / `tx_query_mscan(...)`：整表一次下发、硬件按 dwell 自主顺序执行、无当前点索引读回、点数不超过 32767。
- UI、Core 服务和 SCPI 的 Draft 整表上限为 100000 个有效点；UI 自动数据源单次最多生成 20000 行。H2 一次只能执行 32767 点，因此在提交 `[STARt, STOP]` 选中范围时检查区间长度；超限只拒绝本次提交，不截断或丢弃完整 Draft。
- H2 触发源作用于整段序列开始，不支持 R&S `STEP` 模式的“每个触发只前进一步”，也不支持 `INDex` 模式的硬件单点定位。
- 已有用户脚本大量采用 R&S List Mode 命令。兼容目标因此从“只保留本机真实能力”改为“R&S 命令面完整保留，本机能力按明确规则降级”。
- 现有 SCPI 传输仍存在按换行切块、1024 字节输入缓冲和固定 100 ms 延时等问题，二进制列表下发前必须先改造传输和解析层。

### 1. 兼容目标

兼容分为三类：

1. **真实执行**：H2 已具备对应能力，命令直接读写真实业务状态。
2. **兼容映射**：R&S 命令映射到 SGStudio 已有的等价业务操作。
3. **接口兼容**：命令和合法参数必须被接受；如果请求值无法生效，查询返回实际生效值，底层不执行不受支持的硬件动作。

“完全兼容”指 R&S 脚本使用的命令头、长短关键字、设置格式和查询接口均可识别，不因本机缺少硬件能力而返回 `Undefined header`。这不代表 H2 具备与 R&S 相同的硬件执行能力。

通用规则：

- 支持 R&S 的 `[:SOURce<hw>]` 形式；省略 `<hw>` 时默认通道 1。
- 单通道设备接受 `SOURce` 和 `SOURce1`；只有实际存在对应 RF 通道时才允许 `SOURce2` 等其他通道。
- SCPI 关键字大小写不敏感，并支持规范中的短关键字，例如 `FREQ`、`POW`、`DWEL`、`IND`。
- 合法但本机不支持的枚举按下表的兼容语义处理，不进入错误队列。
- 非法枚举、非法参数、越界值和格式错误仍按 SCPI 规范报错。

### 2. R&S 命令兼容矩阵

| R&S 命令 | 兼容级别 | SGStudio 行为 |
|---|---|---|
| `LIST:FREQuency`、`LIST:POWer`、`LIST:DWELl:LIST` | 真实执行 | 写入 Draft 的三列数据，支持 ASCII 列表和 IEEE 488.2 定长块 |
| `LIST:xxx:POINts?` | 真实执行 | 返回对应 Draft 列的点数 |
| `LIST:DWELl`、`LIST:DWELl:MODE` | 真实执行 | 支持全局驻留时间和逐点驻留时间模式 |
| `LIST:INDex:STARt/STOP` | 真实执行 | 0-based 闭区间，映射现有 UI 的 1-based `rangeFrom/rangeTo` |
| `LIST:INDex <n>` / `LIST:INDex?` | 接口兼容 | 缓存并查询逻辑索引；不改变硬件当前输出点，不宣称是设备实时索引 |
| `LIST:RESet` | 接口兼容 | 将逻辑索引重置为 `INDex:STARt`；不操作 H2 当前点 |
| `LIST:MODE AUTO` | 真实执行 | H2 按范围自动执行整张表 |
| `LIST:MODE STEP\|INDex` | 接口兼容 | 设置命令静默接受并归一化为 `AUTO`；`MODE?` 返回实际生效值 `AUTO`，H2 按整张表执行 |
| `LIST:TRIGger:SOURce AUTO` | 兼容映射 | 映射为内部/总线启动并连续运行 |
| `LIST:TRIGger:SOURce SINGle` | 兼容映射 | 映射为 BUS 单次启动；一次触发执行一整张表，不是一个点 |
| `LIST:TRIGger:SOURce EXTernal` | 兼容映射 | 映射公共外部触发源；一个外部边沿启动一整张表，不是一个点 |
| `LIST:TRIGger:EXECute` | 兼容映射 | 等价于 `*TRG`，触发一次整表执行 |
| `LIST:RMODe LIVE\|LEARned` | 接口兼容 | 接受并缓存枚举，`RMODe?` 原样返回；两者使用同一套 H2 整表配置路径 |
| `LIST:LEARn` | 兼容映射 | 等价于 `LIST:APPLy`：同步校验并提交不可变整表快照，设备下发和回读沿现有 runtime 链路异步完成 |
| `LIST:RUNNing?` | 真实执行 | 返回 MScan 真实运行状态 `1` 或 `0` |
| `LIST:SELect`、`CATalog?`、`DELete`、`DELete:ALL`、`FREE?` | 接口兼容 | 首版提供单内存表的文件容器兼容外观，不进行磁盘保存或加载 |
| `FREQuency:MODE CW\|LIST` | 真实执行 | `LIST` 同步校验并提交启动请求；`CW` 提交停止 MScan 并切回定频的请求，设备操作沿现有 runtime 链路异步完成 |

上述接口兼容命令不能被优化删除。即使底层没有动作，也必须注册设置和查询处理函数，并返回稳定结果。

### 3. 状态模型

- **Draft**：频率、功率、逐点 dwell、全局 dwell、dwell 模式和扫描范围。三列可在编辑过程中暂时长度不一致。
- **Submitted**：从某一 Draft revision 完整校验后，按 `[STARt, STOP]` 复制得到的不可变点表，并绑定设备 UID 与 capability revision。后续 Draft 修改不影响已经生成的 Submitted。
- **Applied**：`LIST:APPLy`、`LIST:LEARn` 或 `FREQuency:MODE LIST` 对应的异步设备操作成功并完成回读后形成。命令处理阶段同步校验列长、范围、有限值、dwell、选中区间不超过 32767 点和设备能力，并在返回前生成只包含 `[STARt, STOP]` 范围的不可变提交快照；后续 Draft 修改不得改变已提交的快照。
- **Compatibility State**：仅为 R&S 脚本往返查询保存 `LIST:INDex`、`LIST:RMODe` 和逻辑文件名等值，不作为硬件真实状态。`LIST:MODE` 不保存无法生效的请求值，其有效状态恒为 `AUTO`。

Draft、Submitted、Applied 和上述 Compatibility State 的拥有者为 Core 级 `MScanTableService`。`ListModePanel` 和 SCPI 都是它的客户端；SCPI 不调用面板。设备提交仍通过 `TxSessionService` 完成。

CarrierPlan/FrequencyMode/Running 状态由 `TxSessionService` 统一持有，普通 UI refresh 不得从面板反推并覆盖它；List Trigger 直接读写唯一的 `CommonDeviceProfile` trigger source/count/action。`MScanTableService` 不保存这两类状态，也不提供 request-local trigger decorator。

复位默认值：

| 状态 | `*RST` 后的值 |
|---|---|
| `LIST:INDex` | `0` |
| `LIST:INDex:STARt` | `0` |
| `LIST:MODE` | `AUTO` |
| `LIST:RMODe` | `LIVE` |
| `LIST:DWELl:MODE` | `GLOBal` |
| `LIST:TRIGger:SOURce` | `AUTO` |

### 4. R&S 兼容命令清单

```text
# 数据列
[:SOURce<hw>]:LIST:FREQuency <f1>{,<fn>} | #<block>
[:SOURce<hw>]:LIST:FREQuency?
[:SOURce<hw>]:LIST:FREQuency:POINts?
[:SOURce<hw>]:LIST:POWer <p1>{,<pn>} | #<block>
[:SOURce<hw>]:LIST:POWer?
[:SOURce<hw>]:LIST:POWer:POINts?
[:SOURce<hw>]:LIST:DWELl:LIST <d1>{,<dn>} | #<block>
[:SOURce<hw>]:LIST:DWELl:LIST?
[:SOURce<hw>]:LIST:DWELl:LIST:POINts?

# 全局驻留时间和驻留模式
[:SOURce<hw>]:LIST:DWELl <time>
[:SOURce<hw>]:LIST:DWELl?
[:SOURce<hw>]:LIST:DWELl:MODE GLOBal|LIST
[:SOURce<hw>]:LIST:DWELl:MODE?

# 索引和扫描范围
[:SOURce<hw>]:LIST:INDex <index>
[:SOURce<hw>]:LIST:INDex?
[:SOURce<hw>]:LIST:INDex:STARt <index>
[:SOURce<hw>]:LIST:INDex:STARt?
[:SOURce<hw>]:LIST:INDex:STOP <index>
[:SOURce<hw>]:LIST:INDex:STOP?
[:SOURce<hw>]:LIST:RESet

# 运行方式和硬件学习兼容
[:SOURce<hw>]:LIST:MODE AUTO|STEP|INDex
[:SOURce<hw>]:LIST:MODE?
[:SOURce<hw>]:LIST:RMODe LIVE|LEARned
[:SOURce<hw>]:LIST:RMODe?
[:SOURce<hw>]:LIST:LEARn

# 触发和运行状态
[:SOURce<hw>]:LIST:TRIGger:SOURce AUTO|SINGle|EXTernal
[:SOURce<hw>]:LIST:TRIGger:SOURce?
[:SOURce<hw>]:LIST:TRIGger:EXECute
[:SOURce<hw>]:LIST:RUNNing?

# R&S 文件容器命令面；首版仅提供单内存表兼容状态
[:SOURce<hw>]:LIST:SELect "<filename>"
[:SOURce<hw>]:LIST:CATalog?
[:SOURce<hw>]:LIST:DELete "<filename>"
[:SOURce<hw>]:LIST:DELete:ALL
[:SOURce<hw>]:LIST:FREE?

# 模式总开关
[:SOURce<hw>]:FREQuency:MODE CW|LIST
[:SOURce<hw>]:FREQuency:MODE?
```

单位兼容规则：

- `LIST:FREQuency` 裸数值默认 `Hz`，接受 `kHz/MHz/GHz`。
- `LIST:POWer` 裸数值默认 `dBm`。
- `LIST:DWELl` 全局驻留时间裸数值按秒解释，接受 `ms/us` 后缀。
- 为兼容既有 R&S 脚本，`LIST:DWELl:LIST` 的 ASCII 裸数值默认按微秒解释；显式 `s/ms/us` 后缀优先。
- 二进制块必须根据统一的数值格式和字节序设置解码；未建立 `FORMat` 状态前，首版至少支持 ASCII 和明确约定的 `REAL,64`，不能依靠块长度猜测 `REAL,32/64`。

### 5. SGStudio 扩展命令

以下命令不属于 R&S 兼容面的必要部分，但保留为 SGStudio 原生能力：

```text
[:SOURce<hw>]:LIST:CLEar
[:SOURce<hw>]:LIST:APPLy
[:SOURce<hw>]:LIST:APPLied? FREQuency|POWer|DWELl
[:SOURce<hw>]:LIST:MODE:CATalog?
:INITiate:CONTinuous ON|OFF
:INITiate:CONTinuous?
:SYSTem:ERRor?
*OPC?
*WAI
```

- `LIST:CLEar` 清空 Draft 三列。
- `LIST:APPLy` 同步完成校验和不可变快照提交，整表下发与设备回读继续使用现有异步 runtime 链路；`LIST:LEARn` 是它的 R&S 兼容别名。
- `LIST:APPLied?` 返回设备回读的已生效列。
- `LIST:MODE:CATalog?` 返回当前设备真正支持的 List 执行模式，首版固定返回 `AUTO`。
- `INITiate:CONTinuous` 可供 SGStudio 原生脚本显式选择单次或连续运行，但 R&S 脚本优先使用 `LIST:TRIGger:SOURce`。

### 6. 不支持能力的精确定义

#### 6.1 `LIST:INDex` 和 `LIST:RESet`

- `LIST:INDex <n>` 校验 `n` 位于当前 Draft 列表范围内，然后保存为逻辑索引。
- `LIST:INDex?` 返回最后设置的逻辑索引。
- `LIST:RESet` 将逻辑索引设置为 `LIST:INDex:STARt`。
- 这三个操作不调用 H2，不改变正在输出的点，也不用于生成 Applied 快照。

#### 6.2 `LIST:MODE STEP|INDex`

- `AUTO`、`STEP`、`INDex` 都是合法参数，设置命令均返回成功，且不产生任何 SCPI 响应数据。
- 设置 `STEP` 或 `INDex` 时不写入 SCPI 错误队列，内部直接归一化为 `AUTO`；可以记录诊断日志说明发生了降级。
- `LIST:MODE?` 始终返回实际生效模式 `AUTO`，不回显无法生效的请求值。
- `LIST:MODE:CATalog?` 固定返回 `AUTO`，供新脚本在设置前查询设备能力。
- H2 的执行模式恒为整表 `AUTO`：`STEP` 不会逐触发前进一步，`INDex` 不会只输出逻辑索引指定的点。
- 切入 `FREQuency:MODE LIST` 时不得因为模式是 `STEP` 或 `INDex` 而拒绝应用。
- `*OPC?` 只表示命令处理完成，不能用于判断请求模式是否受支持。

#### 6.3 `LIST:RMODe` 和 `LIST:LEARn`

- `LIST:RMODe LIVE|LEARned` 保存兼容枚举；两者不改变 H2 配置算法。
- `LIST:RMODe?` 返回最后设置的枚举。
- `LIST:LEARn` 不做 R&S 特有的硬件预计算，直接执行与 `LIST:APPLy` 相同的原子校验、下发和回读。

#### 6.4 R&S 文件容器命令

首版不实现磁盘保存和加载，但必须注册 R&S 文件命令：

- `LIST:SELect` 校验并缓存逻辑文件名；切换名称不切换 Draft 表。
- `LIST:CATalog?` 返回当前缓存的逻辑文件名；没有名称时返回空目录结果。
- `LIST:DELete` 和 `LIST:DELete:ALL` 只清除匹配的逻辑文件名，不删除磁盘文件，也不清空 Draft。
- `LIST:FREE?` 返回可用于当前内存表的非负容量值，单位为字节；不得返回语法错误或未定义命令。

后续加入真实文件持久化时保持同一命令头，不再新增 `LIST:LOAD/STORe` 作为主接口。

### 7. R&S 脚本兼容示例

以下脚本可以保持 R&S 写法不变：

```scpi
*RST; *CLS
:SOURce1:LIST:SELect "freq_hop.lsw"
:SOURce1:LIST:FREQuency 1GHz,1.2GHz,1.5GHz,1.8GHz,2GHz
:SOURce1:LIST:POWer -10,-10,-5,-5,0
:SOURce1:LIST:DWELl:LIST 200,200,500,500,1000
:SOURce1:LIST:DWELl:MODE LIST
:SOURce1:LIST:INDex:STARt 1
:SOURce1:LIST:INDex:STOP 3
:SOURce1:LIST:MODE AUTO
:SOURce1:LIST:TRIGger:SOURce AUTO
:SOURce1:LIST:RMODe LEARned
:SOURce1:LIST:LEARn
:SOURce1:FREQuency:MODE LIST
:SOURce1:LIST:RUNNing?
```

不支持的 R&S 步进脚本也能完成命令解析和查询，但执行仍是整表模式：

```scpi
:SOURce1:LIST:MODE STEP
:SOURce1:LIST:MODE?          // 返回 AUTO，表示实际生效模式
:SOURce1:LIST:MODE:CATalog?  // 返回 AUTO，表示当前仅支持 AUTO
:SOURce1:LIST:INDex 2
:SOURce1:LIST:INDex?         // 返回 2，属于逻辑索引
:SOURce1:LIST:RESet
:SOURce1:LIST:INDex?         // 返回 INDex:STARt
```

### 8. SCPI 底层最小改造方案

本节只处理 List Mode 上线必需的 SCPI 基础设施，不借机改造现有全部 UI/Property 型 handler，也不在本阶段引入全局异步完成状态机。

#### 8.1 当前链路与本次边界

当前控制链路为：

```text
TCP 5025 -> TransportTCP 分帧 -> Parser 单工作线程/FIFO
         -> ScpiRegister handler -> 业务状态或 runtime 异步请求
         -> Parser 聚合查询结果 -> 原 TCP 连接返回
```

已确认：

- `Parser` 的 FIFO 队列保证 SCPI program message 按入队顺序进入 handler。
- Center、Level 等设备控制最终通过现有业务/runtime 链路异步执行；parser 每条命令结束后的固定 100 ms 既不等待设备完成，也不提供完成成功保证。
- 100 ms 位于 handler 和响应回调之后，只会把下一条命令人为推迟；它不是 TCP 分帧条件、主线程安全条件或 H2 完成条件。
- List handler 必须在返回前完成参数解析、Draft 原子替换或 apply 快照生成。后续设备下发可以异步，因此删除 100 ms 后仍可保证 `FREQ -> POW -> DWEL -> APPLY` 看到确定的 Draft 顺序。

本次明确保留：

- 保留一个 `Parser` 工作线程、一个 FIFO 命令队列和当前命令注册/回调类型。
- 保留现有 handler 必要的线程切换方式，不批量迁移旧命令到新业务模块。
- 保留现阶段 `*OPC?` 和 `*WAI` 行为：它们只表示此前 handler 已处理或接受请求，不表示异步设备操作已经完成。
- 保留 5125 为现有输出广播数据通道；List 写入和查询响应都走发起请求的 5025 控制连接。

本次明确不做：

- 不建立每个 SCPI 命令的 future/promise、apply generation 等待器或全局 operation status。
- 不把现有 Center、Level、Sweep 等 SCPI handler 全部改成直连业务服务。
- 不把错误队列改成每 TCP 客户端一份，也不从 runtime 异步线程直接操作 libscpi context。
- 不实现 `#0` 不定长块，不复用 5125 接收 List 数据。

#### 8.2 必改文件与职责

| 文件 | 必需改造 | 不扩张的边界 |
|---|---|---|
| `src/libs/scpi/src/transport/transport_tcp.h/.cpp` | 按连接保存分帧状态；识别 definite-length block；增加单连接上限；把查询响应投递回 socket 所在线程 | 不新增第二套协议，不改变监听端口 |
| `src/libs/scpi/src/parser/parser.cpp` | 完整报文改用 `SCPI_Parse`；删除固定 100 ms；每条报文开始前重置结果类型和结果缓冲 | 保留单线程 FIFO，不重写 dispatcher |
| `src/libs/scpi/include/scpi/scpi_context.h`、`src/libs/scpi/src/scpi_context.cpp` | 增加 Source 通道号读取和 List 数值数组解析接口；统一 ASCII/块错误 | 不把 MScan 业务校验塞入通用 SCPI 库 |
| `src/libs/scpi/src/engine/scpi_engine.cpp` | 大报文和二进制块只记录命令头、方向和字节数；大查询响应日志限长 | 不改变 command/reply callback 公共签名 |
| `src/plugins/scpi/register/scpiregister.*` | 后续用 `[SOURce#:]LIST:...` 注册命令并调用上述 helper | 此文件只做协议适配，不拥有 Draft/Applied 表 |

`helper.cpp` 现有 `QString` 文本输出和 `QByteArray -> SCPI_ResultArbitraryBlock` 已能承载结果，首版 List 查询返回 ASCII，因此暂不需要修改结果类型体系。

#### 8.3 TCP program message 分帧

`TransportTCP::onReadyRead()` 不能继续直接使用 `indexOf('\n')`。每个客户端需要维护接收缓冲和扫描位置，抽取函数返回三种结果：`Complete`、`NeedMoreData`、`FatalFramingError`。

扫描规则：

1. 普通状态下只在字符串和 definite block 之外把 LF 当作 program message 结束符；结束符前的一个 CR 被移除。
2. 跟踪单引号和双引号，正确跳过成对引号转义；引号中的 `#` 不启动块解析。
3. 仅把 `#<n><length><payload>` 且 `n` 为 `1..9` 识别为 IEEE 488.2 定长块。`#H/#Q/#B` 是 SCPI 非十进制数值前缀，不能误判成块。
4. 块头不完整时保留缓冲等待下一次 `readyRead`；长度字段完整后，按声明的 payload 字节数直接跳过，payload 中的 LF、CR、NUL、引号和 `#` 均不得参与分帧。
5. 块 payload 结束后继续扫描，允许同一 program message 后面还有分号命令；只有块外 LF 才结束整条 message。
6. 同一次 `readyRead` 中循环抽取所有完整 message，覆盖 TCP 粘包；未完成的尾部保留，覆盖 TCP 分包。
7. 单连接接收缓冲和块声明长度上限均固定为 16 MiB。Draft 上限 100000 个 `REAL,64` 点时每列约 782 KiB，16 MiB 有足够余量，同时可阻止无限增长。
8. `#0`、长度字段非十进制、长度计算溢出或声明长度超过上限属于无法可靠恢复边界的 framing error：记录不含 payload 的诊断信息，清空该连接缓冲并关闭该连接。

传输层发生 fatal framing error 时不直接写 libscpi 错误队列：transport 与 parser 不在同一线程，而且错误报文边界本身已经不可信。能够形成完整报文的参数、块格式和值错误才交给 handler 写入 `SYSTem:ERRor?` 队列。

#### 8.4 解析器容量与 100 ms 删除

`Parser::onCommandWorker()` 按以下顺序处理一条完整报文：

1. 出队后立即清空 `mCommandResultBuffer`，并把 `mIsByteArray` 重置为 `false`，防止上一条查询的结果类型污染当前命令。
2. 保留现有“没有结束符则补 LF”的处理。
3. 调用 `SCPI_Parse(&mScpiContext, input.data(), checkedLength)`，不再调用 `SCPI_Input()`。transport 已经完成报文累积，继续使用 `SCPI_Input()` 只会触发其 1024 字节内部缓冲上限。
4. 解析结束后立即执行当前 reply callback，然后处理 FIFO 中下一条命令。
5. 删除 `kCommandDelayTimes` 和 `QThread::msleep(100)`，不增加新的固定 sleep、重试或轮询。

`SCPI_Init()` 仍需要传入内部 input buffer，可以暂时保留现有 1024 字节数组；完整 TCP 报文已经直接交给 `SCPI_Parse()`，该数组不再限制 List 报文长度。16 MiB transport 上限也保证传给 libscpi 的长度安全落入其 `int` 参数范围。

删除延时后的语义：

- 设置命令无响应；handler 返回成功表示参数有效、状态已同步更新或异步请求已被接受。
- 查询命令仍在 handler 生成结果后响应。
- `*OPC?` 在 FIFO 中排到前序 handler 之后，当前仍返回 `1`；它不等待设备异步完成。
- `*WAI` 当前仍为兼容 no-op。若以后确实需要“设备完成”语义，应单独设计 operation generation，而不是恢复经验性 sleep。

#### 8.5 List 数值数组解析 helper

在 `scpi::context` 增加一个 List 专用解析入口，返回临时 `std::vector<double>`；只有整列解析和协议级校验全部成功后，handler 才一次性替换 Draft 对应列，禁止边解析边修改业务状态。

建议接口按数值语义区分四类：`FrequencyHz`、`PowerDbm`、`GlobalDwellSeconds`、`DwellListMicroseconds`。其中全局 dwell 仍可复用标量单位解析，数组入口主要服务前三个 List 数据列。

ASCII 分支：

- 第一项必填，随后循环调用 libscpi 的数值/单位解析直到参数结束。
- 频率裸值按 Hz，显式后缀只接受频率单位；内部统一为 Hz。
- 功率裸值按 dBm，显式后缀只接受 dBm；内部统一为 dBm。
- `LIST:DWELl:LIST` 裸值按微秒，显式 `s/ms/us` 先由 libscpi 转成秒；内部统一使用 MScan 服务约定的时间单位。
- 拒绝 `NaN`、`Inf`、错误量纲、空数组和超过 100000 个元素；遇到第一个错误立即停止，不留下半列数据。

块分支：

- 在消费第一个参数前判断它是否为 arbitrary block，再调用 `SCPI_ParamArbitraryBlock()`；不能先尝试块解析失败后再退回 ASCII，因为 libscpi 已经消费了参数并写入错误。
- `SCPI_ParamArbitraryBlock()` 返回的指针只在当前 `SCPI_Parse()` 期间有效，必须在 handler 返回前完成复制或解码。
- 首版按第 4 节既定范围只接受 IEEE-754 `REAL,64` 大端网络字节序，不根据 payload 长度猜测 `REAL,32/64`，也不按主机字节序直接解释。
- payload 长度必须是 8 的整数倍；按字节序逐项读取，禁止用未对齐的 `reinterpret_cast<double*>`。
- 二进制数值采用各命令的裸值单位：频率为 Hz、功率为 dBm、逐点 dwell 为微秒；解码后再执行有限值、点数和业务范围校验。

块类型错误或字节数错误写入 `-161 Invalid block data`；数值、单位或索引越界使用 `-222 Data out of range`；写入超过 100000 点的 Draft 列，或提交超过 32767 点的选中范围，都使用 `-223 Too much data`。三列长度允许在 Draft 编辑过程中暂时不同，只在 `APPLy/LEARn/FREQuency:MODE LIST` 提交校验时用 `-221 Settings conflict` 拒绝不一致。

#### 8.6 Source 通道号和命令注册前提

实际注册 pattern 使用 libscpi 支持的数字占位符：

```text
[SOURce#:]LIST:FREQuency
[SOURce#:]LIST:FREQuency?
...
```

在 `scpi::context` 封装 `SCPI_CommandNumbers()`：省略 `SOURce` 或写 `SOURce` 时返回默认通道 1，`SOURce1` 返回 1。单通道设备收到 `SOURce2` 等不存在通道时，handler 报稳定的通道/硬件错误，不得静默落到通道 1。

List 命令使用 `registerCommand()`，不能使用当前会补 `[SENSe:]` 的 `registerSenseCommand()`。第 4 节全部设置和查询头应在同一批注册，包括无 H2 动作的兼容 handler，避免 R&S 脚本出现 `Undefined header`。

#### 8.7 大查询响应和线程归属

首版 `LIST:FREQuency?`、`LIST:POWer?`、`LIST:DWELl:LIST?` 在 5025 原连接返回 ASCII 逗号列表：

- 使用 C locale 和足以往返 `double` 的精度格式化，不能受系统小数点区域设置影响。
- `Parser` 可以继续在 `QByteArray mCommandResultBuffer` 聚合结果，不引入 5125 数据通道。
- reply callback 当前从 parser 工作线程直接调用 `QTcpSocket::write/flush`，而 socket 属于 transport 线程；List 大响应上线前必须改为向 socket 所在线程排队写入。排队前复制完整响应，执行时再次检查 `QPointer` 和连接状态。
- 不强制调用 `flush()`；让 Qt socket 事件循环异步发送，避免 parser 工作线程承担网络发送等待。
- 命令监视日志不得打印整个二进制 payload 或数十万点的查询结果。二进制/超长消息只显示命令头、方向、总字节数和“内容已省略”，避免日志污染和 UI 卡顿。

#### 8.8 与异步业务层的衔接契约

删除 100 ms 后，List 业务侧只需满足下面的同步边界，无需改变整个 SCPI 机制：

1. 列设置 handler 在返回前完成临时数组校验和 Draft 原子替换。
2. 范围、dwell 模式、兼容状态在返回前完成内存更新。
3. `APPLy/LEARn/FREQuency:MODE LIST` 在返回前验证完整 Draft 并生成不可变快照，然后把快照提交给现有异步 runtime。
4. 设备成功后再更新 Applied/回读快照；设备失败时保留上一份成功 Applied，不把失败请求伪装成已应用。
5. 不允许异步任务继续引用可变 Draft，也不允许靠 100 ms 推测前一条设备命令已经完成。

异步设备失败目前不能安全地从 runtime 线程补写同一个 libscpi error queue，这是现有完成模型的边界。本阶段只保证同步解析、校验和请求提交错误进入 `SYSTem:ERRor?`；真实设备完成状态以后作为独立任务设计，不阻塞 List 基础命令落地。

#### 8.9 实施顺序

1. **Parser 小改**：切换 `SCPI_Parse`，重置每条报文结果状态，删除 100 ms；先回归现有短 ASCII 命令。
2. **Transport 分帧**：实现分包/粘包/块内换行处理、16 MiB 上限和 fatal close；同时把响应写回 transport 线程。
3. **日志收敛**：禁止 control/data 监视器输出完整块和超长列表。
4. **Context helper**：增加通道号、ASCII 数值数组和 `REAL,64` definite block 解析。
5. **List handler 接入**：注册全部 R&S 命令头，再连接 `MScanTableService`；该步骤不回头改 transport/parser 架构。

每一步保持可独立审查，不在同一提交中顺带重构旧 SCPI handler。

#### 8.10 验证矩阵

| 类别 | 必测场景 | 通过条件 |
|---|---|---|
| 现有命令回归 | `*IDN?`、Center/Level 设置与查询、CRLF | 结果与改造前一致，无额外 100 ms 间隔 |
| FIFO | 同一连接连续发送多条设置和查询 | handler 顺序与发送顺序一致，查询不串结果类型 |
| 粘包 | 一次 write 发送多条 LF 结尾命令 | 每条只执行一次，响应顺序正确 |
| 分包 | 命令头、块长度字段、payload、LF 分多次 write | 收齐前不执行，收齐后只执行一次 |
| 块内特殊字节 | payload 含 LF、CR、NUL、`#`、引号 | 不误切报文，解码点数和值正确 |
| 引号 | 文件名含 `#` 或转义引号 | 不误判 arbitrary block |
| 非十进制数 | ASCII 参数含 `#H/#Q/#B` | 不被 transport 当作块头 |
| 大列表 | 三列各 100000 个 `REAL,64` 点 | 可完整写入 Draft，不受 1024 字节缓冲限制，无截断或错位 |
| 大查询 | 查询 100000 点 ASCII 列表 | 在原 5025 socket 完整返回，应用不因跨线程写 socket 报警或崩溃 |
| 超限 | 写入 Draft 第 100001 点、提交 32768 点选中范围、声明块超过 16 MiB、连接缓冲超过 16 MiB | 前两者得到 `-223`，后两者关闭违规连接，进程继续服务其他连接 |
| 格式错误 | `#0`、坏长度字段、payload 非 8 倍数、NaN/Inf | 按 transport/parser 边界稳定拒绝，Draft 保持原值 |
| 异步边界 | 快速发送三列、`APPLy`、随后修改 Draft | apply 使用提交时不可变快照，不依赖 sleep，不被后续 Draft 修改污染 |

### 9. 首版范围

首版必须同时具备：

- CW 载波的 MScan 真实执行；
- 第 4 节全部 R&S 命令头及查询接口；
- `AUTO` 模式、三列数据、dwell、范围、触发映射、应用和运行状态的真实行为；
- `INDex/RESet`、`STEP/INDex` 模式、`RMODe` 和文件容器命令的稳定兼容行为，其中 `STEP/INDex` 设置后 `LIST:MODE?` 返回实际生效值 `AUTO`；
- 对不支持但合法的 R&S 命令不返回 `Undefined header`，也不写入错误队列。

Playback/Streaming 可继续复用同一载波计划，但待 provider 选择成为服务级意图后再暴露，并且不承诺逐行切换波形。
