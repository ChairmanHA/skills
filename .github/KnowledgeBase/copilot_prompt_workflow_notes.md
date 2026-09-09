# Copilot 轻量协作与 Agent Loop Harness 设计说明

## 1. 目的

本文档记录当前仓库中关于“是否引入自定义 prompt、它应如何与模型默认分析流程协作，以及运行时任务如何形成 Agent 闭环”的结论，供后续建设 `.github/prompts/`、补充协作规范或为新项目设计 Agent loop harness 时参考。

本文不是某一条 prompt 的最终定稿，而是：

- 总结哪些规则值得固化到 prompt / instruction
- 总结哪些判断应继续留给模型默认分析流程现场决策
- 解释为什么当前仓库不适合直接照搬更重的多阶段文档流水线
- 区分协作提示体系与实际赋予 Agent 执行、观察、操作和验证能力的运行时 harness
- 总结 GacUI 案例中可迁移到 SGStudio 及其他项目的闭环设计原则
- 给后续实现轻量 prompt 时提供边界与取舍依据

## 2. 讨论背景

本次讨论参考了 VlppParser2 的 Copilot 定制体系：

- [VlppParser2 copilot-instructions.md](https://github.com/vczh-libraries/VlppParser2/blob/master/.github/copilot-instructions.md)
- [VlppParser2 0-scrum.prompt.md](https://github.com/vczh-libraries/VlppParser2/blob/master/.github/prompts/0-scrum.prompt.md)
- [VlppParser2 investigate.prompt.md](https://github.com/vczh-libraries/VlppParser2/blob/master/.github/prompts/investigate.prompt.md)
- [VlppParser2 .github/prompts](https://github.com/vczh-libraries/VlppParser2/tree/master/.github/prompts)
- [VlppParser2 .github/Agent](https://github.com/vczh-libraries/VlppParser2/tree/master/.github/Agent)

后续又分析了 GacUI 的完整 coding-agent 工作流：

- [GacUI AGENTS.md](https://github.com/vczh-libraries/GacUI/blob/master/AGENTS.md)
- [GacUI Automation Support](https://github.com/vczh-libraries/GacUI/blob/master/README.md#automation-support-for-coding-agent)
- [GacUI AutomationService 说明](https://github.com/vczh-libraries/GacUI/blob/master/.github/KnowledgeBase/manual/gacui/coding-agent/automation-service.md)
- [GacUI 运行指南](https://github.com/vczh-libraries/GacUI/blob/master/.github/Guidelines/Running-GacUI.md)
- [GacUI 端到端操作 SOP](https://github.com/vczh-libraries/GacUI/blob/master/.github/Jobs/DebugRemoteProtocolSop.md)
- [GacUI 单元测试快照说明](https://github.com/vczh-libraries/GacUI/blob/master/.github/KnowledgeBase/manual/unittest/gacui.md)

该仓库的特点是：

- 用多阶段 prompt 把需求拆成 scrum、design、planning、execution、verifying 等阶段
- 用多份 `Copilot_*.md` 文档把每个阶段的输出显式化
- 对构建、测试、知识库利用、阶段切换和更新机制给出非常严格的流程约束

这套做法很完整，但也明显偏重。对当前 SGStudio 仓库而言，直接照搬会引入较高的维护成本与流程负担。

GacUI 进一步说明了另一个问题：即使任务入口、设计文档和阶段 prompt 都很完善，只有当 Agent 能稳定地构建、启动、观察、操作和验证目标程序时，运行时任务才真正形成闭环。因此“提示工作流是否轻量”和“运行时 harness 是否完备”是两个相互关联但不能互相替代的问题。

## 3. 当前仓库已有的“常驻规则”

当前仓库已有的 `copilot-instructions.md` 规则提供了大量仓库级常驻约定，例如：

- 先读 `.github/KnowledgeBase/Index.md` 再决定看哪些知识库文档
- 只能读当前被 CMakeLists.txt 纳入项目的文件
- 不读 Qt 生成的 `moc_*.cpp`
- 写代码前需要重新读取可能已被用户改动的文件
- 构建 / 运行 / 日志的默认工作方式
- 设计文档放在 `.github/KnowledgeBase/`
- 任务计划和设计草案放在 `.github/TaskLog/`

这意味着：

- 很多“执行规则”其实已经在 instruction 中常驻存在
- 如果再把这些规则整段复制到 prompt，会产生重复维护
- prompt 更适合补充“单次任务真源、范围、验证等级、冲突处理方式”，而不是复写整份 instruction

这也是后续不建议把 `sg-tasklog-implement` 的 execution rules 写得过长的原因。

## 4. 应该固化到 Prompt 的内容

以下内容适合被 prompt 固化，因为它们属于“不该每次重新发明”的协作约定。

### 4.1 单次任务的真源

对当前仓库，单次任务最合适的真源通常是某一篇 [TaskLog](../TaskLog/2026-04-09_eth_manual_connect_minimal_plan.md)。

prompt 应明确：

- 本次任务必须引用一篇 TaskLog
- TaskLog 决定 goal、non-goals、minimal architecture、risk、verification checklist
- 若聊天中的补充要求与 TaskLog 冲突，应先报最小冲突点，而不是静默扩设计

### 4.2 范围控制

prompt 应允许用户声明：

- 做完整个 TaskLog
- 只做其中若干 step
- 只做某些文件或某个行为目标

这件事值得固化，因为它直接影响是否会越界实现。

### 4.3 验证等级

对当前仓库，至少应支持三类验证等级：

- `static`：只做静态检查，不构建不运行
- `debug-build`：做一次已有 Debug build tree 的编译验证
- `debug-run`：做 Debug 编译、运行、看日志或补日志定位

这同样不应每次重新解释，因为它与本仓库 instruction 中“是否需要跑运行时验证”的规则高度耦合。

### 4.4 冲突处理方式

prompt 应固定一条原则：

- 发现 TaskLog 与代码现状矛盾时，优先报告“最小 concrete mismatch”
- 不要为了完成任务而私自补设计

这点很重要，因为当前仓库大量任务来自阶段性设计文档，用户通常希望先看分歧，再决定是否继续改实现。

## 5. 不应该固化到 Prompt 的内容

以下内容应继续由模型默认分析流程现场决定，否则 prompt 会变成僵硬执行器。

### 5.1 具体先读哪些源码

instruction 已经规定“只读被 CMake 纳入的文件”，但具体先看哪些 `.h/.cpp`，应由模型根据 TaskLog、KnowledgeBase、当前代码形态现场判断。

### 5.2 具体实现路径

例如：

- 先改 controller 还是先改 device
- 先补 seam 还是先补 UI
- 某个局部逻辑用复用现有类还是抽一层 helper

这些属于局部工程判断，不适合在 prompt 里写死。

### 5.3 相关知识库的筛选

prompt 只需要规定“先读 [Index.md](Index.md) 并选择相关文档”，不需要提前枚举所有文档。否则 prompt 会和知识库本身重复，而且容易过期。

### 5.4 局部调试手段

是否补 `qWarning()`、是否先做静态推断还是先跑一轮 Debug，这些仍应由任务性质和现场证据决定，只保留“什么时候允许这么做”的仓库级规则即可。

## 6. 轻量 Prompt 与默认分析流程的分工

### 6.1 Prompt 负责什么

轻量 prompt 最适合承担以下职责：

- 指定任务真源
- 指定范围边界
- 指定验证等级
- 指定冲突优先级
- 强制尊重 non-goals

也就是说，prompt 主要负责“上层工作方法”和“输入约束”。

### 6.2 默认分析流程负责什么

模型默认分析流程仍然负责：

- 理解代码结构
- 建立局部假设
- 选择最小改动路径
- 判断哪些知识库条目真正相关
- 判断某个失败是根因还是连带问题
- 处理未在 TaskLog 中写死的工程细节

也就是说，默认分析流程主要负责“局部技术推理”和“现场收敛”。

### 6.3 为什么两者不能互相替代

如果没有 prompt：

- 模型仍可完成任务
- 但更容易遗漏用户真正关心的边界、验收定义和非目标

如果只有 prompt、没有默认分析：

- 流程会更一致
- 但 prompt 不可能穷举所有局部实现与调试判断

因此两者最好的关系不是替代，而是分层协作：

- prompt 固化上层规则
- instruction 提供仓库常驻约束
- TaskLog 提供单次任务真源
- 默认分析流程解决现场问题

## 7. 与 VlppParser2 方案的对比

### 7.1 VlppParser2 的优势

VlppParser2 的体系强在：

- 流程分阶段极其明确
- 文档化程度高
- 每一步产物可 review、可追踪、可回放
- 对复杂生成链、重型测试链、强验证型任务很有帮助

这类体系尤其适合：

- 多轮设计评审
- 大量代码生成
- 必须留下完整中间产物的团队协作
- 强流程、强文档、强验证导向的仓库

### 7.2 VlppParser2 的代价

它的代价同样明显：

- 提示体系重
- 多份文档维护成本高
- 阶段切换复杂
- 小任务会显得过度流程化
- 若 prompt / 文档本身过期，AI 会稳定执行过期流程

### 7.3 为什么当前仓库不宜直接照搬

SGStudio 当前已经具备：

- 比较完整的 `copilot-instructions.md` 规则
- 持续维护的 [TaskLog](../TaskLog/2026-04-09_eth_manual_connect_minimal_plan.md)
- 结构化的 [KnowledgeBase](Index.md)

换句话说，本仓库已经有了轻量版三件套：

- 仓库级常驻规则
- 单次任务计划文档
- 可检索的知识库

如果再叠加一整套 VlppParser2 风格的多阶段 `Copilot_*.md` 流水线，收益未必能覆盖复杂度。因此更合理的方向是：

- 保持 instruction + TaskLog + KnowledgeBase 为主体
- 只补少量轻量 prompt 作为任务入口
- 不引入完整的多阶段文档流水线

## 8. Prompt 与 Agent Loop Harness 的边界

### 8.1 Prompt 不能凭空赋予执行能力

`AGENTS.md`、instruction 和 prompt 能够规定：

- 任务如何分类和路由
- 先读取哪些上下文
- 哪些行为被允许或禁止
- 任务范围、非目标和验证等级是什么
- 遇到冲突、失败或不确定性时如何决策

但这些文件本身不能让 Agent：

- 启动一个原本无法启动的程序
- 读取一个没有暴露状态的 GUI
- 操作一个没有控制接口的应用
- 判断异步命令是否真正完成
- 从一个没有日志、快照或退出码的过程恢复事实

这些能力来自 Agent 宿主、仓库工具和目标程序三者的组合。只复制一份写得很好的 `AGENTS.md`，只能提高 Agent 的行为一致性，不能自动获得运行时闭环。

### 8.2 能力的四个来源

| 层次 | 提供者 | 负责内容 |
| --- | --- | --- |
| Agent 宿主 | Codex、Copilot 或其他 Agent 平台 | 终端、文件、进程、调试器、浏览器和工具调用能力 |
| 仓库工作流 | instruction、prompt、Project/KnowledgeBase、脚本和 SOP | 告诉 Agent 应运行什么、如何运行、期望看到什么 |
| 目标系统 | 应用内测试接口、状态导出、输入接口、诊断钩子 | 让程序本身变得可观察、可操作 |
| 证据存储 | 日志、快照、TaskLog、测试矩阵、退出码 | 让本轮事实可复查，并成为下一轮输入 |

Agent loop harness 是这四层之间的契约，而不是其中某一个脚本或 prompt 的名字。

## 9. Agent Loop Harness 的闭环模型

一个可复用的 Agent loop harness，至少应覆盖以下循环：

```text
Plan -> Execute -> Observe -> Decide -> Persist -> Iterate
  ^                                                   |
  +---------------------------------------------------+
```

### 9.1 Plan：把目标改写为可验证行为

计划中不应只写“功能完成”或“界面正常”，而应明确：

- 前置状态是什么
- Agent 要执行什么动作
- 动作发生在哪个进程、窗口、页面或设备上
- 哪个可观察事实代表成功
- 哪些状态明确代表失败
- 允许等待多久，超时后保留什么证据
- 哪些场景属于本次 non-goals

对于多后端、多传输或多平台功能，应把这些维度显式展开为测试矩阵，而不是让 Agent 临场猜测覆盖范围。

### 9.2 Execute：提供单一、稳定、可重复的入口

构建、运行和调试应尽量通过稳定入口完成，例如：

- 固定构建脚本及配置选择规则
- 固定运行目录和参数来源
- 返回可追踪的进程 ID 或会话 ID
- 保留标准输出、标准错误和退出码
- 明确正常停止和强制清理方式

入口的目的不是隐藏所有底层工具，而是消除每一轮都重新推导环境、参数、工作目录和产物位置的随机性。

### 9.3 Observe：让 Agent 读取机器可判定的当前状态

观察面应优先提供结构化状态，例如：

- 控件树、DOM、对象树或业务状态快照
- 当前进程、连接、任务和设备状态
- 结构化错误、事件和状态转换
- 构建日志、运行日志、退出码和崩溃信息

截图适合补充布局、绘制和无法结构化的原生窗口，不应成为所有操作的唯一真源。结构化状态更容易检索、比较、断言和持久化，也不依赖屏幕是否解锁。

### 9.4 Decide：区分“已接受”和“已完成”

Agent 发出命令后，不能仅凭调用成功就认为功能完成。Harness 必须区分：

- 请求已发送
- 请求已接受或已排队
- 程序已开始处理
- 目标状态已经出现
- 程序仍然响应且没有进入新的错误状态

每个动作都应有对应的后置判据。异步动作应通过状态变化、事件、日志标记或完成回执收敛，而不是依赖任意时长的 `sleep`。

### 9.5 Persist：保存事实而不是只保存结论

一次运行至少应能留下：

- 使用的版本、配置、参数和测试场景
- 执行步骤及其时间或顺序
- 操作前后的关键状态
- 日志、退出码、快照和错误信息
- Agent 对“产品缺陷、harness 缺陷、环境问题”的分类
- 已验证和被否定的假设

TaskLog 适合保存推理和决策，日志及快照保存原始证据，KnowledgeBase 保存经过多次任务验证后仍然成立的长期知识。三者不应混为一份文件。

### 9.6 Iterate：以新证据调整下一轮

下一轮应从上一轮的持久化状态开始，而不是依赖聊天上下文回忆。典型分支是：

```text
目标状态出现
  -> 记录通过，进入下一个场景

出现明确产品错误
  -> 保存证据，形成或验证根因假设，修改产品代码

程序状态正确但验证器失败
  -> 修正 selector、同步判据或 harness，不改产品行为

状态不可观察或过程被阻塞
  -> 切换诊断通道，补充日志、调试器或外部窗口检查

超出范围或需要新的产品决策
  -> 保留现场，停止扩展实现并交回用户决定
```

## 10. 人与 Agent 的职责边界

### 10.1 人负责定义产品语义和授权边界

人最重要的输入不是详细告诉 Agent 每一行代码怎么改，而是定义：

- 用户真正要达成的行为目标
- 哪些可见或结构化状态代表功能正确
- 关键场景、边界条件和测试矩阵
- 哪些外部副作用被允许
- 哪些失败允许降级，哪些必须直接暴露
- 本次任务的范围、预算、验证等级和停止条件

如果这些内容没有写清楚，Agent 即使拥有完备工具，也只能用技术现象猜测产品意图。

### 10.2 Agent 负责执行、取证和局部收敛

Agent 适合承担：

- 从 Project、KnowledgeBase、TaskLog 和当前代码中恢复上下文
- 将目标拆成可执行步骤和可观察断言
- 构建、启动、操作、读取结果并保存证据
- 根据证据区分产品、harness 和环境问题
- 提出最小修改，验证后确认或否定假设
- 在不改变产品语义的前提下修正局部实现和验证方法

Agent 不应根据一次超时、一个非零退出码或一次 selector 失败就直接修改产品代码。首先要证明失败发生在哪一层。

## 11. GacUI 案例如何补齐闭环

GacUI 是一个较完整的 Agent loop harness 示例，因为它同时提供了以下能力。

### 11.1 统一执行入口

`copilotBuild.ps1`、`copilotExecute.ps1` 和各类启动脚本统一了构建环境、运行目录、参数、进程和日志位置。运行中的 `.unfinished` 文件与完成后的正式日志还提供了明确的生命周期信号。

### 11.2 应用内观察和操作协议

应用在启动时显式装配 AutomationService，通过本机 HTTP 或 MiniHTTP 暴露：

```text
GET  /Automation/<App>/Controls
GET  /Automation/<App>/Dom
POST /Automation/<App>/IO[/<WindowId>]
```

`Controls` 和 `Dom` 输出窗口、控件、可见文本、边界和渲染状态；`IO` 接受键盘、鼠标、文本和滚轮命令。Agent 因而能够按语义定位控件，再根据边界执行操作，而不是只依赖像素猜测。

### 11.3 显式完成判据

IO 返回 `Queued` 只表示命令已进入队列。操作 SOP 要求 Agent 重新读取当前 UI，并确认精确文本、元素消失、页面切换或其他目标状态。这个约束防止把“接口调用成功”误认为“用户目标完成”。

### 11.4 阻塞和崩溃的旁路诊断

应用内自动化依赖 UI 线程；原生模态窗口可能阻塞该线程。因此 GacUI 规定：停止无休止轮询，从另一个进程使用 Win32 枚举窗口、读取控件信息、发送直接消息，必要时再截图；崩溃和复杂状态则进入 CDB 或平台调试器。

这说明一个完整 harness 不只需要主路径，还需要在主观察通道失效时能够保留事实并切换到独立诊断通道。

### 11.5 帧快照和文件化记忆

GacUI 单元测试在 `OnNextIdleFrame` 边界保存结构化 UI 帧、渲染命令和差异。动作若没有触发预期 UI 更新，测试会失败，而不是无限等待。调查结果随后进入 TaskLog、测试矩阵和 Learning 文件。

这里的“学习”不是模型重新训练，而是外部文件记忆：下一轮 Agent 重新读取已验证事实，减少重复探索。

## 12. 可复用的 Harness 设计原则

### 12.1 语义优先，像素补充

能导出业务状态、对象树、控件树或 DOM 时，优先使用结构化信息。截图用于验证最终视觉结果、处理没有结构化接口的原生表面，或帮助解释歧义。

### 12.2 状态驱动等待，不使用无依据延时

等待必须对应可观察条件，并有上限。超时不是一个可被隐藏的正常分支，而是需要保存现场并分类的失败。

### 12.3 每个操作都要有前置状态和后置状态

先读取当前状态并重新定位目标，再执行操作；页面、窗口、菜单、连接或 Renderer 发生切换后，不复用旧句柄、旧坐标或旧 selector。

### 12.4 把 Harness 当成需要验证的代码

测试失败可能来自产品，也可能来自过短超时、错误 selector、不正确输入方式或终止阶段竞争。Harness 的判断规则应能被测试、记录和修正，且修正 harness 时不应顺带改变产品语义。

### 12.5 保存可重放证据

优先保存文本、JSON、命令序列、日志和退出码。只有聊天摘要而没有原始证据时，下一轮难以确认上一次判断是否可靠。

### 12.6 明确安全和生命周期边界

测试接口应显式启用，默认限制在本机或测试环境，拥有明确的启动、停止和清理顺序。若接口进入生产环境，需要单独设计鉴权、授权、审计和暴露范围，不能直接沿用测试 harness 的信任假设。

### 12.7 允许失败直接暴露，但不能无界等待

测试应用可以选择让违反内部不变量的错误直接崩溃，以便保留根因；Agent 仍需要检测进程退出、崩溃窗口、日志停滞和超时，并在确定的边界内结束本轮。

## 13. 按任务复杂度逐级建设

并非所有项目都需要一次性实现 GacUI 级别的 harness。更合理的做法是按任务类型逐级补齐。

### 13.1 Level 0：静态协作

适用于代码阅读、设计、局部重构和文档任务：

- AGENTS / instruction
- Project / KnowledgeBase
- TaskLog
- 静态检查

SGStudio 当前的轻量协作模型主要覆盖这一层。

### 13.2 Level 1：构建和 CLI 闭环

适用于编译、单元测试和命令行程序：

- 稳定构建入口
- 稳定运行目录和参数
- 标准输出、错误和退出码
- 运行中与已完成状态
- 超时、停止和调试入口

### 13.3 Level 2：GUI 或异步业务闭环

在 Level 1 基础上增加：

- 结构化状态读取
- 可定位的语义对象及其当前属性
- 确定性的输入或业务动作接口
- 每个动作的完成条件
- UI 帧、状态快照或事件轨迹

### 13.4 Level 3：多进程和故障闭环

适用于 Renderer、Helper、设备服务或远程进程：

- 明确进程拓扑和启动顺序
- 连接、接管、断连和退出契约
- 测试矩阵
- 独立于主通道的故障诊断路径
- 所有进程的状态、日志和清理方式

未来 SGStudio 若建设自动 GUI 或设备联调能力，应先选择真实需要覆盖的等级，再设计最小接口；不应为了“Agent 化”先增加一套宽泛的远程控制框架。

## 14. 建议的最小自定义 Prompt 方向

如果未来要在当前仓库正式加入 prompt，普通静态和实现任务仍建议从一条轻量入口开始，而不是一开始就做多阶段体系。运行时任务是否具备闭环，则由对应等级的 harness 解决，不应把执行协议和全部测试细节塞进 prompt。

这条 prompt 的目标不是“替代模型思考”，而是：

- 让用户在聊天中明确本次真源是哪篇 TaskLog
- 明确实现范围
- 明确验证等级
- 明确如果发生冲突时应先停下来报告

换句话说，它应当是一个“TaskLog 驱动的任务入口 prompt”，而不是一套完整开发操作系统。

### 14.1 这条 prompt 应避免的重复

它不应重复抄写以下内容：

- 不读 `moc_*.cpp`
- 只读被 CMake 纳入的文件
- 构建 / 运行 / 日志基本规范
- 设计文档与 TaskLog 的放置位置

这些都已经在 `copilot-instructions.md` 规则中常驻存在。

### 14.2 这条 prompt 应该补充的内容

它应只补 instruction 中没有“参数化”的部分，例如：

- 当前使用哪篇 TaskLog
- 当前只做哪几个 step
- 当前做 `static`、`debug-build` 还是 `debug-run`
- 当前是否有相对 TaskLog 的临时更新

## 15. 用户在聊天中应如何喂给 AI

对当前仓库，一个高质量的任务入口消息通常只需要四类信息：

1. TaskLog 路径
2. 本次范围
3. 验证等级
4. 相对 TaskLog 的临时更新

例如，以 [2026-04-09_eth_manual_connect_minimal_plan.md](../TaskLog/2026-04-09_eth_manual_connect_minimal_plan.md) 为例：

### 15.1 完整实现但只做静态验证

```text
# TaskLog
.github/TaskLog/2026-04-09_eth_manual_connect_minimal_plan.md

# Scope
按 Minimal Implementation Steps 完成整个最小版本，严格遵守 Non-Goals。

# Verify
static
```

### 15.2 只先打通主链路

```text
# TaskLog
.github/TaskLog/2026-04-09_eth_manual_connect_minimal_plan.md

# Scope
先只做 Minimal Implementation Steps 1 到 4，优先打通 DeviceManager / FancyDevice / MainWindowDeviceController / ETHConnectDialog 主链路。

# Verify
static
```

### 15.3 对原计划有局部修订

```text
# TaskLog
.github/TaskLog/2026-04-09_eth_manual_connect_minimal_plan.md

# Update
Device 菜单先保留 Connect 子菜单，不拆成顶层 USB Connect 和 ETH Connect；其余计划保持不变。

# Scope
只做受这个更新影响的实现。

# Verify
static
```

### 15.4 明确要求运行验证

```text
# TaskLog
.github/TaskLog/2026-04-09_eth_manual_connect_minimal_plan.md

# Scope
完成整个最小版本，并重点验证 ETH Connect 失败时弹窗内回写错误、以及 USB scanner 不会误移除当前 ETH 设备。

# Verify
debug-run
```

这些例子说明：

- 用户不需要重写整份设计
- 只需要声明“这次相对 TaskLog 的差异”
- 任务边界越清楚，AI 越不容易顺手扩展

## 16. 最终结论

对当前仓库，最合适的方向不是照搬 VlppParser2 的重型多阶段 prompt 体系，而是同时维护两个相互独立的层次。

协作控制层保持轻量：

- instruction 常驻仓库规则
- TaskLog 负责单次任务真源
- KnowledgeBase 提供稳定背景知识
- 轻量 prompt 只负责把真源、范围、验证等级和冲突策略参数化
- 默认分析流程继续负责代码理解、局部权衡与现场收敛

运行验证层按任务需要逐级建设：

- 稳定执行入口负责启动和停止
- 结构化观察面负责提供当前事实
- 确定性操作面负责改变系统状态
- 明确完成判据负责判断动作是否收敛
- 日志、快照和 TaskLog 负责保存证据并驱动下一轮

如果未来正式建设 `.github/prompts/`，应优先做：

- 一条 TaskLog 驱动的实现入口 prompt

而不是优先做：

- 一整套多阶段文档流水线

原因是：Prompt 负责约束 Agent，Harness 负责连接 Agent 与真实系统。当前 SGStudio 已经具备较好的静态协作基础；普通任务缺的是更清晰、更低重复的入口，而复杂运行时任务还需要按实际需求补齐可执行、可观察、可验证和可持久化的闭环。

这也是 GacUI 案例最值得复用的地方：不要求模型凭空变得更聪明，而是改造开发环境和被测系统，让每一轮操作都能产生机器可读的新证据，使 Agent 能够在有限、可审计的循环中持续收敛。
