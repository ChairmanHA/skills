# FancyTabWidget 调制列表顶部对齐

## Scope

- 仅修正 `src/plugins/core/fancytabwidget.cpp` 中右侧调制列表 item 的垂直绘制内缩。
- 不修改 CommonPanel、主窗口中央布局、列表宽度、单双列切换、滚动条策略或业务选择行为。

## Observation

- `MainWindow` 中 `CommonPanel` 和调制列表 dock 位于同一个 `QGridLayout`，中央布局的 `5px` 顶部 margin 同时作用于两者，不会造成两者之间的相对错位。
- `CommonPanel` 的按钮行没有额外顶部 contents margin。
- `ListviewDelegate::paint()` 当前将每个 item 的绘制矩形执行 `r.adjust(3, 3, -3, -3)`，因此首行背景会在列表顶部之外再向下缩进 `3px`。
- 调制列表的 item cell 固定为 `100 x 100`，当前背景绘制高度为 `94px`，相邻行之间的总可见间隔为 `6px`。

## Inference

- 截图中首个调制 item 相对“通用设置”偏低，来源是 delegate 的顶部 `3px` 绘制内缩，而不是主窗口或 dock 的顶部 margin。

## Design

- 将 item 的垂直内缩从“顶部 `3px` + 底部 `3px`”调整为“顶部 `0px` + 底部 `6px`”。
- 保留左右各 `3px` 内缩。
- 这样首行背景从列表 viewport 顶边开始，同时背景高度仍为 `94px`，相邻行间距仍为 `6px`。

## Success Criteria

1. 首行调制 item 的背景顶边与右侧列表顶边一致，并与 CommonPanel 的“通用设置”按钮顶边处于同一水平线。
2. item 可见高度保持不变。
3. 相邻行的可见间距保持不变。
4. item 宽度、横向间距、单双列切换和滚动条计算不受影响。

## Verification

- Level: `static`
- 检查最终 diff，确认仅 TaskLog 和 delegate 绘制内缩发生变化。
- 不编译、不运行；如需像素级确认，后续在目标主题下启动界面检查首行顶边。

## Result

- 已将绘制内缩改为 `r.adjust(3, 0, -3, -6)`。
- 静态检查通过：源码 diff 仅包含该绘制矩形调整及说明注释，`git diff --check` 无错误。
