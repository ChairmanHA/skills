# 删除 BusinessUiBridge 并收口 BusinessEntryUiBridge 方案

## 本轮目标

1. 删除 `BusinessUiBridge`
2. 保留并复用 `IBusinessEntryHost + BusinessEntryUiBridge`
3. 不处理 minibar menu 细节
4. 主模式下完成 Debug 构建与启动验证

## 设计判断

当前 `BusinessUiBridge` 已经没有继续保留的必要：

1. 它是 `FancyTabWidget` 专属命名包装
2. 其能力已经被 `FancyTabWidget` 实现的 `IBusinessEntryHost` 覆盖
3. 再保留一层只会让主模式同时依赖两套 bridge

因此本轮应直接删除：

1. `src/plugins/core/businessuibridge.h`
2. `src/plugins/core/businessuibridge.cpp`
3. 所有对它的 include / attach / detach / initialize 引用

## BusinessEntryUiBridge 收口方式

本轮不做多 host registry，也不做 minibar menu adapter。

只做更直接的收口：

1. `BusinessEntryUiBridge` 不再使用单例静态 `instance()`
2. 改为由 `CoreRuntimeServices` 持有唯一实例
3. `CoreRuntimeServices` 暴露 `businessEntryUiBridge()` getter
4. 主模式和 analog plugin 通过 `CoreRuntimeServices::instance()` 获取该 bridge

这样仍然保持“当前进程只有一个激活 UI shell”的简单模型，不引入额外复杂度。

## 影响面

### Core

1. `coreruntimeservices.h/.cpp`
   - 新增成员 `m_businessEntryUiBridge`
   - 在 `initializeRuntimeBridges()` 中按成员方式创建
   - 提供 getter
2. `mainwindow.cpp`
   - 只 attach/detach `BusinessEntryUiBridge`
3. `CMakeLists.txt`
   - 删除 `businessuibridge.*`

### Analog

1. `analogmodulationplugin.cpp`
   - 不再调用 `BusinessEntryUiBridge::instance()`
   - 改从 `CoreRuntimeServices::instance()->businessEntryUiBridge()` 获取

## 本轮不做

1. 不让 minibar 实现 `IBusinessEntryHost`
2. 不做 menu-backed host
3. 不做 presenter / compact panel
4. 不做额外架构抽象

## 验证口径

1. `BusinessUiBridge` 相关源码和引用全部删除
2. Core/Analog 编译通过
3. 主模式启动后不因 bridge 缺失崩溃

## 后续微调：FancyTabWidget 符号导出收口

当前 `FancyTabWidget` 已经只在 `Core` 内部使用：

1. `MainWindow` 直接创建并持有它
2. `BusinessManager` 只在 core 内部 `.cpp` 中使用其具体能力
3. Analog 等外部 plugin 只通过 `IBusinessEntryHost / BusinessEntryUiBridge` 间接消费业务入口语义

因此本轮后续微调应把 `FancyTabWidget` 从导出类退回为 core 内部类：

1. 移除 `FancyTabWidget` 类声明上的 `CORE_EXPORT`
2. 不改 `IBusinessEntryHost` 抽象边界
3. 不额外改 minibar 或 menu host

## 后续微调：BusinessManager host 接口抽象化

仅移除 `FancyTabWidget` 的导出还不够，因为 `BusinessManager` 的 public 头文件仍暴露了 `FancyTabWidget *`。

本轮继续做一层收口：

1. `BusinessManager::attachBusinessUiHost(...)` 改为接收 `IBusinessEntryHost *`
2. `BusinessManager` 内部不再保存 `FancyTabWidget` 指针，而是保存抽象 host + `hostObject()` 生命周期锚点
3. `BusinessManager` 需要的宿主能力补齐到 `IBusinessEntryHost`，至少覆盖：
   - `addBusiness(...)`
   - `setCurrentBusiness(...)`
   - `setBusinessSelected(...)`

这样主模式仍由 `FancyTabWidget` 提供实现，但 `BusinessManager` 的公开边界不再泄漏具体 widget 类型。