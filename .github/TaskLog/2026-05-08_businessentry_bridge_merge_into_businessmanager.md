# BusinessEntryUiBridge 并入 BusinessManager

## 本轮目标

1. 保留 `IBusinessEntryHost`，但从现有桥接头中抽出为独立头文件。
2. 删除 `BusinessEntryUiBridge`，把其 facade 能力合并到 `BusinessManager`。
3. 将 `attachBusinessUiHost` / `detachBusinessUiHost` 重命名为 `attachBusinessEntryHost` / `detachBusinessEntryHost`。
4. 删除 `CoreRuntimeServices` 对 bridge 的持有和 getter。
5. 让 Analog 插件直接依赖 `BusinessManager` 的 entry facade。
6. 只做最小静态修改与最小 Core 编译验证，不做运行验证。

## 当前局部判断

当前代码里 `BusinessManager` 与 `BusinessEntryUiBridge` 分别维护了同一份激活 `IBusinessEntryHost` 的生命周期与方法转发，已经出现重复 owner：

1. `BusinessManager` 负责业务注册重放和内部 `requestSelectBusiness(...)` 入口。
2. `BusinessEntryUiBridge` 负责对外暴露 `selected/current/visible/fallback` 语义和 `hostChanged`。

在当前“单激活宿主”模型下，这两组职责可以收口到 `BusinessManager`，前提是继续保持 `activedBusiness` 与 entry 语义分离。

## 实施边界

本轮不做：

1. 不改 `TxSessionService` 语义。
2. 不补 `BusinessManager::unregisterBusiness()`。
3. 不改 minibar compact panel。

本轮只做：

1. 抽出 `IBusinessEntryHost` 独立头文件。
2. 把 bridge 的 facade API 和 `hostChanged` 信号并入 `BusinessManager`。
3. 改主窗体、minibar、analog 的调用点。
4. 从 Core 构建系统中移除 bridge 文件。

## 便宜的判别检查

如果这次收口是闭合的，那么：

1. `Core` 目标最小编译应通过。
2. 工作区内不应再残留 `BusinessEntryUiBridge` 或 `businessEntryUiBridge()` 的真实代码引用。
3. `MainWindow` / `MiniBarWindow` 只需 attach 一次 `BusinessManager`。

## 后续收口

在 bridge 删除后的下一轮整理里，`BusinessManager` 对外暴露的 entry facade 命名应继续与 `attachBusinessEntryHost(...)` 对齐，避免和 `activedBusiness()` 或普通 UI host 概念混淆：

1. `hasUiHost()` 应收口为明确的 entry host 命名。
2. `selected/current/visible/fallback` 这一组 facade 应加上 entry 前缀，明确它们描述的是“入口宿主语义”，不是业务运行态。
3. `hostChanged` 信号也应同步收口为 entry host 语义，并更新 KnowledgeBase 对 `BusinessManager` 单 owner 模型的说明。