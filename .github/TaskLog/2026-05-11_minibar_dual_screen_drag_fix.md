# MiniBar 双屏拖拽回跳修复

## 问题现象

双屏场景下，如果副屏相对主屏存在负坐标区域（例如副屏更大、上边超出主屏，或副屏位于主屏左侧），拖拽 `MiniBarWindow` 到该区域后松开鼠标，窗口会在释放时跳回主屏。

## 本地根因假设

`MiniBarWindow` 当前把 `x < 0` 或 `y < 0` 的全局坐标统一视为“非法存储点”。

这在单屏或所有屏幕都位于主屏右下区域时成立，但在 Windows 双屏布局里，合法屏幕区域本来就可能落在负坐标象限。拖拽释放后，`syncCollapsedAnchorFromCurrentGeometry()` 会拿到真实位置，但 `updateWindowGeometry()` 又会因为“负坐标非法”把锚点回退到默认位置，最终表现为窗口跳回主屏。

## 最小修改策略

1. 不再用“坐标非负”判断位置合法性。
2. 仅把内部哨兵值 `(-1, -1)` 视为“未初始化位置”。
3. 保持现有 `screenAt(...) -> fallback 当前屏/主屏 -> clamp` 的收口逻辑不变，避免扩大行为面。

## 验证口径

1. 检查拖拽释放路径仍然是 `move -> syncCollapsedAnchorFromCurrentGeometry -> persistLocalState -> updateWindowGeometry`。
2. 确认位于负坐标屏幕区域的锚点不会再被 `updateWindowGeometry()` 误判为无效。
3. 做一次针对 `minibarwindow.cpp` 的诊断检查，确保没有引入新的编译错误。