# GPS Plugin 工程骨架接入计划

## 目标

- 新建独立的 GPS 插件目录工程，纳入当前 CMake 插件体系。
- 插件依赖 Core 与 HTRA，并在初始化后向 System 菜单注册 GNSS action。
- 仅复用现有 `gpsinfodialog.ui` 作为界面资源，不接入旧项目中的业务逻辑与属性系统。

## 非目标

- 本次不打通 GNSS 数据轮询、设备配置、Property 绑定。
- 本次不处理旧 `gpsinfodialog.cpp/.h` 的遗留依赖与编译问题。
- 本次不要求插件可编译可运行，只先把工程骨架和菜单挂接关系搭好。

## 设计

### 1. 目录与构建

- 在 `plugins/gps/` 下新增独立 `CMakeLists.txt`。
- 新增 `GPSPlugin`，`OUTPUT_NAME` 设为 `GPS`。
- `SOURCES` 仅包含：插件类、对话框壳层、`gpsinfodialog.ui`、`gps.json`。
- 不把旧 `gpsinfodialog.cpp/.h` 纳入构建；后续若要迁移 UI 控件绑定，再逐步拆分旧代码。

### 2. 插件元数据

- `Name`: `GPS`
- `Dependencies`: `["Core", "HTRA"]`
- 分类先放在 `Device`，后续若 System 菜单项增多再细分。

### 3. 插件初始化职责

- 在 `initialize()` 中创建 GNSS action。
- 通过 `Core::ActionManager` 取得 `System` 菜单容器，并插入到 `GROUP_MENU_SYSTEM_DEVICE`。
- action 触发时弹出一个轻量 `GpsInfoDialog`。

### 4. 对话框壳层

- 新建轻量 `GpsInfoDialog`，继承 `Controls::Dialog`。
- 仅调用 `Ui::GPSInfoDialog::setupUi(contentWidget())` 完成界面装载。
- 不接任何旧业务信号、PropertyNode、DeviceNode、Profile 逻辑。
- 先统一设置标题为 `GNSS`，作为后续接入实时状态的承载窗口。

### 5. 风险与后续

- 旧 `gpsinfodialog.ui` 若引用了旧项目自定义控件，后续仍需处理头文件和链接依赖。
- 插件当前只完成菜单与窗口骨架，Core/HTRA 的 GNSS property / query 流程后续再接。
- 若后续决定复用 Core 里已有 GNSS 对话框语义，需要先清理重复命名和职责边界。

## 预期改动文件

- `plugins/CMakeLists.txt`
- `plugins/gps/CMakeLists.txt`
- `plugins/gps/gps.json`
- `plugins/gps/plugin.h`
- `plugins/gps/plugin.cpp`
- `plugins/gps/gpsdialog.h`
- `plugins/gps/gpsdialog.cpp`

## 2026-03-24 迁移补充

- Core 原有 `gpsinfodialog` 已不再保留实现，GNSS 对话框职责收拢到 `plugins/gps`。
- `MainWindow` 不再持有 GNSS dialog，也不再直接处理 GNSS dialog 的显示/更新。
- `plugins/gps` 新增 qmake 工程文件，避免删除 Core 对话框后 qmake 路径失配。
