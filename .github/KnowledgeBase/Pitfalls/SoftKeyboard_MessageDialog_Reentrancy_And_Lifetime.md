# 软键盘提交 / 步进 / MessageDialog 的重入与生命周期陷阱

本文记录 2026-05 这轮已经实锤并完成修复的一组问题。它们表面上分别表现为：

- 数字键盘回车后弹大波形提示时崩溃。
- 改完回车路径后，软键盘里的上下步进不再触发大波形提示。
- 崩溃点落在 `BaseUnitAdapter`、`StepController` 或 `TouchNumKeyboard` 的 `focusWidget` 相关代码上，看起来像“Qt 自己失效了对象”。

这些问题不是三个孤立 bug，而是同一组“提交来源 + 提示框时序 + popup 生命周期 + 弱引用保护”边界的连锁反应。

## 1. 先拆问题，不要混为一谈

这轮最终必须把问题拆成三类：

1. **文本回车提交过早进入业务链**
   `BaseUnitAdapter::editingFinished` 触发后，`prepareNumericKeyBoard(...)` 立刻发 `property->editingFinished()`，Digital 的大波形确认因此跑进了软键盘尚未关闭的调用栈。
2. **步进键和文本提交不是同一条时序链**
   如果把 `property->editingFinished()` 一刀切全部延后，步进路径就再也不会即时触发大波形提示。
3. **同步提示框会在槽链里改变对象生命周期**
   一旦 `editingFinished` 的槽链里同步弹出提示框，adapter、step controller 或焦点 widget 都可能在信号返回前被关闭流程销毁。

## 2. 这轮最终落地了哪些调整

### 2.1 `BaseUnitAdapter` 增加 `EditingTrigger`

文件：`src/libs/controls/baseunitadapter.h/.cpp`

新增：

- `EditingTrigger::Commit`
- `EditingTrigger::Step`

当前语义：

- 文本回车提交前，由 `BaseUnitAdapter` 标记 `Commit`。
- 步进回写前，由 `StepController` 标记 `Step`。

这样公共层才能知道“本次 adapter 完成编辑”到底来自哪种输入来源，而不是把所有 `editingFinished` 都看成同一件事。

### 2.2 `prepareNumericKeyBoard(...)` 把“值同步”和“业务确认”拆开

文件：`src/libs/business/utils.cpp`

当前策略不是简单延时，而是两段式：

1. adapter `editingFinished` 到来后，**立即**把 `currentUnit/value/displayText` 同步到 property。
2. 若来源是 `Step`，则**立即** `emit property->editingFinished()`。
3. 若来源是 `Commit`，则只记 `pendingPropertyEditingFinished=true`，等键盘 `finished(QDialog::Accepted)` 后，再 `QTimer::singleShot(0, property, ...)` 发出 `property->editingFinished()`。

原因：

- 文本提交要躲开“软键盘 popup 尚未关闭”的临界区。
- 步进路径必须保留即时约束和即时提示能力。

### 2.3 不再在 `emit editingFinished(...)` 之后回写 trigger

文件：`src/libs/controls/baseunitadapter.cpp`
文件：`src/libs/controls/stepcontroller.cpp`

这轮的关键 crash 之一，不是 setter 有逻辑问题，而是**在信号发出后还继续访问 sender**。如果槽链里同步弹框、关 popup、换焦点，sender 完全可能在信号返回前已经死亡。

当前修复是：

- 只在下一次发信号前覆盖新的 trigger。
- 不要求本次发完后再手动清成 `Unknown`。

这是一条通用规则：**只要槽链里可能弹同步对话框，就不要在 `emit` 之后再补写 sender 状态。**

### 2.4 `TouchNumKeyboard` 合成按键改为弱引用保护

文件：`src/libs/controls/touchnumkeyboard.cpp`

原问题：

- 代码先缓存 `QApplication::focusWidget()` 原始指针。
- 先发 `KeyPress`，再发 `KeyRelease`。
- 但 `KeyPress` 触发的业务链里可能同步弹框并销毁那个焦点控件。
- 于是第二次继续对旧指针发 `KeyRelease`，直接 use-after-free。

当前修复：

- 用 `QPointer<QWidget>` 持有焦点控件。
- `KeyPress` 返回后再次判活。
- 失效则直接跳过 `KeyRelease`。

以后如果再看到“崩在 sendEvent 第二次发键”的栈，优先检查这一类生命周期问题。

### 2.5 `MessageDialog` 新增 `execMessage()`

文件：`src/libs/controls/messagedialog.h/.cpp`

当前差异：

- `showMessage()`：会先关所有可见 `Qt::Popup`，再异步 `open()`。
- `execMessage()`：同步 `exec()`，返回标准按钮结果；执行期间临时关闭 `WA_DeleteOnClose`，返回后恢复并按原策略 `deleteLater()`。

它解决的是“需要同步决策 + 需要明确阻塞边界”的问题，不是通用替代品。

### 2.6 大波形提示与 QuickWaveform 提示各自按调用点收口

文件：`src/plugins/analog/analogplaybackbusiness.cpp`
文件：`src/plugins/analog/digitalmodulation.cpp`
文件：`src/plugins/htra/arbpanel.cpp`
文件：`src/plugins/quickwaveform/quickwaveformpanel.cpp`

当前代码中的主要边界：

- 共用大波形确认 `showTrimmedDownloadPrompt(...)` 改成同步 `execMessage()`。
- `DigitalModulation` 的截断提示使用异步信息提示；远程 non-interactive 请求直接继续，
  不等待用户选择。
- Arb 的“文件大小超限”提示改成同步 `execMessage()`，它天然处在 `QFileDialog::exec()` 返回之后。
- QuickWaveform 没有系统文件对话框边界，因此本地提示改成“当前事件返回后，再同步 `execMessage()`”。

这说明：**不是所有同步 `exec` 都等价。是否稳定，还取决于它是不是脱离了当前点击/焦点切换的事件栈。**

当前 C/S Minibar 不属于这些 `MessageDialog` 展示路径：helper 不创建
`MessageDialog`，MainWindow 隐藏期间的主进程提示也会被 suppression 直接拒绝或丢弃。

## 3. 最终为什么不是 `WA_QuitOnClose` 根因

这轮中间一度怀疑把 `WA_QuitOnClose=false` 下沉到 `Controls::Dialog` 基类能统一解决问题，但最终证据不支持。

最终原因：

- Arb 的稳定路径来自 `Controls::getOpenPath(...)` 内部的 `QFileDialog::exec()` 边界。
- QuickWaveform 的不稳定路径来自 panel 点击链里直接触发提示，缺少 Arb 那层天然边界。
- 因此正确修法是恢复 `Dialog` 基类原状，再让 QuickWaveform 本地提示先脱离当前事件栈，再同步 `exec`。

结论：**时序边界优先于全局窗口属性补丁。**

## 4. 快速排障清单

未来遇到类似问题，按下面顺序排：

1. 先问自己：这次触发来自文本回车，还是来自上下步进？
2. 看 `prepareNumericKeyBoard(...)` 是否还在正确区分 `Commit` / `Step`。
3. 如果崩在 `emit editingFinished(...)` 之后的几行，优先怀疑 sender 在槽链里已经死了。
4. 如果崩在 `sendEvent(KeyRelease)`，优先检查 `focusWidget` 是否在 `KeyPress` 链里被销毁。
5. 不要先加 `QTimer::singleShot(10, ...)`。定时器只能暂时掩盖问题，不能替代边界建模。

## 5. 易踩坑总结

### 5.1 不要把所有 `property->editingFinished()` 都统一延后

文本提交要延后，步进不能延后。两者语义不同。

### 5.2 不要在同步提示链里继续访问 sender

“发完信号顺手清个状态”这种写法，在同步弹框链里就是高危代码。

### 5.3 不要跨两次 `sendEvent` 信任原始焦点指针

`KeyPress` 和 `KeyRelease` 之间，世界已经可能变了。用弱引用重判。

### 5.4 不要让 child widget 的析构期信号继续打回 parent 内部状态

`TouchNumKeyboard` 这类对象里，`ui->edit` 既是 child widget，又会通过 `textChanged/cursorPositionChanged/selectionChanged` 把状态同步回 keyboard 自己。

如果这些连接的 slot/lambda 内部继续通过 `Q_D` 或 `d->ui->edit` 读取当前状态，而 `closeEvent`/析构开始时没有先断开桥，那么 child widget 在后续 QObject teardown 里只要再发一次信号，就可能打进半析构 parent。

这类问题的修法不是继续怀疑 receiver 强弱引用，而是：

1. 在 `closeEvent`/析构开始时先 `removeEventFilter`。
2. 立即断开 `child -> this` 的同步连接。
3. 再交给 Qt 做后续 focus/child teardown。

### 5.5 不要把 `execMessage()` 当万能药

它只能提供同步边界，不能自动修复 popup 归属或点击栈重入。

### 5.6 不要再用“给基类全局加 `WA_QuitOnClose=false`”解释 Arb 的稳定性

这轮已经证明，Arb 的关键差异是先经过 `QFileDialog::exec()`，不是那个属性。

## 6. 相关文件

- `src/libs/business/utils.cpp`
- `src/libs/controls/baseunitadapter.h/.cpp`
- `src/libs/controls/stepcontroller.h/.cpp`
- `src/libs/controls/touchnumkeyboard.cpp`
- `src/libs/controls/messagedialog.h/.cpp`
- `src/plugins/analog/analogplaybackbusiness.cpp`
- `src/plugins/analog/digitalmodulation.cpp`
- `src/plugins/htra/arbpanel.cpp`
- `src/plugins/quickwaveform/quickwaveformpanel.cpp`
