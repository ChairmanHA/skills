# 2026-04-24 Updater 当前实现文档同步

## 目标

- 按当前 CMake 实际编译的实现刷新 Updater 相关知识库文档。
- 修正 repo memory 中仍描述旧 `UpdateCoordinator` / `maintenance install phase` 链路的条目。

## 已确认的当前实现

- 当前编译的是 `src/plugins/updater/` 与 `src/app/maintenance/`；`src/plugins/updater_bak/`、`src/app/maintenance_bak/` 不在当前 CMake 主链路。
- `Updater::Internal::Plugin` 当前只注册 `System -> Update` 菜单并创建 `UpdateDialog`；`Settings.ini` 读取 `online/packageUrl` 的代码已被注释，`extensionsInitialized()` 只做 `AppLocalDataLocation` 一级缓存清理，超时时间是 60 秒。
- `PacketSpec::loadUrl()` 会忽略传入 URL，直接按编译期宏拼接 `https://www.harogic.cn/sg/.../<PACKET_FILE_NAME>.zip`。
- 自动提示入口 `showNewVersionNotification()`、`autoCheckUpdates`、Ignore 持久化当前都只是 TODO / 注释代码，没有真实生效。
- `UpdateDialog` 当前是手动入口：用户在对话框里选择 `Latest Online`、`Local Default` 或 `Local File` 后执行更新。
- Windows 下执行更新时，`UpdateDialog` 通过 `ShellExecuteExW(..., "runas")` 提权启动 `maintenance.exe`，随后直接 `taskkill /PID <self> /F /T` 强杀当前主程序，而不是走设备安全断开或正常 shutdown。
- `maintenance` 当前参数协议很简单：`argv[1]` 是 updater 可执行文件；若还带 `argv[2]`、`argv[3]`，则表示“更新包根目录 -> 当前安装目录”的复制更新。
- `maintenance` 当前流程是：延迟 2 秒启动包内 updater，固定传 `-y`；updater 结束后若提供了源/目标目录，则用 `CopyThread` 把目标目录整体重命名为 `_bak*`，把源目录复制为同级新目录，再把备份中的少量目录/文件合并回新目录，然后删除备份并启动新目录下的应用。
- 当前活跃 `maintenance` 不解析 `install-manifest.json`，不写 `update-session.ini`，也不触发 `PostUpdateDialog`；这些能力只存在于 `_bak` 代码或主程序的遗留支撑代码里。

## 本次同步范围

- 更新 `.github/KnowledgeBase/updater_firmware_update_mechanism.md`，明确区分“当前活跃实现”和“旧 `_bak` 方案”。
- 更新 repo memory 中与启动静默下载、maintenance handoff、install phase、settings merge、UAC 语义相关的旧结论。

## 不做的事

- 不修改当前 updater / maintenance 行为。
- 不删除 `_bak` 代码，仅在文档中标明其非活跃状态。