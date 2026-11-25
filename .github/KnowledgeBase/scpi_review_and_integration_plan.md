# SCPI 接入静态审阅与当前收口方案

2026-09-17 初审，2026-11-24 复核当前源码。

## 2026-11-10：幅度补偿 R&S 核心命令

代码已实施。2026-11-10 在用户已启动的 Debug 程序上完成软件 SCPI 自动测试：7/7 组、620/620 断言通过，覆盖命令、草稿提交、错误保留、名称、软件 VAL?、REAL64 和容量；本轮未构建，GUI/Reset/扫描与实际射频功率仍待验收。测试已并入 `scripts/test_scpi_foundation.py --correction-only`，各组独立函数和可选 --correction-case，完整结果及首次脚本问题见 [测试记录](../TaskLog/2026-11-10_test-amplitude-compensation-scpi.md)。范围按现有 ListMode 的兼容程度收敛为单草稿、单提交表和单逻辑容器。业务状态/校验属于 `PowerReferencePlane`，SCPI 不驱动界面按钮、不重复计算设备输出。实施记录见 [TaskLog](../TaskLog/2026-11-10_implement-amplitude-compensation-scpi.md)，厂商依据与完整取舍见 [复审合同](../TaskLog/2026-11-10_review-amplitude-compensation-scpi.md)。

以下为 **9 类功能、13 个设置/查询入口** 的使用说明。公共前缀 `[SOURce#:]` 可省略，支持长短写、SOUR/SOUR1，其他通道报 -241。

后续按用户要求，补偿脚本已大幅精简为 basic/draft_commit/names 三组，只保留正常写表、提交/失败保留和单次名称兼容流程；删除复杂边界专项。前述 7/7、620 项为旧版历史结果，精简版尚未重跑设备，见 [精简记录](../TaskLog/2026-11-10_reduce-correction-tests.md)。

| 编号 | 命令（省略公共前缀） | 常用短写 | 参数 / 返回 | 功能 |
| --- | --- | --- | --- | --- |
| 1 | `CORRection[:STATe]` | `CORR` | ON/OFF/1/0 | ON 提交草稿并开启，OFF 关闭 |
| 2 | `CORRection[:STATe]?` | `CORR?` | 0 或 1 | 查询软件开关 |
| 3 | `CORRection:CSET[:SELect]` | `CORR:CSET` | 名称字符串 | 记录逻辑名称 |
| 4 | `CORRection:CSET[:SELect]?` | `CORR:CSET?` | 名称字符串 | 查询逻辑名称 |
| 5 | `CORRection:CSET:CATalog?` | `CORR:CSET:CAT?` | 名称字符串 | 查询单逻辑容器 |
| 6 | `CORRection:CSET:DELete` | `CORR:CSET:DEL` | 名称字符串 | 匹配时清除名称 |
| 7 | `CORRection:CSET:DATA:FREQuency` | `CORR:CSET:DATA:FREQ` | 频率数组 | 整列替换草稿频率 |
| 8 | `CORRection:CSET:DATA:FREQuency?` | `CORR:CSET:DATA:FREQ?` | Hz 数组字符串 | 查询草稿频率 |
| 9 | `CORRection:CSET:DATA:POWer` | `CORR:CSET:DATA:POW` | dB 数组 | 整列替换草稿补偿 |
| 10 | `CORRection:CSET:DATA:POWer?` | `CORR:CSET:DATA:POW?` | dB 数组字符串 | 查询草稿补偿 |
| 11 | `CORRection:CSET:DATA:FREQuency:POINts?` | `CORR:CSET:DATA:FREQ:POIN?` | 非负整数 | 查询频率列实际点数 |
| 12 | `CORRection:CSET:DATA:POWer:POINts?` | `CORR:CSET:DATA:POW:POIN?` | 非负整数 | 查询补偿列实际点数 |
| 13 | `CORRection:VALue?` | `CORR:VAL?` | 数值，dB | 查询设置 Center 对应的软件补偿量 |

两列可按任意顺序发送，允许暂时不等长和单点；每列最多 999 点，频率 (0,100 GHz]、补偿 ±100 dB，均须有限。ON 提交另要求等长、至少两点、频率严格递增；失败保留原提交表/使能状态。GUI Apply 非空表保持开关，空表则清除已提交表并关闭补偿；GUI 开关使用已提交表。GUI CSV 超限仅保留前 999 个数据行并提示，不改变 SCPI 的超限拒绝规则。数值/顺序错误 -222，超量 -223，列不齐/不足两点 -221，空名称 -224；单位和语法错误由 parser 报告。新增标量命令在写入前拒绝多余参数，查询同样不接受参数。

数组沿用 ListMode 的 ASCII 和 big-endian REAL,64 IEEE 定长块；未新增 FORMat/字节序选项。DATA 查询也沿用其 QString 响应，数组文本带外层引号，空列返回 `""`，点数返回 0。共享字符串解析改为按收到的参数区长度分配缓冲区，修复旧固定 128 字节导致的静默截断；引号/转义仍交原 parser。

`VAL?` 精确命中节点时返回该点 dB，频点间按 Hz 线性插值，表外按首/末两点外推；扫描时也取设置 Center，不追踪实际扫描点。结果不量化、不按输入的 ±100 dB 范围夹值；非有限结果报 -222 并返回既有不可用哨兵 9.91e37。该软件定义不证明 SDK 的插值/外推一致，补偿仍由设备 API 执行。用户实测的 1 GHz、-20 dBm、+2 dB→-18 dBm 支持该点的正补偿方向，不能替代其他模式的验收。

基础使用示例（以下响应为软件状态，不代表射频测量）：

```text
CORR OFF
FREQ:CENT 1GHz
CORR:CSET "cable_A"
CORR:CSET:DATA:FREQ 1GHz,2GHz
CORR:CSET:DATA:POW 2dB,4dB
CORR ON
CORR:STAT?
CORR:CSET:DATA:POW?
CORR:VAL?
```

此时 STATE? 应为 1、POW? 为 `"2,4"`；若设置 Center=1 GHz，VAL? 应为 2。逻辑名与 CSV 来源分离；Reset 关闭补偿、保留提交表、草稿恢复提交表并清名，配置恢复也清名。命令成功仅表示软件接受/提交，不表示设备执行完成；现有 `*WAI`/`*OPC?` 不提供补偿稳定保证。未实现多表、DEXChange、NRP 自动采集/归零、其他 RF 通道或私有 APPLy/CLEar。

### 通用调用规则

启用 SGStudio 的 SCPI 服务后连接 TCP 5025，命令以换行结束。命令不区分大小写；表中大写部分是短写，小写部分可整体省略。方括号表示可省略节点，发送时不带方括号；`#` 是通道后缀占位，实际发送 SOUR/SOUR1，不发送井号。

以下设置等价：

```text
CORR ON
CORR:STAT ON
SOUR:CORR:STAT ON
SOUR1:CORR:STAT ON
:SOURCE1:CORRECTION:STATE ON
```

设置成功无响应，不应对 setter 等待读回。查询必须带 `?`，不接受额外参数。数值响应无单位后缀；名称和 DATA 数组响应带外层双引号，空字符串响应是 `""`，不等于不回包。数组客户端先去除外层引号，再按逗号拆分数值。错误通过现有 `*ERR?` 读取，`*CLS` 清除错误队列；失败查询一般不返回正常数据，不能等待正常响应直至超时后再误判为断线。

### 1. 开关与提交：CORR[:STAT] / ?（入口 1、2）

参数是单个 ON/OFF/1/0。ON 先校验两列，再一次性更新已提交表与使能状态；已经 ON 时再发 ON 仍提交本轮草稿。失败保留原提交表和开关。OFF 不检查草稿，保留所有表数据。

```text
CORR ON
CORR:STAT?
CORR OFF
CORR?
```

有效草稿下，两次查询分别返回 `1`、`0`。空表、单点或不等长草稿不能通过 ON 开启。

| 入口 | 当前行为 |
| --- | --- |
| SCPI CORR ON | 校验、提交草稿并开启 |
| SCPI CORR OFF | 只关闭，不提交、不删除表 |
| GUI Apply 非空表 | 校验并提交，保持开关 |
| GUI Clear 后 Apply 空表 | 清除已提交表并关闭补偿 |
| GUI 开关 ON | 使用已有合法提交表，不自动提交草稿 |

### 2. 设置/查询逻辑名：CSET[:SEL] / ?（入口 3、4）

```text
CORR:CSET "cable_A"
CORR:CSET?
CORR:CSET:SEL?
```

两个查询均返回 `"cable_A"`。参数必须是一个带引号的非空字符串，名称首尾空白被去掉，比较区分大小写；例如 `"  cable_A  "` 保存为 `cable_A`。名称内双引号用两个双引号表示，例如 `CORR:CSET "cable_""A"""`。

这里只记录会话名称，不创建或读取文件，不切换数据、不改变 dirty/开关/CSV 来源；可以完全不调用 SEL，直接写表。选择 A 后再选择 B，仍操作同一份草稿；不支持通过名称恢复 A 的旧数据。Reset 和配置恢复清名，名称不持久化。

### 3. 单容器查询：CSET:CAT?（入口 5）

```text
CORR:CSET:CAT?
```

有名称时返回该名称，例如 `"cable_A"`，无名称时返回 `""`。这里不会列举目录或多张表。

### 4. 删除逻辑名：CSET:DEL（入口 6）

```text
CORR:CSET "cable_A"
CORR:CSET:DEL "other"
CORR:CSET?
CORR:CSET:DEL "cable_A"
CORR:CSET?
```

第一次查询仍为 `"cable_A"`，第二次为 `""`。不匹配名称也返回成功；匹配只清名，**不清除草稿/提交表，不关闭补偿，不删除磁盘文件**。空名称报 -224。

名称三类功能的用途只是迁移脚本兼容：与现有 ListMode 一样保留一个可查回的名称。R&S 的 SEL/CAT/DEL 则用于实际补偿文件的选择或创建、目录查询、删除。SGStudio 没有多文件补偿仓库；名称接口不是补偿业务前置条件，不能将 CSET:DATA 的真实数据操作也当作空实现。依据和取舍见前述复审合同。

### 5. 频率列：CSET:DATA:FREQ / ?（入口 7、8）

以下输入等价，都是整列覆盖而非追加：

```text
CORR:CSET:DATA:FREQ 1000000000,2000000000
CORR:CSET:DATA:FREQ 1GHz,2GHz
CORR:CSET:DATA:FREQ 1000MHz,2000MHz
CORR:CSET:DATA:FREQ 1e9,2e9
CORR:CSET:DATA:FREQ?
```

返回 `"1000000000,2000000000"`。裸数为 Hz，支持 Hz/kHz/MHz/GHz；每次 1–999 点，每点有限且在 (0,100 GHz] 内。写列时先检查数值/范围，成功才替换整列；严格递增在 ON 整表提交时检查。写频率列不会自动补齐补偿列，也不会自动更新已提交表。

空列查询返回 `""`，但不带参数的 `CORR:CSET:DATA:FREQ` 是缺参错误，不是清空操作。

### 6. 补偿列：CSET:DATA:POW / ?（入口 9、10）

```text
CORR:CSET:DATA:POW 2dB,4dB
CORR:CSET:DATA:POW?
```

返回 `"2,4"`。`2,4` 与 `2dB,4dB` 等价；参数为有限的相对 dB，每列 1–999 点，范围 [-100,100]。支持正、负、零，拒绝 dBm 或其他量纲。输入错误不改变原补偿列；不做 Level Offset 换算、不把数组值当作绝对输出功率。

例如 Level=-20 dBm、节点补偿 +2 dB，数组里填 `2`；输出的 -18 dBm 不是数组值。前端不再次把补偿加到 Level/扫描功率/RMS 请求上。

### 7. 频率点数：CSET:DATA:FREQ:POIN?（入口 11）

```text
CORR:CSET:DATA:FREQ:POIN?
```

返回草稿频率列长度，例如 `2`；空列为 `0`。不返回已提交表点数。

### 8. 补偿点数：CSET:DATA:POW:POIN?（入口 12）

```text
CORR:CSET:DATA:POW:POIN?
```

返回草稿补偿列长度，例如 `2`；空列为 `0`。两列可以任意顺序写入，点数查询允许不同，提交才要求相同。

例如先写 `POW 2,4,6`，再写 `FREQ 1GHz,2GHz`，点数分别为 3 和 2；此时 ON 报 -221。补写 `FREQ 1GHz,2GHz,3GHz` 后才可提交，不会自动截断或补零。

### 9. 当前软件补偿量：CORR:VAL?（入口 13）

无参数，返回 dB 数值。OFF 为 0；ON 使用**已提交表 + CommonDeviceProfile 的设置 Center**，不读草稿、不读取设备瞬时频率或功率。Frequency Error 和多音 LO 偏移不改变其设置频率语义。

已提交 `(1 GHz,2 dB)`、`(2 GHz,4 dB)` 时：

| Center | VAL? 预期 |
| --- | --- |
| 0.5 GHz | 1 |
| 1 GHz | 2 |
| 1.5 GHz | 3 |
| 2 GHz | 4 |
| 2.5 GHz | 5 |

节点直接取值，区间内按 Hz 线性插值，表外使用端部两点线性外推。计算结果是软件定义，不证明 SDK 的设备算法或实测输出一致。非有限计算结果报 -222，同时返回 9.91e37。

### 开启期间更新草稿示例

在上述两点表已开启、Center=1 GHz 的前提下：

```text
CORR:CSET:DATA:POW 3,5
CORR:CSET:DATA:POW?
CORR:VAL?
CORR ON
CORR:VAL?
```

三个查询依次返回 `"3,5"`、`2`、`3`。这区分了 DATA? 的草稿、VAL? 的提交数据，以及重复 ON 的提交动作。

### REAL64 二进制输入示例

两列均支持 IEEE 定长块 `#<长度字段位数><字节数><数据>`，固定 big-endian REAL64，每点 8 字节；频率为 Hz，补偿为 dB。数据块非空、长度为 8 的整数倍，最多 999 点，不能附带其他参数。查询仍返回文本数组；没有 FORMat 或字节序切换命令。

已有 TCP socket `sock` 时：

```python
import struct

def write_real64(sock, command, values):
    payload = struct.pack(f">{len(values)}d", *values)
    length = str(len(payload)).encode("ascii")
    block = b"#" + str(len(length)).encode("ascii") + length + payload
    sock.sendall(command.encode("ascii") + b" " + block + b"\n")

write_real64(sock, "CORR:CSET:DATA:FREQ", [1e9, 2e9])
write_real64(sock, "CORR:CSET:DATA:POW", [2.0, 4.0])
sock.sendall(b"CORR ON\n")
```

### 错误与使用限制

| 错误码 | 示例 / 条件 |
| --- | --- |
| -108 | 多余参数，如 `CORR OFF,1` 或 `CORR:VAL? 1` |
| -109 | 缺少必需参数；空参数不能用来清空列 |
| -131 | 非法单位后缀，如补偿数组输入 dBm |
| -161 | 二进制块长度非法 |
| -221 | ON 时列不齐、空表或不足两点 |
| -222 | 数值越界/非有限值，ON 时频率重复或逆序 |
| -223 | 单列超过 999 点 |
| -224 | 名称为空或全为空白 |
| -241 | 非通道 1，如 SOUR2 |

其他语法错误沿用 parser；错误不会通过夹值、截断 SCPI 数组或静默补齐变成成功。GUI CSV 超限处理是另一条交互合同，不代表 SCPI 允许截断。Reset 的清表/保留行为见本节开头；没有 CORR 私有清空命令。仅名称操作不影响补偿数据，真正的数据操作始终经过 owner 校验。

## 2026-11-17：删除 Level Offset 软件参考面

按用户要求删除 POWer:OFFSet / POWer:OFFSet?（POW:OFFS / POW:OFFS?）注册与处理，不保留兼容命令。Level/RMS、Sweep Start/Stop、List Power 和 Minibar 不再做全局加减。原数值校验、RMS 不可用反馈、设备执行与回写语义保持。

CORR 命令和表格继续使用原 owner/API，两个不同频点填写相同补偿量可实现常量校正。新保存不写 common.LevelOffset，旧字段自然忽略，不自动迁移。半自动验收真源/生成文档同步删除旧命令与字段依赖，未执行设备验收。见 [本次 TaskLog](../TaskLog/2026-11-17_remove-level-offset-use-ucor.md)。

## 2026-10-23/26：内部基线自动化入口

以下为内部基线命令，不增加 GUI 菜单，也不扩展 `*OPC?` / `*WAI` 或执行完成状态。LOAD/截图已在 10 月 23 日的 Debug 程序上运行基础脚本；10 月 26 日新增 SAVE 及 RMS 场景仅做静态检查，尚未构建或运行。UI/射频复核及下述历史参数差异仍待确认。

| 命令 | 参数/响应 | 行为 |
| --- | --- | --- |
| `SYSTem:TEST:LOAD "testcases/M-1.json"`（短写 `SYST:TEST:LOAD`） | 路径字符串；设置成功无响应 | 复用 File/Open 的配置恢复、批量 pipeline 挂起、pending MOD 恢复及业务重新选择；可在未连接设备时恢复配置。成功仅表示配置读取/恢复并已请求执行，不代表波形生成、下载或设备配置结束。 |
| `SYSTem:TEST:SAVE "A_RMS.json"`（短写 `SYST:TEST:SAVE`） | 单个 `.json` 文件名；成功无响应 | 自动创建 `<runtime>/testcases/`，保存当前 GUI 配置和功率意图，同名文件覆盖。复用现有捕获/持久化实现，在 GUI 线程同步完成文件提交，不等待波形生成或设备执行。 |
| `HCOPy:DATA?`（短写 `HCOP:DATA?`） | 本机 PNG 文件的绝对路径字符串 | 捕获主窗口并直接保存到 `<runtime>/images/`；成功返回带外层双引号的路径，失败返回 `""` 并入错误队列；不弹出截图保存提示。 |

2026-11-17 增加本机波形测试命令（仅静态实现，未构建/实机验收）：

| 前缀（均为 `SYST:TEST:` 下的短写） | 命令和契约 |
| --- | --- |
| `ARB:` | `FILE "name.wav"`、`FILE?`、`CLE`；`SRAT`、`ASC`、`SCAL`、`OFFS`、`USE`、`PER`及对应查询；`STAT ON/OFF`及查询。文件固定相对runtime/testcases/assets，只接收单个文件名。IQS-WAV缩放只读，写入报-221。 |
| `STREAM:` | `FILE "name.wav"`替换列表、`APP "name.wav"`追加、`CLE`清空、`FILES?`按顺序返回逗号分隔文件名（空列表为空字符串）；`SRAT`、`SCAL`和`STAT`均可设置/查询。文件根同ARB；文件名不允许逗号。 |
| `QUICK:` | `ASC`及查询，`FILE "QAM/16QAM_4MHz_1MHz.wav"`；相对当前QuickWaveFormData，复用业务根路径校验与异步加载，命令成功仅表示请求被接受，G12.12人工段确认加载成功后检查键盘。 |

STAT ON选择对应业务并开MOD；关闭当前选中业务时关MOD，关闭未选业务不改变其他业务的MOD。RF/载波扫描不由该命令重设；文件为空时使能报-221。复用BusinessManager选择、属性编辑及已有applyRemoteEditorAction，MainWindow/TxSession继续负责执行。Streaming参数使用业务远程编辑入口，不触发GUI吞吐确认；确认框本身仍由M6人工测试。没有新播放器、线程、完成轮询或跨机器文件映射。普通WAV/IQS头部由Utils校验，业务仍决定格式/容量与参数归一化；非法文件名-224、不可用业务-241、读取/业务拒绝-200，非有限数值和不可精确表示的样点计数-222。只读字段-221。设置值经业务归一化后查询实际值。

完整用法及更新后的用例见 `scripts/README_scpi_gui_baseline.md` 和 `testcases/semiautomatic_cases.json`。M1的large.wav由测试者本机一次性预置；其余新文件命令使用现有包内资源。保存/加载专项继续使用原LOAD/SAVE，不再通过派生JSON模拟普通波形参数修改。

LOAD 路径由运行 SGStudio 的机器解析：相对路径固定以可执行文件目录的上一级为基准，即包含 `bin/`、`plugin/`、`configuration/` 的运行根目录，不跟随客户端或 SGStudio 的 cwd。绝对路径直接使用。例如可执行文件在 `<runtime>/bin/` 时，上述命令读取 `<runtime>/testcases/M-1.json`。SAVE 只接受文件名，不接受目录或绝对路径；`SAVE "A_RMS.json"` 固定写入与 bin 同级的 testcases，随后用 `LOAD "testcases/A_RMS.json"` 恢复。

命令中的路径字符串必须为 ASCII。2026-10-23 实测直接发送桌面中文目录产生 -101 Invalid character，改为 `<runtime>/testcases` 下的同一 JSON 后加载正常。因此使用 ASCII 相对路径，脚本在连接前拒绝非 ASCII 路径；不修改 parser 来扩展协议。JSON 内的中文 WAV 路径仍由配置加载实现直接读取，不受命令文本这一限制。

2026-10-27 起，GUI/SCPI 共用的配置文件持久化边界支持 Playback.FileName、Streaming.FileList 相对该 JSON 所在目录的资源路径：读取后转换为绝对路径再恢复；保存时仅将目标 JSON 目录内及子目录资源转为相对路径，外部绝对路径保留，不修改内存状态。空路径保持空；Save As 按新 JSON 位置计算。SCPI LOAD 自身的配置路径仍以运行根目录解析，不能混淆这两个基准；不上传/复制波形或自动选择其他设备。有效 JSON 对象仍按现有默认值合并规则恢复，不能把 `{}` 当成格式损坏样本。

仓库根 testcases 的 G-2/M-1 共用 `assets/16QAM_4MHz_1MHz.wav`，已原样复制实际原文件；所有测试 JSON 清理固定 QuickWaveFormData 的 filePath/currentPath 绝对根路径。Quick Waveform 固定使用当前运行包的资源根，根目录浏览时不保存 currentPath，子目录浏览与选中文件/列表项保存为相对此根的路径；旧配置不能覆盖资源根或将浏览位置指向过时开发目录。Python 预检对相对/绝对波形路径均按配置目录检查存在性。Windows watermark ON 包按实际 CMake cache 带 testcases/assets 与 scripts 下两个 Python/README，OFF 不附带；新变更仅静态核对，未构建/打包/执行 SCPI。

错误沿现有 SCPI 错误队列上报：空白 LOAD 路径或非法 SAVE 文件名为 `-224`；无 MainWindow 为 `-241`；文件读取/JSON 对象解析/恢复、保存目录创建/JSON 提交、截图捕获/PNG 保存失败为 `-200`。可用 `*ERR?` 读取错误码；普通加载失败在恢复前返回，不把读取失败当作加载成功。原 `*ERR?` 的通用错误文字格式保持不变。截图失败仍有空字符串响应，客户端不必等待无响应超时。

2026-10-23/26 的历史基线以客户端等待 **100 ms** 为默认值，大 WAV/IQS-WAV 场景可单独延长。当前脚本的固定间隔上限为 **20 ms**，普通检查点只发步骤需要的查询。必需的功率回读遇到 `9.91e37` 和 `-221` 时，在客户端超时范围内重读，波形生成、下载及 RF 稳定按验收步骤观察。使用说明见源码仓库的 `scripts/README_scpi_gui_baseline.md` 和 [SCPI 回读精简记录](../TaskLog/2026-11-23_simplify-scpi-readbacks-and-awgn-power.md)。

2026-10-23 按用户本机运行脚本和程序的部署方式，取消最初的 PNG 二进制响应，保留命令名并直接在服务器保存。例：`HCOP:DATA?` → `"D:/development/vsg2.0/images/20261023_153012_123.png"`。文件名采用 `yyyyMMdd_HHmmss_zzz.png`，适配 100 ms 检查点。仅用控制连接读取一行路径，无须 5125 数据通道或二进制块读取。`images/` 与配置的相对路径使用同一运行根目录锚点，不依赖 cwd。

截图范围为当前 MainWindow 的 QWidget 图像，包含其子控件；独立顶层 GNSS/设置弹窗、系统桌面和硬件屏幕亮度不属于这次截图接口范围。截图在 GUI 线程同步捕获，不修改当前业务/页面，不承担设备完成或 UI 稳定等待。

实现入口：`src/plugins/scpi/register/scpiregister.cpp`、Core 的 `MainWindow::loadSettingsFile()` / `saveSettingsFile()` / `saveWindowScreenshot()` 和原 SettingsController。配置保存复用 `captureRuntimeProfileState()` 与 `writeRuntimeProfileFile()`（QSaveFile）；Core 负责窗口捕获和 PNG 保存，手动截图按钮保存后显示提示，SCPI 截图只返回路径，不另建发射、配置持久化或恢复链路。

2026-10-26 新增 `--scenario rms`，按用户最新 G6～G9：1024 音时连续发送 RMS=-40/SAVE A、Level=-15/SAVE B，中间无等待或截图；读取真实 A/B 文件检查 v5 功率意图及 1024 音/10 kHz、RF OFF/MOD ON，加载 A 等 10 s 再加载 B；随后测试 RMS 保持、新 Level 建立目标、RF/MOD 不重复补偿、F/L/MScan 门控及返回 Fixed。RMS 稳定检查使用固定 `--rms-wait-ms`（默认 1000 ms），其余点默认 100 ms，无 OPC/完成轮询或重试。同批保存不能证明统计未知；截图不能证明保存瞬间状态。主窗/Minibar 置灰和实际输出仍待人工复核，G9 不含手动 Streaming 文件加载。

`--basic-only` 默认选 basic/scans/files/repeat/rms，适合单台普通 125M 设备；拒绝 external，保持用户已注释的选件入口。输入配置须为 Standard/Internal/BUS，普通 Playback 不高于 125 MSPS；原 files 通过已保存 M-1 自动加载仍保留。v5 RMS 加载按 power 意图断言，不能把 common.Level 兼容值当新派生 Level；波形变化后的 Level 记录并由 rms 场景验证联动。原 Excel、基准 JSON/WAV 不改，A/B 由新保存命令生成。需重新构建更新 Core/SCPI 后运行，新接口/场景尚无实测结论。

精简脚本：`D:/development/vsg2.0/scripts/test_scpi_gui_baseline.py`，使用说明见同目录 `README_scpi_gui_baseline.md`。默认 basic/scans/files/repeat，含 MScan、Playback、Streaming；external 按接线单独选择，用户已注释掉选件场景入口，保持该修改。加载期望取自用户当前 JSON，不改原 Excel/JSON/WAV；图片和逐点报告位于 images/。本机 Python 3.12.13/.venv 已配置，四组基础场景共 59 点，51 PASS/8 FAIL，59 张截图均存在，最终 RF/MOD/Sweep OFF。差异集中在 MScan 全局驻留读回以及 Streaming 切换后的 Center/Level；未将这些失败改成通过或擅自扩展业务修复。UI/接收信号复核仍为 pending。报告 `scpi-baseline_20261023_125240_302101.json` 位于当前 Debug 运行根目录 images/，完整过程见本次环境 TaskLog。

2026-10-23 后续复测修正：用户指出 From List 不使用全局驻留，脚本现按活动模式保留逐点/全局其中一项查询与断言，并在模式切换时清掉未使用的继承期望。重跑失败所属 scans/files 两组，仍为 100 ms；35 点中 28 PASS、7 FAIL、35 张截图存在，最终 RF/MOD/Sweep OFF。原全局驻留误报消除；本轮 3 点出现 MScan 频率/功率/逐点驻留/范围与 JSON 不符，4 点为 Streaming 修改/开关或切 CW 的 Center/Level 不符。截图也显示这些状态；结束后一段时间只读电平仍为 -20 dBm，而最后 F-1 期望 -30 dBm，故至少该现象不能直接归为 100 ms 瞬时读回。原因待进一步定位，未改产品代码或放宽判据。新报告：`scpi-baseline_20261023_134351_581757.json`，原报告保留。

2026-10-23 后续静态配置核对：用户要求仅按保存配置调整未通过 testcase，禁止重测。相关 7 份桌面 JSON 与运行副本哈希相同，原加载期望已匹配文件；额外手改段才引入不同目标。脚本移除 MScan 的 GLOBAL 0.5 s/范围 2～3/第三点 -25 dBm 编辑和 M-1 的 1.01 GHz/-35 dBm 编辑，改按保存值停启、开关和重载。F-1 仍须检查原文件的 -30 dBm。该变更尚未运行，不改旧报告，不据此宣称剩余加载差异已解决。

## 结论

SCPI 的 TCP 收包、libscpi 解析和产品命令处理在 GUI 线程串行执行。`src/libs/scpi` 提供 transport、命令注册、解析和错误队列，`src/plugins/scpi` 将命令接到共享 Property、Core 服务或现有界面。Core 管线的设备 I/O 由 worker 执行。软件设置或提交之后，波形准备和设备执行可以继续在后台进行。

## 证据来源

- 构建入口为 `src/plugins/scpi/CMakeLists.txt`、`src/libs/scpi/src/CMakeLists.txt`、`src/plugins/core/CMakeLists.txt` 和 `3rdParty/scpi-parser-2.3/CMakeLists.txt`。本轮按当前源码 `92f371e0` 静态复核。
- 协议层见 `transport_tcp.cpp`、`scpi_engine.cpp`、`parser.cpp`、`scpi_context.cpp`。产品层见 `scpi.json`、`scpidialog.cpp`、`register/scpiregister.cpp`。设备执行及回写见 `txsessionservice.cpp`、`txpipelineruntime.cpp` 和 `devicemanager.cpp`。
- SCPI 已接入 Core TX 完成判定。状态寄存器扩展、桥接业务自身的完成通知和异步失败上报仍待实施。相关资料见 [TX 执行与回写](tx_execution_context_phase1_and_provider_migration.md)和 [分阶段方案](../TaskLog/2026-11-09_scpi-status-and-opc-implementation-plan.md)。

## 已确认的可复用部分

- `TransportTCP` 为每个连接组装程序消息，支持拆包、多行和 IEEE 定长数据块，单条消息上限为 16 MiB。响应通过各自连接的 queued write 写回。`HCOP:DATA?` 通过控制连接返回服务器上的文件路径。
- `Parser` 共用一个 libscpi context 和 FIFO，`mDraining` 使嵌套事件循环中收到的命令排队。`pushCommand()` 直接调用 `SCPI_Parse`，其中没有固定 sleep。`ScpiEngine::stop()` 停止接收命令并清理队列，然后关闭 transport。
- `scpi.json` 已声明 `Core` 依赖。SCPI 对话框负责手动启停和日志，默认控制端口为 5025、数据端口为 5125。

## 2026-09-17 已落地：退出死锁与日志洪泛收口

现场日志确认旧实现存在确定死锁：Parser worker 在产品命令的 `BlockingQueuedConnection` 上等待 GUI 线程；GUI 线程同步执行插件 shutdown，并在 `~Parser()` 中无限期等待 worker。窗口已关闭但主进程无法结束。2 ms Center/Level 轮询可以稳定保证退出瞬间存在在途命令并复现。

当前实现已做以下收口：

- 删除 Parser worker、mutex、condition variable、interruption 和析构 `wait()`。
- `pushCommand()` 在 transport/GUI 线程用 FIFO 同步排空；`mDraining` 使嵌套事件循环只能入队，不能递归解析。
- 普通 get/set 命令与 List 命令直接调用 handler，不再使用 `BlockingQueuedConnection`。
- `ScpiEngine::stop()` 先停止 Parser 接收并清空待处理队列，再关闭 transport；服务重新启动时 `init()` 恢复接收。
- socket reply 仍通过带 `QPointer` 的 queued invocation 写回，保持连接生命周期安全；它不参与 blocking wait。
- `SCPIDialog` 把命令/响应日志暂存并每 50 ms 合并为一次 `append()`；pending 和文档均最多保留 5000 行，Clear 同时清空两者。

验证边界：代码侧完成静态搜索和 diff 检查；用户在更新后的程序上保持 2 ms Center/Level 轮询并执行退出，确认进程正常结束、未再驻留后台。该结果是现场运行验证，不等价于全部 SCPI 命令族的功能回归。

## 当前仍存在的问题

### 1. 部分命令仍由主窗口或业务面板执行

- `Center`、`Level`、`RF` 的设置走共享 Property 和现有编辑通知。`LIST` 的表格和载波计划分别由 `MScanTableService` 和 `TxSessionService` 处理，`CORR` 走 `PowerReferencePlane`，损伤设置走 `SignalImpairmentsSettings`。这些 handler 已有明确的业务入口。
- `OUTP:MOD:STAT` 调用 `CommonPanel::triggerModButton()`，`SWE:STAT ON` 操作 `StepSweepPanel` 并调用 `showSweepPage()`，参考时钟输出、Low Power 和部分调制启停仍经面板入口。`SYST:TEST:LOAD/SAVE` 和 `HCOP:DATA?` 复用主窗口的配置、保存和截图逻辑。
- 在当前桌面应用中，SCPI 从主窗口所属的对话框启用，启用后关闭该对话框也能继续处理命令。现有依赖是主窗口和相关面板的生命周期，普通命令无需一直显示 SCPI 对话框。若后续提供 headless 入口，应先迁移实际依赖面板的业务操作，复用现有 owner 和 UI 回写机制。

### 2. 错误队列支持 Streaming 异步失败

- `scpi::context::reportError()` 通过 `SCPI_ErrorPushEx` 上报错误。List、CORR、损伤、RMS、测试 LOAD/SAVE 和截图已有明确错误码。`*ERR?` 从全局 10 项错误队列逐条取出，回传错误码和通用译文。`*CLS` 清空错误队列和事件寄存器，取消挂起的 OPC 登记。`*ESR?` 读取并清除标准事件寄存器。`SYST:ERR?` 和 `SYST:ERR:NEXT?` 读取同一队列并返回详细原因。Streaming 启动、重配或读文件失败进入队列并置 ESR bit 3。
- 旧 handler 的 Property 缺失、只读、业务不可见，以及断开设备后的 `*IDN?`，仍有只写日志并返回失败的路径。libscpi 在 handler 返回失败且未主动报错时补入通用 `-200 Execution error`。因此客户端可以检测到失败，但有些路径缺少可据以处理的错误码或设备详情。
- 设备 apply 失败由 `TxPipelineRuntime` 经 `deviceConfigurationDone` / `applyStateChanged` 回报给 Core。SCPI 没有订阅并将这些异步失败加入错误队列。`*RST` 触发 `ACTION_PRESET` 和 MScan 重置，当前 Preset 代码直接调用 `resetDevice()`，无确认框。设备 reset 失败当前只记日志，SCPI handler 仍返回动作已触发。

### 3. 完成命令覆盖 Core 与 Streaming

| 入口 | 当前行为 |
| --- | --- |
| `*OPC?` | 必须是程序消息的最后一条命令或独占一条消息。整条消息解析完后，Core 忙时暂缓整行回复和后续消息，首次空闲后一起回复。按 IEEE 488.2 返回 NR1 数字 `1`（不带引号；2026-11-24 前为 `"1"`，不保留兼容）。 |
| `*WAI` | 同样必须位于消息末尾或独占一条消息。Core 忙时暂缓下一条程序消息，命令本身没有响应。 |
| `*OPC` | 可位于消息任意位置。登记一次完成事件并继续解析。空闲时置 ESR bit 0，读取 `*ESR?` 可取得该位。`*CLS` 和 `*RST` 均取消挂起的登记。 |

等待只作用于程序消息边界，由 `src/libs/scpi` 的 Parser 在 libscpi 原生 `SCPI_Parse` 返回后执行，第三方解析器不做修改。`*WAI` / `*OPC?` 后面若还有 `;` 分隔的命令，该命令报 `-100 Command error`（附 "must end the program message"），不产生等待或 `"1"`，其余命令照常执行。不保留旧脚本同一行中途屏障的兼容。

所有连接共用解析 FIFO。等待中的消息保留同一行已产生的查询结果，后续消息和其他连接的新消息排队。Core 已空闲时立即通过。无可用设备且无切换时也会通过。普通 Property 查询仍读取软件状态。

Core 自 2026-11-24 起提供主线程查询 `TxSessionService::pendingOperationReasons()`，原因集合变化时发出 `operationStateChanged()`。在设备可用时，刷新请求同步登记，并覆盖后续的波形准备、设备 apply 与结果回写。配置批处理、设备切换、能力协调和 Core 向桥接业务交接也参与判定。切换中的短暂断开保留切换原因，普通断开后集合为空。空集合表示当前没有待完成的 Core TX 操作，成功与失败均可到达此状态。MScan 只跟踪实际进入执行链的操作。[Core 实施记录](../TaskLog/2026-11-24_core-tx-operation-state.md)

Streaming 启动、运行中改参和停止都参与等待。启动与重配持续到设备配置结束且 `tx_send_stream()` 接受首个 IQ 块，停止持续到发送线程结束当前设备 I/O。播放继续时，`STAT:OPER:COND?` 的 bit 10 为 1。启动／重配失败先将 `-300` 和详细原因写入错误队列，再解除等待。普通断开也会解除等待。Streaming 的设备时序待验收。[Streaming 实施记录](../TaskLog/2026-11-24_streaming-scpi-operation-completion.md)

ProgrammedArb 的桥接 I/O 和其他 Core TX 异步失败转发待接入。

例如先发 `FREQ:MODE LIST;OUTP:STAT ON;*OPC?`，再发 `LIST:RUNN?`，后者会在 Core 操作结束后读取 MScan 生效状态。连续扫描的轮次、具体频点和实际 RF 稳定时间须另行观测。[实现与回归用例](../TaskLog/2026-11-24_scpi-operation-barriers.md)

### 4. 启用后控制和数据端口监听所有接口

`TransportTCP::start()` 和 `DataChannel::start()` 均使用 `QHostAddress::Any`。监听由 SCPI 对话框手动启用，两条通道当前无应用层身份验证或绑定地址配置。部署文档应写明启用时的实际监听范围。改变默认绑定方式需考虑现有 LAN 控制用法。

### 5. 端口设置只保留到进程退出，数据通道失败已有处理

- 默认 5025/5125 来自 `ScpiRegister::init()`。对话框把编辑值写入当前 `ScpiServer`，重开窗口读取当前值，重启进程恢复默认值。启用时只检查 `toUInt()` 是否成功，监听前转为 `quint16`。普通文本框输入 `65536` 可通过当前转换检查，最终按端口 0 请求监听，界面仍显示请求值。aarch64 专用数字键盘的范围是 0 到 65535。
- `ScpiEngine::start()` 以控制端口监听为启动成功条件。数据端口失败记入日志，控制端口可继续工作。控制端口失败则回滚已启动的数据监听，并取消对话框中的启用状态。现有产品 handler 未调用 `ScpiServer::transferData()`，定长块输入和截图路径查询均走控制端口。

### 6. 部分旧 setter 会夹取有限越界值

`parseDouble()` / `parseUnits()` 已拒绝非有限值和不支持的特殊数值。`CORR` 数组、List 数据、`IQ:IMPairment` 等另有各自的业务校验。`FREQ:CENT` 和 `POW:LEV` 在解析成功后直接写 Property，`NumericProperty::setValue()` 按当前 metadata 的范围夹值。发送低于 Center 下限的有限值时，软件回读会显示边界值，setter 无专门的越界错误。

2026-11-02 的回写修复保留了 GUI 和业务的归一化，并使 Pulse DutyCycle 写入复用 Width 的编辑和生成路径。对 SCPI 的有限值范围，应逐族确认哪些属于允许归一化、哪些应拒绝，并以实际设备/业务的约束为依据。

### 7. 部分调制命令仍依赖业务名称精确匹配

`PANEL_NULL_CHECK` 调用 `BusinessManager::getBusinessByName()`，与 `IBusiness::name()` 逐字比较。Digital 使用含换行、末尾空格的 `Digital\nMod `，翻译后的 `fullName()` 是另一字段。改动该字面键会影响查找；同一个 `name()` 也被 `BusinessManager` 用在配置读写中。后续迁移调制命令时，可在业务注册处提供稳定的查找键，同时保留已有配置的名称兼容。

## 当前推荐落地顺序

1. 设备 apply 相关 setter 成功表示软件已接受请求。使用完成命令等待 Core 到达终态，按具体状态查询确认应用结果。
2. 按 [分阶段方案](../TaskLog/2026-11-09_scpi-status-and-opc-implementation-plan.md)验证配置恢复、波形生成和下载、扫描、失败及断开，补充异步失败上报和桥接业务完成依据。
3. 按命令族收口有实际影响的缺口。优先核实有限越界的公开行为、Reset 失败反馈、端口输入边界及 LAN 使用约定。迁移 UI 依赖时沿用已有 Property 或 Core 服务，剩余 MOD/Sweep/调制启停再按各自业务 owner 接入 UI 无关入口。

## 后续 SCPI patch 审查清单

1. 参数范围、业务规则和结果由哪个现有组件负责？设置和查询各自使用配置、提交数据还是设备结果？
2. 依赖了哪个 UI host 或注册名称？前面板与 SCPI 能否共用业务入口与正常回写？
3. 同步拒绝与异步失败如何被客户端识别？完成命令等待到哪个阶段？
4. 同一程序消息的相对命令头、各查询响应顺序、定长块处理和嵌套事件循环行为是否兼容？
5. 控制与数据通道的启动结果、监听范围和端口显示是否与实际一致？

第三方 libscpi 的 CMake 目标仅编译选定的 `libscpi/src` 源文件，随库保留的示例和上游测试不进产品目标。目录裁剪和许可证整理属于第三方依赖维护。
