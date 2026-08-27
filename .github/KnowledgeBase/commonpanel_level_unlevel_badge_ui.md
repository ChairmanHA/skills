# CommonPanel Level UnLevel Badge UI

本文档简要说明 `CommonPanel` 中 `Level` 按钮的 `UNLEVEL` 特殊 UI 处理，以及为什么这套改动不会影响其他普通 `LabelButton`。

## 1. 目的与边界
当前产品语义是：

- `tx_config_ffm()` 返回 `99` 时，主窗口 `CommonPanel` 的 `Level` 按钮显示右上角 `UNLEVEL` badge
- 下行数值继续显示当前用户设置的电平
- 当后续再次 `tx_config_ffm()` 且返回正常时，该 badge 自然消失
- `MiniBar` 本次不显示这条状态

## 2. UI 结构

`Level` 按钮依然是 `LabelButton`，只是在顶部一行按需扩展了一个 header widget：

- 左侧仍然是原来的 `textLabel`，显示 `Level`
- 右侧是可选 `topBadgeLabel`，显示 `UNLEVEL`
- 下半行仍然是原来的 `infoLabel`，显示数值，如 `20dBm`

视觉规则：
- 顶部保持原有左右留白，不让内容贴边
- `UNLEVEL` 只提亮文字
- 当 badge 隐藏时，`Level` 控件回到普通样式

## 3. 为什么不会影响其他 LabelButton

原因有三层：

1. `InfoButton` 默认路径没变
   - 顶部区域默认仍然是 `m_textLabel`
   - 只有子类显式调用 `setTextWidget()`，顶部结构才会被替换

2. `LabelButton` 的 badge 能力是按需启用
   - 只有调用 `setTopBadgeText()` 或 `setTopBadgeVisible()` 时，才会懒创建 `m_headerWidget` 和 `m_topBadgeLabel`
   - 未使用 badge 的普通 `LabelButton` 仍然保持旧结构

3. 当前只有 `CommonPanel` 的 `Level` 调用了这套接口
   - `m_level->setTopBadgeText(tr("UNLEVEL"))`
   - `m_level->setTopBadgeVisible(...)`
   - 其他 `LabelButton` 没有调用，因此不会切到这条布局分支

同时，QSS 也被限制在 `CommonPanel LabelButton#level QLabel#topBadgeLabel` 这个范围，不会扩散到别的按钮。

## 4. 与 warning 机制的关系

这条 UI 与状态栏 warning 的关系是：

- `FancyDevice::warningCategory(99)` 已改为 `Ignorable`
- 因此 `STATUS_WARNING_UNLEVEL` 不会进入 `DeviceInfoWidget` 的滚动 warning 队列
- 但设备层仍保留 `txLevelUnlevelActive()` 作为局部 UI 状态源

所以要记住：

`UNLEVEL` 现在是 `Level` 按钮的局部 badge 状态，不是状态栏 warning。