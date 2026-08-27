# 2026-04-23 Maintenance App 接入计划

## 目标

- 在 `src/app/` 下新增独立 `maintenance` app 工程。
- 主程序完成安全断开设备后，不再直接执行 `Updater*.exe`，而是启动 `maintenance`。
- `maintenance` 负责：
  - 解析命令行参数
  - 等待主程序 PID 完全退出
  - 若当前未提权则自提权重启自身
  - 启动固件 updater
  - 将 updater 的 stdout/stderr 实时写入自身 ProgressDialog
  - 根据 updater 退出码给出成功/失败状态
  - 成功后拉起主程序
- 为 `maintenance` 增加独立打包 task，产出可上传服务器的单独包。

## 约束与取舍

- 本次只实现“maintenance 接管固件 updater + 重启主程序”的第一阶段，不引入 `install.bat`、整包覆盖、回滚。
- 不复用主程序的 `PendingRestart` 机制；maintenance 会等待主程序彻底退出后自行重启，避免与插件 shutdown 竞争。
- maintenance UI 保持轻量，优先用原生 Qt Widgets，避免无关的业务依赖。

## 局部假设

- 当前更新链路的唯一执行切换点就在 `UpdateCoordinator::launchUpdaterFromSpec()`；把这里改成“启动 maintenance 并退出主程序”即可完成第一阶段迁移。
- 最便宜的判定方式是：新增 maintenance target 后直接做一次 Release 构建；若链接或运行时依赖不完整，错误会先暴露在该 target 或打包脚本上。

## 设计摘要

### 主程序侧

- `UpdateCoordinator` 新增 maintenance 启动参数组装逻辑：
  - `--parent-pid`
  - `--app-exe`
  - `--updater`
  - `--working-directory`
  - `--updater-arg`（可重复）
- 启动顺序：
  1. 校验 updater 存在
  2. 选择 maintenance 路径（包内优先，安装目录回落）
  3. detached 启动 maintenance
  4. 关闭更新状态框
  5. 调用 `QCoreApplication::quit()` 进入正常 shutdown

### maintenance 侧

- `main.cpp` 创建 `QApplication` 和 `ProgressDialog`。
- `ProgressDialog` 负责：
  - 解析参数和基本校验
  - 自提权与参数透传
  - 等待主程序退出
  - 启动 updater 并合并输出通道
  - 实时刷新日志文本
  - 退出后在成功时重启主程序

### 打包

- CMake 增加 `sgstudio_stage_maintenance` 自定义目标，生成单独目录并复制 maintenance 可执行文件、Qt 运行时和解析出的非 Qt 依赖。
- VS Code 新增 task 调用该 target，默认走 Release build tree。

## 验证

1. `CMake Build Release` 至少通过一次，确认 maintenance target 可编译。
2. 单独执行 maintenance 打包 task，确认生成独立目录。
3. 若构建阶段出现 maintenance 依赖缺失，先在 CMake 安装/打包层修正，再重跑同一验证。