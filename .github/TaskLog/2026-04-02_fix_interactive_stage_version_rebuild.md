# Interactive Stage 自定义版本重编译修复

## 问题
- `CMake Stage Release Interactive` 当前先依赖普通 `CMake Build Release`，再在脚本里执行带 `SGS_PROJECT_VERSION` 的 configure。
- 这样第二次输入新版本时，真正的 Release 二进制仍可能沿用上一次 build tree cache 中的版本宏，`DeviceInfoWidget` 里的 `SGS_VERSION` 不会更新。
- `deviceinfowidget.cpp` 的 `ui->guiVersion->setText(SGS_VERSION);` 说明 GUI 版本来自编译期宏，而不是 stage 阶段的目录名或运行时配置。

## 根因
- interactive stage 的版本化 configure 发生在构建之后。
- `cmake --build ... --target sgstudio_stage` 只负责 install/stage，不保证在 cache 变量变化后重新编译全部二进制。
- 因此 stage 包目录名可以变，新版本宏却可能仍停留在旧构建产物里。

## 方案
- 修改 `.vscode/cmake-stage-release.ps1`：带版本 configure 后，先执行 `ALL_BUILD`，再执行 `sgstudio_stage`。
- 修改 `.vscode/cmake-stage-release-neutralized.ps1`：保持同样顺序，避免 neutralized interactive 任务重复踩坑。
- 修改 `.vscode/tasks.json`：移除两个 interactive stage 任务对普通 build 任务的 `dependsOn`，避免先用旧版本 cache 编译一次再重配。

## 预期结果
- 每次 interactive stage 输入新的 version 后，实际参与打包的 Release 二进制都会按该 version 重新编译。
- `DeviceInfoWidget` 显示的 `guiVersion` 与本次输入的 version 一致。
- 非 interactive 的 `CMake Stage Release` / `CMake Stage Release Neutralized` 行为保持不变。