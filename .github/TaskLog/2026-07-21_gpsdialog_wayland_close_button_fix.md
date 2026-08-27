# GpsInfoDialog Wayland 关闭按钮修复

## Scope

- 修复 GPS 对话框切换为 Wayland hosted child widget 后，触摸关闭按钮只有震动反馈、无法关闭的问题。
- 保留标题栏鼠标/触摸拖动能力，不改变关闭按钮既有 `clicked -> QDialog::reject` 连接。

## Evidence and assumptions

- 现场观察：触摸关闭按钮有震动，但 GPS 对话框不关闭。
- 代码观察：震动由应用级 `TouchEventFilter` 在任意 `TouchBegin` 时触发，不能作为 `QPushButton::clicked` 已发出的证据。
- 代码观察：GPS Wayland 路径把整个 `TitleBar` 设置为 `WA_AcceptTouchEvents`，并在其上安装会接受并吞掉完整 Touch 序列的 `WidgetMouseMoveTool`。
- 代码观察：关闭按钮是 `TitleBar` 的 child，但未自行处理原生 Touch；因此按钮区域的 Touch 会落到接受 Touch 的标题栏拖动路径，而不是继续形成按钮所需的鼠标点击。
- 推断：应缩小原生 Touch drag handle，仅让标题 QLabel 接收原生 Touch；标题栏空白区域仍可通过鼠标事件（包括平台合成鼠标）拖动。

## Success criteria

- Wayland 触摸关闭按钮时触发其既有 `clicked -> reject`，GPS 对话框隐藏。
- 触摸标题文字区域仍可拖动 GPS 对话框。
- 鼠标标题栏拖动、鼠标关闭按钮和非 Wayland 行为不变。

## Verification level

- `static`

## Verification checklist

- [x] 标题栏不再声明接受原生 Touch，避免抢占关闭按钮触摸。
- [x] 标题 QLabel 继续接受 Touch 并由移动工具处理。
- [x] 关闭按钮原有连接和对象层级未修改。
- [x] `git diff --check` 通过。

## Verification result

- 静态验证通过：`TitleBar` 仍安装 Mouse 拖动过滤器，但不再成为原生 Touch target；其直接 QLabel 子控件继续作为 Touch drag handle。
- `m_pCloseButton` 未安装移动过滤器，原有 `QPushButton::clicked -> QDialog::reject` 连接保持不变。
- 目标文件 `git diff --check` 通过；按仓库默认规则未编译运行，需在树莓派 Wayland 实机确认触摸关闭和标题文字触摸拖动。
