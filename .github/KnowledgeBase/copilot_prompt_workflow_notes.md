# Copilot 自定义 Prompt 轻量工作流与默认分析流程说明

## 1. 目的

本文档记录当前仓库中关于“是否引入自定义 prompt，以及它应如何与 Copilot 默认分析流程协作”的结论，供后续正式建设 `.github/prompts/` 或补充协作规范时参考。

本文不是某一条 prompt 的最终定稿，而是：

- 总结哪些规则值得固化到 prompt / instruction
- 总结哪些判断应继续留给模型默认分析流程现场决策
- 解释为什么当前仓库不适合直接照搬更重的多阶段文档流水线
- 给后续实现轻量 prompt 时提供边界与取舍依据

## 2. 讨论背景

本次讨论参考了 VlppParser2 的 Copilot 定制体系：

- [VlppParser2 copilot-instructions.md](https://github.com/vczh-libraries/VlppParser2/blob/master/.github/copilot-instructions.md)
- [VlppParser2 0-scrum.prompt.md](https://github.com/vczh-libraries/VlppParser2/blob/master/.github/prompts/0-scrum.prompt.md)
- [VlppParser2 investigate.prompt.md](https://github.com/vczh-libraries/VlppParser2/blob/master/.github/prompts/investigate.prompt.md)
- [VlppParser2 .github/prompts](https://github.com/vczh-libraries/VlppParser2/tree/master/.github/prompts)
- [VlppParser2 .github/Agent](https://github.com/vczh-libraries/VlppParser2/tree/master/.github/Agent)

该仓库的特点是：

- 用多阶段 prompt 把需求拆成 scrum、design、planning、execution、verifying 等阶段
- 用多份 `Copilot_*.md` 文档把每个阶段的输出显式化
- 对构建、测试、知识库利用、阶段切换和更新机制给出非常严格的流程约束

这套做法很完整，但也明显偏重。对当前 SGStudio 仓库而言，直接照搬会引入较高的维护成本与流程负担。

## 3. 当前仓库已有的“常驻规则”

当前仓库已经通过 [copilot-instructions.md](copilot-instructions.md) 提供了大量仓库级常驻规则，例如：

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

- 比较完整的 [copilot-instructions.md](copilot-instructions.md)
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

## 8. 建议的最小自定义 Prompt 方向

如果未来要在当前仓库正式加入 prompt，建议从一条轻量实现入口开始，而不是一开始就做多阶段体系。

这条 prompt 的目标不是“替代模型思考”，而是：

- 让用户在聊天中明确本次真源是哪篇 TaskLog
- 明确实现范围
- 明确验证等级
- 明确如果发生冲突时应先停下来报告

换句话说，它应当是一个“TaskLog 驱动的任务入口 prompt”，而不是一套完整开发操作系统。

### 8.1 这条 prompt 应避免的重复

它不应重复抄写以下内容：

- 不读 `moc_*.cpp`
- 只读被 CMake 纳入的文件
- 构建 / 运行 / 日志基本规范
- 设计文档与 TaskLog 的放置位置

这些都已经在 [copilot-instructions.md](copilot-instructions.md) 常驻存在。

### 8.2 这条 prompt 应该补充的内容

它应只补 instruction 中没有“参数化”的部分，例如：

- 当前使用哪篇 TaskLog
- 当前只做哪几个 step
- 当前做 `static`、`debug-build` 还是 `debug-run`
- 当前是否有相对 TaskLog 的临时更新

## 9. 用户在聊天中应如何喂给 AI

对当前仓库，一个高质量的任务入口消息通常只需要四类信息：

1. TaskLog 路径
2. 本次范围
3. 验证等级
4. 相对 TaskLog 的临时更新

例如，以 [2026-04-09_eth_manual_connect_minimal_plan.md](../TaskLog/2026-04-09_eth_manual_connect_minimal_plan.md) 为例：

### 9.1 完整实现但只做静态验证

```text
# TaskLog
.github/TaskLog/2026-04-09_eth_manual_connect_minimal_plan.md

# Scope
按 Minimal Implementation Steps 完成整个最小版本，严格遵守 Non-Goals。

# Verify
static
```

### 9.2 只先打通主链路

```text
# TaskLog
.github/TaskLog/2026-04-09_eth_manual_connect_minimal_plan.md

# Scope
先只做 Minimal Implementation Steps 1 到 4，优先打通 DeviceManager / FancyDevice / MainWindowDeviceController / ETHConnectDialog 主链路。

# Verify
static
```

### 9.3 对原计划有局部修订

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

### 9.4 明确要求运行验证

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

## 10. 最终结论

对当前仓库，最合适的方向不是照搬 VlppParser2 的重型多阶段 prompt 体系，而是保留一条更轻的协作原则：

- instruction 常驻仓库规则
- TaskLog 负责单次任务真源
- KnowledgeBase 提供稳定背景知识
- 轻量 prompt 只负责把真源、范围、验证等级和冲突策略参数化
- 默认分析流程继续负责代码理解、局部权衡与现场收敛

如果未来正式建设 `.github/prompts/`，应优先做：

- 一条 TaskLog 驱动的实现入口 prompt

而不是优先做：

- 一整套多阶段文档流水线

原因很简单：当前仓库已经有足够多的结构化材料，真正缺的不是更重的流程，而是一个更清晰、更低重复的任务入口。