# CommonPanel Level 状态 Badge UI

2026-11-17：AMPC 更名 UCOR，OFST 与 Level Offset 功能删除。本文说明 Level 的 UNLEVEL 告警和 UCOR 幅度补偿标记；本次仅静态修改，未构建或运行，见 [TaskLog](../TaskLog/2026-11-17_remove-level-offset-use-ucor.md)。

## 1. 目的与边界

- UNLEVEL 激活时只显示告警；补偿开关或表格更新不能覆盖它。
- 无告警且 flatnessEnabled() 为真时显示 UCOR，补偿关闭后隐藏；告警解除后按当前开关恢复 UCOR。
- UCOR 由 PowerReferencePlane 直接派生，不缓存第二份状态。初始化和 flatnessChanged 共用 updateLevelButtonBadge()，覆盖 GUI、SCPI、Load、Reset 和空表 Apply。
- 仅加载或编辑未提交草稿不新增启用标记；当前频点补偿为 0 dB 时仍显示。标记不宣称设备执行或功率实测完成。
- Level 下行数值与既有功率语义保持原样；Minibar 不显示这些状态。

## 2. UI 结构

复用既有 LabelButton 顶部 header：左侧 textLabel 显示 Level，右侧 topBadgeLabel 显示 UNLEVEL 或 UCOR，下行 infoLabel 显示数值。UNLEVEL 保留 warningUnlevel 属性及深浅主题告警文字色，UCOR 沿用普通字色；复用原 repolish，不新增控件、状态字段或 QSS。

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

刷新入口：`MainWindow` 在 `CommonDeviceProfile::deviceConfigurationDone`（errorStr 为空）时读取 `txLevelUnlevelActive()`。Runtime 托管管线经 `TxSessionService::deviceConfigurationDone` 转发到该信号；Streaming 等 legacy business 经 `IBusiness::deviceConfigurationEnd` 发出该信号，因此两条路径都会刷新。

## 5. 历史 Instrument 模式例外

以下为历史实现记录。2026-11-10 核对当前 `CommonPanel` 仅有 Wide/Compact 布局，已无 `setInstrumentLayout()` 专用分支；当前 UCOR 沿用统一 badge 路径，不重新引入历史隐藏例外。Minibar 仍不显示这些标记。

`ui-mode=instrument` 使用 1024x600 固定布局，Level 按钮采用独立的紧凑显示：

- 2026-09-14 真机字体复核后，宽度固定为 `220px`，PEP/RMS 两行字体为 `24px`；该宽度用于容纳两位小数的 `PEP: -130.25dBm` / `RMS: -30.25dBm` 并保留少量取整余量；
- 复用 `LabelButton` 的原生两行结构，上行显示 `PEP: <value>`，下行显示 `RMS: <value>`；
- `infoLabel` 在 instrument 模式移除共享的额外左 margin，使 RMS 与 PEP 的文字起点一致；
- 独立的 `rmsPower` 按钮继续隐藏；
- `UNLEVEL` 状态仍由设备链路记录，但 badge 在 instrument 模式不显示，后续状态回写也不会重新显示它；
- main 模式仍保留 `Level` 与 `RMS` 两个独立按钮，并继续按原规则显示 `UNLEVEL`。

该差异由 `CommonPanel::setInstrumentLayout()` 和现有显示刷新函数控制，不通过全局
LabelButton QSS 改造，因此不会影响 main 模式或其他 LabelButton。
