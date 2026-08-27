# OFDM SwitchButton 样式问题修复计划

## 背景
- OFDM 面板中 `Null DC` 与 `Windowed` 这两个 `SwitchButton` 点击后，标题 label 左右边缘出现高亮残影。
- 同面板中的 `enabled` 使用同类控件，但没有该残影。

## 当前局部事实
- `SwitchButton` 继承 `InfoButton -> AbstractButton -> QAbstractButton`，外层控件本身会按 push button 方式绘制。
- `ofdmpanel.ui` 中 `btnNullDC`、`btnWindowed` 声明了 `checkable=true`，而 `enabled` 没有。
- 深色主题存在全局 `.QPushButton:pressed, .QPushButton:checked` 规则，且 `SwitchButton` 外层未单独屏蔽 checked/pressed 视觉。
- `SwitchButton QWidget { background-color: #171a1f }` 会覆盖内部子 QWidget，外层一旦进入 checked/pressed 态，边缘残留更容易可见。

## 假设
- 根因是外层复合控件 `SwitchButton` 被错误地参与了 checked/pressed 视觉状态；点击内部 On/Off 后，外层按钮的 push-button 绘制或样式边框残留从子控件边缘漏出。

## 方案
1. 从控件实现层将 `SwitchButton` 外层固定为非 checkable，明确状态只由内部 `btnOn/btnOff` 承载。
2. 在深浅主题中给 `SwitchButton` 外层补显式样式兜底，屏蔽外层 pressed/checked 的背景与边框残影。
3. 仅在必要时再清理 `ofdmpanel.ui` 中多余的 `checkable=true`，但优先保证控件级修复可覆盖全仓库。

## 验证
- 检查修改后的编译错误。
- 静态确认 `enabled / btnNullDC / btnWindowed` 三者行为仍由 `statusChanged` 驱动，未引入外层 checked 依赖。
- 如可行，再做一次 Debug 编译验证改动未破坏 controls/analog 相关构建。
