# 2026-07-21 Streaming FileList 表格化

## Scope

- 只修改 `src/plugins/htra/streamingpanel.cpp`、`src/plugins/htra/streamingpanel.ui`。
- 不修改 streaming 业务层、属性定义或设备交互逻辑。

## Verification Level

- static

## Observation

- `StreamingPanel` 当前使用 `QListWidget` 展示文件列表，单条文本里拼接了文件名、sample count 和 sample rate。
- 文件列表控件在 `streamingpanel.ui` 中只占据第 0 列，因此视觉上只覆盖左半边。
- 多音 `toneTable` 在 `multitonepanel.cpp` 中通过本地 `QTableWidget` 配置和主题相关 QSS 实现了居中表头、居中内容和统一的表格视觉。

## Inference

- 这次需求可以限制在 panel 本地 UI 层：把 `QListWidget` 替换为三列表格，并把现有元数据显示逻辑拆分到独立列即可。
- 最小风险路径是保留 `Streaming_FileList` 属性和现有 `wavFileInfo(...)` 解析入口，只替换控件填充、删除和拖拽后重组顺序的本地实现。

## Success Criteria

1. `StreamingPanel` 的文件列表改为三列表格，列为 `File Name`、`Sample Count`、`Sample Rate`。
2. 表格横向跨满 streaming 面板整行，而不是只占左半列。
3. 表头和单元格内容均居中显示，局部样式与 `MultitonePanel::toneTable` 保持一致。
4. 现有加载、卸载、删除、拖拽重排后的属性同步逻辑继续工作。
5. 本轮只做静态验证，不跑构建或运行。

## 2026-07-21 Minibar Helper Follow-up

- 观察：`src/app/minibarhelper/remotemodformeditors.cpp` 的 `RemoteStreamingPanel` 复用同一个 `Ui::StreamingPanel`，但仍按 `QListWidget` 使用 `FileList`，包括 `currentItem()`、`count()`、`setCurrentRow()`、`QListWidgetItem` 构造和单参数 `item(row)`。
- 推论：主面板把 `FileList` 切到 `QTableWidget` 后，helper 端必须同步切到表格 API，并补上表头/列配置，否则会出现当前编译错误，且即便编过也会出现表格未初始化的问题。
- 跟进成功标准：`RemoteStreamingPanel` 使用三列表格 API 完成同样的文件名、sample count、sample rate 展示；拖拽重排、删除当前行和当前选中恢复逻辑继续有效；静态诊断无新增错误。

### Verification Result

- `get_errors` 检查 `src/app/minibarhelper/remotemodformeditors.cpp` 无新增静态诊断。
- 使用现有 `build/cmake-win-debug` 对 `SGStudioMiniBar` 做窄目标 Debug 增量构建，命令以 `/FS` 和单并行收敛 PDB 竞争。
- 构建结果：`SGStudioMiniBar.vcxproj -> D:\development\vsg2.0\bin\SGStudioMiniBar.exe`，退出码 `0`。
- 观察到与本次改动无关的既有构建噪音：多处 `warning C4819`，以及一次历史存在的 `'pwsh.exe' 不是内部或外部命令` 输出；本轮未扩 scope 处理。