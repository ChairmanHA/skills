# 2026-04-10 ETH Dialog StatusLabel Layout Fix

## Goal

- 把 ETH Connect 的初始提示收敛到 `m_statusLabel`，在连接前显示提示，连接后复用为状态/错误文案。
- 修复当前弹窗出现重复 `Connect` 按钮和整体布局错乱的问题。

## Root Cause

- 当前实现同时依赖 `Controls::Dialog` 的底部按钮区和弹窗内容区自己的布局语义，视觉上产生了不符合预期的底部区域。
- 初始提示拆成两个独立 `QLabel`，而连接中/失败又单独使用 `m_statusLabel`，导致状态区语义被拆散，样式不连续。

## Plan

- 让 `m_statusLabel` 在 idle 状态显示默认网络提示，在 connecting / failed 状态显示运行时文案。
- 明确传入 `QDialogButtonBox::NoButton`，并在内容区自建唯一的 `Connect` 按钮，保持居中且避免重复按钮。
- 同步更新知识库对当前 ETH Connect UI 结构的说明。

## Validation

- 对改动文件做静态错误检查。
- 构建 Debug，确认弹窗改动可正常编译。