# MiniBarBusinessMenuHost 最小实现切片

## 当前状态

1. `MiniBarBusinessMenuHost` 已落地，并通过 `BusinessManager::attachBusinessEntryHost(...)` 接入 minibar 模式。
2. provider menu 已改为 business-backed：menu item 来自 business registration，且按 `providerKind() != None` 过滤。
3. `AnalogModulationPlugin` 的 license gating 已可通过 `BusinessManager` entry facade 直接驱动 minibar 菜单显隐与 fallback。
4. `selected/current` 在当前菜单阶段已分开建模，但用户点击菜单项时二者仍保持一致；`m_selectedProvider` 只保留为本地恢复缓存。
5. 当前未做的仍是 compact panel / presenter，以及 `MiniBarWindow -> TxSessionService::setSelectedBusiness(...)` 的 provider 执行注入。

## 这次切片实际解决了什么

1. 把 `MiniBarWindow` 的 provider 菜单来源从硬编码字符串切到 business registration。
2. 让 minibar 和 `FancyTabWidget` 复用同一套 entry 语义：selected/current/visible/fallback。
3. 让 analog license 的三态更新无需感知具体 host 类型，直接作用在当前激活 entry host 上。

## 本次故意没做什么

1. 不接 compact panel。
2. 不改 `TxSessionService` 的 provider 执行语义。
3. 不补 `BusinessManager::unregisterBusiness()`。

## 已完成的验证口径

1. minibar 启动后，provider 菜单来源不再是硬编码。
2. 根据 2026-05-08 联机测试，menu list 已能随插件注册结果与 analog license 结果动态更新。
3. 当前项被隐藏时，按钮文案和当前 business 已能按 `firstVisibleBusiness(...)` fallback。

## 下一步最小切片

1. 显式定义 minibar 的 `currentBusiness / selectedBusiness / apply-selected business` 关系。
2. 决定是否以及何时把 selected business 注入 `TxSessionService`。
3. 定义 compact presenter 接口，并选一个 analog business 做首个样板。