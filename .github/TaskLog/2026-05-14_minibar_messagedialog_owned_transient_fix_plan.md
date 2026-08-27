# 2026-05-14 MiniBar MessageDialog 问题现状整理

## 目的

把这一轮已经确认的代码事实、用户已验证结果整理下来，避免重复试错。



## 当前基线代码的已确认事实

### 1. MessageDialog 在显示前会同步关闭所有可见的 Qt::Popup

文件：src/libs/controls/messagedialog.cpp

- `MessageDialog::showMessage(...)` 在调用 `runAsync(...)` 之前，会遍历 `QApplication::topLevelWidgets()`。
- 只要顶层窗口 `isVisible()` 且 `windowFlags().testFlag(Qt::Popup)` 为真，就会直接 `close()`。
- 这不是 MiniBar 专用逻辑，而是全局、无差别地关闭所有可见 popup。

这个事实直接成立，不依赖运行时猜测。

### 2. MessageDialog 当前是“异步 open”，不是“同步 exec”

文件：src/libs/controls/dialog.cpp

- `Dialog::runAsync(...)` 只是保存回调，然后调用 `this->open()`。
- `Dialog::runAsShow(...)` 调用的是 `this->show()`。
- 当前自定义 MessageDialog 走的是 `runAsync(...)`，不是 `exec()`。

这意味着当前路径存在一个明确的时序特征：

1. 先同步关闭 popup。
2. 再异步把 MessageDialog 打开。

两步之间存在事件循环空档。

### 3. “把自定义 dialog 变成模态”本身不是根因解

文件：src/libs/controls/messagedialog.cpp

- `MessageDialogPrivate::applyMessageType(...)` 里，`MSG_QUESTION`、`MSG_WARNING`、`MSG_ERROR`、`MSG_FAILED`、`MSG_WAIT_OR_QUIT`、`MSG_REBOOT`、`MSG_REPROFILE` 都已经设置了 `Qt::ApplicationModal`。

因此，相关提示框在“真正显示出来之后”本来就是应用级模态。

这条证据可以直接排除一个错误方向：

- 问题不在于“MessageDialog 完全不是模态”。
- 更关键的差异在于它是 `open()`，且显示前会先同步清理 popup。

### 4. 软键盘本身就是 Qt::Popup

文件：src/libs/controls/touchnumkeyboard.cpp

- `TouchNumKeyboard` 构造函数里调用了：
	`setWindowFlags(Qt::Popup | Qt::FramelessWindowHint | Qt::CustomizeWindowHint);`
- 文件内注释也明确写了：设置了 popup 之后，本质上它就不是模态窗口，而是依赖 popup 机制处理对话框外点击。

这意味着：

- 只要 MessageDialog 继续“显示前同步关闭全部 Qt::Popup”，它就一定会命中软键盘。

### 5. 数字调制的大波形提示是在软键盘提交链路里同步触发出来的

文件：src/libs/controls/baseunitadapter.cpp
文件：src/libs/business/utils.cpp
文件：src/plugins/analog/digitalmodulation.cpp

- `BaseUnitAdapter::event(...)` 的 `commitInput(...)` 中，顺序是：
	1. 解析输入。
	2. `emit editingFinished(realValue);`
	3. 然后才尝试关闭父 `QDialog`。
- `prepareNumericKeyBoard(...)` 里把这个 `editingFinished` 连接到：
	1. `property->setProperty("currentUnit", ...)`
	2. `property->setValue(value)`
	3. `emit property->editingFinished()`
- `DigitalModulation` 中多个参数，包括 `PN`、`Oversample`，都是在 `property->editingFinished` 里直接判断 `estimatedSize >= MAXDOWNLOADSIZE`，然后进一步触发 `showExistingTrimmedDataPrompt(...)`。

因此当前调用链是明确的：

1. 软键盘回车。
2. `BaseUnitAdapter` 发 `editingFinished`。
3. property 桥同步回写并发 `property->editingFinished`。
4. 数字调制同步进入“大波形提示”逻辑。
5. 提示最终走到 `MessageDialog::showMessage(...)`。

这条链路说明：MessageDialog 介入时，软键盘 popup 仍然很可能还活着。

### 6. 之前的 10ms 延迟不是根因修复，只是时序缓冲

文件：src/plugins/analog/digitalmodulation.cpp
文件：src/plugins/analog/analogplaybackbusiness.cpp

- `DigitalModulation::showExistingTrimmedDataPrompt(...)` 仍然包了一层 `QTimer::singleShot(10, ...)`。
- `AnalogPlaybackBusiness::showTrimmedDownloadPrompt(...)` 也一样。

这说明旧代码已经在绕开一个时序问题，而不是在根治生命周期问题。

上一轮实验也证明了这一点：

- 把“同步清 popup”改成异步后，崩溃消失了。
- 但二级业务窗口仍然会关。

所以 10ms 只是在掩盖至少两个问题，其中一个是重入/生命周期，另一个是 MiniBar 收起路径。

### 7. MiniBar 的二级业务窗口不是 Qt::Popup，而是 Qt::Tool 顶层窗口

文件：src/plugins/core/minibarwindow.cpp

- `showBusinessPanelPopup(...)` 创建业务二级页时使用：
	`new QDialog(this, Qt::Tool | Qt::FramelessWindowHint | Qt::WindowStaysOnTopHint)`
- 同时显式设置：
	`setModal(false)` 和 `setWindowModality(Qt::NonModal)`。

这点很重要：

- MiniBar 二级业务窗口本身不是 popup。
- 它是否保留，取决于 `MiniBarWindow` 的外点关闭/失焦关闭策略，而不是 Qt popup 的默认自动关闭行为。

### 8. MiniBar 当前只识别“已经激活/已经可见”的 owned transient，没有“即将出现的异步 dialog”概念

文件：src/plugins/core/minibarwindow.cpp

当前相关判断分布在这些函数里：

- `popupBelongsToMiniBar(...)`
- `hasOwnedMiniBarModalWindow(...)`
- `hasOwnedMiniBarPopupWidget(...)`
- `activeOwnedMiniBarAuxiliaryTransient(...)`
- `isOwnedPopupArea(...)`
- `MiniBarWindow::eventFilter(...)`

`eventFilter(...)` 的关键行为：

- 收到 `QEvent::ApplicationDeactivate` 时：
	如果当前 Expanded，且没有 owned modal/popup，则关闭 transient 或收起 MiniBar。
- 收到 `QEvent::MouseButtonPress` 时：
	如果当前 Expanded，且没有 owned modal，且点击位置不在 owned popup area，就关闭 transient 或收起 MiniBar。

而这些判断使用的是：

- `QApplication::activeModalWidget()`
- `QApplication::activePopupWidget()`
- `QApplication::activeWindow()`
- `m_providerMenu`
- `m_activePopup`

也就是说，当前逻辑只看“已经处于活动态的窗口”，没有“某个属于 MiniBar 的异步对话框已经开始打开但尚未激活”的保护状态。

这和第 2 条的 `open()` 时序空档可以直接拼起来。

### 9. 系统文件对话框之所以没问题，关键差异是 exec() 而不是“更模态”

文件：src/libs/controls/filedialogutils.cpp

- Windows 下 `Controls::getOpenPath(...)` 直接构造 `QFileDialog dialog(...)`。
- 然后调用的是 `dialog.exec()`。

和 MessageDialog 对比，系统文件对话框路径有两个本质差异：

1. 它是同步 `exec()`，没有 `open()` 的时序空档。
2. 它不会在显示前自己先去遍历并关闭全部 `Qt::Popup`。

这条证据是当前最可靠的“为什么文件对话框没问题、MessageDialog 有问题”的代码级解释。

## 已经被上一轮实验证实的结论

### 1. 崩溃和“二级窗口被关掉”不是同一个问题

用户已经明确反馈：

- 上一轮改动后“不崩溃了”，但“窗口依然会关闭”。

因此可以确认：

- 同步关闭软键盘 popup 导致的重入/悬空访问，确实解释了崩溃。
- 但 MiniBar 二级窗口关闭，仍然还有独立关闭路径没有被拦住。

### 2. 单纯把 MessageDialog 设成模态，不能解释文件对话框和它的行为差异

因为当前相关消息类型本来就已经是 `ApplicationModal`。

文件对话框能保住二级页，真正更像是因为：

- 它走 `exec()`。
- 它没有“先同步清空所有 popup”这一步。


## 本轮 ArbPanel 定点修复计划

- 目标只收敛 `ArbPanel::loadFile()` 的“文件过大”提示，不扩散到数字调制或软键盘链路。
- 维持现有 `MessageDialog::showMessage(...)` 异步行为不变，避免影响既有调用点。
- 在 `MessageDialog` 上增加一个显式同步接口，使调用方可以按系统文件对话框的方式直接 `exec()`。
- `ArbPanel` 中“文件过大”提示改走新的同步接口；其余如“非法文件类型”提示先保持原样，避免扩大测试面。
- 本轮不编译，只做头源文件一致性和局部静态校验，运行验证交由用户完成。

## 本轮 Digital / OFDM / Ramp 修复计划

- 数字键盘链路的根因先收敛在 `PropertyBindingHelper::prepareNumericKeyBoard(...)`：adapter 的 `editingFinished` 仍同步写回 property 值和显示文本，但不再同步发 `property->editingFinished()`。
- `property->editingFinished()` 改为在键盘 `finished` 之后通过下一轮事件再发，确保大波形提示不再跑在软键盘提交/关闭栈内。
- 共享大波形提示 `AnalogPlaybackBusiness::showTrimmedDownloadPrompt(...)` 改为同步 `exec` 取结果，这样 OFDM / Ramp / Digital 的公共大波形提示统一具备和系统文件对话框相同的阻塞语义。
- `DigitalModulation::showExistingTrimmedDataPrompt(...)` 不再保留独立异步实现，直接复用共享同步提示入口，减少重复逻辑和行为漂移。
- 同步 `exec` 路径需要保证 `MessageDialog` 在执行期间不会因为 `WA_DeleteOnClose` 提前自删，因此会顺手把 `execMessage(...)` 的生命周期收口为“exec 期间禁止自删，返回后再按原策略 deleteLater”。

## 本轮 QuickWaveform / Arb 差异复盘与修复计划

- 用户要求回退 `Controls::Dialog` 基类上的 `WA_QuitOnClose=false` 修改，并重新分析为什么 `ArbPanel` 的“文件大小超限”提示在 MiniBar 下不会让软件退出。
- 新的高可信结论是：Arb 的关键安全因子不是 `MessageDialog` 额外设置了什么窗口属性，而是它的提示出现在 `Controls::getOpenPath(...)` 内部 `QFileDialog::exec()` 返回之后。也就是说，提示已经脱离了 MiniBar 当前点击/激活事件栈，窗口系统状态先经过一次同步模态边界再回到 `ArbPanel::loadFile()`。
- `QuickWaveformPanel` 不经过系统文件对话框，而是在自身 `btnLoad` 点击链里直接 `execMessage(...)`；这条路径缺少 Arb 那个天然的模态边界，因此更容易把自定义提示框放进 MiniBar 当前鼠标事件/焦点切换的临界区。
- 根因修复方向不是继续改全局 `Dialog` 属性，而是把 QuickWaveform 本地这些直接从 panel 点击链触发的提示，改成“当前事件返回后，再同步 `execMessage()`”。这样既保留同步交互语义，又人为补出一层和 Arb 相近的稳定边界。

## 本轮数字键盘步进回归修复计划

- 上一轮把 `prepareNumericKeyBoard(...)` 中的 `property->editingFinished()` 统一延后到了键盘 `finished` 之后，但 `Up/Down/PageUp/PageDown` 步进键原本就依赖 `StepController` 立即发出的 `adapter->editingFinished()`。
- 这导致步进键虽然仍会同步改 adapter / property 数值，但不会再立即触发大波形提示与业务回写，表现为 PN 可以在软键盘内一路加大且无提示。
- 本轮修复只恢复 `stepBy` 这一路的即时 `property->editingFinished()` 语义；文本输入 + 回车提交仍保持“键盘关闭后再触发业务提示”的延后策略。

## 本轮 markEditingTrigger 崩溃修复计划

- 当前崩溃不是 `markEditingTrigger(...)` setter 本身的逻辑错误，而是调用时机错误：`BaseUnitAdapter` / `StepController` 在 `emit editingFinished(...)` 之后又尝试把 trigger 重置回 `Unknown`。
- 当大波形提示在 `editingFinished` 的槽链中同步弹出时，软键盘可能因为失焦或关闭流程销毁 adapter；信号返回后再执行“重置 trigger”就会踩到悬空对象。
- 本轮最小修复是不再在 `emit editingFinished(...)` 之后回写 `Unknown`。trigger 只需要在下一次发信号前覆盖成新的来源，不需要在本次发信号后立即清零。
- 这样既保留 `prepareNumericKeyBoard(...)` 里按来源分流的能力，也去掉了信号后半段对潜在已销毁对象的再次访问。

## 本轮 TouchNumKeyboard sendEvent 崩溃修复计划

- 当前新的崩溃点在 `TouchNumKeyboard::clickedKey()` / `TouchNumKeyboardPrivate::handleShortCutInMapping()`：代码先缓存 `QApplication::focusWidget()`，然后连续向同一个原始指针发送 `KeyPress` 与 `KeyRelease`。
- 当 `KeyPress` 触发的业务链同步弹出大波形提示时，当前焦点控件（例如软键盘的 `edit` 或步长编辑框 `stepEdit`）可能在 `sendEvent(KeyPress)` 返回前已被关闭流程销毁。
- 此时第二次继续对旧指针发送 `KeyRelease` 就会命中悬空对象；这和之前 `markEditingTrigger` 的问题属于同一类“同步提示导致发送目标在槽链中死亡”。
- 修复策略是保留“发给当前焦点控件”的既有路由语义，但把目标改成 `QPointer<QWidget>` 弱引用，并在 `KeyPress` 返回后再次判活；若目标已失效则直接跳过 `KeyRelease`。

## 本轮最终收口结论（2026-05-15）
- 最终可复用的排障模型必须把这轮问题拆成四个独立层次：
	1. 软键盘文本提交时，`property->editingFinished()` 过早进入业务链，导致大波形提示跑在软键盘关闭栈内。
	2. 步进键和文本回车提交不是同一条时序链，不能简单共用“全部延后”的策略。
	3. 同步提示框会改变焦点与对象生命周期，因此 `emit editingFinished(...)` 之后继续访问 sender，或在合成按键里跨 `KeyPress/KeyRelease` 持有原始 `focusWidget`，都会踩到悬空对象。
	4. MiniBar 下提示框是否稳定，不只取决于 `exec/open`，还取决于它是否脱离了当前点击/激活事件栈；Arb 安全的关键是先经过 `QFileDialog::exec()` 边界，而不是给 `Dialog` 全局补 `WA_QuitOnClose=false`。
- 已落地的公共修复策略如下：
	1. `BaseUnitAdapter` 新增 `EditingTrigger`，由提交来源显式区分 `Commit` 与 `Step`。
	2. `prepareNumericKeyBoard(...)` 中，文本回车提交仍同步写回 property 的 `value/currentUnit/displayText`，但把 `property->editingFinished()` 延后到键盘 `finished` 后的下一轮事件；步进路径则保留立即触发业务链的语义。
	3. `BaseUnitAdapter` / `StepController` 不再在 `emit editingFinished(...)` 之后把 trigger 重置回 `Unknown`，避免提示框链路中对象已销毁后再回写 sender。
	4. `TouchNumKeyboard` 合成按键改用 `QPointer<QWidget>` 持有焦点目标，并在 `KeyPress` 返回后再次判活，再决定是否发送 `KeyRelease`。
	5. `MessageDialog` 新增 `execMessage(...)`，用于需要明确同步决策边界的调用点；执行期间临时关闭 `WA_DeleteOnClose`，返回后再按原策略 `deleteLater()`。
	6. `AnalogPlaybackBusiness::showTrimmedDownloadPrompt(...)`、`DigitalModulation::showExistingTrimmedDataPrompt(...)` 与 `ArbPanel`/`QuickWaveformPanel` 的大波形/非法文件提示已按各自时序边界改为同步 `exec` 或“下一轮事件再同步 `exec`”。
- 当前对未来最有价值的经验不是“再加几个定时器”，而是：先区分提交来源，再区分生命周期边界，再判断提示框是应该同步执行，还是先脱离当前事件栈后再同步执行。


