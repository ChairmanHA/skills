# TouchNumKeyboard 统一改为 Overlay 子控件：跨平台（Wayland/Win32）一致的重构方案

> 目标：让触摸数字键盘在 **Wayland（树莓派）** 与 **Win32** 上行为一致：
> - 顶部空白/标题区按住可拖拽（Mouse + Touch）
> - 弹出期间具备“模态感”：**点击外部不触发底层控件**（可配置是否点外部关闭）
> - 关闭时通过 `accept()/reject()/done(result)` 发出 `finished(int)`（外部逻辑保持不变）
> - 不再依赖 `Qt::Popup` 或任何“全局鼠标钩子/抓取”类方案

## 1. 背景与现状

当前 `Controls::TouchNumKeyboard`（见 [src/app/ui/touchnumkeyboard.h](src/app/ui/touchnumkeyboard.h)、[src/app/ui/touchnumkeyboard.cpp](src/app/ui/touchnumkeyboard.cpp)）是 `QDialog`，并使用：
- `setWindowFlags(Qt::Popup | Qt::FramelessWindowHint | ...)`
- 通过 `mousePressEvent/mouseMoveEvent` + `move()` 做拖拽
- 通过 `Utils::MousePressEater` 捕获“点击外部”以便关闭

已知问题：
- **Wayland**：顶层 surface 的位置由 compositor 决定，`move()` 对顶层窗口可能无效，导致拖拽失效或不一致。
- “顶部空白区域拖不动”：鼠标/触摸事件常被标题区子控件吃掉（Qt 事件不冒泡到父控件）。
- 依赖 `Qt::Popup` 才容易实现点外部关闭，但 `Qt::Popup` 又会加剧 Wayland 上的定位/拖拽不可控。

而项目中已有 `Controls::DraggableFramelessDialog`（见 [src/app/ui/draggableframelessdialog.cpp](src/app/ui/draggableframelessdialog.cpp)）已经实践了关键点：
- **强制作为 parent 内部子控件**：`setWindowFlags(Qt::Widget | Qt::FramelessWindowHint | Qt::CustomizeWindowHint)`
- 标题栏作为 drag handle，Mouse + Touch 都可拖
- show 时居中 / clamp 到 parent 可视区

结论：TouchNumKeyboard 想要 Wayland/Win32 一致，必须从“顶层弹窗”转为“parent(mainwindow) 内 overlay”。

## 2. 目标架构（推荐）

### 2.1 三个角色

1) **Overlay 容器（遮罩层）**：Overlay 覆盖 MainWindow 全客户区
- 吞掉外部区域的 Mouse/Touch（实现“模态感”：外部点击无反应）
- 可选：外部点击触发关闭（RepeatEsc/RepeatEnter/None）
- 可选：半透明 dim 背景

2) **内容弹层（TouchNumKeyboard 本体）**：遮罩层的子控件
- 仍然继承 `QDialog` 以复用 `finished(int)` / `accept/reject/done`
- 但 window flags 必须是 `Qt::Widget`（非顶层）

### 2.2 为什么此架构跨平台一致

- 拖拽移动的是 `parent` 坐标系下的子控件：Wayland 不限制。
- 遮罩层拦截事件完全发生在应用内部：Wayland/Win32 一致。
- 不需要 `Qt::Popup`、不需要全局鼠标钩子/抓取：可靠性与安全性更高。

## 3. API 与行为约定

### 3.1 TouchNumKeyboard 对外仍保留

保持现有使用方式尽量不变（调用点主要见：
- [src/app/business/propertybindinghelper.cpp](src/app/business/propertybindinghelper.cpp)
- [src/app/ui/freqpanel.cpp](src/app/ui/freqpanel.cpp)

当前调用方式：
- `keyboard->open();`（非阻塞）
- 通过 `connect(keyboard, &QDialog::finished, ...)` 收尾

重构后建议语义：
- `open()`：在主窗口 overlay 中打开（非阻塞），并开启“模态遮罩”。
- `exec()`：仍可提供阻塞语义，但内部不再依赖顶层窗口/Popup；改为进入本地 `QEventLoop`，直到 `finished(int)`。
- `showGlobal()`：可以保留接口但改为调用 `exec()`；它不再是“系统级全局模态”，而是“应用内阻塞”。（跨平台一致更重要）

### 3.2 finished/accept/reject 规则（你提到的关键）

- **任何关闭路径**都必须调用 `done(result)` 或 `accept()/reject()`，以触发：
  - `finished(int)`
  - `accepted()`/`rejected()`
- 禁止仅 `hide()` 或 `close()` 但不 `done()`（否则调用点拿不到稳定的 finished）。

### 3.3 外部点击策略（覆盖层实现），这次不用，因为accept() / reject() 在Baseunitadapter中处理的

将现有 `PressOutsidePolicy` 语义迁移到 overlay：
- `NoneRepeat`：吃掉外部点击，什么都不做（“外部无反应”）。
- `RepeatEnter`：外部点击等价于 `accept()`（或发送 Enter，再 accept；建议直接 accept）。
- `RepeatEsc`：外部点击等价于 `reject()`（默认）。

注意：这里的“外部”定义为 overlay 覆盖区域内、但不在键盘几何范围内。

## 4. 拖拽（顶部空白区域可拖）实现策略

### 4.1 总原则：用明确 drag handle + 同时支持 Mouse/Touch

当前 TouchNumKeyboard 有自己的标题区（`nameLabel` + spacer + 可插入 `titleWidget()`）。

推荐实现：
- 把“标题区”视为 drag handle：
  - Mouse: `MouseButtonPress/Move/Release`
  - Touch: `TouchBegin/Update/End/Cancel`
- 对标题区涉及的子控件（至少 `nameLabel` 与动态插入的 `titleWidget()`）安装 `eventFilter`，在 filter 内统一走拖拽状态机。
- 对键盘自身 `mousePressEvent` 也保留一条通路：当点击落在标题区的“空白处”（layout spacer 区域）时，事件往往直接给到父 widget，这时也应允许拖拽开始。

### 4.2 复用现有 DraggableFramelessDialog 的实现经验

`DraggableFramelessDialog` 已经实现了：
- `resolveGlobalPosFromEvent()`（mouse/touch 统一取 global/screen pos）
- `shouldStartDrag(handle, localPos)`（避免 slider/button 等交互控件触发拖拽）
- `doMoveByDelta(delta)` + `clampToParentVisibleRect()`

建议：
- 把这些“拖拽通用能力”抽成可复用组件（见 5.2），TouchNumKeyboard 直接配置 drag handle。

## 5. 详细重构步骤（按最小风险拆分）

### 5.1 第一步：引入 Overlay 容器（不动 TouchNumKeyboard 内部逻辑）

新增一个遮罩层类（建议位置：`src/app/ui/overlaycontainer.{h,cpp}`）：
- 继承 `QFrame` 或 `QWidget`
- 覆盖 parent 客户区：`setGeometry(parent->rect())`，并监听 parent resize
- `setAttribute(Qt::WA_AcceptTouchEvents)`
- `setFocusPolicy(Qt::StrongFocus)`（必要时抢焦点）
- `eventFilter/override event()`：
  - 对 overlay 自身区域：吞掉 Mouse/Touch/Wheel 等
  - 若事件落点在子控件（keyboard）区域：让正常事件分发给子控件
  - 若事件落点在外部区域：根据 `PressOutsidePolicy` 决定是否 `accept/reject` 并 `return true`

Overlay 与 keyboard 的关系：
- `overlay->setActiveDialog(QDialog* dialog)`
- overlay 连接 `dialog->finished(int)`：隐藏自己、解除引用
### 5.2 第二步：改造 TouchNumKeyboard 为 Mainwindow 子控件
去掉 `Qt::Popup`，改为 `Qt::Widget`，并改为 parent 内部子控件：

### 5.4 第四步：模态体验一致化（你提到“外部点击无反应”）

Overlay 天生能做到：
- 外部点击：被 overlay 吞掉，不触发底层任何控件
- 外部点关闭：也容易（按 policy 调 `reject/accept`）

额外建议（可选）：
- dim 背景：提升用户感知“当前在编辑”
- Esc 键：overlay 或 keyboard 统一处理，映射到 `reject()`

### 5.5 第五步：兼容现有调用点的边角行为

在 [src/app/business/propertybindinghelper.cpp](src/app/business/propertybindinghelper.cpp) 里，关闭后会给 triggerObj 安装 200ms blocker，避免“点穿/重复触发”。

overlay 化后：
- 很多点穿问题会减少，但关闭瞬间仍可能存在“释放事件落到底层”的情况（取决于打开/关闭时机）。
- 建议保留该 blocker 逻辑一段时间作为保险；等验证稳定再考虑简化。

## 6. 验收清单（Wayland/Win32）

- 键盘显示位置：首次居中；再次打开 clamp 到父窗口可视区
- 标题区：
  - 鼠标按住拖动可移动
  - 触摸按住拖动可移动
  - 标题区内的交互控件（如 EditableWidget）不会被误判为拖拽（或按需求设定）
- 模态：键盘显示期间点击底层控件无反应
- Outside policy：NoneRepeat/RepeatEnter/RepeatEsc 三种都能正确触发 `finished(int)`
- 生命周期：关闭后键盘 `deleteLater`，overlay 自动隐藏/复位

## 7. 建议的文件与职责拆分（最终落地）

- `src/app/ui/overlaycontainer.{h,cpp}`：遮罩层，吞事件/可选点外关闭
- `src/app/ui/draggableframelessdialog.{h,cpp}`：升级为可配置 drag handle 的通用拖拽基类（或新增新基类）
- `src/app/ui/touchnumkeyboard.{h,cpp}`：改为 overlay 模式打开；拖拽绑定到标题区；外部点击由 overlay 管理
- `src/app/business/propertybindinghelper.cpp`：只负责创建/配置键盘，不再管理全局点击捕获（逐步收敛）

---

## 决策点（需要你确认，但不影响整体方向）

1) **Overlay 覆盖范围**：确认覆盖 MainWindow 全客户区
- 覆盖全客户区：更像真正模态（包括侧边栏等）

2) **外部点击默认策略**：
- 你希望默认“无反应”还是“点外关闭（Esc）”？（目前默认 RepeatEsc）确认维持默认

3) **阻塞调用是否还需要**：
- 目前调用点主要用 `open()` + finished；若不再需要真正阻塞，可弱化 `exec()/showGlobal()`。 确认可以弱化阻塞调用，能先保证`open()` + finished 有能力模拟模态即可。
