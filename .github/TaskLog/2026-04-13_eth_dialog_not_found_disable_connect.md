# 2026-04-13 ETH Dialog Not Found Disable Connect

## Goal

- 在 ETH Connect 弹窗未检测到任何本机候选接口时禁用 Connect 按钮。
- 保持“不同网段仅做软提示”的现有语义不变。

## Reasoning

- 该入口已经收敛为手工有线 ETH 连接入口。
- 当本地候选接口列表为空且界面显示 `Not found` 时，当前主机不存在可用本地链路，继续允许 Connect 只会制造错误预期。

## Plan

- 在按钮使能逻辑中增加“存在本机候选接口”的前置条件。
- 更新知识库文档，明确 `Not found` 时 Connect 会被禁用。

## Validation

- 对 `ethconnectdialog.cpp` 做静态错误检查。
- 不额外运行程序；本次改动为最小交互修正。