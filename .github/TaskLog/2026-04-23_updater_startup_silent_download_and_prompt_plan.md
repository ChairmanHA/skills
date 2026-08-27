# 2026-04-23 Updater 启动静默下载与三选提示实现计划

> 过时说明
>
> - 本文记录的是旧的 `UpdateCoordinator` 方案目标；当前 CMake 活跃实现并没有这条启动静默下载与自动三选提示链路。
> - 当前实现请以 `.github/TaskLog/2026-04-24_updater_current_impl_doc_sync.md` 与 `.github/KnowledgeBase/updater_firmware_update_mechanism.md` 为准。

## 背景

- 当前 `Updater::Internal::Plugin` 仍保留“手动本地包更新”为主的入口。
- 当前 `UpdateCoordinator` 在线检查触发点是“设备 open 后下载 tools.json 并比较 FPGA/MCU/BUS 版本”。
- 用户已将 `Settings.ini` 中 `online=true`，并配置可访问的 `packageUrl=https://localhost:8443/updates/releases/SGStudio.zip`。
- 本阶段目标不是恢复完整 manifest/兼容矩阵，而是先打通“启动静默下载更新包 -> 设备连接且包已就绪时比较 GUI 版本 -> 弹三选框”的最小闭环。

## 本次目标

1. 应用启动后在后台静默下载 `packageUrl` 指向的更新包，并解析包内版本元数据。
2. 缓存目录清理策略改成“仅清理过期旧缓存”，避免像当前 `RemoteFileLoader::clearDirectory()` 那样每次把整个 Updater 缓存目录清空。
3. 当且仅当“设备已连接”且“远端包已下载解析完成”两个条件同时满足时，执行更新判断。
4. 当前 API 不上报不匹配时，改为仅比较 GUI 版本：本地 `SGS_VERSION` vs 远端包 `Version`，不比较 FPGA/MCU/BUS。
5. 判断到远端 GUI 版本更高时，弹出三选框：`Update Now / Later / Ignore`。
6. 保留必要日志，便于 Debug 运行验证流程是否真正打通。

## 设计要点

- `Plugin` 在 `extensionsInitialized()` 中：
  - 创建 `UpdateCoordinator`
  - 先执行一次 Updater 缓存目录过期清理（参考旧文档 7200 秒策略）
  - `online=true` 且 `packageUrl` 非空时立即发起静默预下载
  - 同时监听 `DeviceManager::currentDeviceOpenStateChanged`
- `UpdateCoordinator` 新增状态：
  - `m_startupPackageSpec`
  - `m_startupPackageReady`
  - `m_deviceOpen`
  - `m_promptedVersion`
- 评估逻辑统一收口到一个方法：
  - 若设备未连接，记录状态但不弹窗
  - 若包未准备好，等待下载完成
  - 两者都满足后，以 `SGS_VERSION` 与远端 `PacketSpec::version()` 比较
  - 版本更高且未 Ignore 且未对当前版本弹过窗时，弹三选框
- `Ignore` 持久化从原来的 FPGA/MCU/BUS 三字段切换到单一 GUI 版本字段，避免当前比较维度与忽略维度不一致。
- `Update Now` 优先复用已静默下载好的包，避免再次下载；若缓存包失效，再回落到现有远端下载路径。

## 验证

1. Debug 构建 Updater 相关改动。
2. 运行 Debug SGStudio，观察日志：
   - 启动是否发起静默下载
   - 下载完成后是否识别远端 `Version`
   - 设备连接后是否进入统一评估
   - 远端 GUI 版本更高时是否弹出三选框
3. 若流程未通，继续补日志和最小修正，直至日志能证明这条链路成立。