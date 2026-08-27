# 2026-04-23 Install Update 下一阶段计划

## 当前状态

- 本文档描述的“update session + release notes + 插拔设备提示”阶段已经完成实现。
- 用户已手工验证：
  - 第一次重启动后会弹出 release notes + 插拔设备提示
  - 关闭提示后 session 会被清理
  - 第二次正常启动不再重复弹出
- 因此本文档对应的阶段已完成，当前只作为阶段记录保留。

## 背景

- 当前 maintenance 接管固件 updater 的链路已经手工验证成功。
- 用户实测确认：固件更新完成后，若不先插拔电设备，主程序重启后有概率打不开设备。
- 当前 maintenance 成功分支只会直接拉起主程序；主程序也没有 `--UpdateCompleted` 或等价逻辑来展示 `releasenote.txt`。
- 包根目录已经稳定包含：`version.json`、`releasenote.txt`、`install.bat`、`bin/maintenance.exe`、`updater/Updater_Win.exe`。

## 本阶段目标

把当前“固件 updater 成功 -> maintenance 直接重启主程序”的链路，扩展为：

1. maintenance 在固件 updater 成功后拉起主程序，并显式传入“更新完成”上下文。
2. 主程序首次启动识别该上下文后：
   - 显示当前更新包的 `releasenote.txt`
   - 明确提示用户插拔电设备后再继续使用
3. 在上述体验收口稳定后，再进入 `install.bat` 软件安装阶段设计与实现。

## 建议实现边界

### A. 先补“更新后首次启动”信号

- 不复用当前 `PendingRestart` 语义。
- 新增独立机制，二选一：
  - `--UpdateCompleted` + 若干辅助参数
  - 或 `update-session.json` / `update-session.ini`

推荐优先使用会话文件：
- 可避免长命令行引号与编码问题
- 便于同时传递：
  - `packageRoot`
  - `releaseNotePath`
  - `targetVersion`
  - `deviceReconnectHint`
  - 后续 `installRoot / installScript / backupRoot`

### B. 主程序启动后的最小收口

- `main.cpp` 或 core plugin 启动早期识别 update session
- 在主窗口 ready 后弹一个收口对话框，内容包含：
  - 更新成功
  - 当前版本 release notes
  - 请插拔电设备后再重新连接
- 该提示应作为一次性行为，完成后清理会话标记

### C. 插拔提醒的工程约束

- 插拔提醒不能只是纯文案；至少要保证用户关闭提醒前不会被误导去立即打开设备。
- 最低要求：
  - 文案明确说明“请先断电重插设备，再重新连接”
  - 提示期间不自动触发更新后立即重连
- 若后续仍发现误开概率高，再考虑在首次启动阶段临时压制自动 open / reconnect。

### D. 为 install.bat 留参数边界

- 这一步先不执行 `install.bat`，但要在 session 结构中预留：
  - `packageRoot`
  - `installRoot`
  - `installScript`
  - `appExePath`
  - `relaunchArgs`

这样下一步进入软件安装阶段时，不需要重新改 maintenance 与主程序之间的协议。

## 最小验证目标

1. 固件 updater 成功后，maintenance 重启主程序并带上更新完成上下文。
2. 主程序首次启动能弹出 release notes + 插拔提醒。
3. 关闭该提示后，会话被清理，第二次启动不再重复弹出。
4. 在这一步稳定之前，不接 `install.bat`。

## 阶段结果

- maintenance 现已在固件 updater 成功后写入 `update-session.ini`。
- 主程序现已在首次重启动时展示独立更新完成对话框，内容包括：
  - 更新成功说明
  - release notes
  - 插拔设备提醒
- 提示关闭后会立即清理 session，第二次正常启动不会重复弹出。

## 后续移交

- 下一步不再停留在 update session 或首次启动提示本身。
- 最终一步应转到软件安装：
  - 让 maintenance 调用包内 `install.bat`
  - 完成 dll 和主程序覆盖
  - 覆盖成功后再拉起主程序
  - 失败时保留日志并阻止误报成功

## 本次实现落点

### 1. maintenance 侧

- 在固件 updater 成功后，不再只传 `restartArguments`。
- maintenance 直接把 update session 写到用户级 `AppLocalDataLocation/Updater/update-session.ini`。
- 首版 session 字段只落当前阶段确实需要的内容：
  - `TargetVersion`
  - `PackageRoot`
  - `ReleaseNotePath`
  - `DeviceReconnectHint`
  - 预留 `InstallRoot / InstallScript / AppExePath / RelaunchArgs`
- 写 session 失败时，maintenance 仍应直接报错并停止收口，避免“固件更新成功但主程序无上下文”这种半成功状态。

### 2. 主程序侧

- `app/main.cpp` 启动时读取并解析 `update-session.ini`。
- 若 session 有效，则先缓存其内容，但不立刻弹窗。
- 在 `ExtensionSystem::PluginManager::loadPlugins()` 完成后，通过一个轻量的启动后轮询逻辑等待可见顶层窗口出现。
- 找到窗口后弹一次收口对话框：
  - 标题：更新完成
  - 内容：release notes + 插拔设备提醒
- 用户确认后立即删除 session 文件，保证只弹一次。

### 3. UI 形式

- 这一版不新增复杂业务页，也不接 install.bat。
- 采用一个独立的轻量对话框承载文案，避免把长 release notes 塞进标准 message box。
- 对话框需要支持：
  - 顶部成功说明
  - 中部可滚动 release notes 文本
  - 底部突出显示“请先断电重插设备，再重新连接”

### 4. 当前阶段的取舍

- 先不压制后续所有手工 open 行为；本次先做到明确提示且一次性收口。
- 先不接 install.bat，也不在 session 中真正消费 install 字段。
- 若 release notes 文件不存在，则仍弹更新完成 + 插拔提醒，只是 release notes 区域显示缺省提示。