# 2026-04-09 Fix ETH Connect Button Flow

## Goal

- 修复 ETH Connect 弹窗点击 Connect 后没有真正触发设备连接的问题。
- 保持当前弹窗 UI 和“连接中不自动关闭”的交互语义不变。

## Root Cause

- EthConnectDialog 复用了 Controls::Dialog 的底部标准按钮。
- 标准按钮创建时会默认连接到 Controls::Dialog::onButtonClicked，并在点击后立即执行 QDialog::done(id)。
- Connect 按钮一旦先走这条默认路径，弹窗会先 finished，MainWindowDeviceController::onEthConnectDialogFinished 会先把 m_ethConnectDialog 清掉。
- 随后 EthConnectDialog::handleConnectClicked 发出的 connectRequested 到达控制器时，onEthConnectRequested 因 m_ethConnectDialog 已空而直接 return，真正的 DeviceManager::setCurrentDevice(device) 没有执行。

## Plan

- 在 EthConnectDialog 中彻底断开底部 Connect/Cancel 按钮到 Controls::Dialog 的默认 clicked 路由，再只重连到本弹窗自己的 reject / handleConnectClicked。
- 在 MainWindowDeviceController 中把 ETH 手工连接请求改成“即使弹窗指针瞬时失效，也继续执行设备连接”，仅把弹窗存在时的 UI 状态更新做成可选分支。
- 同时把 pending ETH 请求的成功/失败清理逻辑改成不依赖弹窗对象存在，避免异常路径留下脏状态。

## Validation

- 对变更文件做静态错误检查。
- 构建 Debug，确认 Core 插件相关代码可以通过编译。