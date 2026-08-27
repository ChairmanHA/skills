# Minibar 文档与记忆同步

## 2026-05-08 增量同步背景

1. 先前知识库已经完成 `RF / Center / Level / Sweep` 的同步，但 `MOD / provider` 仍停留在“占位菜单”口径。
2. 根据 2026-05-08 联机测试，minibar 现在已经可以根据插件注册与 analog license 结果动态更新 menu list。
3. 当前需要补的不是“再证明菜单该不该动态化”，而是把 entry 语义和剩余边界写清楚，避免后续把 current / selected / active / apply 混成一件事。

## 本轮同步结果

1. `ui_independent_runtime_and_minibar_design.md`
   - 新增 entry 语义说明，明确它是当前 UI host 的业务入口状态，而不是 active business 或 apply owner。
   - 更新 minibar 当前进度为：`RF / Center / Level / Sweep` 已接通，`MOD / provider` 的 business-backed 菜单模型与 license 驱动 list 更新也已完成。
2. `tx_execution_context_phase1_and_provider_migration.md`
   - 把 minibar 侧 `MOD / provider` 的状态从“占位菜单”更新为“入口层已完成”。
   - 明确当前剩余的是 provider execution context 注入与 compact panel 接线，而不是菜单模型本身。
3. `Index.md`
   - 同步索引摘要，避免继续把 minibar 的 `MOD / provider` 写成“主要未完成项”。
4. 相关 TaskLog
   - `2026-05-08_minibar_mod_business_menu_two_phase_plan.md` 更新为 phase 1/2 已完成、下一步转向 selected/current/apply 语义与 compact presenter。
   - `2026-05-08_minibar_business_menu_host_impl.md` 更新为实现切片已完成的结果记录。

## 本轮同步后仍需保留的边界

1. `MiniBarBusinessMenuHost` 已经接住 entry 层 selected/current/visible/fallback，但 `MiniBarWindow` 当前还没有把 selected business 注入 `TxSessionService`。
2. `MOD / provider` 菜单项现在只解决“显示哪些业务”和“当前按钮文案是什么”，还没有解决“点击后打开哪个 compact panel”。
3. `BusinessManager::unregisterBusiness()` 仍未补齐，这个生命周期尾项不因本轮文档同步而消失。