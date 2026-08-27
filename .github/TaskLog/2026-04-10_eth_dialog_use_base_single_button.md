# 2026-04-10 ETH Dialog Use Base Single Button

## Goal

- 按 `Controls::Dialog` 基类的标准单按钮实现恢复 ETH Connect 的底部按钮区。
- 复用 `m_statusLabel` 作为初始提示、连接中状态和失败文案的统一显示区域。

## Reasoning

- 当前 `Dialog` 基类本身就支持单按钮居中布局，没有必要在内容区额外自建按钮。
- 初始提示若拆成独立 label，会把同一个“状态区”语义拆散；用 `m_statusLabel` 统一 idle / connecting / failed 三种状态更自然。

## Plan

- `EthConnectDialog` 构造时直接通过 `Dialog` 基类创建单个 `Ok` 按钮，并把按钮文本覆写为 `Connect`。
- 在构造函数里仅重接该按钮的 clicked 行为到 `handleConnectClicked()`，避免默认关闭对话框。
- 删除内容区自建按钮逻辑，把 `m_statusLabel` 放回表单后的状态区，并在 idle 时显示网络前提提示。
- 同步更新知识库关于当前 ETH Connect UI 结构的说明。

## Validation

- 对改动文件做静态错误检查。
- 停掉正在运行的 `SGStudiod` 后构建 Debug，确认 `Core.dll` 能成功重新部署到 `plugin/`。