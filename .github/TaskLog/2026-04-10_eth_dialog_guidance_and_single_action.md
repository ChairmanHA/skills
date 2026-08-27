# 2026-04-10 ETH Dialog Guidance And Single Action

## Goal

- 在 ETH Connect 弹窗里明确提示默认直连部署约束，避免用户把目标 IP 误认为任意可改。
- 去掉底部 Cancel，保留右上角关闭按钮，底部只保留一个居中的 Connect。

## Reasoning

- 当前主场景是 Windows 主机与树莓派网线直连，设备默认地址仍是 `192.168.1.100:5000`。
- 用户当前最大的误解风险不在输入格式，而在网络前提：主机以太网 IPv4 必须落在同一网段，否则填任何 IP 都无法建立通路。
- 关闭动作已有标题栏 CloseButton；底部再放一个 Cancel 只会增加干扰，不增加实际能力。

## Plan

- 在 `EthConnectDialog` 表单上方增加说明文本，明确“默认地址不是随便改的”和“主机必须同网段可达”的前提。
- 把底部按钮改成单个 `Connect`，让 `Controls::Dialog` 自动居中布局。
- 保留连接中禁止关闭的 `reject()` 语义。
- 同步更新知识库中的当前 UI 交互描述。

## Validation

- 对变更文件做静态错误检查。
- 构建 Debug，确认 Core 插件相关代码可正常编译。