# Streaming 退出后 Single/Continue 未恢复可用性

## 现象

- 进入 Streaming 业务后，标题栏的 Single / Continue 输出模式按钮会被禁用。
- 退出 Streaming 后，按钮保持禁用，没有恢复可用性。

## 局部假设

- 输出模式按钮的 enabled 状态由 `MainWindow::onCurrentBusinessChanged()` 统一维护。
- `StreamingBussiness::singleTriggerEnabled()` 返回 `false`，因此进入 Streaming 时该槽会禁用按钮。
- `BusinessManager` 在业务退出且当前无新 active business 时，会发出 `currentActivedBusinessChanged(nullptr, prev)`。
- `MainWindow::onCurrentBusinessChanged()` 的 `current == nullptr && prev != nullptr` 分支只更新标题，没有恢复按钮 enabled 状态，导致按钮残留为 Streaming 的禁用状态。

## 最小修改策略

- 不改 `StreamingBussiness`。
- 在 `MainWindow` 中抽出一个统一的输出模式按钮 enabled 刷新函数。
- `current != nullptr` 时按 `current->singleTriggerEnabled()` 刷新。
- `current == nullptr` 时恢复为默认可用，避免停留在上一个业务的禁用状态。

## 验证

- 先做 CorePlugin 的窄范围编译验证，确认改动无编译错误。
- 若需要进一步运行验证，再走现有 Debug 构建和运行链路。