# Wayland overlay 与系统键盘协议边界分析

## 已验证事实

- `TouchNumKeyboard` 改成 host 内子控件 + overlay 后，在 Wayland 下拖拽问题得到解决。
- `Controls::Dialog` 基类整体改成相同 hosted child dialog 模式后，普通 `QLineEdit` 场景中的树莓派系统键盘失效。
- 将 `Dialog` / `Keyboard` / `EthConnectDialog` 回退到 `948b78a14fe5d1033cf4f96dad0aa793715e568f` 对应实现后：
  - `TouchNumKeyboard` 仍正常。
  - `EthConnectDialog` 中的 Wayland 系统键盘恢复正常。

## 当前结论

- 不能简单把“系统键盘也是 overlay”理解成“所以应用内所有 dialog 也应该统一成 overlay child widget”。
- 在 Raspberry Pi Wayland (`labwc + wf-panel-pi + squeekboard`) 下，系统键盘更接近 compositor / input-method 协议一侧的独立 layer-shell / input-method surface，而不是应用内部普通 QWidget overlay 的同类物。
- 应用内 hosted child dialog overlay 解决的是“顶层 frameless 窗口 move() 不可靠”的问题；系统键盘依赖的是“当前 top-level 可交互文本输入上下文是否被 Wayland 输入法协议正确识别”。

## 设计边界

- `TouchNumKeyboard`：属于应用内部自绘工具面板，适合 host + overlay child widget 模式。
- 普通 `QLineEdit` + 树莓派系统键盘：更适合保持为正常 top-level dialog / window 输入链，不要轻易改成 hosted child dialog，否则可能破坏 text-input 上下文。

## 将写入知识库的重点

1. overlay child dialog 适用范围只覆盖“应用内自绘交互面板”。
2. 对依赖系统输入法 / 系统键盘的普通文本输入对话框，优先保持标准 top-level dialog 输入链。
3. `Visible=true` 但键盘未实际显示，不足以证明只是 z-order 遮挡，更可能是 text-input / seat focus / layer-shell 侧没有建立有效输入上下文。