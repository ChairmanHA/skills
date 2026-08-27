# Wayland 下 Frameless 弹窗的统一实现模式：Overlay 子控件 + 模态遮罩

## 结论（TL;DR）
在 Wayland（例如 Raspberry Pi 桌面环境）下，**不要指望顶层（top-level）frameless/popup 的 `move()` 能可靠移动窗口**。

想要 Win32 + Wayland 都一致可拖拽、可触摸、可控行为，推荐把“Dialog”做成：
- **宿主窗口内的子控件**（`Qt::Widget`，有 parent，且避免 `Qt::Popup` 作为顶层）；
- 外面套一层 **overlay 遮罩容器** 拦截外部输入，以“模拟模态”。

这套模式已在 `TouchNumKeyboard` 改造后验证可用。

但是要立刻加一个边界：**不要把这套模式无差别推广到所有依赖树莓派系统键盘的普通文本输入 dialog。**

在 Raspberry Pi Wayland（当前实测为 `labwc + wf-panel-pi + squeekboard`）下：
- 应用内 `OverlayContainer` 只是 SGStudio 自己窗口树里的 child widget；
- 系统键盘更接近 compositor / input-method 协议一侧管理的独立 surface；
- 两者都“看起来像 overlay”，但**不属于同一层抽象，也不共享同一种 parent/child 语义**。

这轮实测已经证明：
- `TouchNumKeyboard` 保持 host 内 child overlay 模式，Wayland 下拖拽正常；
- 但把 `Controls::Dialog` 基类整体也改成同样的 hosted child dialog 后，`EthConnectDialog` 里的树莓派系统键盘会失效；
- 回退 `Dialog/Keyboard/EthConnectDialog` 相关修改后，系统键盘立即恢复。

---

## 1. 背景：为什么 Wayland 下顶层 `move()` 可能失效
Wayland 的核心原则之一是：**顶层 surface 的屏幕位置由 compositor 决定**。

因此：
- 对**顶层窗口**（无 parent、或使用 `Qt::Window/Qt::Dialog/Qt::Popup` 等方式变成 top-level surface），客户端调用 `move()`/`setPosition()` 可能被 compositor 忽略。
- 对**有系统标题栏的窗口**，用户拖拽标题栏属于 compositor 的交互式移动流程，所以“拖得动”。
- 对 **frameless** 窗口，由于没有系统标题栏，往往无法触发 compositor 的系统移动交互，于是“看起来不能拖”。

反过来：
- **parented 子控件** 的移动是客户端内部坐标系行为（同一个顶层 surface 内部布局/绘制），不触碰 Wayland 的限制，所以 `move()` 稳定可用。

---

## 2. 推荐模式：Overlay 子控件 + 遮罩模拟模态

### 2.1 结构
- `Host`：通常选 MainWindow（或其 central widget / client area）。
- `OverlayContainer`：覆盖 host client area 的透明/半透明遮罩层。
- `DialogWidget`：逻辑上仍可继承 `QDialog`，但运行形态是 overlay 的子控件（`Qt::Widget` flags）。

效果：
- Dialog 在 host 内可自由 `move()`（Win32 + Wayland 一致）。
- Overlay 截获外部点击/触摸/滚轮，避免下层控件收到输入，从而“像模态一样”。

### 2.2 生命周期与信号语义
为了让既有业务代码继续依赖 `finished(int)`：
- 关闭必须走 `accept()/reject()/done(int)`。
- overlay 的 outside-click 只决定“是否触发 accept/reject（或仅拦截不关闭）”。

项目约定（已在 `TouchNumKeyboard` 使用）：
- 默认 outside-click 行为仍是 `RepeatEsc`（即：外部点击相当于 Esc）。
- 也支持 `RepeatEnter`、或 `NoneRepeat`（只拦截不关闭）。

---

## 3. 关键实现要点（Checklist）

### 3.1 Dialog 必须是子控件，而不是顶层 popup
- 推荐 flags：`Qt::Widget | Qt::FramelessWindowHint | Qt::CustomizeWindowHint`
- parent 设为 host 或 overlay。
- 避免使用 `Qt::Popup` 造成顶层语义回归。

### 3.2 拖拽只允许在“标题/空白区”发生
动机：避免拖拽与按钮、滑条、输入框冲突。

实践建议：
- 明确一个 drag handle（例如 title bar 的 label 或 title widget）。
- 在 handle 上安装 `eventFilter`，处理 Mouse + Touch：
  - press/begin：记录 `pressedPoint(global)` 与 `originalPos(pos)`
  - move/update：`move(originalPos + (global - pressedPoint))`
  - release/end：停止拖拽
- 对交互子控件（button/slider/lineEdit/combo/spinbox）要过滤掉拖拽开始。

### 3.3 位置要 clamp 在 host 可视区域
因为是子控件移动，用户可把 dialog 拖出边界。

建议每次 move 后做一次 clamp：
- `x ∈ [0, hostW - dialogW]`
- `y ∈ [0, hostH - dialogH]`

并在首次 show 时做一次“居中 + clamp”。

### 3.4 overlay 必须吞掉外部输入（mouse/touch/wheel）
遮罩层要拦截：
- MousePress/Release
- TouchBegin/Update/End
- Wheel

规则：
- 若事件点落在 active dialog 几何区域外：
  - 发出 `outsideClicked()`（通常在 press/begin 发一次即可）
  - 返回 `true` 吞掉事件，阻止下层控件响应

### 3.5 Focus 与输入
- overlay + dialog 显示后，主动把焦点给 dialog 内的输入控件（例如 `QLineEdit`）。
- 对虚拟键盘场景，建议禁用输入控件的右键菜单，避免菜单弹出导致关闭/析构时机异常。

---

## 4. 常见坑与规避
- **回归顶层窗口**：一旦 dialog 变成 top-level（无 parent 或 popup/window flags），Wayland 下移动问题会复现。
- **outside-click 触发多次**：建议只在 press/begin 时发一次 `outsideClicked()`，release 继续吞掉即可。
- **overlay 覆盖范围不对**：确保 overlay 以 MainWindow client area 为 parent，并在 resize 时自动填充。
- **finished(int) 丢失**：不要 `hide()` 代替关闭；统一走 `accept/reject/done`。
- **树莓派点击崩溃**：使用鼠标操作，刚刚show出keyboard，就立即点击overlay外部区域崩溃，使用touch触屏操作不会。
  - outside-click 处理在 `TouchNumKeyboard::open()` 的 outsideClicked lambda 中：
    1) 先向 `inputsReceiver` 发送合成按键事件（Enter 或 Esc）
    2) 然后再调用 `accept()` / `reject()` 关闭键盘 ！！！！这个步骤是错误的！！！！设计中keyboard只发出合成按键事件，真正处理事件关闭dialog的工作应该交给inputsReceiver去做。
  -  `inputsReceiver` 通常是 `BaseUnitAdapter`，其 `event(QEvent*)` 对 `Key_Return/Key_Enter` 会 `parentObj->close()/accept()`，对 `Key_Escape`（EscPolicy=Close 时）会 `parentObj->reject()`。
  - 于是：一次 outside-click 里会发生两次关闭：
  - 第一次：`BaseUnitAdapter` 在处理合成 Esc/Enter 时关闭 parent 对话框
  - 第二次：outsideClicked lambda 随后又调用 accept/reject
  - 这会真实导致 `QDialog::finished(int)` emit 两次。deleterlatter 也会调用两次，第二次调用时 Wayland下刚刚show出来还未稳定的窗口hide,会触发本身的析构流程，第二次调用deleterlatter时keyboard指针已经悬空，导致崩溃。
  - 编程规范：延迟回调捕获对象必须用 `QPointer`（或把回调的 QObject parent 设为目标对象本身），避免对悬空对象调用任何成员函数（包括 `deleteLater()`）。

---

## 5. 与树莓派系统键盘的边界

这一节非常重要：**系统键盘不是 SGStudio 自己 QWidget 树里的另一个“遮罩子控件”。**

### 5.1 从 Raspberry Pi Wayland 系统角度看，系统键盘更像什么

当前树莓派桌面组合是：
- compositor：`labwc`
- panel：`wf-panel-pi`
- system OSK：`squeekboard`

在这个体系下，更合理的理解是：
- `squeekboard` 是独立进程，不是 SGStudio 的 child widget；
- 它的显示与否，不取决于 SGStudio 里某个 `QWidget` 是否“画在最上面”；
- 它更依赖 compositor 侧是否认为：
  - 当前活动 surface 合法；
  - 当前文本输入目标合法；
  - 当前 text-input / input-method 协议状态已经建立。

所以即使视觉上它也像一个“全屏 overlay”，它也不是和 `OverlayContainer` 同类的东西。

### 5.2 为什么 `TouchNumKeyboard` 可以，系统键盘却不行

`TouchNumKeyboard` 是应用内自绘面板：
- 它完全属于 SGStudio 自己的 QWidget 树；
- 它的拖拽、outside-click、模态感，都是应用内部坐标系和事件分发问题；
- 把它改成 host 内 child overlay，正好避开了 Wayland 对顶层 frameless `move()` 的限制。

而普通 `QLineEdit` + 树莓派系统键盘不是这个问题：
- `QLineEdit` 需要的是“被 Wayland 输入法协议正确识别为当前文本输入目标”；
- 系统键盘需要的是“compositor 侧认可的活动输入上下文”；
- 这条链里，最关键的不是应用内 overlay 的视觉 stacking，而是 top-level / focus / seat / text-input 的协议状态。

因此：
- `TouchNumKeyboard` 成功，不代表所有 `Dialog` 都应该一起 child-overlay 化；
- 实测中，把 `Controls::Dialog` 基类整体改成 hosted child dialog 后，`EthConnectDialog` 的系统键盘就失效了；
- 只回退 `Dialog/Keyboard/EthConnectDialog` 这条链，而保留 `TouchNumKeyboard` 的 overlay 方案，两个场景就都恢复正常。

### 5.3 为什么这更像协议边界问题，而不是简单的遮挡问题

这轮排障里出现过一个很关键的信号：
- 系统键盘 DBus `sm.puri.OSK0.Visible` 可以是 `true`；
- 但屏幕上仍然没有看到系统键盘真正出现。

这说明：
- 问题不能简单理解为“我们自己的 overlay 把系统键盘盖住了”；
- 更可能是“系统键盘服务已经切到 visible，但当前并没有一个 compositor 认可的有效文本输入上下文，让它真正进入稳定显示态”。

换句话说：
- **应用内 overlay 主要影响的是 QWidget 事件和几何。**
- **系统键盘是否真正出现，更像受 Wayland text-input / input-method 协议状态支配。**

### 5.4 当前的工程结论

基于这轮实测，当前应当采用下面的边界：

- 适合用 `Overlay 子控件 + 遮罩模拟模态` 的场景：
  - `TouchNumKeyboard`
  - 其它纯应用内自绘工具面板
  - 不依赖系统输入法的局部浮层

- 不要直接套用这套模式的场景：
  - 依赖树莓派系统键盘的普通文本输入 dialog
  - 依赖标准 `QLineEdit` -> Qt input method -> compositor -> `squeekboard` 这条输入链的窗口

### 5.5 实践建议

如果未来又遇到“某个 frameless dialog 在 Wayland 下想拖动，同时里面还有普通文本输入框要拉起系统键盘”，不要先把它无脑并入全局 hosted child dialog 基类。更稳妥的判断顺序应该是：

1. 先问：这个窗口是“应用内自绘交互面板”，还是“系统输入法参与的普通文本 dialog”？
2. 如果是前者，优先考虑本文的 overlay child widget 模式。
3. 如果是后者，优先保留标准 top-level dialog 输入链，再单独想拖拽方案，不要先破坏 text-input 上下文。
4. 如果不得不混合两者，必须在目标树莓派桌面环境里实机验证，而不是只凭 QWidget 层面的直觉推广。

---

## 6. 非模态 About 页的窄例外（2026-07）

`AboutDialog` 使用 `Controls::Dialog::runAsShow()`，产品语义是非模态：用户打开
About 后仍可操作 MainWindow。因此它在 Wayland 下只复用本文的“hosted child +
标题栏拖动”部分，不创建模态遮罩：

- 仅在 Wayland 且存在 MainWindow parent 时，把 About 的 window flags 切换为
  `Qt::Widget | Qt::FramelessWindowHint | Qt::CustomizeWindowHint`。
- 复用 `Controls::WidgetMouseMoveTool`，只安装到 `m_pHeaderWidget` 及其直接
  `QLabel` 子控件；关闭按钮和内容区不作为拖动 handle。
- 拖动位置由 `WidgetMouseMoveTool` 限制在 parent 可见区域。
- show 后使用 parent 本地坐标重新居中并 `raise()`。
- 不使用当前会创建透明 modal overlay 的 `WaylandDialogMoveTool`，避免改变
  `runAsShow()` 的非模态行为。
- Windows 和非 Wayland 平台继续保留原有顶层 `Controls::Dialog` 路径。

这仍然遵守“不要全局 child-widget 化所有 Dialog”的边界：改动只存在于不包含
文本输入、也不依赖系统键盘的 About 页。
---



