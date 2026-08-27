# ListMode Cell 软键盘锚点修复

## 目标

修复 ListMode 频表中编辑任意 cell 时软键盘总在同一固定位置弹出的问题，使键盘位置基于正在编辑的 cell，而不是整个 `QTableView`。

## 观察与推断

- `ListModePanel::popupNumKeyBoard()` 当前对频率、功率、驻留时间三列都调用 `prepareNumericKeyBoard(ui->table, cfg)`。
- `TouchNumKeyboard` 构造时把传入 widget 保存为 `anchorWidget`，显示时从该 widget 的几何位置计算初始位置。
- 因此当前观察是：所有 cell 都以同一个 `ui->table` 为位置锚点；这直接解释了截图中的固定弹出位置。
- LabelButton 路径传入具体按钮，位置随按钮变化；cell 不是 QWidget，需提供一个与 `QTableView::visualRect(index)` 对齐的临时 QWidget 锚点。

## 设计边界

- `ui->table` 继续作为 `prepareNumericKeyBoard` 的 trigger widget，以保留键盘关闭后的 viewport 防重复点击逻辑。
- 新增 `TouchNumKeyboard::setAnchorWidget()`，只分离“位置锚点”，不改变 receiver、模态、overlay、outside-click 或触发对象语义。
- 当前 cell 锚点是 `table->viewport()` 的透明、无焦点临时 child，几何来自 `visualRect(index)`；随 keyboard 销毁清理。
- 不硬编码屏幕坐标，不修改通用键盘的 clamping/Wayland presentation 算法。

## 成功标准

1. 编辑不同行、不同列时，键盘初始位置使用对应 cell 的当前可视矩形。
2. 表格滚动后再编辑，锚点仍对应滚动后的 cell 位置。
3. LabelButton 等既有调用不改变行为。
4. 键盘关闭后的防重复点击仍作用于 `QTableView::viewport()`。
5. 临时 cell 锚点不会泄漏，也不接收鼠标、触摸或焦点。

## 验证级别

`static`

## 计划

1. 为 `TouchNumKeyboard` 增加独立位置锚点 setter。
2. 在 ListMode cell 编辑路径创建并绑定当前 cell anchor。
3. 静态检查对象生命周期、调用顺序、CMake 纳入和代码差异。

## 实施结果

- `TouchNumKeyboard` 新增 `setAnchorWidget(QWidget *)`，允许创建完成后独立替换位置锚点。
- `ListModePanel` 仍以 `ui->table` 创建键盘，随后根据当前 index 的 `visualRect()` 创建透明 cell anchor，并在 `open()` 前设置。
- cell anchor 以 `table->viewport()` 为 parent，不接收鼠标或焦点；keyboard 销毁后通过 `deleteLater()` 清理。
- 删除了 `popupNumKeyBoard()` 原先先创建后立即覆盖的无用 keyboard 分配。

## 静态验证

- 调用顺序确认：`prepareNumericKeyBoard(table, cfg)` → `visualRect(index)` → `setAnchorWidget(cellAnchor)` → `open()`。
- `prepareNumericKeyBoard` 捕获的 trigger 仍是 `ui->table`，因此 `ComplexWidgetEventBlocker` 仍安装在 table viewport。
- 通用 LabelButton 调用不调用新 setter，继续使用构造时的原 anchor，行为不变。
- `src/libs/controls/CMakeLists.txt` 与 `src/plugins/core/CMakeLists.txt` 已纳入所有修改文件。
- `git diff --check` 通过，仅有仓库行尾转换提示；未编译或运行。
