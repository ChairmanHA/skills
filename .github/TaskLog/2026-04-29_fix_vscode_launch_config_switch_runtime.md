# Fix VS Code Launch Config Switching Runtime

## Problem

- VS Code 当前使用 `SGS_RUNTIME_LAYOUT=repo-root`，Debug / Release 都把产物写回仓库根 `bin/`、`plugin/`、`configuration/`。
- 切换 launch 配置几次后，程序会在启动日志里提示 Debug / Release 运行时或插件混用，最终无法稳定拉起。
- 当前 `.vscode/prepare-launch-runtime.ps1` 仍按旧命名模型处理 Debug：
  - Debug 主程序假定为 `SGStudiod.exe`
  - Debug 内部库假定为 `Businessd/Controlsd/ExtensionSystemd/Utilsd`
  - Qt 清理只递归删除根 `plugin/`，但没有清掉 `bin/platforms`、`bin/imageformats`、`bin/iconengines`、`bin/styles` 等 Qt 插件子目录里的对侧配置残留

## Local Hypothesis

- 当前切配置失败的直接根因不在业务插件复制，而在 launch 前准备脚本没有按照“当前同名产物 + 对侧 Qt 插件残留”这一现实模型重建 repo-root 运行时。
- 因为脚本的 Debug 分支仍使用旧的带 `d` 命名假设，它会在以下两处失真：
  - 选择 `windeployqt` 目标时找错可执行文件和内部模块
  - 判断 Qt runtime 是否已匹配时，没有把 `bin/...` 子目录中的对侧配置插件视为脏状态
- 便宜的证伪检查已经完成：
  - `src/app/CMakeLists.txt` 明确把主程序输出名统一为 `SGStudio`
  - 当前 `bin/platforms`、`bin/imageformats`、`bin/iconengines`、`bin/styles` 中同时存在 debug / release Qt 插件 DLL

## Minimal Change Plan

- 仅修改 `.vscode/prepare-launch-runtime.ps1`。
- 把主程序和内部库目标集合改为“优先当前同名产物，兼容旧 d 后缀残留”的探测方式。
- 在 Qt runtime 脏检查中加入“对侧配置 Qt 插件 / 历史根目录插件残留”的检测。
- 在真正需要 refresh 时，递归清理所有 repo-root Qt 插件子目录，再使用 `QtPrefixPath` 指向的 Qt 5.15.9 重新部署当前配置。
- 保持“未切配置且运行时已匹配时跳过 Qt 重新部署”的优化。

## Validation Plan

1. 运行一次 Debug pre-launch 准备并启动可执行文件。
2. 再运行一次 Release pre-launch 准备并启动可执行文件。
3. 再切回 Debug，确认脚本会清掉 Release Qt 插件残留并重新部署 Debug Qt。
4. 重复同一配置一次，确认脚本输出为跳过 Qt redeploy。

## Implemented Fix

- `.vscode/prepare-launch-runtime.ps1`
  - `Get-WindeployqtTargets()` 不再把 Debug 主程序硬编码为 `SGStudiod.exe`，改为优先使用当前统一产物名 `SGStudio.exe`，仅对历史残留保留 `SGStudiod.exe` fallback。
  - 内部 Qt 目标 DLL 的收集改为“当前存在的候选文件优先”，避免把历史 `*d.dll` 残留也加入当前配置的 `windeployqt` 目标集。
  - Qt 清理从“删根 bin 中少量 DLL + legacy bin/plugins”扩展为：递归清理 `bin/platforms`、`bin/imageformats`、`bin/iconengines`、`bin/styles` 等真实 Qt 插件子目录。
  - 新增按配置识别 Qt 工件的逻辑；如果 repo-root `bin/` 中存在与当前配置不匹配的 Qt DLL / Qt 插件，则强制 refresh。
  - `windeployqt` 执行后，再次按当前配置裁掉不匹配的 Qt DLL / Qt 插件，防止工具把 debug / release Qt 插件同时铺回 `bin/`。
  - 新增 repo-root 运行时配置戳记 `.sgstudio-launch-config`，记录上一次由 launch 准备脚本成功落地的配置。
  - 新增“配置敏感同名输出”的清理逻辑：当配置从 Debug 切到 Release 或从 Release 切到 Debug 时，脚本会在构建前删除根 `bin/` 中共享同名的主程序和内部库输出（如 `SGStudio.exe`、`Business.dll`、`Controls.dll`、`ExtensionSystem.dll`、`Utils.dll`、`QXlsx.dll`）。这样 MSBuild 就不会把对侧配置生成的同名产物误判为当前配置的最新输出并跳过重链。

## Updated Root Cause

- 仅修复 Qt runtime/Qt plugin 残留还不够；repo-root 模式下还有第二个独立污染源：
  - `SGStudio.exe`、`Business.dll`、`Controls.dll`、`ExtensionSystem.dll`、`Utils.dll`、`QXlsx.dll` 在 Debug / Release 下文件名相同，且都直接输出到根 `bin/`。
  - 当先跑 Debug 再切 Release 时，如果这些同名输出已经被 Debug 构建生成，Release 构建可能因为增量判断而不重写它们。
  - 结果就是：根 `bin/` 中主程序/内部库仍是 Debug，Qt runtime 已被 release prepare 切成 Release，根 `plugin/` 也已经是 Release 插件，于是真实 VS Code launch 会出现“Debug app/libs + Release Qt + Release plugins”的混装。
- 这也是为什么之前“手工 Start-Process 成功”不能证明 VS Code launch 没问题：真正的问题只有在“Debug launch 后再走 Release launch”时才会暴露出来。

## Validation Result

- 同配置重复执行 Debug prepare：脚本输出 `Qt runtime already matches Debug; skipping redeploy`。
- `Release` prepare 收尾输出正常：脚本输出 `Qt runtime already matches Release; skipping redeploy`，最终 repo-root `bin/Qt5Core.dll`、`Qt5Gui.dll`、`Qt5Widgets.dll`、`Qt5Xml.dll`、`Qt5PrintSupport.dll`、`Qt5Svg.dll` 全部为 `5.15.9.0`。
- `Release` 运行时下 Qt 插件目录只剩 release 集合：`qwindows.dll`、`qwindowsvistastyle.dll`、`qgif.dll`、`qsvg.dll`、`qsvgicon.dll` 等，无 `*d.dll`。
- 在 `Release` 运行时下直接启动 `bin/SGStudio.exe`，`WaitForInputIdle()` 返回 `Ready=True`。
- `Release -> Debug` 回切时脚本实际执行了 Qt refresh，收尾输出 `Qt runtime refreshed for Debug`。
- 回切后 repo-root `bin/Qt5Cored.dll`、`Qt5Guid.dll`、`Qt5Widgetsd.dll`、`Qt5Xmld.dll`、`Qt5PrintSupportd.dll`、`Qt5Svgd.dll` 全部为 `5.15.9.0`。
- 回切后 Qt 插件目录只剩 debug 集合：`qwindowsd.dll`、`qwindowsvistastyled.dll`、`qgifd.dll`、`qsvgd.dll`、`qsvgicond.dll` 等，无 release 同名文件。
- 在 `Debug` 运行时下直接启动 `bin/SGStudio.exe`，`WaitForInputIdle()` 返回 `Ready=True`。
- 最新 `bin/debug.log` 中未再检出 `Cannot mix debug and release libraries` / `mix debug and release` / `Cannot load library` 相关旧错误。

## Actual VS Code Launch Verification

- 用户清空根 `bin/` 后，直接通过 VS Code `workbench.action.debug.start` 真实触发当前 launch，确认 preLaunchTask 会重建 repo-root `bin/` 并把 `SGStudio.exe` 真正拉起。
- 真实 `Release` launch 已成功：
  - 根 `bin/` 中 Qt DLL 为纯 Release 集合。
  - `bin/debug.log` 中插件 `Load/Initialize/Start` 全部成功。
- 为了在当前 VS Code 会话里强制复现 `Debug -> Release` 真实 launch，我临时把当前活动 launch 映射到 `Prepare Debug Launch Runtime`，随后再恢复正式配置。
  - 临时映射后的真实 `Debug` launch 已成功：根 `bin/` 中 Qt DLL 为纯 Debug 集合，`.sgstudio-launch-config=Debug`，进程被实际拉起。
  - 恢复正式 launch 后再次真实触发 `Release` launch，脚本依据 `.sgstudio-launch-config=Debug` 先清理根 `bin/` 里同名的 Debug app/libs，再构建 Release。
  - 最终根 `bin/` 中 `SGStudio.exe`、`Business.dll`、`Controls.dll`、`ExtensionSystem.dll`、`Utils.dll` 时间戳全部刷新到 Release launch 时刻，Qt DLL 为纯 Release 集合，`bin/debug.log` 中插件加载全部成功，不再出现“不能混合使用库的调试版本和发布版本”。