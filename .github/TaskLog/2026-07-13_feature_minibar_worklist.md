# feature/minibar 当前工作清单

说明：本文只统计 `feature/minibar` 相对 `origin/dev` 的分支独有 minibar 工作，不重复计算已经并入 `dev` 的旧版 in-process minibar 历史；当前分支独有主提交范围为 `2026-07-09 4c9d6c6c` 到 `2026-07-13 71a6a7fe`。

## 总表

| 模块 | 已完成 | 还需要做 | 工作量判断 | 预估时间 |
| --- | --- | --- | --- | --- |
| 双进程 helper 基座 | 已落地独立 `SGStudioMiniBar` target、`src/app/minibarhelper/` 骨架、`MinibarHelperController` 生命周期控制、`RemoteMinibarService` 权威 snapshot 发布；helper 已能根据 main 状态显示并支持基础拖动与显隐 | 继续补 helper 冷启动 session、startup visibility policy、异常恢复策略 | 大。这里不是简单 UI 改造，而是新增一套 companion executable、main/helper 生命周期、显隐切换和状态同步链路 | 若补齐启动策略，约 `1` 天 |
| IPC / 状态同步 | 已把协议抽到 `src/libs/minibaripc/`，`MinibarClient` 已支持 request id、超时、response snapshot 关联、多 in-flight request | 继续收口协议稳定性和边界文档，确认异常/超时/重入路径 | 大。涉及 main/helper 双端、协议层、时序、状态一致性，不是单点功能 | 与联调并行，约 `0.5` 天 |
| 基础发射参数 | Center / Level 数值编辑已接回 main，helper 已能做基础发射参数修改 | 继续做 Win32 / Wayland 实机联调，确认数值键盘、焦点、关闭路径完全稳定 | 中到大。功能表面不多，但跨平台输入、弹窗和回写链路复杂 | 并入平台联调 |
| Sweep 远程面板 | `RemoteSweepPanel` 第一版已落地，helper 能渲染 Sweep 状态并提交完整 authoring request；popup、outside click、Wayland 定位已做过第一轮修复；差分刷新已减少整窗闪烁 | 继续做 Win32 / Wayland 真实环境验证，重点确认 popup、overlay、outside-dismiss、拖动和键盘交互 | 大。Sweep 不是静态展示，而是完整远程 authoring 闭环，还叠加窗口协议差异 | Win32 + Wayland 联调合计约 `1` 到 `1.5` 天 |
| MOD 菜单 | MOD 菜单已从 main 当前可见 business 列表生成，不再由 helper 硬编码；菜单外点关闭已补齐 | 继续验证菜单在 Win32 和 Wayland 下的关闭、定位、owned-area、恢复路径 | 中到大。看起来只是菜单，实际涉及 business 可见性、license 变化、native menu 行为和 layer-shell 适配 | 并入平台联调 |
| MOD 参数面板 | `RemoteModPanel` 第一版已接入，已覆盖 `AM`、`FM`、`PM`、`Pulse`、`Digital Ramp`、`AWGN`、`Digital Mod`、`Multitone`、`Playback`、`Streaming`；Playback / Streaming 已支持 `Load/Clear` 类型 authoring 输入 | 当前正在收尾数值编辑 checked 状态和按钮样式恢复；之后还要补一轮完整运行验证 | 很大。这一块本质上是把多类业务参数面板远程镜像到 helper，并保证 profile 写回、文件输入、键盘交互都可用 | 收尾约 `0.5` 天，联调约 `0.5` 到 `1` 天 |
| Win32 适配 | 已具备基本可用路径，但当前仍缺针对 MOD 菜单、MOD 面板、数值键盘、Playback/Streaming 文件选择、Restore、outside-dismiss 的系统验证 | 需要继续适配和联调，确保 native menu、dialog、keyboard、focus、dismiss 行为稳定 | 明确仍有工作量。Win32 不是“已自动兼容”，还需要针对 helper 模式单独适配和验证 | 约 `0.5` 到 `1` 天 |
| Wayland 适配 | 已完成第一轮 layer-shell helper、拖动、popup 定位、overlay、outside-click 基础适配 | 仍需继续适配和联调，重点是 layer-shell 菜单定位、panel 内点击、keyboard overlay、outside-dismiss、拖动、跨窗口 owned-area 行为 | 很大。Wayland 不是 Win32 的同构环境，layer-shell、popup、overlay、输入抓取都需要单独处理 | 约 `1` 天 |
| 文档与技术债 | 已有 TaskLog 和 helper 架构文档基础；当前未提交改动集中在 `RemoteModPanel::setNumericEditorActive(...)` 和 `openModNumericKeyboard(...)` 收尾 | 需要同步 KnowledgeBase，因为现文档仍把 MOD 视为只读菜单；还要清理 `m_rfText` 本地推导、pending UI 脚手架等小技术债 | 中。不是主功能，但关系到后续维护成本和认知一致性 | 约 `0.5` 天 |
| 总体判断 | helper 版 minibar 主骨架已经完成，基础通信、基础发射参数、Sweep 和 MOD 第一版都已具备 | 剩余重点是 MOD 面板交互收尾、Win32 适配、Wayland 适配、真实环境联调、冷启动策略和文档同步 | 工作量仍然很大。当前不是“做个小窗”，而是跨 `main/helper/IPC/UI/Win32/Wayland` 多层同时推进，整体更像一个独立子系统收口阶段 | 当前完成度约 `80%` 到 `85%`；若只收当前交互和双平台联调，预计还要 `1` 个工作日 |

## 2026-07-13 MOD panel overlay 双平台修复（现场回归后撤回）

### 现场观察

1. Raspberry Pi Wayland 下，打开 MOD panel 后，点击 panel 内部会直接关闭 panel。
2. Win32 下，从 MOD panel 的数值 `LabelButton` 打开软键盘并通过外点关闭键盘后，minibar 会失去焦点；此后继续外点无法关闭 MOD panel。
3. 第一轮修改把 MOD fullscreen overlay 扩展到 Win32，并让 MOD dialog 在两个平台都从创建开始成为 overlay child。
4. 新的现场反证：该修改恢复了此前已解决的交互状态 bug；在部分操作序列中，MOD panel 内部 `LabelButton` press 被当成外部交互，panel 在按钮正常处理前关闭。
5. 历史 TaskLog `2026-07-13_remote_minibar_mod_menu.md` 和当前 in-process `MiniBarWindow` 都保留同一平台分界：Win32 business panel 是 top-level `Qt::Tool` dialog；只有 Wayland layer-shell 把 dialog reparent 到 fullscreen overlay。

### 推断与设计

“MOD 应在 Win32 / Wayland 无条件对齐 Sweep overlay”的推断已被现场结果否定。MOD panel 与 Sweep panel 的交互历史不同，不能只按宿主结构表面相似性合并。恢复 in-process 已验证的平台分界：

1. Win32 保持 MOD dialog 为 top-level `Qt::Tool`，沿用应用级 pointer/activation ownership 判断；不创建 fullscreen MOD panel overlay。
2. Wayland layer-shell 继续创建 fullscreen MOD overlay，并在显示前把 dialog reparent 成 overlay child；outside-click 仍由该 owned surface 处理。
3. `objectBelongsToWidgetOrWindow(...)` 与 host-local hit test 继续保护 Wayland native `QWidgetWindow` 先于 child widget 收到 press 的情况。
4. 不修改 `RemoteModPanel` 的 `LabelButton` checked/editor-active 链，也不把 Win32 失焦问题继续扩大为跨平台 fullscreen overlay。

### 成功条件与验证级别

验证级别：`static`。本次不主动编译或运行；实机行为留给 Win32 / Raspberry Pi Wayland 联调。

1. Win32 MOD dialog 的 parent/window flags 与 in-process business popup 一致，内部 `LabelButton` 点击不经过 fullscreen child-overlay 状态路径。
2. Wayland panel 内部 mouse/touch 事件由 overlay child dialog 接收，不触发 outside handler；panel 外部输入仍关闭 panel。
3. `ApplicationDeactivate` 不作为 Wayland visible MOD panel 的无条件关闭依据。
4. 关闭、collapse、hide、Restore、device disconnect 和 shutdown 仍通过现有 `closeModPanel()` 收口，不遗留 Wayland fullscreen 输入层。

### 修正实施计划

1. 恢复 `ensureModPanelHost()` 的 layer-shell-only overlay 创建和 top-level MOD dialog 构造。
2. 恢复 `showModPanelForBusiness()` 的平台分支：Wayland reparent/host-local 定位，Win32 top-level/global 定位。
3. 在代码中保留平台分界注释，防止后续再次因 Sweep 表面相似而无条件合并。
4. 静态核对内部 press 的 ownership short-circuit、Wayland native host-local hit test、键盘 guard 和所有 close 路径；执行 `git diff --check`，不主动编译运行。

### 修正实施与静态验证

1. 已撤回 Win32 fullscreen MOD overlay：`ensureModPanelHost()` 再次只在 active layer-shell Wayland 创建 overlay，MOD dialog 恢复为 top-level tool 构造。
2. `showModPanelForBusiness()` 已恢复 in-process 的平台分流：Wayland 显示前 reparent 到 overlay 并使用 screen-local 坐标；Win32 保持 top-level dialog 和全局屏幕坐标。
3. Wayland 保护未被撤回：`isModPanelInteractionObject()` 仍先按 dialog QObject/native window ownership 判断，并保留 overlay-host `QWidgetWindow` 的 host-local geometry 命中。
4. Win32 内部 `LabelButton` 重新走 top-level dialog 的正常 press/release 与 activation 路径，不再经过 fullscreen `WA_ShowWithoutActivating` host。
5. `git diff --check` 通过；按约定未编译、未运行。现场应优先回归“打开 MOD panel 后连续点击多个 numeric/enum LabelButton”以及“键盘关闭、panel 关闭、重开后再次点击”的序列。

## 2026-07-13 Wayland MOD 菜单切换后的 overlay 事件归属修复

### 现场观察与代码证据

1. Win32 在恢复 top-level `Qt::Tool` MOD dialog 后已由现场确认正常；本轮不修改 Win32 宿主路径。
2. Raspberry Pi Wayland 仍会在从 `QMenu` item 打开 MOD panel 后，把 panel 内部点击当成外点并关闭 panel。
3. in-process `MiniBarWindow::eventFilter()` 在任何 deactivate / mouse outside-dismiss 判断之前，先对完整 layer-shell popup overlay 对象树 `return false`；内部控件事件与 overlay 空白区事件都交给 `Controls::OverlayContainer`。
4. remote `RemoteMiniBarWindow::eventFilter()` 当前把 Sweep/MOD overlay bypass 放在应用级 session outside-dismiss 判断之后。Wayland native overlay host 或其 `QWidgetWindow` 先收到 press 时，事件会先经过 `isModPanelInteractionObject(...)` 的二次分类，尚未到达后面的 bypass 就可能关闭 panel。
5. MOD 从 `QMenu` 打开确实增加了 menu layer surface 到 panel layer surface 的切换，与直接打开 Sweep 的时序不同；但现有证据指向的是该切换暴露了重复分类顺序，而不是需要增加异步延时或修改 `LabelButton` 状态。

### 修复设计与成功条件

验证级别：`static`。按仓库约定不主动编译或运行，Raspberry Pi Wayland 行为由现场联调确认。

1. 保留 MOD menu overlay 的专用外点分支；它仍负责关闭可见 `QMenu`。
2. 紧接其后、在 `ApplicationDeactivate` 和 session mouse outside-dismiss 之前，对 Sweep/MOD panel overlay 完整对象树直接放行。
3. panel overlay 的内部/外部判断只由 `OverlayContainer` 负责：child panel 内点击正常送达控件，overlay 空白区点击由 container 关闭 panel。
4. eventFilter 尾部的 overlay mouse bypass 仅保留给非 layer-shell（当前主要是 Win32 Sweep）拖动分流；layer-shell panel overlay 不再依赖这个过晚的 fallback。
5. Wayland 从 MOD 菜单选择 business 后，连续点击 numeric/enum `LabelButton` 或 panel 空白区域都不关闭 panel；点击 panel 外 overlay 仍关闭。
6. Win32 不创建 MOD panel overlay，因此本次提前放行不改变其 top-level dialog、软键盘与 outside-dismiss 路径。

### 实施与静态验证

1. 已在 MOD menu overlay 专用分支之后、`ApplicationDeactivate` 与 session mouse classifier 之前，增加 active layer-shell Sweep/MOD overlay 对象树的 early bypass。
2. early bypass 覆盖 overlay host、host `windowHandle()`、`OverlayContainer`、child dialog 和内部控件，顺序与 in-process `MiniBarWindow::eventFilter()` 一致。
3. MOD menu 的 fullscreen overlay 外点仍由原专用分支消费；MOD panel 外点仍由 `m_modOverlay->setOutsideInputHandler(...)` 调用 `closeModPanel()`，未增加 timer 或第二套 geometry workaround。
4. 非 layer-shell 路径保持原状：Win32 MOD 没有 `m_modOverlayHost`；Win32 Sweep 仍保留 eventFilter 尾部既有 overlay/drag 分流。
5. 已确认 `remoteminibarwindow.cpp` 仍由 helper `CMakeLists.txt` 编译；`git diff --check` 与 `git diff --cached --check` 通过（仅有工作区既有的 LF/CRLF 提示）。按约定未编译、未运行。

## 2026-07-13 Remote MOD 枚举字段 EnumTextButton 对齐

### 观察与范围

1. main 的 AM/FM/PM `Shape`、Digital Mod `Filter Type / Modulation Type / Oversample`、Multitone `Tone Phase` 都使用 `EnumTextButton`；remote 当前却把所有 `FieldKind::Enum` 创建成普通 `LabelButton`，点击只能循环到下一个值。
2. main 的 Shape、Filter Type、Tone Phase 使用默认 ListMode；Digital Modulation Type 与 Oversample 显式使用带标题的 IconMode。
3. `RemoteSweepPanel` 的 Sweep Type 已证明 helper 可以复用 `EnumTextButton`，其 `PopupWidget` 在 Win32 走控件自身定位，在 Wayland 则由 `RemoteMiniBarWindow` 根据 layer-shell owner visual geometry 重新配置。
4. remote MOD profile 中枚举值既有字符串也有数字。`EnumTextButton` 的公开 value 是 `int`，因此不能直接把控件的局部 int 当作 IPC/profile 值。
5. MOD 与 Sweep panel 使用独立 overlay/session；新增 MOD enum popup 后，Win32 的 deactivate/outside-dismiss 和 Wayland overlay outside-input 都必须先关闭最内层 popup，不能直接关闭 MOD panel。

### 设计

验证级别：`static`。按仓库约定不主动编译或运行；Win32 与 Raspberry Pi Wayland 行为留给现场联调。

1. `RemoteModPanel` 对 `FieldKind::Enum` 创建真实 `EnumTextButton`，保持标题左对齐、当前值右对齐、60px 高度和现有 MOD field objectName/QSS 入口。
2. 每个 enum option 使用稳定的局部 index 作为 `EnumTextButton::EnumSpec::value`；`currentItemEdited(index)` 再映射回原 `QJsonValue` 写入 profile，确保字符串/数字 payload 与当前协议完全一致。
3. snapshot/profile 刷新时按 `QJsonValue` 找 option index 并调用 `setCurrentEnum(index)`；未知值仅显示原始文本，不伪造 profile 写回。
4. FieldSpec 增加 popup mode 描述：Shape、Filter Type、Tone Phase 保持 ListMode；Digital Modulation Type 和 Oversample 与 main 一样设置 IconMode 和 popup title。
5. 将现有 Sweep `PopupWidget` 注册/guard 模式平行扩展到 MOD enum popup：popup 自身及 native `windowHandle()` 计入 MOD owned interaction；关闭 panel 前先关闭 popup；popup 刚关闭的同一事件循环用独立 guard 防止 panel 连带关闭。
6. Wayland `configureLayerShellOwnedPopupWidget()` 同时接受 Sweep 与 MOD dialog owner。ListMode 以按钮左下角对齐、超底部翻到上方并按可用屏幕裁剪；IconMode 按 in-process/main 既有规则居中于 layer-shell owner visual rect。
7. Win32 不进入 layer-shell 重定位，继续复用 `EnumTextButton::updatePopupPosition()` 的主程序行为；MOD top-level tool dialog 与 popup ownership 路径保持不变。

### 成功条件

1. 所有已声明 `FieldKind::Enum` 的 MOD 字段都弹出列表/图标选择器，不再循环值。
2. 当前项高亮、选择后 profile request、随后 authoritative snapshot 回写均保持一致，且不把局部 index 发给 main。
3. Win32 ListMode popup 锚定按钮，IconMode popup 居中；点击 popup item 不关闭 MOD panel，popup 外点只关闭 popup。
4. Wayland ListMode/IconMode popup 获得正确 layer/output/anchors/margins/size，选择项时 MOD panel 保持打开；panel overlay 外点在 popup 已关闭后仍能关闭 panel。

### 实施与静态验证

1. `RemoteModPanel` 已为全部 `FieldKind::Enum` 创建 `EnumTextButton`；旧的 `cycleEnumValue()` 路径已删除，数值和文件字段仍保持原 `LabelButton` 行为。
2. enum button 使用 option index 作为控件局部 value，`currentItemEdited` 按 `FieldSpec::enumOptions` 映射回原 `QJsonValue`；profile refresh 则反向查 index 后调用 `setCurrentEnum()`。
3. AM/FM/PM Shape、Digital Filter Type、Multitone Tone Phase 保持 ListMode；Digital Modulation Type 和 Oversample 已按 main 设置 IconMode 与 popup title。按钮和列表文字 alignment 继续复用 `EnumTextButton/PopupWidget` 的 main 规则。
4. `RemoteMiniBarWindow` 已识别来自 Sweep 或 MOD panel 的 `PopupWidget::Show`；Wayland 共用 owner visual geometry 配置，而 Win32 不进入 layer-shell 分支。
5. MOD enum popup 已加入独立 pointer/dismiss guard、native-window owned-interaction、panel close 顺序和 overlay outside handler：外点先关 popup，不会在同一点击中连带关闭 MOD panel。
6. 已核对 main property display options，当前 remote option 顺序与文本一致；局部 index 不进入 IPC。声明/定义与旧 `cycleEnumValue` 残留检查通过。
7. `git diff --check` 与 `git diff --cached --check` 通过（仅有工作区既有 LF/CRLF 提示）；按约定未编译、未运行。

## 2026-07-13 Remote MOD EnumTextButton 首次点击被吞修复

### 现场观察与事件证据

1. Sweep Type 第一次点击即可显示 popup；从 MOD `QMenu` 打开的 panel 中，任意 `EnumTextButton` 第一次点击看似无效，第二次才显示。
2. `EnumTextButton::onClicked()` 在第一次 clicked 时已经同步执行 `PopupWidget::popup()` 和 `show()`，不存在“第一次只选中、第二次才 show”的控件逻辑。
3. popup 的 `Show` 会先被 `RemoteMiniBarWindow::eventFilter()` 注册为 `m_modEnumPopup`；随后 Win32 top-level MOD dialog / `Qt::Popup` 的首次激活切换可能产生 `ApplicationDeactivate`。
4. remote 当前在 MOD session 的 `ApplicationDeactivate` 分支中，只要 popup 可见就主动 `closeModEnumPopup()`。因此 popup 实际已显示又立即关闭，用户看到的是第一次点击失效。
5. Sweep panel 使用既有 overlay child 宿主，通常没有 MOD 从 `QMenu` 到 top-level tool 再到 `Qt::Popup` 的首次激活边沿。in-process 对可见 owned auxiliary transient 则直接放行 deactivate，不把它解释为 outside-dismiss。

### 修复设计与成功条件

验证级别：`static`。本次不增加 timer，不强制 `activateWindow()`，也不修改共享 `EnumTextButton`。

1. MOD session 收到 `ApplicationDeactivate` 时，若 MOD enum popup 仍可见，跨平台直接放行事件，不关闭 popup 或 panel；该分支显式覆盖 Win32 的首次激活边沿，也保持 Wayland owned-popup 语义。
2. 真正的 popup 外点仍由 `Qt::Popup` 自身关闭，并由现有 Hide/Close dismiss guard 防止同一点击连带关闭 panel。
3. 没有 enum popup、没有软键盘且没有 guard 时，Win32 原有 deactivate 关闭 MOD panel 的路径保持不变。
4. Wayland 不进入 `Q_OS_WIN` deactivate 分支，继续由 layer-shell popup 和 `OverlayContainer` 负责 inside/outside routing。
5. 从 MOD 菜单首次打开 AM/FM/PM Shape、Digital enum 或 Multitone Tone Phase 后，第一次点击即可看到 popup；选择项和外点关闭顺序不变。

### 实施与静态验证

1. MOD session 的 `ApplicationDeactivate` 分支已改为：MOD enum popup 可见时在平台宏之前直接 `return false`，按 owned transient 放行，不再调用 `closeModEnumPopup()`。
2. 无 popup 时的 `m_modEnumPopupDismissGuard`、direct keyboard guard 和 `closeModPanel()` 路径未改变；应用内 mouse outside 与 Wayland overlay outside handler 仍负责真正的 popup/panel 分层关闭。
3. 未修改 Sweep、共享 `EnumTextButton`、MOD panel show/activate 或 Wayland layer-shell 配置，也未增加延迟补丁。
4. 已静态核对 popup Show/register、ApplicationDeactivate、MouseButtonPress、OverlayContainer outside handler 和 panel teardown 顺序；`git diff --check` 与 cached check 通过（仅有既有 LF/CRLF 提示）。按约定未编译、未运行。

## 2026-07-13 Remote MOD Wayland 首次枚举点击二次修正

### 现场反证与重新观察

1. Raspberry Pi Wayland 实测确认：上一轮 `ApplicationDeactivate` 修改后，MOD 枚举第一次点击仍然无效；第二次以及随后所有点击均正常。
2. 该结果否定了“Wayland 首次 popup 被 `ApplicationDeactivate` 关闭”的主因判断：旧关闭代码原本位于 `Q_OS_WIN` 分支，本就不会在树莓派 Wayland 执行。
3. in-process minibar 也存在相同现象，不能作为正确参照；同一 helper、同一 layer-shell 路径下首次点击正常的 `RemoteSweepPanel` 才是有效对照组。
4. Remote Sweep 的 dialog 从创建开始就是 `OverlayContainer` 下的 `Qt::Widget` child，`EnumTextButton` 及其 `Qt::Popup` 从未经历宿主转换。
5. Remote MOD 当前先把 dialog 创建为 minibar 下的 top-level `Qt::Tool`，随后创建动态枚举按钮；仅在 Wayland 第一次显示时，才把整棵 dialog 从 top-level reparent 到 MOD overlay child。这个一次性窗口角色/父窗口转换与“仅第一次失败，之后永久正常”的现场特征完全一致。
6. `EnumTextButton::onClicked()` 第一次 clicked 就同步调用 popup 的 `show()`；MOD 与 Sweep 的枚举按钮逻辑没有需要第二次点击的分支。差异位于首次显示前的宿主生命周期，而不是枚举值绑定或按钮 checked 状态。

### 修正设计与成功条件

验证级别：`static`。不修改共享 `EnumTextButton/PopupWidget`，不增加定时器、合成点击或强制激活。

1. 保持已验证的平台分流：Win32 MOD dialog 仍从创建开始就是 top-level `Qt::Tool`；Wayland layer-shell MOD dialog 则从创建开始就与 Sweep 一样，是对应 overlay 下的 `Qt::Widget` child。
2. 在 Wayland 构造阶段直接确定最终 parent/window flags，使随后动态创建的每个 `EnumTextButton::PopupWidget` 都基于最终 overlay owner 建立 transient 关系。
3. `showModPanelForBusiness()` 不再承担正常路径上的首次 top-level -> child 转换；保留窄范围防御检查仅用于修复异常旧状态，不改变正常生命周期。
4. MOD 仍由专用 overlay、outside handler、popup guard 和 screen-local 定位负责；Win32 不创建 MOD overlay，现有 top-level 焦点/外点路径保持不变。
5. 树莓派 Wayland 冷启动后第一次从 MOD 菜单打开包含枚举的业务，第一次点击 Shape/Filter Type/Modulation Type/Oversample/Tone Phase 即显示 popup；第二次及后续行为不变。
6. Remote Sweep 代码不修改；两者在 Wayland 的关键宿主不变量对齐，而不是合并 Win32 行为。

### 实施与静态验证

1. `ensureModPanelHost()` 现在按平台直接构造最终形态：active layer-shell Wayland 使用 `m_modOverlay` parent 和 `Qt::Widget` flags；非 layer-shell/Win32 继续使用 minibar parent 和 `Qt::Tool` flags。
2. `RemoteModPanel` 及其动态 `EnumTextButton` 在 dialog 最终 parent/window flags 确定后才创建；`showModPanelForBusiness()` 正常 Wayland 路径的 parent/flags 检查不再触发首次 reparent。
3. 保留 show 路径的防御性 `setParent` 检查、MOD 专用 overlay/outside handler、popup pointer/guard 和 layer-shell popup 定位；Remote Sweep 与 Win32 路径未修改。
4. 已移除上一轮代码中把 QMenu/tool activation edge 写成首次失败根因的注释；可见 popup 的 deactivate 放行仅保留为 owned-transient 约定。
5. 已确认 `ensureModPanelHost()` 在 `applyBusiness()` 之前执行、helper CMake 继续编译该源文件，且 source diff 只涉及 MOD dialog 构造和错误注释修正。`git diff --check` 与 cached check 通过（仅有工作区既有 LF/CRLF 提示）；按约定未编译、未运行。
