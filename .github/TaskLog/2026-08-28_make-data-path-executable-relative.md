# 将默认 data 路径固定为可执行文件相对路径

## Scope

- 调整 `Utils::mkdirs()` 对相对路径的解析基准。
- 相对路径以 `QCoreApplication::applicationDirPath()` 为基准；无应用实例时保留 cwd 回退。
- 绝对路径保持原有含义。
- 不修改保存配置、保存波形等调用方；它们现有的 `../data/` 参数自动解析为 `<可执行文件目录>/../data`。

## Assumption and evidence

- 目标语义：若可执行文件为 `/software/SGStudio/bin/SGStudio`，则 `../data/` 应解析为 `/software/SGStudio/data`。
- 当前保存配置与所有 Save IQ Data 调用均使用 `Utils::mkdirs("../data/")`。
- 仓库内 `Utils::mkdirs()` 的其他有效调用传入的是已经基于可执行文件解析的绝对路径，因此统一相对路径基准不会改变该调用。

## Success criteria

- `Utils::mkdirs("../data/")` 不再依赖进程 cwd。
- 直接启动、launcher 启动和应用内部重启得到相同的默认 data 目录。
- 保存配置和保存波形无需分别修改调用代码。
- 目录仍会按需创建，返回值为清理后的目标路径。

## Verification

- Verification level: `static`
- 检查 `Utils` CMake 纳入关系与所有 `Utils::mkdirs()` 调用参数。
- 检查相对路径、绝对路径和无应用实例回退分支。
- 执行 `git diff --check`；不构建、不运行。

## Plan

1. 读取 `utils.h`、`utils.cpp` 当前完整内容和调用点。
2. 记录并实现 `mkdirs()` 的可执行文件相对路径契约。
3. 静态复核保存配置、保存波形调用链与最终路径。

## Verification result

- `utils.cpp` 与 `fileutils.cpp` 均已由 `src/libs/utils/CMakeLists.txt` 纳入 `Utils`。
- 所有相对 `Utils::mkdirs()` 调用参数均为 `../data/`；另一有效调用传入已解析的绝对路径。
- 保存配置与所有 Save IQ Data 调用继续使用同一入口，无需调用点分支。
- `git diff --check` 通过，仅显示工作区既有的 LF/CRLF 转换提示。
- 按静态验证范围未执行构建或运行。
