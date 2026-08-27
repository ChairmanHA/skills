# 2026-04-28 Fix Russian Language Xlsx And Exe Name

## 目标

- 修复俄文包内 `Language.xlsx`，确保 Excel 可正常打开。
- 修复 `Settings.ini` 中 `Language=ru` 启动仍显示英文的问题。
- 打包时把俄文主程序改名为 `СПО ГСРВ.exe`，并保证 stage/runtime/update 查找链不被改坏。

## 已确认根因

- 当前基线 `configuration/Language.xlsx` 可以被 Excel 正常打开。
- 当前坏掉的是 `branding/russian/configuration/Language.xlsx`，之前用手改 OOXML 的方式生成，Excel 无法稳定接受该结果。
- 俄文包启动仍是英文，是因为 stage 覆盖后的 `configuration/Language.xlsx` 就是这份坏文件，翻译加载链没有拿到稳定的俄文列。

## 最小修改面

1. 用 Excel COM 从基线表重新生成 `branding/russian/configuration/Language.xlsx`
2. `src/libs/utils/translator.cpp`：把语言码推断从“精确匹配”放宽到“稳定识别俄文/英文/中文标签”
3. `src/CMakeLists.txt`：stage 中对俄文主程序执行重命名，并让 runtime 依赖扫描使用新文件名
4. `src/app/maintenance/progressdialog.cpp`：补强更新完成后的主程序查找模式，显式支持 `СПО ГСРВ.exe`

## 验证

- 用 Excel COM 打开基线表和俄文覆盖表，确认两者都能正常打开。
- 重新执行俄文 stage task，检查包内 `configuration/Language.xlsx`、`configuration/Settings.ini`、`bin/СПО ГСРВ.exe`。
- 必要时再读 build cache / stage 输出，确认 runtime 依赖扫描和 updater 语言宏仍然正确。

## 2026-04-28 追加排查

- 用户反馈：俄文包启动后实际仍是中文，虽然 `Settings.ini` 已是 `Language=ru`。
- 当前局部假设：启动时实际没有把 stage 包里的 `Language.xlsx` 成功加载成可用翻译表，或者 `setCurrentLanguage("ru")` 最终没有落到俄文语言名。
- 最便宜判别：在 `src/app/main.cpp` 增加一次启动日志，打印 `Settings.ini` 路径、语言原始值、`Language.xlsx` 路径、`loadFile()` 结果、可用语言列表、最终 `currentLanguage()`；然后重新打包并启动俄文 stage 包读取日志。
- 根据这次日志结果决定根修复：若 `loadFile()` 失败，修文件加载链；若加载成功但当前语言错误，修语言映射或启动选择逻辑。

## 2026-04-28 最终取舍

- 用户决定不再在仓库中保留 `branding/russian/configuration/Language.xlsx` 这份派生文件。
- 原因：项目需要跨平台；基于 Excel COM 的生成方式只适合 Win32 + 已安装 Excel 的环境，不适合作为跨平台仓库内构建前提。
- 最终方案：俄文打包继续沿用通用 `configuration/Language.xlsx`；如需俄文包专用的列裁剪与 header 调整，由用户在打包完成后手工修改最终包内的 `configuration/Language.xlsx`。
- 代码动作：从仓库移除 `branding/russian/configuration/Language.xlsx`，其余 stage / translator / package layout 逻辑保持不变。