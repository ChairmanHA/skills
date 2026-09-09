# ListMode SCPI 命令与 R&S 兼容调试指南

适用版本：2026-09-07 当前实现。范围为 MScan/ListMode 的 36 个命令头（设置、查询分别计数）及调试所需公共命令，不是整机全部 SCPI 命令手册。

## 1. 端口调试助手的使用方式

- 使用 TCP 客户端连接软件所在 IP，控制端口默认为 **5025**。
- 选择 ASCII/文本发送，每行以 LF 或 CRLF换行 结尾。代码块可逐行发送，不要发送 Markdown 标记。
- 设置命令正常情况下没有响应；带 `?` 的查询才返回数据。一次发送一个查询并读取响应，最便于排查。
- 命令大小写不敏感。下文示例使用短写；命令表中的大写部分是短写，例如 `FREQuency` 可写为 `FREQ`。
- List 命令可省略 Source 前缀，也可使用 `SOURce1:`。当前只接受通道 1，其他通道返回 `-241`。
- 先连接设备，选择可用的发射业务。示例采用 1 GHz 附近、−30 dBm；请按设备范围及测试接线调整。
- 每个独立实验开始前先关闭 RF、切回 CW、复位。**选择 LIST 模式不会自动开启 RF**。

## 2. 当前支持的命令

表中“设置／查询”表示同时支持不带 `?` 的设置和带 `?` 的查询。所有下列 List/Frequency Mode 命令均支持可选 `[SOURce1:]` 前缀。

### 2.1 点表与驻留时间

| 命令 | 类型／参数 | 当前行为 |
|---|---|---|
| `LIST:FREQuency` / `LIST:FREQuency?` | 设置／查询，数值列表 | 覆盖／读取频率列；裸值单位 Hz，支持 MHz 等频率单位 |
| `LIST:FREQuency:POINts?` | 查询 | 频率列点数 |
| `LIST:POWer` / `LIST:POWer?` | 设置／查询，数值列表 | 覆盖／读取功率列，单位 dBm |
| `LIST:POWer:POINts?` | 查询 | 功率列点数 |
| `LIST:DWELl:LIST` / `LIST:DWELl:LIST?` | 设置／查询，数值列表 | 逐点驻留时间；**裸值和查询值单位为 µs** |
| `LIST:DWELl:LIST:POINts?` | 查询 | 逐点驻留时间列点数 |
| `LIST:DWELl` / `LIST:DWELl?` | 设置／查询，单个数值 | 全局驻留时间；**裸值和查询值单位为秒** |
| `LIST:DWELl:MODE` / `LIST:DWELl:MODE?` | `GLOBal` / `LIST` | 全局统一驻留／逐点独立驻留 |

三列可分开写入，但当前提交时要求三列非空且等长，**即使使用 GLOBal 也需填入等长的 dwell 列**。查询点数对应完整草稿，不是选中子范围的点数。数值回读可能带字符串外层引号，驻留时间经过 float 路径后可能出现微小舍入差异。

ASCII 适合端口助手；批量脚本也可使用 IEEE 488.2 定长块 `#<位数><长度><数据>`。当前固定为 **REAL,64 大端字节序**，不是可由 `FORMat` 切换的 R&S 通用二进制格式；不支持 REAL,32 或 `#0`。提交范围最多 32767 点，4097 点 ASCII 和二进制传输及启动已完成回归。

### 2.2 索引与子范围

| 命令 | 类型／参数 | 当前行为 |
|---|---|---|
| `LIST:INDex:STARt` / `LIST:INDex:STARt?` | 设置／查询，整数 | 选中区间起点，0-based |
| `LIST:INDex:STOP` / `LIST:INDex:STOP?` | 设置／查询，整数 | 选中区间终点，包含此点 |
| `LIST:INDex` / `LIST:INDex?` | 设置／查询，整数 | 兼容用逻辑索引；不会定位硬件输出点 |
| `LIST:RESet` | 无参数事件 | 将逻辑索引恢复为 STARt；不会重启硬件扫描 |

`STARt/STOP` 真正控制执行范围，例如 `1..3` 执行原表第 2～4 行。修改范围后重新 LEARn 并启动；不是向 `INDex` 写入一个值来移动硬件指针。

### 2.3 执行、触发与状态

| 命令 | 类型／参数 | 当前行为 |
|---|---|---|
| `LIST:MODE` / `LIST:MODE?` | `AUTO` / `STEP` / `INDex` | 三种设置均接受，实际始终 AUTO；查询返回 `"AUTO"` |
| `LIST:RMODe` / `LIST:RMODe?` | `LIVE` / `LEARned` | 保存并回读兼容枚举；LIVE 没有实时硬件实现，使用 LEARned 调试 |
| `LIST:LEARn` | 无参数事件 | 校验草稿并生成不可变提交快照；已处于 LIST 时请求异步刷新 |
| `LIST:TRIGger:SOURce` / `LIST:TRIGger:SOURce?` | `AUTO` / `SINGle` / `EXTernal` | 设置／查询共用触发配置，具体映射见下表 |
| `LIST:TRIGger:EXECute` | 无参数事件 | 在 LIST 模式下请求软件触发；一次执行整个选中范围 |
| `LIST:RUNNing?` | 查询，`0` / `1` | 当前载波计划为 MScan 且已应用 MScan 扫描管线时返回 1 |
| `FREQuency:MODE` / `FREQuency:MODE?` | `CW` / `LIST` | 选择固定载波／MScan；LIST 校验并提交点表、请求异步刷新 |

| List 触发源 | 共用业务层映射 | 执行方式 |
|---|---|---|
| `AUTO` | BUS，count = −1，action = SWEEP | 按选中范围自动循环 |
| `SINGle` | BUS，count = 1，action = SWEEP | 软件触发一次执行选中范围，不是单步 |
| `EXTernal` | EXTERNAL，count = 1，action = SWEEP | 外部触发一次执行选中范围，不是单步 |

List 和公共 Trigger/UI 使用同一份配置，修改一方会影响另一方。当前 List 参数只接受上表三种名称，不把 `BUS`、`IMMediate` 当作 List 参数别名。公共触发配置无法用 List 枚举表示时，查询可能返回 `-221` 错误。

`RUNNing?` 不是硬件忙位或扫描完成信号：单次运行及等待触发时也不能用它判断当前点或一轮是否结束。它也不保证射频波形已满足时序指标；实际波形需仪器观测。

### 2.4 文件名称兼容接口

| 命令 | 类型／参数 | 当前行为 |
|---|---|---|
| `LIST:SELect "name.lsw"` | 设置名称 | 保存逻辑名称，不创建或加载磁盘文件 |
| `LIST:CATalog?` | 查询 | 返回当前逻辑名称，例如 `"name.lsw"`；未选择时为 `""` |
| `LIST:DELete "name.lsw"` | 删除名称 | 名称匹配时清除逻辑名称，不删除点表 |
| `LIST:DELete:ALL` | 无参数事件 | 清除逻辑名称，不删除点表 |
| `LIST:FREE?` | 查询，字节数 | 按剩余草稿点容量估算，不是磁盘剩余容量 |

这些命令提供单内存表的兼容外观，切换名称不会切换多份点表。本文不提供 SAVE/LOAD 操作。

### 2.5 调试配套命令

| 命令 | 用途 |
|---|---|
| `OUTPut:STATe ON` / `OFF` / `OUTPut:STATe?` | 开关和查询 RF；查询通常返回 `"ON"` 或 `"OFF"` |
| `*RST` | 复位，当前实现也会清空 List 草稿及兼容状态；不是 R&S 持久文件保留语义 |
| `*CLS` | 清除错误状态，排查时先读错误再清除 |
| `*ERR?` | 读取错误队列；反复查询直到错误码为 0 |
| `*OPC?` | 查询当前命令处理完成；不能据此判定异步设备配置已完成 |

## 3. 如何兼容 R&S，以及需要调整的脚本

兼容的是上述命令头、长短写、三列写入、单位、子范围和触发操作习惯，不能理解为 R&S 全部 SCPI 及硬件行为完全等价。

- **可直接沿用**：三列 ASCII 写入、点数查询、全局／逐点 dwell、0-based 闭区间、AUTO 自动执行及 LEARned 风格命令组合。
- **接受但有降级**：STEP/INDEX 设置成功后 `MODE?` 仍为 AUTO；设置命令不会额外返回“不支持”文本，也不会因此进入错误队列。依赖单步的脚本必须调整。
- **仅保存兼容状态**：逻辑 INDEX、RMODE 和文件名称。其中 `RMODe LIVE` 回读 LIVE 不代表硬件具备 LIVE 能力。
- **学习时序不同**：当前 LEARn 是软件校验和快照准备；从 CW 状态调用不单独完成硬件预学习。选择 LIST、开启 RF 后沿现有异步设备配置链路应用，不能把 LEARn 的返回视作 PLL/ALC 已逐点学习完成。
- **尚未提供的设计项**：当前注册表没有 `LIST:APPLy`，调试请使用 `LIST:LEARn`；也不要照搬 `FORMat` 或独立文件持久化命令。

## 4. 可复制的命令组合

每个查询都应读回后再继续。以下“等待 Running”为人工重复发送 `LIST:RUNN?`，直到为 1；若持续为 0，查询 `OUTP:STAT?`、`FREQ:MODE?` 和 `*ERR?` 排查。不要将固定延时当作完成保证。

### 场景 A：高速跳频配置（全表自动循环＋独立驻留时间＋LEARned 提交）

使用 4 个频点和不同驻留时间。这里沿用“预学习”操作习惯，实际 LEARn 语义见第 3 节。示例驻留为 10～25 ms，便于先验证流程，不作为最小跳频时间指标。

```scpi
*RST
*CLS
LIST:FREQ 1000MHz,1001MHz,1002MHz,1003MHz
LIST:POW -30,-30,-30,-30
LIST:DWEL:LIST 10000,15000,20000,25000
LIST:DWEL:MODE LIST
LIST:IND:STAR 0
LIST:IND:STOP 3
LIST:MODE AUTO
LIST:RMOD LEAR
LIST:TRIG:SOUR AUTO
LIST:LEAR
*ERR?
FREQ:MODE LIST
OUTP:STAT ON
OUTP:STAT?
FREQ:MODE?
LIST:RUNN?
```

预期 RF 为 ON、模式为 LIST、最终 Running 为 1。UI 应显示 RF 开启及 ListMode 使能。

### 场景 B：索引控制与子范围切片（复用场景 A 的点表）

仅执行原表第 2、3 行，即 1001 MHz 和 1002 MHz。无需重新发送三列；切回 CW 后调整范围并重新提交。

```scpi
OUTP:STAT OFF
LIST:IND:STAR 1
LIST:IND:STOP 2
LIST:IND 2
LIST:IND?
LIST:RES
LIST:IND?
LIST:LEAR
FREQ:MODE LIST
OUTP:STAT ON
LIST:IND:STAR?
LIST:IND:STOP?
LIST:FREQ:POIN?
LIST:RUNN?
```

前两次 `IND?` 分别为 2、1，只验证逻辑索引接口；真正的执行范围由 STAR/STOP 决定。频率点数查询仍为 4，执行范围为 2 点。

### 场景 C：软件单次触发（统一驻留时间＋整表执行）

独立示例：3 个点统一驻留 20 ms。全局模式仍填入等长 dwell 列，满足当前提交校验。

```scpi
*RST
*CLS
LIST:FREQ 1000MHz,1001MHz,1002MHz
LIST:POW -30,-30,-30
LIST:DWEL:LIST 20000,20000,20000
LIST:DWEL:MODE GLOB
LIST:DWEL 0.02
LIST:IND:STAR 0
LIST:IND:STOP 2
LIST:MODE AUTO
LIST:RMOD LEAR
LIST:TRIG:SOUR SING
LIST:LEAR
FREQ:MODE LIST
OUTP:STAT ON
LIST:TRIG:SOUR?
LIST:RUNN?
```

确认触发源为 SINGLE，且 Running 为 1 后，再逐行发送：

```scpi
LIST:TRIG:EXEC
*ERR?
```

再次发送 `LIST:TRIG:EXEC` 可请求下一次整表执行。不要用 INDEX 查询或 Running 的下降沿判断单次完成；需要观测实际 RF 输出。

### 场景 D：外部触发（复用场景 C 的点表）

保持 AUTO 执行模式，每个有效外部触发执行完整选中范围。外部输入电气条件、边沿与连线按设备及 UI 当前配置核对。

```scpi
OUTP:STAT OFF
FREQ:MODE CW
LIST:TRIG:SOUR EXT
LIST:LEAR
FREQ:MODE LIST
OUTP:STAT ON
LIST:TRIG:SOUR?
LIST:RUNN?
*ERR?
```

查询应为 EXTERNAL，配置完成后施加外部触发。本场景是调试步骤，尚未以本轮 TCP 回归确认外部边沿和波形行为。

### 场景 E：验证 R&S 兼容降级与文件名称接口

复用已有点表，先停止输出。STEP 设置不报错，但查询应明确返回 AUTO；文件目录只有逻辑名称。

```scpi
OUTP:STAT OFF
FREQ:MODE CW
LIST:MODE STEP
LIST:MODE?
LIST:MODE IND
LIST:MODE?
LIST:MODE AUTO
LIST:SEL "debug_list.lsw"
LIST:CAT?
LIST:FREE?
LIST:DEL "debug_list.lsw"
LIST:CAT?
*ERR?
```

两次 MODE 查询均为 `"AUTO"`；目录先为 `"debug_list.lsw"`，删除后为 `""`。点表内容不因名称删除而清空。

### 调试结束：停止输出并清理

```scpi
OUTP:STAT OFF
FREQ:MODE CW
*RST
OUTP:STAT?
FREQ:MODE?
*ERR?
```

预期 RF 为 OFF，模式为 CW。若希望保留当前草稿继续实验，省略 `*RST`。

## 5. 依据与验证边界

- 命令注册及参数：`src/plugins/scpi/register/scpiregister.cpp`。
- 点表、兼容状态与提交校验：`src/plugins/core/mscantableservice.cpp`。
- 模式选择及 Running 判定：`src/plugins/core/txsessionservice.cpp`。
- 本地设计参考：[ListMode 接口实现](listmode接口实现.md)、[R&S ListMode 参考](rs_list_mode_scpi_reference.md)。本指南按当前代码注明与设计／参考的差异。
- 已执行回归：4097 点 ASCII、4097 点 REAL,64、LEARned 列长校验；RF、ListMode UI 回显已由用户确认。基础用例仍有 float dwell 精度容差待调整，不能宣称全部断言通过。
