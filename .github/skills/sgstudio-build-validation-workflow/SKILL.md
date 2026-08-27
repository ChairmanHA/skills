---
name: sgstudio-build-validation-workflow
description: 'Use when validating SGStudio C++ changes with the existing Debug build tree, choosing between plugin-target incremental build and full Debug build, monitoring long-running builds asynchronously, and handling MSVC C1041 PDB contention on Windows.'
argument-hint: 'Describe the touched files, whether shared libs or CMake/runtime files changed, and whether you need quick compile verification or repo-wide confidence.'
user-invocable: true
---

# SGStudio Build Validation Workflow

## When to Use

- 验证 SGStudio 仓库里的 C++ / Qt 改动是否能通过编译。
- 决定本次应该只做插件目标增量编译，还是要跑工作区定义好的完整 Debug 构建任务。
- 需要在 Windows 上持续观察长时间编译进度，而不是盲目重跑构建。
- 遇到 MSVC `C1041`、PDB 竞争、任务输出不够直观等仓库常见构建噪音。

## Core Rules

- 默认复用现有 `build/cmake-win-debug` build tree，不要每次为了验证小改动重新配置工程。
- 默认优先做最窄可证明的增量编译；只有当改动面跨越共享库、App、打包或 CMake 配置边界时，才升级为完整 Debug 构建。
- 长时间构建要么走工作区定义的 Debug 构建任务，要么走同一 build tree 的异步终端构建；不要同步阻塞后失去进度可见性。
- 监控进度时读取同一个终端 / 任务输出，不要把“再次启动构建命令”当作刷新方式。
- Windows / MSVC 下若命中 `error C1041`，优先在当前终端临时注入 `/FS` 再重试，不要先改仓库构建系统。

## Scope Decision

1. 先判断改动归属。
   - 只改了单个插件目录下的实现文件，例如 `src/plugins/analog/**`、`src/plugins/core/**`：优先插件目标增量编译。
   - 改了共享库实现或共享 qrc / 资源，例如 `src/libs/controls/**`、`src/libs/business/**`：优先编译当前验证链上最近的依赖目标；若需要更高置信度，再升级完整 Debug 构建。
   - 改了 `CMakeLists.txt`、App 入口、插件装载、运行时布局、打包脚本、`configuration_files/**`：直接跑完整 Debug 构建任务。

2. 再判断验证目标。
   - 只是确认本次局部改动能编过：窄目标增量编译即可。
   - 需要确认不会打穿整仓依赖：完整 Debug 构建更合适。

## How To Find The Narrow Target

1. 打开对应目录的 `CMakeLists.txt`。
2. 找 `add_library(...)` 或 `add_executable(...)` 的目标名。
3. 插件改动优先直接编插件目标。
4. 如果同时改了插件依赖的共享库，例如 `Controls` 资源，而当前验证重点仍是某个插件，就直接编该插件目标，让依赖链自动带起共享库重编。

示例：

- `src/plugins/analog/CMakeLists.txt` 的目标是 `AnalogModulation`。
- 改了 `DigitalPanel`，或者同时改了它依赖的 `Controls` 资源，优先编 `AnalogModulation` 即可。

## Preferred Build Modes

### A. 快速局部验证：现有 Debug build tree + 目标增量编译

适用：

- 单插件改动。
- 插件 + 少量直接依赖库改动。
- 需要尽快知道“这次改动能不能编过”。

Windows 推荐命令模板：

```powershell
$env:CL='/FS'; cmake --build build/cmake-win-debug --config Debug --target <Target> -- /m:1 /p:CL_MPCount=1 /p:UseMultiToolTask=false
```

说明：

- `/FS`：缓解当前仓库在 MSVC 下常见的 `C1041` PDB 竞争。
- `--target <Target>`：只编当前验证链需要的目标。
- `/m:1`、`CL_MPCount=1`、`UseMultiToolTask=false`：尽量收敛多层并行，减少 PDB 冲突噪音。

### B. 较大范围验证：工作区定义的完整 Debug 构建任务

适用：

- 改了共享库公共接口。
- 改了 CMake / 运行时布局 / 配置拷贝逻辑。
- 需要完整 Debug 产物和更高覆盖面。

优先任务：

- `shell: CMake Build Debug`

原因：

- 工作区任务已经串好了 configure + build。
- 对仓库当前 Debug 工作流最贴近用户日常使用方式。

## Async Monitoring Procedure

1. 启动长构建时，优先保留可继续读取的会话句柄。
   - 如果工具能稳定返回工作区任务输出，就用任务输出继续看进度。
   - 如果任务输出句柄不稳定，直接对等价的 build tree 命令使用异步终端运行。

2. 观察三类进度信号。
   - 新的 `Configuring done`、`Generating done`、`Build files have been written`。
   - 新的 `Automatic MOC`、`Building`、`Linking`、`Generating Code`、`*.vcxproj -> ...`。
   - 输出末尾持续变化，而不是长时间停在完全同一行。

3. 用最终成功 / 失败信号收尾。
   - 成功：目标出现 `*.vcxproj -> ...\xxx.dll` 或 `...\xxx.exe`。
   - 失败：出现 `error Cxxxx`、`FAILED:`、MSBuild 错误摘要，或终端返回非零退出码。

4. 如果要再次检查进度，只读取同一个异步终端 / 任务输出。
   - 不要重新发起第二次相同构建。

## Escalation Rules

- 插件目标编译通过，但你改了共享库公共头文件、模板、导出接口：升级跑完整 Debug 构建。
- 局部目标构建失败，但失败点落在无关的大范围并行噪音上：先用 `/FS` + 窄目标收敛，再决定是否升级。
- build tree 不存在、明显过期、或 CMake 输入变更导致频繁重配：先接受一次重新 configure，再继续增量构建。

## Common Failure Modes

- `error C1041: cannot open program database ...vc143.pdb`
  - 原因：MSVC 并行写同一个 PDB。
  - 处理：在当前终端先设 `$env:CL='/FS'`，并降低 MSBuild / CL 并行度。

- 小改动却去跑完整仓构建
  - 原因：没有先按目录和目标归属缩小验证面。
  - 处理：先从目标 `CMakeLists.txt` 找 owning target，优先编该目标。

- 为了看进度而重复启动构建
  - 原因：把“重跑命令”当成刷新输出。
  - 处理：坚持复用同一个异步终端 / 任务输出句柄。

- 每次都重新 configure
  - 原因：把“验证代码改动”和“重建 build tree”混在一起。
  - 处理：默认复用现有 `build/cmake-win-debug`，只在 CMake 输入真的变了时接受重配。

## Expected Output

- 小范围改动优先被快速验证，不再默认走全量构建。
- 长构建有稳定的异步观察方式，不再“看不到输出就重跑”。
- 遇到当前仓库常见的 `C1041` 时，有固定收敛手法。
- 何时该升为完整 Debug 构建，边界清晰。 