# SAStudioEx 更新全流程迁移说明

> 工作区绝对路径：
> - 仓库根目录：D:\development\SAStudioEx
> - 源码工作区：D:\development\SAStudioEx\src
> - 工具工作区：D:\development\SAStudioEx\tools
> - 下文中的源码相对链接默认对应源码工作区 D:\development\SAStudioEx\src

> 目的：基于当前 SAStudioEx 仓库代码、当前根目录元数据约定，以及确认后的 SGStudio 目标包结构，整理“启动、静默准备、是否需要更新、固件更新、maintenance 替换软件文件、重启收尾”的完整流程，供移植到另一个项目时复用。

## 1. 先说结论

当前 SAStudioEx 的更新链路，本质上分成两段：

1. 主程序中的 Updater 插件负责准备更新信息、准备更新包、拉起 maintenance 进程，然后退出自己。
2. maintenance 进程负责运行外部固件更新器，必要时再把整包软件复制到当前安装目录，最后重启主程序。

对迁移最关键的结论有四个：

- 当前仓库代码里确实存在“启动后静默准备远程更新包”的行为，入口在 [plugins/updater/plugin.cpp](../../plugins/updater/plugin.cpp) 和 [plugins/updater/updatedialog.cpp](../../plugins/updater/updatedialog.cpp)。
- 当前仓库里“是否需要固件更新”的运行时判定，主要来自设备打开返回 `-49`，入口在 [plugins/device/devicenode.cpp](../../plugins/device/devicenode.cpp) 和 [plugins/device/utils.cpp](../../plugins/device/utils.cpp)。
- 真正执行固件更新和软件覆盖的不是主程序，而是 [maintenance/progressdialog.cpp](../../maintenance/progressdialog.cpp) 和 [maintenance/copythread.cpp](../../maintenance/copythread.cpp)。
- 当前 [plugins/updater/packetspec.cpp](../../plugins/updater/packetspec.cpp) 与本次确认的 SGStudio 目标包结构并不完全一致。你可以保留它“下载整包并解压”的骨架，但元数据和路径解析逻辑不能直接照搬。

## 2. 目标更新包结构

按本次迁移约定，SGStdio 的目标包结构如下。当前生成的 SGStudio.zip 骨架包也会严格匹配这个结构：

```text
SGStudio/
  version.json
  releasenote.txt
  bin/
    maintenance_placeholder.keep
    CalFile/
  configuration/
  data/
  images/
  plugin/
  reports/
  updater/
    Updater_placeholder.keep
    data/
      Firmware_2/
      Firmware_bak/
  tools/
  install.bat
```

这份目标结构里有几个关键约束：

- 元数据固定来自包根目录下的 `version.json` 和 `releasenote.txt`。
- 正式更新包建议包含 `bin/maintenance.exe`（或 Linux 下的 `bin/maintenance`）。当前骨架包仍然只放 `bin/maintenance_placeholder.keep` 占位，后续再替换成真实程序。
- 外部固件更新器的正式放置位置固定为包根目录下的 `updater/Updater_*`。
- 当前骨架包里不放实际 updater 程序，而是用 `updater/Updater_placeholder.keep` 作为占位，后续替换成真正的 `Updater_*.exe` 或对应平台可执行文件即可。
- 软件安装或整包覆盖入口固定为包根目录下的 `install.bat`。
- 当前骨架包不包含 DLL、主程序、插件程序和真实 maintenance / updater 可执行文件；这些内容后续再填充到既定目录。
- 对 SG 产品而言，`tools/` 目录可以为空；当前包契约保留它，只是为了给后续可选工具预留位置。
- 包根目录仍然是后续 maintenance 做整包替换时的复制源。

这说明如果你在新项目里保持这套包结构不变，最稳妥的做法是：

- 整包下载并解压后，直接解析 `version.json` 和 `releasenote.txt`。
- 由包内 `bin/maintenance.exe` 接管更新编排。
- 外部固件更新器路径按包根目录下的 `updater/Updater_*` 规则解析。
- 软件安装脚本固定读取包根目录下的 `install.bat`。
- 整包更新路径继续以包根目录为复制源，而不是靠文件名模糊匹配。

### 2.1 按当前代码看，还差什么

如果只看“目录位置是否能被当前代码找到”，目前 SGStudio 结构和现代码是部分对齐，不是完全对齐。

已经对齐的部分：

- 压缩包只有一个顶层根目录，这一点符合 [plugins/updater/packetspec.cpp](../../plugins/updater/packetspec.cpp) 的解压假设。
- 包根目录下的 `version.json` 和 `releasenote.txt`，这和 [plugins/updater/packetspec.cpp](../../plugins/updater/packetspec.cpp) 当前读取方式一致。
- 外部固件更新器放在包根目录下的 `updater/Updater_*`，这和 [plugins/updater/packetspec.cpp](../../plugins/updater/packetspec.cpp) 的 `findUpdater()` 约定一致。
- `bin/CalFile`、`configuration`、`data`、`images`、`reports` 这些目录仍然保留，和 [maintenance/copythread.cpp](../../maintenance/copythread.cpp) 的保留目录思路一致。

仍然不一致的部分：
- 正式包应该带 `bin/maintenance.exe`，但当前骨架包里仍然只有 `maintenance_placeholder.keep`，所以它还不能被当前代码直接执行。
- 当前代码仍然要求更新包里带 `bin/maintenance.exe`，否则 [plugins/updater/updatedialog.cpp](../../plugins/updater/updatedialog.cpp) 在在线包或本地包路径上无法拉起 maintenance。
- 当前代码并不会读取或执行包根目录下的 `install.bat`；这个文件现在是你定义的新包契约，不是现代码已经接入的入口。
- 更新完成后主程序显示更新日志时，仍然读取运行目录相对路径 `../releasenote.txt`，见 [plugins/core/mainwindow.cpp](../../plugins/core/mainwindow.cpp)。如果后续改为脚本或 helper 写入其他位置，这里也需要同步调整。
- 当前骨架包里没有实际 `Updater_*.exe` 和 `maintenance.exe`，所以它适合作为结构样板，不适合作为当前代码可直接跑通的更新包。

关于 `tools/` 为空是否正常：
- 对 SG 产品来说，如果本来就不带 SCPI、server 或其他附属工具，那么 `tools/` 为空完全合理。


## 3. 当前 SAStudioEx 的真实启动与静默准备流程

### 3.1 主程序启动后，Updater 插件何时介入

主程序启动后会加载插件系统，入口在 [app/main.cpp](../../app/main.cpp)。Updater 插件在初始化时就创建 `UpdateDialog`，对应实现见 [plugins/updater/plugin.cpp](../../plugins/updater/plugin.cpp)。

这一步有两个直接后果：

- 即使用户还没主动点“更新”，`UpdateDialog` 对象也已经被构造出来。
- 只要配置里存在更新 URL，`UpdateDialog` 构造函数就会立刻让 `m_remoteFile->loadUrl(url)` 开始准备远程更新包，见 [plugins/updater/updatedialog.cpp](../../plugins/updater/updatedialog.cpp)。

也就是说，当前工程里的“静默下载”不是独立后台服务，而是 `UpdateDialog` 构造副作用。

### 3.2 更新 URL 和缓存目录来自哪里

Updater 插件会从 `../configuration/Settings.ini` 的 `Update` 分组中读取 `url` 和 `online` 配置，同时用 `QStandardPaths::AppLocalDataLocation` 作为缓存根目录，见 [plugins/updater/plugin.cpp](../../plugins/updater/plugin.cpp)。

当前实现还会在 `extensionsInitialized()` 阶段清理缓存目录下 7200 秒之前的旧内容，仍然在 [plugins/updater/plugin.cpp](../../plugins/updater/plugin.cpp)。

这套机制可迁移，但建议只保留“缓存目录分离”和“过期清理”两个思想，不保留“启动即大包下载”的行为。

### 3.3 当前 `PacketSpec` 的真实行为

[plugins/updater/packetspec.cpp](../../plugins/updater/packetspec.cpp) 里，`PacketSpec::loadUrl()` 会覆盖传入的 URL，重新拼成固定站点地址；随后 `PacketSpec::run()` 会：

1. 在 AppLocalDataLocation 下创建一个 UUID 子目录。
2. 下载整包 zip 到该目录。
3. 解压整包。
4. 假定解压后只有一个顶层目录，并把这个目录作为包根目录。
5. 继续查找版本文件、maintenance 和 updater。

这一实现有三个对迁移很重要的前提：

- 它要求压缩包必须只有一个顶层根目录。
- 它当前查找的是包根目录下的 `version.json` 和 `releasenote.txt`。
- 它当前查找 updater 的逻辑是“包根目录下 `updater/Updater_*` 模式文件”。

而本次确认的目标包结构是把元数据放到在包根目录下，同时保留 updater 在包根目录下。因此：

- `findUpdater()` 这部分路径约定可以延续。
- `parseVersionFile()`、`parseReleaseNoteFile()` 以及安装脚本定位逻辑都需要改成新包契约。
- `findMaintenance()` 的路径约定可以延续，但正式包必须真正提供 `bin/maintenance.exe`，不能只停留在骨架占位。

## 4. 当前工程里“是否需要更新”的判定，是分两条线的

### 4.1 设备运行时固件不匹配判定

设备打开时，如果底层返回 `APIRETVAL_ERROR_FirmwareVersionMismatch`，也就是 `-49`，当前工程会把它视为“固件不匹配但设备信息仍可读取”的状态，代码位于 [plugins/device/devicenode.cpp](../../plugins/device/devicenode.cpp)。

这条线上当前行为是：

1. `Device_Open` 返回 `-49`。
2. `info.FirmwareUnmatch` 被置为 `true`。
3. 弹出消息框提示 “Firmware version mismatch(Error Code = -49); Please update firmware.”。
4. 如果用户选择继续或设置“不再提示”，主流程仍然允许继续打开设备。
5. 状态栏也会显示 `FirmwareUnMatch` 状态，见 [plugins/deviceexplorer/updatestshelper.h](../../plugins/deviceexplorer/updatestshelper.h)。

也就是说，当前代码的真实行为不是“发现 `-49` 就自动开始升级”，而是“打标、提示、允许用户自己决定”。

### 4.2 套件版本更新判定

UI 层的版本比对意图在 [plugins/updater/updatedialog.cpp](../../plugins/updater/updatedialog.cpp)，`UpdateDialog` 会同时持有：

- 当前安装包信息 `m_nativelFile`
- 远程包信息 `m_remoteFile`
- 用户手选本地包信息 `m_localFile`

但当前 `checkNewSuiteVersion()` 基本是空壳，仍然返回一个近似恒真的结果，没有形成完整“严谨判定是否需要整包更新”的闭环，见同文件。

所以如果你要移植到新项目，建议把“是否需要更新”明确拆成两层：

- 第一层：先下载小元数据文件，例如独立下发的 `version.json` 或更轻量的 `manifest.json`，判断是否存在新包。
- 第二层：整包下载后，再从包根目录下的 `version.json` 读取目标版本，并结合设备实际固件版本或 `-49` 结果，判断是否必须做固件更新。

## 5. 用户点击更新后，主程序如何切换到 maintenance

### 5.1 更新入口

Updater 插件会在系统菜单里注册一个 Update 动作，代码位于 [plugins/updater/plugin.cpp](../../plugins/updater/plugin.cpp)。用户点击后打开 `UpdateDialog`。

### 5.2 `UpdateDialog` 中实际有三种源

在 [plugins/updater/updatedialog.cpp](../../plugins/updater/updatedialog.cpp) 中，更新来源分成三类：

1. 在线包 `m_remoteFile`
2. 当前本机包 `m_nativelFile`
3. 用户手动选择的本地 zip `m_localFile`

这三类源会走出两条不同分支：

- 如果选择的是当前本机包 `m_nativelFile`，只把 updater 路径传给 maintenance，这是一条“固件更新为主”的路径。
- 如果选择的是在线包或本地 zip，则除了 updater 路径，还会把“新包根目录”和“当前安装根目录”一起传给 maintenance，这是一条“固件更新 + 软件替换”的路径。

### 5.3 maintenance 启动参数

`UpdateDialog::executeUpdate()` 的核心行为在 [plugins/updater/updatedialog.cpp](../../plugins/updater/updatedialog.cpp)：

1. 选中某个 `PacketSpec`。
2. 取出其中的 maintenance 路径和 updater 路径。
3. 如果不是当前本机包，再把 `spec->folderPath()` 和 `m_currentFolderPath` 一并作为参数传入。
4. Windows 下通过管理员权限拉起 maintenance。
5. 主程序随后直接强制结束自己。

这里的 `m_currentFolderPath` 是 `applicationDirPath()` 的上一级目录，也就是安装根目录，而不是 `bin/` 目录。

### 5.4 当前实现的一个重要特点

当前主程序并没有在退出前做“优雅下线”：它启动 maintenance 后直接执行强杀，代码在 [plugins/updater/updatedialog.cpp](../../plugins/updater/updatedialog.cpp)。

这意味着它默认依赖两个事实：

- 真正的设备释放工作主要靠主程序进程退出来完成。
- maintenance 启动后已经足够稳定，不需要主程序继续参与协调。

对迁移来说，这种方式能工作，但工程质量不高。更稳妥的做法是：

- 先显式停止所有测量、采集、录制、流式任务。
- 再主动关闭设备连接。
- 最后再进入退出和维护进程接管阶段。

## 6. maintenance 进程中的真实执行链路

### 6.1 进程入口

[maintenance/main.cpp](../../maintenance/main.cpp) 很薄，只负责把参数交给 `ProgressDialog`。因此真正逻辑都在 [maintenance/progressdialog.cpp](../../maintenance/progressdialog.cpp)。

### 6.2 maintenance 先做什么

`ProgressDialog` 构造后会：

1. 读取命令行参数。
2. 如果参数数目至少为 3，就认为后续需要复制软件文件。
3. 创建 `QProcess`，把第一个参数作为外部 updater 可执行文件。
4. 固定传入 `-y` 参数，表示静默或自动确认模式。
5. 等待 updater 退出。

所以 maintenance 的第一职责始终是“跑外部固件更新器”。

### 6.3 当前实现里最值得注意的风险

在 [maintenance/progressdialog.cpp](../../maintenance/progressdialog.cpp) 中，`onUpdaterProcessFinished()` 虽然收到了 `exitCode` 和 `exitStatus`，但当前逻辑没有用它们做任何分支控制。

现在的真实行为是：

- 只要外部 updater 进程结束了，不管成功还是失败，只要 `m_needCopyFile` 为真，就会继续进入 `CopyThread` 做整包软件替换。

这正是当前链路里最应该优先修正的点之一。

### 6.4 `CopyThread` 如何替换整包软件

复制逻辑在 [maintenance/copythread.cpp](../../maintenance/copythread.cpp)。整体步骤是：

1. 把当前安装目录改名为 `_bak`、`_bak1` 之类的备份目录。
2. 以“新包根目录名”为目标目录名，把新包完整复制过去。
3. 从备份目录中回灌一部分必须保留的目录或文件。
4. 删除备份目录。

当前硬编码保留的内容包括：

- `bin/CalFile`
- `configuration/Settings.ini`
- `data`
- `images`
- `reports`

这说明 maintenance 的真实意图不是“无脑覆盖”，而是“整包升级，但保留用户本地配置和业务数据”。

### 6.5 一个很容易忽略的路径假设

`CopyThread` 复制时，是用“新包根目录名”决定最终目标目录名的。也就是说它隐含假设：

- 当前安装根目录名，与新包解压后的根目录名一致。

如果两者不一致，例如当前安装目录叫 `SAStudio4_4.3.55.35`，而新包根目录名叫 `SAStudio4`，那么复制落点就可能变成同级的另一个目录，而不是原位置替换。

所以迁移时应当把“最终安装目标目录”作为显式参数传递，而不是依赖目录名碰巧一致。

### 6.6 建议保留的 maintenance 完整职责

从工程最佳实践看，保留 `maintenance` 是合理的，但建议把它的职责边界写清楚，不要让它既像 UI 程序、又像安装器、又像脚本跳板却没有明确主次。比较稳妥的职责拆分如下：

1. 接管更新会话

- 解析主程序传入的更新参数，至少包括：`packageRoot`、`installRoot`、`appExePath`、`appPid`、`updaterPath`、`scriptPath`、`relaunchArgs`。
- 建立独立日志，记录固件更新、脚本执行、重启结果和失败原因。

2. 结束软件并等待退出

- 主程序在拉起 `maintenance` 之前，应该先显式停止测量、录制、流式任务，并主动断开设备连接。
- 然后主程序发起正常退出，让 Qt 的 `aboutToQuit`、插件 shutdown 和已有的设备关闭路径有机会执行，见 [app/main.cpp](../../app/main.cpp)、[libs/extensionsystem/pluginmanager.cpp](../../libs/extensionsystem/pluginmanager.cpp) 和 [plugins/device/devicenode.cpp](../../plugins/device/devicenode.cpp)。
- `maintenance` 随后根据 `appPid` 等待主程序真正退出；只有超时后才做兜底强杀。当前代码直接 `taskkill` 的做法过于激进，会绕过正常 shutdown 路径。

3. 处理提权

- Windows 下由 `maintenance` 统一处理 UAC 提权，而不是把提权逻辑分散在脚本里。
- 需要管理员权限的固件 updater 和 `install.bat`，都应在同一个权限上下文内执行，避免“前半段提权、后半段掉回普通权限”。

4. 拉起固件更新

- `maintenance` 使用绝对路径启动外部固件 updater，并收集 stdout/stderr 与退出码。
- 固件 updater 失败时，应直接停止软件更新阶段，不得继续覆盖软件文件。
- 最稳妥的做法是在 updater 退出后再做一次固件版本复核，而不是只看退出码。

5. 执行更新脚本

- 只有在固件 updater 成功后，`maintenance` 才执行包根目录下的 `install.bat`。
- `install.bat` 负责产品相关的软件覆盖逻辑、资源迁移、可选清理等步骤；`maintenance` 负责调用、记录和判断结果。
- 脚本不应再自行猜测安装目录，而是由 `maintenance` 通过绝对参数明确传入。

6. 结果检查与回滚

- `maintenance` 至少要检查 `install.bat` 的退出码、目标可执行文件是否存在，以及关键目录是否仍然完整。
- 如果脚本失败，应保留备份和日志，不要自动删除现场。
- 如果采用目录级替换，备份和回滚规则最好由 `maintenance` 控制，而不是完全放在批处理里裸写。

7. 拉起软件

- 更新完成后，`maintenance` 用主程序的绝对路径重启软件，并附带 `--UpdateCompleted`。
- 最好不要再通过模糊文件名搜索主程序，而应直接使用主程序传来的 `appExePath`。

### 6.7 绿色软件目录被用户改名时，更新脚本怎么处理更稳妥

绿色软件最常见的问题不是“包名叫什么”，而是“用户把当前安装目录手工改了名字”。这时最忌讳的做法，就是在脚本或 helper 里写死 `SGStudio` 这样的目录名。

更稳妥的处理方式是：

1. 主程序负责提供真实安装路径

- 主程序启动时已经知道自己的真实运行位置，可以从 `applicationDirPath()` 的上一级得到实际 `installRoot`。
- 这个 `installRoot` 应该作为显式参数传给 `maintenance`，而不是让 `maintenance` 或脚本去猜。

2. `maintenance` 负责把绝对路径传给脚本

- `maintenance` 调用 `install.bat` 时，至少应传入：`packageRoot`、`installRoot`、`appExePath`、`backupRoot`。
- 如果参数较多，最佳实践是由 C++ 先写一个 `update-session.json` 或 `update-session.ini`，脚本只读取这个文件，避免命令行引号和编码问题。

3. 脚本只用 `%~dp0` 识别包目录，不用它识别安装目录

- `install.bat` 可以通过 `%~dp0` 得到“自己位于哪个更新包目录”。
- 但安装目标目录必须以 `maintenance` 传入的 `installRoot` 为准，而不是用脚本目录名、压缩包根目录名或 `SGStudio` 常量去推导。

4. 不要再依赖“新包根目录名 == 当前安装目录名”

- 当前 [maintenance/copythread.cpp](../../maintenance/copythread.cpp) 里的 `makeCopyTargetPath()` 会把新包根目录名带入目标路径，这对绿色软件自定义目录名并不稳妥。
- 更推荐的做法是：`maintenance` 明确知道 `installRoot`，脚本或 C++ 覆盖逻辑直接面向这个绝对路径工作。

5. 关键路径优先放在 C++，把产品差异留给脚本

- 适合放在 C++ 的内容：路径规范化、进程等待、提权、固件 updater 启动、退出码判断、备份回滚、主程序重启。
- 适合放在脚本里的内容：产品特有的资源迁移、目录清理、可选工具分发、定制化注册步骤。

这类职责分工通常比“所有事情都写进 install.bat”更稳，也比“所有事情都写死在 maintenance 里”更容易维护。

## 7. 更新完成后的重启与收尾

当 maintenance 认为流程结束后，会尝试找到主程序可执行文件并重启，逻辑在 [maintenance/progressdialog.cpp](../../maintenance/progressdialog.cpp)。

它重启时会附带 `--UpdateCompleted` 参数。当前主程序由 [plugins/core/mainwindow.cpp](../../plugins/core/mainwindow.cpp) 在扩展初始化完成后识别该参数，并从 `applicationDirPath()/../releasenote.txt` 读取新安装根内的更新内容后显示 `ReleaseNotesDialog`。这里不能使用依赖 cwd 的 `../releasenote.txt`：Linux maintenance 会把 cwd 切到用户主目录，以免 helper 自身停留在待替换目录中。

这意味着当前链路已经具备一个很清晰的“更新完成后首次启动”信号位。这个机制很适合保留到新项目里，用来做：

- 首次启动提示
- 更新结果页
- 版本变化说明
- 更新日志展示

## 8. 按当前目标包结构迁移时，推荐保留的总体流程

如果你准备把这套机制迁移到另一个项目，同时保持“更新包结构大致不变”，建议把流程明确成下面这条链路：

1. 软件启动后，只检查轻量元数据，不直接下载大 zip。
2. 轻量元数据优先读取独立下发的 `version.json` 或一个新增的 `manifest.json`。
3. 如果发现套件版本有新包，先记录“软件可更新”状态，不要立刻进入 maintenance。
4. 设备连接时，如果打开设备返回 `-49` 或版本比对不一致，记录“固件必须更新”状态。
5. 用户确认更新后，显式停止业务、断开设备、冻结 UI。
6. 再下载整包 zip 到缓存目录，并解压到唯一临时目录。
7. 从包根目录下的 `version.json` 和 `releasenote.txt` 中解析软件版本、固件版本、ReleaseNote 等信息，并定位包内的 `bin/maintenance.exe`、包根目录下的 `updater/Updater_*` 和包根目录下的 `install.bat`。
8. 以管理员权限拉起 maintenance，并显式传入：
  - 当前主程序 PID
  - 当前主程序绝对路径
  - 当前安装根目录绝对路径
  - 新包根目录绝对路径
  - 外部 updater 绝对路径
  - 更新脚本绝对路径
  - 可选的会话文件路径或校验参数
9. 主程序先停止业务、断开设备并发起正常退出。
10. maintenance 等待主程序退出；超时后才做兜底强杀。
11. maintenance 完成提权处理后，先运行外部固件 updater。
12. 只有在 updater 成功，并且固件校验也通过时，maintenance 才执行 `install.bat` 完成软件更新。
13. 脚本和 maintenance 共同完成结果检查、必要回滚，并由 maintenance 用 `--UpdateCompleted` 重启主程序；主程序随后显示 release note 并重新建立设备连接做最终确认。

## 9. 对另一个项目的优化建议

下面这些优化建议不改变大方向，也不要求你改包结构，但很值得一并做：

### 9.1 把“小元数据判定”和“大包下载”分离

当前仓库的启动时静默下载，实质上是在启动阶段就把整个 zip 拉下来。这对迁移不建议保留。

建议改成：

- 启动时只请求一个很小的元数据文件。
- 只有用户确认更新，或者已经判定“固件必须升级”时，才去下载完整 zip。

### 9.2 以包根目录下的 `version.json` 和 `releasenote.txt` 为准

既然这次已经把元数据固定到包根目录，迁移时最好的做法是：

- 主流程在整包落地后直接解析包根目录下的 `version.json`。
- 发布说明直接读取包根目录下的 `releasenote.txt`。
- 如需兼容旧包，可以在解析层做兼容，但不要让文档长期维护多套主契约。

### 9.3 固定 `bin/maintenance.exe`、根目录 updater 和 install.bat 的协作边界

当前迁移目标已经把两个关键入口固定下来：

- `bin/maintenance.exe` 负责更新编排
- 外部固件更新器位于包根目录下的 `updater/Updater_*`
- 软件安装入口位于包根目录下的 `install.bat`

这样职责更清晰：`maintenance` 负责等待、提权、编排和重启；`install.bat` 负责产品相关的软件更新动作；外部 updater 继续只负责固件烧录。

### 9.4 以 updater 成功和固件复核通过，作为软件替换前置条件

当前 maintenance 只要等到 updater 进程退出，就继续复制软件。这会导致“固件失败但软件已覆盖”的半更新状态。

迁移时应至少满足下面任一组合：

- `exitCode == 0` 且外部 updater 返回成功
- 更新后重新连接设备并读回版本，确认达到目标版本

只有满足条件后，才允许覆盖 bin、plugin、资源和 DLL。

### 9.5 安装目标目录要显式传递

不要依赖“新包根目录名 == 当前安装目录名”。

这类路径约定在迁移后最容易失效，建议 maintenance 直接接收明确的 `installRoot` 参数，并按这个绝对路径做备份、脚本调用和回滚。

### 9.6 保留目录名单要项目化配置

当前保留名单写死在 [maintenance/copythread.cpp](../../maintenance/copythread.cpp)。移植到另一项目时，建议改为：

- 项目配置表
- 或 `version.json` / `manifest.json` / 项目配置中的保留规则

这样更容易适应不同产品线的数据目录差异。

### 9.7 去掉硬编码凭证和固定站点拼装

当前代码里存在固定站点地址和硬编码认证信息。这类实现不建议带到新项目。

建议只保留：

- 服务器地址来自配置或后端响应
- 凭证来自受控配置或 token 机制
- 所有下载都带 hash 或签名校验

## 10. 迁移时最值得直接复用的部分

下面这些思想是值得保留的：

- 固件更新与软件替换分阶段执行。
- 主程序负责决策与切换，maintenance 负责占用释放后的更新编排。
- 更新包同时携带 `maintenance`、外部固件 updater 和 `install.bat`，由 `maintenance` 串起整个流程。
- 升级后通过 `--UpdateCompleted` 让主程序进入“更新后首次启动”路径。
- 保留用户配置、校准数据和业务目录，而不是全量覆盖。

## 11. 一句话归纳给迁移项目

如果你保持当前这版 SGStudio 包结构不变，那么新项目最应该照搬的是“主程序判定 + maintenance 接管 + 外部 updater 黑盒执行 + `install.bat` 完成软件更新 + maintenance 重启收尾”这条总路线；最不应该照搬的是“启动即下载大包、主程序直接强杀自己、忽略 updater 失败继续覆盖软件、脚本靠目录名猜安装路径”这几处旧实现。
