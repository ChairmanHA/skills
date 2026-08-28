# SGStudio MessageDialog / Dialog 设计说明

---

## 1. 背景与目标

项目中默认仍采用“异步打开 + finished 信号回调”的消息弹窗方式，但从 2026-05 起，`MessageDialog` 已新增一个**窄范围同步接口** `execMessage()`，专门用于那些必须获得稳定阻塞边界、且不能承受 `showMessage()` 的“先关 popup、再异步 open”时序空档的调用点。

核心目标：

- **统一 UI 风格**：自定义无边框 `Controls::Dialog` + `TitleBar` + bottom 按钮区。
- **默认异步模态**：使用 `open()` 触发 `finished(int)`，在回调里继续业务逻辑。
- **保留同步例外**：在大波形确认、文件选择返回后的校验提示，或其他确实需要同步决策的少数 MainWindow 场景下，允许显式使用 `execMessage()`。
- **排除 C/S Minibar**：当前 `SGStudioMiniBar` helper 不承载 `MessageDialog`；MainWindow 隐藏期间，主进程提示由统一 suppression 直接拒绝或丢弃。
- **按钮结果码稳定**：用标准按钮 ID（`QDialogButtonBox::StandardButton` / `QMessageBox::StandardButton`）作为返回值 result。
- **支持两种回调模式**：
  - 旧模式：一个 `std::function<void(int)>`，外部通过 result 分发。
  - 新模式：每个按钮独立 `std::function<void()>`，内部按 result 路由。
---

## 2. 类与职责

### 2.1 `Controls::Dialog`

定位：通用对话框基类，负责：

- 无边框窗口、标题栏（`TitleBar`）
- 内容容器 `contentWidget()`
- 底部按钮区创建（标准按钮集合 or 按钮列表）
- 按钮点击 → `done(result)` → `finished(result)`
- 异步打开：`runAsync()` / `runAsShow()`

关键点：**`finished(result)` 的 result 完全由代码决定**，本项目通过 `done(id)` 让它等于标准按钮 ID。

### 2.2 `Controls::MessageDialog : Controls::Dialog`

定位：消息弹窗（Info/Question/Warning/Error…），负责：

- 统一宽度/文本区域布局
- 文本换行、动态高度计算（但宽度固定）
- 根据 `SG::EMsgType` 设置 title、模态、标题栏按钮
- 对外提供异步 `showMessage()` 与同步 `execMessage()` 两组 API：
  - 标准按钮集合 + 单回调
  - 按钮列表（独立回调）
  - 同步 `execMessage()`（返回标准按钮 result）

内部私有实现类：`Controls::Internal::MessageDialogPrivate`

---

## 3. 对外 API（推荐用法）

### 3.1 旧模式：标准按钮集合 + 单回调

```cpp
auto dialog = new Controls::MessageDialog(this);
dialog->setWindowTitle(tr("Confirm"));

dialog->showMessage(
    tr("Whether to exit the application?"),
    SG::EMsgType::MSG_QUESTION,
    QMessageBox::Yes | QMessageBox::No,
    [this](int result) {
        if (result == QMessageBox::Yes) {
            // ...
        }
    }
);
```

适用：快速写法；按钮逻辑简单；你愿意在一个 lambda 里 `if/switch`。

### 3.2 自定义按钮文案（推荐：按钮列表模式）

说明：已移除“标准按钮集合 + 自定义文字 + 单回调”的重载接口。若需要自定义按钮文案，请使用按钮列表模式（同时可精确控制按钮顺序）。

如果你仍想保留“单回调”的写法，可以把共同分发逻辑抽成一个 lambda，然后在每个按钮回调里调用它：

```cpp
using namespace Controls;

auto dialog = new Controls::MessageDialog(this);
dialog->setWindowTitle(tr("Confirm"));

auto onResult = [this](int result) {
  if (result == QMessageBox::Yes) {
    // ...
  }
};

QList<Controls::MessageButton> buttons;
buttons << Controls::MessageButton(QMessageBox::No,  tr("Cancel"), [onResult]() { onResult(QMessageBox::No); });
buttons << Controls::MessageButton(QMessageBox::Yes, tr("OK"),     [onResult]() { onResult(QMessageBox::Yes); });

dialog->showMessage(tr("..."), SG::MSG_INFO, buttons);
```

### 3.3 新模式：按钮列表（每个按钮独立回调）

```cpp
using namespace Controls;

auto dialog = new Controls::MessageDialog(this);
dialog->setWindowTitle(tr("Confirm"));

QList<Controls::MessageButton> buttons;
buttons << Controls::MessageButton(QMessageBox::No,  tr("Cancel"), nullptr);
buttons << Controls::MessageButton(QMessageBox::Yes, tr("OK"), [this]() {
    // 业务逻辑
});

dialog->showMessage(tr("Whether to exit the application?"), SG::MSG_QUESTION, buttons);
```

适用：

- 按钮多、每个按钮逻辑独立
- 不想写 `switch(result)`
- 希望按钮顺序由列表决定

说明：新模式内部仍然会产生 `finished(result)`，但 `runAsync(nullptr)` 不再传统一回调；按钮点击后的业务回调由内部 `m_buttonCallbacks[result]` 分发。

### 3.4 同步模式：`execMessage()`

```cpp
auto dialog = new Controls::MessageDialog(parent);
const int result = dialog->execMessage(
  tr("Expected full waveform size: %1. Do you want to continue?").arg(sizeText),
  SG::MSG_QUESTION,
  QMessageBox::Yes | QMessageBox::Cancel);

if (result == QMessageBox::Yes) {
  // continue
}
```

适用：

- 需要像系统 `QFileDialog::exec()` 一样，立刻拿到用户选择并在当前业务函数里同步分支。
- 当前调用点明确需要一个稳定的模态边界，不能接受 `showMessage()` 的“先清 popup，再异步 `open()`”时序空档。

不适用：

- 普通信息提示。
- 仅仅因为“写起来方便”就把所有消息框都改成同步调用。
- 试图用同步 `exec` 掩盖 parent/ownership、popup 协议或焦点生命周期问题。

---

## 4. 结果码（result）与按钮 ID 的约定

### 4.1 result 的来源

按钮点击统一走 `Controls::Dialog::onButtonClicked()`：

- 按钮上挂了 `property("id")`（标准按钮枚举值）
- 点击后调用：
  - 若有标准 id：`QDialog::done(id)`
  - 否则按 role 推导：`accept()/reject()/done(role)`

因此：

- **标准按钮**：`finished(result)` 的 result == 该标准按钮枚举值
- **非标准按钮（或 id 缺失）**：result 可能是 role 或 accept/reject 的默认值

### 4.2 为什么仍使用 `QMessageBox::Yes/No`

即使在“每按钮独立回调”的新模式下，`Yes/No/...` 仍有价值：

- 作为稳定的**按钮身份标识**（id），用于 `done(id)` 与 `finished(result)` 的一致性
- 作为回调路由表 `m_buttonCallbacks` 的 key
- 未来若仍需兼容“单回调模式”，外部仍可用 `result == QMessageBox::Yes` 判断

结论：这是 Qt 允许的用法（result 是 int），但并非操作系统强制机制；是项目/Qt 层面的约定实现。

---

## 5. 默认异步、同步例外与线程约束

### 5.1 异步打开

`Controls::Dialog::runAsync(cb)` 的行为：

- 若当前线程不是 UI 线程：`QTimer::singleShot(0, qApp, ...)` 切回 UI 线程再执行
- 设置 `m_aRunAsyncFunc = cb`
- 调用 `open()`（异步模态），最终会触发 `finished(int)`

### 5.2 同步打开

`MessageDialog::execMessage(...)` 当前语义是：

- 先复用同一套文本/按钮/标题配置逻辑。
- 执行前暂时把 `WA_DeleteOnClose` 关掉，避免 `exec()` 期间对象提前自删。
- 调用 `exec()` 同步阻塞当前调用点。
- 返回后恢复原来的 `WA_DeleteOnClose`，若原先是自动删除，则补一次 `deleteLater()`。

这个接口只解决“需要同步返回值”和“需要稳定阻塞边界”这两个问题；它**不会**替代对 parent、时序、owned popup 边界的正确建模。

### 5.3 C/S Minibar 的提示抑制边界

当前 Minibar 是独立 `SGStudioMiniBar` helper，不是把业务 panel 嵌入另一个
MainWindow host：

- helper 正常路径没有 `MessageDialog`、`showMessage()` 或 `execMessage()` 调用，
  IPC 也没有“转发弹窗”的命令。
- `MinibarHelperController` 在隐藏 MainWindow 前开启
  `GUIContext::popupPromptsSuppressed()`，恢复 MainWindow 或异常回退时解除。
- suppression 生效时，`execMessage()` 直接返回 `QDialog::Rejected`；
  标准按钮版 `showMessage()` 同步回调 `QDialog::Rejected`；按钮列表版不执行任何
  按钮回调。相应对象按既有 ownership/`WA_DeleteOnClose` 规则释放。
- 结果不会排队，恢复 MainWindow 后也不会补弹。已知 remote MOD/QuickWaveform
  请求还会在业务层选择明确的非交互分支，避免把“未展示”误当成用户点击
  Cancel/Close。
- helper 生命周期发生致命错误时，controller 会先恢复 MainWindow、解除
  suppression，再显示主窗口 warning；这不属于 Minibar 运行期业务弹窗。

以上策略没有 Win32/Linux 分支，Win32 与 aarch64 行为一致。第 6.5 节的 Wayland
hosted 层级约束只属于 MainWindow 可见的普通 UI 路径。

### 5.4 finished 回调分发顺序

`finished(int result)` 连接的 lambda 中：

- 如果是按钮触发（`senderButton != nullptr`）：调用 `dialogFinished(result)`
- 否则（例如程序调用 done/reject）：直接走 `m_aRunAsyncFunc(result)`

`dialogFinished(result)` 内：

1. 若 `m_buttonCallbacks.contains(result)`：调用该按钮独立回调，然后 `m_buttonCallbacks.clear()`
2. 若 `m_aRunAsyncFunc` 存在：再调用统一回调（兼容旧模式）
3. 清空 `m_aRunAsyncFunc`

> 注意：当前实现中，独立回调会清空整个映射表，这意味着同一个对话框关闭后不会再触发其他按钮回调（符合一次性弹窗语义）。

---

## 6. UI/布局与尺寸规则（以当前实现为准）

本节只描述“规则与统一流程”，避免陷入实现细节。

### 6.1 统一处理流程（DRY）

当前实现已经把“老接口（标准按钮集合）”与“新接口（按钮列表）”收敛到同一套按钮渲染与布局规则：

- MessageDialog 只负责：设置文本/类型/标题栏，然后把按钮需求交给 Dialog。
- Dialog 负责：创建按钮 Widget、计算按钮高度、按规则布局，并通过 `done(id)` 保证 `finished(result)` 的 result 稳定。
- 关键统一点：
  - 两种按钮入口最终都会调用同一套“按钮高度计算 + 布局规则”逻辑。
  - 按钮高度根据按钮文案自动增高（避免换行截断），对话框高度会随按钮区 `sizeHint()` 动态调整。

### 6.2 MessageDialog 尺寸规则

- 对话框宽度固定为 520；文本在固定宽度内换行，仅通过高度自适应。
- 文本高度限制在合理区间内（避免过高），总体高度由：标题栏 + 内容边距 + 文本高度 + 按钮区高度 组成。

### 6.3 按钮尺寸规则

- 按钮宽度固定为 120。
- 按钮高度会根据文案换行结果统一增高（至少 45，且偶数化），避免长文案被截断。

### 6.4 按钮区布局规则

- 1 个按钮：居中排列（spacing=20）
- 2 个按钮：左右分布（left/right margin=50，中间 `addStretch()`）
- 3 个及以上：居中排列（spacing=20）

### 6.5 Wayland hosted 展示约束

- Raspberry Pi Wayland 下，`MessageDialog` 使用现有 `WaylandDialogMoveTool`
  归一到 MainWindow host，并通过独立 overlay 模拟模态；这样它能稳定压在
  hosted About 和 Open/Save file dialog 之上。
- `MessageDialog` 是固定居中的纯模态窗口，Win32 与 Wayland 均不允许标题栏拖动；
  Open/Save 等其他 helper 调用仍保留默认拖动能力。
- hosted MessageDialog 必须在 `open()/exec()` 前同步完成文本、按钮、高度计算和
  host 本地坐标居中。其 `showEvent()` 不得再走包含 `processEvents()` 的通用
  `Dialog::moveToCenter()` 路径，否则已 raise 的 `(0, 0)` 初始几何可能先绘制一帧。
- hosted 路径不保留 show 后的 deferred geometry mutation；Win32 top-level
  继续使用既有延迟布局与通用居中流程。
- 本节描述的是 MainWindow 可见时的普通 UI 路径，不适用于 C/S Minibar；后者不展示
  `MessageDialog`。

---

## 7. MessageType 行为约定

`applyMessageType(SG::EMsgType type)`：

- `MSG_INFO`：标题 Info
- `MSG_QUESTION`：`Qt::ApplicationModal` + 标题 Question + **显示标题栏关闭按钮**（`TitleBar::CloseButton`）；点击关闭按钮沿用 `QDialog::reject()` 的取消语义
- `MSG_WARNING`：`Qt::ApplicationModal` + 标题 Warning
- `MSG_ERROR/MSG_FAILED`：`Qt::ApplicationModal` + 标题 Error
- 其他：默认 Success

> 设计意图：Question 类型保持与语言/主题重启提示一致，始终显示右上角关闭按钮；点击关闭按钮按取消（reject）语义结束弹窗，不会触发 Yes 确认分支。

---

## 8. Popup 窗口关闭策略（点击事件保障）

每次 `showMessage()` 前都会遍历 `QApplication::topLevelWidgets()`：

- 对所有可见窗口，如果 `windowFlags()` 包含 `Qt::Popup`，则 `close()`

目的：避免 popup（菜单/下拉/tooltip）拦截鼠标事件，导致弹窗按钮点不到。

注意：

- 这个策略只属于异步 `showMessage()` 路径。
- `execMessage()` 不会自动先清理全部 popup；调用方要自己保证当前调用栈下的 popup/focus 生命周期是安全的。
- 在软键盘提交或其他焦点敏感链路里，若提示直接跑在当前点击事件栈中，即便已经改成 `execMessage()`，仍然可能需要像 QuickWaveform 那样先延后到下一轮事件再同步执行，以获得“先经过稳定边界、再弹确认框”的行为。

---

## 9. 典型开发建议

- **新开发优先用按钮列表模式**：逻辑更清晰，每个按钮就近写回调。
- **按钮 ID 选择**：尽量用 `QMessageBox::Yes/No/Cancel/Close/Apply...` 这类标准枚举，便于未来统一行为与兼容旧模式。
- **避免在回调里直接 delete dialog**：`MessageDialog` 已 `WA_DeleteOnClose`，关闭后自动释放。
- **线程注意**：如果你在工作线程想弹窗，直接调用 `showMessage()` 也能工作（内部 runAsync 会切回 UI 线程），但你的回调应避免做耗时任务。
- **不要把 `execMessage()` 当万能修复**：先判断问题是返回值需求、时序空档、还是 popup/焦点生命周期；只有确实需要同步边界时才使用。
- **不要再把 `WA_QuitOnClose=false` 下沉到 `Controls::Dialog` 基类**：这轮实际问题的关键差异在于时序边界，而不是给所有自定义对话框强行塞统一退出语义。

---

## 10. 本轮相关源码位置

- `src/libs/controls/messagedialog.h/.cpp`：`execMessage()` 与 `showMessage()` 的当前差异
- `src/plugins/core/minibarhelpercontroller.cpp`：MainWindow 隐藏期的提示抑制生命周期
- `src/plugins/analog/analogplaybackbusiness.cpp`：共享大波形确认框改为同步 `execMessage()`
- `src/plugins/quickwaveform/quickwaveformpanel.cpp`：本地提示改为“下一轮事件再同步 `execMessage()`”

---

## 11. 已知“注释 vs 实现”差异（后续可清理）

- 历史上“项目完全不使用 `exec()`”的说法已经过时。当前正确表述应为：默认仍是异步 `showMessage()`，但已经存在受控的 `execMessage()` 例外。

---

## 12. 关联源码位置

- `src/libs/controls/dialog.h`：`Dialog`、`DialogButton`、`setButtonsWithCallbacks()`、回调映射 `m_buttonCallbacks`
- `src/libs/controls/dialog.cpp`：按钮创建、`onButtonClicked()`、`dialogFinished()`、按钮区布局策略
- `src/libs/controls/messagedialog.h`：`MessageDialog`、`MessageButton`、`showMessage()` 两种重载
- `src/libs/controls/messagedialog.cpp`：固定宽度/动态高度、消息类型策略、新旧按钮接口桥接
