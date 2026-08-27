# SGStudio 在线更新改为纯手动下载方案

## 任务目标

将当前“启动时静默下载完整远端包并比较版本”的在线更新机制，改为完全由用户发起的手动下载：

- 启动 SGStudio 时不发起任何更新网络请求，也不下载远端包。
- 打开更新窗口或仅切换到在线来源时不发起网络请求。
- 只有用户点击在线来源右侧的 `Download` 按钮后，才下载、解压和解析远端包。
- 下载和解压交互、布局、尺寸与
  `D:/development/SAStudioEx/src/plugins/updater/updatedialog.cpp`
  及其 `updatedialog.ui` 保持一致。
- 远端包完整下载、解压并解析有效后显示版本和 release notes；软件版本关系不作为
  `Update` 按钮的门控，仍允许用户执行软件/固件更新。
- 保留现有可靠的临时工作区、锁、handoff 接管和清理机制，不照搬参考项目较弱的缓存实现。

## 范围与验证级别

- 范围：
  - `src/plugins/updater/plugin.cpp`
  - `src/plugins/updater/plugin.h`
  - `src/plugins/updater/updatedialog.cpp`
  - `src/plugins/updater/updatedialog.h`
  - `src/plugins/updater/updatedialog.ui`
  - `src/plugins/updater/packetspec.cpp`
  - `src/plugins/updater/packetspec.h`
  - `configuration_files/*/Settings.ini`
  - `scripts/build.bat`
  - `scripts/build_pi.sh`
  - updater 与打包相关 KnowledgeBase / 联测文档
- 本轮验证级别：`static`，只形成方案，不修改业务代码。
- 实施后仅仅做静态验证即可，我会自行编译测试。

## 当前实现观察

1. `Plugin::extensionsInitialized()` 调用 `startStartupOnlineCheck()`。
2. 启动逻辑先接管 handoff 缓存并清扫一次遗留工作区；若没有 handoff 且
   `Settings.ini [Update]/online=true`，就创建 `PacketSpec` 并调用 `loadUrl()`。
3. 因为远端没有独立 manifest，当前所谓“检查版本”必须下载并解压整个软件包后，才能读取
   `version.json`，这是启动流量过大的直接原因。
4. `UpdateDialog::typeChanged(1)` 调用 `ensureRemotePacketSpecLoaded()`，因此仅切换到
   `Latest Online` 也可能触发完整包下载。
5. 正式打包脚本当前会强制将 staging 中的 `Settings.ini` 改成 `online=true`，并把它当作
   正式包的校验条件。
6. 当前 `PacketSpec` 已具备 UUID 工作区、`QLockFile`、失败清理、析构重试、handoff 标记、
   更新后接管以及启动时单次遗留清扫。这些机制应继续保留。
7. 当前在线包 URL 是按平台和语言由代码/CMake 宏生成的；`Settings.ini` 中的 URL 实际没有
   接入下载地址。

## 非目标

- 不修改 maintenance、外部 updater、`ProgressDialog` 或 `CopyThread`。
- 不增加断点续传。
- 不增加启动时 HEAD 请求、manifest 请求或任何其他“轻量自动检查”。
- 不把在线包持久保留为跨普通启动的长期缓存。
- 不删除或改写用户选择的本地更新归档。
- 不改变 Windows 提权、Linux 普通权限启动、软件目录替换和更新后重启链路。

## 设计

### 1. 启动阶段：只做本地缓存接管，不做在线检查

将 `startStartupOnlineCheck()` 拆成语义明确的本地初始化，例如
`adoptHandoffCacheAndCleanup()`：

1. 调用 `PacketSpec::takeHandoffRemotePacket()`，尝试接管上次真实更新留下的有效在线包。
2. 接管完成后调用一次 `PacketSpec::cleanupAbandonedScratchData()`。
3. 不创建新的远端 `PacketSpec`，不调用 `loadUrl()`，不比较版本，不显示新版本提示。

保留启动接管的原因：

- 它只访问本地临时目录，不产生网络请求。
- 更新成功并重启 SGStudio 后，同一份在线包仍可注入 `UpdateDialog`。
- 用户再次打开更新窗口时，版本和 release notes 能立即显示，不会重复下载。

删除启动自动更新相关逻辑：

- `[Update]/online` 读取和 `m_online`
- `onlineCheckEnabled()`
- 启动远端下载与版本比较函数
- 自动新版本 `Yes / No` 提示及其状态
- 仅为自动提示服务的点分版本比较帮助代码

`m_startupRemoteSpec` 可重命名为 `m_handoffRemoteSpec`，避免继续表达“启动在线检查”的错误语义。

### 2. Settings.ini 与正式打包脚本

彻底移除 `[Update]/online` 的功能语义：

- 从各 `configuration_files/*/Settings.ini` 删除 `online=false`；若 `[Update]` 已无其他有效项，
  删除空节。
- `scripts/build.bat` 和 `scripts/build_pi.sh` 不再将它改成 `online=true`，也不再把
  `online=true` 作为正式包成功条件。
- 已安装用户配置中遗留的 `online=true/false` 只被忽略，无需迁移或写回。

在线包 URL 的现有平台映射暂时保留在 `PacketSpec`，因为它仍是用户点击 `Download` 后的下载地址。
不在本任务中扩展运行时 URL 配置。

### 3. UpdateDialog 布局：严格对齐参考实现

对话框继续固定为 `800 x 600`，不因进度条增高。

布局顺序严格采用参考实现：

1. Target Release Note 分组及 `releaseNote`
2. `downloadStatusWidget`
3. 在线/本地来源选择行
4. 本地文件选择行
5. 底部 `Update` 行

`downloadStatusWidget` 使用参考实现相同属性：

- `QFrame`
- vertical size policy 为 `Fixed`
- minimum height 为 `35`
- `NoFrame`、`Plain`、line width `0`
- 内部水平布局四边 margin 均为 `0`
- 左侧 `downloadStatusLabel` minimum width `120`
- 中间 `downloadProgressBar` minimum width `200`，显示百分比文本
- 右侧 `downloadSizeLabel` minimum width `120`，右对齐
- 由主题 QSS 对 `QFrame#downloadStatusWidget` 显式设置透明背景

在线/本地来源选择行改成参考实现相同的固定高度 `37` 的 `QWidget + QHBoxLayout`：

- 左侧保留 SGStudio 现有在线/本地来源文案。
- 在线来源右侧使用伸缩 spacer。
- 最右侧放置高度 `37` 的 `btnStartDownload`，文本为 `Download`。
- 选择在线来源时显示 Download；选择本地来源时隐藏。

空间交互是硬性要求：

- Idle：隐藏 `downloadStatusWidget`，release notes 占满原有高度。
- Downloading / Decompressing / Failed：显示高度 35 的状态行，由上方 release notes 自动缩短，
  对话框总高度不变。
- Ready：先按参考实现短暂显示 `Download complete` 和 100%，3 秒后隐藏状态行；
  release notes 自动恢复原高度。
- 切到本地来源时隐藏在线进度区域；切回在线来源时根据实际 phase 恢复显示。

不使用手工计算或硬编码修改 `releaseNote` 高度，依赖与参考实现一致的垂直布局和 size policy
完成收缩/恢复，避免 Windows 与 Linux 字体/DPI 差异造成布局偏差。

删除底部失效且误导用户的 `Notify` / `autoCheckUpdates` 复选框及对应空函数、连接。

### 4. 手动下载状态机

`PacketSpec` 增加并暴露与参考实现一致的阶段：

```text
Idle -> Downloading -> Decompressing -> Ready
                       \             /
                        -> Failed <-
```

信号：

- `downloadProgress(qint64 received, qint64 total)`
- `loadPhaseChanged(PacketSpec::LoadPhase phase)`
- `loadFailed(const QString &reason)`

行为：

- 仅 `btnStartDownload` 的 clicked 处理调用 `m_remoteFile->loadUrl(...)`。
- `switchOnline()` 和 `typeChanged(1)` 只切换 UI、显示已有缓存信息，不调用 `loadUrl()`。
- 下载开始前清空在线目标版本和 release notes，禁用 `Update`。
- CURL 进度回调继续优先检查取消请求；未取消时发送 received/total。
- `total > 0` 显示 0–100% 和 `received / total`；未知总大小时显示不确定进度和 received。
- 下载结束后切换到 `Decompressing`，显示 `Preparing update package` 和不确定进度。
- 只有解压、`version.json` 解析、maintenance/updater 查找及 firmware profile 元数据校验全部成功，
  才切换到 `Ready`、填充版本/release notes 并刷新 `Update`。
- 所有失败出口先执行现有工作区清理，再进入 `Failed` 并提供简短 UI 状态；详细原因写日志。
- 连接 `QThread::finished` 刷新按钮状态，确保失败信号发生在 worker 尚未完全退出时，不会让
  Download 永久保持禁用。
- 失败后允许再次点击 Download；`prepareForLoad()` 必须确认上次工作区已清理后才创建新工作区。
- Ready 后 Download 按钮保持可见但禁用，与参考实现一致，避免同一会话重复下载。

关闭更新窗口不取消下载；插件保留同一个 `UpdateDialog` 实例，因此重新打开后继续显示当前阶段。
退出整个 SGStudio 时仍取消未完成的 CURL 下载、等待 worker 收口并清理工作区。

### 5. 版本信息与 Update 门控

手动下载成功后：

- 始终显示包内 `Version`、`Software`、固件目标版本和 release notes。
- 不再以远端 `Software` 相对本地 `SGS_VERSION` 的大小关系作为清理或更新门控。
- 相同版本仍允许 Update，以支持固件重刷。
- 按本次“无论版本是否相等都允许”的要求，较低软件版本也不做硬阻止，不显示版本关系警告；
  包有效且 firmware profile 可解析时允许用户直接决定是否更新。

仍然保留的硬门控：

- 包下载、解压和结构解析必须有效。
- maintenance/updater 必须存在。
- 当前设备选件已知时，必须能精确解析对应 firmware profile。
- 更新事务已经启动时不得再次点击。
- 其他 SGStudio 实例仍必须关闭。

### 6. 在线缓存清理建议

推荐继续使用“会话缓存 + 更新 handoff”，不引入长期持久缓存：

| 场景 | 在线下载/解压工作区 |
| --- | --- |
| 下载中、解压中关闭更新窗口 | 保留并继续，重新打开窗口可复用 |
| 下载并解析成功，用户暂不更新 | 保留到本次 SGStudio 会话结束 |
| 同一会话重复打开更新窗口 | 直接复用，不重复下载 |
| 点击 Update，handoff 标记写入和 maintenance 接管成功 | 跨进程保留 |
| 更新后新 SGStudio 启动 | 本地验证并接管；本会话继续复用 |
| 普通退出 SGStudio，且没有新的成功 handoff | 删除 |
| 下载失败、HTTP 错误、用户退出导致取消 | 立即删除整个 UUID 工作区 |
| 解压、解析、包结构或 firmware 元数据失败 | 立即删除整个 UUID 工作区 |
| 删除失败 | 保留 owner 路径，析构重试；下次启动单次锁感知清扫再兜底 |

因为当前不支持断点续传，残缺压缩包没有复用价值，必须清理。因为远端使用不带版本号的稳定 URL，
又没有客户端可验证的 manifest/hash，成功包也不适合跨普通启动无限期复用，否则服务端换包后客户端
可能继续展示旧缓存。

本地更新边界保持不变：

- 绝不删除、移动或改写用户选择的本地 zip/tar.gz。
- 只清理由 SGStudio 自己创建的本地包解压工作区。

可选的后续优化不是本任务的一部分：服务器提供小型 manifest（版本、大小、SHA-256、签名、包 URL）。
用户点击 Download 后先手动请求 manifest；若本地持久缓存 hash 匹配则复用，否则再下载大包。这样才能在
不产生启动网络流量的同时安全地长期复用完整包。

## 文件级改造清单

### `plugin.cpp/.h`

- 保留本地 handoff 接管和启动单次清扫。
- 删除启动联网、版本比较、自动提示和 `[Update]/online` 状态。
- 打开 UpdateDialog 时只注入已接管的 handoff 包；没有缓存时保持 Idle。

### `updatedialog.ui`

- 按参考实现增加 download status 行和右侧 Download 按钮。
- 依靠 release notes 上方伸缩空间实现进度显示时缩短、成功后恢复。
- 删除 Notify 复选框。

### `updatedialog.cpp/.h`

- 删除 `ensureRemotePacketSpecLoaded()`、fallback 标志和自动提示空逻辑。
- 增加手动下载、进度、阶段、失败及完成后延迟隐藏处理。
- 所有 remote PacketSpec 替换/注入路径统一重新连接上述信号。
- 保持本地更新和 maintenance 参数构造不变。

### `packetspec.cpp/.h`

- 增加 phase/progress/failure 信号。
- 保留现有取消检查、临时目录、锁、handoff 和失败清理。
- 为每个失败出口提供统一收口，避免 UI phase 与磁盘清理结果不一致。

### 配置、脚本和文档

- 删除 `online` 配置与正式包强制开启逻辑。
- 使用 `git mv` 将自动更新联测指南改为手动在线下载联测指南，并更新 KnowledgeBase Index。
- 更新 updater 当前机制、整改重点和打包/水印文档，删除启动自动下载相关断言。

## 实施验收标准

### 静态

- `plugin.cpp` 中不存在启动调用 `loadUrl()` 的路径。
- `typeChanged()`、`switchOnline()`、`showEvent()` 中不存在 `loadUrl()`。
- updater 业务代码中只有 Download clicked 路径能开始远端加载。
- 打包脚本不再写入或校验 `online=true`。
- 用户本地归档路径从不进入递归删除目标。

### Windows x86_64 与 Linux aarch64 运行验收

1. 启动 SGStudio 后观察日志和服务器访问日志：无更新 URL 请求。
2. 打开 UpdateDialog：无请求。
3. 切换到在线来源：无请求；右侧出现与参考实现一致的 Download 按钮。
4. 点击 Download：只产生一次完整包 GET。
5. 下载时状态行出现，窗口仍为 800x600，release notes 高度缩短。
6. 已知 Content-Length 时百分比和字节数递增；未知时使用不确定进度。
7. 下载完成后进入 Preparing 状态，直到解压和解析完成。
8. Ready 后版本/release notes 立即显示，100% 状态短暂显示后隐藏，release notes 恢复原高度。
9. 远端软件版本高于、等于和低于本地版本时，只要包/profile 有效，Update 均可用。
10. 关闭并重新打开 UpdateDialog：不重复下载，立即复用或显示当前进度。
11. 下载中退出应用：下载被取消，临时归档和解压目录被删除。
12. 模拟下载、解压、解析失败：可重试，旧 UUID 工作区已清理。
13. 在线更新成功并自动重启后，再打开 UpdateDialog：立即显示 handoff 包信息，无网络请求。
14. 普通退出后再次启动：无网络请求；没有 handoff 时在线页回到 Idle。
15. 选择本地包更新：用户选择的原始归档始终存在，只有程序生成的解压工作区被清理。

## 风险与控制

- 参考实现失败后可能因 worker 尚未结束而暂时保持按钮禁用；通过 `QThread::finished` 再刷新规避。
- 不手工设置 releaseNote 高度，防止 Windows/Linux DPI 与字体差异导致抖动或越界。
- 不把“去掉启动联网”误实现为“去掉启动本地 handoff 接管”，否则更新后会丢失可复用信息。
- 不把版本较低当作无效包，不显示版本关系警告，也不隐式清理该有效缓存。

## 2026-07-30 实施结果

- 已删除启动在线下载、版本比较、自动提示和 UpdateDialog 来源切换下载入口。
- 启动阶段只执行 handoff 本地接管和一次遗留工作区清扫。
- 已按参考实现加入 Download 按钮、35px 状态行、下载百分比/大小、解压阶段和完成后
  3 秒隐藏；对话框仍为 800x600，release notes 依靠布局自动缩短/恢复。
- 远端 `loadUrl()` 的唯一直接调用点是 Download clicked 处理。
- 远端软件版本高、相同或低都不显示版本关系警告，也不参与 Update 门控。
- 已删除 Settings 模板中的 `[Update]/online`，并修改 Windows/树莓派正式打包逻辑，
  使其删除而不是写入该废弃键。
- 已更新 KnowledgeBase，并将 Standard CN 联测指南改为手动下载版本。

静态验证：

- `updatedialog.ui` XML 可解析；根布局顺序与参考实现一致。
- 状态行高度 35、状态/大小标签最小宽度 120、进度条最小宽度 200、来源行和 Download
  按钮高度 37，Notify 控件不存在。
- updater 源码中只有一处直接 `loadUrl()` 调用，位于 `startOnlineDownload()`。
- updater、配置模板和指定打包脚本中不再存在自动更新符号或 `online=...` 配置。
- `scripts/build_pi.sh` 通过 Git Bash `bash -n`。
- Windows PowerShell 正则和 Linux sed 的正式包配置清理表达式均用内存样例验证通过。
- `git diff --check` 通过。
- 按仓库规则未执行编译和运行验证。

## 2026-07-30 实机样式修正

观察（根据实机复测修正）：

- 前一版取消 `Qt::WA_StyledBackground` 后黑色背景仍然存在，说明该属性不是根因。
- 深色主题的全局 `QWidget` 规则设置了黑色背景，而 UpdateDialog 现有透明规则未覆盖新增的
  `QFrame#downloadStatusWidget`，因此状态行仍显示为黑色。
- `btnStartDownload` 未设置宽度上下限，宽度会随当前显示文本自动变化。

修正边界与验收标准：

- 所有随包发布的深色/浅色主题均对 `QFrame#downloadStatusWidget` 显式设置透明背景，
  使其透出 UpdateDialog 背景。
- `btnStartDownload` 使用与现有更新窗口按钮一致的 100px 宽度基准，并同时设置最小和最大宽度，
  保证按钮宽度不随翻译文本变化。
- 不修改进度条本体、状态行尺寸、布局和下载状态机。
- 静态确认 10 份主题文件均包含上述专用规则，且 UI XML 仍可解析。
