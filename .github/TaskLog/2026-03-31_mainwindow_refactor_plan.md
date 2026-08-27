# 2026-03-31 MainWindow Refactor Plan

## Goal

- 把 `MainWindow` 从“实现中心”收缩成“装配中心”。
- 在不破坏现有设备打开链路、pipeline 切换链路、Windows 无边框/DPI 行为的前提下，逐步拆分职责。
- 同时优化 AI/人类阅读上下文：以后排查某个问题时，只需要打开对应职责文件，而不是反复把整个 `mainwindow.cpp` 塞进上下文。

## Current Problem

当前 `src/plugins/core/mainwindow.cpp` 已经同时承担以下职责：

1. 顶层 UI 组装与布局。
2. 菜单/Action 注册与行为绑定。
3. 设备连接、状态刷新、错误/警告弹窗。
4. `TxOrchestrator` + `TxPipelineRuntime` 编排与业务切换。
5. 配置保存/恢复、主题、语言、偏好设置、启动项。
6. Windows 无边框窗口、DPI、多屏白边处理。
7. Sequence Editor 外部进程生命周期。
8. aarch64 下隐藏鼠标相关输入逻辑。

从当前文件分布看：

- `mainwindow.cpp` 约 2200 行。
- `mainwindow.h` 持有约 40 个成员字段。
- 单个构造函数既做对象创建，又做布局、信号连接、状态同步和平台初始化。
- 多处关键状态通过 lambda 捕获分散在构造函数和 `registerDefaultActions()` 中，难以局部推理。

这已经不是“函数太长”的问题，而是职责边界失真：任何改动都需要同时理解 UI、设备、业务、平台四条链路。

## Refactor Principles

### 1. 保持 MainWindow 仍是 QMainWindow 壳层

- `MainWindow` 继续作为顶层窗口类型存在。
- 但它只保留三类职责：
  - 持有顶层窗口对象所有权。
  - 转发生命周期 override。
  - 装配各个 controller / helper。

### 2. 优先组合，不扩散继承

- 不把更多逻辑塞进 `TitleBar`、`CommonPanel`、`FancyTabWidget`。
- 不新增多层 `MainWindowBase` / `MainWindowPrivate` 继承体系。
- 使用 `QObject` 组合对象，把状态和逻辑按职责成块迁出。

### 3. 先拆高内聚叶子模块，再拆强耦合核心链路

- 第一批优先拆：Windows chrome、外部进程、主题/设置。
- 最后再拆：pipeline / business orchestration。
- 避免一开始就直接碰最核心的 `selectBusiness2Work()` + `updateOrchestrator()` 链。

### 4. 不做“工具箱式”拆分

- 不新增 `mainwindowutils.cpp`、`mainwindowhelpers.cpp` 这类杂物文件。
- 每个新文件必须回答清楚：它拥有哪段状态，负责哪条链路，不负责什么。

### 5. 维持增量可验证

- 每个阶段都应能独立构建并做最小行为回归。
- 每次迁移后都保持 `MainWindow` 对外行为不变。

## Recommended Target Structure

考虑当前 `src/plugins/core/` 是平铺文件布局，建议继续平铺，但统一使用 `mainwindow` 前缀，便于搜索、跳转和 AI 上下文收敛。

建议目标拆分如下：

### A. `mainwindow.cpp` / `mainwindow.h`

保留为装配层，目标控制在 250 到 400 行。

保留职责：

- 顶层 widget 创建与所有权持有。
- 各 controller 的构造与依赖注入。
- `nativeEvent` / `changeEvent` / `showEvent` / `closeEvent` 等 override 转发。
- 极少量纯窗口壳逻辑，例如 `instance()`。

不再直接承载：

- 设备状态处理细节。
- pipeline 组装与 apply 逻辑。
- 主题/配置/保存恢复细节。
- Sequence Editor 的 pid / start / kill 逻辑。

### B. `mainwindowchrome_win.cpp` / `mainwindowchrome_win.h`

负责 Windows 无边框窗口与 DPI 相关逻辑。

拥有职责：

- `windowDpi()` / `adjustWindowRectForDpi()` / `calcMaximizedContentsMargins()`。
- `WM_NCCALCSIZE` / `WM_NCHITTEST` / `WM_GETMINMAXINFO` / `WM_DPICHANGED`。
- 初始化时的 `WS_THICKFRAME` / `DwmExtendFrameIntoClientArea`。
- `changeEvent()` / `showEvent()` 的 Windows 专有补偿逻辑。

接口形态建议：

- `initialize()`
- `bool handleNativeEvent(void *message, long *result)`
- `void onWindowStateChanged()`
- `void onFirstShow()`

说明：

- `MainWindow` 仍然保留 override，但内部第一时间转发给 chrome helper。
- 这样可以在完全不改变消息派发入口的前提下，把 Win32 噪音从主文件中移走。

### C. `sequenceeditorcontroller.cpp` / `sequenceeditorcontroller.h`

负责 Sequence Editor 外部进程管理。

拥有职责：

- editor 可执行路径解析。
- Windows / Linux 下进程是否仍存活判断。
- `startDetached()`。
- 退出时的 kill 清理。

MainWindow 只保留：

- 菜单 action 触发到 `m_sequenceEditorController->launchOrRaise()`。
- 关闭时调用 `shutdown()`。

这是第一批非常适合抽离的模块，因为它几乎不参与 UI / 设备 / pipeline 链路。

### D. `mainwindowsettingscontroller.cpp` / `mainwindowsettingscontroller.h`

负责“应用级状态”和“用户配置”相关逻辑。

建议纳入：

- 主题读取、应用、刷新。
- 语言切换确认流程。
- 启动配置类型 `Default/User/Last`。
- 设置保存/恢复。
- `preset()` 的 UI/配置部分。
- 偏好对话框、亮度设置、锁屏、自动亮度的桥接。
- `closeEvent()` 中与配置持久化相关的部分。

建议暂不纳入：

- `closeEvent()` 里与 runtime/business 关闭有关的逻辑，那个更接近 pipeline/session shutdown。

原因：

- 这部分虽然函数多，但基本都围绕“用户设置”和“会话持久化”这一条主线，内聚度其实高。

### E. `mainwindowdevicecontroller.cpp` / `mainwindowdevicecontroller.h`

负责设备状态到 UI 的同步。

建议纳入：

- `currentDeviceChanged` 处理。
- `currentDeviceOpenStateChanged` 处理。
- `deviceRealTimeStatusUpdated` 处理。
- `systemMessage` 到 `MessageDialog` 的路由。
- `DeviceInfoWidget` 的 warning / error 去重状态。
- connect menu 的动态生成与触发。

这个 controller 应拥有自己的错误状态缓存：

- `m_deviceOpenErrorDialog`
- `m_errorMsgActive`
- `m_lastErrorCode`
- `m_lastWarningCode`

这样可以把一整块“设备 UI 状态机”从主窗口字段中搬走。

### F. `mainwindowpipelinecontroller.cpp` / `mainwindowpipelinecontroller.h`

负责当前最核心也最敏感的业务编排链路。

建议纳入：

- `selectBusiness2Work()`。
- `updateSweepAndBusinessAvailability()`。
- `showSweepPage()` 中与 pipeline 状态相关的部分。
- `onCurrentBusinessChanged()`。
- `buildApplyRequest()`。
- `legacyActivationTargetFor()`。
- `applyResolvedPipeline()`。
- `updateOrchestrator()`。
- `onInitializationDone()` 里和 sweep/runtime/orchestrator 相关的 wiring。

建议原则：

- 这个 controller 只处理“从 UI 状态到业务执行状态”的映射。
- 它不负责主题、不负责 MessageDialog、不负责设备发现菜单。
- 它应成为未来排查 Tx 流程问题时的首要上下文文件。

## Why This Split Is Good For AI Context

这是本轮方案必须单独强调的点。

当前 AI 在处理 `MainWindow` 相关问题时，常见低效模式是：

- 为了回答一个 Windows 白边问题，不得不同时加载设备/pipeline/主题代码。
- 为了回答一个 `selectBusiness2Work()` 问题，不得不把 2000 多行窗口代码一起带上。
- 大量 lambda、平台宏和 unrelated 状态字段让检索命中率下降。

拆分后的上下文收益：

1. 问 Windows 多屏/DPI，只看 `mainwindowchrome_win.*`。
2. 问设备状态弹窗和连接菜单，只看 `mainwindowdevicecontroller.*`。
3. 问 RF/Mod/Sweep 与 runtime 切换，只看 `mainwindowpipelinecontroller.*` + `txorchestrator.*` + `txpipelineruntime.*`。
4. 问主题/配置/启动项，只看 `mainwindowsettingscontroller.*`。
5. 问外部编辑器，只看 `sequenceeditorcontroller.*`。

这会显著降低：

- 搜索噪音。
- 上下文 token 消耗。
- 对大段无关 lambda 的重复解析。

## AI-Friendly Code Organization Rules

为了让这次重构不仅“拆文件”，而且“拆得适合后续 AI 协作”，建议执行以下规则：

### 1. 新文件命名必须可按职责直搜

- 使用 `mainwindowdevicecontroller.cpp` 这类强语义命名。
- 不使用 `controller1.cpp`、`mainwindow_part2.cpp`、`misc.cpp`。

### 2. 每个文件开头写清楚职责和非职责

示例风格：

- 负责：设备状态到 UI 的同步、错误弹窗去重。
- 不负责：业务 active 决策、主题样式应用。

这样 AI 和人类都能在前 30 行快速判断文件是否相关。

### 3. 把长 lambda 提升为命名私有方法

- 构造函数里只保留连接关系，不保留大段业务实现。
- 凡是超过 8 到 10 行的 lambda，优先挪成私有方法。

### 4. 状态跟着职责走，不留在 MainWindow 挂名持有

- 例如错误去重状态应进 `MainWindowDeviceController`。
- editor pid 应进 `SequenceEditorController`。
- 不要让 `MainWindow` 继续变成“所有状态都先放这里”的中转站。

### 5. Header 只暴露窄接口

- controller header 只暴露少量动作和 signal/slot。
- 复杂实现细节尽量留在 cpp，减少跨文件展开阅读成本。

### 6. 避免拆成过多微型类

- 当前合适的粒度是 4 到 6 个协作对象。
- 不要把每个 action 都拆一个类；那会让上下文从“大文件噪音”变成“文件碎片噪音”。

## Recommended Migration Order

## Implementation Status (2026-03-31)

### Completed (Phases A-E)

| File | Lines | Responsibility |
|------|-------|----------------|
| `mainwindow.cpp` | 929 | Assembly shell (was 2210) |
| `mainwindow.h` | 134 | Slimmed header (was 198) |
| `mainwindowchrome_win.cpp` | 243 | Win32 frameless chrome + DPI |
| `mainwindowchrome_win.h` | 33 | Chrome declaration |
| `mainwindowdevicecontroller.cpp` | 217 | Device status→UI sync |
| `mainwindowdevicecontroller.h` | 46 | Device controller declaration |
| `mainwindowsettingscontroller.cpp` | 501 | Settings/theme/language/startup |
| `mainwindowsettingscontroller.h` | 62 | Settings controller declaration |
| `sequenceeditorcontroller.cpp` | 152 | External editor process lifecycle |
| `sequenceeditorcontroller.h` | 33 | Editor controller declaration |

**Total**: ~2350 lines across 10 files (vs original ~2408 in 2 files). Net reduction: ~60 lines + much better separation.

### Deferred (Phase F)
- `MainWindowPipelineController` — pipeline orchestration code remains in `mainwindow.cpp` (~400 lines of the 929).
- To be extracted when pipeline business logic is stable enough for a dedicated controller.

建议按以下顺序做增量重构。

### Phase 1. 抽离 `SequenceEditorController`

原因：

- 低耦合。
- 易验证。
- 可以先减少 `registerDefaultActions()` 和 `closeEvent()` 中的非主线逻辑。

完成标准：

- `m_pid`、`m_editorProcess`、`checkProcessStatus()`、`killEditorProcessWin32()`、`killEditorProcessLinux()` 离开 `MainWindow`。

### Phase 2. 抽离 `MainWindowChromeWin`

原因：

- Windows 消息处理噪音大，但边界非常清晰。
- 与设备/pipeline 基本正交。

完成标准：

- `nativeEvent()` 内大部分分支迁移到 helper。
- 构造函数里的 Win32 初始化代码迁移。
- `showEvent()` / `changeEvent()` 中 Windows 逻辑迁移。

### Phase 3. 抽离 `MainWindowSettingsController`

原因：

- 配置/主题/语言/preset 目前占据大量菜单与对话框代码。
- 但它们与 runtime 设备链路的直接耦合相对可控。

完成标准：

- `saveSettings()`、`restoreSettings()`、`saveSettingsFile()`、`loadSettingsFile()`、`languageChanged()`、主题相关、启动项相关从 `MainWindow` 迁移。

### Phase 4. 抽离 `MainWindowDeviceController`

原因：

- 设备状态链路有独立状态缓存，非常适合整体迁走。
- 迁出后 `MainWindow` 会明显变干净。

完成标准：

- 设备 signal wiring 与对应状态字段迁移。
- connect 菜单逻辑迁移。

### Phase 5. 抽离 `MainWindowPipelineController`

原因：

- 这是当前最重要的主链路，必须最后做。
- 前四步完成后，主文件中剩下的核心逻辑会更显性，也更容易稳定迁出。

完成标准：

- `selectBusiness2Work()` 到 `updateOrchestrator()` 这一整条链路迁移。
- `MainWindow` 不再直接读写 `m_orchestrator` / `m_pipelineRuntime` 的细节行为。

### Phase 6. 收尾压缩 `registerDefaultActions()`

原因：

- 当前该函数既注册菜单又实现动作逻辑。
- 在其他 controller 就位后，action 逻辑大部分会变成一行转发，自然缩短。

目标：

- `registerDefaultActions()` 只保留 action 创建、注册、连接到 controller 方法。

## Boundaries To Keep Explicit

以下边界必须在重构中明确坚持：

### 1. `MainWindowDeviceController` 不决策 business

- 它只反映设备状态，不决定 RF/Mod/Sweep 该如何切换。

### 2. `MainWindowPipelineController` 不直接处理 MessageDialog 样式问题

- 它只关注执行语义，不做通用 UI 提示路由。

### 3. `MainWindowSettingsController` 不处理 Win32 事件

- 平台窗口行为必须和应用设置行为分离。

### 4. `MainWindow` 不再长期保存 domain-specific 临时状态

- 除非该状态是多个 controller 的共享根状态，否则应迁入所属 controller。

## What Should Not Be Done

以下方案不建议采用：

1. 直接把 `mainwindow.cpp` 拆成多个 `.cpp`，但仍共享一个巨型 `MainWindow` 头文件和全部成员。
2. 把所有 helper 都做成匿名 namespace 下的 free function。
3. 为了“少改头文件”而继续让所有 controller 直接操纵所有 `MainWindow` 私有字段。
4. 先重写 business 选择规则，再顺手做文件拆分。
5. 一次性把所有职责迁完再验证。

这些做法要么没有真正重建边界，要么风险过高。

## Proposed End-State Criteria

当以下条件满足时，可以认为 MainWindow 重构达标：

1. `mainwindow.cpp` 不再超过 400 行量级。
2. `mainwindow.h` 的私有字段数量明显下降，尤其是 domain-specific 状态字段下降。
3. Windows 窗口处理、设备 UI、pipeline 编排、设置持久化可以分别单独阅读。
4. 修改某一类行为时，不再需要同时阅读大段无关逻辑。
5. 针对 AI 提问时，通常只需附加 1 到 3 个文件即可完成定位。

## Verification Plan

每个 phase 后都建议做最小回归，而不是等全部结束再统一回归。

### Static

- 构建通过。
- 无新增 include 循环。
- controller 间依赖方向单向清晰。

### Runtime

- 设备切换、打开失败、warning 展示正常。
- RF/Mod/Sweep 切换与 `TxPipelineRuntime` 行为不变。
- 设置保存/恢复、主题和语言切换行为不变。
- Sequence Editor 启动/退出清理正常。
- Windows 下多屏/高 DPI 无边框行为不回退。

## Recommended First Implementation Slice

如果下一步要真正开始动代码，我建议从下面这一刀开始：

1. 新增 `sequenceeditorcontroller.*`。
2. 把 editor 相关 pid / process / start / kill 全迁走。
3. 让 `registerDefaultActions()` 中 editor action 只调用一个命名方法。
4. 让 `closeEvent()` 只调用 `shutdown()`。

理由：

- 这一步收益直接。
- 风险低。
- 能先建立“MainWindow 负责装配，controller 负责状态和行为”的迁移模式。
