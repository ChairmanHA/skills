# TitleBar 菜单组与工具组样式修正

## Scope

- 调整主窗口 TitleBar 的中部布局和入口归属。
- 菜单组只包含 `File / Device / System` 三个弹出式 `QMenu` 入口。
- 工具组包含 `Preset / Single / Continue / Screenshot / MiniBar` 五个入口。
- 同步所有受支持配置档案的深色、浅色 QSS。
- 不修改菜单内容、输出模式状态语义、截图逻辑、MiniBar 启动逻辑、窗口控制按钮或 menubar overflow/touch 处理。

## Observation

- `titlebar.ui` 的 `menubarLayout` 当前直接排列 `QMenuBar` 和 `outputModeLayout`，两者之间只有 `30px` layout spacing，没有明确分隔符。
- `File / Device / System` 是带 `QMenu` 的顶层菜单入口；`Preset` 是普通 `QAction`，但当前也被添加到了 QMenuBar。
- `Single / Continue / Screenshot / MiniBar` 已经位于 `outputModeWidget` 的同一个 `QHBoxLayout` 中，layout spacing 为 `8px`。
- QMenuBar item 的左右 padding 当前均为 `14px`，三项菜单的视觉间距偏大。

## Design

1. 在 `menubarLayout` 中加入 `QFrame#menuToolSeparator`：
   - 固定宽度 `2px`；
   - 固定高度 `22px`；
   - 位于 QMenuBar 与 `outputModeLayout` 之间；
   - `menubarLayout` 的组间 spacing 调整为 `12px`。
2. 将所有主题中的 QMenuBar item 左右 padding 从 `14px` 调整为 `10px`。
3. 在 `outputModeWidget` 中创建 `btnPreset`，并放在 `btnSingle` 之前。
4. `btnPreset` 继续复用 `ACTION_PRESET`；按钮点击触发同一个 QAction，QAction 再调用现有 `MainWindow::preset()`。
5. 不再把 `ACTION_PRESET` 添加到 QMenuBar。
6. 在同一个 `outputModeWidget` 内、`btnScreenshot` 左侧加入 `QFrame#toolIconSeparator`，形成 `Preset / Single / Continue | Screenshot / MiniBar` 的视觉分区。
7. `btnPreset / btnSingle / btnContinue` 共享文本工具按钮样式；五个工具入口和内部视觉分隔符继续使用同一 layout 的统一 `8px` 间距。

## Assumptions

- “等间距”指相邻按钮点击区域之间使用同一个 layout spacing，而不是强制五个宽度不同的控件中心点等距。
- 参考图中的分隔线用于区分“弹出菜单”和“直接操作工具”，不属于任何 QMenu 的内容。
- 工具组内部第二道分隔线仅区分文字入口与图标入口；它不改变五个工具入口属于同一组的结构语义。

## Success Criteria

1. TitleBar 可见顺序为：`logo | File Device System | separator | Preset Single Continue | separator | Screenshot MiniBar | stretch | window controls`。
2. `File / Device / System` 视觉间距比当前略小。
3. 工具组五个入口由同一个 layout 以统一间距排列。
4. `Preset` 不再占用 QMenuBar item/overflow，但仍能触发现有 preset 行为，也仍可由 ActionManager 外部访问。
5. 深色、浅色以及所有配置档案的按钮和分隔符样式一致。
6. menubar popup、overflow 图标、Wayland touch 防重开逻辑不变。

## Verification

- Level: `static`
- 检查 UI XML、MainWindow action 绑定和所有主题 QSS 的一致性。
- 执行 `git diff --check`。
- 按仓库默认不编译、不运行。

## Result

- 已完成 `File / Device / System | Preset / Single / Continue | Screenshot / MiniBar` 的视觉编组。
- `Preset` 的可视入口已迁入 `outputModeWidget`，并继续复用 `ACTION_PRESET`。
- 菜单/工具边界和工具组内部边界最终均使用 `2 x 22` 的明暗双边 `QFrame`。
- 五套配置档案的深色、浅色 QSS 已同步。
- `uic` 对 `titlebar.ui` 的静态解析通过。
- 十个主题文件均命中 `menuToolSeparator`、`toolIconSeparator` 和 `btnPreset` 样式；`git diff --check` 通过。
- 未编译、未运行。

## Runtime Feedback Follow-up

用户运行后的截图确认：

- `1px` 纯色分隔线过硬，参考样式更接近具有明暗双边的细分隔槽。
- 相同 layout spacing 不等于相同视觉间距；`44px` 图标按钮承载 `28px` 图标时，两个图标的可见内容明显比文字按钮更靠近。

修正设计：

1. 两个分隔符从 `1 x 22` 调整为 `2 x 22`。
2. 深色主题使用“左侧中灰 + 右侧深色”，浅色主题使用“左侧中灰 + 右侧高光”的双边 border，形成轻微凹槽效果。
3. `btnPreset / btnSingle / btnContinue` 的左右 padding 从 `10px` 调整为 `8px`。
4. `btnScreenshot / btnShowPanel` 的固定槽位宽度从 `44px` 调整为 `64px`，图标仍保持 `28px`。
5. QMenuBar item 的 `10px` padding 和所有 layout spacing 保持不变；不同控件通过各自的内部留白校准可见内容间距。

## Compact Spacing Follow-up

用户要求在保持统一视觉节奏的前提下，整体进一步接近参考图的紧凑间距：

- QMenuBar item 左右 padding：`10px -> 8px`。
- 菜单组与工具组的 layout spacing：`12px -> 10px`。
- 工具组内部 layout spacing：`8px -> 6px`。
- 文字工具按钮左右 padding：`8px -> 6px`。
- 图标按钮槽位：`64px -> 60px`；保留 `28px` 图标，避免退回原 `44px` 槽位造成的图标过近。
- `2 x 22` 明暗双边分隔符保持不变。

静态复核：

- `uic` 解析 `titlebar.ui` 通过。
- 十个主题文件均已应用 `8px` 菜单 padding、`6px` 文字工具 padding和 `60px` 图标槽位。
- `git diff --check` 通过；未编译、未运行。
