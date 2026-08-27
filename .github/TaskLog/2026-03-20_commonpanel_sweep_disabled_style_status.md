# 2026-03-20 CommonPanel Sweep Disabled Style Status

## Background
- 目标问题：`CommonPanel` 中 `SWEEP` 按钮在暗色主题下，启动初始状态与运行期真正 `disable` 状态的文字颜色表现不一致。
- 期望行为：
  - 程序启动时，如果 `SWEEP` 视觉上应保持普通未选中态，不应被错误地渲染成 disabled 灰色。
  - 当 `SWEEP` 确实进入 `setEnabled(false)` 场景时，内部 label 应可靠切换到 disabled 颜色。
- 相关文件：
  - `plugins/core/commonpanel.cpp`
  - `configuration/theme.css`
  - `configuration/theme_light.css`
  - `libs/controls/labelbutton.cpp`
  - `plugins/core/mainwindow.cpp`

## Current Code State
- 当前没有继续尝试在全局 QSS 中彻底修复该问题。
- 当前采用的是局部 workaround：在 `CommonPanel::setSweepButtonEnabled(bool enabled)` 中，直接对 `ui->sweep->label()` 设置内联样式。
- 当前逻辑：
  - `enabled == true` 时，清空 `infoLabel` 的内联样式，恢复全局 QSS 接管。
  - `enabled == false` 时，根据 `ThemeManager` 当前主题，直接设置 `infoLabel` 的颜色。
- 当前设置的 disabled 颜色：
  - Dark: `#949494`
  - Light: `#6b6b6b`
- 这只是规避方案，不是根因修复。

## Observed Behavior

### 1. 仅通过 QSS 增加 disabled 规则时，启动初始状态会出错
- 在 `theme.css` 中加入如下规则后：

```css
Core--Internal--CommonPanel LabelButton#sweep:disabled QLabel#infoLabel {
    color: #6b6b6b;
}
```

- 用户实验确认：
  - 如果不加这条规则，程序启动后的 `SWEEP` 样式是正确的。
  - 一旦加上这条规则，程序启动时 `SWEEP` 的 `infoLabel` 会错误显示成 disabled 灰色。

### 2. 仅调整 QSS 选择器优先级/顺序不能同时满足两个场景
- 尝试过的方向：
  - 通过规则顺序，让 `[parentChecked="false"]` 覆盖 `:disabled`。
  - 给 sweep 的 `[parentChecked="false"]` 增加 `:enabled`，与 `:disabled` 互斥。
- 结果：
  - 可以修正启动时的错误样式。
  - 但当按钮真正进入 disable 状态时，颜色仍然不会稳定刷新到目标 disabled 颜色。
- 结论：问题不只是 QSS 规则冲突，还涉及运行期状态切换后的样式刷新链。

### 3. 当前 workaround 已经可以稳定让真正 disabled 的 sweep 变色
- 通过直接给 `ui->sweep->label()` 设置内联 `styleSheet`，禁用场景颜色能够稳定生效。
- 这说明问题的核心不是颜色值本身，而是 Qt 样式表在该控件层级上的状态传播/刷新不可靠。

## Verified Technical Findings

### A. `SWEEP` 是 `LabelButton`，其下方文字来自 `infoLabel`
- `CommonPanel` 中：
  - `ui->sweep->setLabelText(tr("Sweep"));`
  - `ui->sweep` 是 `LabelButton`，不是普通 `QPushButton`。
- `LabelButton` 内部：
  - `m_label->setObjectName("infoLabel")`
  - 因此 QSS 里的 `QLabel#infoLabel` 实际命中的是 `LabelButton::m_label`。

### B. `LabelButton` 只在 checked/theme 变化时主动刷新子 label 样式
- `libs/controls/labelbutton.cpp` 中：
  - `LabelButton::onCheckedStateChanged(bool checked)` 会对子控件 `unpolish/polish/update`。
  - `LabelButton::onThemeChanged(...)` 也会调用 `onCheckedStateChanged(isChecked())`。
- 但当前没有看到专门针对 `enabled/disabled` 切换的对应刷新逻辑。

### C. `MainWindow` 在初始化阶段会多次改变 sweep 的可用性/上下文状态
- `MainWindow::onInitializationDone()` 中会调用 `updateSweepAndBusinessAvailability()`。
- `loadSettingsFile()` 之后，`FancyTabWidget::selectedBusiness()` 可能发生变化。
- `updateSweepAndBusinessAvailability()` 会再调用 `m_commonPanel->setSweepButtonEnabled(...)`。
- 因此启动期的视觉异常，不是“静态样式加载一次”那么简单，而是“初始化期间状态多次变化 + QSS 重新计算不稳定”的复合问题。

## Probable Root Cause
- 当前高度怀疑根因在下面两点之一，或者两者叠加：

### 1. 父控件 disabled 状态到子 label 的样式传播，不足以稳定触发目标 QSS 规则
- `LabelButton#sweep:disabled QLabel#infoLabel` 命中依赖祖先伪状态。
- Qt 在该层级结构下，可能不会在 `setEnabled(false)` 后稳定对子 `infoLabel` 做期望的样式重算。
- 这与 `checked` 状态不同，因为 `checked` 路径已经有 `LabelButton::onCheckedStateChanged()` 主动刷新兜底。

### 2. 启动阶段存在样式求值时机问题
- 主题文件加载、`themeChanged` 信号、`refreshAllWidgets(this)`、`loadSettingsFile()`、`selectedBusiness` 变化、`setSweepButtonEnabled()` 调用，这些动作发生在非常接近的初始化窗口内。
- 某些时机下，`infoLabel` 可能在祖先状态尚未稳定或尚未完成重新 polish 时就被渲染，从而留下错误首帧状态。

## Why The Current Workaround Works
- 对 `ui->sweep->label()` 直接设置内联 `styleSheet`，优先级高于全局 QSS。
- 这样可以绕过：
  - 祖先 `:disabled` 伪状态传播是否可靠
  - 子控件何时重新 polish
  - `parentChecked` / `:disabled` 规则之间的竞争
- 代价是：
  - 这是一处局部硬编码
  - 仍然把主题颜色知识写在了 `CommonPanel` 里
  - 没有解决 `LabelButton` 在 enabled 变化时缺少统一刷新机制的问题

## Suggested Proper Fix Direction

### Direction 1. 在 `LabelButton` 层补齐 enabled/disabled 状态变化的统一刷新机制
- 目标：让 `LabelButton` 在自身 `enabled` 状态变化时，也像 `checked` 状态变化一样，对 `m_label` / `m_textLabel` 做一次统一 `unpolish/polish/update`。
- 可能的入口：
  - 重写 `changeEvent(QEvent *event)`，关注 `QEvent::EnabledChange`
  - 或者在更靠近 `InfoButton/LabelButton` 的层统一处理
- 这是最值得优先验证的方向。

### Direction 2. 避免依赖祖先 `:disabled` 选择器，改成显式动态属性
- 思路：
  - 在 `LabelButton` 或 `CommonPanel` 中给 `infoLabel` 设置类似 `parentEnabled` / `buttonDisabled` 的动态属性。
  - QSS 直接使用 `QLabel#infoLabel[parentEnabled="false"]` 这类规则。
- 这样可以减少 Qt 对祖先伪状态传播的依赖。

### Direction 3. 如果未来继续保留局部 override，应该把颜色抽到 ThemeManager
- 当前 workaround 里直接写了 hex 颜色。
- 如果后续仍需局部 override，建议在 `ThemeManager` 中增加类似 `labelButtonDisabledTextColor()` 的统一接口，避免颜色散落在业务代码里。

## Not Recommended
- 不建议继续只在 `theme.css` / `theme_light.css` 中反复通过顺序、优先级、`:enabled`、`:disabled` 组合做试错。
- 原因：现有现象已经说明，问题不仅是选择器冲突，更多是 Qt 对该控件层级的运行期样式刷新行为不稳定。

## Current Handoff Summary
- 现状：用户可见问题已通过 `CommonPanel` 局部内联样式 workaround 暂时规避。
- 根因：未根治。
- 最可能的正确修复点：`LabelButton` 对 `EnabledChange` 缺少与 `CheckedChange` 对应的统一子控件样式刷新。
- 后续继续开发时，建议优先验证 `LabelButton` 的 `changeEvent(QEvent::EnabledChange)` 方案，而不是继续堆叠 QSS 规则。