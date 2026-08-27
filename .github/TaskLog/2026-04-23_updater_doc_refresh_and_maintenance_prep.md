# 2026-04-23 Updater 文档刷新与 Maintenance 准备

> 过时说明
>
> - 本文基于当时的 `UpdateCoordinator + maintenance` 设计阶段结论撰写，当前已不代表活跃编译链路。
> - 当前实现请以 `.github/TaskLog/2026-04-24_updater_current_impl_doc_sync.md` 与 `.github/KnowledgeBase/updater_firmware_update_mechanism.md` 为准。

## 目的

- 更新 `.github/KnowledgeBase/updater_firmware_update_mechanism.md`，把已经落地的 Updater 状态写准确。
- 记录当前已完成的能力：启动静默下载、缓存过期清理、GUI 版本比较、三选提示、新包根目录解析、真实 updater 可访问性验证。
- 为下一步新增 `maintenance` 项目做准备，先把主程序与 maintenance 的职责边界写清楚。

## 当前已完成

1. `Settings.ini` 已切到 `online=true`，默认从 `packageUrl` 启动静默预下载。
2. Updater 缓存策略已改为“启动时清理 7200 秒前旧缓存”，不再每次下载前整目录清空。
3. 当前默认比较逻辑已改为：`SGS_VERSION` vs 远端包根目录 `version.json` 中的 `Version`。
4. 设备连接且预下载包已解析完成时，会弹出 `Update Now / Later / Ignore` 三选框。
5. `PacketSpec` 已支持新 SGStudio 包根目录契约：`version.json`、`releasenote.txt`、`install.bat`、`updater/Updater*.exe`。
6. 已实际验证最新下载包中存在真实 `Updater_Win.exe`，当前仅做可访问性验证，尚未执行。

## 当前未完成

1. 尚未引入独立 `maintenance` 项目或 target。
2. 当前 `UpdateCoordinator` 仍由主程序直接持有更新会话；下一步需要改为“主程序安全断开 + 正常退出 -> maintenance 接管”。
3. 当前尚未把 `Updater_Win.exe` 的执行迁移到 maintenance。
4. 当前尚未定义 maintenance 的会话参数、日志、提权、失败收口与重启责任边界。

## 下一步建议边界

- 主程序负责：
  - 版本判断与三选提示
  - 安全断开设备
  - 收集 `packageRoot/installRoot/appExePath/appPid/updaterPath/installScript` 等参数
  - 拉起 `maintenance` 后正常退出
- maintenance 负责：
  - 等待主程序完全退出
  - 统一处理提权
  - 执行 `Updater_Win.exe`
  - 后续再扩展到 `install.bat`、回滚和重启

## 本次只更新文档

- 本次不改代码行为，不新增 maintenance 项目；只把现状和下一步准备写入知识库，避免后续实现仍参考旧的“local-only + tools.json”描述。