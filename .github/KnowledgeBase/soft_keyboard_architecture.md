# 软键盘设计与实现总览

本文档描述当前仓库中软键盘体系的真实实现，而不是历史方案设想。重点覆盖以下几个问题：

- 软键盘本体负责什么，哪些逻辑不应该放进它。
- 单位输入、快捷单位、步进键、步长编辑分别由谁负责。
- `currentUnit`、`preferredDisplayUnit`、`displayText` 三者如何协同，为什么它们不能只保留一份状态。
- 当业务层在键盘提交后同步夹值或回滚原值时，单位上下文如何一起恢复。
- 为什么主输入区和 `EditableWidget` 步长编辑区能共用一套适配器协议。

如果只想看某个焦点陷阱，请另读 `Pitfalls/Qt_FocusProxy_SoftKeyboard_StepEdit.md`。如果要看 2026-05 这轮“数字键盘提交/步进/MessageDialog”重入与修复，请另读 `Pitfalls/SoftKeyboard_MessageDialog_Reentrancy_And_Lifetime.md`。本文关注总体架构、调用链和维护边界。

## 1. 设计目标

当前软键盘实现服务于两类输入场景：

1. 属性主输入区的数值与单位编辑，例如频率、时间、功率。
2. 标题栏步长输入区的即时编辑，例如 `Step 6MHz` 或 `Step 2dB`。

这套设计的核心目标不是“做一个会弹出来的键盘”，而是统一下列约束：

- 软键盘按钮、实体键盘输入、步进键都要走同一套数据语义。
- 显示文本允许保留单位上下文，但内部值必须始终回到 base value。
- 频率/时间类允许自动规范化显示单位，功率主输入与功率步长则要分开处理。
- UI 绑定控件不能各自做格式化，否则软键盘显示和主界面显示一定会漂移。

## 2. 模块分层

### 2.1 `TouchNumKeyboard`：键盘外壳与事件注入器

实现文件：`src/libs/controls/touchnumkeyboard.h/.cpp`

职责：

- 管理键盘 UI，包括主输入框 `edit`、数字键、确认键、快捷单位键、标题区附加控件。
- 把按钮点击翻译成 `QKeyEvent`，并发送给当前 `QApplication::focusWidget()`。
- 维护主输入框和当前 receiver 的动态属性同步：`inputText`、`cursorPos`、`selectionStart`、`selectionLen`。
- 负责“点击键盘外部如何关闭”的策略，使用 `PressOutsidePolicy` 配合 `MousePressEater` 实现。

维护边界补充：

- `TouchNumKeyboard` 自己还持有 `ui->edit -> keyboard` 的同步桥；如果 `QLineEdit` 在关闭/析构时因为焦点或选区清理继续发 `selectionChanged/textChanged/cursorPositionChanged`，slot 又反过来读取 `ui->edit` 当前状态，就会打进半析构对象。
- 因此这类桥必须在 `closeEvent()`/析构开始阶段先断开，不能等到 `QObject` 自动删除 child widgets 时再被动收尾。某些路径（例如 `CommonPanel`）只是较少触发这个窗口，不代表架构天然安全。

明确不负责：

- 不负责单位转换。
- 不负责数值合法性校验。
- 不负责自动规范化显示单位。
- 不负责理解某个 receiver 是主输入还是步长输入。

这使得 `TouchNumKeyboard` 保持通用。它只知道“把键送给当前焦点控件/receiver 所在链路”，而不关心业务语义。

### 2.2 `BaseUnitAdapter`：输入状态机与数值语义中枢

实现文件：`src/libs/controls/baseunitadapter.h/.cpp`

`BaseUnitAdapter` 是当前软键盘体系的核心。它维护三类关键状态：

- `realValue`：内部基准值，例如频率统一为 Hz，时间统一为 s。
- `inputText`：当前显示字符串，例如 `30.009MHz`。
- `preferredDisplayUnit`：当前显示上下文，例如 `MHz`。

当前实现中，`BaseUnitAdapter` 统一处理：

- 从 `inputText` 中拆分数字部分和尾部单位 token。
- 维护“数字区默认选中、单位区保留显示”的交互语义。
- 对 Enter、Esc、单位快捷键、上下键、PageUp/PageDown、`.`、`+/-` 做统一分发。
- 通过 `convertToBase()` 和 `convertFromBaseValue()` 将显示文本与内部基准值互转。

当前几个重要接口的语义如下：

- `setRealValue()`：用于 API 回写或外部值同步。它优先保留 `preferredDisplayUnit`，适合“值更新，但显示上下文尽量别变”的场景。
- `setRealValueAndNormalize()`：用于用户确认输入或步进后刷新显示。它会尽量走适配器自己的规范化输出，并在必要时回退到给定单位。
- `getCurrentUnit()`：从 `inputText` 提取当前显示单位。
- `resetSelectionToNumericPart()`：把选区限制在数字部分，使单位尾巴保留显示但默认不可直接编辑。

之所以同时保留 `setRealValue()` 与 `setRealValueAndNormalize()`，是因为这两个调用场景的意图不同：

- API/属性回写强调“别突然改用户的显示上下文”。
- 用户按 Enter、按单位快捷键、按步进键时，强调“显示结果应该重新规范化”。

如果只保留其中一个，频率/时间类就会在“保持上下文”和“自动换更合适单位”之间来回冲突。

### 2.3 派生适配器：把“单位语义”下沉到最小实现层

实现文件：

- `src/libs/business/frequencyunitadapter.cpp`
- `src/libs/business/timeunitadapter.cpp`
- `src/libs/business/powerunitadapter.cpp`
- `src/libs/business/powerstepunitadapter.cpp`

各适配器只负责自己的单位转换与 token 归一化，不负责键盘 UI。

#### 频率适配器

- base unit 为 `Hz`。
- 支持 `GHz/MHz/kHz/Hz`。
- 允许 `g/m/k/h` 作为实体键盘缩写。
- 在无显式单位要求时，会根据数值大小选择更合适的显示单位。

#### 时间适配器

- base unit 为 `s`。
- 支持 `s/ms/μs/ns`。
- 允许 `m/u/n/s` 以及 `us` 这类 ASCII 兼容写法。
- 同样支持按数值大小自动规范化显示单位。

#### 功率主输入适配器

- base unit 为 `dBm`。
- 支持 `dBm/dBmV/dBμV`。
- 这里的单位不是单纯倍率换算，而是带阻抗假设的偏移换算。
- 因此功率主输入不能和功率步长共用同一个适配器。

#### 功率步长适配器 `PowerStepUnitAdapter`

- 只服务步长输入区。
- base unit 固定为 `dB`。
- `convertToBase()` 与 `convertFromBaseValue()` 都是 1:1 数值语义，不做 `dBm/dBmV/dBμV` 之间的偏移换算。

这是一个关键拆分：

- 主输入的 `-20dBm` 是物理量。
- 步长的 `2dB` 是增量。

两者虽然都带 `dB` 相关文本，但语义完全不同，必须分别建模。

### 2.4 `StepController`：步进策略执行者

实现文件：`src/libs/controls/stepcontroller.h/.cpp`

`StepController` 本身不解析文本，它只负责：

- 接收 `BaseUnitAdapter::stepBy(value, step)`。
- 计算 `newValue = value + step * m_singleStep`。
- 再把结果回写给 adapter。

当前实现中，`StepController` 回写时调用的是 `setRealValueAndNormalize()`，而不是简单的 `setRealValue()`。这样做的原因是：

- `9kHz + 6MHz` 后，显示应规范化为 `6.009MHz`。
- 如果继续强保留原显示单位，就会退化成 `6009kHz`。

这也是本轮修复里最重要的行为调整之一。

### 2.5 `EditableWidget` / `IEditableTitleWidget`：标题栏步长编辑器

实现文件：`src/libs/controls/editablewidget.h/.cpp`

当属性声明 `stepEditable` 后，键盘标题栏会挂一个 `EditableWidget`，它里面有自己的 `QLineEdit stepEdit`。

其职责是：

- 在步长编辑获得焦点时，把键盘 receiver 切换到步长 adapter。
- 把主输入区的单位快捷键临时替换成步长编辑自己的快捷键。
- 通过事件过滤器把 Enter/Esc 与普通按键转发给步长 adapter。
- 失焦时自动提交，并通过 `focusedOut()` 通知外层恢复主输入区 receiver。

当前实现没有在键盘层引入额外的“显式输入目标”抽象，而是选择局部化处理：

- 步长编辑获得焦点后，`onFocus()` 重新配置 keyboard 的 receiver 和快捷键。
- `stepEdit` 的文本、光标、选区变化通过动态属性回流到步长 adapter。
- `BaseUnitAdapter::specialKeyHandled` 和 `cursorPosCrossUnit` 再回写到 `stepEdit`。

这使得主输入区和步长编辑区都能复用同一套 adapter 协议。

## 3. 组装层：`createKeyboardBase()` 如何把它们接起来

实现文件：`src/libs/business/utils.cpp`

`createKeyboardBase()` 是软键盘体系的实际组装入口。它负责把“键盘外壳 + 主 adapter + 步进控制器 + 可选步长编辑器 + 属性同步”串起来。

组装顺序大致如下：

1. 创建 `TouchNumKeyboard`。
2. 按 `cfg.unitType` 创建主 adapter。
3. 设置 decimals、初始值、validator。
4. 创建 `StepController` 并绑定主 adapter。
5. 如果 `stepEditable` 为真，再创建步长 adapter 与 `EditableWidget`。
6. 为主 adapter 填充快捷单位按钮并设为 keyboard receiver。
7. 如果 property 上已有 `currentUnit`，用它初始化主输入区显示。
8. 把 adapter 与 keyboard 的显示更新信号连起来。

### 3.1 主输入区初始化

主输入区初始文本来自：

- `cfg.value` 对应的 `adapter->setRealValue(cfg.value)`。
- 如果 property 身上缓存了 `currentUnit`，则再额外用该单位构造一次显示文本。

因此“当前值”与“当前显示单位上下文”是分开存储的：

- 值在 property 的 `value` 中。
- 单位上下文在 property 的 `currentUnit` 属性中。

### 3.2 步长编辑区初始化

步长区不会简单地复用主输入的当前显示文本。

当前策略是：

- 频率/时间类步长先按步长自身数值规范化显示，例如 `4000000` 显示为 `4MHz`。
- 功率步长使用 `PowerStepUnitAdapter`，固定显示为 `XdB`。

这避免了两类历史问题：

- 频率步长被机械复用成 `0.004GHz` 这种可读性很差的格式。
- 功率步长显示虽然像 `XdB`，但提交时却因为 adapter 不认识 `dB` 而完全不生效。

### 3.3 功率主输入的特殊步进处理

功率主输入在 `createKeyboardBase()` 中保留了一个 `stepBy` 特殊分支：

- 步进动作完成后，通过延迟回写把显示单位拉回当前功率单位上下文。

原因是功率主输入不应像频率/时间那样自动在不同单位间跳变；`dBm`、`dBmV`、`dBμV` 对用户来说是显式选择的物理单位上下文，不应在普通步进中被“聪明地改掉”。

### 3.4 普通属性键盘与 C/S Minibar 键盘的边界

当前 `prepareNumericKeyBoard(PropertySystem::IProperty*, QObject*)` 会把 `triggerObj` 传给 `createKeyboardBase()`，后者直接执行：

1. `new TouchNumKeyboard(parent)`
2. 用 trigger widget 作为 keyboard parent

行为边界补充：

- property 路径和 widget 路径共用同一个 `TouchNumKeyboard` / adapter / step controller 内核，但只有 property 路径会在 `prepareNumericKeyBoard(property, triggerObj)` 中把 `Step` 来源的 `editingFinished` 立即透传给业务属性。
- 对纯 widget 路径，如果业务也需要“按 stepUp/stepDown 后立刻走设备/API 写回”，必须在调用侧显式监听 `BaseUnitAdapter::editingFinished` 并只对 `EditingTrigger::Step` 做即时回写；不要误以为 `keyboard->finished(Accepted)` 能自动覆盖这类需求。

软键盘本身并不要求 parent 一定是 `MainWindow`。只要调用方传入的是仍然存活的
`QWidget`，键盘就可以挂在对应 host 之下。

对于 `QTableView` 这类“一个 QWidget 承载多个虚拟编辑单元”的控件，业务触发对象和位置锚点必须分开：

- `prepareNumericKeyBoard(table, cfg)` 仍使用整个 item view 作为 trigger，使键盘关闭后的短时防重复点击覆盖 `viewport()`。
- cell 本身不是 QWidget，可在 `table->viewport()` 下创建透明、无焦点的临时 child，并把几何设置为 `table->visualRect(index)`。
- 在 `open()` 前调用 `TouchNumKeyboard::setAnchorWidget(cellAnchor)`，键盘即复用与 LabelButton 相同的 anchor 定位和屏幕钳位算法。
- 临时 anchor 应随 keyboard 销毁清理；不要把 cell 的全局坐标硬编码进调用侧，否则滚动、多屏、DPI 和 Wayland visual geometry 会再次分叉。

当前 Minibar 已经是独立 `SGStudioMiniBar` helper，不再存在 in-process
`MiniBarWindow`：

1. 主进程的 MainWindow 仍然存在，只是在 Minibar 可见期间隐藏。
2. helper 的 `RemoteMiniBarWindow` 通过
   `PropertyBindingHelper::prepareNumericKeyBoard(anchor, config)` 创建 helper-local
   `TouchNumKeyboard` 和 adapter。
3. 键盘提交后，helper 发出 typed IPC intent；共享 property、runtime 和设备 writeback
   仍全部由 main 持有，不存在 helper 直接共享 property 的链路。
4. Win32 使用普通 helper-owned 键盘；Wayland 使用 managed layer-shell keyboard
   overlay。host 必须把键盘及其 native/overlay 对象视为 owned interaction，避免
   outside-click collapse 误伤编辑会话。
5. 这条键盘路径不创建 `MessageDialog`。远程请求失败通过 RSP/snapshot
   回滚、重试或刷新 UI，不把主进程消息框转发到 helper。

这些属于 helper window/IPC protocol 边界，不属于 `TouchNumKeyboard` 本体职责。

## 4. 属性系统如何与软键盘保持一致

### 4.1 `currentUnit` 是 property 的可通知显示上下文

软键盘关闭前，当前主 adapter 的显示单位会写回 property：

- 在用户确认编辑成功时也会尽早写回一次。
- 在键盘 `finished` 时只会在“本次提交未被业务层同步修正”的情况下补写一次。

这样做的目的是让以下场景共用同一单位上下文：

- 再次打开软键盘。
- 步进键更新后的显示。
- 主界面 LabelButton 的文本显示。

`currentUnit` 当前由 `IProperty` 声明为带 `currentUnitChanged` 通知的 `Q_PROPERTY`。既有的
`property("currentUnit")` / `setProperty("currentUnit", ...)` 调用仍保持兼容，但单位消费者不应再监听
宽泛的显示文本变化或临时 keyboard adapter；应监听 property 上的 `currentUnitChanged`。

例如 `CommonPanel` 的 RMS 始终保存 dBm 基准值，只在 `Level.currentUnitChanged` 时通过
`PowerUnitAdapter` 重绘为 `dBm/dBmV/dBμV`。显示单位变化不进入 Tx runtime，也不触发设备重配或
波形 RMS 重算。

### 4.2 `displayText` 不是 UI 自己格式化出来的

`NumericProperty::updateDisplayText()` 当前会：

1. 根据 metadata 的 unit 新建一个 adapter。
2. 读取 property 属性 `currentUnit`。
3. 用 `setRealValue()` 或 `setRealValueAndNormalize()` 生成当前显示字符串。
4. 调用 `metadata()->setDisplayText()` 写回。

然后 `PropertyBindingManager` 监听 `displayTextChanged`，把这个文本推到 `LabelButton`。

这个链路的意义是：

- 显示字符串由业务属性层统一生成。
- 绑定控件只消费 `displayText`，不自行推断单位。

否则就会出现经典问题：

- 软键盘里看到的是 `30.009MHz`。
- 界面按钮却自己按默认单位格式化成 `30009kHz`。

### 4.3 业务层同步修正后的单位恢复

实现文件：`src/libs/business/utils.cpp` 中的 `prepareNumericKeyBoard(PropertySystem::IProperty*, QObject*)`

这一层现在除了负责“把键盘提交结果写回 property”，还负责处理一个很具体的同步修正边界：

- 用户在键盘里输入了一个带单位的值，例如 `99999999999GHz`。
- `BaseUnitAdapter::editingFinished` 先把本次提交值和当前单位写回 property。
- 紧接着业务层在同一个 `property->editingFinished()` 链里调用自己的配置/约束逻辑，把值夹回合法范围，或者直接回滚到编辑前原值。

如果这里不额外处理，property 身上的 `currentUnit` 会继续停留在本次非法编辑的单位上。结果就是：

- 合法值虽然已经回来了，但按钮文本和下次打开键盘时仍可能显示成 `0.01GHz` 这类“数值合法、单位上下文错误”的状态。

当前公共修复策略是：

1. 打开键盘时，记录本次编辑会话的初始 `value/currentUnit/displayText`。
2. 键盘提交时，记录“刚刚提交的数值”和“本次是否改了单位”。
3. 如果键盘仍存活时收到了 `property->valueChanged`，并且新值不同于刚刚提交的值，则认定为业务层同步修正。
4. 如果修正结果等于编辑前原值，则恢复编辑前的 `currentUnit` 和 `displayText`。
5. 如果修正结果不是原值，而是新的合法值，则对 `Frequency/Time` 这类自动规范化单位的属性清空本次编辑残留单位，再按合法值重新归一化显示。
6. 键盘关闭时，如果本次提交已经被业务层修正，则禁止 `finished` 再把错误单位写回 property。

这样做之后，公共层可以统一覆盖 AM、FM、Pulse、Ramp 等“键盘提交后业务立即修正值”的路径，而不需要在每个业务模块里单独补“清空单位”或“恢复单位”的局部逻辑。

### 4.4 文本提交与步进必须分开建模

实现文件：`src/libs/business/utils.cpp`

2026-05 这轮修复进一步证明：`BaseUnitAdapter::editingFinished` 只是“adapter 完成一次输入语义”的信号，不能再把它简单等同于“可以立刻触发业务提示”。当前公共层已经把两类来源显式拆开：

1. `BaseUnitAdapter` 在回车提交前标记 `EditingTrigger::Commit`。
2. `StepController` 在步进回写前标记 `EditingTrigger::Step`。
3. `prepareNumericKeyBoard(...)` 收到 adapter 的 `editingFinished` 后，总是立即同步 `value/currentUnit/displayText` 到 property。
4. 如果来源是 `Step`，则保留既有语义，立即 `emit property->editingFinished()`，因为这一路本来就没有“键盘即将关闭”的生命周期风险。
5. 如果来源是 `Commit`，则只把 `pendingPropertyEditingFinished` 记到本次键盘会话里，等键盘 `finished(QDialog::Accepted)` 后再通过 `QTimer::singleShot(0, property, ...)` 发出 `property->editingFinished()`。

这样拆开的原因很具体：

- 文本回车提交时，业务链里可能同步弹出 `MessageDialog`，而此时软键盘 `Qt::Popup` 还活着；如果继续在同一栈里进业务提示，极易打到 popup 关闭、焦点切换与对象销毁的临界区。
- 步进键则不同。它需要在键盘仍保持打开时立即触发约束逻辑和大波形提示；若把这一路也延后，就会出现数值一直增加但没有任何提示的回归。

因此，当前公共策略不是“全部延后”或“全部立即”，而是“值同步立即，业务确认按来源分流”。

## 5. 当前交互语义

以下内容描述当前真实行为，而不是最早设计稿中的目标行为。

### 5.1 默认选区

- 键盘打开后保留当前单位显示。
- 默认选中数值部分，包含前导负号，不选中单位后缀。
- 这样首个输入键会整体替换当前数值，但仍保留单位上下文。

### 5.1a +/- 的起始输入语义

- 当整段数值部分被选中时，`+/-` 按“开始新输入”处理，并一律进入 `-` 起始态，同时保留当前单位上下文；不会再出现“负值时清空、正值时变负号”的双重语义。
- 若不是整段数值被选中，而是局部编辑或已有光标位置，则仍沿用原来的 `+/-` 切换逻辑。

### 5.2 单位按钮与实体键盘缩写

- 频率、时间类支持常用单字母缩写，物理键盘单位键可直接承担“覆盖单位 + 立即确认”的职责。
- 软键盘显式单位按钮优先级高于手敲缩写。
- Bare Enter 会继承当前显示上下文单位，而不是盲目退回 base unit。

### 5.3 非法输入与 Esc

- 非法单位或非法文本不会更新 `realValue`。
- 键盘会保留当前错误文本，并通过 `specialKeyHandled` 全选回显。
- `Esc` 会恢复上一次合法文本，并按当前 `EscKeyPolicy` 关闭键盘。当前默认策略是 `Close`。

### 5.4 单位区保护

- 当前实现仍阻止 `Delete` 直接删掉单位。
- 光标如果越过单位区，会通过 `cursorPosCrossUnit` 回到数字区边界。

因此它仍然是“数字优先编辑、单位受保护”的模型，而不是完全自由文本框。

### 5.5 步进与步长编辑

- 上下键、PageUp/PageDown 由 `BaseUnitAdapter` 发出 `stepBy(realValue, step)`。
- `StepController` 负责计算新值并规范化刷新显示。
- 步长编辑区确认后，新的步长会回写到 `StepController::singleStep()`。
- 功率步长与功率主输入显式分离：前者固定 `dB`，后者保留当前功率单位上下文。

## 6. 当前几个容易踩坑的点

### 6.1 不要把 `currentUnit` 只留在 keyboard 内部

如果只保存在 adapter 内部，不写回 property，那么：

- 再次打开键盘时会丢失单位上下文。
- `NumericProperty` 生成的 `displayText` 会退回默认单位。

### 6.2 不要让 LabelButton 自己做单位格式化

正确做法是：

- 属性层统一维护 `displayText`。
- UI 绑定层只消费 `displayTextChanged`。

否则主界面和软键盘一定会出现不同步。

### 6.3 不要复用 `PowerUnitAdapter` 处理功率步长

原因前面已经提过：

- `PowerUnitAdapter` 处理的是功率物理量。
- 功率步长处理的是 dB 增量。

二者语义不同，混用后要么显示不对，要么提交不生效。

### 6.4 不要把 `setRealValue()` 与“用户确认输入”混为一谈

`setRealValue()` 是“保留当前显示上下文”的接口。

如果把它直接拿来处理步进、确认输入、快捷单位切换，频率/时间类就会卡在旧显示单位里，无法回到更合理的规范化结果。

### 6.5 不要在 `emit editingFinished(...)` 之后再回写 sender 状态

这轮已经踩实的坑是：一旦 `editingFinished` 的槽链里同步弹出提示框，adapter、step controller 或焦点控件都可能在信号返回前被关闭流程销毁。

因此像“发完信号后再把 trigger 重置回 `Unknown`”这类写法，虽然看起来只是一个无害的清理动作，实际上会在同步提示链里命中 use-after-free。当前正确做法是：

- 在下一次发信号前覆盖新的 trigger。
- 不要求本次发信号后立即清零。

### 6.6 不要跨 `KeyPress/KeyRelease` 持有原始 `focusWidget` 指针

`TouchNumKeyboard` 的数字键和快捷键本质上是向当前焦点控件发送合成按键。如果 `KeyPress` 触发的业务链同步弹出提示并销毁了焦点控件，那么继续对旧指针发送 `KeyRelease` 就会崩溃。

当前实现已经改成：

- 用 `QPointer<QWidget>` 弱引用持有 `QApplication::focusWidget()`。
- `KeyPress` 返回后再次判活，再决定是否继续发送 `KeyRelease`。

2026-07 follow-up: in hosted overlay / layer-shell scenarios, a keyboard button may still receive pointer press/release while `QApplication::focusWidget()` has escaped the keyboard subtree. When the keyboard has an active receiver/adapter, `TouchNumKeyboard` falls back to its internal edit widget as the synthetic-key target. The edit widget's existing event filter still routes the key to the current receiver, so normal main-input editing and step-edit receiver switching stay on the same adapter path. If no receiver is configured, the old focus-widget behavior is preserved.

后续类似问题，优先检查这里，而不是先怀疑 Qt 输入法或消息循环。

### 6.7 软键盘链路里不要把任意提示都直接塞回当前点击栈

对于 `MessageDialog`，要先判断这条提示究竟属于哪一类：

- 若只是普通页面上的独立提示，异步 `showMessage()` 仍是默认手段。
- 若它来自 MainWindow 中的软键盘提交或其他 popup/焦点敏感链路，则要先分析是否需要同步 `execMessage()`，或者像 QuickWaveform 那样先脱离当前事件栈，再同步 `execMessage()`。

当前 C/S Minibar 的远程请求不走这条提示策略：helper 不创建
`MessageDialog`，主进程隐藏期间的提示也由 suppression 直接拒绝或丢弃。

这一点与 `currentUnit`、`displayText` 一样，已经属于软键盘体系的维护边界，而不只是 Dialog 组件自己的实现细节。

### 6.8 非合成 X11 的顶层键盘首帧

`TouchNumKeyboard` 在 Linux X11 上是普通 `Qt::Tool` 顶层窗口；在 Wayland 上则是
managed overlay。X11 目标机可能有 Composite 扩展但没有 compositor，此时不能把
`windowOpacity(<1.0)` 当作可靠的透明合成能力。

- Linux（X11 顶层窗口和 Wayland overlay）统一使用不透明窗口（opacity `1.0`）；其他
  平台保留既有外观设置。
- 顶层键盘必须在第一次 `setVisible(true)`/native map 之前计算并设置最终位置，不能先
  显示默认几何再 `move()`。后者在非合成 X11 上会暴露新窗口 backing store 的中间帧，
  形成桌面或其他控件的残影。
- 这条规则只约束窗口呈现顺序，不要求预创建键盘、全局重绘、延时或安装 compositor。

## 7. 维护建议

后续如果还要扩展这套体系，建议遵守以下边界：

1. 新单位类型优先新增 adapter，不要往 `TouchNumKeyboard` 里写业务判断。
2. 任何“显示与内部值不同步”的问题，先检查 `BaseUnitAdapter`、`currentUnit`、`displayText` 三段链路，不要先怀疑 `LabelButton`。
3. 步长若与主输入语义不同，应像 `PowerStepUnitAdapter` 一样单独建模。
4. 如果未来真的要放开“单位区自由编辑”，先重新审视 `resetSelectionToNumericPart()`、`cursorPosCrossUnit`、`Delete` 拦截和 `splitNumberAndUnit()` 的整体契约，不要只改一个点。
5. 如果回归只出现在“回车提交”而不出现在“上下步进”，优先检查 `EditingTrigger` 分流和 `pendingPropertyEditingFinished`，不要直接去改业务层的提示逻辑。
6. 如果崩溃堆栈落在输入控件、step controller 或合成按键发送后半段，优先怀疑“同步提示导致对象在槽链中死亡”，不要先加延时掩盖。

## 8. 相关文件

- `src/libs/controls/touchnumkeyboard.h/.cpp`
- `src/libs/controls/baseunitadapter.h/.cpp`
- `src/libs/controls/editablewidget.h/.cpp`
- `src/libs/controls/stepcontroller.h/.cpp`
- `src/libs/business/utils.cpp`
- `src/libs/business/numericproperty.h`
- `src/libs/business/propertybindingmanager.cpp`
- `src/libs/business/frequencyunitadapter.cpp`
- `src/libs/business/timeunitadapter.cpp`
- `src/libs/business/powerunitadapter.cpp`
- `src/libs/business/powerstepunitadapter.cpp`

