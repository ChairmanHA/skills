# 测试机制现状分析与 SCPI + 配置文件自动化提升方案

- 日期：2026-11-09
- 范围：`D:/development/vsg2.0/scripts/test_scpi_*.py`、`README_scpi_gui_baseline.md`、`D:/development/vsg2.0/testcases/`（Markdown 57 项、11 个 JSON、2 个 WAV、Excel 同步脚本），以及与测试直接相关的 SCPI/TX 运行时代码。
- 验证级别：static（只读代码与文档，未运行脚本、未构建、未上机）。
- 状态：分析与方案提案，**未实施**。下文“观察”均有代码/文档出处，“推断/建议”单独标注。

## 1. 现状盘点（观察）

| 资产 | 内容 | 作用 |
| --- | --- | --- |
| `testcases/SGStudio测试用例.md` | CASES 区 57 项（F1、G1～G13 含 G12.1～12.13、H1～3、I1～2、J1～2、K1～3、L1～6、M1～6、N1～3、O1～4、P1～2），每项 6 个自然语言字段 | Excel 文本源，也是引导模式的提示真源 |
| `testcases/sgstudio_md_sync.py` | Markdown ↔ Excel `SGStudio` 表，G/H（结果、测试人+时间）列按序号保留 | 用例文本维护 |
| `testcases/*.json` | F-1、G-1/2/3/4/6、H-1/2、L-1、M-1/2，均为 GUI 全量快照（约 4.7 KB/个） | `SYST:TEST:LOAD` 初始化 + 期望值来源 |
| `scripts/test_scpi_gui_baseline.py`（887 行） | 默认人工引导；`--batch/--scenario/--basic-only` 为旧批量 | 主入口 |
| `scripts/test_scpi_foundation.py` | 传输/解析回归、只读命令目录、同值写回、MScan 接线；同时被 baseline 当作库导入 `ScpiSession` | 协议回归 |
| `scripts/test_scpi_error_queue.py` | 错误队列容量/溢出/交替 | 专项 |
| `scripts/test_scpi_listmode_learned.py` | List 模式 LEARn、大点表、含 `*RST` | 专项 |
| SCPI 命令面 | `scpiregister.cpp` 注册 217 个模式；内部命令 `SYST:TEST:LOAD`、`SYST:TEST:SAVE`、`HCOP:DATA?` | 自动化通道 |

批量模式每个检查点：发送命令 → 固定 `sleep(wait_ms)`（默认 100 ms；RMS 1000 ms；M2 2 s；A_RMS 5 s）→ 查询公共参数与期望 → 清空 `*ERR?` → `HCOP:DATA?` 截图 → 下一点（`test_scpi_gui_baseline.py` `Baseline.checkpoint`）。引导模式：加载配置 → 人工操作/截图 → 人工 p/f。

## 2. 问题分析

### P1 没有完成语义，所有等待都是无依据定时（最根本）

- 观察：`parser.cpp:59` 的 `*OPC?` 固定返回 `"1"`；`ScpiRegister::onWait()`（`scpiregister.cpp:809`）直接 `return true`；KB [SCPI 审阅 §3](../KnowledgeBase/scpi_review_and_integration_plan.md) 已记录。脚本只能 `time.sleep`。四个脚本中只有 `test_scpi_listmode_learned.py:256 wait_until_running` 做了有界状态轮询。
- 观察：运行时其实已有完成状态：`TxPipelineRuntime` 有 `applyQueueIdle()` 信号、`isApplyInFlight()`、`m_desiredGeneration / m_hardwareGeneration`、`m_hasPendingLatest`、`m_desiredSnapshotPending`；`TxSessionService` 有 `m_refreshDirty / m_refreshScheduled` 和 `deviceConfigurationDone / sweepConfigurationDone / appliedStateChanged`；各调制器调用 `PlaybackPayloadReservation::notifyGenerationChanged()`。只是没有暴露给 SCPI。
- 影响（推断）：
  - 100 ms 后的 PASS 只说明“意图已被接受”，不说明波形已生成、下载、设备已应用；树莓派/大波形/AWGN 100 ms 上更慢，结果随机器和负载漂移（假 FAIL 或假 PASS）。
  - 慢场景只能把等待加大，整体测试时间线性变长。
  - 违背本项目自己的 harness 原则：[Agent Loop Harness §9.4/§12.2](../KnowledgeBase/copilot_prompt_workflow_notes.md)“区分已接受和已完成”“状态驱动等待，不使用无依据延时”。

### P2 回读是“意图回环”，不是设备结果

- 观察：getter 直接读 Property，例如 `onGetFrequency` 返回 `Center` 属性值（`scpiregister.cpp:957`），`onGetMultitoneCount` 返回 `Multitone_Count`（`scpiregister.cpp:2414`）。setter 写同一属性。
- 影响：set X → get X 主要验证 SCPI→Property 管道。Markdown 判据大多针对设备与射频（“载波位置/电平随操作变化”“迟到波形或扫描不覆盖”“无旧流复活”），当前自动检查无法覆盖。例如 K1 只能检查最终意图为 F1，**不能**发现迟到波形把设备改回 G2；`POW:LEV?: None`（只记录）类断言还会让 PASS 名不副实。历史报告中的“28/35 PASS”属于参数通路通过，不等于功能通过。

### P3 射频判定全部人工

- 观察：脚本不控制接收机；README 明确 UI/RF 为 pending；截图只截 MainWindow（`HCOP:DATA?`），频谱仪、弹窗、Minibar 不在其中。
- 影响：57 项中几乎每项都需要人看 SAStudio。耗时最多、主观性最强、回归频率最受限制的环节正是这一层。截图是证据，不是断言。

### P4 用例定义与自动化逻辑“双真源”，追溯断裂

- 观察：Markdown 只有自然语言；自动化逻辑分散在 Python 的 `CASE_PROFILES`、`guided_actions()` 中的 `if name == "G6"/"K1"/"G13"...` 分支、`SCENARIOS` 函数里。README 规定“加载参数以 JSON 为准，人工要求以 Markdown 为准”，同一用例因此有两个真源。
- 观察：已出现漂移。M2 文档写 50 MSPS，M-2.json 为 30 MSPS，脚本只在运行时提示；批量标签 `"L3 stop MScan"`、`"L3 resume saved MScan"` 与 Markdown L3（未 Apply 编辑与已提交数据）语义不一致；`H3`、`L5` 也只覆盖了其中一部分。
- 影响：新增或修改一个用例要同时改 Markdown、Python、JSON 和 Excel；报告里的点名无法可靠映射回 Excel 行。

### P5 配置夹具是 GUI 全量快照：脆弱、有噪声、职责混合

- 观察：每个 JSON 都包含全部 12 个业务块和全部 common 字段，约 95% 相同。键名直接使用 UI 显示名或历史拼写，如 `"Digital\nMod "`（F-1 的 `currentWidgetBusiness` 也是它）、`activedBussiness`、`Bandwith`。数值带浮点噪声，如 `1004999999.9999999`、`0.20000000298023224`。版本混用：G-6 为 v5（带 `power`），其余为 v4。新功能（Level Offset、Impairments、Flatness 等）不在夹具里，依赖“默认值合并”。
- 影响（推断）：
  - 默认值一改，旧夹具的语义就会静默改变。
  - 夹具无法表达“只有这几个字段对本用例有意义”。
  - 维护必须开 GUI 重存。
  - 同一份文件既当测试初始化，又当持久化回归样本，两种职责互相牵制：想精简夹具，就会削弱持久化覆盖。
  - KB [SCPI 审阅 §8](../KnowledgeBase/scpi_review_and_integration_plan.md)“业务查找依赖显示名”的问题也延伸到了夹具。

### P6 缺少确定性的初始状态与隔离

- 观察：`*RST` 走 `ACTION_PRESET`，可能被模态框阻塞（KB §4），所以 baseline 不用它，listmode 专项因为含 `*RST` 被排除在 GUI 用例之外。隔离只靠 LOAD 全量快照，收尾只关 RF/MOD/Sweep。
- 影响：LOAD 不覆盖的状态会跨用例泄漏，例如 MScan Draft、端口/参考面、Minibar、GNSS、器件设置。O2→O3 等用例本身依赖顺序，单项复测难以复现。

### P7 错误与边界语义不足，限制了 G12 类用例自动化

- 观察：KB §2 指出产品侧失败未统一进入错误队列；KB §7 指出 `NumericProperty` 全局 clamp，SCPI 越界可能被静默夹值并返回成功（只有 NaN/Inf 被拒）。异步 apply 失败（`applyStateChanged(request,false)`）没有回到 SCPI 可见面。
- 关联：[2026-11-06 偶数多音分析 §3](2026-11-06_even-multitone-ffm-download-race-analysis.md) 发现 `tx_download_waveform` 返回 +10 被 `warningAsSuccess` 吞掉。这类“静默失败”恰恰是参数回读永远发现不了的。
- 影响：G12.1～12.13（13 项，占 23%）的业务边界规则本可以用 SCPI 表驱动测试，但目前只能人工点键盘。

### P8 SCPI 命令面缺口阻塞了大量用例

- 观察：217 个模式中没有 Playback/ARB（文件、Offset、SamplesToUse、IQ Scale、AutoScale、Period）、Streaming（文件列表、采样率、运行/进度）、Quick Waveform、GNSS、设备状态（UNLEVEL、参考失锁、实际参考源）、Single/Continue 与立即触发、设备选择/断连、启动策略、非交互 Preset 等命令。README 已说明“Playback/Streaming 尚无文件名、采样率、进度查询”。
- 影响：G2、G11、G12.11～13、K2/K3、L2/L4/L6、M1～M6、N*、O3/O4 都无法自动化（分级见第 6 节）。

### P9 无人值守的前置条件缺失

- 观察：SCPI 服务只在 `SCPIDialog` 中手动启动（`scpidialog.cpp:137 scpiServer->start()`），没有启动参数或持久化开关（KB §6 端口也未持久化）。
- 影响：每次启动都要人点开 SCPI。K3、L6、M4 这类“退出→重启→恢复”用例，以及任何 CI/夜间运行，都无法由 runner 闭环完成。

### P10 结果管理与追溯

- 观察：每轮报告是 `<runtime>/images/scpi-*.json`；Excel 的 G/H 列由人工填写；报告记录 `*IDN?`（设备 UID 与 FPGA/MCU/EIO 版本），但不记录 SGStudio 版本、git commit、型号/选件、接口类型；没有 JUnit 之类的机器汇总，也没有跨轮次/跨版本对比。
- 影响：回答“2.7.9 相对 2.6.4 哪些项退化”“某型号/某固件是否通过”只能靠人翻文件。偶数多音事件中，2.6.4 与 2.7.9 的对比就是靠人工完成的。

### P11 脚本工程化

- 观察：
  - 有三套独立的会话实现：foundation 的 `ScpiSession`、error_queue 的 `Session`、listmode 的 `ScpiSession`。
  - baseline 把一个测试脚本当库导入。
  - `profile_expectations()` 用 Python 硬编码“JSON 字段 → SCPI 查询”映射，等于在测试侧复制了一份持久化知识。
  - harness 自身没有测试。2026-10-23 的“全局驻留误报”就是 harness 缺陷（README 记录）。
- 影响：每次改动容易引入 harness 缺陷，并被误判为产品缺陷。

### P12 无硬件不可测，竞态类用例不可复现

- 观察：仓库 CMake 中没有 `enable_testing/add_test/QtTest`，也没有模拟设备，`IDevice` 只有 HTRA/Analog 真实实现。
- 观察：G6“统计未就绪时保存”、K1“迟到波形不覆盖”、M4“迟到流数据不覆盖”都依赖时序运气。KB 也承认“同批保存不能证明统计未知”，即用例前提本身无法被测试证明。
- 影响：RMS/Level 意图、MScan Draft/Submitted、配置合并与相对路径、参数 resolver 等纯逻辑都要接设备跑，无法在每次提交时回归。

### P13 安全边界（次要但应同步处理）

- 观察：TCP 监听 `QHostAddress::Any`（`transport_tcp.cpp:77`）；`SYST:TEST:LOAD` 接受绝对路径，`SAVE` 会写文件，`HCOP` 会截屏。三者无条件注册（`scpiregister.cpp:206-208`）。
- 建议：测试命令应显式启用（启动参数或 watermark/test 构建），并默认限制为本机访问，符合 harness 原则 §12.6。

## 3. 目标模型

1. **三层判据，每个用例声明自己验证到哪一层**
   - L1 意图：Property/SCPI 查询（现状）。
   - L2 应用：运行时已应用请求与设备写回，例如管线、设备 center（含偶数多音 s/2 偏移）、端口电平、采样率、波形身份/点数、MScan 已提交修订号、最近一次 apply 结果。
   - L3 物理：接收机实测频率、电平、音数/间隔、脉宽/周期、扫描覆盖与驻留、有无输出。
2. **状态驱动等待**：每个动作后执行 `wait_settled(timeout)`。超时就是失败，并保存现场；只有在用例明确要求“不等待”（K1、G6）时才连续发送。
3. **单一真源**：每个用例有一份结构化规格，Markdown/Excel 文本由它生成或接受它的校验；runner 是通用解释器，不再出现按用例编号的 `if` 分支。
4. **能力标签自动跳过**：用例声明 `requires`（如 320M、HighPower、USB3、ETH、外触发、10 MHz、GNSS、接收机、多设备）。runner 根据 `*IDN?`、能力查询和工位配置自动选择或跳过，并写明原因，取代 `--basic-only` 这类硬编码开关。
5. **测试接缝显式启用**：hold/fault 等测试命令只在测试模式下注册。

## 4. 分阶段方案

### Phase 0：纯脚本侧，零产品改动（约 1～3 天）

- 抽出 `scripts/sgs_test/` 包，包含：
  - 唯一的 `ScpiSession`（带 `query/send/drain_errors/poll_until`），三个脚本都改为导入它；
  - `report.py`：统一 JSON 报告，追加 JUnit XML；记录 SGStudio 版本（可读 `bin/` 中版本资源或由 runner 传入）、git commit、`*IDN?`、工位配置；
  - `cases.py`：现有 Markdown 解析器搬入此处。
- 临时把“固定 sleep”替换为**意图层轮询**：轮询期望的查询，直到相符或超时（有界，默认按场景设置）。这只是 Phase 1 之前的过渡，不能当成完成判据。
- 用 stdlib `unittest` 为 `profile_expectations/read_cases/matches` 写 harness 自测。
- 夹具 linter：
  - 校验 `formatVersion`、引用资源是否存在、Markdown 与 JSON 的显式数值是否一致（如 M2 采样率）；
  - 检查 Markdown 中每个用例 ID 在 runner 里要么有自动规格，要么显式标为 manual；
  - 检查批量检查点名是否对应真实用例 ID（修正 `L3 stop MScan` 一类标签）。
- 扩展 `sgstudio_md_sync.py`：增加 `results` 子命令，把某轮报告写入 Excel 的 G/H 列（结果 + 版本 + 时间），Excel 从“输入”变成“输出”。

### Phase 1：让完成状态与设备结果可观测（产品侧关键改动，约 1 周）

> 这是后续一切自动化的地基。数据大多已存在于 `TxSessionService`/`TxPipelineRuntime`，只需暴露，不新增业务规则。

1. `SYSTem:TEST:STATe?`：返回单行 JSON 字符串，客户端轮询。**不**实现阻塞式 `*OPC?`，因为 parser 在 GUI 线程同步执行，且 handler 可能进入嵌套事件循环（`parser.cpp:104` 注释），阻塞等待风险高。建议字段：

   ```json
   {"settled":true,"deviceOpen":true,"capabilitiesReady":true,
    "intentDirty":false,"generating":false,"applyInFlight":false,"pendingLatest":false,
    "desiredGeneration":42,"hardwareGeneration":42,
    "lastApply":{"seq":17,"ok":true,"error":""},
    "pipeline":"Playback","carrierPlan":"Fixed","streaming":"stopped"}
   ```

   - `settled = deviceOpen && capabilitiesReady && !intentDirty && !generating && !applyInFlight && !pendingLatest && (hardwareGeneration == desiredGeneration || lastApply 失败且对应当前 desired)`。
   - 需先确认：哪些管线不由 `TxPipelineRuntime::isCoreManaged` 管理（如 Streaming 的 legacy 双线程、Analog provider），这些需单独给出 running/ready 状态。没有可观测完成信号的管线应返回 `"settled":null` 并说明原因，**不能**谎报 true。
2. `SYSTem:TEST:APPLied?`：基于 `appliedRequest()` 与硬件写回返回 L2 快照（管线、设备 center、端口电平/用户电平、RF/MOD、采样率、波形身份与点数、`fixedPlaybackCenterOffsetHz`、`mscanSubmissionRevision`、deviceUid）。K1 的“迟到不覆盖”、偶数多音的 s/2 偏移都能据此直接断言。
3. `SYSTem:TEST:DEVice:STATus?`：基于 `DeviceRealTimeStatus` 返回活动告警集合（UNLEVEL、参考失锁等）和实际参考源。J1/J2/M5/O1 的判据据此可机器判定。
4. 异步 apply 失败可见：`lastApply` 记录 `applyStateChanged(..., false)` 与 `deviceConfigurationDone(error)`。后续可评估是否同时推入 SCPI 错误队列（真实仪器对重叠命令的标准做法）。
5. 无人值守前置：
   - 启动参数 `--scpi[=port]`（或持久化“启动时开启 SCPI”）；
   - 增加 `SYSTem:TEST:PRESet`：复用 Preset 业务，不弹模态，并复位 MScan 与功率意图；
   - 测试命令只在 `--scpi-test` 或 watermark 构建中注册，默认只监听本机。

成功判据：baseline 全部改用 `wait_settled`，默认 100 ms 不再出现在任何断言路径；同一组场景在 Windows Debug 与树莓派上不调等待参数即可稳定通过（需实测）。

### Phase 2：用例规格单一真源 + 夹具分层（约 1 周）

- 规格格式：TOML（Python 3.11+ 自带 `tomllib`，仍无第三方依赖）。每个用例一份 `testcases/specs/<ID>.toml`，ID 与 Markdown 一致：

  ```toml
  id = "G1"
  requires = ["device", "receiver"]
  setup = { fixture = "G-1" }            # 或 preset + commands

  [[step]]
  name = "改为 3 音、0.5 MHz"
  send = ["MTON:COUN 3", "MTON:SPAC 500kHz"]
  wait = "settled"                       # 或 { poll = "MTON:COUN?", equals = 3 }；sleep 必须写 reason
  intent  = { "MTON:COUN?" = 3, "MTON:SPAC?" = 5e5 }
  applied = { pipeline = "Multitone" }
  rf      = { measure = "tones", count = 3, spacing_hz = 5e5, tol_hz = 1e3 }

  [[step]]
  name = "4 音中心无载波泄露"
  send = ["MTON:COUN 4"]
  wait = "settled"
  rf   = { measure = "tones", count = 4, center_leakage_dbc_max = -30 }

  [[step]]
  manual = "MOD OFF 时观察 UI 按钮状态"   # 仍需人工的步骤显式标注，runner 进入引导
  ```

  runner 是通用解释器：同一份规格既能全自动运行，也能在含 `manual` 步骤时进入现有引导流程。Markdown 的“需手改/判据”可以由规格生成，或由 linter 校验两者一致。
- 夹具分层：
  - **行为用例**默认从 `SYST:TEST:PRES` 起步，再用 SCPI 命令建立前置条件（与真实仪器的 `*RST` + 命令习惯一致），只声明与本用例相关的参数；
  - **持久化用例**保留现有 11 份全量 JSON，作为 Save/Load 回归样本：LOAD → SAVE → 与原文件做忽略易变字段的结构化 diff，并验证旧 v4 → v5 迁移。两种职责分开后，持久化覆盖不会因为精简夹具而丢失。
  - 如果确需文件夹具，用“基线 + overlay”（点路径 → 值）在运行时合成，避免整份复制。

### Phase 3：接收机闭环（L3 自动判定，约 1～2 周）

- `Receiver` 抽象层，后端可插拔：
  - **N9040B**：KB 已有 [N9040B 测试方案](../KnowledgeBase/n9040b_pulse_detection_122_narrow_pulse_test_guide.md)，说明实验室有此仪器。它原生支持 SCPI over TCP 5025，可直接复用同一个 `ScpiSession`，无需 pyvisa；
  - **HAROGIC 频谱仪/SAStudio**：是否提供 SCPI 或可调用的 SA API（ctypes）**待确认**；
  - **Manual**：回退为现有引导模式下的人工判定。
- 首批测量原语：峰值频率/电平；N 音计数与间隔；中心泄露 dBc；占用带宽（Ramp/AWGN）；零跨度包络的脉宽/周期/顶部电平（G3/G13）；Max Hold 覆盖范围（H1）；零跨度驻留时间与电平台阶（H2/L1）；有无输出（RF/MOD OFF）；突发计数（G11 Single）。
- 线损只标定一次：使用 `POW:OFFS` 或接收机 Ref Offset，容差写在规格里。
- 这样一来，F1、G1、G3、G4、G13、H1、H2、L1、L5 等用例的判据可以完全机器化；截图保留为辅助证据。

### Phase 4：无硬件回归与可复现竞态（可选，收益高，约 2～3 周）

- **测试接缝**（只在测试模式注册）：`SYST:TEST:HOLD GENeration|APPLy,ON|OFF`，在 `TxPipelineRuntime::dispatch` 或波形生成发布点保持/释放。
  - G6：HOLD GEN → 设 RMS → SAVE A → 释放，“统计未就绪”从靠运气变为确定。
  - K1/M4：HOLD APPLY → 连续 LOAD → 释放，断言只有最新请求被应用（latest-intent）。
  - 归属：TxPipelineRuntime 维护者评审，不改业务语义。
- **h2_api 记录代理**：生成同名导出的代理 DLL/SO（`h2_api.h` 约 67 个函数），转发到真实库，并用 JSONL 记录时间、参数和返回值。
  - 真机：同一用例在 2.6.4 与 2.7.9 上的 API 序列可以直接 diff。偶数多音事件中人工整理的调用序列与返回码（如 +10），会变成可检索的证据。
  - 故障注入：让 `tx_download_waveform` 返回 +10，断言 SGStudio 不会把它当成功（对应 2026-11-06 TaskLog §3 的缺陷）。
- **模拟设备**：实现 `IDevice` 的 SimDevice 插件（约 17 个纯虚函数 + 能力快照），可配置时延和故障。在 CI 中 headless 运行 Phase 1/2 的 L1+L2 用例与 foundation 协议回归。
- **C++ 单元测试**：引入 CTest + QtTest，覆盖纯逻辑：配置合并与相对资源路径、MScan Draft/Submitted、RMS/Level 意图状态机、各业务 resolver 上下限。G12.1～12.13 的业务规则改为表驱动单测，键盘重开回写只保留少量人工抽样。

## 5. 优先级建议

| 优先级 | 事项 | 理由 |
| --- | --- | --- |
| P0 | Phase 1 的 `STATe?` / `APPLied?` / `DEVice:STATus?` + `--scpi` 启动参数 | 改动小、数据已存在，同时消除定时依赖和意图回环两个根本问题 |
| P0 | Phase 3 的 N9040B 后端 + 首批 6 个测量原语 | 人工工作量的大头在射频观察 |
| P1 | Phase 0 脚本重构 + Phase 2 规格单一真源 | 降低维护成本，修复追溯断裂 |
| P1 | Playback/Streaming/Single 触发/非交互 Preset 等 SCPI 命令补齐 | 解锁 M、G2、G11、K、L 系列 |
| P2 | Phase 4 测试接缝、API 代理、SimDevice、单元测试 | 进入 CI 与可复现竞态；偶数多音这类问题能更早被发现 |

## 6. 57 项用例自动化分级（建议，按上述方案）

| 级别 | 含义 | 用例 | 数量 |
| --- | --- | --- | --- |
| A | 现有 SCPI 已能驱动；补 Phase 1/3 即可全自动判定 | F1、G1、G3、G4、G5（页面浏览部分除外）、G7、G8、G9（Streaming 除外）、G13（UI 只读另查业务可编辑状态）、H1、H2、H3、K1、L1、L3、L5、O2（320M 设备） | 17 |
| B | 需新增 SCPI 命令/查询或测试接缝 | G2、G6、G11、G12.1～G12.13、K2（对话框部分保留人工）、L2、M2、M3、M6（确认提示需非交互策略） | 21 |
| E | 需外部可编程仪器/信号（函数发生器、10 MHz 源、GNSS） | I1、I2、J1、M5、O1、N2、N3 | 7 |
| C | 需 runner 控制进程与设备生命周期（重启、断连、切换设备） | K3、L4、L6、M1、M4、O3、O4 | 7 |
| D | 保持人工或半自动 | G10（键盘/Minibar 视觉）、J2（物理拔线；有可编程 10 MHz 源时可升为 E）、N1、P1、P2 | 5 |

说明：分级为静态推断。A 级当前只能自动到 L1，“全自动判定”依赖 Phase 1/3 落地。

## 7. 与 2026-11-06 偶数多音问题的关系（示例）

假设上述能力已经落地：

- 参数化矩阵用例（音数 4/5/6/10 × 间隔 1/5/10 MHz × 多台设备）可通过 SCPI 无人值守运行。L3 判据“预期音位功率 vs 本振泄露”能直接判定“无波形、只见 LO”。
- `APPLied?` 可直接显示设备 center 是否为 C + s/2、采样率是否为 5s；`lastApply` 可暴露被吞掉的 +10 警告。
- API 代理 trace 可在 2.6.4 与 2.7.9 之间直接 diff 调用序列，现场实验与人工整理表可以大幅减少。

## 8. 需确认事项

1. HAROGIC 频谱仪/SAStudio 是否有 SCPI 或可调用 API？N9040B 是否可固定接入测试工位？
2. 测试命令的启用方式：启动参数、watermark 构建，还是两者都要？是否接受默认只监听本机？
3. 非 core-managed 管线（Streaming 等）的完成信号由谁提供？是否接受在其就绪前返回 `settled=null`？
4. 用例真源选择：保留 Markdown 为人工文本 + TOML 规格为执行真源（推荐），还是在 Markdown 中扩展字段（会影响 Excel 同步格式）？
5. SCPI 越界策略：报 -222 还是保持 clamp 并允许查询实际值？这决定 G12 的自动化方式。

## 9. 建议的第一步（若批准）

先做 Phase 1 第 1～2 项（`STATe?`、`APPLied?`）和 `--scpi` 启动参数，再把 baseline 的 `basic` 场景改为 `wait_settled` + L2 断言，作为样板。验证级别为 debug-run：同一场景在 Windows Debug 与一台树莓派上，不调等待参数各连续通过 10 轮，以此验证稳定性。

## 10. 追加（2026-11-09 11:45）：真实 `*OPC?`/`*WAI` 与状态寄存器的代价和收益

用户前提：“射频结果靠人看 SAStudio”短期无法简单绕过。本节评估把 IEEE 488.2 完成语义和寄存器命令接入后的代价与收益，并从 RF 信号源日常使用角度考虑。仍为 static 分析。

### 10.1 现状（观察）

- **h2_api 没有完成或稳定确认**：`h2_api.h` 的 67 个导出函数中，没有 busy、settled、progress、扫描运行状态这类查询。`tx_query_*` 读的是主机缓存（见 [2026-11-06 TaskLog §2](2026-11-06_even-multitone-ffm-download-race-analysis.md)）。设备健康信息只有两个来源：
  - 函数返回码：`H2_WARNING_UNLEVEL 99`、`SYSCLK/ADDACLK/DLREF/RFLO/IFLO_UNLOCKED 15～19`、`PARAMOUTRANGE 98`、`BUS_TIMEOUT 10`、默认校准文件 42/44；
  - `device_query_clock(..., is_locked_o)`。
  - 结论：`*OPC?` 最多只能做到“SGStudio 已完成且 API 调用全部返回”，**无法证明 RF 已稳定**。
- **libscpi 已具备寄存器基础**：`ieee488.c`/`minimal.c` 已编译，提供 `SCPI_Core{Cls,Ese,EseQ,EsrQ,Opc,OpcQ,Sre,SreQ,StbQ,Wai}`、`SCPI_SystemError{Next,Count}Q`、`SCPI_Status{Questionable,Operation}*`、`SCPI_StatusPreset`。`SCPI_ErrorPushEx` 已按错误码区间设置 ESR 的 CME/EXE/DDE/QYE 位（`error.c:161-189`），但 SGStudio 没有注册任何读取 ESR/STB 的命令。
- **产品命令面**：只有 `*OPC?`（固定 `"1"`）、`*WAI`（空操作）、`*CLS`、`*RST`、`*IDN?` 和非标准的 `*ERR?`。
  - `*CLS` 只调用 `SCPI_ErrorClear`，没有清 ESR/事件寄存器（`scpiregister.cpp onClear`）。
  - `*ERR?` 返回 `"code, 通用文字"`（`parser.cpp:49`），丢弃了 `reportError` 写入的 device-dependent 详细原因。
- **查询失败无响应**：handler 返回 `nullopt` 时，libscpi 推入 -200（`parser.c:143-147`），但不输出任何内容，客户端只能等到超时。例如：
  - 未连接设备时 `*IDN?` 不响应（`onGetIDN`）；
  - 只读属性的 setter 静默失败，统一报 -200。
  - `*IDN?` 格式也不是 IEEE 488.2 的“厂商,型号,序列号,固件”4 字段。
- **执行模型**：所有 TCP 客户端共用一个 `Parser` 和 libscpi 上下文（`scpi_engine.cpp:171`、`transport_tcp.cpp onNewConnection`），错误队列和寄存器是全局的。解析在 GUI 线程同步执行，`Parser::pushCommand` 支持 handler 嵌套事件循环时保持 FIFO（`parser.cpp:104` 注释）。libscpi 的 `SCPI_Parse` 一次处理整行程序消息，不支持在 `;` 中途挂起后恢复。
- **完成状态已有大半**：
  - `TxSessionService::requestRefresh()` 同步置 `m_refreshDirty` 并 `markDesiredStateDirty()`（`txsessionservice.cpp:486-501`），之后 `QTimer::singleShot(0)` 执行刷新；无设备或能力未就绪时不调度（`:529-536`）；
  - `TxPipelineRuntime` 有 `isApplyInFlight()`、`applyQueueIdle()`，以及 desired/hardware generation；
  - `TxProviderExecutionContext` 有 `dataReady/errorMessage`；`TxPowerDisplayState` 有 Waiting/Applying/Applied/Failed。
  - 只覆盖 core-managed 管线：`isCoreManaged` 只包含 Mute/FixedCw/SweepCw/FixedPlayback/SweepPlayback，**FixedStream/SweepStream 不在其中**（`txpipelineruntime.cpp:39`）。
- **设备告警已有**：`DeviceRealTimeStatus.activeWarnings`、`IDevice::txLevelUnlevelActive()` 已驱动 UI 的 UNLEVEL 标记（`mainwindow.cpp:450-475`）。

### 10.2 设计要点（建议）

1. **语义**：SGStudio 的设置命令全部按 IEEE 488.2 的 overlapped 命令处理。
   - `*WAI`：暂停执行后续命令，直到无 pending operation；
   - `*OPC?`：完成时输出 `1`；
   - `*OPC`：完成时置 ESR bit0。
   - 失败、设备断开、管线被 deactivate 都算“完成”，同时把错误推入队列，避免永久挂起。
   - 不设服务端超时，以客户端超时为上限。
2. **pending 的唯一归属在 Core**：由 TxSessionService 汇总一个 `operationState()` 加变化信号，SCPI 只做适配，Python/UI/Minibar 也可复用。初始来源：
   - refresh dirty/scheduled；
   - 能力 reconcile 或设备切换进行中；
   - 当前有效管线的 provider 尚未 `dataReady` 且无错误；
   - runtime in-flight 或 pending latest，或 hardware generation 未追上 desired generation 且未失败；
   - MScan Submitted 与 Applied 修订号不一致；
   - 配置 LOAD 事务（`setUpdatesSuspended`）进行中；
   - Streaming 启停过渡，需要 Streaming 插件提供 started/stopped 信号；在此之前不得谎报完成。
   - 不计入：外参考的 500 ms×10 锁定确认（作为 QUES 条件，而不是 pending，否则 `*OPC?` 会长时间阻塞）。
   - 无设备：intent 已被接受但无事可做，视为完成并置 OPER/QUES 自定义位“未连接”。是否同时报 -241 待产品决定。
3. **解析器挂起**：推荐“延迟应答 + 暂停 FIFO”，不用嵌套事件循环。
   - handler 发现有 pending 时登记 continuation，Parser 停止取下一条；完成信号到来后输出 `1` 或置位，再恢复。
   - 判定前先让出一个事件循环轮次，使 singleShot(0) 等排队传播先落地，避免“空闲缝隙”误判。
   - 限制：`*WAI`/`*OPC?` 之后同一行内剩余的单元已被 libscpi 执行。解决办法是在 transport 层于未加引号的 `;*WAI` / `;*OPC` 处切分程序消息。按 SCPI-99，公共命令不改变当前路径；SGStudio 的 SOURce/SENSe 均可省略，影响有限，需在文档中写明。
   - 嵌套事件循环方案代码最少，但会引入 GUI 重入、关闭顺序和 deleteLater 问题，项目已在 SoftKeyboard/MessageDialog 上吃过重入的亏，故不推荐。
4. **异步失败进入错误队列**：`applyStateChanged(...,false)` 和 provider `errorMessage` 映射为 -200/-300 或正数设备码（附 device-dependent info），ESR 随之自动置位。被 `warningAsSuccess` 吞掉的 +10 也属于这一类。

### 10.3 代价估算（静态推断，人日）

| 项 | 内容 | 估算 | 风险 |
| --- | --- | --- | --- |
| T1 寄存器与错误命令 | `*ESR?` `*ESE(?)` `*STB?` `*SRE(?)`、修正 `*CLS`、`SYST:ERR[:NEXT]?/:COUN?/:ALL?`（标准带引号与详细信息）、`SYST:VERS?`、`*IDN?` 始终响应、`*OPT?` | 1～2 | 低。libscpi 已有实现；`*IDN?` 改格式有客户兼容风险 |
| T2 Parser 延迟应答 | 挂起/恢复 FIFO、按 `;*WAI` 切分、断连清理、停止时取消 | 2～3 | 中 |
| T3 Core 完成状态汇总 | `operationState()`；审计约 12 个调制/回放 provider 与各 panel，确认 intent 变化同步或可追踪地标记 pending | 4～6 | **高**：任一路径漏标都会造成“假完成”，这是主要成本 |
| T4 异步错误入队 | apply/provider/download 失败推入队列 | 1～2 | 中：需决定 UI 发起的失败是否也入 SCPI 队列 |
| T5 STATus:OPER/QUES | 条件位接线（见 10.4）、事件锁存、ENABle、`STAT:PRES` | 3～5 | 中：PTR/NTR 依赖 libscpi custom registers 配置 |
| T6 Streaming 与非 core 管线 | Streaming 插件提供启停完成信号 | 约 5 | 高：legacy 双线程 |
| T7 验证 | Python 协议回归（`*WAI` 顺序、ESR 位、断连、多客户端）+ 真机各管线（CW、1024 音、AWGN、大 Playback、F/L/MScan、Streaming、切设备、离线）×（Windows、树莓派） | 3～5 | — |

合计：T1 约 2 天；T1～T5 + T7 约 3～4 周；含 T6 约 4～5 周。SRQ 需要 VXI-11/HiSLIP（raw socket 无法发出异步服务请求），约 2 周以上，暂不建议。

### 10.4 RF 信号源寄存器位映射（建议，参照 SCPI-99 与常见 SG 实践）

| 寄存器 | 位 | 来源 |
| --- | --- | --- |
| QUES | 3 POWer | UNLEVEL（`txLevelUnlevelActive` / 99） |
| QUES | 5 FREQuency | RF/IF LO 失锁 18/19，系统/DA/DLREF 时钟失锁 15/16/17，外参考失锁回退 |
| QUES | 8 CALibration | 使用默认校准文件 42/44，校准文件缺失 -29/-43 |
| QUES | 7 MODulation | 波形截断（Digital 大波形），未来的 IQ 过载 |
| QUES | 4 TEMPerature | 温度阈值待定义，可后置 |
| OPER | 1 SETTling | 10.2 第 2 点的 pending |
| OPER | 3 SWEeping | F/L/MScan 运行 |
| OPER | 5 WAIT for TRIGger | 外部/BUS/XPPS 已 arm 等待 |
| OPER | 8～12（设备自定义） | 基带生成中、波形下载中、Streaming 运行、设备未连接、设备切换中 |

### 10.5 收益（推断）

1. **产品正确性**（最大收益，不只是测试）：常见 ATE 写法是 `FREQ ...;POW ...;*OPC?` 然后频谱仪测量。目前 `*OPC?` 立即返回 1，对 1024 音、AWGN、大 Playback 这类耗时数秒的操作，客户会**在没有任何错误的情况下测到旧信号**。产品已在做 R&S List Mode 兼容，R&S 风格脚本大量依赖 `*OPC?`/`*WAI`，移植过来能跑但时序是错的。
2. **测试**：消除固定 sleep，结果不再随机器漂移，典型场景更快。它也是“频谱仪进环”的前提：不管频谱仪是 N9040B，还是可编程的 HAROGIC 接收端，SG 完成后才能测。
3. **可见性**：`*ESR?`/`SYST:ERR?` 和 QUES 位让“下载失败、失锁、UNLEVEL、参数被钳位”这类当前静默或只在 UI 上显示的问题，对脚本可见。
4. **上限**：
   - 无法证明 RF 已稳定，需要厂商给出稳定时间或完成确认（可并入 2026-11-06 TaskLog §7 的厂商问题）；
   - 连续扫描和 Streaming 的“完成”只能表示“已启动”；
   - 偶数多音那类“API 全部成功，但没有波形输出”的故障，`*OPC?`/QUES 都发现不了，仍需要接收端判定。

### 10.6 待决策

1. ~~`*IDN?` 是否改为标准 4 字段？~~ **已决定（2026-11-09 用户）：保持现有格式。**
2. ~~无设备时 `*OPC?` 立即返回 1 并置位，还是同时报 -241？~~ **已决定（2026-11-09 用户）：立即返回 1，并置“未连接”位（`STAT:QUES` 自定义 bit 9），不报 -241。**
3. UI 或 Minibar 发起的异步失败，是否也进入 SCPI 错误队列？

分阶段实施方案见 [2026-11-09_scpi-status-and-opc-implementation-plan.md](2026-11-09_scpi-status-and-opc-implementation-plan.md)。
4. 多客户端共享上下文：维持现状（并文档化），还是限制控制连接为单客户端？
5. 是否向厂商索取 FFM/下载/输出的稳定时间或完成确认？
