# HTRA 插件依赖与打包遗漏排查

## 目标

- 确认 `plugin/HTRA.dll` 的直接依赖 DLL。
- 判断这些依赖在当前运行目录/打包目录中是否齐全。
- 结合 Windows 打包脚本分析是否存在遗漏路径或未被扫描到的运行时依赖。

## 计划

1. 检查 Windows stage / 运行时部署脚本，确认插件 DLL 的收集与依赖扫描策略。
2. 使用 Win32 工具链分析 `plugin/HTRA.dll` 的导入表与递归依赖。
3. 将依赖结果与仓库根 `bin/`、`plugin/`、第三方目录及系统运行时进行逐项核对。
4. 输出“确定缺失 / 高风险缺失 / 当前已覆盖”的结论，并给出修复建议。

## 备注

- 本次任务以静态依赖和现有打包脚本核对为主，不修改业务代码。

## Findings

- `src/plugins/htra/CMakeLists.txt` 中，`HTRA.dll` 链接 `HTRA::HTRA`，构建期会把 `HTRA_API_RUNTIME_FILES` 复制到根 `bin/`。
- `3rdParty/htra/CMakeLists.txt` 在当前 Windows 配置下将 `HTRA_API_RUNTIME_FILES` 定义为 4 个文件：`h2_api.dll`、`libiomp5md.dll`、`libliquid.dll`、`libmkl.dll`。
- 最新 stage 包 `stage/SGStudio_v2.3.4_Win64/bin` 只包含 `h2_api.dll`，不包含 `libiomp5md.dll`、`libliquid.dll`、`libmkl.dll`。
- 但对 stage 包二进制做 `dumpbin /dependents` 后，`stage/.../plugin/HTRA.dll` 的直接第三方依赖只有 `h2_api.dll`；`stage/.../bin/h2_api.dll` 的直接外部依赖为 `VISA64.dll`、`WS2_32.dll`、`SETUPAPI.dll`。
- `WS2_32.dll`、`SETUPAPI.dll` 属于系统 DLL；`VISA64.dll` 不是系统自带通用 DLL，本机之所以能加载，是因为 `C:/Windows/System32/visa64.dll` 已存在。
- 当前 stage 包和根 `bin/` 都没有 `VISA64.dll`。因此在未安装 NI-VISA/IVI VISA runtime 的 Win11 机器上，`h2_api.dll` 会先于插件初始化失败，进而导致 `HTRA.dll` 无法被 Qt/PluginManager 加载。

## Conclusion

- 从“插件能否被 Windows loader 成功装载”这个角度看，当前最明确的遗漏不是 `libmkl.dll` / `libliquid.dll` / `libiomp5md.dll`，而是 `VISA64.dll` 对目标机环境的前置依赖没有被随包满足，也没有被安装器/启动前检查显式声明。
- `libmkl.dll` / `libliquid.dll` / `libiomp5md.dll` 虽然在开发运行目录被复制，但当前静态导入表里没有看到它们参与 HTRA 插件的装载时依赖；它们更像是厂商包一并提供的配套运行时，是否影响具体业务功能还需后续运行时验证。
- 现有 stage 脚本的运行时递归扫描对象只覆盖 `SGStudio.exe` 与 `plugin/*.dll`，不会继续递归扫描已复制进 `bin/` 的第三方 DLL 自身依赖；如果后续 `h2_api.dll` 新增其他真实装载依赖，这条链路仍有继续漏包的风险。

## Recommended Fix Direction

1. 先确认 `VISA64.dll` 的分发策略：若许可证允许，可在 Windows stage 中显式携带对应 runtime；若不允许，则必须把 NI-VISA 作为安装前置条件并在启动或安装阶段做检测。
2. 在 stage 验证中增加对 `plugin/HTRA.dll` -> `bin/h2_api.dll` -> `VISA64.dll` 的显式检查，避免开发机因系统已安装 VISA 而掩盖问题。
3. 若后续确认 `libmkl.dll` / `libliquid.dll` / `libiomp5md.dll` 在业务运行时也必需，则需要补一轮针对 `bin/` 第三方 DLL 的递归依赖扫描或显式 install 规则。