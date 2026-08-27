# 2026-03-16 Stage Qt Runtime Dependency Scan Fix

## Goal
- 修正 Windows stage 发布链路，避免只有主程序可见的 Qt 依赖被部署，而 DLL/插件传递引入的 Qt 模块漏打包。
`
## Findings
- 当前 stage 安装脚本只对 bin/SGStudio.exe 执行一次 windeployqt。
- 随后的 file(GET_RUNTIME_DEPENDENCIES) 虽然扫描了 stage 主程序和 plugin 目录 DLL，但通过 POST_EXCLUDE_REGEXES 及复制阶段过滤，无条件排除了所有 Qt5*.dll。
- 已验证缺失案例为 Utils.dll -> Qt5Xml.dll，主程序本身并不直接导入 Qt5Xml.dll。

## Change Plan
- 让 stage 脚本把主程序、bin 下内部模块 DLL、plugin 下插件 DLL 一起传给 windeployqt，而不是仅传 SGStudio.exe。
- 保留现有 windeployqt 调用位置和职责。
- 调整运行时依赖复制逻辑：不再无条件排除 Qt5*.dll，只在目标 Qt DLL 已存在于 stage/bin 时跳过复制。
- 避免把 xlsx.dll、libcurl.dll 这类非 Qt 第三方 DLL 直接喂给 windeployqt；这些 DLL 仍通过 install 或运行时依赖复制进入 stage  包。
- 为 Utils 链接的 xlsx.dll 增加 install 级别复制，避免 stage 成功与否影响其入包。
- 保持系统 DLL、MSVC runtime、stage/plugin 内部 DLL 的现有过滤策略不变。

## Verification Plan
- 重新执行 CMake Stage Release。
- 检查 stage/bin 是否包含 Qt5Xml.dll。
- 用 dumpbin 验证 Qt5Xml.dll 仍由已打包模块需要，且 stage 产物已补齐。

## Implementation Notes
- stage 里的 windeployqt 输入已收紧为：SGStudio.exe、bin 下内部模块 DLL、plugin 下插件 DLL；显式跳过 Qt 自身 DLL、MSVC runtime 和 xlsx/libcurl/zip/zlib1/h2_api/gensignalwave 等非 Qt 第三方 DLL。
- Utils 的 xlsx.dll/xlsxd.dll 改为按配置 build copy + install copy，避免 stage 成败影响 xlsx 入包。
- stage 运行时脚本里检查“Qt DLL 是否已在 stage/bin”时，使用 `list(FIND ...)` 替代 `IN_LIST`，兼容当前 install 脚本执行时的 CMake policy。

## Verification Result
- 重新删除 `stage/SGStudio_v1.1.1_Win64` 后执行 stage，产物成功生成。
- `stage/SGStudio_v1.1.1_Win64/bin` 已包含 `Qt5Xml.dll` 与 `xlsx.dll`。
- `dumpbin /dependents` 验证 `Utils.dll` 仍直接依赖 `Qt5Xml.dll` 和 `xlsx.dll`。
- 实际启动 `stage/SGStudio_v1.1.1_Win64/bin/SGStudio.exe`，进程成功存活，10 秒后由验证脚本主动结束。
- 仍存在未解析依赖告警：`azureattestmanager.dll`、`azureattestnormal.dll`、`hvsifiletrust.dll`、`pdmutilities.dll`、`wpaxholder.dll`；本次未阻塞程序启动。 