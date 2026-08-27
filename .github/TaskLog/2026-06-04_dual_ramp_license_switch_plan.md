# 2026-06-04 Dual Ramp License Switch Design Note

## Goal

为 Ramp 建立一条稳定、可调试的切换模型：

1. 默认由 HTRA 提供 Ramp。
2. 只有当调制证书状态最终落到 `Licensed` 时，才切到 Analog Ramp。
3. 任一时刻 Core 内只保留一个已注册的 `Digital Ramp` entry。
4. 两套 Ramp 共用同名全局 property：`Ramp_Span`、`Ramp_SweepTime`、`Ramp_Period`。

---

## Runtime Model

### 1. Provider 角色

- `HTRA Ramp` 是默认 provider，也是 `Unknown` / `Unlicensed` 下的回退 provider。
- `Analog Ramp` 是授权后 provider。
- `AnalogModulationPlugin` 负责做 provider coordinator，因为证书状态由它持有。
- `HTRA` 不直接依赖 Analog 的 license 实现。

### 2. 状态到 Provider 的映射

- `Licensed` -> Analog Ramp
- `Unknown` -> HTRA Ramp
- `Unlicensed` -> HTRA Ramp

这里最重要的调试前提是：

- `Unknown` 不是“保持当前 Ramp 不动”的过渡态。
- 一旦从 `Licensed` 回落到 `Unknown`，Ramp 就应该立即切回 HTRA。

### 3. 单一已注册 Ramp 约束

- Core 的 entry 语义里，Ramp 不是“双 provider 常驻 + 只切 visible”。
- 当前设计要求 Core 注册集中始终只有一个 `Digital Ramp`。
- 这样可以避免同名 business 在 current/selected 恢复、名称查找、host attach replay 中出现歧义。

---

## Ownership And Lifetime

### 1. 对象所有权

- HTRA Ramp 由 HTRA plugin 持有。
- Analog Ramp 由 Analog plugin 持有。
- `BusinessManager` 只负责“是否注册到 entry host”，不负责这些可切换 Ramp 的长期所有权。

### 2. Analog Ramp 的创建时机

- `m_analogRampBusiness` 不是启动时预创建。
- 只有当状态真正进入 `Licensed` 时，Analog 才会：
  - 先对 Ramp 三个共享 property 做 get-or-create；
  - 再懒实例化 `m_analogRampBusiness`；
  - 然后把它接入 provider 切换流程。

调试时如果看到 Analog Ramp 从未出现，先检查的不是 UI，而是：

- 当前状态是否真的到过 `Licensed`
- `m_analogRampBusiness` 是否已完成懒创建
- 三个 Ramp property 是否在构造前已经存在

### 3. 关闭流程边界

- `IBusiness` 析构不应该再反向 touching `BusinessManager`。
- business 的 unregister 必须由 owner 在合适的 shutdown 阶段显式完成。
- `BusinessManager` 在退出期只能被当成注册 facade，不能被晚到的 business 析构反向依赖。

如果关闭程序时再出现 Ramp 析构崩溃，优先检查：

- plugin 是否在 `aboutToShutdown()` 中显式 unregister 自己持有的 Ramp
- 是否还有代码在 business 析构里反调 `BusinessManager`
- `BusinessManager` 的静态私有状态是否已经在析构时清空

---

## Entry Behavior

### 1. 当前设计使用纯 unregister + register

- Ramp 切换不是 in-place hot replace。
- 当前接受的设计是：旧 provider 退出注册，新 provider 重新注册。

### 2. current / selected 的预期

- 如果旧 Ramp 是当前或选中的 entry，切换后应尽量把 current / selected 迁移到新的 Ramp。
- 目标是保持“用户仍停留在 Ramp 这个概念页”，而不是跳到其他 modulation。

### 3. 已接受的取舍

- 当前位置顺序漂移是可接受的，不作为本轮问题处理。
- 也就是说，Ramp provider 切换后在 modulation list 中的位置变化，属于当前设计允许的表现。
- 当前不引入顺序保留的热替换协议，也不把“位置不变”作为调试判错标准。

调试时要区分两类现象：

- `可接受现象`：位置变化、一次性列表重建。
- `不可接受现象`：出现两个 `Digital Ramp`、切换后没有任何 Ramp、切换后 current/selected 丢失到无关页面、关闭阶段崩溃。

---

## Shared Property Contract

### 1. Canonical Keys

Ramp 共用以下三组 key：

- `Ramp_Span`
- `Ramp_SweepTime`
- `Ramp_Period`

### 2. 初始化原则

- 任何一侧在需要使用 Ramp 前，都只能先 get，再在缺失时 create。
- property 初始化不能依赖插件加载顺序。

### 3. 静态 metadata 与动态约束的边界

- plugin 启动阶段只负责通用静态 metadata，例如 unit 和 `stepStrategy`。
- 动态 min/max、`SweepTime <= Period`、`maxPeriod(span)` 这类约束，属于具体 Ramp business / modulator 的职责。

调试 shared property 问题时，先判断是：

- property 根本没创建
- property 已创建但 metadata 没补齐
- property 存在，但动态约束没有被当前 provider 刷新

---

## Analog And HTRA Constraint Alignment

### 1. 统一口径

- 当前 Ramp 约束以 HTRA 的实现口径为准。
- Analog Ramp 需要对齐 HTRA 的 `Span`、`SweepTime`、`Period` 与 sample rate 约束。

### 2. 调试重点

如果看到 Analog Ramp 和 HTRA Ramp 的行为不一致，优先比较：

- property metadata 刷新结果
- packing clamp 后的 profile
- sample rate 计算结果
- restore/reset 后 property 是否重新同步到 modulator

不要先把问题归因到 provider 切换本身；很多看似“切换异常”的现象，本质上是两边约束没有完全对齐。

---

## Debug Checklist

### 1. 如果 Analog Ramp 没切上来

检查顺序：

1. license 状态是否真的到达 `Licensed`
2. `m_htraRampBusiness` 是否已解析到当前默认 Ramp
3. `m_analogRampBusiness` 是否已懒创建
4. `switchRampProvider()` 是否实际拿到了非空 target
5. Core 注册集中是否仍只有一个 `Digital Ramp`

### 2. 如果出现两个 Ramp 或没有 Ramp

优先说明切换链路破坏了“单一已注册 Ramp”约束，应检查：

- unregister 是否成功
- register 是否成功
- `isBusinessRegistered()` 的判断是否与实际注册集一致
- host reload 后是否在使用旧映射

### 3. 如果页面选中状态不对

先区分：

- 当前 provider 是否已经正确切换
- 只是 entry host 的 current / selected 没迁移

这类问题首先看 host 侧重建和恢复，而不是先改 license 逻辑。

### 4. 如果关闭时崩溃

优先检查 owner 边界，而不是 Ramp 本身算法：

- plugin 持有对象是否在退出前显式 unregister
- 是否仍有析构函数反调 `BusinessManager`
- shutdown 阶段是否还在访问已经失效的静态 facade 状态

---

## Decision Summary

当前确认保留的设计结论：

1. `Unknown` 必须立即回退到 HTRA Ramp。
2. Analog Ramp 采用 `Licensed` 后懒实例化，而不是启动期预创建。
3. Core 内始终只允许一个已注册的 Ramp entry。
4. 当前位置变化是当前设计接受的取舍，不作为本轮修正目标。
5. 以后调试时，优先按“状态映射 -> 注册集 -> current/selected -> shared property -> shutdown owner”这条链路排查。