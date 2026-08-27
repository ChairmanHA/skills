# 2026-04-03 DeviceSetting Toggle Style Fix

## Problem

- `DeviceSettingPanel` 里的 `SystemClockOut` / `TriggerOutState` 使用 `LabelButton` 显示 `ON/OFF`。
- `LabelButton` 的子文本颜色依赖 `toggled(bool)` 触发的 `onCheckedStateChanged()` 来刷新 `parentChecked` 动态属性。
- 当前 `DeviceSettingPanel::updateToggleButton()` 在程序化同步状态时用 `blockSignals(true)` 包住了 `setChecked()`。
- 结果是 preset/load 等非鼠标路径下，按钮 checked 状态和文字能更新，但子标签颜色不会刷新；手动点击后因为走了正常 toggled 链，样式又恢复正常。

## Fix Plan

- 去掉 `updateToggleButton()` 中对 `setChecked()` 的信号屏蔽，让 `LabelButton` 内部的 `toggled -> onCheckedStateChanged()` 样式刷新链正常执行。
- 保持 `clicked` 到 property 的写回逻辑不变，因为程序化 `setChecked()` 不会发出 `clicked()`，不会产生回写环。

## Expected Result

- preset、load、device writeback 等程序化状态切换后，`Trigger Output` 和 `RefCLKOut` 的 `ON/OFF` 文本颜色都与 checked 状态一致。