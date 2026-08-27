# Analog / HTRA Basic Provider Ownership

本文记录 Analog / HTRA 基础调制 provider 的当前边界，以及旧双 provider 切换机制为何已经拆除。

## 1. 当前状态

HTRA 现在是以下基础调制的唯一 provider owner：

- `AM`
- `FM`
- `Pulse`
- `Digital Ramp`
- `AWGN`

`src/plugins/htra/plugin.cpp` 在插件初始化时创建并注册这些业务。`src/plugins/analog/analogmodulationplugin.cpp` 不再创建同名 Analog business，也不再根据 Analog 许可证状态解析 HTRA provider、注销 HTRA provider、注册 Analog provider 或在两者之间切换。

因此这些基础调制的 modulation list 行为不再受 Analog vendor license provider switch 影响。许可证变化仍可影响 Analog 插件自己保留的数字类业务，但不会替换 HTRA 的 AM/FM/Pulse/Digital Ramp/AWGN。

## 2. 已删除的旧机制

旧实现曾经让 HTRA 与 Analog 同时拥有同名 provider：

1. HTRA 启动时创建并注册基础 provider。
2. Analog 许可证为 `Licensed` 时懒创建同名 Analog provider。
3. `AnalogModulationPlugin` 根据许可证状态把同名 provider 从 `BusinessManager` entry list 中来回注销/注册。
4. 两套 provider 共享 `Am_Rate`、`Fm_Deviation`、`Pulse_Width`、`Ramp_Span`、`Awgn_Bandwith` 等全局 property。

这套机制会让 inactive provider object 和 panel 继续存活，隐藏 panel 仍可能响应共享 property signal，造成重复软键盘、隐藏业务更新 modulator 状态等副作用。

当前代码已经删除这套路径：

- Analog AM/FM/Pulse/Ramp/AWGN business、panel、modulator 源文件已移除。
- Analog CMake 不再编译这些基础调制实现。
- `AnalogModulationPlugin` 不再保留 HTRA provider 指针缓存、Analog provider 指针缓存或 registered provider 指针。
- `switchProvider(...)` 及各基础调制 `switchXxxProvider(...)` 已删除。
- HTRA 基础调制 panel 不再需要为了防 hidden same-name panel 而判断 `PropertyBindingHelper::isEditTriggerFromWidget(...)`。

## 3. 维护规则

后续不要重新引入“同名 provider + 共享 property + 运行期 license switch”的模式。若某个新业务需要同时存在 vendor 与 HTRA 实现，应先明确唯一 owner，或把 provider 名称、property schema、对象生命周期和 entry host 行为拆开设计。

如果确实必须支持两个同名 provider，不能只靠 `BusinessManager::unregisterBusiness(...)` 隐藏 inactive provider；仍需处理 QObject signal/slot、panel 生命周期、property metadata 覆盖、profile 兼容和当前 entry 迁移等问题。

## 4. 验证入口

检查这条边界时，优先确认：

- `src/plugins/analog/CMakeLists.txt` 没有 AM/FM/Pulse/Ramp/AWGN 的 Analog 源文件。
- `src/plugins/analog/analogmodulationplugin.cpp` 没有 `ensureAnalog...Business`、`resolveHtraProvider` 或 `switch...Provider`。
- `src/plugins/htra/plugin.cpp` 仍直接注册 HTRA 基础 provider。
- `src/plugins/htra/*panel.cpp` 的基础调制 panel 只负责自身 property 编辑和键盘交互，不承担 provider 切换防护。
