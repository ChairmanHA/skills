# Dialog Wayland Overlay 实施计划

## 背景

- [src/libs/controls/dialog.cpp](src/libs/controls/dialog.cpp) 当前仍以 `Qt::Dialog | Qt::FramelessWindowHint | Qt::CustomizeWindowHint` 的顶层对话框语义运行。
- [src/libs/controls/dialog.cpp](src/libs/controls/dialog.cpp) 的 `TitleBar::mouseMoveEvent()` 直接移动顶层 dialog；在 Wayland 下这类顶层 frameless 窗口位置由 compositor 控制，拖拽不可靠。
- 多个派生类通过 `runAsync()`、`runAsShow()`、`exec()` 使用 Dialog 基类，所以必须从基类一次性收口，而不是逐个派生类修补。

## 目标

1. 让 Controls::Dialog 与 TouchNumKeyboard 一样，运行在宿主窗口内部的子控件坐标系中。
2. modal 打开路径使用 overlay 遮罩吞掉外部输入，继续提供 finished(int) / accept / reject / done 语义。
3. modeless `show()` 路径也切到宿主子控件模式，至少保证 Wayland 下可拖动。
4. 在显示前统一归一化 flags，避免派生类通过 `Qt::Tool` 等 flags 把 dialog 拉回顶层窗口。

## 设计

### 显示模式

- `show()`:
  - 作为宿主窗口子控件显示。
  - 不启用拦截外部输入的 overlay，保持 modeless 语义。
- `open()` / `runAsync()`:
  - 作为 overlay 子控件显示。
  - overlay 吞掉 dialog 外部 mouse/touch/wheel，模拟模态。
- `exec()`:
  - 与 TouchNumKeyboard 一致，不再走 `QDialog::exec()` 顶层语义。
  - 改为 `open()` + 本地 `QEventLoop` 等待 `finished(int)`。

### 宿主与 flags

- 宿主优先取 `parentWidget()->window()`；没有则回退到当前 active window 或 main window。
- 每次显示前都把 window flags 归一化为 `Qt::Widget | Qt::FramelessWindowHint | Qt::CustomizeWindowHint`，并保留其他非 window-type hints。

### 拖拽

- TitleBar 继续作为 drag handle，但改为移动子控件坐标。
- 鼠标和触摸都支持。
- 点击标题按钮时不启动拖拽。
- 移动时 clamp 到宿主可视区，避免拖出边界。


## 风险点

- `runAsShow()` 的少量使用点依赖 modeless 语义，因此不能让所有 `show()` 都自动变成遮罩模态。
- `exec()` 在仓库里有真实调用，必须保留同步返回语义。
- 派生类如 MessageDialog / InputDialog 会继续改自己的 flags；基类需要在显示前做最后一次归一化。

## 计划修改文件

- [src/libs/controls/dialog.h](src/libs/controls/dialog.h)
- [src/libs/controls/dialog.cpp](src/libs/controls/dialog.cpp)

## 验证

- 先对改动文件做静态诊断检查。
- 若诊断通过，再评估是否需要补充单点调用侧调整。