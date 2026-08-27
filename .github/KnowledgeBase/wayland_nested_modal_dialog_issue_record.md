# Wayland 下嵌套模态对话框问题记录

## 结论（TL;DR）

当前仓库里的 `Controls::Dialog / InputDialog / PxSaveFileDlg` 这条链路，在 Wayland 下**不能认为“模态对话框里再弹一个模态对话框”是稳定可用的能力**。

这次问题已经确认不是某个按钮回调写错，也不是一处显式 `hide()` 直接把 `InputDialog` 收掉。更可信的结论是：

- 外层自定义无边框模态对话框已经是一个 top-level dialog；
- 内层 `InputDialog` 再以 top-level 模态 dialog 方式弹出；
- 在 Wayland 下，外部点击会触发 compositor / Qt 对这组嵌套模态窗口的激活、堆叠、transient、输入屏障处理；
- 一旦这条链路没有被稳定管理，就会出现“内层弹窗视觉消失，但输入没有正常回到应用”的状态。

截至当前，这个问题**没有得到可靠修复**。用户已经删除了 `SaveFileDlg` 中的“新建文件夹”按钮，避免继续暴露这条路径。
文档中所有的尝试修复均已回退，不添加工程的复杂性。

这份文档的目的，是把问题记录清楚，避免后续继续按 `InputDialog` 单类补丁、按钮逻辑或局部 `setWindowFlags()` 修补的方向反复试错。
## 1. 问题现象

历史复现链路如下：

1. 打开 `PxSaveFileDlg`。
2. 在其内部的 `DirWidget` 中点击“新建文件夹”。
3. 弹出 `InputDialog` 输入文件夹名。
4. 点击 `InputDialog` 外部区域。

实际现象：

- `InputDialog` 会从视觉上消失，或看起来被压到后面；
- 但应用输入状态没有恢复正常；
- 最终表现为 SGStudio 整体鼠标点击失效，像是仍有一个看不见的模态输入屏障存在。

这个入口当前已经被移除，但问题本身仍应视为**Wayland 下嵌套模态管理缺口**。

## 2. 相关代码链路

调查时涉及的主要代码位置：

- `src/libs/controls/SaveFileDlg.cpp`
  - `PxSaveFileDlg::show()` 通过 `Dialog::runAsync()` 以模态方式打开文件保存对话框。
- `src/libs/controls/DirWidget.cpp`
  - `DirWidget::createNewFolder()` 中创建 `InputDialog`。
- `src/libs/controls/dialog.cpp`
  - `Dialog::runAsync()` 走基类的异步模态路径；
  - `InputDialog::runAsync()` 又有自己的显示和模态处理逻辑。

结构关系是：

- `PxSaveFileDlg` 本身已经是一个自定义无边框模态对话框；
- `DirWidget` / `FileWidget` 只是它内部的普通子控件；
- `InputDialog` 则是在外层模态 dialog 之上，再次打开的内层模态 dialog。

也就是说，这次不是“普通页面里弹一个对话框”，而是**模态 dialog 下再弹模态 dialog**。

## 3. 已确认不是根因的方向

### 3.1 不是按钮回调直接关闭了 InputDialog

本轮调查没有找到“点击外部就显式调用 `hide()` / `close()` / `reject()` 关闭 `InputDialog`”的直接业务代码路径。

换句话说：

- 不是 `DirWidget::createNewFolder()` 里的 lambda 主动收掉了窗口；
- 也不是某个按钮点击信号错误路由到了 `reject()`。

### 3.2 不是单纯的 parent 指针错误就能解释全部现象

调查中尝试过把 `InputDialog` 的 owner/parent 收敛到：

- `parent->window()`；
- 最近的 `Dialog / QDialog` 祖先；
- 显式补 `QWindow::setTransientParent(...)`。

这些补丁**理论上更合理**，但并没有带来可靠收敛，因此不能再把问题简化成“只要 parent 找对就好了”。

### 3.3 不是简单的 Keyboard QWidget Popup 关闭问题

在 Linux / Wayland 路径下，`Controls::Keyboard` 走的是 Qt input method + DBus 请求系统键盘的路径，不是一个普通的 QWidget popup 叠在 `InputDialog` 上面。

因此，这次问题虽然发生在文本输入场景中，但更像是**嵌套模态窗口管理**问题，而不是“某个应用内 popup 抢焦点”的简单版本。

## 4. 当前更可信的结论

当前更可信的技术结论是：

### 4.1 真正的问题边界在 Wayland 的嵌套模态窗口语义

对于当前仓库这套自定义无边框 dialog：

- 外层 `PxSaveFileDlg` 不是系统标题栏 dialog，而是自定义 frameless top-level dialog；
- 内层 `InputDialog` 也是自定义 frameless top-level dialog；
- 两者之间的 owner、transient、stacking、activation、modality 语义，并不是单靠 QWidget 父子关系就能完全定义清楚；
- 最终谁在最上层、谁接收输入、外部点击是否让某个窗口失活或隐藏，要受 Qt 平台插件和 Wayland compositor 共同影响。

一旦这条链路进入“不一致”状态，就会出现：

- 内层窗口视觉上不见了；
- 但输入屏障没有正确释放；
- 或激活态没有正确返回给期望窗口；
- 从而让应用整体进入“鼠标点不动”的表象。

### 4.2 这不是适合继续做 one-off patch 的问题

过去尝试过的方向包括：

- 改 `InputDialog` 的 parent；
- 改 `InputDialog` 的 modality；
- 改 `InputDialog` 的 `windowFlags`；
- 改 transient parent；
- 单独给 `InputDialog` 自定义 `runAsync()`。

这些都属于**局部修补**。

当前应当承认：如果根因是“Wayland 下自定义 top-level dialog 的嵌套模态管理本身不稳定”，那么继续在 `InputDialog` 这个单类上打补丁，收益会越来越低，而且容易让后续人误以为问题已被修复。

## 5. 当前工程决策

当前工程决策应当视为：

- 这条“在 `PxSaveFileDlg` 内再弹 `InputDialog` 新建文件夹”的路径已经被下线；
- 下线不是因为逻辑功能不需要，而是因为其窗口管理在 Wayland 下不可靠；
- 后续若要恢复，必须先从**窗口承载架构**层面重新设计，而不是恢复按钮后继续修补 `InputDialog`。
- 文档中所有的修复均已回退，不添加工程的复杂性。

## 6. 后续可接受的方向

如果未来必须恢复“保存对话框里新建文件夹”能力，优先考虑下面几种方向：

### 6.1 单模态宿主 + 宿主内部 overlay 子控件

最稳妥的方向，是避免 top-level modal 上再弹 top-level modal，而改为：

- 保留一个外层模态宿主；
- 新建文件夹输入框作为宿主内部的 child overlay / embedded prompt；
- 外部输入屏蔽也在宿主内部完成。

这类方案更接近 [WAYLAND_FRAMELESS_OVERLAY_PATTERN.md](WAYLAND_FRAMELESS_OVERLAY_PATTERN.md) 里总结的“host 内 overlay 模拟模态”模式。

### 6.2 串行 dialog，而不是嵌套 dialog

另一种可接受方向是：

- 先结束 / 暂停外层 dialog；
- 再单独打开内层输入 dialog；
- 完成后再恢复外层状态。

这会牺牲一部分交互连续性，但比“嵌套 top-level modal”更可控。

### 6.3 从 Dialog 基类统一重构，而不是单补 InputDialog

如果团队仍然想支持“dialog 内再弹 dialog”，那应该从 `Controls::Dialog` 的承载模型统一重构：

- 明确哪些 dialog 必须是 top-level；
- 哪些 dialog 应该 hosted 到现有宿主内部；
- 是否需要统一的 overlay / local event loop / owned-child 模型；
- 如何在 Wayland 下定义 activation / outside-click / modality 的真实边界。

在这之前，不建议继续按 `InputDialog` 单类做局部修复。

## 7. 与现有文档的关系

这份文档与下面两篇相关：

- [WAYLAND_FRAMELESS_OVERLAY_PATTERN.md](WAYLAND_FRAMELESS_OVERLAY_PATTERN.md)
  - 讲的是“什么场景适合 host 内 overlay child widget 模式”；
  - 这次问题进一步说明：嵌套 top-level modal 是另一类边界，不能按普通 QWidget 直觉处理。
- [wayland_raspberry_pi_system_keyboard_integration.md](wayland_raspberry_pi_system_keyboard_integration.md)
  - 讲的是普通文本输入与系统键盘接入；
  - 这次问题说明：即便系统键盘链路单独成立，也不等于嵌套模态 dialog 本身稳定。

## 8. 当前结论

请把这次问题视为：

- 一个**已记录、未解决、已绕开入口**的 Wayland 架构问题；
- 不是一个“再补几个 `setWindowFlags()` / `setTransientParent()` 就能收尾”的局部 bug；
- 后续若要继续处理，入口应当是“Wayland 下模态 dialog 内再弹模态 dialog 如何统一管理”，而不是继续从 `InputDialog` 单点出发。