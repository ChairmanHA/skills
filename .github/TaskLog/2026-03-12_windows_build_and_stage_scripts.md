# Windows 构建与 Staging 脚本设计

## 目标
- 提供 `build-win.ps1`：只负责初始化 MSVC/Qt 环境并编译 qmake 工程。
- 提供 `stage-win.ps1`：只负责组装一个可直接运行的 Windows 目录，不负责压缩发布包。

## 约束
- 不复用历史 `tools/` 下脚本。
- 脚本放在仓库根目录，也就是当前工作区的上级目录，与 `tools/` 同级。
- 保持当前运行时目录布局：`bin/`、`plugin/`、`configuration/`，并补齐 `fonts/`、`data/`、`bin/translations/`、`bin/CalFile/`。
- `stage-win.ps1` 需要在 `windeployqt` 之外，继续分析 `SGStudio.exe` 与所有插件 DLL 的依赖，把非插件依赖尽量收敛到 `bin/`。
- 默认 staging 输出目录放在仓库根目录的 `stage/` 下，与 `tools/` 同级。

## build-win.ps1 设计
- 自动定位 `VsDevCmd.bat`，优先用 `vswhere`。
- 自动定位 `qmake.exe`，优先使用参数或环境变量 `QTDIR` / `QT_DIR`。
- 使用 out-of-source build 目录，不污染源码目录。
- 优先使用 `jom`，找不到时回退到 `nmake`。

## stage-win.ps1 设计
- 从仓库根目录读取 `bin/`、`plugin/`、`configuration/`、`fonts/`、`data/`。
- 先复制主程序、插件和静态资源，再调用 `windeployqt`。
- 使用 `dumpbin /dependents` 递归分析依赖：
  - Qt 和编译器运行时由 `windeployqt` 补齐；
  - 自研库和第三方运行库从 `bin/`、`lib/`、`plugin/` 中解析并复制；
  - 插件 DLL 本身保持在 `plugin/`，不搬到 `bin/`。
- 对缺失依赖给出显式 warning，便于后续补充规则。

## 输出
- `scripts/build-win.ps1`
- `scripts/stage-win.ps1`

## 默认参数固化
- 将两个脚本的默认 `Config` 固定为 `Release`。
- 将两个脚本的默认 `QtDir` 固定为 `C:/Qt/5.15.9/msvc2022_64`，与本机实际成功构建所使用的 Qt 根目录一致。
- 将两个脚本的默认 `Clean` 固定为开启；若要保留旧目录，可显式传 `-Clean:$false`。
- 说明：脚本参数默认化后，可在 PowerShell 中直接运行脚本而无需再传这三个参数；是否能通过资源管理器双击直接执行，仍受系统 `.ps1` 文件关联与 PowerShell 执行策略影响。