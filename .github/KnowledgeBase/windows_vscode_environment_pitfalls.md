# Windows / VS Code 环境坑记录

本文记录两个已经在当前仓库实际出现过、但本质上不属于业务代码缺陷的环境问题：

- Windows 构建输出中的 `pwsh.exe` 提示
- VS Code 中 Qt includePath / IntelliSense 缺失

这两个问题都容易干扰判断：

- 一个看起来像“构建报错”，但当前通常不阻塞最终产物生成
- 一个看起来像“代码一片红”，但真实 CMake 构建仍然可以通过

## 1. `pwsh.exe` 提示

### 现象

在 VS Code 或命令行执行 CMake / MSBuild 构建时，输出中可能出现：

```text
'pwsh.exe' 不是内部或外部命令，也不是可运行的程序或批处理文件。
```

当前已观察到的特点是：

- 该提示常出现在某些 target 的 link / post-build 阶段之后
- 提示出现时，目标本身仍可能成功生成
- 整体 `cmake --build ...` 可能最终仍然返回成功

### 根因归类

这是 Windows 本机工具环境与某些构建辅助脚本之间的兼容性问题，不是 SGStudio 业务代码本身的编译错误。

当前已知背景：

- 机器环境缺少 `pwsh.exe`
- 但工程构建链中的某些外部步骤会尝试调用 `pwsh.exe`
- 工程主构建目前仍可通过，因此它更像“构建噪音”或“辅助步骤环境缺口”，而不是主流程阻塞点

它常见于：

- vcpkg / applocal
- 某些 VS / MSBuild 辅助脚本
- 某些默认假设本机装有 PowerShell 7 的后处理动作

### 影响判断

#### 当前通常不阻塞的场景

如果满足以下条件，可以先把它归类为“非阻塞环境提示”：

- 目标 DLL / EXE 最终确实生成了
- `cmake --build` 最终成功
- 运行目录中的关键产物实际已就位
- 程序可以正常启动或至少进入下一步排障阶段

#### 必须升级处理的场景

如果出现下面任一情况，就不能再把它当成纯噪音：

- 构建最终失败
- 某些后处理复制动作没有执行
- 运行目录缺少预期 DLL / plugin / manifest
- VS Code 与 Qt Creator 的构建结果开始出现分叉

### 推荐处理方式

按优先级建议如下：

1. 先看最终构建是否成功，不要因为单条 `pwsh.exe` 提示就立即误判为主构建失败。
2. 如果构建成功且产物完整，可先记录为环境噪音，继续推进真正的问题定位。
3. 如果需要彻底消除这类提示，优先补齐本机 PowerShell 7 / `pwsh.exe` 环境，或定位到底是哪一步外部脚本在调用它。
4. 如果后续确认某个自定义 post-build 明确依赖 `pwsh.exe`，应优先改成 `powershell.exe` 或 CMake 自带命令，避免对机器环境做额外假设。

### 工程内的经验结论

- 看到 `pwsh.exe` 提示时，先判断“是否阻塞最终构建”，再决定要不要处理。
- 不要把它和 C++ 编译错误、链接错误、Qt plugin 加载错误混为一谈。
- 如果只是提示但不影响产物，不应中断当前真正的运行时问题排查。

## 2. VS Code Qt includePath / IntelliSense 缺失

### 现象

在 VS Code 编辑器里，`main.cpp`、`pluginmanager.cpp` 等 Qt 源文件可能出现大量 IntelliSense 报错，例如：

- `无法打开 源 文件 "QApplication"`
- `无法打开 源 文件 "QDir"`
- `无法打开 源 文件 "QtGlobal"`
- `检测到 #include 错误。请更新 includePath。`

但与此同时：

- CMake configure 是成功的
- `cmake --build` 也能成功
- 程序仍然可以正常运行

### 根因归类

这通常不是编译器真的找不到 Qt，而是 VS Code 的 C/C++ 扩展没有拿到正确的 IntelliSense 配置。

当前仓库里可见的状态是：

- `.vscode/settings.json` 只配置了 `C_Cpp.default.compilerPath = cl.exe`
- 配置了 CMake source / build directory 和 `CMAKE_PREFIX_PATH`
- 但没有 `c_cpp_properties.json`
- 也没有 `compile_commands.json`
- 也没有显式把 C/C++ 扩展接到 CMake Tools 的 configuration provider

因此结果是：

- 真正构建时，CMake / MSBuild 知道 Qt 在哪里
- 但 IntelliSense 本身不知道

### 影响判断

#### 当前属于编辑器体验问题

如果满足下面条件，它属于“编辑器诊断失真”，而不是“工程真的编不过”：

- CMake configure 成功
- `cmake --build` 成功
- Qt 头文件错误只出现在 VS Code Problems / 波形红线里

#### 需要尽快处理的场景

如果出现下面任一情况，应尽快补齐 IntelliSense 配置：

- 频繁误导问题定位
- 导致跳转、补全、重命名等编辑能力明显失效
- 团队成员开始把 IntelliSense 红线误判为真实构建失败

### 推荐处理方式

优先级建议如下：

1. 优先确认真实 `cmake --build` 是否成功，不要用 IntelliSense 红线代替真实构建结论。
2. 在 VS Code 中优先让 C/C++ 扩展接入 CMake Tools 的配置提供能力，而不是手工堆大量 includePath。
3. 可选地为当前 build tree 导出 `compile_commands.json`，再让 VS Code 基于它驱动 IntelliSense。
4. 如果暂时不修 IntelliSense，也要在排障时明确区分“编辑器报红”和“真实构建失败”。

### 工程内的经验结论

- VS Code 中 Qt 头文件一片红，不等于 CMake 工程真的有编译错误。
- 先信真实构建结果，再信 IntelliSense。
- 只有当它明显影响开发效率时，才值得把它升级写进 instructions；否则更适合留在 KnowledgeBase 里当环境坑说明。

## 相关文件

- [../.vscode/settings.json](../.vscode/settings.json)
- [cmake_build_output_clean_run_workflow.md](cmake_build_output_clean_run_workflow.md)
- [../TaskLog/2026-03-13_cmake_build_output_clean_run_workflow_doc.md](../TaskLog/2026-03-13_cmake_build_output_clean_run_workflow_doc.md)