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

## 5. Instrument 模式例外

`ui-mode=instrument` 使用 1024x600 固定布局，Level 按钮采用独立的紧凑显示：

- 2026-09-14 真机字体复核后，宽度固定为 `220px`，PEP/RMS 两行字体为 `24px`；该宽度用于容纳两位小数的 `PEP: -130.25dBm` / `RMS: -30.25dBm` 并保留少量取整余量；
- 复用 `LabelButton` 的原生两行结构，上行显示 `PEP: <value>`，下行显示 `RMS: <value>`；
- `infoLabel` 在 instrument 模式移除共享的额外左 margin，使 RMS 与 PEP 的文字起点一致；
- 独立的 `rmsPower` 按钮继续隐藏；
- `UNLEVEL` 状态仍由设备链路记录，但 badge 在 instrument 模式不显示，后续状态回写也不会重新显示它；
- main 模式仍保留 `Level` 与 `RMS` 两个独立按钮，并继续按原规则显示 `UNLEVEL`。

该差异由 `CommonPanel::setInstrumentLayout()` 和现有显示刷新函数控制，不通过全局
LabelButton QSS 改造，因此不会影响 main 模式或其他 LabelButton。
