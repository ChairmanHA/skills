# SCPI 接入静态审阅与当前收口方案

日期：2026-09-17

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

## 2026-11-05：Level Offset 软件参考面

`POWer:OFFSet` / `POWer:OFFSet?` 已静态接入（短写 `POW:OFFS` / `POW:OFFS?`）。参数为有符号 dB，默认 0、范围 [-100,100]；接受裸数或 dB 后缀，拒绝 dBm/其他量纲、多参数、特殊/非有限数值。数值范围复用 PowerReferencePlane 的校验，越界报 DataOutOfRange 而不夹值。setter 只更新软件参考面和显示上下文，不触发 TX、扫描 Apply、RMS 目标重建或波形生成；query 返回该软件设置，不调用设备 API。

Level/RMS、Sweep Start/Stop 和 List Power 的 SCPI 绝对功率仍为用户参考面：set 在入口 toPort 一次，query 在出口 toUser 一次。内部 Minibar 协议 2 的数值及完整状态改为端口面，不改变外部 SCPI 合同；设备请求、IDevice::Profile 和查询回写始终为端口电平。尚未编译/运行，详见 [统一参考面设计](level_reference_plane_port_ipc_and_api_integration.md) 与 [实施 TaskLog](../TaskLog/2026-11-05_implement-level-offset-port-ipc.md)。

## 2026-10-23/26：内部基线自动化入口

以下为内部基线命令，不增加 GUI 菜单，也不扩展 `*OPC?` / `*WAI` 或执行完成状态。LOAD/截图已在 10 月 23 日的 Debug 程序上运行基础脚本；10 月 26 日新增 SAVE 及 RMS 场景仅做静态检查，尚未构建或运行。UI/射频复核及下述历史参数差异仍待确认。

| 命令 | 参数/响应 | 行为 |
| --- | --- | --- |
| `SYSTem:TEST:LOAD "testcases/M-1.json"`（短写 `SYST:TEST:LOAD`） | 路径字符串；设置成功无响应 | 复用 File/Open 的配置恢复、批量 pipeline 挂起、pending MOD 恢复及业务重新选择；可在未连接设备时恢复配置。成功仅表示配置读取/恢复并已请求执行，不代表波形生成、下载或设备配置结束。 |
| `SYSTem:TEST:SAVE "A_RMS.json"`（短写 `SYST:TEST:SAVE`） | 单个 `.json` 文件名；成功无响应 | 自动创建 `<runtime>/testcases/`，保存当前 GUI 配置和功率意图，同名文件覆盖。复用现有捕获/持久化实现，在 GUI 线程同步完成文件提交，不等待波形生成或设备执行。 |
| `HCOPy:DATA?`（短写 `HCOP:DATA?`） | 本机 PNG 文件的绝对路径字符串 | 捕获主窗口并直接保存到 `<runtime>/images/`；成功返回带外层双引号的路径，失败返回 `""` 并入错误队列；不弹出截图保存提示。 |

LOAD 路径由运行 SGStudio 的机器解析：相对路径固定以可执行文件目录的上一级为基准，即包含 `bin/`、`plugin/`、`configuration/` 的运行根目录，不跟随客户端或 SGStudio 的 cwd。绝对路径直接使用。例如可执行文件在 `<runtime>/bin/` 时，上述命令读取 `<runtime>/testcases/M-1.json`。SAVE 只接受文件名，不接受目录或绝对路径；`SAVE "A_RMS.json"` 固定写入与 bin 同级的 testcases，随后用 `LOAD "testcases/A_RMS.json"` 恢复。

命令中的路径字符串必须为 ASCII。2026-10-23 实测直接发送桌面中文目录产生 -101 Invalid character，改为 `<runtime>/testcases` 下的同一 JSON 后加载正常。因此使用 ASCII 相对路径，脚本在连接前拒绝非 ASCII 路径；不修改 parser 来扩展协议。JSON 内的中文 WAV 路径仍由配置加载实现直接读取，不受命令文本这一限制。

2026-10-27 起，GUI/SCPI 共用的配置文件持久化边界支持 Playback.FileName、Streaming.FileList 相对该 JSON 所在目录的资源路径：读取后转换为绝对路径再恢复；保存时仅将目标 JSON 目录内及子目录资源转为相对路径，外部绝对路径保留，不修改内存状态。空路径保持空；Save As 按新 JSON 位置计算。SCPI LOAD 自身的配置路径仍以运行根目录解析，不能混淆这两个基准；不上传/复制波形或自动选择其他设备。有效 JSON 对象仍按现有默认值合并规则恢复，不能把 `{}` 当成格式损坏样本。

仓库根 testcases 的 G-2/M-1 共用 `assets/16QAM_4MHz_1MHz.wav`，已原样复制实际原文件；所有测试 JSON 清理固定 QuickWaveFormData 的 filePath/currentPath 绝对根路径。Quick Waveform 固定使用当前运行包的资源根，根目录浏览时不保存 currentPath，子目录浏览与选中文件/列表项保存为相对此根的路径；旧配置不能覆盖资源根或将浏览位置指向过时开发目录。Python 预检对相对/绝对波形路径均按配置目录检查存在性。Windows watermark ON 包按实际 CMake cache 带 testcases/assets 与 scripts 下两个 Python/README，OFF 不附带；新变更仅静态核对，未构建/打包/执行 SCPI。

错误沿现有 SCPI 错误队列上报：空白 LOAD 路径或非法 SAVE 文件名为 `-224`；无 MainWindow 为 `-241`；文件读取/JSON 对象解析/恢复、保存目录创建/JSON 提交、截图捕获/PNG 保存失败为 `-200`。可用 `*ERR?` 读取错误码；普通加载失败在恢复前返回，不把读取失败当作加载成功。原 `*ERR?` 的通用错误文字格式保持不变。截图失败仍有空字符串响应，客户端不必等待无响应超时。

脚本按用户最新确认的简版流程执行：发送 LOAD 或设置命令 → 客户端等待 **100 ms** → 查询参数、RF/MOD/Sweep 及 `*ERR?` → 请求截图。大 WAV/IQS-WAV 可显式延长单组场景的客户端等待；服务器不添加 sleep、重试或完成状态轮询，`*OPC?` 当前仍固定返回 1。

2026-10-23 按用户本机运行脚本和程序的部署方式，取消最初的 PNG 二进制响应，保留命令名并直接在服务器保存。例：`HCOP:DATA?` → `"D:/development/vsg2.0/images/20261023_153012_123.png"`。文件名采用 `yyyyMMdd_HHmmss_zzz.png`，适配 100 ms 检查点。仅用控制连接读取一行路径，无须 5125 数据通道或二进制块读取。`images/` 与配置的相对路径使用同一运行根目录锚点，不依赖 cwd。

截图范围为当前 MainWindow 的 QWidget 图像，包含其子控件；独立顶层 GNSS/设置弹窗、系统桌面和硬件屏幕亮度不属于这次截图接口范围。截图在 GUI 线程同步捕获，不修改当前业务/页面，不承担设备完成或 UI 稳定等待。

实现入口：`src/plugins/scpi/register/scpiregister.cpp`、Core 的 `MainWindow::loadSettingsFile()` / `saveSettingsFile()` / `saveWindowScreenshot()` 和原 SettingsController。配置保存复用 `captureRuntimeProfileState()` 与 `writeRuntimeProfileFile()`（QSaveFile）；Core 负责窗口捕获和 PNG 保存，手动截图按钮保存后显示提示，SCPI 截图只返回路径，不另建发射、配置持久化或恢复链路。

2026-10-26 新增 `--scenario rms`，按用户最新 G6～G9：1024 音时连续发送 RMS=-40/SAVE A、Level=-15/SAVE B，中间无等待或截图；读取真实 A/B 文件检查 v5 功率意图及 1024 音/10 kHz、RF OFF/MOD ON，加载 A 等 10 s 再加载 B；随后测试 RMS 保持、新 Level 建立目标、RF/MOD 不重复补偿、F/L/MScan 门控及返回 Fixed。RMS 稳定检查使用固定 `--rms-wait-ms`（默认 1000 ms），其余点默认 100 ms，无 OPC/完成轮询或重试。同批保存不能证明统计未知；截图不能证明保存瞬间状态。主窗/Minibar 置灰和实际输出仍待人工复核，G9 不含手动 Streaming 文件加载。

`--basic-only` 默认选 basic/scans/files/repeat/rms，适合单台普通 125M 设备；拒绝 external，保持用户已注释的选件入口。输入配置须为 Standard/Internal/BUS，普通 Playback 不高于 125 MSPS；原 files 通过已保存 M-1 自动加载仍保留。v5 RMS 加载按 power 意图断言，不能把 common.Level 兼容值当新派生 Level；波形变化后的 Level 记录并由 rms 场景验证联动。原 Excel、基准 JSON/WAV 不改，A/B 由新保存命令生成。需重新构建更新 Core/SCPI 后运行，新接口/场景尚无实测结论。

精简脚本：`D:/development/vsg2.0/scripts/test_scpi_gui_baseline.py`，使用说明见同目录 `README_scpi_gui_baseline.md`。默认 basic/scans/files/repeat，含 MScan、Playback、Streaming；external 按接线单独选择，用户已注释掉选件场景入口，保持该修改。加载期望取自用户当前 JSON，不改原 Excel/JSON/WAV；图片和逐点报告位于 images/。本机 Python 3.12.13/.venv 已配置，四组基础场景共 59 点，51 PASS/8 FAIL，59 张截图均存在，最终 RF/MOD/Sweep OFF。差异集中在 MScan 全局驻留读回以及 Streaming 切换后的 Center/Level；未将这些失败改成通过或擅自扩展业务修复。UI/接收信号复核仍为 pending。报告 `scpi-baseline_20261023_125240_302101.json` 位于当前 Debug 运行根目录 images/，完整过程见本次环境 TaskLog。

2026-10-23 后续复测修正：用户指出 From List 不使用全局驻留，脚本现按活动模式保留逐点/全局其中一项查询与断言，并在模式切换时清掉未使用的继承期望。重跑失败所属 scans/files 两组，仍为 100 ms；35 点中 28 PASS、7 FAIL、35 张截图存在，最终 RF/MOD/Sweep OFF。原全局驻留误报消除；本轮 3 点出现 MScan 频率/功率/逐点驻留/范围与 JSON 不符，4 点为 Streaming 修改/开关或切 CW 的 Center/Level 不符。截图也显示这些状态；结束后一段时间只读电平仍为 -20 dBm，而最后 F-1 期望 -30 dBm，故至少该现象不能直接归为 100 ms 瞬时读回。原因待进一步定位，未改产品代码或放宽判据。新报告：`scpi-baseline_20261023_134351_581757.json`，原报告保留。

2026-10-23 后续静态配置核对：用户要求仅按保存配置调整未通过 testcase，禁止重测。相关 7 份桌面 JSON 与运行副本哈希相同，原加载期望已匹配文件；额外手改段才引入不同目标。脚本移除 MScan 的 GLOBAL 0.5 s/范围 2～3/第三点 -25 dBm 编辑和 M-1 的 1.01 GHz/-35 dBm 编辑，改按保存值停启、开关和重载。F-1 仍须检查原文件的 -30 dBm。该变更尚未运行，不改旧报告，不据此宣称剩余加载差异已解决。

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
- TCP transport、Parser 和当前产品 handler 在 GUI 线程执行；Parser FIFO 保持命令顺序，`mDraining` 防止嵌套事件循环递归进入共享 libscpi context。
- `Parser::stop()` 拒绝新命令并清空重入期间的待处理队列，析构路径不再等待 SCPI worker。
- `SCPIDialog` 作为启停和日志入口可以保留，但它不应成为命令行为 owner。

这些优点不要求保留 UI 自动化式命令实现。长期稳定形态仍应把命令映射到 UI 无关的 intent/service。

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


### 1. SCPI 命令仍依赖 UI 对象和可见主窗口

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

### 2. 命令错误没有进入 SCPI 错误队列

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

### 3. `*OPC?` / `*WAI` 没有真实完成语义

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

### 4. `*RST` 可能被 UI modal 阻塞

观察：

`onReset()` 触发 `ACTION_PRESET`。当前 handler 直接在 GUI 线程执行；如果 Preset 打开模态确认框，当前 SCPI 命令仍要等用户结束该交互后才能完成。

风险：

- 如果 Preset 触发模态确认框，协议命令会被 UI 交互延迟到用户点击之后。
- 自动化环境下这会表现为命令卡死。

建议：

- 为 SCPI 提供不弹窗的 preset/reset intent。
- 或把 `*RST` 明确标注为 UI 交互命令，不推荐自动化使用。

### 5. TCP 绑定地址和安全策略不明确

观察：

`TransportTCP::start()` 使用 `QHostAddress::Any` 监听控制端口。

风险：

- 默认向所有网卡开放 5025。
- 当前没有认证、访问控制或显式 LAN opt-in。

建议：

- 产品定位如果是本机自动化，默认改为 `QHostAddress::LocalHost`。
- 如果产品定位就是网口仪器，保留 `Any` 也可以，但 UI 和文档必须明确“已对局域网开放且无鉴权”。
- 后续可增加 bind address 配置和持久化。

### 6. 端口配置未持久化，数据通道策略不清晰

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

### 7. 参数越界被全局 `NumericProperty` clamp 掩盖

观察：

`NumericProperty::setValue()` 会按 metadata min/max 对所有写入方 clamp。

2026-11-02 参数回写修复保留了基础 metadata 夹值和既有有限输入归一化策略，设备联动上限仍由各 business resolver 负责。公共键盘不再用旧通知或 adapter 文本覆盖最终属性显示。Pulse 的 DutyCycle 写入改为由业务 `editingFinished` 换算 Width 并触发同一 resolver；派生刷新只镜像属性，避免仅修改 UI 属性而未重新生成波形。

同轮在 SCPI 输入边界为 `parseDouble/parseUnits` 增加有限值检查，NaN/Inf/溢出和不支持的特殊数值返回既有 `-222 Data out of range`，不进入 Property。上述修改仅静态验证；下列“有限越界是否拒绝”的协议策略建议仍不等同于已实现行为。

风险：

- SCPI 发越界参数时可能被静默夹到边界并返回成功。
- 这不符合常见 SCPI 行为，自动化客户端无法知道请求值无效。
- 这个变化影响所有 UI/业务调用方，不只是 SCPI。

建议：

- SCPI handler 在写 Property 前先按 metadata 校验。
- 越界直接推 `-222 Data out of range` 或项目定义的等价错误。
- 全局 clamp 是否保留应作为 UI/Property 行为单独评审，不应作为 SCPI validation 策略。

### 8. 业务查找依赖显示名

观察：

部分命令通过 `BusinessManager::getBusinessByName(...)` 查找业务，名称包含 UI 显示字符串，例如带换行或空格的调制业务名。

风险：

- i18n、文案、空格、换行调整都可能破坏 SCPI。
- 失败没有编译期保护，运行时也不易定位。

建议：

- 给业务引入稳定 ID。
- SCPI 使用稳定 ID 或业务注册表，不使用显示名。

### 9. 第三方 parser vendoring 较宽，但不是当前功能阻塞项

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
11. SCPI 路径是否重新引入了 worker blocking 回 GUI、析构 join worker 或逐条 UI 日志事件？

## 底线

当前 SCPI 支持可以作为“UI 可见条件下的过渡控制通道”继续推进，但不要把 UI 自动化路径包装成最终架构。正确方向不是重写 `TxApplyRequest` 或 `TxPipelineRuntime`，而是新增一层窄的 SCPI intent/service：它复用现有 Property、Profile、TxSessionService 和 runtime writeback，同时提供仪器协议需要的参数校验、错误队列、完成语义和安全配置。
