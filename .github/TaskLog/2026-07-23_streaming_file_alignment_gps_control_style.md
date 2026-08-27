# 2026-07-23 Streaming FileList 对齐与 GPS 控件样式

## Scope

- 调整 `StreamingPanel` 文件表格的 File Name 列对齐方式，并同步复用该表单的 minibar helper。
- 调整 GPS 对话框 XPPS 开关的尺寸策略，使其与右侧其他输入控件等宽、等高。
- 保持时间格式为现有 `Controls::ComboBox`，为该控件补上与输入框一致的局部外框和背景。
- 不修改 Streaming 文件业务、GPS 配置写回、数值键盘或实时状态逻辑。

## Evidence And Assumptions

- 观察：主 Streaming 面板和 `RemoteStreamingPanel` 分别创建表格项，当前表头与三列内容都使用居中对齐。
- 观察：`gpsinfodialog.ui` 的时间格式已经是 `Controls::ComboBox`，但 GPS 局部 QSS 只设置字号，没有设置外框。
- 观察：XPPS 开关位于与其他输入框相同的网格列中，但 `.ui` 未显式声明横向扩展和固定 35 px 高度。
- 推论：用户所说的“filename 列左对齐”同时覆盖该列表头和单元格；其他两列表头与内容保持当前居中。
- 推论：用户所说的“时间格式作为 combox”是保留现有 ComboBox 交互并补足可见外框，而不是替换成另一种控件。

## Success Criteria

1. 主 Streaming 面板和 minibar helper 的 File Name 表头、文件名单元格均左对齐。
2. Sample Count 与 Sample Rate 的表头、单元格继续居中。
3. GPS XPPS `SwitchButton` 横向填满右侧输入列，固定高度为 35 px。
4. 时间格式继续使用 `Controls::ComboBox`，深色与浅色主题下均有与输入框一致的 1 px 外框和背景。
5. 静态检查确认 CMake 包含关系不变、XML 可解析、差异无空白错误；本轮不编译或运行。

## Verification Level

- static

## Implementation And Verification Result

- 主 Streaming 面板与 minibar helper 的 File Name 表头、单元格已改为左对齐；其余两列继续通过默认表头对齐和表格项默认参数保持居中。
- XPPS 开关已显式设为横向扩展、固定 35 px 高度。
- 时间格式继续使用 `Controls::ComboBox`，GPS 深色/浅色局部 QSS 已补充与输入框相同颜色的 1 px 外框和背景。
- `gpsinfodialog.ui` XML 解析通过，深浅 QSS 花括号配对检查通过。
- CMake 包含关系静态确认未变，`git diff --check` 通过。
- 按仓库默认规则未执行编译或运行。

## XPPS SwitchButton Width Follow-up

### Observation

- 用户截图显示 `xpps_onoff` 外层位于正确的网格列中，但可见的 On/Off 按钮区域相对下方输入框左右各缩进约 10 px。
- `SwitchButton` 构造函数把内部 `QHBoxLayout` 的左右 `contentsMargins` 都设为 10 px。
- GPS 当前代码通过 `findChild<QWidget *>(..., Qt::FindDirectChildrenOnly)` 查找任意第一个直接子控件，再尝试修改其 layout；该查找不保证命中承载 On/Off 的内部容器。
- `SwitchButton::setCompactMode(true)` 是控件公开的类型安全入口，会直接把正确的内部布局左右边距设为 0，并刷新相关控件样式和几何。

### Follow-up Success Criteria

1. XPPS On/Off 可见区域与下方输入框使用相同的左右边界。
2. 不通过子对象查找或依赖 `SwitchButton` 内部对象创建顺序。
3. 保持现有 35 px 外层高度、开关状态和 GPS 配置逻辑不变。
4. 静态确认 GPS 调用公开 compact API，旧的脆弱子对象查找代码已移除。

### Follow-up Implementation And Verification Result

- `GpsInfoDialog` 已直接调用 `xpps_onoff->setCompactMode(true)`。
- 旧的任意直接子 `QWidget` 查找和手工 layout 修改已删除。
- 静态断言确认 compact setter 会把目标内部 `QHBoxLayout` 的左右边距从 10 px 改为 0。
- `gpsinfodialog.ui` 仍可正常解析，`git diff --check` 通过。
- 本轮按仓库默认规则未编译或运行。
