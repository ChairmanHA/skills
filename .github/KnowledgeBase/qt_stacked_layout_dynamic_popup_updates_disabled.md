# QStackedLayout 切页期间动态创建 Popup 的禁绘继承陷阱

## 1. 适用范围

本文记录一个 Qt Widgets 跨平台问题：

- `QStackedWidget` / `QStackedLayout` 正在切换页面；
- 新页面的 `showEvent()` 或由它同步调用的代码中动态创建控件；
- 该控件在构造时创建 `Qt::Popup`、`Qt::Dialog`、`Qt::Tool` 等窗口型子对象；
- 后续 Popup 的 `visible`、native `exposed`、geometry、transient parent 都正常，但没有 `QPaintEvent`，屏幕上不可见。

SGStudio 的现场案例是 Instrument System 首次进入页面时创建 Language、Theme、Power On State 三个 `EnumTextButton`。设备相关枚举在主窗口构造期已经创建，所以不受影响。

## 2. 已确认现象

正常参考时钟 Popup 与异常 System Popup 均满足：

- 枚举项和当前值正确；
- `PopupWidget::show()` 已执行；
- QWidget `visible=true`；
- native `QWindow::isExposed()=true`；
- `transientParent` 是 MainWindow；
- geometry 在屏幕范围内；
- 点击空白区域会关闭 Popup。

关键差异：

- 正常 Popup 在 Expose 后收到 Popup 和 viewport 的 `QEvent::Paint`；
- 异常 Popup 从 Show 到 Hide 都没有收到 Paint。

因此 `visible` 或 `exposed` 不能证明 QWidget 已经向平台窗口提交了可显示内容。没有 Paint 就没有新的绘制内容；Wayland 下表现为 surface 没有可见 buffer，Win32 下同样表现为窗口存在但没有正常绘制。

## 3. 根因

Qt 5.15 的 stacked 切页不是简单调用 `hide()` / `show()`。切换当前页时，`QStackedLayout::setCurrentIndex()` 会在显示新页面的过程中临时关闭父级更新，以避免中间状态闪烁。

这个状态通过 QWidget 子树传播：

1. stack 暂时进入 `updatesEnabled=false`；
2. 新页面开始 `show()`，触发其 `showEvent()`；
3. `showEvent()` 内创建的普通 QWidget 后代继承禁绘状态；
4. 新控件构造期间创建的 Popup 最初也从父对象继承该状态；
5. Popup 设置了 `Qt::Popup`，成为独立窗口；
6. stacked 切页结束，Qt 恢复普通 QWidget 子树的 updates；
7. 恢复递归跳过 `isWindow()` 的窗口型子对象；
8. Popup 及其列表保持 `updatesEnabled=false`，以后即使再次 `show()` 也不会 Paint。

问题发生在 QWidget 的通用状态传播与恢复逻辑中，早于 Win32、Wayland 等平台插件实际绘制，所以两个平台都会出现。平台只影响最终表现和诊断方式，不是根因。

## 4. “动态创建”本身没有问题

不要把本问题概括为“Popup 不能动态创建”。以下动态创建通常安全：

- 主窗口构造或首次显示前创建；
- 页面已经稳定显示、stacked 切换已经完成后创建；
- 创建的对象只是普通非窗口子控件，恢复 updates 时仍位于递归子树中；
- 在事件循环下一轮创建，且已经脱离 stacked 切页的同步调用栈。

真正危险的组合是：

> 临时禁绘期间创建 + 子对象继承禁绘 + 子对象随后成为窗口 + 恢复递归跳过窗口。

`showEvent()` 只是本次触发位置，不是唯一可能入口。任何处于祖先 `setUpdatesEnabled(false)` 区间内的同步构造链都可能触发。

## 5. 推荐修复

### 5.1 首选：在稳定阶段构造

对固定、生命周期与页面一致的 Popup 控件：

- 在页面装配阶段创建；
- 在主窗口首次显示或 stacked 首次切换前加入最终布局；
- 后续只同步数据、可见性和 enabled 状态，不反复重建。

SGStudio 的修复是在 `InstrumentSystemWidget::setPreferencePanel()` 完成页面装配后调用既有 `rebuildMenuEntries()`，使三个固定菜单枚举在首次切页前构造。动态 File / Maintenance 菜单入口仍按原机制更新。

这种修复改变的是错误的创建时机，不修改共享 `EnumTextButton` / `PopupWidget`，也不影响正常设备枚举。

### 5.2 可接受：切页完成后延迟构造

如果业务必须按需创建，可将构造投递到事件循环下一轮，并确保对象 owner 仍存活：

```cpp
QTimer::singleShot(0, owner, [owner]() {
    owner->createPopupControl();
});
```

采用此方案前必须确认：

- 下一轮执行时 stacked 切换已经完成；
- 页面没有在此期间离开或销毁；
- 不会因重复 show 产生重复控件；
- 延迟创建不会造成首次交互缺失。

固定控件优先提前构造，不要仅为规避时序而引入异步状态。

### 5.3 不推荐：事后强制恢复 updates

对 Popup 调用 `setUpdatesEnabled(true)` 可以解除当前症状，但它依赖调用方了解共享控件内部窗口结构，并掩盖错误构造时机。若内部还有窗口型后代或以后结构变化，问题可能复发。

除非对象确实必须在禁绘区间创建，且该恢复责任由创建边界明确拥有，否则不要使用这种补丁。

## 6. 排查方法

遇到“点击后按钮高亮、点空白会关闭，但 Popup 不可见”时，不要只检查数据和 geometry。按以下顺序对比正常与异常控件：

1. 枚举项数、当前值、enabled；
2. Popup `isVisible()`、`isWindow()` 和 geometry；
3. native `QWindow::isExposed()`、flags、transient parent；
4. Popup、内容控件及 viewport 的 `updatesEnabled()`；
5. Show 到 Hide 期间是否收到 `QEvent::Paint`；
6. 创建调用栈是否位于 `showEvent()`、stacked 切页或祖先 `setUpdatesEnabled(false)` 区间。

最有区分度的证据是：

```text
Show -> native Expose -> 无 Paint -> Hide
```

如果 `visible/exposed` 正常而 Paint 缺失，应优先检查 updates 状态及其继承时机，而不是继续修改坐标、窗口 flags、QSS、菜单数据或 Wayland transient parent。

## 7. 验证要求

修复后至少验证：

- Popup、列表和 viewport 的 `updatesEnabled()` 为 true；
- 首次打开和第二次打开均收到 Paint；
- 选项和当前值不变；
- 外点关闭和按钮高亮复位正常；
- Win32 与树莓派 Wayland 均可见；
- 动态菜单刷新不销毁固定 Popup；
- 临时诊断日志与 event filter 已清理。

## 8. 本次案例

- 修复提交：SGStudio `c01fffaa`
- 调查记录：[System 菜单枚举的构建与显示时序修正](../TaskLog/2026-10-13_fix-system-enum-construction-order.md)
- 业务设计：[Instrument System 分类页面 / Tab](instrument_system_category_dialog_design.md)

