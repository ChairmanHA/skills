# 2026-03-17 Updater 外部执行接入计划

## 背景

- 当前 `UpdateCoordinator::onPackageLoadFinished()` 在下载并解压成功后仅执行 5 秒等待，然后走 mock success。
- 远端 `tools.json` 已新增 `UpdaterLauncher` 字段，更新包内 `tools/updater/Updater_Win.exe` 已就位。
- `PacketSpec::parseSpecFile()` 已能把 `UpdaterLauncher` 解析到 `m_updaterPath`，但上层尚未真正使用。

## 目标

- 将 mock success 替换为真实的外部 updater 启动流程。
- 仅在设备已完成安全断开后启动 updater。
- 使用 updater 进程退出码作为第一层成功/失败判定。
- 成功后提示用户并恢复正常设备重连窗口；失败时保留维护窗口收口并给出明确错误信息。

## 计划

1. 在 `PacketSpec` 中补充 `updaterPath()` 访问器，供 `UpdateCoordinator` 读取包内 updater 绝对路径。
2. 在 `UpdateCoordinator` 中引入 `QProcess` 托管外部 updater 生命周期，并连接 `finished/errorOccurred`。
3. 在 `onPackageLoadFinished()` 中校验 `PacketSpec::valid()` 与 `updaterPath()` 是否存在，再启动 `Updater_Win.exe`。
4. 移除 5 秒 mock 定时器，改为根据外部 updater 实际退出状态收口。
5. 成功路径：关闭更新中状态框，提示更新成功，点击确认后解除 `firmwareUpdateDisconnectMode` 并尝试重连设备。
6. 失败路径：关闭更新中状态框，解除更新进行中状态并提示失败原因；同时关闭 `firmwareUpdateDisconnectMode`，避免设备长期停留在维护窗口。
7. 构建验证 updater 插件相关代码至少通过 Debug 或 Release 一次。

## 取舍

- 本次先不尝试解析 updater stdout/stderr，也不做版本二次校验；以“真正拉起 updater + 退出码收口”为最小闭环。
- 默认不传复杂参数，先按当前服务器包布局直接启动 `Updater_Win.exe`；若 black-box updater 后续需要参数，再基于实际行为补充。

## 2026-03-17 补充调整

- 用户确认当前 `Updater_Win.exe` 启动后只能在任务管理器中看到进程，看不到 updater 窗口。
- 用户还确认手工双击该 exe 时需要确认交互，因此本次要求启动时显式带 `-y` 参数跳过确认。
- Windows 侧启动策略改为：
	- `QProcess` 启动参数固定附加 `-y`
	- 通过 `CreateProcessArgumentsModifier` 显式设置 `CREATE_NEW_CONSOLE + SW_SHOW`，确保控制台型 updater 能弹出可见窗口
	- 同时清除 `STARTF_USESTDHANDLES`，避免仍然走 Qt 默认管道重定向而导致窗口不可见

## 2026-03-18 临时切换到本地包模式

- 公网测试环境未就位，暂时禁用“设备连接后自动下载 tools.json、比较版本、弹三选更新框”的在线检查流程。
- 暂时禁用 `UpdateDialog`，`Update` Action 不再打开对话框，而是直接弹出“选择固件更新包”的文件选择框，只接受 `*.zip`。
- 用户选中本地 zip 后，仍复用现有安全断连、维护窗口、解压到临时目录、检查 `Updater_Win.exe`、启动 updater、等待退出码并重连设备的主链路。
- 本地包模式下不再附加 `-y` 参数；在线远程模式保留为兼容接口，但当前 UI 不再暴露。

## 2026-03-18 纯 Updater 目录解析

- 当前本地更新包解压后不一定包含 `tools/tools.json` 结构，新的实际包形态可能只是一个纯 updater 目录。
- 典型结构：目录根或某一级子目录下直接包含 `Updater_Win.exe`、`Settings.ini`、`changelog.md`、`data/`。
- 为兼容该结构，`PacketSpec` 需要新增专门解析纯 updater 目录的方法：
	- 递归查找包含 `Updater_Win.exe` 的目录
	- 将该目录视为更新包根目录
	- 直接填充 `m_updaterPath`
	- 尝试从同目录的 `changelog.md` 填充 `m_releaseNote`
- 同时修正 `PacketSpec` 的有效性判定：不能再以“只要解压成功就 valid”为准，必须在 `tools/tools.json` 或纯 updater 目录二者之一解析成功后才算有效。