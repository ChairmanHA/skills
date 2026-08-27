# 2026-04-28 Russian Stage Packaging And Language Plan

## 目标

- 俄文版 stage 配置显式传入 `language=ru`。
- 主 stage 包补齐根目录 `releasenote.txt`、`version.json`、`bin/maintenance.exe`、根级 `updater/`。
- 俄文版包内 `configuration/Language.xlsx` 使用俄文专用覆盖内容。
- 语言配置支持更可读的 ASCII key，兼容旧 ini 中的显示名写法。

## 局部假设

- 当前俄文 stage 脚本只设置了 `SGS_BRANDING_PROFILE=russian`，没有设置 `language=ru`，因此 updater 相关宏仍按默认 `cn` 编译。
- 当前主 stage 只安装了应用、插件、配置和字体，没有把根级发布元数据、维护程序和 updater 目录纳入默认安装。
- 当前 ini 可读性差的直接原因是 UI 把俄文显示名原样写入 `QSettings`，Qt 在 ini 中会把非 ASCII 文本编码为 `\xXXXX`。

## 最小修改面

1. `.vscode/cmake-stage-release-russian.ps1` 和 `.vscode/tasks.json`
2. `src/CMakeLists.txt`
3. `src/libs/utils/translator.h/.cpp`
4. `src/app/main.cpp`
5. `src/tools/arbeditor/main.cpp`
6. `src/plugins/core/mainwindowsettingscontroller.cpp`
7. `branding/russian/configuration/Language.xlsx`

## 便宜校验

- 先做完脚本与安装规则修改后，执行一次俄文 stage task，检查 stage 目录中是否出现目标文件和 updater 目录。
- 再检查俄文 stage 包中的 `configuration/Settings.ini` 是否可直接使用 `Language=ru`，并确认程序仍能解析旧值。