# Updater 当前实现说明

本文档描述当前 CMake 实际编译并运行的 updater / maintenance 链路，入口主要在：

- `src/plugins/updater/updatedialog.cpp`
- `src/plugins/updater/packetspec.cpp`
- `src/plugins/updater/plugin.cpp`
- `src/maintenance/main.cpp`
- `src/maintenance/progressdialog.cpp`
- `src/maintenance/copythread.cpp`

仓库中仍有 `_bak` 目录保留更完整的历史方案，但它们不是当前活跃路径。排查现网行为时，以本文和当前源码为准。

---

## 1. 当前总体行为

当前在线更新已经改为完全手动：

1. Updater 插件初始化时注册 `System -> Update` 菜单入口。
2. `extensionsInitialized()` 只安排一次本地缓存初始化：先尝试接管上次真实更新留下的
   handoff 包，再对系统临时目录与旧 `AppLocalDataLocation` 中未被活跃进程锁定的 updater
   工作区清扫一次；该过程不发起网络请求，也不再做延迟或每分钟清扫。
3. 启动时不创建远端下载任务、不比较远端软件版本、不弹出自动更新提示。
4. 打开 `UpdateDialog` 或切换到 `Latest Online` 时仍不联网；在线行右侧显示
   `Download` 按钮。
5. 只有用户点击 `Download`，对话框才调用 `PacketSpec::loadUrl()`，依次进入下载、解压和
   包解析。
6. 下载时在 release notes 下方显示进度行，release notes 自动让出高度；解压和解析阶段显示
   `Preparing update package`。完整成功后短暂显示 100%，随后隐藏进度行并恢复 release notes
   高度。
7. 有效包始终显示 `Version`、`Software`、固件目标和 release notes。远端软件版本高于、
   等于或低于当前 `SGS_VERSION` 都不产生警告，也不参与 `Update` 门控。
8. `Local Default` 使用当前安装目录自带的 updater 和固件，只执行固件更新；它不携带
   包源目录和安装目标目录，因此 maintenance 不进入 `CopyThread`，不会备份、复制或覆盖
   SGStudio 文件。
9. 关闭更新窗口不会取消下载或删除有效包；同一 SGStudio 会话再次打开窗口继续显示当前
   进度或直接复用完成的包。
10. 用户点击 `Update` 后，主程序进入 `firmwareUpdateDisconnectMode`。
11. 如果当前有设备，主程序先把 `currentDevice` 置空，并等待
    `currentDeviceDisconnectFinished()`。
12. 在线或本地归档源先保留其解压工作区；在线源还必须成功写入 handoff-cache 标记。
    `Local Default` 没有下载/解压工作区，明确跳过这一步。
13. 主程序启动 maintenance，并传入 updater 路径、设备参数、可选的复制源/目标路径、
    父进程 PID、handoff ready marker 路径。
14. maintenance 初始化完成后写入 ready marker。
15. 主程序确认 ready marker 后请求正常关闭窗口，进入原有 shutdown 链路。
16. maintenance 等待父进程退出；如果超时，执行一次强制终止兜底。
17. maintenance 启动外部 updater。只有 updater 正常退出且 exit code 为 0，才继续复制
    安装包或重启应用。

因此当前正常路径已经不是“启动 maintenance 后立刻强杀主程序”。强杀只保留在 maintenance 等待父进程退出超时后的兜底分支。

---

## 2. 包解析

`PacketSpec` 负责下载、解压和解析更新包。当前包结构假设如下：

```text
<zip-root>/
  <package-folder>/
    version.json
    releasenote.txt
    updater/
      updater.exe  # Windows
      updater      # Linux
    bin/
      maintenance*
      ...
```

当前解析规则：

- 下载与解压共用一个临时工作区。首选路径为
  `QStandardPaths::TempLocation/SGStudio/updater/<uuid>/`，其次为
  `QDir::tempPath()` 下的同名目录，只有前两者不可写时才回退到
  `AppLocalDataLocation/SGStudio/updater/<uuid>/`。
- 每个 UUID 工作区由 `PacketSpec` 持有 `.sgstudio-updater.lock`。正常工作区只有在清扫能够取得这把锁时才删除；准备交接的在线包必须先成功写入 `.sgstudio-handoff-cache`，才能启动 maintenance 并跨越主进程退出阶段。
- 解压目录下按名称排序取第一个一级子目录作为包根。
- 从包根读取 `version.json`、`releasenote.txt`。
- 从 `updater/` 下按平台精确查找 updater：Windows 为 `updater.exe`，Linux 为 `updater`。
- 从 `bin/` 下查找 maintenance；maintenance 或 updater 任一缺失时整包无效并清理工作区。

`version.json` 当前同时支持两代固件元数据：

- 旧格式只有顶层 `FPGA / MCU / FX3 / EIO`。`PacketSpec` 将它们作为无条件目标继续读取。
- 新格式使用 `SchemaVersion: 2` 和 `Firmware` 节点。顶层字段继续保留，既供旧客户端读取，也作为设备未连接或选件查询不可用时的明确默认值。
- `Firmware.SelectorOptions` 最多声明两个真正影响固件选择的 H2 选件号；当前只启用 `OPTION_BW_320M_TX = 51`，解析器已经可以在将来接收选件 `73` 及 `{51, 73}` 组合。
- 每个 `Firmware.Profiles[]` 使用 `PresentSelectorOptions` 声明一个精确选件组合，并携带一整组 `FPGA / MCU / BUS / EIO` 目标版本。运行时先把设备的完整选件集合与 `SelectorOptions` 求交集，再做精确匹配；不按 `Model` 选择，也不使用优先级或模糊规则。
- 新格式只要存在就必须完整有效；schema 错误、重复组合或组件版本缺失会使整包解析失败，不能静默退回旧字段。

当前包的默认 FPGA 是 `2.0.19`；选件已知时，无 51 档显示 `2.0.14`，含 51 档显示 `2.0.19`。

在线和本地文件都使用这套 `PacketSpec` 解压/解析契约；远端加载只有
`UpdateDialog` 的 `Download` 点击入口。当前远端下载地址仍由代码和 CMake 宏拼接，不再读取
`Settings.ini [Update]/online`。

### 临时工作区生命周期

当前 transport 不支持断点续传，因此只保留当前会话或真实更新交接仍需要复用的完整有效包：

- 下载失败、解压失败、包结构错误、版本文件解析失败或找不到 updater 时，`PacketSpec` 在工作线程返回前删除整个 UUID 工作区。
- 软件版本关系不再触发清理；高、相同或低版本的有效包都可用于用户明确发起的更新。
- 关闭更新对话框不会清理有效包。关闭更新对话框时若仍在下载，下载会继续在后台完成。
- 同一进程内反复打开更新窗口复用同一个 `PacketSpec`；没有 handoff 的普通应用退出会删除该会话工作区。
- 如果退出应用时下载仍在进行，清理请求会通过 libcurl 进度回调中止下载；worker 退出后删除压缩包、解压内容和 UUID 目录。
- `PacketSpec` 析构时仍会兜底等待 worker，并删除尚未交接的工作区；此前删除失败时保留路径与 owner 状态，析构阶段对同一路径再重试一次。
- 主程序启动 maintenance 前先原子写入 `.sgstudio-handoff-cache`。标记写入失败时更新不启动，也不会进入可复用/可交接状态。
- maintenance 启动失败或 ready marker 未按时出现时，主程序删除 handoff-cache 标记并取消交接状态，但不会删除当前会话内已经完成的有效包。
- maintenance 写入 ready marker 后，主程序关闭窗口并释放 `PacketSpec` 锁。
- updater/copy 正常完成后，maintenance 按原有流程启动新 SGStudio。新进程读取标记、重新校验解压目录并接管原 `PacketSpec`，不再下载远端大包。
- 接管成功后移除 handoff 标记；包在新进程本次会话内可以反复打开更新窗口复用，普通退出时再由 `PacketSpec` 析构清理。如果再次执行更新，则会重新建立 handoff 标记。
- 接管函数只接收标记内容与当前编译目标 URL 完全一致、且包重新解析有效的一个候选。接管完成后立即执行一次锁感知清扫，删除损坏、不匹配、重复以及其他未接管候选。
- 如果进程崩溃或被强杀，来不及执行对象析构，下一次 SGStudio 启动会通过 `QLockFile` 的失效锁判断清除遗留 UUID 目录。

---

## 3. UpdateDialog 交接流程

`UpdateDialog` 的在线源当前有明确的 owner 边界：

- 插件只持有更新后启动阶段接管的远端包对象：`m_handoffRemoteSpec`。
- 对话框显示用的在线源槽位是：`m_remoteFile`。

当前活跃行为：

1. `UpdateDialog` 构造、显示和来源切换都不调用 `loadUrl()`。
2. 当插件打开对话框时，如果启动阶段接管了 `m_handoffRemoteSpec`，就把它注入
   `UpdateDialog::setRemotePacketSpec()`；注入只访问本地缓存。
3. `switchOnline()` 只切换到 `Latest Online` 并刷新已有信息。
4. 只有用户点击右侧 `Download` 时才开始远端加载；失败后 worker 完全结束即可重试。
5. 下载成功后，进度区短暂显示完成状态再隐藏，目标版本和 release notes 立即显示。
6. 一旦 `m_remoteFile` 有效，`Update` 按钮直接使用这份 `PacketSpec` 的
   `folderPath()`、`updaterFileName()` 和 `maintenancePath()` 进入 maintenance handoff。
7. 关闭对话框不会丢弃或取消在线包；再次打开时继续使用同一个 `PacketSpec`。

`UpdateDialog` 顶部 `Current` 列的来源也要区分清楚：

- `Version` / `SGStudio` 来自当前安装包编译信息。
- `FPGA` / `MCU` / `BUS` / `EIO` 来自 `currentDevice->getDeviceInfo()` 里的设备开机快照。
- 如果设备本身没有 EIO 选件，底层 `DeviceInfo.EIOVersion` 可能就是 `0`，此时 `UpdateDialog` 显示 `0.0.0` 属于预期行为，不应按 updater UI bug 处理。

`Target` 固件列不是当前设备快照，而是 `PacketSpec` 对当前设备选件快照解析出的包目标：

- `FancyDevice` 复用设备 open 阶段已有的 `device_query_options()`，把“查询是否成功、选件命名空间、完整选件号集合”放进 Core capability snapshot。
- `UpdateDialog` 订阅 `currentDeviceCapabilitiesChanged`；设备切换、重新 open 或包来源变化时会重新解析目标。
- 当前设备选件已知时按新 Profile 精确匹配；型号 122/132 不参与判断。
- 没有当前设备或选件查询失败时，使用顶层旧字段作为包默认目标，因此当前 FPGA 显示 `2.0.19`。
- 选件已知但新包没有精确 Profile 时，固件目标显示 `-` 并禁用 Update，避免元数据缺失时猜测。
- Profile ID 作为目标固件字段的 tooltip 提供诊断，不新增 maintenance 参数。

`Local Default` 的目标包元数据则走另一条链路：

- 它不读取当前已连接设备的固件版本。
- 它会读取当前运行时安装根目录下的 `version.json` 和 `releasenote.txt`。
- 这两个文件来自 `package-info/` 的配置阶段拷贝；在 `repo-root` 运行布局下，它们会被放到仓库根运行时目录，在 `build-tree` 布局下则会被放到 build 根目录。
- 因此 `Local Default` 的 `Version / Software / FPGA / MCU / BUS / EIO` 目标值，当前以运行时 `version.json` 为准，而不是以 updater 专用 firmware 编译期宏为准。
- 新格式的本地目标同样会根据当前设备选件选择 Profile；无设备时读取顶层默认字段。
- updater 不再需要单独携带 `FPGA_VERSION` / `MCU_VERSION` / `BUS_VERSION` / `EIO_VERSION` 这几条 firmware 编译期宏。
- 如果运行时根目录缺少 `version.json`，`Local Default` 仍可继续作为可更新源存在，但本地 firmware 目标字段只会回退为 `-`，这是运行时元数据缺失而不是设备信息链路问题。

软件版本只用于展示，不参与 `Update` 按钮门控。硬门控仍是包有效、maintenance/updater
存在、当前设备选件对应的固件 Profile 能够解析，以及当前没有重复更新事务。

点击 `Update` 后，`UpdateDialog` 会构造 maintenance 参数。

固定传入：

- updater 可执行文件路径
- `Interface`
- `DeviceNum`
- `IP`
- `Port`
- `TimeOut`
- `--parent-pid`
- `--handoff-ready-file`

当来源不是 `Local Default` 时，额外传入：

- `spec->folderPath()`：解压后的包根目录
- `m_currentFolderPath`：当前安装根

启动 maintenance 前，在线和本地归档源必须先成功保留解压工作区，其中在线源还必须成功
写入 handoff-cache 标记；失败时恢复设备状态并停留在原进程。`Local Default` 不拥有
scratch workspace，必须跳过缓存交接。启动成功后主程序等待 maintenance 写入 ready marker。
若 ready marker 未按时出现，主程序取消适用的 handoff 状态并恢复设备；若 marker 到位，
主程序关闭窗口并走正常退出链。

---

## 4. Maintenance 执行流程

`maintenance/main.cpp` 负责解析命令行参数并创建 `ProgressDialog`。

`ProgressDialog` 的核心流程：

1. 写入 ready marker，通知主程序 maintenance 已接管。
2. 等待父进程退出，当前超时时间为 15 秒。
3. 如果父进程未退出，执行一次强制终止：
   - Windows：`taskkill /PID <pid> /F /T`
   - Linux：`kill(pid, SIGKILL)`
4. 2 秒后启动 updater，并实时展示 stdout/stderr。
5. updater 结束后检查结果：
   - 只有 `NormalExit && exitCode == 0` 才继续。
   - 失败时停留在 maintenance 界面，不复制目录、不自动重启。
6. 如果参数中带有复制源/目标：
   - Windows 先通过 `IShellWindows` 关闭当前路径位于安装根或其子目录内的 Explorer 窗口，释放目录句柄；不会终止桌面 Shell 或无关 Explorer 窗口。
   - 然后进入 `CopyThread`。
7. `Local Default` 不带复制源/目标，因此不会关闭 Explorer 窗口、不会创建 `CopyThread`，
   固件 updater 成功后直接从当前 `bin/` 查找应用。
8. 复制成功或 Local Default 固件更新成功后，带 `--UpdateCompleted` 重启；Windows 只通过 Explorer shell 以非管理员身份启动，失败时停留在错误界面，不回退为 maintenance 的管理员身份；新进程随后接管适用的 handoff 缓存。
9. Core 主窗口识别 `--UpdateCompleted` 后，从 `applicationDirPath()/../releasenote.txt` 读取新安装根内的更新内容并显示 `ReleaseNotesDialog`。该路径不依赖 maintenance 设置的当前工作目录；文件缺失或不可读时仍显示更新完成兜底文案，并在日志中记录绝对路径和读取错误。

强杀兜底成功后不再做额外二次等待。这个分支的语义是“父进程已无法在合理时间内正常退出，maintenance 继续接管后续更新事务”。

### 4.1 Win32 外部文件占用隐患

当前 Windows 关闭逻辑只通过 `IShellWindows` 定向关闭安装根或其子目录中的 Explorer
窗口。它不会检测或关闭 Excel、文本编辑器、IDE 等打开安装目录文件的外部用户程序；
maintenance 具备管理员权限也不代表能够绕过这些程序创建的 Windows 文件共享限制。

这是一个尚未由现场故障证实、当前不主动增加处理代码的隐患。以
`configuration/Language.xlsx` 和 `configuration/Settings.ini` 为例，外部程序持续持有文件
句柄时，可能分别影响下面三个阶段：

1. 安装根重命名为 `_bak`；当前只重试三次，每次间隔 1 秒。
2. 从备份回灌 `Settings.ini` 等运行时文件。
3. 回灌完成后递归删除 `_bak`。

由于设备固件更新当前发生在软件目录重命名之前，这类占用如果在复制阶段才暴露，可能形成
“设备固件已更新、软件包未完整替换”的部分更新状态。

如果后续现场复现该问题，优先方案不是默认强杀外部程序，而是在启动设备固件 updater 之前：

1. 检测并列出占用安装文件的 Excel、编辑器等外部用户程序。
2. 明确要求用户保存并关闭这些程序。
3. 重新检测；占用未释放时停止本次更新，不进入固件更新和软件复制。

不应默认强制终止 Excel 或编辑器。一个进程可能同时承载其他无关且未保存的文档，管理员权限
只能允许结束进程，不能保证第三方程序替用户保存数据。只有未来出现明确产品决策和交互确认时，
才重新评估是否提供显式的强制关闭选项。

---

## 5. CopyThread 复制语义

当前复制阶段不是 manifest 驱动的逐文件安装，而是“备份当前安装根，再把新包整目录复制回同一安装根”。

输入含义：

- `srcPath`：更新包解压后的包根目录。
- `destPath`：当前安装根。

执行步骤：

1. 如果 `destPath` 存在，将其重命名为 `_bak`、`_bak1` ... `_bak9`。
   - Windows 在此之前已定向关闭安装根内部的 Explorer 窗口，并仍保留三次重命名重试。
2. 新包固定复制回原 `destPath`，不再根据包根目录名创建新安装目录。
   - Linux 遇到符号链接时直接重建链接，并保留包内原始链接目标，不跟随链接复制其目标。
3. 从备份目录回灌运行时条目：
   - `configuration/Settings.ini`
   - `QuickWaveFormData`
   - `data`
   - `images`
   - `reports`
   - `bin/**/*.lic`
4. 回灌成功后删除备份目录。

`bin/CalFile` 不能整目录回灌。当前约定是：

- `bin/CalFile/132.ini` 属于产品包文件，应随更新包更新。
- `bin/CalFile/132_..._mod.lic` 属于用户现场授权文件，应在更新后回灌。

因此当前代码递归保留 `bin/**/*.lic`，而不是保留整个 `bin/CalFile`。只要更新前 `.lic` 位于旧安装根的 `bin/` 下，更新后会按同一相对路径放回新安装根。

---

## 6. Linux / 树莓派部署结论

现场树莓派桌面入口通过脚本启动：

- `/home/htra/Desktop/SGStudio.desktop` -> `/software/app.sh`
- `/home/htra/.config/autostart/SGStudio.desktop` -> `/software/start.sh`

脚本最终用绝对路径启动 `/software/SGStudio/bin/SGStudio`。这类方式通常不会影响 updater 对安装根的推导，因为 `applicationDirPath()` 仍应来自真实二进制路径。

现场测试已确认：只要 `.lic` 实际存在于 `/software/SGStudio/bin/CalFile`，通过该 desktop entry / shell 脚本启动也能正常授权。因此脚本未 `cd` 不是本次 `.lic` 丢失的原因，问题收敛到更新复制阶段是否正确保留 `.lic`。

---

## 7. 调试检查点

未来排查“为什么启动时联网”“为什么又下载了”“为什么在线页无内容”时，优先按下面顺序看：

### 7.1 启动阶段

- 正常启动只应出现本地缓存日志：
  - `Updater: reusing package handed off by the previous update`
  - 或 `Updater: no handoff package to reuse; online download remains idle`
- 启动、打开对话框、仅切换到 `Latest Online` 都不应出现 `begin download file:`。
- 如果未点击 `Download` 就出现下载日志，说明仍存在未清理的下载入口。

### 7.2 手动远端包是否成功下载并解析

- `PacketSpec::run()` 当前会打印：
   - `begin download file:`
   - `download ok, file =`
   - `decompress finished`
   - `Version FileName:`
- 上述日志只应在用户点击 `Download` 后出现一次。
- 下载成功后，同一会话关闭并重开 UpdateDialog 不应出现第二条 `begin download file:`。

### 7.3 进度与更新门控

- 下载阶段应显示百分比和传输大小；服务端没有 Content-Length 时显示不确定进度。
- 解压和解析阶段显示 `Preparing update package`。
- 成功后短暂显示 100%，状态行隐藏，release notes 恢复原高度。
- 软件版本高、相同或低都不显示警告，也不影响 Update；包有效性和固件 Profile 解析仍是硬门控。

### 7.4 如何区分会话复用与 handoff 复用

- 会话复用：手动下载完成后关闭并重新打开 UpdateDialog，直接显示目标信息且无新下载。
- handoff 复用：真实更新完成并重启后，启动日志出现
  `PacketSpec: adopted handoff cache without downloading`，打开 UpdateDialog 立即显示目标信息。
- 普通退出后没有 handoff 时，下次启动在线页应回到 Idle，仍不自动下载。

### 7.5 ownership / lifetime 边界

- 手动创建的远端包 owner 是 `UpdateDialog`。
- 更新后接管的远端包 owner 是插件的 `m_handoffRemoteSpec`；对话框的 `m_remoteFile`
  只是引用它。
- 临时工作区日志包括：
  - `PacketSpec: removed scratch workspace`
  - `PacketSpec: removed abandoned scratch workspace`
  - `PacketSpec: keeping locked scratch workspace`
- shutdown 排障时，如果下载没有及时结束，应先确认 libcurl 是否收到取消回调，以及日志中是否出现工作区删除结果。
- maintenance 期间，成功交接的在线包应出现 handoff-cache 接管日志；启动接管完成后，其他带损坏/不匹配标记或无标记的失效锁遗留目录都会在本次唯一清扫中删除。

---

## 8. 当前仍需关注

- 更新包下载链路仍需恢复 TLS 校验、移除明文认证信息，并补充 hash / 签名校验。
- 包验证仍偏宽，`updater.exe` / `updater` 与 maintenance 的可执行性仍需要更严格校验。
- 当前复制仍依赖手写回灌清单，尚未建立 manifest / package ownership。
- Win32 当前只定向关闭 Explorer 窗口，未检测 Excel、编辑器等外部程序对安装文件的占用；在现场复现前保留为已知隐患，复现后按 4.1 节在固件更新前增加“检测、列出、要求用户保存关闭、重新检测”的门控。
- post-update 已收敛为 `--UpdateCompleted -> ReleaseNotesDialog` 唯一活跃路径；未接入且没有 writer 的 `UpdatePostSession` / `PostUpdateDialog` 支撑链路已删除。除此之外，`--UpdateCompleted` 会参与启动连接意图计算：若上次成功传输为 ETH，更新后的新进程会尝试恢复上次 ETH endpoint；失败时清理临时 manual ETH，并允许 USB fallback。该行为只恢复连接，不等价于保留完整的 `Profile.json`。
