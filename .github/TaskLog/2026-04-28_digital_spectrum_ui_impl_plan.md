# 2026-04-28 Digital Spectrum UI Implementation Plan

## 目标

- 在 `DigitalPanel` 左侧空白区域增加一个 `showWaveForm` 按钮。
- 点击后弹出一个非模态对话框。
- 对话框内部使用 `QTabWidget`，第一个 tab 显示 spectrum。
- spectrum 直接消费 `DigitalModulator` 已生成的 `DigitalSpectrumResult`，不在 UI 层重复做 FFT。
- 仅当波形生成完成并收到 `spectrumDataChanged()` 后重绘；未生成完成时不做任何动作。

## 本地实现假设

- 现有项目已经内置 `QCustomPlot` 于 `Controls` 库，可直接复用，无需新增第三方依赖。
- 最小闭环是：
  - `DigitalModulation` 接 `digitalModulator->spectrumDataChanged()`
  - 转发 `DigitalSpectrumResult` 给 `DigitalPanel`
  - `DigitalPanel` 缓存最新频谱，并在非模态对话框打开时展示
- 频谱绘图只消费：
  - `freqHz`
  - `levelDbFs`
  - `rbwHz`
  - `displayPoints`
  - `analysisFftSize`

## 计划改动

1. 新增一个轻量 `DigitalSpectrumDialog`：
   - `QDialog`
   - `QTabWidget`
   - 第一个 tab 内含 `QCustomPlot`
2. 在 `digitalpanel.ui` 中增加 `showWaveForm` 按钮。
3. 在 `DigitalPanel` 中：
   - 响应按钮点击并以非模态方式显示 dialog
   - 新增 `setSpectrumData(const DigitalSpectrumResult &)`
   - 缓存最近一次频谱数据
4. 在 `DigitalModulation` 中：
   - 新增 `onSpectrumDataChanged()`
   - 在收到 `spectrumDataChanged()` 后，将快照转发给 `DigitalPanel`
5. 更新 `src/plugins/analog/CMakeLists.txt`，把新 dialog 文件纳入构建。

## 最便宜的证伪检查

- 完成后只跑一次 Debug 编译：
  - `cmake --build D:\development\vsg2.0\build\cmake-win-debug --config Debug --target ALL_BUILD --parallel`
- 该检查足以暴露：
  - ui 文件生成问题
  - 新增类未进 CMake
  - QCustomPlot 头/库引用错误
  - `DigitalModulation -> DigitalPanel` 接口不匹配

## 2026-04-28 样式修正补记

- `DigitalSpectrumDialog` 不应在暗色主题下局部强制改成白底 pane 和白底 plot；这会让标题栏和 tab 与全局主题断裂。
- 频谱对话框的 tab 视觉要对齐当前主题下的一级按钮语义：暗底、灰色选中块、白字或浅蓝字，而不是浅色页签。
- 标题行 `10.00 dBFS / Spectrum / RBW` 需要显式设置浅色文字；不能依赖默认 QWidget 调色板，否则局部 stylesheet 覆盖后容易在黑底上不可读。
- 频谱图可继续保留 `zeroLinePen = gridPen`，避免 0Hz 和 0dBFS 被单独高亮。

## 2026-04-28 星座图迁移补记

- `StandardConstellationWidget` 的绘制样式已经独立封装在控件内部，本轮不改其绘制逻辑，只改承载位置。
- `DigitalPanel` 原来的 `constellationHost` 保留为空白占位，不再往里面添加 `StandardConstellationWidget`，这样现有 grid stretch 行为不变。
- 对话框新增 `Constellation` tab，并复用同一个 `StandardConstellationWidget` 实例挂进去，避免出现 panel 和 dialog 两份预览样式漂移。
- 星座图的 modulationType 更新链继续保留在 `DigitalPanel`，只要 `m_constellationWidget` 还是同一个实例，迁移到 dialog 后无需改业务更新逻辑。

## 2026-07-13 I/Q 预览时间轴补记

### 范围与口径

- 仅修改数字调制 Waveform Viewer 的 `I/Q` 页 X 轴；频谱和星座图不变。
- I/Q 交织数据中的每一对 `I/Q` 视为一个复样点，首个复样点时间为 `0 s`。
- 对预览中的第 `index` 个复样点，X 值为 `index / sampleRate` 秒；因此终点为 `(previewComplexCount - 1) / sampleRate` 秒。
- 采样率直接使用本次数字波形生成结果对应的 `DigitalModulator::sampleRate()`，不从 FFT 点数或频率轴反推。
- X 轴刻度复用 `Adapters::TimeUnitAdapter` 的秒基准格式化逻辑，使短时间自动显示为 `ms`、`μs` 或 `ns`。

### 成功标准

- X 轴数据从样点序号改为秒，并保持 I/Q 数据与时间点一一对应。
- 起点刻度为 `0s`，终点刻度对应预览点数和采样率计算出的真实时间。
- 短时间刻度通过 `TimeUnitAdapter` 自动选择合适单位。
- 波形、频谱和星座图现有绘制与更新链不受影响。

### 验证级别

- `static`：检查采样率从 `DigitalModulation -> DigitalPanel -> DigitalSpectrumDialog` 的传递、时间公式、空数据/无效采样率处理和所有调用点一致性。
- 本次不主动编译或运行；如需运行时视觉确认，再使用现有 Debug 构建树验证。

### 2026-07-13 编译后显示修正

- 现场现象：I/Q 图能够看到与时间 ticker 对应的竖向网格线，但 X 轴没有任何时间文字。
- 已确认原因：I/Q 图初始化及主题刷新都调用了 `applyPreviewPlotStyle(..., false, ...)`，其中 `false` 会明确关闭 X 轴 tick labels。
- 修正边界：仅把 I/Q 图的 `showXAxisLabels` 改为 `true`；频谱图继续隐藏内部 X 轴标签并使用现有 footer。
- 成功标准：首次打开及切换主题后，I/Q 图都能显示包括 `0s` 和计算所得终点时间在内的 X 轴刻度，且现有自动 bottom margin 为刻度文字保留空间。
