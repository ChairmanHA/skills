# 删除未接入的 PostUpdateSession / PostUpdateDialog 链路

## 范围

- 删除 `PostUpdateDialog` 和 `UpdatePostSession` 四个源文件。
- 删除 `main.cpp` 中会话读取、无效会话清理、主窗口轮询和弹窗调度逻辑。
- 删除 Win32/Linux 两个 SGStudio target 中对应的 CMake 源文件条目。
- 更新 updater 当前机制文档，明确唯一活跃完成提示为
  `--UpdateCompleted -> Core::ReleaseNotesDialog`。
- 保留已实际运行的 `ReleaseNotesDialog`、安装根 `releasenote.txt` 读取和 maintenance
  `--UpdateCompleted` 重启参数。

验证级别：`static`

## 证据

- 全仓库 `writeUpdatePostSession()` 只有头文件声明和 cpp 定义，没有调用方。
- 当前 `src/maintenance` 不引用 `UpdatePostSession`，也不写 `update-session.ini`。
- `main.cpp` 只能读取历史遗留或外部手工创建的 session 文件，当前更新事务不会产生该文件。
- `PostUpdateDialog` 只由 `main.cpp::schedulePostUpdateDialog()` 创建；删除 session reader 后没有
  其他入口。
- 当前真实更新重启由 maintenance 传入 `--UpdateCompleted`，Core `MainWindow` 直接读取
  `applicationDirPath()/../releasenote.txt` 并创建 `ReleaseNotesDialog`。

## 成功标准

1. `src/app` 不再包含或构建 `postupdatedialog.*`、`updatepostsession.*`。
2. `main.cpp` 不再读取、清理或轮询 `update-session.ini`，并移除仅服务该链路的 include。
3. 全仓库生产源码和 CMake 中不再存在 `PostUpdateDialog`、`UpdatePostSession`、
   `schedulePostUpdateDialog`、`writeUpdatePostSession` 或 `update-session.ini` 引用。
4. `src/maintenance/progressdialog.cpp` 仍以 `--UpdateCompleted` 重启应用。
5. `src/plugins/core/mainwindow.cpp` 仍解析 `--UpdateCompleted` 并创建 `ReleaseNotesDialog`。
6. `git diff --check` 通过；按仓库默认规则不主动编译。

## 实施结果

- 已删除 `postupdatedialog.cpp/.h` 和 `updatepostsession.cpp/.h`，并从 Win32/Linux
  的 SGStudio CMake 源文件列表中移除。
- 已删除 `main.cpp` 中 session 读取、损坏文件清理、主窗口轮询和弹窗调度代码，以及
  仅由该链路使用的 include 和常量。
- 生产源码和 CMake 中已无上述类、函数或 `update-session.ini` 引用；全仓库剩余命中
  仅为 KnowledgeBase 和历史 TaskLog 记录。
- 已确认 maintenance 仍以 `--UpdateCompleted` 重启，Core 仍从安装根读取
  `releasenote.txt` 并创建 `ReleaseNotesDialog`。
- `git diff --check` 和暂存区 `git diff --cached --check` 均通过。
- 按仓库默认规则未执行编译或运行验证。
