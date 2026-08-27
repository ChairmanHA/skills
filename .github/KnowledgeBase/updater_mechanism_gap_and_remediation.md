# Updater 当前状态与整改重点

本文是 updater / maintenance 的当前状态备忘。详细流程见 [updater_firmware_update_mechanism.md](updater_firmware_update_mechanism.md)。本文只保留当前事实、已修复项和仍需整改的重点，不再展开早期错误机制。

---

## 1. 当前已经收敛的行为

### 安全交接

当前更新入口已经接入设备断开语义：

1. `UpdateDialog` 进入 `firmwareUpdateDisconnectMode`。
2. 如果有当前设备，先把 `currentDevice` 置空。
3. 等待 `currentDeviceDisconnectFinished()`。
4. 在线或本地归档源先保留解压工作区，其中在线源原子写入 handoff-cache 标记；失败则
   恢复设备并留在主程序。Local Default 没有 scratch workspace，跳过缓存交接。
5. 启动 maintenance 并等待 handoff ready marker。
6. ready marker 到位后，主程序请求正常关闭窗口。

这意味着正常路径不再是“启动 maintenance 后立即强杀主程序”。

### Maintenance 接管

maintenance 会先等待父进程正常退出。若超过当前超时时间仍未退出，会执行一次强制终止兜底：

- Windows：`taskkill /PID <pid> /F /T`
- Linux：`kill(pid, SIGKILL)`

强制终止成功后，maintenance 直接继续启动 updater，不再做额外二次等待。

### Updater 结果门控

maintenance 只在 updater `NormalExit && exitCode == 0` 时继续后续动作。

如果 updater 启动失败、崩溃或返回非零退出码：

- 不复制安装目录。
- 不自动重启应用。
- 停留在 maintenance 错误界面。

### 安装根稳定

`CopyThread` 当前固定复制回原 `destPath`，不再因为包根目录名创建新的安装根。这避免 desktop entry、快捷方式、部署脚本中的绝对路径在更新后失效。

Windows 整目录备份前，maintenance 会通过 Shell COM 只关闭当前路径位于安装根或其子目录内的 Explorer 窗口，避免这些窗口的目录句柄阻止 `destPath -> _bak`。它不会终止桌面 Shell 或关闭其他位置的 Explorer 窗口，后续仍使用存活的 Explorer 以非管理员身份重启 SGStudio。

### 授权文件保留

当前复制阶段不再整目录回灌 `bin/CalFile`，而是：

- 保留新包中的 `bin/CalFile/132.ini`。
- 从旧安装根递归回灌 `bin/**/*.lic`。

因此 `132_..._mod.lic` 这类现场授权文件会被放回 `bin/CalFile`，而旧版 `132.ini` 不会覆盖新包版本。

### 纯手动在线下载与无二次下载

当前活跃 updater 不再用完整软件包做启动版本检查：

- 启动、打开 UpdateDialog、切换到 `Latest Online` 都不发起网络请求。
- 只有用户点击在线行右侧的 `Download` 才下载、解压和解析远端包。
- 下载进度区位于 release notes 下方；显示时 release notes 让出高度，成功后进度区隐藏并恢复高度。
- 有效包在同一会话内复用；真实更新完成后通过 handoff 跨重启再复用一个会话。
- 软件版本高、相同或低都不产生警告，也不参与 `Update` 门控。

### 下载与解压临时数据清理

当前 `PacketSpec` 按“失败数据清理、有效数据复用”管理下载和解压内容：

- 工作区位于系统临时目录的 `SGStudio/updater/<uuid>` 下，并用 `QLockFile` 标记活跃 owner。
- 下载、解压、解析或 updater 查找失败时立即删除整个工作区。
- 远端软件版本关系不触发清理；手动下载得到的有效包都允许用户决定是否更新。
- 关闭更新对话框不会删除有效工作区；关闭更新窗口时下载可继续在后台完成。
- 退出整个应用时若仍在下载，会请求 libcurl 中止，不保留部分包用于续传。
- 主程序启动 maintenance 前先原子写入 handoff-cache 标记；标记写入失败时不启动更新，也不会把该包声明为可跨进程复用。
- maintenance 启动或 ready 确认失败时取消 handoff 状态，但保留当前会话内已经完成的有效包。
- updater/copy 完成后，maintenance 按原有流程重启 SGStudio；新进程重新验证并接管该缓存，在本次会话再次打开更新窗口不会重复下载。
- 新进程先尝试接管一个 URL 与包内容都匹配的 handoff，再执行一次启动清扫；损坏、不匹配、重复或其他未接管且未锁定的工作区会被删除，不再做延迟或每分钟清扫。
- 普通工作区删除失败时保留 owner 状态，`PacketSpec` 析构时再重试一次；崩溃或强杀留下的失效锁工作区在下次启动清扫中删除。

清扫只处理 updater 生成的 UUID 目录，并且删除前必须成功取得对应锁；不会再按创建时间递归删除整个 `AppLocalDataLocation` 一级目录。

---

## 2. 当前活跃机制的边界

当前机制仍是“整包复制 + 回灌运行时条目”，不是 manifest 驱动的原地安装。

当前在线包路径还有这些边界：

- 远端 URL 仍由 `PacketSpec::loadUrl()` 内部按编译期宏拼接。
- `Settings.ini [Update]/online` 已移除；旧安装遗留键被忽略。
- 手动下载与更新后重启接管共享同一 `PacketSpec` 下载/解压契约；有效包在同一会话内复用，handoff 包在新进程中再复用一个会话。
- `Local Default` 的目标版本元数据现在依赖运行时根目录 `version.json`；因此这条路径对 `package-info/` 元数据拷贝是否进入当前 runtime root 是敏感的。
- updater 已不再依赖单独的 firmware 编译期宏去填充 `Local Default` 的 `FPGA / MCU / BUS / EIO` 目标值；缺失时应先排查 runtime-root `version.json` 是否存在、内容是否正确。

当前保留条目写在 `CopyThread` 中：

- `configuration/Settings.ini`
- `QuickWaveFormData`
- `data`
- `images`
- `reports`
- `bin/**/*.lic`

这套规则足以覆盖当前已知现场问题，但还没有形成完整的 package ownership 模型。新增用户数据、授权文件、校准文件或运行时目录时，仍必须同步检查回灌清单。

---

## 3. 仍需优先整改的问题

### 3.1 更新下载安全

当前下载链路仍存在高风险：

- 客户端代码中存在认证信息。
- TLS peer / host 校验曾被显式关闭。
- 更新包缺少 hash / 签名校验闭环。

这是当前 updater 中优先级最高的剩余问题。

### 3.2 包验证不够严格

`PacketSpec` 对包结构和可执行文件的判断仍偏宽：

- updater 已改为按平台精确匹配小写文件名：Windows 为 `updater.exe`，Linux 为 `updater`。
- updater 和 maintenance 路径及可执行性需要更严格校验。
- zip 根目录结构错误时诊断不够清晰。

应把“包能否被完整执行”作为 `valid()` 的判断条件，而不只是“找到了某个 updater 文件”。

### 3.3 安装模型仍需 manifest 化

当前复制模式虽然已经能保持安装根稳定，但仍不能回答这些问题：

- 哪些是产品文件。
- 哪些是用户现场文件。
- 哪些文件应覆盖。
- 哪些文件应保留。
- 哪些旧文件应删除。

长期方向仍应是 manifest / ownership 驱动的 install phase，而不是继续扩展手写 allowlist。

### 3.4 Post-update 信息仍可扩展

当前已有一条基础完成路径：maintenance 带 `--UpdateCompleted` 重启，Core 主窗口从安装根读取 `releasenote.txt` 并显示 `ReleaseNotesDialog`。读取路径已锚定 `applicationDirPath()`，不会再受 maintenance 在 Linux 上把 cwd 切到用户主目录的影响；release note 不可读时仍显示更新完成兜底文案。

未接入的 `UpdatePostSession` / `PostUpdateDialog` 及其 session reader 已删除，当前不再保留没有 writer 的第二条完成提示路径。目标版本摘要仍未实现；设备重连已经接入现有 `--UpdateCompleted` 启动路径：若 `Settings.ini` 记录上次成功 ETH，更新后会尝试恢复该 endpoint，失败则清理临时 manual ETH 并允许 USB fallback。该行为不表示更新过程会保留完整的 `Profile.json`。

### 3.5 在线更新可观测性仍偏弱

当前已有手动下载开始、下载完成、解压完成、失败原因、工作区清理和 handoff 接管日志。剩余主要
调试短板是缺少服务端发布 manifest/hash，客户端无法把稳定 URL 对应到一个可验证的发布版本。

---

## 4. 建议验收项

后续验证 updater 时，建议至少覆盖：

- 启动、打开 UpdateDialog、切到 `Latest Online` 都不产生更新网络请求。
- 在线来源右侧显示 Download；只有点击它才产生一次完整包 GET。
- 下载时 release notes 高度缩短；解析成功后进度状态短暂显示 100% 并隐藏，release notes 恢复高度。
- 远端 `Software` 低于、等于或高于本地 `SGS_VERSION` 时，只要包和 Profile 有效，Update 均可用且不显示版本关系警告。
- 关闭更新窗口后再打开，仍使用同一份有效包或显示当前下载进度。
- 下载失败后，临时 UUID 目录和部分压缩包均被删除。
- 下载过程中关闭更新对话框时下载继续；退出整个应用时下载被中止，worker 退出后删除工作区。
- handoff 标记写入失败时更新不启动，当前包仍可在本会话重试。
- maintenance 完成后重启 SGStudio，后者接管 handoff 包且不触发重复下载；再次打开更新窗口仍可使用。
- 损坏、URL 不匹配或重复的 handoff 工作区在接管之后的唯一一次启动清扫中删除。
- 接管后的 SGStudio 普通退出时删除该会话的工作区；如果再次更新则重新交接。
- 模拟一次 owner 删除失败后，析构阶段会对同一路径再尝试一次。
- 模拟崩溃遗留目录时，下次启动能删除失效锁工作区。

- 正常更新：设备断开完成后进入 maintenance。
- 主程序正常退出：maintenance 不触发强杀。
- 主程序退出超时：maintenance 执行一次强杀后继续 updater。
- updater 返回非零：不复制、不重启。
- 本地 zip 更新：安装根保持不变。
- Linux 包中的文件或目录符号链接在复制后仍为符号链接，且保存原始相对/绝对目标文本。
- Windows 更新期间不终止 Explorer 进程；只关闭正在浏览安装根或其子目录的窗口。更新后只通过存活 Explorer 的非提权 shell 重启，失败时 maintenance 留在错误界面且不启动管理员身份的 SGStudio。
- `bin/CalFile/132.ini` 来自新包。
- `bin/CalFile/*.lic` 来自旧安装根并成功回灌。
- desktop entry / 自启动脚本仍指向同一安装路径。
