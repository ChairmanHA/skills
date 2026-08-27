# Windows Stage 中 MSVC 运行库的打包边界

本文总结当前仓库在 Windows stage 发布链路中，对 MSVC 运行库、UCRT 与 system DLL 的打包策略，以及背后的取舍依据。

本文的目标不是解释某一次临时修补，而是给后续编写、重构发布链路时提供稳定参考。

## 1. 先说结论

当前仓库在 Windows stage 包中的运行库策略应固定为三条：

1. 显式携带全部 MSVC 非 UCRT runtime。
2. 同包附带 `vc_redist.x64.exe`。
3. 不把 UCRT 和 Windows system DLL 做 app-local 打包。

换句话说，推荐策略不是：

- 只靠 `windeployqt --compiler-runtime`；
- 只手工挑 `vcruntime140.dll` / `vcruntime140_1.dll` 两个文件；
- 或者把 `ucrtbase.dll`、`api-ms-win-*`、`kernel32.dll` 这一类系统组件一股脑复制进应用目录。

## 2. 这条边界是谁负责的

在当前仓库的 stage 链路里，运行时分工应保持清晰：

- Qt runtime 与 Qt plugins：由 `windeployqt` 负责。
- 非 Qt 第三方 DLL：由显式 `install()` 与 `file(GET_RUNTIME_DEPENDENCIES)` 负责。
- MSVC 非 UCRT runtime：由顶层 `CMakeLists.txt` 显式加入 stage `bin/`。
- `vc_redist.x64.exe`：由顶层 `CMakeLists.txt` 显式加入 stage `bin/`，作为现场修复入口。

这个分工比“一个工具包打天下”更稳定，也更容易定位问题。

## 3. 为什么不能只依赖 windeployqt

`windeployqt --compiler-runtime` 能补齐一部分编译器运行时，但它不适合作为唯一真源，原因有三点：

1. 它的职责重点是 Qt 运行时，而不是项目整体的 Windows 运行库治理。
2. 在环境较混乱的机器上，只依赖它的隐式行为，最终 stage 包仍可能受目标机运行库状态影响。
3. 仅靠它无法清晰表达“哪些运行库是项目明确承诺随包分发的”。

因此当前项目要做的是：

- 继续使用 `windeployqt` 处理 Qt；
- 但把 MSVC 非 UCRT runtime 与 `vc_redist.x64.exe` 的入包职责收回到 CMake 显式规则中。

## 4. 为什么不能只打包两个 vcruntime 文件

早期最容易想到的是只补：

- `vcruntime140.dll`
- `vcruntime140_1.dll`

这能覆盖一部分机器，但从发布工程角度仍然不够完整。

原因是实际依赖闭包里，常见还会出现：

- `msvcp140.dll`
- `msvcp140_1.dll`
- `msvcp140_2.dll`
- `msvcp140_atomic_wait.dll`
- `msvcp140_codecvt_ids.dll`
- `concrt140.dll`

如果只手工挑两个 `vcruntime`，等于把运行时完整性建立在“当前恰好没撞到其他 CRT/STL 组件”的侥幸上。

因此更合理的策略是：

- 从 `CMAKE_INSTALL_SYSTEM_RUNTIME_LIBS` 里收集整套候选；
- 过滤掉不该 app-local 化的 UCRT/system 项；
- 剩余的 MSVC 非 UCRT runtime 全部入包。

## 5. 为什么不把 UCRT 一起塞进包

不建议把 UCRT 一并放进 stage 包，核心原因是：

1. UCRT 在 Windows 10/11 上属于操作系统级组件的一部分。
2. 它由系统更新统一维护，强行 app-local 化容易引入版本错配与维护责任混乱。
3. 当目标机器已经“脏”到 UCRT 状态异常时，应用目录硬塞几份 DLL 往往不是根治方案。

典型不建议 app-local 打包的文件包括：

- `ucrtbase.dll`
- `ucrtbased.dll`
- `api-ms-win-*`
- `ext-ms-win-*`

## 6. 为什么不把 Windows system DLL 一起塞进包

同样不建议把下面这类 DLL 复制进应用目录：

- `kernel32.dll`
- `user32.dll`
- `gdi32.dll`
- `shell32.dll`

原因是：

1. 这些 DLL 属于系统加载器与核心系统 API 的一部分。
2. 把它们 app-local 化既不符合 Windows 运行库治理边界，也有明显的兼容与安全风险。
3. 即便某台机器确实异常，应用自带一份 system DLL 也不应成为修复手段。

项目应该负责的是“应用自己的运行库”；系统应该负责的是“系统自己的运行库”。

## 7. 当前推荐策略

当前仓库的 Windows stage 运行库策略，推荐固定为下面这个组合：

### 7.1 必须显式入包

- 全量 MSVC 非 UCRT runtime
- `vc_redist.x64.exe`

### 7.2 必须继续排除

- `ucrtbase*.dll`
- `api-ms-win-*`
- `ext-ms-win-*`
- 所有 Windows system DLL

### 7.3 对现场问题的兜底方式

当目标机器确实存在运行库问题时：

1. 优先使用包内的 `vc_redist.x64.exe` 做官方修复。
2. 不要尝试通过往 `bin/` 里继续塞 UCRT/system DLL 来“碰运气”。

## 8. 在本仓库中的落地实现建议

后续若有人改写 stage 链路，应保持以下实现原则：

1. 继续保留 `InstallRequiredSystemLibraries`，因为它能提供当前工具链视角下的 system runtime 候选集。
2. 不要直接全量安装 `CMAKE_INSTALL_SYSTEM_RUNTIME_LIBS`，而要先做过滤。
3. 过滤规则至少应排除：
   - `ucrtbase*.dll`
   - `api-ms-win-*`
   - `ext-ms-win-*`
4. 对 `vcruntime*`、`msvcp*`、`concrt*`、`vccorlib*`、`vcamp*`、`mfc*`、`atl*` 这类 MSVC runtime 保持白名单入包。
5. `vc_redist.x64.exe` 应通过 Visual Studio Redist 标准目录或 `VCToolsRedistDir` 派生候选路径，并在找到时随包复制。
6. 找不到某些运行库时，应输出 warning，而不是直接让 stage 失败。

## 9. 当前策略的验证标准

判断当前 stage 策略是否正确，不要只看“程序是否在开发机能启动”，还应检查：

1. stage `bin/` 中是否包含完整的 MSVC 非 UCRT runtime 子集。
2. stage `bin/` 中是否包含 `vc_redist.x64.exe`。
3. stage `bin/` 中是否没有 `ucrtbase.dll`、`api-ms-win-*`、`ext-ms-win-*` 和 system DLL。
4. stage 包在当前机器上是否可直接运行。

如果上述 4 条同时满足，这条发布链路才算落在了正确边界上。

## 10. 与其他发布文档的关系

阅读顺序建议如下：

1. 先看 `windows_cmake_clone_build_stage_guide.md`，理解当前 stage 的整体分工。
2. 再看本文，理解为什么 MSVC runtime 要显式治理、为什么 UCRT/system DLL 不该入包。
3. 若怀疑 Qt 混装，再看 `windows_qt_runtime_mixing_pitfall.md`，理解为什么 Qt 运行时必须保持单一来源。

## 11. 适用范围

本文只适用于本仓库当前的：

- Windows
- MSVC
- Qt 5.15.x
- CMake stage 发布链路

如果未来改成 MinGW、Clang-cl、MSIX、安装器式发布，本文中的实现细节需要重新评估，但“不要 app-local 打包 UCRT/system DLL”这条边界仍应默认成立。