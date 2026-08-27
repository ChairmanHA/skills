# MessageDialog 固定居中与首次显示无闪烁

## Scope

- 修复 aarch64 Raspberry Pi Wayland 上 hosted `MessageDialog` 首次显示时，窗口/关闭按钮可能先在屏幕左上角绘制、随后再移动到中央的闪烁。
- `MessageDialog` 作为纯模态提示固定居中，不再允许标题栏拖动。
- 保留上一轮建立的同-host overlay 层级，确保错误提示仍位于 About 和文件对话框上方。
- Win32 保持现有 top-level、modal、topmost 和居中语义。

## Verification Level

- `static`

## Observation

- `WaylandDialogMoveTool` 在 `QEvent::Show` 的 event filter 中先创建 overlay 并 raise hosted dialog。
- 随后 `Controls::Dialog::showEvent()` 调用 `moveToCenter()`。
- `moveToCenter()` 在调整尺寸与移动之前先调用 `qApp->processEvents()`；因此已被 raise、初始位置仍为 `(0, 0)` 的 hosted dialog 有机会先绘制一帧。
- 仅停止安装 `WidgetMouseMoveTool` 不能消除闪烁，因为首次位置仍未在 `Show` 前确定。
- `Controls::TitleBar` 自身也实现了鼠标拖动，因此要让 MessageDialog 真正固定，还需要关闭这条通用拖动路径。
- `MessageDialogPrivate::setMessage()` 当前通过 0 ms timer 延后文本尺寸计算；在 `open()/exec()` 前还没有稳定的最终几何。

## Minimal Design

1. 为 `Controls::Dialog` 增加窄接口 `setTitleBarMovable(bool)`：
   - 默认保持 `true`，不改变任何现有对话框。
   - `TitleBar` 在 mouse press/move 时检查该标志。
2. 为 `WaylandDialogMoveTool` 增加默认开启的 `enableDragging` 构造参数：
   - 现有调用点使用默认值，Open/Save/About 等行为不变。
   - MessageDialog 传 `false`，不安装额外的 Wayland mouse/touch move tool。
3. MessageDialog 构造时调用 `setTitleBarMovable(false)`，从 Win32 与 Wayland 两边都关闭标题栏拖动。
4. hosted MessageDialog 内容设置时同步完成文本、高度布局；按钮创建后在展示准备阶段完成最终尺寸：
   - hosted 路径不再保留 show 后的 deferred geometry mutation。
   - Win32 top-level 路径保留既有 0 ms 延迟布局，并补充 QObject context。
5. 在 `runAsync()/exec()` 之前，如果 MessageDialog 已被转换为 hosted child，则按 parent 本地矩形预先居中并设置 `WA_Moved`。
6. 不修改 overlay 的创建、raise、销毁顺序，不增加全局 restack 或层级管理器。

## Success Criteria

- aarch64 Wayland：MessageDialog 第一次可见时已经位于 host 中央，不出现左上角关闭按钮/窗口闪帧。
- MessageDialog 的标题栏鼠标与触摸拖动均不再移动窗口；关闭按钮仍可点击。
- About < file overlay < file dialog < error overlay < MessageDialog 的顺序不变。
- 关闭 MessageDialog 后，原文件对话框恢复交互且无残留 error overlay。
- Open/Save 等其他使用 `WaylandDialogMoveTool` 的窗口仍保持原有拖动能力。
- Win32 MessageDialog 仍使用原有顶层窗口路径，只新增“固定不可拖动”的产品语义。

## Static Verification

- [x] `Dialog` 的 movable 默认值为 true，只有 MessageDialog 显式关闭。
- [x] `WaylandDialogMoveTool` 保留原二参构造，并以三参重载选择关闭拖动；现有调用点无需修改。
- [x] MessageDialog 的 helper 调用关闭额外拖动，但仍启用 modal overlay。
- [x] MessageDialog 在 `open()/exec()` 之前完成 hosted 本地坐标居中。
- [x] hosted `showEvent()` 不进入含 `processEvents()` 的通用居中路径。
- [x] `Show` 时 overlay 仍先 raise，MessageDialog 后 raise。
- [x] Win32 helper 仍为 no-op；现有 flags/modality/callback 未改变。
- [x] `git diff --check` 通过。
