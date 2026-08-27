# 2026-03-17 StepSweep Restore Original Mutex Semantics

## Goal
- 恢复 StepSweep 作为 `BusinessManager` 中普通互斥业务的原始语义。
- 恢复 `RF == true` 且 StepSweep enabled 时，`selectBusiness2Work()` 选择并激活 `StepSweepBusiness` 的行为。
- 恢复 `CommonPanel::ui->sweep` 与 `FancyTabWidget` 普通业务高亮之间的互斥关系。
- 保留当前通过 `MainWindow` 将 `StepSweepPanel::enabledChanged(bool)` 镜像到 `CommonPanel::setSweepHighlighted(bool)` 的装配方式，前提是该镜像最终仍服从旧的互斥链。

## Current Regression Source
- 当前回归点来自 `IBusiness::participatesInHighlightMutex()` / `StepSweepBusiness::participatesInHighlightMutex() == false`。
- 由于 `TabWidget` 不再为 StepSweep 建立 `Panel::enabledChanged -> TabWidget::setSelected()` 连接：
  - StepSweep enabled 不再进入 `FancyTabWidget::selectedBusiness()`。
  - `selectBusiness2Work()` 不再能在 `RF == true` 时优先激活 StepSweepBusiness。
  - `FancyTabWidget` 普通业务高亮与 `CommonPanel::sweep` 高亮不再互斥。

## Restore Strategy
1. 保留 `showInFancyTabWidget() == false`，继续让 StepSweep 隐藏在列表之外。
2. 恢复 StepSweep 参与 `FancyTabWidget` 的互斥高亮链。
3. 让 `StepSweepPanel::enabledChanged(bool)` 再次驱动 `TabWidget::setSelected()`。
4. 依赖原有 `FancyTabWidget::onBusinessSelectedStateChanged()` 互斥清理前一个业务。
5. 依赖 `TabWidget::setSelected(false)` -> `Panel::setBtnEnabledChecked(false)` -> `enabledChanged(false)` -> `CommonPanel::setSweepHighlighted(false)` 完成 SWEEP 高亮熄灭。

## Expected Behavior After Restore
- 启用 StepSweep：
  - `CommonPanel::sweep` 高亮。
  - `FancyTabWidget::selectedBusiness()` 变为 StepSweepBusiness。
  - 若之前 AM/FM 已高亮，则其高亮被清除。
  - `RF == true` 时，`selectBusiness2Work()` 激活 StepSweepBusiness。
- 启用 AM/FM：
  - 之前若 StepSweep 已 enabled，则其 panel enabled 被关闭。
  - `CommonPanel::sweep` 高亮熄灭。
  - 新业务进入互斥高亮位。

## Validation Focus
- UI：SWEEP 与 FancyTabWidget 普通业务高亮互斥。
- Business：`selectedBusiness()` 在 StepSweep enabled 时返回 StepSweepBusiness。
- Activation：`RF == true` 且 StepSweep enabled 时，active business 为 StepSweepBusiness。
- Device chain：不改 `StepSweepBusiness::startBusiness()/stopBusiness()/onDeviceProfileChanged()`，只恢复其重新进入原始选择链。