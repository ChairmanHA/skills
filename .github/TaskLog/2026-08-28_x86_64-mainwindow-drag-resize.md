# Linux x86_64 主窗口拖拽与缩放支持方案

- 日期：2026-08-28
- 范围：仅 Linux x86_64（`scripts/build.sh` 的 `ubuntu-x86_64` / `gcc` 目标）
- 验证级别：static（本方案只做静态分析与设计，不含编译/运行验证）
- 相关 KnowledgeBase：
  - `linux_x86_64_x11_and_future_labwc_wayland.md`
  - `mainwindow_panel_minimum_height_wayland_contract.md`
  - `frameless_multimon_dpi_white_border_issue.md`

## 1. 现状分析（已核对代码）

窗口 chrome 目前是 Windows 独占实现：

| 位置 | 现状 |
| :--- | :--- |
| `src/plugins/core/mainwindow.cpp:357` | 所有平台都加 `Qt::FramelessWindowHint`（无系统标题栏、无系统 resize 边框） |
| `src/plugins/core/mainwindow.cpp:359-363` | 仅 `#ifdef Q_OS_WIN` 创建 `MainWindowChromeWin` |
| `src/plugins/core/mainwindowchrome_win.cpp:397-414` | 拖动/缩放全部由 `WM_NCHITTEST` 返回 `HTCAPTION / HTLEFT / HTBOTTOMRIGHT` 等实现 |
| `src/plugins/core/mainwindow.h:114-118` | `nativeEvent / changeEvent / showEvent` 三个 override 都在 `#ifdef Q_OS_WIN` 内 |
| `src/plugins/core/titlebar.cpp:103-106` | `#ifndef Q_OS_WIN` 时隐藏最大化/最小化按钮 |
| `src/plugins/core/mainwindow.cpp:651-671` | `#ifdef __aarch64__` 走 `showFullScreen()`；`#else`（含 x86_64）走 `restoreGeometry()` |

结论：x86_64 Linux 当前得到的是「无边框 + 不可拖动 + 不可缩放 + 无最大化按钮」的窗口，因为它既拿不到 Win32 hit-test，也没有走 aarch64 的全屏分支。

有利条件：

1. `scripts/build.sh` 的 `ubuntu-x86_64` 目标固定 `QT_QPA_PLATFORM_NAME="xcb"`、`LAYER_SHELL_OPTION="OFF"`（`scripts/build.sh:207-232`），即 X11 + 正常 WM。`Qt::FramelessWindowHint` 在 xcb 下只是清 `_MOTIF_WM_HINTS` 装饰位，窗口仍受 WM 管理，`_NET_WM_MOVERESIZE` 与 `_NET_WM_STATE_MAXIMIZED_*` 都可用。
2. Qt 版本是 `/opt/Qt/5.15.18/gcc_x86_64`，`QWindow::startSystemMove()` / `startSystemResize(Qt::Edges)` 在 5.15 已可用，xcb 插件直接映射到 `_NET_WM_MOVERESIZE`。不需要引入 xcb 私有 API 或 qwindowkit。
3. 仓库已有架构分流约定：`#if defined(Q_OS_LINUX) && !defined(Q_PROCESSOR_X86_64)`（见 `src/libs/controls/Keyboard.cpp:93`、`src/plugins/core/ethconnectdialog.cpp:585`）。新代码用其正向形式即可，天然不影响 aarch64。
4. `closeEvent` 已经保存 `saveGeometry()`（含最大化状态），x86_64 恢复路径已就绪，无需改。

## 2. 方案（只影响 Linux x86_64）

### 2.0 统一分流宏

在 `src/plugins/core/mainwindow.h` 顶部或新头文件中定义一次：

```cpp
#if defined(Q_OS_LINUX) && defined(Q_PROCESSOR_X86_64)
#  define SGS_DESKTOP_X11_CHROME 1
#endif
```

后续所有改动都挂在这个宏上，`Q_OS_WIN` 和 aarch64 分支一行不动。

### 2.1 新增 `mainwindowchrome_x11.{h,cpp}`

与 `MainWindowChromeWin` 对等的窄职责类，文件内整体包在 `#ifdef SGS_DESKTOP_X11_CHROME` 里，并加入 `src/plugins/core/CMakeLists.txt:84`（与 `mainwindowchrome_win.cpp` 同样「无条件列出、内部条件编译」）。

接口对齐现有 Win 版风格：

```cpp
class MainWindowChromeX11 : public QObject
{
public:
    MainWindowChromeX11(QMainWindow *window, TitleBar *titleBar, QObject *parent = nullptr);
    void initialize();                 // 安装过滤器 + 设定最小尺寸
    void handleWindowStateChange();    // 同步 TitleBar 最大化按钮状态
protected:
    bool eventFilter(QObject *watched, QEvent *event) override;
private:
    Qt::Edges edgesAt(const QPoint &globalPos) const;
    bool beginSystemMove();
    bool beginSystemResize(Qt::Edges edges);
    void applyEdgeCursor(Qt::Edges edges);
};
```

#### (a) 拖动：在 `TitleBar` 上装事件过滤器

- `MouseButtonPress` + `LeftButton`：复用 Win 版的判定规则，`titleBar->childAt(pos)` 后向上跳过 `WA_TransparentForMouseEvents` 的祖先；命中 `nullptr` 或 TitleBar 本体才调用 `windowHandle()->startSystemMove()`。这样菜单栏、Preset/Screenshot/MiniBar 工具组、关闭按钮不会被夺走点击。
- `MouseButtonDblClick`：与 `src/plugins/core/titlebar.cpp:86-94` 的 `btnMax` 行为一致，`isMaximized() ? showNormal() : showMaximized()`。
- 最大化状态下不禁用拖动：X11 WM 对最大化窗口的 `_NET_WM_MOVERESIZE` 会自动 unmaximize，行为与桌面一致。

#### (b) 边缘缩放：在 `qApp` 上装事件过滤器

- 主窗口子控件铺满整个客户区，`MainWindow` 自身收不到边缘的 `MouseMove`，因此必须用应用级过滤器。
- 过滤器第一步就收敛作用域：`watched` 必须是 `QWidget` 且 `qobject_cast<QWidget *>(watched)->window() == m_window`，否则直接放行，保证对话框、`Controls::PopupWidget`、`TouchNumKeyboard` overlay、helper 进程窗口零影响。
- `MouseMove`（无按键）：计算 `edgesAt()`；进入/离开边带时用 `setOverrideCursor / changeOverrideCursor / restoreOverrideCursor` 成对管理，类内用一个 `bool m_cursorOverridden` 做状态机，避免 override 栈泄漏。
- `MouseButtonPress` + `LeftButton` + `edges != 0`：调用 `startSystemResize(edges)` 并 `return true` 吃掉事件。
- 边带宽度：建议 6 逻辑像素（Win 版用 4 物理像素并有系统阴影补偿；X11 无阴影，取稍宽更好点中）；最大化/全屏时 `edgesAt()` 直接返回 0。

#### (c) 最小尺寸

Win 版靠 `WM_GETMINMAXINFO` 用 `minimumSizeHint()` 夹住（`src/plugins/core/mainwindowchrome_win.cpp:419-444`）。X11 下 WM 读 `WM_NORMAL_HINTS`，Qt 已自动写入布局最小尺寸，理论上够用；但按 `mainwindow_panel_minimum_height_wayland_contract.md` 的结论，隐藏页会把最小高度顶到 805px 这类不稳定值。

因此在 `initialize()` 里显式 `m_window->setMinimumSize(1280, 800)`，把可缩放下界固定为已验收的产品分辨率，避免用户拖到布局崩坏的尺寸。

### 2.2 `MainWindow` 接线

`src/plugins/core/mainwindow.cpp:359`：

```cpp
#ifdef Q_OS_WIN
    m_chromeWin = new MainWindowChromeWin(this, m_titleBar, this);
    m_chromeWin->initialize();
#elif defined(SGS_DESKTOP_X11_CHROME)
    m_chromeX11 = new MainWindowChromeX11(this, m_titleBar, this);
    m_chromeX11->initialize();
#endif
```

`src/plugins/core/mainwindow.h:114` 增加一个平行的 `changeEvent` override（不要动现有 `#ifdef Q_OS_WIN` 块，另起一个 `#elif defined(SGS_DESKTOP_X11_CHROME)` 块），只做一件事：`WindowStateChange` 时调 `m_chromeX11->handleWindowStateChange()`，进而调用 `m_titleBar->adjustMainWindowStateChange()`，让最大化/还原图标同步。X11 不需要 `nativeEvent`、不需要 `showEvent` 首帧修正。

窗口 flags（`src/plugins/core/mainwindow.cpp:357`）保持原样，`Qt::WindowMinMaxButtonsHint` 已在其中，xcb 会据此写 `_NET_WM_ALLOWED_ACTIONS`。

### 2.3 TitleBar 按钮可见性

`src/plugins/core/titlebar.cpp:103`：

```cpp
#if !defined(Q_OS_WIN) && !defined(SGS_DESKTOP_X11_CHROME) // 平板下隐藏最大化最小化按钮
    ui->btnMax->setVisible(false);
    ui->btnMin->setVisible(false);
#endif
```

`btnMax` / `btnMin` 的槽已是跨平台的 `showMaximized/showNormal/showMinimized`，无需改。

### 2.4 不需要改的部分（已核对，避免误伤）

- 启动几何：`initialize()` 的 `#else` 分支已走 `restoreGeometry()`，x86_64 直接受益，不动 `__aarch64__` 分支。
- 软键盘全屏副作用：`Keyboard::setSystemKeyboardVisible()` 里的 `showFullScreen()` 只在 `m_activeBackend == System` 时可达，而该状态只由 `requestSoftwareInputPanel()` 设置，后者只在 `#if defined(Q_OS_LINUX) && !defined(Q_PROCESSOR_X86_64)` 分支调用，x86_64 不可达，不会破坏可调整的窗口尺寸。
- Minibar 还原：`MinibarHelperController::restoreMainWindow()` 已按保存的 `WindowState` 分 `showFullScreen/showMaximized/showNormal`，天然兼容新增的最大化状态。
- 响应式布局：`resizeEvent -> scheduleResponsiveUiUpdate()` 已存在，缩放时 FancyTabWidget 单/双列切换自动生效。
- `build.sh` / launcher / 打包：无改动，纯源码层。

## 3. 风险与取舍

| 风险 | 说明与对策 |
| :--- | :--- |
| `qApp` 级事件过滤器 | 已有 aarch64 的隐藏光标过滤器（`src/plugins/core/mainwindow.cpp:624`），但那是 `__aarch64__` 独占，x86_64 上不共存。新过滤器必须先做 `window() == m_window` 收敛，且只在边带内 `return true`。 |
| override cursor 栈泄漏 | 必须用单一 bool 状态机加 `changeOverrideCursor`，并在 `WindowDeactivate`、`Leave`、窗口状态变化时强制 restore。 |
| WM 差异 | `_NET_WM_MOVERESIZE` 在 GNOME/KDE/XFCE/Openbox 都支持；`startSystemMove()` 返回 `bool`，为 `false` 时保持「不响应」即可。不建议再补一套 `move()` 手动拖动回退，那会形成第二条主流程。 |
| 最小尺寸硬编码 1280x800 | 这是产品已验收分辨率。若将来 x86_64 要支持更小窗口，需要先按 `mainwindow_panel_minimum_height_wayland_contract.md` 做面板压缩验证。 |

## 4. 改动清单

| 文件 | 动作 |
| :--- | :--- |
| `src/plugins/core/mainwindowchrome_x11.h` / `.cpp` | 新增（整体条件编译） |
| `src/plugins/core/CMakeLists.txt` | 新增两行源文件 |
| `src/plugins/core/mainwindow.h` | 定义 `SGS_DESKTOP_X11_CHROME`；新增 `m_chromeX11` 成员与平行 `changeEvent` |
| `src/plugins/core/mainwindow.cpp` | 构造函数内新增 `#elif` 分支 |
| `src/plugins/core/titlebar.cpp` | 隐藏最大化/最小化按钮的条件加一个否定项 |

Windows、aarch64 Raspberry Pi / RK3588 三条路径的编译产物与运行行为完全不变。

## 5. 成功标准与验收要点（x86_64 Ubuntu X11）

实现后必须全部成立：

1. 标题栏空白区可拖动窗口；菜单、Preset/Screenshot/MiniBar 按钮点击不被吞。
2. 四边加四角可缩放，光标形状正确切换；缩到 1280x800 停住不再变小。
3. 双击标题栏、点击 `btnMax` 均可最大化/还原，图标 `windowMaximized` 属性同步刷新。
4. 最大化状态下拖动标题栏能正常 unmaximize 并跟随鼠标。
5. 打开 MessageDialog / ETH Connect / 数字键盘 overlay 时，边缘缩放过滤器不干扰其鼠标交互。
6. 进入 MiniBar 再 Restore，窗口回到进入前的尺寸与最大化状态。
7. 退出后重启，`restoreGeometry()` 恢复上次尺寸与位置。
8. 交叉验证：aarch64 Raspberry Pi 与 RK3588 包仍为全屏、无最大化/最小化按钮；Windows 行为无变化。
