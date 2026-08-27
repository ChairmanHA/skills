# Minibar Helper High-DPI 配置对齐

## 背景与证据

- 现象：小尺寸高分辨率笔记本上，MainWindow 尺寸正常，切换到独立 Minibar helper 后整体明显偏小。
- 观察：`src/app/main.cpp` 在创建 `QApplication` 前设置了
  `Qt::AA_EnableHighDpiScaling` 和 `Qt::AA_UseHighDpiPixmaps`；
  `src/app/minibarhelper/main.cpp` 当前没有对应设置。
- 观察：Windows Per-Monitor DPI 配置位于 `src/app/etc/qt.conf`，通过
  `src/app/etc.qrc` 只编入主程序；Minibar helper 的 CMake 源列表当前未包含该资源。
- 观察：helper 是独立进程，由 Main 通过 `QProcess` 从同一运行目录启动；Qt 的
  application attributes 和内嵌 `qt.conf` 不会跨进程继承。
- 推断：Main 与 helper 的 Qt 高 DPI 初始化不一致，导致相同的 QWidget/QSS 逻辑尺寸
  在高 DPI 屏上采用不同映射，表现为 Minibar 整体偏小。

## Scope

- 在 Minibar helper 创建 `QApplication` 前启用与 Main 相同的 Qt High-DPI 属性。
- 将 Main 已使用的同一份 `etc.qrc` 编入 Minibar helper，使两进程共享
  `WindowsArguments = dpiawareness=2` 配置来源。
- 不修改 Minibar 的固定尺寸、布局、QSS 或 IPC；避免在 Qt 缩放之外叠加第二套手工倍率。
- 在现有 High-DPI KnowledgeBase 中补充“每个独立 GUI executable 必须分别初始化”的约束。

## 成功标准

1. Main 与 Minibar helper 都在 `QApplication` 构造前设置相同的 High-DPI attributes。
2. Main 与 Minibar helper 都编入同一份 `src/app/etc.qrc`，Windows 下都使用
   Per-Monitor DPI aware 配置。
3. Minibar 的现有逻辑尺寸常量保持不变；在 125%/150%/200% 系统缩放下由 Qt 统一映射。
4. 静态检查确认属性设置时机、资源归属和修改范围正确。
5. 建议运行回归覆盖：小屏高分辨率单屏、100%/125%/150%/200%，以及不同缩放双屏切换。

## Verification Level

- `static`
- 按仓库默认规则不编译、不运行；如需实机确认，再使用现有 Debug build tree 执行回归。

## 风险与边界

- Qt High-DPI 属性必须在 `QApplication` 构造前设置，之后设置无效。
- 不读取并手工应用 `devicePixelRatio`，否则可能与 Qt High-DPI scaling 重复放大。
- 不新增环境变量覆盖，避免主进程与 helper 在部署环境中产生新的配置分叉。

## 实施与静态验证结果

- 已在 `src/app/minibarhelper/main.cpp` 的 `QApplication` 构造前设置
  `Qt::AA_EnableHighDpiScaling` 和 `Qt::AA_UseHighDpiPixmaps`，与 Main 对齐。
- 已在 `src/app/minibarhelper/CMakeLists.txt` 中加入 `../etc.qrc`，复用 Main 的
  `:/qt/etc/qt.conf` 与 `WindowsArguments = dpiawareness=2`。
- 已确认 helper 的现有尺寸常量和样式未改动。
- 已执行目标文件 `git diff --check`，无空白或补丁格式错误。
- 未执行编译和运行；实机验证仍建议覆盖 125%/150%/200% 以及不同缩放双屏。
