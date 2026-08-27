---
name: labelbutton-style-workflow
description: 'Use when modifying LabelButton styles, CommonPanel dual-line buttons, or debugging RF/Sweep/General Settings visual states. Helps classify business enabled vs current-page selected vs disabled states, choose the right state carrier (checked, dynamic property, enabled), update QSS safely, and refresh the style chain correctly.'
argument-hint: 'Describe the button, the target visual state, and whether it is business-enabled, page-selected, or disabled.'
user-invocable: true
---

# LabelButton Style Workflow

## When to Use

- 修改 `LabelButton` / `InfoButton` 双行按钮样式。
- 调整 `CommonPanel` 中 `RF / MOD / Sweep / General Settings` 一类按钮的视觉语义。
- 排查“背景变了但上下两行文字没同步”或“当前页高亮覆盖了 enabled 样式”这类问题。
- 决定某个状态应该放在 `checked`、动态属性，还是 `setEnabled()` 上。

## Core Model

`LabelButton` 不是单层按钮，而是一个复合控件：

- 按钮本体负责背景与边框。
- `textLabel` 负责上半部分标题。
- `infoLabel` 负责下半部分状态字。

因此一次样式修改通常至少要检查三件事：

1. 按钮本体背景是否正确。
2. `textLabel` 颜色是否正确。
3. `infoLabel` 颜色是否正确。

## Repo-Specific State Rules

- `RF / MOD`：`checked` 表达业务 enabled / ON/OFF 语义。
- `General Settings`：`checked` 表达当前页 selected 语义。
- `Sweep`：
  - `checked` 继续表达业务 enabled 语义。
  - 当前页 selected 不可复用 `checked`，必须走单独动态属性，如 `pageHighlighted`。
- `disabled / unavailable`：走 `setEnabled(false)`；若 `infoLabel` 刷新不稳定，允许局部兜底刷新。

更完整的背景与原因见 [labelbutton state workflow](../../KnowledgeBase/labelbutton_style_state_workflow.md)。

## Procedure

### Externally-owned checked state

- 如果 `checked` 的最终 owner 是业务/property，而点击只表达导航意图，不要采用“允许 Qt 自动 toggle，再在 `clicked` 中恢复”的方式。
- 这种 toggle/revert 会产生真实的瞬时 `checked` 状态；在 Raspberry Pi 等不同绘制/合成链路上可能被单独绘制成一次错误高亮。
- 优先让该按钮覆盖 `nextCheckState()`、阻止用户点击自动翻转；业务侧的程序化 `setChecked()` 继续作为唯一状态写入口。

1. 先分类状态语义。
   - 这是业务 enabled 吗？
   - 这是当前页 selected 吗？
   - 这是控件 disabled / unavailable 吗？

2. 决定状态载体。
   - 单一业务开关态：优先用 `checked`。
   - 单一导航选中态：可用 `checked`。
   - 两种语义会并存：业务态保留在 `checked`，导航态拆到动态属性。
   - 可用性：用 `setEnabled()`，不要硬塞进 `checked`。

3. 决定状态 owner。
   - property / business 驱动的状态，由业务或 property 回写 UI。
   - 当前页 selected，由 `MainWindow` / 页面切换逻辑统一回写。
   - 点击通常只表达意图，不直接拥有最终视觉状态。

4. 更新代码状态链。
   - 若程序化调用 `setChecked()`，同步刷新按钮本体、`textLabel`、`infoLabel`。
   - 若使用动态属性，设置属性后同样做样式刷新。
   - 若是 `setEnabled(false)`，确认 `infoLabel` 在本仓库当前实现下是否还需要局部兜底。

5. 更新 QSS。
   - 同时检查深色与浅色主题。
   - 同时覆盖按钮本体、`textLabel`、`infoLabel`。
   - 明确优先级：谁覆盖谁，不能靠偶然的规则顺序碰运气。

6. 验证三类切换。
   - 初始状态是否正确。
   - 点击 / 页面切换后的视觉是否正确。
   - 程序化状态回写后，子 label 是否同步刷新。

7. 如果发现新模式，回写文档。
   - 流程性结论更新 skill。
   - 原理性结论更新 KnowledgeBase。
   - 本次任务设计和边界写入 TaskLog。

## Minimal File Checklist

- `src/libs/controls/infobutton.cpp`
- `src/libs/controls/labelbutton.h`
- `src/libs/controls/labelbutton.cpp`
- `src/plugins/core/commonpanel.h`
- `src/plugins/core/commonpanel.cpp`
- `src/plugins/core/mainwindow.cpp`
- `configuration/theme.css`
- `configuration/theme_light.css`

## Expected Output

- 状态语义清晰，不混用。
- 深浅主题都同步更新。
- 样式刷新链完整，避免“按钮变了但文字没变”。
- 若涉及新规则，TaskLog 与 KnowledgeBase 一起补齐。
