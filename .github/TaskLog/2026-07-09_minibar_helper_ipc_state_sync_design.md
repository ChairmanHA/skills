# Minibar Helper IPC State Sync

本文是双进程 minibar 第一阶段的实施记录。历史讨论已经收敛为当前决策、实际
行为、关键实现边界和下一阶段准备，不再保留已被实现替代的候选方案。

## 范围与状态

当前已经完成：

1. 独立 `SGStudioMiniBar` helper 与主进程之间的持久化本机 IPC。
2. MainWindow 与 helper minibar 的纯显隐切换；正常切换不销毁窗口或进程。
3. main 向 helper 推送 RF、Center、Level、只读 MOD、Sweep authoring 和当前
   设备状态。
4. helper 通过请求修改 RF、Center、Level，最终显示值由 main snapshot 回写。
5. minibar 状态下，设备断开后延迟隐藏 helper，设备重连后重新显示。
6. Win32 普通 Qt 窗口路径和 Raspberry Pi Wayland layer-shell 路径。
7. Wayland Center/Level 数字键盘 overlay、定位、焦点和收起保护。
8. helper CW `FScan/LScan` Sweep panel、revisioned high-level request，以及
   main `StepSweepPanel -> TxSessionService -> SweepCw` 闭环的静态实现。

明确未完成：

1. 冷启动时 main/helper 同时隐藏，首次插入设备后自动显示 helper。
2. 设备列表、完整能力、错误、busy、unlevel 和 pipeline snapshot。
3. 可写 MOD/provider、MScan，以及 Sweep runtime completion/error UI。
4. IPC feature handshake 和用户可见的请求错误反馈。helper 已有本地请求
   超时保护，但当前只通过 error signal/log 报告。

验证记录：

1. `SGStudioMiniBar` 与 Core Debug 构建曾在 Win32/MSVC 通过。
2. main 修改 RF、Center、Level 后向 helper 同步已经过用户测试。
3. 当前阶段在 Raspberry Pi Wayland 上由用户报告测试正常。
4. 2026-07-10 已完成协议归位和 `MinibarClient` 提取，并通过定向及完整 Debug
   构建；尚未重复执行 Win32/树莓派交互测试。
5. 2026-07-10 SweepCw 本阶段按用户要求只完成静态检查；未编译、未运行，
   未把此前 RF/Center/Level 的构建/现场结论外推为 Sweep 已验证。

## 核心决策

1. main 是唯一设备、property、business、runtime 和 apply owner。
2. helper 是纯 UI client，不加载 Core plugin，不访问 `DeviceManager`、
   `TxSessionService` 或设备 API。
3. helper 可以链接 `Business`、`Controls`、`Utils`，但用途仅限共享控件、
   数字键盘、单位适配和显示工具；这些库不能把 main 的运行时对象带入 helper。
4. 状态同步使用 main 主动推送的完整 snapshot，不使用固定频率轮询。
5. helper 写操作只发送用户意图；`RSP ok=true` 表示 main 已接受请求，最终
   UI 仍以之后的 `STAT:SNAP` 为准。
6. 正常 main/minibar 切换只做 `hide()/show()`。只有 SGStudio 明确退出或
   helper 异常终止时才销毁进程和窗口。
7. main 与 helper 不同时作为可操作 UI 显示；复杂操作回到 MainWindow 完成。
8. 设备来源不属于 helper 关注范围。USB、ETH 或未来其它来源，只要 main
   判定当前设备已打开，就使用相同 snapshot 和可见性逻辑。

## 当前组件

```text
Main process
  MainWindow
    owns MinibarHelperController
    owns RemoteMinibarService

  MinibarHelperController
    QLocalServer / QProcess / authenticated QLocalSocket
    helper lifecycle and show/hide state machine
    main-window hide/restore and failure recovery

  RemoteMinibarService
    observes DeviceManager, RF/Center/Level/MOD and StepSweepPanel
    builds and pushes authoritative snapshots
    validates and executes typed helper requests
    bridges complete Sweep authoring state back to the main StepSweepPanel
    owns disconnect/reconnect visibility policy

Helper process
  MinibarClient
    QLocalSocket, shared framing, request ids and EVT/RSP dispatch
    one pending request, bounded timeout and error signal

  RemoteMiniBarWindow
    snapshot rendering and user intents
    collapsed/expanded state, drag, keyboard, Sweep overlay and layer-shell behavior

  RemoteSweepPanel
    pure child view; no Core/property/runtime dependency
    renders SweepSnapshot and emits full SweepChangeRequest
```

职责边界目前是正确的：lifecycle 没有混入设备写操作，helper 也没有获得 main
进程对象。下一阶段应保持这个边界。

## 协议现状

传输与 framing：

```text
QLocalServer / QLocalSocket
COMMAND [compact-json]\n
maximum line length: 64 KiB
one authenticated helper connection per main process
```

生命周期消息：

```text
AUTH <token>
READY
SHOW_MINIBAR <top-right-x>,<top-right-y>
VISIBLE
RESTORE_REQUEST
HIDE_MINIBAR
HIDDEN
SHUTDOWN
```

结构化消息：

```text
EVT {"v":1,"seq":N,"event":"STAT:SNAP","payload":{...}}
REQ {"v":1,"id":K,"cmd":"...","args":{...}}
RSP {"v":1,"id":K,"ok":true,"status":"accepted"}
RSP {"v":1,"id":K,"ok":false,"error":{"code":"...","message":"..."}}
```

当前可写命令：

```text
OUTP:STAT        args.enabled : bool
SOUR:FREQ:CENT  args.hz      : finite double
SOUR:POW:LEV    args.dbm     : finite double
SOUR:SWEEP:CONF args         : full Sweep authoring state + expectedRevision
STAT:SNAP?      args         : {}
```

Restore 仍使用独立生命周期消息 `RESTORE_REQUEST`，当前没有
`UI:MAIN:SHOW` 请求命令。

## Snapshot

当前实际 payload：

```json
{
  "seq": 42,
  "ui": {
    "mainVisible": false,
    "minibarVisible": true
  },
  "device": {
    "present": true,
    "open": true,
    "interface": "USB",
    "connection": "...",
    "model": "...",
    "uid": "..."
  },
  "tx": {
    "rf": true,
    "rfText": "ON",
    "mod": false,
    "modText": "OFF",
    "centerHz": 1000000000,
    "centerText": "1.000 GHz",
    "centerUnit": "GHz",
    "levelDbm": -20,
    "levelText": "-20.000 dBm",
    "levelUnit": "dBm"
  },
  "sweep": {
    "available": true,
    "revision": 8,
    "enabled": true,
    "selectedPlanId": "fscan",
    "supportedPlanIds": ["fscan", "lscan"],
    "frequencyPlan": {
      "startHz": 1000000000,
      "stopHz": 2000000000,
      "stepHz": 10000000
    },
    "levelPlan": {
      "startDbm": -20,
      "stopDbm": -19,
      "stepDb": 1
    },
    "dwellSeconds": 0.001
  }
}
```

若 main 当前 configured plan 不是 helper 支持的 Freq/Power，snapshot 使用只读
`selectedPlanId="unsupported"` 且 `available=false`；不会把 List/MScan 伪装成
FScan，也不会在 `supportedPlanIds` 中广告它。

发布规则：

1. helper `READY` 后立即发布一次。
2. helper 可见性变化后立即发布一次。
3. RF、Center、Level、MOD、profile、provider availability 和 Sweep authoring
   变化经过 50 ms single-shot debounce。
4. 设备打开/关闭和 helper 请求执行后立即发布。
5. snapshot sequence 在 main 进程生命周期内单调增加；helper 丢弃旧 sequence。
6. helper 同步隐藏期间仍保持 IPC 连接，后续 snapshot 可以继续到达。
7. Sweep revision 只在完整 authoring state 语义变化时递增；相同 writeback 不
   制造新 revision。snapshot `seq` 仍负责整个 payload 的先后顺序。

不需要 heartbeat。只有真实状态变化才推送，既降低 Raspberry Pi 空闲开销，也
能暴露缺失的状态订阅，而不是用轮询掩盖问题。

## 写回语义

```text
User edits helper RF/Center/Level
  -> helper sends REQ id=K
  -> main validates version, device-open state and argument type
  -> main writes the existing RF/Center/Level property path
  -> main sends RSP id=K
  -> main publishes authoritative STAT:SNAP
  -> helper clears pending state and renders the snapshot

User edits helper Sweep
  -> helper sends one full SweepChangeRequest + expectedRevision
  -> main validates revision, device/MOD/provider/plan and selected step/dwell
  -> RemoteMinibarService maps the DTO to the existing StepSweepPanel
  -> StepSweepPanel reuses preview/normalize and enabled/args signals
  -> MainWindow -> TxSessionService -> SweepCw applies through the existing chain
  -> preview/runtime writeback changes the same authoring owner when needed
  -> main sends RSP and publishes the authoritative Sweep snapshot
```

当前 helper UI 只允许一个本地 pending request。它会暂时禁用 RF、Center、
Level、Sweep 入口和 Sweep 写控件，避免重复点击；`MinibarClient` 在 3 秒内没有
收到匹配 RSP 时解除 pending 并发出 `TIMEOUT` error signal。Sweep 的“原子”只
表示一次请求携带完整 authoring state，并由 main 适配到同一个 owner；现有
preview 仍可能规范化 `CommonDeviceProfile` 并走既有 refresh。main 端尚未实现
统一 busy gate 或异步 runtime completion，因此当前 `accepted` 不能解释为“设备
已经完成配置”。

## 设备显隐

当前支持的是“已经进入 minibar 模式后的断开/重连”：

```text
device closes
  -> main pushes device.open=false snapshot
  -> start 2000 ms reevaluation timer
  -> if no current open device: HIDE_MINIBAR
  -> helper hides; main remains hidden
  -> remember waiting-for-reopen

device reopens
  -> main pushes fresh snapshot
  -> if main is still hidden for minibar: SHOW_MINIBAR
  -> helper shows; main remains hidden
```

用户点击 Restore 后，main 恢复可见并清除 waiting-for-reopen。之后的设备插拔
只更新状态，不会抢回 helper UI。

冷启动隐藏策略不是当前代码的一部分。helper 目前由用户从 MainWindow 发起
切换时按需启动，`m_waitingForDeviceReopen` 只在 minibar 可见后发生设备断开
时置位。若产品下一步仍需要“启动后双隐藏，插设备显示 helper”，必须新增明确
startup visibility policy，不能把它描述成现状。

## 平台实现要点

Win32：

1. helper 使用 frameless/tool/stays-on-top QWidget。
2. 数字键盘属于 helper-owned transient；键盘激活、关闭或 native
   `windowHandle()` 事件不会触发 minibar 误收起。
3. main 只操作自己的窗口，不依赖跨进程 HWND 控制。

Raspberry Pi Wayland：

1. 只有 helper 进程调用 `LayerShellQt::Shell::useLayerShell()`。
2. base surface 使用 `LayerOverlay`、`AnchorTop | AnchorRight`、
   `exclusiveZone=0`、`KeyboardInteractivityNone`。
3. 横向拖动通过 top/right margins 实现，不依赖 `QWidget::move()`。
4. base 和键盘 overlay 发布
   `sgstudioLayerShellVisualTopLeft/VisualSize`，作为 compositor visual
   geometry 的事实源。
5. Center/Level 键盘放在 fullscreen transparent managed overlay host 中；
   helper-owned keyboard/overlay 受到 `ApplicationDeactivate` guard 保护。
6. Sweep 使用另一个 fullscreen transparent overlay host；只有该 host 创建并配置
   layer-shell surface，`RemoteSweepPanel` 与其 `QDialog/Qt::Widget` 容器都是 child。
7. `OverlayContainer` 消费 panel 外部点击；panel/keyboard session 在完整关闭前都
   属于 owned interaction，避免 `ApplicationDeactivate` 折叠 minibar。

## 重构评估

2026-07-10 已完成的基础收紧：

1. 已完成：`minibarhelperprotocol.*` 已迁入中立的 `MinibarIpc` static
   target；Core 不再反向编译 app 源文件。协议版本、命令、字段、JSON codec、
   envelope builder 和 64 KiB line buffer 已集中。
2. 已完成：helper socket、request id、pending correlation、EVT/RSP dispatch
   和 3 秒超时已提取为 `MinibarClient`；`main.cpp` 只负责应用初始化和对象
   wiring。
3. 已完成：MOD 只读显示且保持禁用；Sweep 按钮、panel 和 numeric editor 都由
   typed snapshot 渲染，写操作只发送带 revision 的完整 request，本地点击不会
   留下假 checked/value 状态。

后续功能增长时再做，而不是现在单独大改：

1. 当 snapshot 继续增加设备列表、capabilities、status 和 runtime feedback 时，
   再从
   `RemoteMinibarService` 提取 `SnapshotBuilder` 和窄
   `CommandAdapter`；当前 command surface 尚不足以支撑一次独立大重构。
2. Sweep 业务 UI 已放入独立 `RemoteSweepPanel`；`RemoteMiniBarWindow` 只保留
   overlay、keyboard、snapshot/pending 协调等窗口职责。

暂不建议立即重构：

1. 已通过树莓派验证的 layer-shell geometry、drag、keyboard overlay 和焦点
   guard。移动这些平台敏感代码的回归风险高于当前收益。
2. `MinibarHelperController` 的显隐状态机。状态虽多，但职责明确，故障恢复
   路径也依赖这些中间态。
3. 旧 `MiniBarWindow` 与 helper 的完整 UI 共用抽取。legacy in-process minibar
   已退役，只保留为视觉/平台处理参考，不再为两套 UI 等价改动旧窗口代码。

## 2026-07-10 协议与 Helper Client 重构

状态：完成。

范围：

1. 将 `minibarhelperprotocol.*` 迁入 `src/libs/minibaripc`，建立
   `MinibarIpc` static target。
2. 共享模块集中 lifecycle framing、64 KiB line buffer、协议版本、command /
   event / JSON key、compact JSON codec 和 request/response envelope 构造。
3. Core 通过链接 `MinibarIpc` 使用协议，不再反向编译
   `src/app/minibarhelper` 源文件。
4. helper 新增 `MinibarClient`，接管 `QLocalSocket`、read buffer、request
   id、单 pending correlation、EVT/RSP dispatch 和有界请求超时。
5. `RemoteMiniBarWindow` 仍只渲染 snapshot、维护窗口行为并发出 typed
   intent；显隐、键盘和 layer-shell 路径不改。

行为保持：

1. 线协议仍是 `COMMAND [compact-json]\n`，现有 lifecycle word 和 JSON
   schema 不变。
2. AUTH/READY、SHOW/VISIBLE、HIDE/HIDDEN、RESTORE_REQUEST、SHUTDOWN 的
   时序不变。
3. RF、Center、Level REQ/RSP 与最终 `STAT:SNAP` 显示语义不变。
4. 请求超时只解除 helper pending UI 并发出错误 signal，不修改 main/runtime
   状态。

成功标准：

1. `rg` 不再发现 Core CMake 引用 `src/app/minibarhelper` 协议源文件。
2. main 和 helper 使用同一个 protocol version、command/event/key 常量和 JSON
   codec。
3. main/helper 两侧都使用共享 line buffer 执行 64 KiB framing 检查。
4. helper `main.cpp` 不再直接持有 `QLocalSocket`、read buffer 或 request id。
5. `MinibarIpc`、`Core`、`SGStudioMiniBar` 在现有 Debug build tree 编译
   通过；由于修改共享库和 CMake，最终执行完整 Debug 构建。

验证结果：

1. 上述五项静态条件全部满足。
2. `MinibarIpc`、`Core`、`SGStudioMiniBar` 定向 Debug 构建通过。
3. 现有 Ninja Debug build tree 的完整构建通过，主程序、helper 和全部插件
   成功链接。
4. 构建输出只有仓库既有的 C4819 编码、STL extension deprecation 和
   QuickWaveform AutoMoc warning，没有本次重构新增错误。

## 下一阶段准备

SweepCw 的 stable ID、revision、owner、legacy 退役和 accepted/snapshot 语义已经
在本阶段确定。后续仍需明确：

1. snapshot v1 后续 section：`devices`、`capabilities`、`status`、busy/unlevel
   和 pipeline/runtime feedback，以及每个字段的 owner 和触发信号。
2. `MScan`、可写 MOD/provider 是否进入 helper；进入前必须定义稳定 DTO 和 main
   owner，不能把 provider business panel 或 property 名跨 IPC 暴露。
3. 命令响应语义：validated、accepted、applied、failed 中哪些需要显式状态，
   哪些只由最终 snapshot 表达。
4. 冷启动双隐藏是否仍是产品需求；如果是，先实现 startup visibility policy
   和无 UI 时的失败恢复策略。

推荐实施顺序：

```text
devices/capability/status schema + typed codec tests
  -> helper read-only rendering
  -> runtime completion/error feedback
  -> separately designed MScan or writable MOD/provider requests
```

## 2026-07-10 Helper SweepCw 闭环

状态：静态实施完成；按本任务要求未编译、未运行。

### 范围与决策

1. 本阶段只开放 CW 的 `FScan / LScan`；`MScan` 继续遵守当前主界面隐藏边界，
   不通过 helper 协议广告。
2. main 现有 `StepSweepPanel -> TxSessionService -> TxPipelineRuntime::applySweepCw()`
   仍是唯一 authoring、preview/normalize、apply 和 writeback 链。helper 不链接
   Core，不复制 `StepSweepPanel`、property binding 或 provider business panel。
3. IPC 增加中立的完整 Sweep authoring DTO：enabled、configured plan、Freq/Power
   两组保留参数、共享 dwell、availability 和 revision。disabled 时也必须保留
   configured plan，不能从 applied `Fixed` 反推。
4. helper 的所有 Sweep 写操作发送一个带 `expectedRevision` 的原子 high-level
   carrier-plan request；这里的“原子”指一次请求携带完整 authoring state，并
   提交给同一个 main owner。之后复用现有 preview/normalize、session refresh 和
   runtime writeback；preview 若规范化 common settings，仍按既有
   `CommonDeviceProfile` 路径刷新。RSP 只表示 accepted，最终 UI 只由后续 snapshot
   更新。
5. `SweepCw` 只在 `MOD=OFF` 时可用。helper 同步只读 MOD 状态并禁用 MOD 占位；
   MOD 为 ON 时 Sweep snapshot 标记 unavailable，main 也拒绝请求，不由 Sweep
   请求暗中关闭 MOD 或切换 provider。
6. legacy in-process `MiniBarWindow` 已过期，只作为视觉、交互和已验证 layer-shell
   处理的参考；本次不要求两套 UI 代码等价，也不向 legacy 路径增加新功能。

### Helper UI / Window 设计

1. 新建独立的纯展示 `RemoteSweepPanel` child view；数据只来自 typed snapshot，
   numeric keyboard 的临时输入只用于构造 request，不成为本地业务真相。
2. panel 结构对齐旧界面：`Enabled`、内联 `Freq / Power` 两态选择，以及
   Start / Stop / Step / Dwell 四个值。入口按钮的 `checked` 只表达 snapshot
   enabled，不表达 panel 是否打开。
3. panel 放入 fullscreen transparent overlay host 内的 child `QDialog/Qt::Widget`；
   panel 自身不创建 native surface。外部输入由 `Controls::OverlayContainer` 吞掉
   并关闭 panel。
4. Wayland 下只有 overlay top-level 显式配置 layer、output、anchors、size、
   margins、scope 和 visual geometry；不使用 `EnumTextButton/QComboBox` 创建新的
   top-level popup。
5. overlay/panel/keyboard 都属于 helper-owned interaction。panel 可见或 keyboard
   活跃时，`ApplicationDeactivate` 不得折叠 minibar；hide、collapse、Restore、
   shutdown 按 keyboard -> panel -> overlay 顺序收口。

### 成功标准

1. snapshot producer、shared codec、`MinibarClient` typed request、helper panel、
   main request handler 和 `StepSweepPanel` existing apply 链完整闭合。
2. helper 源码不出现 `StepSweep_*` property 名、`StepSweepPanel`、`DeviceManager`、
   `TxSessionService`、`IDevice` 或 provider business 依赖。
3. request 校验 version、revision、stable plan id、finite 数值、选中 plan 的正 step、
   正 dwell、
   device-open 和 CW eligibility；冲突或非法请求不修改 authoring/runtime 状态。
4. Sweep enabled/type/参数、disabled authoring 编辑、preview normalization、runtime
   writeback、MOD/device availability 都能触发新的 push snapshot。
5. pending 时 Sweep 入口和 panel 写控件统一禁用；发送失败/超时只解除 pending，
   不把本地 draft 显示为已生效值。
6. 顶部 Sweep 按钮本体、title label、info label 随 authoritative enabled 状态
   一起刷新；panel host 与字段样式同时覆盖浅色/深色主题。
7. 新 panel 文件被 helper CMake 纳入，Core 不反向引用 app 源文件；架构正文、
   draw.io、KnowledgeBase 索引和 legacy 退役口径同步更新。

### 验证级别

按用户要求仅做静态检查，不编译、不运行、不做 Win32/树莓派交互验证：

1. `git diff --check`。
2. CMake source inclusion 与依赖方向检查。
3. 协议 key/command/codec producer-consumer 对账。
4. helper 禁止依赖、单 surface、authoritative rendering 和关闭顺序的定向 `rg`。
5. draw.io XML 可解析性及文档现状一致性检查。
