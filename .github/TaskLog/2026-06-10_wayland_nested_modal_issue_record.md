# 2026-06-10 Wayland Nested Modal Issue Record

## Goal

- 记录 `PxSaveFileDlg -> InputDialog` 这条链路在 Wayland 下的未解决问题。
- 把当前调查结论沉淀到 KnowledgeBase，避免后续继续按按钮级或单类补丁误判根因。

## Current Finding

- 用户已删除 `SaveFileDlg` 中的“新建文件夹”按钮，这条入口当前不再保留。
- 现象不是单个按钮、单个回调或单处显式 `hide()` 导致，而是 `modal dialog` 下再弹 `modal dialog` 的窗口协议问题。
- 在 `PxSaveFileDlg` 已处于模态状态时，再弹 `InputDialog`，外部点击会让内层弹窗视觉上消失，但输入状态没有正常回到应用，表现为整应用鼠标不可用。

## What To Record

- 复现链路与相关代码位置。
- 已尝试但未收敛的修补方向。
- 当前更可信的结论：Wayland 下自定义无边框顶层对话框的嵌套模态管理不可靠。
- 当前工程决策：先移除入口，后续按架构问题处理。

## Deliverables

- 新增一篇 KnowledgeBase 文档，明确这是“未解决问题记录”，不是已验证修复方案。
- 更新 KnowledgeBase 索引。