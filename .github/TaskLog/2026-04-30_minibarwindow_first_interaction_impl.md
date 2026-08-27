# MiniBarWindow 第一阶段交互打通实施切片

## 目标

先把 `MiniBarWindow` 本身做出来，并把最小交互链打通，不追求业务参数编辑完整接入。

本次切片只覆盖：

1. `CorePlugin` 支持 `--ui-mode=minibar` 启动分流。
2. 新增独立 `MiniBarWindow`。
3. 默认收起态显示两个按钮：`RF` 状态按钮、向左展开按钮。
4. 展开态显示四个主要入口：`Freq`、`Level`、`Sweep`、`Mod Provider`。
5. `Freq`、`Level`、`Sweep` 点击后弹出占位弹窗。
6. `Mod Provider` 提供下拉项，例如 `AM`、`FM`、`PM`，选中后弹出对应占位弹窗。
7. 整个 minibar 可拖动。
8. 展开 / 收回交互完整可用。

## 本次明确不做

1. 不接 `CommonPanel` 现有 property binding。
2. 不接 `DeviceInfoWidget`。
3. 不接 `FancyTabWidget`。
4. 不接真实 sweep 或 modulation 参数面板。
5. 不接 owner、宿主联动、运行时命令协议。

## 实现策略

### 一、MiniBarWindow 独立成一个轻量 QWidget

首版直接做成独立顶层 `QWidget`，不复用 `MainWindow` 或 `CommonPanel`。

原因：

1. `CommonPanel` 已经和 property binding、ActionManager、Sweep/Settings 导航耦合太深。
2. 当前目标是把交互打通，不是把现有业务逻辑搬进去。
3. 先用最小控件集合把拖拽、展开、弹窗链路做通，更容易收敛。

### 二、收起态与展开态共用同一个窗口

窗口内部维护两套局部布局：

1. `Collapsed`：`RF` 按钮 + 展开按钮。
2. `Expanded`：`Freq`、`Level`、`Sweep`、`Mod Provider`。

切换状态时通过：

1. 切换可见控件。
2. 调整窗口尺寸。
3. 重新计算展开态矩形。

### 三、弹窗先统一用占位 Popup

首版不接真实参数编辑器，统一使用轻量占位弹窗：

1. 标题显示当前入口名，例如 `Freq` / `Level` / `Sweep` / `AM`。
2. 内容先放一段占位文本，例如 `TODO: panel content`。
3. 使用 `Qt::Popup` 或等价轻量弹出语义，优先满足“能点开、能关掉”。

### 四、Mod Provider 先做本地假数据下拉

首版 provider 列表先本地写死：

1. `AM`
2. `FM`
3. `PM`
4. `Pulse`

选择后：

1. 记录当前 provider 文本。
2. 更新 `Mod Provider` 按钮显示。
3. 弹出对应占位弹窗。

### 五、拖拽先作用于整个窗口

首版不再把拖拽限制在某个 title bar 区域，窗口任意空白区和非交互区都允许拖拽。

为了避免和按钮点击冲突：

1. 只在鼠标按下于窗口背景或非按钮区域时启动拖拽。
2. 按钮自身继续处理点击。

若实现成本更低，也可以先允许通过收起态背景区拖拽，并在展开态通过外层容器拖拽。

## 代码触点

本次切片预计修改：

1. `src/plugins/core/coreplugin.h`
2. `src/plugins/core/coreplugin.cpp`
3. `src/plugins/core/CMakeLists.txt`

本次切片预计新增：

1. `src/plugins/core/minibarwindow.h`
2. `src/plugins/core/minibarwindow.cpp`

## 验证目标

1. 默认启动仍走 `MainWindow`。
2. `--ui-mode=minibar` 能起 `MiniBarWindow`。
3. 收起态可见 `RF` 与展开按钮。
4. 点击展开按钮后出现 `Freq`、`Level`、`Sweep`、`Mod Provider`。
5. `Freq`、`Level`、`Sweep` 能弹出占位弹窗。
6. 选择 `Mod Provider` 下拉项后能弹出对应占位弹窗。
7. minibar 可拖动。

## 2026-05-06 启动一致性补充

### 当前发现

`MiniBarWindow` 已经复用了 `DeviceManager`、`BusinessManager`、`MainWindowDeviceController`、`MainWindowSettingsController` 的一部分启动链，但还有两处共享启动语义仍然停留在 `MainWindow` 路径：

1. `SystemBusyStatus` 这个全局 property 当前是在 `MainWindowDeviceController::setupSystemBusyStatusBinding()` 里首次创建。
2. 默认 `mute` 业务的激活当前只在 `MainWindow` 构造阶段执行。

这两处都不应依赖是否走 `MainWindow`：

1. `SystemBusyStatus` 会被业务插件直接读取和更新，不只是状态栏告警 UI 使用。
2. 默认激活 `mute` 业务属于后台进入已知初始态的一部分，不应只在 `MainWindow` 分支发生。

### 本次收敛策略

1. 把 `SystemBusyStatus` 的创建上移到 `CorePlugin` 的共享 property 初始化阶段。
2. `MainWindowDeviceController::setupSystemBusyStatusBinding()` 只负责绑定，不再承担“首次创建共享 property”的职责。
3. 在 `MiniBarWindow` 的全局 bootstrap 完成后，如果当前还没有激活业务，则补一次 `mute` 业务激活。

### 预期结果

1. `mainwindow` 和 `minibar` 两种 UI 模式都会拥有同一组后台基础 property。
2. `minibar` 模式下业务插件更新 `SystemBusyStatus` 时不再依赖 `MainWindow` 是否存在。
3. `minibar` 模式启动后，后台默认业务状态与 `mainwindow` 模式保持一致的已初始化基线。