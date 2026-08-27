# SGStudio 插件元数据与加载架构

本文档说明 SGStudio 当前基于 Qt plugin 与 `ExtensionSystem` 的插件加载机制，重点覆盖以下问题：

- 插件 json 的真实作用是什么。
- json 在运行时如何参与插件发现、依赖解析与加载顺序。
- `Dependencies` 中的名称到底对应什么。
- 当前实现中与加载顺序相关的注意事项。

## 1. 总体结构

SGStudio 采用 Qt plugin 机制承载运行时插件，并通过 `ExtensionSystem::PluginManager` 做统一管理。

启动入口位于 `src/app/main.cpp`：

1. 创建 `ExtensionSystem::PluginManager`。
2. 设置插件 IID 为 `org.harogic.sgstudio`。
3. 设置插件目录为 `../plugin`。
4. 调用 `ExtensionSystem::PluginManager::loadPlugins()` 扫描并加载插件。

这意味着：

- 运行时扫描的是插件目录中的动态库文件。
- 不是单独遍历磁盘上的 json 文本文件做插件注册。
- json 只是作为 Qt plugin metadata 的一部分被编译进动态库，再由 `QPluginLoader` 读取。

## 2. json 文件如何进入插件系统

每个插件类都通过 `Q_PLUGIN_METADATA(IID ... FILE "xxx.json")` 声明其元数据文件。例如：

- Sweep 插件使用 `sweep.json`
- Core 插件使用 `core.json`
- HTRA 插件使用 `htra.json`

这里的 `FILE "xxx.json"` 不是“运行时去打开这个 json 文件”，而是 Qt 的元对象系统在构建插件时把 json 内容打包进插件库的 metadata 中。

插件系统读取流程是：

1. `PluginManager` 递归扫描插件目录，筛出动态库文件。
2. 对每个动态库创建 `PluginSpec`。
3. `PluginSpec` 内部持有 `QPluginLoader`，通过 `loader.metaData()` 读取库中嵌入的 metadata。
4. 从 metadata 的 `MetaData` 对象中解析 `Name`、`Version`、`Vendor`、`Category`、`Description`、`Dependencies` 等字段。

因此，json 是插件的“声明信息”，不是独立配置输入。

## 3. json 中各字段的作用

以 `src/plugins/sweep/sweep.json` 为例：

```json
{
    "Name": "Freq Level Sweep",
    "Version": "1.0.0",
    "Vendor": "lujiuming",
    "Category": "Sweep",
    "Dependencies": ["Core", "HTRA"],
    "Description": "The Sweep plugin for SGStudio."
}
```

当前实现中，字段作用如下。

### 3.1 Name

`Name` 是插件在 `ExtensionSystem` 中的逻辑名字。

它会被用于：

- 注册到插件哈希表。
- 作为依赖解析时的匹配键。
- 日志输出与调试定位。

这个名字应当在整个插件集合中保持唯一。

### 3.2 Dependencies

`Dependencies` 表示当前插件依赖哪些其他插件。

插件管理器在解析依赖时，会对数组里的每一项执行“按名字查找目标插件”的动作。如果找不到，就会报依赖无法解析。

它影响两个方面：

- 依赖校验：依赖不存在时，当前插件会带错误状态。
- 加载顺序：依赖插件应先于当前插件进入加载/初始化流程。

### 3.3 Version / Vendor / Category / Description

这些字段目前主要是说明性元数据：

- `Version`：版本字符串。
- `Vendor`：作者或厂商信息。
- `Category`：插件分类。
- `Description`：插件描述。

当前仓库里，它们主要用于元数据保留、日志和后续扩展，不直接参与依赖匹配。

## 4. Dependencies 里的名称到底对应什么

这是最容易混淆的点。

`Dependencies` 里的名称对应的是“目标插件 json 里的 `Name` 字段”，不是下面这些：

- 不是 `.pro` 文件名。
- 不是插件目录名。
- 不是 C++ 类名。
- 不是动态库文件名。

例如：

- Sweep 插件声明 `Dependencies: ["Core", "HTRA"]`
- 这要求系统里存在一个 `Name` 为 `Core` 的插件，以及一个 `Name` 为 `HTRA` 的插件

对应关系是：

- `src/plugins/core/core.json` 中 `Name` 为 `Core`
- `src/plugins/htra/htra.json` 中 `Name` 为 `HTRA`

因此，如果以后有人把 `core.json` 里的 `Name` 从 `Core` 改成别的值，那么所有声明依赖 `Core` 的插件都需要同步修改，否则依赖解析会失败。

## 5. 当前插件链路示例

基于现有配置，可以把几个典型插件的关系理解为：

1. Core 插件：基础框架插件。
2. HTRA 插件：设备支持插件，依赖 Core。
3. Sweep 插件：扫频业务插件，依赖 Core 和 HTRA。

所以从语义上讲，理想的加载关系应当是：

```text
Core -> HTRA -> Freq Level Sweep
```

其中：

- Core 提供基础业务框架与通用能力。
- HTRA 提供具体设备侧支持。
- Sweep 在此基础上注册扫频业务。

## 6. 插件从发现到运行的阶段

当前 `PluginManager` 的主要阶段如下：

### 6.1 发现插件

递归扫描 `pluginPaths` 下所有动态库文件，并为每个动态库构造一个 `PluginSpec`。

### 6.2 读取 metadata

`PluginSpec` 通过 `QPluginLoader::metaData()` 读取插件 metadata，并解析 json 中的字段。

### 6.3 解析依赖

插件管理器使用所有插件的 `Name` 建立哈希表，然后逐个检查每个插件声明的 `Dependencies` 是否都能在哈希表中找到。

### 6.4 生成加载队列

当前实现会根据依赖关系做一次拓扑排序，目的是保证依赖项先进入加载队列。

### 6.5 逐阶段执行

加载队列确定后，PluginManager 会依次执行：

1. `loadLibrary()`：加载插件动态库。
2. `initializePlugin()`：调用插件的 `initialize()`。
3. `initializeExtensions()`：调用插件的 `extensionsInitialized()`。
4. `delayedInitialize()`：延迟初始化阶段。

因此，json 的真正运行时作用并不是“给业务读配置”，而是影响插件管理器对该插件的身份识别、依赖合法性判断和生命周期调度顺序。

## 7. IID 的作用与当前实现状态

每个插件都通过 `Q_PLUGIN_METADATA` 声明同一个 IID：`org.harogic.sgstudio`。

按设计，这个 IID 用来区分“是不是本系统认可的插件类型”。通常插件系统会在读取 metadata 后比对 IID，不匹配则忽略。

但需要注意：当前 `PluginSpecPrivate::readMetaData()` 中的 IID 比对逻辑被注释掉了。这意味着当前实现并没有真正依赖 IID 做过滤，只是仍然要求 metadata 中存在 IID 字段。

换句话说：

- 设计目标上，IID 是插件族的身份标识。
- 当前实现里，真正决定依赖匹配的仍然是 `Name`。

## 8. 当前实现的一个顺序注意事项

`PluginManagerPrivate::resolveLoadQueue()` 先通过依赖关系生成加载队列，这一步是合理的。

但在拓扑排序之后，当前代码又对 `loadQueue.begin() + 1` 到结尾做了一次按插件名字排序。这个额外排序可能破坏原本由依赖关系得到的相对顺序。

这会带来一个风险：

- 从依赖角度，`HTRA` 应在 `Freq Level Sweep` 之前。
- 如果后续名字排序改变了两者的先后位置，就可能引入隐藏的初始化顺序问题。

当前代码是否一定出错，要看具体插件集合与名称分布，但这个排序行为本身值得明确记录，避免后续排查插件启动顺序问题时忽略它。

## 9. 维护建议

后续维护插件 metadata 时，建议遵守以下约束：

1. `Name` 一旦对外被其他插件依赖，尽量不要随意修改。
2. 修改 `Dependencies` 时，要同步检查目标插件 `Name` 是否准确。
3. 不要把 `.pro` 名称、类名、目录名误当成依赖名。
4. 如果恢复 IID 校验逻辑，要确保所有插件 IID 统一且与 `PluginManager::setPluginIID()` 一致。
5. 如果未来出现插件初始化时序异常，优先检查 `resolveLoadQueue()` 后追加的名称排序是否破坏了依赖顺序。

## 10. 一句话总结

SGStudio 的插件 json 是编译进动态库的插件元数据；其中 `Name` 定义插件在系统中的逻辑身份，`Dependencies` 依赖的是其他插件 json 的 `Name` 字段，插件管理器据此完成依赖校验和加载顺序安排。