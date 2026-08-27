# 2026-05-28 Updater: Block Update When Other Instances Running

## Goal

- 在点击 Update 后、真正启动 maintenance 之前，检查是否存在其他 SGStudio 实例。
- 若存在其他实例，弹出提示并阻止更新继续执行。
- 采用最小改动方案 A：仅改 updater 入口，不改 maintenance，不改共享内存结构。

## Current State

- 多实例共享状态来源：`Utils::InstanceStateRegistry`（QSharedMemory）。
- 更新入口：`UpdateDialog::executeUpdate()`。
- 当前行为：启动 maintenance 后仅结束当前实例，未处理其他实例仍在运行的情况。

## Minimal Change Plan

1. 在 `src/plugins/updater/updatedialog.cpp` 引入 `utils/instancestateregistry.h`。
2. 在 `UpdateDialog::executeUpdate()` 中，参数拼装与提权启动前调用 `InstanceStateRegistry::otherInstanceCount()`。
3. 若结果 `> 0`：
   - `QMessageBox::warning(...)` 提示用户先关闭其他实例。
   - `return;`，不启动 maintenance，不结束当前进程。
4. 其他流程保持不变。

## Notes

- 不做编译与运行验证（按用户要求）。
- 该改动是 UI 前置门禁，存在微小 TOCTOU 窗口；当前需求接受。
