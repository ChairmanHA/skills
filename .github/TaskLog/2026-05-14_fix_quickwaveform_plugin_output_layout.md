# quickwaveform 插件输出目录修复

## 背景
- 新增 quickwaveform 插件后，Qt Creator 下构建运行时，插件 DLL 没有进入 build tree 的 plugin 运行目录，导致 PluginManager 无法加载。
- 当前仓库同时支持 `build-tree` 与 `repo-root` 两套运行时布局；修复必须同时覆盖 Qt Creator 与 VS Code 工作流。

## 局部结论
- `src/plugins/CMakeLists.txt` 中的 `sgstudio_configure_plugin_target()` 负责把插件真实输出定向到 `${CMAKE_BINARY_DIR}/plugin-runtime`，并在构建后把插件 DLL 同步到 `${SGS_PLUGIN_DIR}`。
- 现有 Core/Analog/HTRA/GPS/Updater 都调用了这个 helper。
- `src/plugins/quickwaveform/CMakeLists.txt` 当前只定义了 `add_library(QuickWaveform SHARED ...)` 和链接关系，没有调用该 helper，因此会沿用普通共享库输出目录，而不是插件目录链路。

## 修改计划
1. 在 `src/plugins/quickwaveform/CMakeLists.txt` 末尾补上 `sgstudio_configure_plugin_target(QuickWaveform)`。
2. 用一次针对性的 CMake configure/build 检查确认该 target 重新接入统一插件 staging / copy 链路。

## 预期结果
- Qt Creator 默认 `build-tree` 下：`QuickWaveform.dll` 会出现在当前 build tree 的 `plugin/`。
- VS Code `repo-root` 下：`QuickWaveform.dll` 会通过 `plugin-runtime` 同步到仓库根 `plugin/`。
