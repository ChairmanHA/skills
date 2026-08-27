# OverlayContainer 统一 Dialog / TouchNumKeyboard 方案

## 背景

- [src/libs/controls/dialog.cpp](src/libs/controls/dialog.cpp) 与 [src/libs/controls/touchnumkeyboard.cpp](src/libs/controls/touchnumkeyboard.cpp) 目前都各自实现了一套 overlay 子控件逻辑。
- 两者重复点包括：
  - 宿主窗口解析
  - overlay 几何同步
  - outside 区域 mouse/touch/wheel 拦截
  - `finished(int)` 后自动隐藏 overlay
  - child dialog 在 host 内部的 clamp
- 两者真正的差异只在：
  - TouchNumKeyboard 需要 `RepeatEnter/RepeatEsc/NoneRepeat` 的 outside-click 策略回调
  - Dialog 只需要“吞掉外部输入以模拟模态”

## 目标

1. 抽出公共的 `OverlayContainer` 类，统一宿主 overlay 行为。
2. 让 Dialog 与 TouchNumKeyboard 只保留各自差异逻辑，不再复制 overlay 实现。
3. 保持现有 `open()` / `exec()` / `finished(int)` 语义不变。

## 抽象边界

### OverlayContainer 负责

- 以 host 为 parent 覆盖宿主客户区。
- 管理 active dialog 指针和 overlay 显示/隐藏。
- 同步 host 尺寸变化。
- 根据 active dialog 几何判断 outside input。
- 对外暴露一个可选 `outsideInputHandler`，让业务层决定 outside-click 是否需要额外动作。

### Dialog / TouchNumKeyboard 保留

- 宿主解析策略。
- 自身拖拽 handle 与 clamp 调用。
- `exec()` 的本地事件循环。
- TouchNumKeyboard 的合成键业务策略。

## 计划修改文件

- [src/libs/controls/overlaycontainer.h](src/libs/controls/overlaycontainer.h)
- [src/libs/controls/overlaycontainer.cpp](src/libs/controls/overlaycontainer.cpp)
- [src/libs/controls/CMakeLists.txt](src/libs/controls/CMakeLists.txt)
- [src/libs/controls/dialog.cpp](src/libs/controls/dialog.cpp)
- [src/libs/controls/dialog.h](src/libs/controls/dialog.h)
- [src/libs/controls/touchnumkeyboard.cpp](src/libs/controls/touchnumkeyboard.cpp)
- [src/libs/controls/touchnumkeyboard.h](src/libs/controls/touchnumkeyboard.h)

## 风险控制

- `outsideInputHandler` 只在 outside press/begin 路径触发一次，release/end 继续吞掉，避免多次关闭。
- overlay 生命周期跟随 host，但通过 dialog destroyed 信号做清理，避免悬挂。
- `QTimer::singleShot` / 异步回调继续使用 `QPointer` 或 QObject 上下文。