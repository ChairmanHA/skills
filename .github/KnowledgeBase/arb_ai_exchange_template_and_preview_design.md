# AWG CSV 通用契约 1.0：维护入口

面向用户与 AI 的唯一完整格式说明位于：

[SGStudio_AWG_CSV_Contract_1.0.md](../../../vsg2.0/package-info/support/SGStudio_AWG_CSV_Contract_1.0.md)

该 Markdown 同时覆盖基础波形与 Arb，可重复交给 AI 使用。本页只记录代码归属和验证边界，不维护第二份格式正文。

## 1 当前行为

- 保存仅生成一份 CSV，包含 17 项标准元数据、素材真实样点和有序范围/重复表；契约 1.0，format_version=1。
- 用户处理范围由消息中的展开后全局点索引或带单位的时间描述，CSV 不保存编辑选区、查看范围或处理指令。
- 面板仅在非 Arb 模式提供 Save CSV，基础 Sine/Square/Pulse/Ramp 保存一周期；Arb 模式移除 Save 布局项，Waveform Type 移至右侧预览上方，左侧参数向上补位、不保留顶部空行；切回其他类型恢复原顶部两列布局。CSV 格式仍能表达完整有限记录并供 Arb 加载。单点素材的 repeat 是同值样点数，不能直接当作周期数。
- 普通 Load File 校验并恢复记录设置，按实际点列加载为 Arb。预览保留 overview 查看框、局部放大和双击创建/拖动的样点游标，不再提供独立编辑选区；查看范围不持久化。
- 新启动面板默认负载 50 Ω、Linear；移除负载、自定义负载、插值编辑按钮，实际设置改为摘要只读显示。默认开路 4 Vpp 对应 50 Ω 下显示 2 Vpp。
- 面向 AI 的通用契约现固定 `expected_load_ohms=50`、`interpolation=linear`，具体电压/边沿语义、不符合固定设置的输入处理及自检要求以通用 Markdown 为准。本次仅收窄交换文档约束，未修改程序对历史其他负载或零阶保持文件的读取和预览兼容性。
- Linear 连接不改变存储点列；单点高电平形成三角尖峰。契约第 5 节说明点间边沿语义，以及外部合并不同采样率时的转换义务；程序未新增重采样功能。
- 通用 Markdown 暂存在 package-info/support，等待测试；按用户要求，本轮未接入运行目录复制、打包或分发。

## 2 代码归属

| 文件 | 职责 |
| --- | --- |
| [AwgCsvExporter](../../../vsg2.0/src/plugins/awg/awgcsvexporter.cpp) / [接口](../../../vsg2.0/src/plugins/awg/awgcsvexporter.h) | 只保存 CSV；接口不接收编辑选区，没有配套说明文件命名或写入 |
| [AwgFileImporter](../../../vsg2.0/src/plugins/awg/awgfileimporter.cpp) | 当前格式识别、记录参数/计数校验；不读取或要求选区字段 |
| [AwgSequenceCodec](../../../vsg2.0/src/plugins/awg/awgsequencecodec.cpp) | 素材/分段的精确紧凑保存、边界检查与展开 |
| [AwgPanel](../../../vsg2.0/src/plugins/awg/awgpanel.cpp) | 非 Arb 模式的单 CSV 保存入口、提示及普通加载；预览交互保留 |
| [AwgWaveformPreview](../../../vsg2.0/src/plugins/awg/awgwaveformpreview.cpp) | 局部图、overview 查看范围、精确时间输入及像素级缩略数据；不改变原始样点或导出范围 |
| [通用 Markdown 源文件](../../../vsg2.0/package-info/support/SGStudio_AWG_CSV_Contract_1.0.md) | 字段、语法、时间/电压、来源差异、用户坐标、修改与回传要求、完整示例的唯一正文 |

当前没有专门的 AI 结果比较或本地快照流程；程序只验证文件数据与播放参数，不自动判断自由文本任务是否完成。

### 预览与长记录绘制

- 布局为局部图 → 标准双行 View Start / View Span LabelButton → overview → 游标样点读数；时间输入复用现有单位键盘，按实际采样间隔对齐。有效 Arb 的 View Start 始终可编辑，包括 Span 满量程时；编辑 Start 与 overview 左手柄一致，保持当前右端点并联动改变 Span，至少保留一个采样间隔。编辑 Span 仍固定 Start。未加载有效记录时空图、输入禁用。
- overview 独立起止手柄调整查看宽度、不能跨越；框内平移保持宽度，框外点击定位或拖动建立新的查看范围。两图滚轮缩放，双击 overview 恢复全记录。不再显示橙色编辑选区、Zoom Selection / Full Record 按钮或选择读数。
- Arb 局部图双击创建或重新定位唯一游标，普通鼠标移动不改变游标。靠近可见竖线（8 px 内）显示水平调整光标，左键按住拖动、松开固定；双击第二次按住也可直接拖动。拖动保留抓取偏移、限于当前视图内真实样点，拖动期间滚轮不改变范围。外观仍为主题文字色竖直点线、8 px 空心圆和 `n / t / d / V` 读数，沿用时间格式及 9 位有效数字。鼠标移出绘图区后保留最后一次结果；查看范围移出该样点时只隐藏图上游标，读数保留。更换记录清空游标，无有效记录显示 `Cursor: --`，非 Arb 隐藏读数；此交互不建立编辑选区或改变数据。
- 缩略数据每个显示桶保留首、最小、最大、末样点，按真实索引排序去重后连续连接；删除原来桶间的 NaN 断口。平台不再退化成孤立点，窄脉冲极值保留。仍遵循记录的 Linear / ZOH；密集波形的桶内细节须放大查看真实点。
- 每 256 点的极值摘要建立二叉聚合树：记录替换时 O(N) 扫描，范围查询使用缓存和首尾少量原始样点。绘制预算按 QCP 布局完成后的实际轴宽确定，最多 2048 桶、8193 个显示点（含末端插值上下文）；overview 仅记录或宽度变化时重建，局部图通过排队重绘合并连续拖动。2000 万点缓存约 1.2 MiB；性能取决于样点数，不取决于以秒表示的时长。




