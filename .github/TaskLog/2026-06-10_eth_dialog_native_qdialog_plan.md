# 2026-06-10 ETH Dialog Native QDialog Plan

## Goal

- 让 EthConnectDialog 不再继承 Controls::Dialog，而是直接继承原生 QDialog。
- 在 Raspberry Pi Wayland 上使用系统标题栏，恢复系统拖拽行为，并保留现有的 moveToMainWindowTop() 调用。
- 除系统标题栏外，保持现有内容区、状态区和 Connect 按钮视觉不变。
- 保持现有业务语义：连接成功自动关闭，连接失败显示错误状态。

## Local Hypothesis

- 现有问题的根因是 EthConnectDialog 仍然是一个 frameless top-level dialog；Wayland 不会可靠接受这类窗口的客户端 move()。
- 现有 QSS 已经主要绑定在 QDialog#EthConnectDialog、QWidget#buttonBox 和按钮内部 QLabel 上；只要本地复刻这些对象结构，切回原生 QDialog 后视觉就能保持稳定。

## Plan

- 将 EthConnectDialog 的基类替换为 QDialog，并删除对 Controls::Dialog 的直接依赖。
- 在 EthConnectDialog 内部本地创建内容容器和底部 buttonBox，保留原有 objectName、属性和按钮内部 QLabel 结构，以继续命中现有主题规则。
- 保留 Linux 输入法相关 eventFilter、hideEvent、reject 和连接状态流转逻辑。
- 将 MainWindowDeviceController 中的显示入口从 runAsShow(nullptr) 调整为标准 show()，但保留 Linux 下的 dialog->moveToMainWindowTop() 调用位置不变。

## Validation

- 对改动文件做静态错误检查。
- 不额外运行构建；本次按仓库约定只做静态验证。