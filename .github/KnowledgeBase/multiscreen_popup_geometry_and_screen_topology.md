# 多屏 Popup 几何与屏幕拓扑变更处理

本文记录 `EnumTextButton`、`Controls::ComboBox` 与 `TitleBar` popup 的几何修复
模式，作为后续排查 Windows / Qt 5 / QWidget popup 在外接屏断开、DPI 切换、
窗口恢复后的首选入口。

相关 TaskLog：

- `.github/TaskLog/2026-06-11_mainwindow_external_screen_restore_and_enum_popup_fix.md`
- `.github/TaskLog/2026-06-12_titlebar_qmenu_external_screen_popup_fix_analysis.md`

## 适用范围

本文覆盖四类相邻但 owner 不同的问题：

- `MainWindowChromeWin`：Windows frameless 主窗口的非客户区、最大化边界、DPI / screen 变化后的 frame refresh。
- `EnumTextButton` / `PopupWidget`：自定义 `Qt::Popup` 的屏幕选择、边界裁剪、列表内容宽度。
- `Controls::ComboBox`：Qt 私有 popup container 的首次显示时机、锚点与屏幕裁剪。
- `TitleBar` / `QMenuBar` / `QMenu`：自定义标题栏中的 Qt 菜单 popup 在屏幕拓扑变化后的 geometry / size hint 刷新。

不要把这三类问题混成一个大修复。它们现象相似，都是“断屏后 popup/窗口几何不对”，但正确 owner 不同。

## 典型现象

- 外接屏断开后，主屏上的 `EnumTextButton` 下拉列表宽度过窄，长 item 文本显示不全。
- 外接屏断开后，标题栏菜单 `QMenu` popup 宽度异常，菜单项被裁切。
- 外接屏最大化后最小化，再从任务栏恢复时，主窗口没有按当前屏正确恢复最大化边界。
- 某些 frameless 场景中，首次显示或恢复后出现右/下白边，轻微移动窗口后消失。

这些现象共同指向一个规律：Qt/Win32 在屏幕拓扑、DPI、window handle、popup show 时机之间可能存在一帧延迟或缓存未失效。

## Owner 边界

### MainWindowChromeWin

`src/plugins/core/mainwindowchrome_win.cpp` 负责 Windows 顶层窗口 chrome：

- `WM_NCCALCSIZE`
- `WM_NCHITTEST`
- `WM_GETMINMAXINFO`
- `WM_DPICHANGED`
- `QWindow::screenChanged`
- state / first-show 后的 deferred frame refresh

它不负责菜单 action、popup 内容宽度，也不应直接管理 `QMenu` 或 `PopupWidget`。

### EnumTextButton / PopupWidget

`src/libs/controls/enumtextbutton.cpp` 负责 popup 放在哪里：

- ListMode 以按钮左下角全局坐标作为 reference point。
- 优先使用 `QGuiApplication::screenAt(referencePoint)` 获取当前屏。
- fallback 到 popup window handle、popup screen、anchor top-level window screen、anchor screen、primary screen。
- 按当前屏 `availableGeometry()` 做右侧和左侧裁剪，底部超出时翻到按钮上方。

`src/libs/controls/popupwidget.cpp` 负责 popup 要多宽：

- ListMode 的最终宽度是 `max(调用方传入的按钮宽度, item/title 内容宽度)`。
- 内容宽度优先用 `sizeHintForColumn(0)`，必要时回退到 `QFontMetrics::horizontalAdvance()`。
- 不再把 ListMode popup 宽度硬绑定为按钮宽度。

### TitleBar / QMenuBar / QMenu

`src/plugins/core/titlebar.cpp` 负责自定义标题栏里的菜单树刷新：

- `TitleBar` 持有被接入的 `QMenuBar`。
- 监听 `QGuiApplication::screenAdded`、`screenRemoved`、`primaryScreenChanged`。
- 屏幕拓扑变化时，如果当前 active popup 属于 titlebar 菜单树，先关闭它。
- 延迟一个 event loop 后，对 `QMenuBar` 和递归 `QMenu` 执行 `ensurePolished()`、`updateGeometry()`、`adjustSize()`、`update()`。
- 刷新 menubar layout、titlebar layout，并更新 overflow 按钮图标/几何。

这里不重建 `QMenu` / `QAction`，也不改变菜单业务结构。

### Controls::ComboBox

`src/libs/controls/combobox.cpp` 负责共享 ComboBox popup 的位置：

- `GpsDialog` 的 Antenna、Time Format 等页面控件不单独写定位逻辑。
- `showPopup()` 在调用 Qt 基类前对 item view 的 top-level popup container 安装
  event filter。
- Qt 完成列表高度计算后，在 popup 的 `QEvent::Show` 中、首次 native mapping 前
  同步设置最终 geometry。
- 默认左对齐显示在 ComboBox 正下方；下方屏幕空间不足时才整体翻到上方。
- 水平位置按 anchor point 所在 screen 的 `availableGeometry()` 裁剪。
- 不按 aarch64 主窗口底边把 popup 任意向上平移，因为这种修正可能让列表重新
  覆盖触发控件。
- popup item 使用本地 delegate，在各主题已有 size hint 上统一增加 4 px；不覆盖
  主题中 25/35/50 px 的不同基线。
- ComboBox 本体接收 touch，并跟踪 popup Show/Hide。第一次 mouse/touch 保持 Qt
  默认打开路径；popup 可见或刚在当前事件循环中被 Wayland 隐藏时，第二次输入
  关闭 popup 并消费完整 touch/兼容 mouse 序列。
- 关闭使用 `QComboBox::hidePopup()` 维护 Qt 内部状态，并给 popup 设置
  `WA_NoMouseReplay`；不能直接 `popup->hide()` 后让同一次输入重新播放到 ComboBox。

这里调整的是共享 `Controls::ComboBox` 的 popup contract，不是 GPS 页面布局。

## 修复模式

### 1. Popup 屏幕选择使用全局 reference point

不要再使用旧式单入口，例如 `QDesktopWidget::screenNumber(this)`。popup 要以“将要显示的位置”作为屏幕选择依据：

```cpp
QRect popupAvailableGeometry(const QWidget *anchor, QWidget *popup, const QPoint &referencePoint)
{
    if (QScreen *screen = QGuiApplication::screenAt(referencePoint)) {
        return screen->availableGeometry();
    }
    ...
}
```

这能避免外接屏断开后，widget/window handle 还短暂保留旧 screen 上下文时，popup 继续按旧屏裁剪。

### 2. Popup 宽度同时考虑触发控件和内容

对菜单、下拉列表、列表型 popup，不要只按触发按钮宽度决定最终 popup 宽度。更稳定的策略是：

- 按触发控件宽度给下限，保持视觉锚点。
- 按 item/title/font/style 计算内容宽度，防止文本裁切。
- 最终宽度取两者最大值。

对 `PopupWidget`，这个规则已经落在 `listModeContentWidth()` 与 ListMode `popup()` 中。

### 3. 屏幕拓扑变化后刷新 popup geometry/cache

Qt popup 通常会缓存 size hint、action geometry、style/QSS 计算结果。外接屏断开后，即使主窗口已经被系统迁回主屏，popup 相关缓存也可能晚一帧或仍按旧上下文计算。

当前 `TitleBar` 的策略是事件驱动：

- 只监听屏幕拓扑变化，不在每次 `QMenu::aboutToShow` 上强刷。
- 只关闭属于 titlebar 菜单树的 active popup，不影响其它 popup。
- 延迟一帧刷新已有 `QMenuBar/QMenu`，不重建 action。

如果未来实测发现仍有漏网场景，再考虑把 `aboutToShow` 作为 fallback，而不是第一步就对每次菜单显示都强刷。

### 4. Top-level window 与 popup 分层处理

主窗口恢复最大化、白边、非客户区边界属于 `MainWindowChromeWin`；popup 宽度和屏幕裁剪属于各 popup owner。不要用以下方式掩盖问题：

- 通过 QSS 给所有菜单加更大的 `min-width`。
- 把 `EnumTextButton` popup 固定成更大的死宽度。
- 在 `MainWindowChromeWin` 中直接遍历/刷新所有菜单和控件 popup。
- 重建 `QMenu` / `QAction` 来绕过缓存。

## 后续排障 Checklist

遇到多屏 popup 问题时，按下面顺序定位：

1. 问题发生在顶层窗口，还是某个 popup？
2. 如果是顶层窗口：先看 `MainWindowChromeWin` 的 screen/state/DPI/frame refresh 路径。
3. 如果是 `EnumTextButton`：检查 reference point、`screenAt()` fallback、`availableGeometry()` 裁剪和内容宽度。
4. 如果是 `Controls::ComboBox`：确认定位发生在 popup Show event，而不是
   `QComboBox::showPopup()` 返回后，并检查是否仍有平台分支把 popup 推回 anchor
   区域；触摸重复展开时同时检查 popup Hide、recently-hidden guard 和兼容 mouse
   是否属于同一个物理手势。
5. 如果是标题栏菜单：检查 `TitleBar` 是否持有当前 `QMenuBar`，屏幕拓扑信号是否触发，递归 `QMenu` refresh 是否覆盖子菜单。
6. 如果只是文本被裁切：优先检查 size hint / content width，不要先改 QSS 固定宽度。
7. 如果只在断屏后发生：优先考虑屏幕拓扑变化后的缓存失效，不要假设 popup show 时 Qt 一定会重算所有 geometry。
8. 如果涉及不同 DPI：确认当前进程仍是 per-monitor DPI aware，并检查 widget/font/QSS 是否混用了固定 px 与 pt。

## 回归矩阵

建议至少覆盖：

- 双屏 100% + 150%，外接屏显示主窗口，断开外接屏后打开 `EnumTextButton`。
- 在 GPS dialog 分别打开 Antenna、Time Format ComboBox，确认列表位于控件下方；
  靠近屏幕底部时确认它整体翻到上方而不覆盖控件。
- 对上述两个 ComboBox 分别用 mouse/touch 验证：第一次点击展开、第二次点击收起、
  选择 item 后正常发出 activated，关闭动作不会立即重新展开。
- 双屏 100% + 150%，打开 TitleBar 顶部菜单，断开外接屏后再次打开顶栏菜单和子菜单。
- 外接屏最大化主窗口，最小化，再从任务栏恢复。
- 断屏后切换主题，再打开 TitleBar overflow 和普通菜单。
- 长文本 enum item、长文本菜单 item、含子菜单 item。

## 当前维护结论

- popup 屏幕归属要按显示点判断，不要按旧 widget 所在屏单点判断。
- `Controls::ComboBox` 要在首次 native mapping 前定位，不能依赖 show 后移动。
- Wayland touch 的关闭判断既要覆盖 popup 当前可见，也要覆盖 Hide 先于
  TouchBegin 到达的顺序；已处理的 touch 必须连同兼容 mouse 一起消费。
- popup 宽度要覆盖内容，不要只跟触发按钮宽度绑定。
- screen topology change 是缓存失效点，TitleBar 菜单树需要显式刷新。
- `MainWindowChromeWin`、`EnumTextButton/PopupWidget`、`Controls::ComboBox`、
  `TitleBar` 四个 owner 各自处理自己的几何问题，彼此不要越界。
