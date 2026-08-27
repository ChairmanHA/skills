# CMake 插件运行时目录修复

## 目标
- 解决 `plugin/` 目录在 Debug/Release 切换后残留另一配置插件 DLL 的问题。
- 解决 VS Code 下构建/启动后，普通运行时依赖被错误复制到 `plugin/` 目录的问题。

## 现状
- 顶层 `sgstudio_add_plugin()` 将所有插件输出到仓库根目录 `plugin/`。
- 插件启用了 `DEBUG_POSTFIX d`，因此切换配置后会同时存在 `Core.dll` 与 `Cored.dll` 这类同名双版本插件。
- vcpkg toolchain 会在 `add_library(SHARED)` 时为动态库追加 applocal post-build 逻辑；插件输出目录是 `plugin/` 时，这些依赖也会被部署到 `plugin/`。

## 方案
- 插件 target 的真实输出不再直接落到仓库根目录 `plugin/`，而是落到构建目录内的隔离 staging 目录。
- 构建开始前，统一清空仓库根目录 `plugin/`，避免 Debug/Release 切换后残留上一配置插件。
- 每个插件构建完成后，只把插件运行库本身同步回仓库根目录 `plugin/`。
- 这样即使 vcpkg / Qt applocal 继续对插件 target 做自动部署，被污染的也只是构建目录 staging，不会污染实际运行目录。

## 验证
- 重新配置并构建 Release，检查 `plugin/` 仅保留 Release 插件。
- 再切到 Debug 构建，检查 `plugin/` 仅保留 Debug 插件。
- 启动 Release/Debug 可执行文件后再次检查 `plugin/`，确认没有额外依赖 DLL 混入。

## 2026-03-13 补充修正
- 上述“构建开始前统一清空仓库根目录 plugin/”不能通过一个无输出的 custom target 挂到每个插件依赖链上。
- 原因是 Qt Creator 点击 Run 时会先做一次增量构建检查；该 custom target 因为没有输出，会被视为总是需要执行，从而在程序启动前把根 plugin 目录清空。
- 如果插件目标本身没有重编译，POST_BUILD 不会重新拷贝插件，于是应用在运行阶段直接因缺失插件 DLL 崩溃。
- 修正方式：保留插件真实输出到 build staging 目录、保留 POST_BUILD 只同步插件 DLL 到根 plugin 目录，但把“清空 plugin 目录”改为显式人工目标 `sgstudio_reset_plugin_directory`，不再自动绑定到常规构建或 Run 前检查。
- 结论：根 plugin 目录的清理只能作为显式维护动作，不能挂进日常增量构建链路。