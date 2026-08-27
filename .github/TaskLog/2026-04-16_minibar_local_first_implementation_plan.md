# MiniBarWindow 启动模式本地优先实施文档

## 目标

- 把 `MiniBarWindow` 启动模式单独列为第一阶段实施目标。
- 根据启动时的命令行参数，决定使用现有 `Frameless QMainWindow` 布局，还是新的 `MiniBarWindow` 布局。
- 明确 `deviceinfowidget`、`fancytabwidget`、`menubar`、`titlebar`、`MainWindowDeviceController`、`ActionManager` 等现有组件在 minibar 模式下分别如何处理。
- 只落本地优先版本：**先实现本地三态切换和启动模式分流，不着急接外部宿主进程**。

## 范围

### 本阶段要完成的事

1. 支持命令行参数选择 UI 启动模式。
2. `main` 模式仍然走现有 `MainWindow`。
3. `minibar` 模式走新建的 `MiniBarWindow`。
4. `MiniBarWindow` 先支持三态：`Hidden`、`Collapsed`、`Expanded`。
5. `MiniBarWindow` 先使用本地模拟会话配置，不依赖宿主进程。
6. `Collapsed` 先实现为可拖拽悬浮球，拖拽结果持久化到本地配置。
7. `Collapsed` 先实现未悬停、未聚焦时自动半透明。
8. `Expanded` 先实现点击外部区域自动收回到 `Collapsed`。
9. 若后续接宿主参数，首版只接初始位置和尺寸。
10. 把现有 UI 组件按“直接复用 / 抽取复用 / 暂不接入”三类明确分治。

### 本阶段不做的事

1. 不做外部宿主进程 IPC。
2. 不做 `HWND owner` 绑定。
3. 不做运行时宿主窗口位置或 DPI 跟随。
4. 不做自动吸附、靠边隐藏、复杂动画。
5. 不做右键菜单。
6. 不要求一次性把完整 `MainWindow` 功能搬进 minibar。
7. 不做宿主到 minibar 的运行时显示、隐藏、展开命令通道。

## 当前代码基线

当前启动路径已经从“单入口、单主窗”演进为“单入口、双 UI 分支”：

1. `src/app/main.cpp` 启动 Qt 应用与插件系统。
2. `CorePlugin` 构造函数只保留与窗口类型无关的属性、ActionManager、CommonDeviceProfile 等基础初始化。
3. `CorePlugin::initialize()` 解析 `--ui-mode=main|minibar`，按模式创建 `MainWindow` 或 `MiniBarWindow`。
4. `MiniBarWindow::initialize()` 在本地 UI 初始化前，先补齐 minibar 分支所需的最小全局启动基座。
5. `CorePlugin::extensionsInitialized()` 延迟显示当前主 UI，其中 minibar 分支额外做 `show/raise/activateWindow`。

当前主窗内部还直接承担：

- `TitleBar + QMenuBar + ActionContainer`
- `CommonPanel`
- `FancyTabWidget + modulation dock`
- `statusBar + DeviceInfoWidget`
- `MainWindowChromeWin`
- `MainWindowSettingsController`
- `MainWindowDeviceController`

这说明 `MiniBarWindow` 不能只是把 `MainWindow` 缩小，而必须成为单独启动模式。

同时也说明一个更关键的事实：

- **仓库里有相当一部分“底层看似与 UI 无关”的启动副作用，实际上历史上藏在 `MainWindow` 构造函数里。**
- 因此 minibar 分支要想真正启动成功，不能只做一个轻量 QWidget 外壳，还必须把这些影响插件初始化、命令注册、设备管理和全局上下文的启动副作用显式搬出来。

## 2026-04-30 当前实现状态补充

本节覆盖本文早期偏“建议态”的描述，以下内容以当前仓库代码为准。

### 本次已经实际落地的内容

1. `CorePlugin` 启动分流已经真正落地，而不是停留在设计阶段。
2. `--ui-mode=main` 保持现有 `MainWindow` 路径，`--ui-mode=minibar` 走新的 `MiniBarWindow` 路径。
3. `MiniBarWindow` 首版交互壳已经落地：支持 `Hidden / Collapsed / Expanded` 三态、本地位置持久化、收起态拖拽、点击展开/收回、外部点击收回、局部占位弹窗。
4. minibar 分支的最小全局启动基座已经补齐，不再只是“创建一个 QWidget 然后 show 出来”。
5. `ActionManager` 已不再硬依赖 `MainWindow::instance()` 作为唯一动作宿主，而是允许回退到 `GUIContext` 当前主窗口。
6. minibar 的首显路径已经单独处理，避免“进程已起来但窗口没有明显显示出来”的问题。

### minibar 模式起不来的根因

本次运行时问题的根因，不在 `MiniBarWindow` 的按钮交互本身，而在于 minibar 分支最初跳过了 `MainWindow` 构造函数中隐藏的那批全局启动副作用。

当时最典型的现象是：

1. 插件初始化阶段访问 `SGStudio.Menu.System` 时，日志出现 `failed to find : "SGStudio.Menu.System"`。
2. `ActionManagerPrivate::overridableAction()` 和 `unregisterAction()` 直接调用 `MainWindow::instance()->addAction/removeAction()`，而 minibar 分支并不会创建 `MainWindow`。
3. `BusinessManager` 历史上默认在 `MainWindow` 路径里、且位于 `DeviceManager` 之后创建；如果 minibar 分支不保持这个顺序，就会出现空指针 connect 或后续业务激活链不完整的问题。

换句话说，**minibar 起不来，并不是因为“轻量 QWidget 天然不适合”，而是因为旧代码把许多系统级初始化错误地耦合在 `MainWindow` 构造链里。**

### 本次如何解决 minibar 启动问题

本次采取的策略不是“为了能起就临时屏蔽插件”，而是把 minibar 分支需要的最小基础设施显式补齐：

1. `CorePlugin` 构造函数不再直接 `new MainWindow`，而是在 `initialize()` 中先解析 `StartupUiMode`，再选择主 UI。
2. `MiniBarWindow::initialize()` 中新增显式的 `initializeGlobalBootstrap()` 步骤，用于补齐以前隐含在 `MainWindow` 构造函数中的关键副作用。
3. 在 minibar 分支中显式执行 `ActionManager::setContext(Context(Constants::CONTEXT_GLOBAL))`，保证动作系统上下文与主窗路径一致。
4. 在 minibar 分支中显式创建默认 `ActionContainer` / menu container，使 GPS、Updater 等插件初始化时仍能注册 System/File/Device 菜单动作，而不是在启动期直接失败。
5. 在 minibar 分支中显式创建 `DeviceManager`，并保持它先于 `BusinessManager` 被构造，复用主窗路径已经验证过的初始化顺序。
6. 在 minibar 分支中创建最小可用的 `MainWindowDeviceController` 和 `MainWindowSettingsController`，先确保设备相关基础初始化和设置/主题相关全局动作链仍然存在。
7. 在 `ActionManager` 中加入“优先 `MainWindow::instance()`，否则回退到 `Utils::GUIContext::instance()->getMainWindow()`”的动作宿主策略，去掉 minibar 分支对 `MainWindow` 单例的硬依赖。

这套修复的本质是：

- **视觉外壳可以分流。**
- **全局启动基座不能因为不走 `MainWindow` 而丢失。**

### 走 MiniBarWindow 分支时，为什么不能影响底层单例实例化

minibar 不是一个“玩具悬浮条”，而是后续需要承载至少 80% 核心能力的轻 UI 入口。因此本项目当前采用的原则应明确写清楚：

- `MainWindow` 和 `MiniBarWindow` 只应该分流“顶层视觉外壳”。
- `PropertyManager`、`ActionManager`、`CommonDeviceProfile`、`DeviceManager`、`BusinessManager`、全局 context、动作容器、scanner 注册面等基础设施，不应继续隐含绑定在 `MainWindow` 是否被构造上。

当前代码中的分层策略是：

1. `CorePlugin` 构造阶段保留与窗口无关的基础单例与全局对象初始化，例如 Property 体系、`ActionManager`、`CommonDeviceProfile`。
2. `MiniBarWindow::initializeGlobalBootstrap()` 负责补齐“过去在 `MainWindow` 路径里才会发生”的那部分最小基础设施，包括全局动作上下文、默认动作容器、`DeviceManager`、`BusinessManager`、设备/设置控制器等。
3. `GUIContext` 在 minibar 路径中指向 `MiniBarWindow`，确保依赖 `GUIContext` 取父窗口的对话框和动作宿主可以继续工作。
4. `ActionManager` 对动作宿主窗口的选择，已经从“必须有 `MainWindow::instance()`”调整成“允许回退到当前 `GUIContext` 主窗口”，从而把命令体系从 `MainWindow` 单例硬绑定里剥离出第一步。

以 `DeviceScanner` 为例，正确的原则应该是：

- scanner 应注册到 `DeviceManager` 这样的底层注册中心；
- 是否存在 `MainWindow`，不应成为 scanner 能否初始化的前提；
- minibar 分支必须尽量在其它业务插件进入 `initialize()` 之前，就把这类基础注册中心准备好。

这也是后续把 minibar 做到“可承载 80% 以上功能”时最重要的架构前提。

### 当前实现与最初计划相比的一个重要调整

本文早期版本更倾向于“minibar 第一阶段尽量不创建菜单容器、不创建设置/设备控制器”。这在纯原型期是合理的，但在真实仓库里不够用。

当前代码已经表明：

1. 不创建可见 `TitleBar/MenuBar`，仍然是对的。
2. 但**不创建可见菜单栏**，不等于**不创建动作容器和菜单对象本身**。
3. minibar 分支为了让插件初始化与动作注册继续成立，实际上已经需要创建一套“不可见但可用”的默认 `ActionContainer` 基座。
4. 同理，`MainWindowSettingsController` 与 `MainWindowDeviceController` 也不是“完全不进 minibar”，而是以“最小启动基座”的方式进入 minibar 分支，先承担全局初始化职责，后续再逐步做 presenter/服务化拆分。

因此，当前更准确的表述应该是：

- minibar 仍然是独立顶层 UI；
- 但它不再追求“与主窗完全断开”；
- 它要共享足够多的底层基础设施，才能不破坏插件系统、动作系统和设备系统的启动链。

## 第一阶段的核心架构决策

### 决策一：启动模式在 CorePlugin 分流，不在 MainWindow 内部做分支

原因：

1. `MainWindow` 当前构造函数太重，进入构造就会创建完整布局和控制器。
2. minibar 模式本质上不需要 `menu bar`、`status bar`、`dock`、`frameless main window chrome`。
3. 如果继续在 `MainWindow` 里加简洁模式分支，后续每个控制器都要双态适配，复杂度会持续上升。

### 决策二：MiniBarWindow 先做成轻量 QWidget，而不是 QMainWindow

原因：

1. minibar 不需要菜单栏、状态栏、dock 管理。
2. 不应复用 `MainWindowChromeWin` 这套为完整主窗准备的 Win32 无边框非客户区逻辑。
3. `QWidget` 更适合做收起/展开的轻量工具条容器。

### 决策三：本阶段允许 minibar 功能不完整，但启动模式必须完整分流

也就是说：

- `main` 模式必须保持现状，不引入行为回归。
- `minibar` 模式先把三态窗口、基本显示和局部组件复用跑通。
- 某些强依赖 `MainWindow`、`menu bar`、`dock` 的功能，本阶段可以明确暂不支持。

## 启动模式设计

### 命令行参数

首版建议命令行参数为：

```text
--ui-mode=main
--ui-mode=minibar
```

默认值：

- 未传参时，默认 `main`。

错误处理：

- 传入未知值时，记录 warning，并回退到 `main`。

### 内部枚举

```cpp
enum class StartupUiMode {
    MainWindow,
    MiniBar
};
```

### 建议解析函数

建议新增独立帮助函数，而不是把解析逻辑散落在 `main.cpp` 和 `CorePlugin` 中。

```cpp
StartupUiMode parseStartupUiMode(const QString &arguments);
```

建议位置：

- 可先放在 `coreplugin.cpp` 的匿名命名空间里。
- 若后续还要复用到 settings/重启流程，再独立成 `startupuimode.h/.cpp`。

### 参数优先级

本阶段只认命令行，不引入配置文件优先级竞争：

1. 命令行 `--ui-mode=...`
2. 无命令行时默认 `main`

原因：

- 第一阶段的目标是把启动模式切换跑通。
- 先不引入 `Settings.ini` 中的 `APP/UiMode`，避免命令行与配置冲突。

## CorePlugin 重构方案

### 当前问题

`CorePlugin` 构造函数里已经直接 `new MainWindow`，导致：

- 命令行还没解析，主窗就已经被创建；
- 无法在 `MainWindow` 与 `MiniBarWindow` 之间做真正分流；
- `extensionsInitialized()` 只能 show 既定的 `MainWindow`。

### 第一阶段改法

把 `CorePlugin` 从“构造时固定创建 MainWindow”改为“initialize 时按模式创建主 UI”。

建议成员调整为：

```cpp
class CorePlugin : public ExtensionSystem::IPlugin
{
    ...
private:
    StartupUiMode m_startupUiMode = StartupUiMode::MainWindow;
    QWidget *m_primaryWindow = nullptr;
    MainWindow *m_mainWindow = nullptr;
    MiniBarWindow *m_miniBarWindow = nullptr;
};
```

### 建议生命周期

#### 构造函数中保留

构造函数仍保留这些“与窗口类型无关”的初始化：

- PropertyManager 注册
- `ActionManager` 创建
- `CommonDeviceProfile` 创建
- 其它纯数据层/业务层初始化

#### 构造函数中移除

- `new MainWindow`

#### initialize(arguments, errorString)

执行顺序建议为：

1. 解析 `StartupUiMode`
2. 按模式创建对应窗口
3. 设置 `Utils::GUIContext::mainWindow`
4. 仅对 `MainWindow` 模式调用 `MainWindow::initialize()`
5. 仅对 `MiniBarWindow` 模式调用 `MiniBarWindow::initialize()`

#### extensionsInitialized()

统一 show `m_primaryWindow`：

```cpp
bool CorePlugin::extensionsInitialized()
{
    if (m_primaryWindow) {
        QTimer::singleShot(0, m_primaryWindow, &QWidget::show);
    }
    return true;
}
```

### 第一阶段的一个现实风险

仓库中已有多处代码直接依赖：

- `MainWindow::instance()`
- `Utils::GUIContext::instance()->getMainWindow()`
- `ICore::dialogParent()` / dock 相关 API

这意味着：

- **MiniBar 模式不能假设整个仓库已经完全去 MainWindow 化**。
- 第一阶段要明确哪些功能在 minibar 模式下不可用，或需要先做最小替代。

## 组件处理总表

| 组件 | `main` 模式 | `minibar` 模式 | 第一阶段处理策略 |
| --- | --- | --- | --- |
| `MainWindow` | 正常创建与显示 | 不创建、不显示 | 保持全量现状，仅在 `main` 模式进入。 |
| `MainWindowChromeWin` | 保留 | 不使用 | 只绑定完整 `MainWindow`。 |
| `TitleBar` | 保留 | 不创建 | minibar 不做标题栏菜单结构。 |
| `QMenuBar / ActionContainer` | 保留 | 不创建可见菜单栏，但创建默认 ActionContainer 基座 | 当前实现已证明：minibar 启动阶段仍需可用的 File/Device/System 容器，以承接插件动作注册。 |
| `MainWindowSettingsController` | 保留 | 创建轻量实例，承担主题/动作相关启动职责 | 当前不是完整“设置 UI 复用”，而是先保证全局设置动作链不断。 |
| `MainWindowDeviceController` | 保留 | 创建最小实例，承担设备相关基础初始化 | 当前实现先复用其已验证的基础初始化链，后续再考虑 presenter/service 拆分。 |
| `DeviceInfoWidget` | 保留在 statusBar | 暂未正式接入 minibar，可作为后续 compact 化重点 | 当前启动问题已先解决，但设备状态展示与 compact 呈现仍是后续工作。 |
| `FancyTabWidget` | 保留 | 本阶段不直接复用整类 | 先不把完整业务页/调制列表整体塞进 minibar。 |
| `CommonPanel` | 保留 | 本阶段不直接复用整类 | 未来视需要拆出 compact command strip。 |
| `LockWidget` | 保留 | 不接入 | minibar 第一阶段不做锁屏相关入口。 |
| `Preference/About/Brightness` | 保留 | 默认不接入 | 这些对话框暂时不作为 minibar 目标功能。 |
| `GUIContext` | 指向 `MainWindow` | 指向 `MiniBarWindow` | 解决基于 GUIContext 的对话框父窗口问题。 |
| `ICore` dock API | 正常可用 | 视为不支持 | 第一阶段 minibar 不支持 dock 体系。 |

## 关键组件的详细处理

### 一、MiniBarWindow 本体

建议新增：

```cpp
class MiniBarWindow : public QWidget
{
    Q_OBJECT
public:
    explicit MiniBarWindow(QWidget *parent = nullptr);
    bool initialize(const QString &arguments, QString *errorString);

private:
    MiniBarStateController *m_stateController = nullptr;
    MiniBarGeometryPolicy *m_geometryPolicy = nullptr;
    DeviceInfoWidget *m_deviceInfoWidget = nullptr;
    QWidget *m_orbButton = nullptr;
    QWidget *m_expandedPanel = nullptr;
};
```

第一阶段职责：

1. 加载本地模拟 `MiniBarSessionConfig`
2. 初始化三态控制器
3. 初始化收起态悬浮球
4. 初始化展开态快捷操作面板
5. 把 `DeviceInfoWidget` 以 compact 模式嵌进去
6. 支持 `Collapsed` 状态下的拖拽、位置持久化与透明度切换
7. 根据状态切换 `show/hide/resize/move`
8. 若后续接宿主参数，只消费启动时的初始位置与尺寸，不引入运行时宿主控制入口

### 二、DeviceInfoWidget

当前 `DeviceInfoWidget` 具备单独复用基础：

- 本身继承 `QWidget`
- 当前只是被 `MainWindowDeviceController` 创建后塞进 `statusBar`
- UI 已经把 `apiVersion`、`guiVersion`、温度、warning、deviceState` 拆成了独立区域

第一阶段建议：

1. 新增 `setCompactMode(bool)` 或 `applyDisplayMode(DeviceInfoDisplayMode mode)`。
2. 在 compact 模式下隐藏：
   - `uid`
   - `model`
   - `hardware`
   - `mfwVersion`
   - `ffwVersion`
   - `sampleRate`
   - `bandwidth`
3. 保留：
   - `deviceState`
   - `warnningStatus`
   - `apiVersion`
   - `guiVersion`
   - `temperature`
4. 去掉对 statusBar 的位置假设，允许直接放进普通布局。

### 三、MainWindowDeviceController

当前类耦合了两类职责：

1. 设备状态同步到 `DeviceInfoWidget`
2. Device 菜单与 ETH Connect 对话框

在 minibar 模式下：

- 第 1 类职责需要复用
- 第 2 类职责不需要跟着进来(eth设备因为不支持scaner需要手动打开连接界面，minibar模式暂不支持)

所以从长期结构上，仍然建议拆出一个窄职责控制器，例如：

```cpp
class DeviceInfoPresenter : public QObject
{
    Q_OBJECT
public:
    explicit DeviceInfoPresenter(DeviceInfoWidget *widget, QObject *parent = nullptr);
    void initialize();
};
```

拆分后：

- `MainWindowDeviceController` 继续负责完整主窗的 device menu / dialog / presenter 组合
- `MiniBarWindow` 只持有 `DeviceInfoWidget + DeviceInfoPresenter`

但要强调：**当前实际代码尚未完成这一步抽取。**

当前为了先把 minibar 启动链跑通，已经采取了一个更务实的过渡方案：

1. minibar 分支先直接创建一个最小 `MainWindowDeviceController` 实例；
2. 先保证设备相关基础初始化、后续 device menu/action 容器和兼容链不因分支切换而断掉；
3. 等设备交互和状态展示真正开始打通时，再回过头把 `DeviceInfoPresenter` 这类更干净的抽取做完。

也就是说，这一块当前是“先保启动正确，再逐步降耦”，而不是已经完全进入最终结构。

### 四、FancyTabWidget

当前 `FancyTabWidget` 不适合直接搬进第一阶段 minibar：

1. 它仍然维护 `QStackedLayout`、业务页切换、standalone page、选中业务状态。
2. 它有 `static FancyTabWidget *instance()` 单例语义。
3. 它的 modulation list 点击仍会驱动当前业务页切换。
4. 它和 `MainWindow` 的业务选择、sweep/device settings 页面切换有大量现成耦合。

所以第一阶段结论是：

- **不直接复用整个 `FancyTabWidget` 到 `MiniBarWindow`**。

建议处理方式：

1. 第一阶段先不接入它。
2. 如果需要在 minibar 里展示调制列表，后续单独从 `FancyTabWidget` 提炼出 `ModulationListPanel` 或等价只读/轻交互子控件。
3. 在调制列表未拆出前，`MiniBarWindow` 展开态可以先用占位区域或最小按钮集合验证三态与几何。

这条决策很关键，因为它直接避免把“隐藏 stacked layout 的旧问题”重新带进 MiniBarWindow。

### 五、CommonPanel

`CommonPanel` 也不适合第一阶段整类复用，原因是：

1. 它固定高度 60。
2. 它自带 `Sweep` / `Settings` 按钮，而这两个按钮当前依赖 standalone page 切换。
3. 它还依赖 `ActionManager` 的 `Single/Continue` 命令体系。

所以第一阶段建议：

- 不直接把 `CommonPanel` 整类塞进 `MiniBarWindow`。

更合适的方式是：

1. 先只做 `MiniBarWindow` 三态壳。
2. 后续如果确实需要频率/功率/RF/Mod 入口，再拆一个 `MiniBarCommandStrip`，只复用必要的 property binding，不把 sweep/settings/trigger 模式一起搬进去。

### 六、TitleBar / MenuBar / ActionManager

当前完整主窗的 menu 体系来自：

- `MainWindow::registerDefaultContainers()`
- `MainWindow::registerDefaultActions()`
- `MainWindowSettingsController::registerActions()`

历史上 `ActionManager` 的关键路径还直接 fallback 到 `MainWindow::instance()`。

当前实现已经演进为：

1. `main` 模式保持现状。
2. `minibar` 模式不创建 `TitleBar`。
3. `minibar` 模式不创建可见菜单栏，但会创建默认动作容器，以保证插件初始化与全局动作注册不被阻断。
4. `ActionManager` 已经开始去掉对 `MainWindow::instance()` 的唯一依赖，当前允许回退到 `GUIContext` 主窗口承接动作。

这意味着当前更准确的表述是：

- minibar 仍然是“无可见菜单、无标题栏、无 dock 的轻量模式”；
- 但它不是“无菜单基础设施”的模式；
- 为了不破坏插件系统与动作系统的启动链，它必须保留一套不可见但可用的动作容器基座。

### 七、GUIContext 与弹窗父窗口

仓库中很多对话框不是直接用 `MainWindow::instance()`，而是走：

- `Utils::GUIContext::instance()->getMainWindow()`

因此在 minibar 模式下必须做的一件事是：

- 让 `GUIContext` 指向 `MiniBarWindow`

这件事当前已经在代码中实际落地。这样至少以下类型的对话框父窗口语义仍然正确：

- `Controls::Dialog`
- `MessageDialog`
- 若后续 minibar 里触发的局部弹窗

但要注意：

- 那些**直接**写死 `MainWindow::instance()` 的路径，本阶段仍然是风险点。

### 八、ICore 与 dock 语义

`ICore` 当前多项 API 直接委托到 `MainWindow::instance()` 的 dock 操作。

这意味着：

- minibar 模式下不应承诺 dock 体系可用。

第一阶段建议：

1. 不在 minibar 模式中使用任何依赖 `ICore::addDockWidget` 的功能。
2. 文档层面明确：dock 不是 minibar 第一阶段目标。

## 第一阶段建议里程碑

### A0：启动模式切换跑通

目标：

- `--ui-mode=main` 正常起现有主窗
- `--ui-mode=minibar` 起新的 `MiniBarWindow`

完成标志：

- `CorePlugin` 不再在构造函数里固定创建 `MainWindow`
- `extensionsInitialized()` 能 show 当前选中的主 UI

### A1：MiniBarWindow 三态壳跑通

目标：

- `Hidden / Collapsed / Expanded` 三态切换稳定
- 使用本地模拟 `MiniBarSessionConfig`
- 几何计算按协议文档执行

完成标志：

- 收起态显示一个可拖拽悬浮球
- 展开态显示一个固定尺寸快捷操作面板
- 支持点击球展开、点击外部收回、隐藏按钮或快捷切换
- 支持收起态拖拽、位置持久化、未悬停半透明

### A2：DeviceInfoWidget compact 化接入

目标：

- `DeviceInfoWidget` 可脱离 statusBar 独立显示
- compact 模式字段裁剪正确
- 设备状态能正常刷新到 minibar

完成标志：

- `deviceState/api/gui/temperature/warning` 正常更新
- 其余版本字段在 compact 模式下隐藏

### A3：其余业务区暂时占位

目标：

- 展开态除了状态区外，其余内容先允许是占位区域

完成标志：

- 不因未接 `FancyTabWidget/CommonPanel` 而阻塞三态本身落地

## 代码触点建议

第一阶段大概率会触到这些文件：

- `src/plugins/core/coreplugin.h`
- `src/plugins/core/coreplugin.cpp`
- `src/plugins/core/CMakeLists.txt`
- `src/plugins/core/deviceinfowidget.h`
- `src/plugins/core/deviceinfowidget.cpp`
- `src/plugins/core/mainwindowdevicecontroller.h`
- `src/plugins/core/mainwindowdevicecontroller.cpp`

新增文件建议：

- `src/plugins/core/minibarwindow.h`
- `src/plugins/core/minibarwindow.cpp`
- `src/plugins/core/minibargeometrypolicy.h`
- `src/plugins/core/minibargeometrypolicy.cpp`
- `src/plugins/core/minibarstatecontroller.h`
- `src/plugins/core/minibarstatecontroller.cpp`
- `src/plugins/core/deviceinfopresenter.h`
- `src/plugins/core/deviceinfopresenter.cpp`

## 验证计划

### 静态验证

1. `main` 模式代码路径不引入额外行为变化。
2. `minibar` 模式不再依赖 `MainWindow` 构造函数进入。
3. `DeviceInfoWidget` compact API 不影响现有主窗状态栏显示。

### 运行验证

1. 用默认启动，确认仍显示现有无边框 `MainWindow`。
2. 用 `--ui-mode=minibar` 启动，确认显示 `MiniBarWindow`。
3. 在 `minibar` 模式下验证三态切换。
4. 验证 `DeviceInfoWidget` 在 minibar 中能跟随设备状态刷新。
5. 验证 `main` 模式下菜单、状态栏、设备菜单、调制布局不回归。

## 本文结论

截至 2026-04-30，本文更准确的结论应当是：

1. 第一阶段的正确落地方式仍然不是“给 `MainWindow` 加一个简洁分支”，而是继续坚持在 `CorePlugin` 层做启动模式分流。
2. `main` 模式保持现有 `Frameless QMainWindow`，`minibar` 模式走独立 `MiniBarWindow`，这一点已经真正落地。
3. 但 minibar 分支不能只切换顶层 QWidget，而必须显式补齐过去隐藏在 `MainWindow` 构造链中的全局启动副作用，否则插件系统、动作系统、设备系统都会在启动期断链。
4. 因为 minibar 最终目标不是做一个装饰性悬浮条，而是承载至少 80% 以上的核心能力，所以底层单例、注册中心、scanner 入口、动作容器和设备管理等基础设施都应该尽量独立于 `MainWindow` 是否存在。
5. 当前实现已经完成第一步：`CorePlugin` 分流、`MiniBarWindow` 三态壳、最小全局启动基座、`ActionManager` 对 `GUIContext` 的回退支持，以及主路径可启动。
6. 当前尚未完成的重点，不再是“能否起 minibar 窗口”，而是后续如何逐步打通设备枚举/连接/状态刷新、DeviceInfo 展示、常用业务入口、更多 `MainWindow::instance()` 直接依赖路径的去耦，以及让 minibar 真正具备接近主窗 80% 能力的可用度。
