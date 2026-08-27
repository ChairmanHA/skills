# MiniBarWindow 协议与三态状态机设计

## 目标

- 定义宿主程序给 `MiniBarWindow` 的最小启动输入；v1 不设计运行时宿主协议。
- 定义 `MiniBarWindow` 的最小窗口状态机，只保留三态：`Hidden`、`Collapsed`、`Expanded`。
- 明确当前实现阶段边界：先做 `MiniBarWindow` 本地三态可运行，只在启动时接受宿主给出的初始位置。
- 避免继续在现有 `MainWindow` 上叠加“简洁模式”分支，改为单独的轻量工具窗实现。

## 当前代码基线

当前启动链路仍是：

1. `src/app/main.cpp` 启动 Qt 应用与插件系统。
2. `CorePlugin` 构造时直接创建 `MainWindow`。
3. `CorePlugin::initialize()` 调 `MainWindow::initialize()`。
4. `CorePlugin::extensionsInitialized()` 里 `QTimer::singleShot(0, m_mainWindow, &MainWindow::show)`。

这意味着当前产品只有一个完整主窗入口；如果要引入 minibar 模式，正确方向是：

- 新增单独的 `MiniBarWindow` 类。
- 启动时根据独立的 UI 模式配置或命令行参数，选择显示 `MainWindow` 或 `MiniBarWindow`。
- 不复用当前 `MainWindow` 的 `Frameless + custom chrome + menu bar + status bar` 结构。

## 阶段边界

### 阶段 A：本地独立 MiniBarWindow

本阶段不接外部宿主进程，先完成：

- `MiniBarWindow` 可独立启动。
- 支持三态切换：`Hidden`、`Collapsed`、`Expanded`。
- `Collapsed` 表现为悬浮球，`Expanded` 表现为展开后的快捷操作面板。
- 支持用户拖拽 `Collapsed` 状态的位置，并把结果持久化到本地配置。
- 支持 `Collapsed` 状态在未悬停、未聚焦时自动半透明。
- 使用本地模拟配置提供默认位置、尺寸、初始状态。

### 阶段 B：宿主一次性传参

宿主在启动 minibar 进程时只需要传入：

- 初始位置
- 折叠尺寸
- 展开尺寸
- 初始显示命令

此阶段只做启动时协作：

- 宿主只提供一次性的初始落点。
- minibar 之后的位置、拖拽、展开、收回、透明度都由自身接收用户输入并处理。
- 不做运行时跨进程位置同步。

当前设计不规划宿主与 minibar 之间的运行时控制协议。

## 设计原则

1. `MiniBarWindow` 是单独窗口，不是 `MainWindow` 的缩小版。
2. 折叠与展开使用同一个窗口做尺寸切换，不创建两个窗口。
3. “最小化”在产品语义上改名为“收起”，不要调用系统最小化来模拟收起。
4. `Collapsed` 是用户可拖拽的悬浮球；拖拽结果是有效配置，需要持久化。
5. `Expanded` 是围绕当前悬浮球位置展开的快捷操作面板；默认保持“右边缘对齐球体右边缘、优先向左展开”的规则，再做工作区 clamp。
6. 失焦半透明只作用在 `Collapsed`；`Expanded` 更适合在外部点击后收回，而不是继续半透明。
7. 宿主对 minibar 的最小输入只是一组启动参数；运行时交互全部由 minibar 自己处理。
8. 当前版本先不设计 owner、全局置顶、宿主最小化联动等跨进程窗口关系问题。
9. 先把交互自洽做好，再决定是否需要后续扩展协议。

## 术语

### Host Window

外部频谱仪软件的主窗口。当前文档只承认它是 minibar 的启动者，不要求它在运行时持续参与控制。

### Floating Orb

`Collapsed` 状态下的悬浮球入口。它是用户可拖拽的主要交互锚点。

### Initial Collapsed Position

宿主或本地模拟配置提供的首次启动位置。它只用于第一次放置 minibar，不代表宿主持续拥有窗口位置控制权。

### Logical Pixel

协议中的几何值统一使用逻辑像素，而不是物理像素。

## 最小启动输入 v1

该协议先定义宿主启动时真正需要提供的最少字段。

v1 只要求支持：

- 本地模拟配置
- 启动参数或等价的一次性启动输入

v1 不定义运行时宿主命令，也不预留实时几何同步能力。

---

## 一、宿主启动输入

### 结构体

```cpp
struct MiniBarSessionConfig
{
    quint32 protocolVersion;
    QPoint  initialCollapsedTopLeftLogical;
    QSize   collapsedSizeLogical;
    QSize   expandedSizeLogical;
    quint32 startupCommand;
};
```

### 字段定义

| 字段 | 类型 | 必填 | 单位 | 含义 |
| --- | --- | --- | --- | --- |
| `protocolVersion` | `quint32` | 是 | 无 | 协议版本，首版固定为 `1`。 |
| `initialCollapsedTopLeftLogical` | `QPoint` | 否 | 逻辑像素 | 宿主或本地模拟配置给出的首次启动位置。建议使用 `(-1, -1)` 表示未指定。 |
| `collapsedSizeLogical` | `QSize` | 是 | 逻辑像素 | 收起态窗口目标尺寸。推荐初版默认 `48x48`。 |
| `expandedSizeLogical` | `QSize` | 是 | 逻辑像素 | 展开态窗口目标尺寸。当前目标方案默认 `725x179`。 |
| `startupCommand` | `quint32` | 是 | 枚举 | 初始显示命令，见下文 `MiniBarStartupCommand`。 |

### 枚举定义

```cpp
enum MiniBarStartupCommand : quint32 {
    StartupHidden = 0,
    StartupCollapsed = 1,
    StartupExpanded = 2
};
```

### 协议边界

为避免后续协议膨胀，v1 把边界明确为：

1. 宿主只负责把 minibar 启起来，并给出一个初始位置。
2. 宿主不负责运行时拖拽、展开、收回、透明度、吸附等交互。
3. 宿主不负责运行时推送窗口位置、DPI、显示命令。
4. 若未来确实需要 owner 或宿主联动，应通过新版本协议单独增加，而不是预埋到 v1。

---

## 二、本地配置与本地行为

以下内容属于 minibar 自己的本地配置，不属于宿主协议：

```cpp
struct MiniBarLocalConfig
{
    QPoint  persistedCollapsedTopLeftLogical;
    bool    persistUserPosition;
    bool    autoFadeWhenInactive;
    quint32 activeOpacityPermille;
    quint32 inactiveOpacityPermille;
};
```

这些字段的职责是：

- 记录用户上一次拖拽后的收起态位置。
- 决定是否持久化用户位置。
- 决定收起态是否自动半透明。
- 决定激活态与非激活态透明度。

这部分建议保存在本地设置中，由 minibar 自己读取与写回。

---

## MiniBarWindow 三态状态机

### 状态定义

#### 1. Hidden

- 窗口对象存在，但界面不可见。
- 不参与屏幕占位。
- 保留当前本地配置和上一次有效几何。

#### 2. Collapsed

- 窗口可见。
- 尺寸为 `collapsedSizeLogical`。
- 位置由本地持久化位置或初始位置决定。
- 表现为一个可拖拽的悬浮球。

#### 3. Expanded

- 窗口可见。
- 尺寸为 `expandedSizeLogical`。
- 位置按“右边缘对齐当前悬浮球右边缘、优先向左展开”计算。
- 表现为展开后的快捷操作面板。

### 局部交互标志

虽然主状态只有三态，但实现上仍可能维护少量局部标志，例如：

- 当前是否处于拖拽中
- 当前收起态是否处于激活视觉强调

这些标志不改变三态状态机的对外定义，也不需要在协议层单独建枚举。

### 主状态转移表

| 当前状态 | 事件 | 下一个状态 | 说明 |
| --- | --- | --- | --- |
| `Hidden` | `StartupCollapsed` | `Collapsed` | 按初始化命令进入收起态。 |
| `Hidden` | `StartupExpanded` | `Expanded` | 按初始化命令进入展开态。 |
| `Hidden` | `ShowCollapsed` | `Collapsed` | 显示为收起态。 |
| `Hidden` | `ShowExpanded` | `Expanded` | 显示为展开态。 |
| `Hidden` | `ToggleExpanded` | `Collapsed` | 首版定义为从隐藏进入收起态。 |
| `Collapsed` | `ClickOrb` | `Expanded` | 点击悬浮球展开。 |
| `Collapsed` | `ToggleExpanded` | `Expanded` | 展开。 |
| `Collapsed` | `Hide` | `Hidden` | 隐藏。 |
| `Expanded` | `ShowCollapsed` | `Collapsed` | 收起。 |
| `Expanded` | `ToggleExpanded` | `Collapsed` | 收起。 |
| `Expanded` | `ClickOutside` | `Collapsed` | 外部点击后收回到悬浮球。 |
| `Expanded` | `Esc` | `Collapsed` | 键盘快速收回。 |
| `Expanded` | `Hide` | `Hidden` | 隐藏。 |

### 不改变主状态的本地交互

以下行为是本地交互细节，不属于主状态迁移：

- `Collapsed` 的 `HoverEnter / HoverLeave` 只改变透明度表现。
- `Collapsed` 的 `BeginDrag / DragMove / EndDrag` 只更新位置并在结束时持久化。

### 明确不支持的行为

首版明确不支持：

- 用户在 `Expanded` 状态下拖拽整个面板改位。
- 隐藏态直接响应“向右展开”。
- 同时存在多个 minibar 窗口实例。
- 通过系统最小化按钮进入收起态。
- 宿主运行时持续推送窗口位置、DPI 或显示命令。
- 以跨进程协议驱动日常交互细节。

---

## MiniBarWindow 本地优先实现文档

`MiniBarWindow` 的第一阶段实施目标、启动分流方案、以及 `deviceinfowidget / fancytabwidget / menubar / MainWindowDeviceController` 等组件在 minibar 模式下的处理方式，已拆分到单独实施文档：

- [2026-04-16_minibar_local_first_implementation_plan.md](2026-04-16_minibar_local_first_implementation_plan.md)

本文保留：

1. 宿主启动输入字段定义。
2. 三态状态机定义。
3. 与交互直接相关的几何规则。

---

## 几何计算规则

### Collapsed

设：

- `W = current screen work area`
- `C = collapsedSizeLogical`
- `P = persistedCollapsedTopLeftLogical`
- `I = initialCollapsedTopLeftLogical`

则目标矩形优先级为：

1. 若 `P` 有效，则以 `P` 作为左上角。
2. 否则若 `I` 有效，则以 `I` 作为左上角。
3. 否则取 `W` 内一个默认安全位置。

最终矩形：

```text
collapsed.left   = preferred.left
collapsed.top    = preferred.top
collapsed.width  = C.width
collapsed.height = C.height
```

### Expanded

设：

- `O = currentCollapsedRect`
- `E = expandedSizeLogical`

则目标矩形：

```text
expanded.right  = O.right
expanded.top    = O.top
expanded.width  = E.width
expanded.height = E.height
expanded.left   = expanded.right - E.width + 1
```

该规则表达的是：

- 展开态围绕当前悬浮球展开。
- 默认保持“右边缘对齐悬浮球右边缘、优先向左展开”。
- 若工作区不足，再通过 clamp 修正。

### Clamp 规则

在任何状态下，目标矩形都要 clamp 到当前工作区：

1. 若 `left < workArea.left`，则整体右移。
2. 若 `top < workArea.top`，则整体下移。
3. 若 `right > workArea.right`，则整体左移。
4. 若 `bottom > workArea.bottom`，则整体上移。

但 clamp 的优先级应为：

1. 先尽量保留与当前悬浮球的对齐关系。
2. 不得超出工作区。

---

## 后续实现建议

### 先做什么

1. 新增 `MiniBarWindow`、`MiniBarStateController`、`MiniBarGeometryPolicy`。
2. 增加本地模拟 `MiniBarSessionConfig` 与本地配置读取逻辑。
3. 在启动选择逻辑中支持 `MainWindow / MiniBarWindow` 二选一。
4. 先把 `Hidden / Collapsed / Expanded` 三态切起来。
5. 先把收起态拖拽、位置持久化、失焦半透明跑通。
6. 先把展开态“点击外部收回”行为跑通。
7. 宿主启动参数只先接初始位置和尺寸相关字段。

### 暂时不要做什么

1. 不着急接任何宿主运行时控制协议。
2. 不着急做运行时宿主位置或 DPI 同步。
3. 不着急把现有完整业务界面全部塞进 minibar。
4. 不着急支持自动吸附、靠边隐藏、复杂动画、右键菜单。
5. 不着急把 owner、任务栏语义、跨进程窗口关系提前定死。

## 本文结论

当前更合理的落地方向是：

1. 新建 `MiniBarWindow`，不要继续挤在 `MainWindow` 的简洁模式里。
2. 把 `Collapsed` 明确定义为可拖拽悬浮球，把 `Expanded` 明确定义为快捷操作面板。
3. 先把本地三态状态机跑通：`Hidden`、`Collapsed`、`Expanded`。
4. 先用本地模拟配置把窗口几何、拖拽持久化、收起/展开、失焦半透明验证稳定。
5. 宿主在 v1 里只需要给一个初始位置和基本尺寸；其余交互都由 minibar 自己接收用户输入并处理。
