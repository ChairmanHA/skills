## 背景

- `GpsInfoDialog` 当前只把 `antenna / xpps_onoff / xpps` 接到 `Core::GnssSettings`，`delayEdit` 仅存在于 UI，未参与 query/configure 回显链。
- 软键盘体系已经在 `PropertyBindingHelper::prepareNumericKeyBoard(QWidget *, NumericKeyboardConfig)` 中支持通用数值编辑，并通过 `FrequencyUnitAdapter / TimeUnitAdapter` 提供频率/时间单位语义。
- `TouchNumKeyboard` 打开后会把焦点切到键盘自身的编辑区；输入事件发给 `BaseUnitAdapter` receiver，而不是原始触发控件。这与 GPS 对话框想要的“点击 LineEdit 后转到软键盘编辑区，关闭后不回焦点”语义一致。

## 局部假设

- 这次不需要改 `TouchNumKeyboard` 底层，也不需要新造一套 GPS 专用软键盘控件。
- 只要在 `GpsInfoDialog` 中为 `xpps` 和 `delayEdit` 建一个很薄的打开入口，复用现有 numeric keyboard 工厂，就能得到：
  - 频率/时间单位输入；
  - 焦点进入键盘编辑区；
  - 关闭后不回原 LineEdit；
  - 与当前 Wayland/minibar 已验证过的软键盘协议保持一致。
- `ppsDelay` 的根因是本地遗漏：`refreshGnssConfig()` 没回显、`applyGnssConfig()` 没读取。

## 便宜校验

- 检查 `BaseUnitAdapter::event()`，确认回车提交时会关闭父键盘并保留 receiver 当前单位。
- 检查 `TouchNumKeyboard::showEvent()`，确认显示后焦点转到键盘编辑区。
- 检查 `prepareNumericKeyBoard(QWidget *, NumericKeyboardConfig)`，确认非 property 场景也能直接创建 frequency/time keyboard。

## 实施计划

1. 在 `GpsInfoDialog` 增加一个局部的 LineEdit -> NumericKeyboard 打开路径，分别给 `xpps` 配 `Frequency`，给 `delayEdit` 配 `Time`。
2. 用对话框成员保存当前显示单位，确保设备 query/configure 回写后仍保留用户最近一次显示上下文。
3. 在 `refreshGnssConfig()` 和 `applyGnssConfig()` 中补上 `ppsDelay` 的读写和 UI 回显；默认 query 失败前保持 `0s` 语义。
4. 去掉旧的 `QDoubleValidator` 直编路径，改由 numeric keyboard config 提供范围约束；`xpps` 继续使用 `[0.25, 10000000] Hz`。
5. 做窄范围静态/诊断校验，优先检查改动文件错误。

## 2026-06-02 补充

- `xpps` 的步进应直接走现有 `stepStrategy="125"`，让 createKeyboardBase 选用 `Step125Controller`，而不是在 GPS 对话框里另写一套频率步进规则。
- `delayEdit` 的步进应设置为 `1.0` 秒，继续复用默认 `StepController`。

## 2026-06-02 二次排查

- 重新对比 `CommonPanel` 与 `GpsInfoDialog` 后，发现两者并非都在“关闭时”触发同一套 reopen 逻辑。
- `CommonPanel` 的键盘只由点击 `LabelButton` 触发；而 `GpsInfoDialog` 额外给 `QLineEdit` 加了 `FocusIn -> requestNumericKeyboard()`。
- 点击软键盘外部关闭时，键盘会先走 `Esc -> reject() -> finished()`；如果焦点在这个过程中回弹到原始 `QLineEdit`，GPS 这条 `FocusIn` 路径就可能在旧键盘 `finished` 栈里再次请求打开，形成 reentrant close/open。
- 因此新的最小修正点不在 `TouchNumKeyboard` receiver 强弱引用，而在 GPS 自己的触发条件：保留显式点击打开，去掉 `FocusIn` 自动打开，避免仅 GPS 独有的 finished 重入链。

## 2026-06-02 三次排查

- 继续沿着崩溃点看，新的直接根因不是 `inputsReceiver`，而是 `TouchNumKeyboard` 自己把 `ui->edit` 的 `textChanged/cursorPositionChanged/selectionChanged` 连接到了以 `this` 为 context 的 lambda，并且 lambda 内部依赖 `Q_D`。
- C++ 析构顺序是：`~TouchNumKeyboard()` 函数体先执行，然后成员 `d_ptr` 析构，最后才进入 `QDialog/QWidget/QObject` 基类析构；而 Qt 子对象（包括 `ui->edit`）是在后面的 QObject 析构阶段删除的。
- 因此 `ui->edit` 在自身销毁或焦点清理时即使只发一次 `selectionChanged`，也可能命中一个“receiver 还是 this，但 `d_ptr` 已经释放”的 lambda，表现出来就是 debugger 里内部状态异常。
- 对这个问题，继续纠结“为什么 QLineEdit 会不会发信号”没有意义；正确修法是在 `closeEvent`/析构开始时先断开 `ui->edit -> TouchNumKeyboard` 的同步桥，再让 Qt 去做后续 focus/child teardown。

## 2026-06-02 四次调整

- GPS 这条 `prepareNumericKeyBoard(QWidget *, cfg)` 路径与 `CommonPanel` 的 property 路径还有一个行为差异：stepUp/stepDown 时，后者会立刻把 `editingFinished` 透传到业务层，而前者原来只在 `keyboard->finished(Accepted)` 后才统一 `applyGnssConfig()`。
- 这会导致 GPS 软键盘步进期间看不到设备 API 回写后的值，也不会立即刷新键盘输入区和页面 `QLineEdit`。
- 这次调整为：仅当 `BaseUnitAdapter::lastEditingTrigger() == Step` 时，GPS 立即调用 `applyGnssConfig()`，并用设备确认后的值反向刷新 adapter/keyboard indicator/LineEdit；文本回车提交仍然保留在 `finished(Accepted)` 后收口，避免 reopen/close 栈重新变复杂。
- `TimeUnitAdapter` 的显示精度也同步收口：`s` 默认保留到 6 位小数，`ms` 默认保留到 3 位小数，避免设备回读中的极小浮点误差直接显示成 `100.000001ms` 这类噪声文本。

## 风险点

- 如果只监听 `BaseUnitAdapter::editingFinished`，则“仅改显示单位、值不变”的 accepted 关闭不会回写 LineEdit 文案；因此需要同时在 `keyboard->finished(Accepted)` 时刷新显示文本。
- `TouchNumKeyboard` 默认 `showEvent()` 会聚焦自身 `edit`；GPS 侧不应在键盘关闭后再手工把焦点还给原 LineEdit，否则会违背需求。
- `ppsDelay` 的设备侧范围当前仓库内没有公开常量；本次不额外夹值，只做时间类型输入与设备原样写回。