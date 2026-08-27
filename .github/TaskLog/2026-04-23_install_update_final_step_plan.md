# 2026-04-23 Install Update 最终一步计划

> 过时说明
>
> - 本文描述的是 `maintenance` 进入 install phase 前的计划，不代表当前活跃代码已经具备这些能力。
> - 当前实现仍应以 `.github/TaskLog/2026-04-24_updater_current_impl_doc_sync.md` 与 `.github/KnowledgeBase/updater_firmware_update_mechanism.md` 为准。

## 背景

- 当前 maintenance 接管固件 updater 已经跑通。
- 当前 update session、release notes 展示与插拔设备提示也已经完成并通过手测。
- 用户已确认：
  - 第一次重启动后能看到提示框
  - 第二次正常启动不会重复出现
- 因此当前剩余的最后一步，已经明确收敛到“更新 dll 和主程序”。

## 最终目标

把当前链路从：

`固件 updater 成功 -> maintenance 写 update session -> 重启主程序`

扩展为：

`固件 updater 成功 -> maintenance 执行 install.bat -> 更新 dll 和主程序 -> 写 update session -> 重启主程序`

## 建议实现边界

### 1. 执行时机

- `install.bat` 必须在主程序完全退出后执行。
- 执行顺序应为：
  1. 固件 updater 成功
  2. maintenance 调用包内 `install.bat`
  3. `install.bat` 成功后写 update session
  4. 拉起更新后的主程序

不要先重启主程序，再由主程序反过来触发软件覆盖。

### 2. 输入边界

- 当前 session 结构里已经预留：
  - `PackageRoot`
  - `InstallRoot`
  - `InstallScript`
  - `AppExecutablePath`
  - `RelaunchArguments`
- 最终一步优先直接复用这套结构，不再重新设计 maintenance 与主程序之间的协议。

### 3. maintenance 职责

- 在固件 updater 成功后，先校验 `install.bat` 是否存在。
- 通过 `QProcess` 执行 `install.bat`，并继续把 stdout/stderr 转发到当前 ProgressDialog。
- 仅当 `install.bat` 返回 0 时，才允许进入“写 update session + 重启主程序”。
- 若 `install.bat` 执行失败：
  - maintenance 保持失败状态
  - 保留输出日志
  - 不要拉起主程序并误报成功

### 4. 本阶段不扩展的内容

- 先不做复杂回滚。
- 先不改 manifest 规则。
- 先不引入额外安装器。
- 本阶段只做“正确执行 install.bat 并正确收口成功/失败”。

## 最小验证目标

1. 固件 updater 成功后，maintenance 能找到并执行包内 `install.bat`。
2. ProgressDialog 能持续显示 `install.bat` 的输出。
3. `install.bat` 成功后，maintenance 才写 update session 并重启主程序。
4. 重启后的第一次启动仍会显示 release notes + 插拔设备提示。
5. `install.bat` 失败时，不会误拉起主程序，也不会误显示更新完成。