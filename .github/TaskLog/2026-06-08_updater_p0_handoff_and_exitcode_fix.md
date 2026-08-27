# 2026-06-08 Updater P0 Handoff And ExitCode Fix

## Goal

- 在不切到 `_bak` 架构的前提下，修复当前活跃 updater 链路中的两个 P0：
  - 主程序未确认 maintenance 接管即退出。
  - maintenance 忽略 updater 退出结果，仍继续复制或重启。
- 保持现有手动 UpdateDialog + maintenance + 可选复制链路可继续工作。
- 同步更新知识库文档，使其反映修复后的现状与剩余风险。

## Local Hypothesis

- 当前真正控制 P0 的局部路径就是 `src/plugins/updater/updatedialog.cpp::executeUpdate()` 与 `src/maintenance/progressdialog.cpp::onUpdaterProcessFinished()`。
- 只要让主程序在 maintenance 明确进入接管状态后再退出，并让 maintenance 仅在 updater `NormalExit + exitCode == 0` 时继续后续步骤，就能在现有机制下去掉这两个事故条件，而无需重建 `UpdateCoordinator`。

## Cheap Disproving Check

- 若 `executeUpdate()` 里已经存在可靠的接管确认握手，或 `onUpdaterProcessFinished()` 已经对失败退出做门控，则该假设不成立。
- 当前代码检查结果：前者只有“启动成功即退出”，后者无条件继续，因此假设成立。

## Minimal Change Plan

1. 为 maintenance 增加一个轻量接管确认信号，主程序仅在收到确认后才结束自身。
2. 为 maintenance 增加 updater 失败门控与错误输出，失败时不复制、不重启。
3. 对改动文件做窄编译验证，确保当前 Debug build tree 可通过相关 target。
4. 更新 `.github/KnowledgeBase/updater_mechanism_gap_and_remediation.md`，把已完成止血项与剩余问题分开描述。
