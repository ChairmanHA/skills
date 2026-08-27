# 2026-05-20 HTRA Multitone Spectrum Preview Plan

## 目标

- 给 HTRA 的 Multitone panel 增加一个“显示波形预览”按钮。
- 点击后弹出非模态预览对话框，只显示 FFT 后的频谱图。
- 频谱结果完全沿用 Digital 预览的计算口径，不在 UI 层重新发明算法。
- 保持插件边界：HTRA 不直接依赖 Analog 插件内部类，而是在 HTRA 内部落一份本地实现。

## 本地假设

- 当前 `MultitoneGenerator` 已稳定产出最近一次 IQ 数据与采样率，并通过 `resultChanged()/statusChanged()` 暴露生命周期。
- 最小闭环是：
  - 在 `MultitoneGenerator` 生成完成时顺手计算并缓存频谱结果。
  - `MultitoneModulation` 监听 generator 的 `resultChanged()`，把最新频谱推给 `MultitonePanel`。
  - `MultitonePanel` 缓存频谱，并在按钮点击时展示一个仅频谱的非模态对话框。

## 计划改动

1. 扩展 `multitonegenerator.{h,cpp}`
   - 新增 `MultitoneSpectrumResult` 结构。
   - 复用 Digital 的 FFT/RBW/display 计算流程，为当前 IQ 构建频谱快照。
   - 提供 `spectrum() const` 读取接口，并在 `resultChanged()` 生命周期内更新。
2. 在 HTRA 插件内新增本地频谱预览对话框
   - 只做 Spectrum tab 对应的单页版本。
   - 复用 Digital 的 `QCustomPlot` 外观思路与 `SpectrumPlotPane` 结构，但文件保留在 HTRA 内。
3. 更新 `multitonepanel.{h,cpp,ui}`
   - 增加“Show Waveform”按钮。
   - 缓存最近一次 `MultitoneSpectrumResult`。
   - 按钮点击时展示/刷新对话框。
4. 更新 `multitonemodulation.{h,cpp}` 与 `src/plugins/htra/CMakeLists.txt`
   - 接上 `resultChanged()` -> panel 的频谱刷新。
   - 把新文件纳入构建，并补上 `QCUSTOMPLOT_USE_LIBRARY`。

## 最便宜的证伪检查

- 改完后只跑一次 Debug 编译：
  - `cmake --build d:\development\vsg2.0\build\cmake-win-debug --config Debug --target ALL_BUILD --parallel`
- 该检查足以暴露：
  - 新增文件未进 CMake
  - `QCustomPlot` 使用缺少编译定义
  - panel / modulation / generator 接口不匹配
  - Qt moc/uic 元对象错误

## 2026-05-20 补充：Multitone 采样率关系与参数约束调整

### 现状解释

- 当前 multitone generator 使用：

   `Fs = 2 * FreqSpacing * (Count - 1)`

- 对 `Count = 5, FreqSpacing = 10 MHz`，tone 集为 `-20, -10, 0, 10, 20 MHz`，因此：

   `Fs = 2 * 10 * (5 - 1) = 80 MHz`

- 预览频谱横轴按 `[-Fs/2, +Fs/2]` 绘制，所以自然看到 `[-40, +40] MHz`。

### 本轮结论

- 对当前对称 complex-baseband multitone，更直观也更贴近用户预期的关系是：

   `Fs = Count * FreqSpacing`

- 这样 odd/even tone lattice 都会在 Nyquist 边界外侧保留半个 spacing 的 guard：
   - odd `Count = 2m + 1` 时，最外 tone 在 `±m * spacing`，而 `Fs/2 = (m + 0.5) * spacing`
   - even `Count = 2m` 时，最外 tone 在 `±(m - 0.5) * spacing`，而 `Fs/2 = m * spacing`
- 如果采用业务上限 `100 MHz`，则参数约束可以直接写成：

   `Count * FreqSpacing <= 100 MHz`

### 计划改动

1. generator 采样率公式从 `2 * spacing * (count - 1)` 改为 `count * spacing`
2. generator 的 count / spacing clamp 规则改成围绕 `Count * FreqSpacing <= 100 MHz`
3. panel/business property metadata 同步更新当前可编辑 max，避免用户输完后再被被动改值

## 2026-05-20 补充：奇数 Count 频谱预览变直线

### 现象

- `Count = 3, FreqSpacing = 5 MHz` 时，预览频谱显示近似一条平线。
- even count 看起来正常，问题主要暴露在 odd count 的极短周期场景。

### 本地假设

- 当前 multitone 预览频谱仍沿用 digital 的“固定 1024 点 FFT，输入不足则补零”的逻辑。
- 对 odd count，当前 generator 常会收敛成非常短的一个精确周期；例如 `Count = 3` 时，一个周期只有 3 个 complex sample。
- 把这 3 点单周期数据直接补零到 1024 点做 FFT，会把“周期序列”误看成“一次性短脉冲”，因此频谱近似展平。

### 修复边界

- 只修正 multitone 的预览频谱构造。
- 预览 FFT 输入改为“按当前一个周期循环展开”后再进 1024 点分析窗，而不是对剩余点直接补零。
- 不改 generator 的实际 `iqData`、`sampleCount`、download/playback 语义。

## 2026-05-20 补充：按 VSG60 风格调整采样率下界与预览展示

### 采样率关系

- 用户要求把当前自动采样率从 `Fs = Count * FreqSpacing` 改成“至少满足”：

   `Fs >= 1.25 * Count * FreqSpacing`

- 同时仍受设备最大采样率 `125 MHz` 限制。
- 在当前“采样率由 generator 自动决定”的实现里，最小闭环就是直接取：

   `Fs = clampSampleRate(1.25 * Count * FreqSpacing)`

- 对应的参数联动上限变成：

   `1.25 * Count * FreqSpacing <= 125 MHz`

   也即 `Count * FreqSpacing <= 100 MHz`。

### 预览风格

- 当前 multitone 预览仍沿用 digital 风格，平滑偏重：`1024` 点 FFT、`16` 段平均、额外 video smoothing。
- 用户希望更接近 VSG60：
   - 显示 `Waveform Details`
   - 频谱线更尖锐，不要过度平滑
   - 视觉上更接近 VSG60 截图中的 analyzer 风格

### 本轮方案

1. 采样率公式改为 `1.25 * Count * FreqSpacing`
2. 预览频谱改为 multitone 专用口径：
    - `512` 点 FFT
    - `1` 段平均
    - 关闭 video smoothing
    - 不再做额外 RBW 邻 bin 平滑，只保留窗口本身的频谱形状
3. 在预览图左上角叠加 `Waveform Details` 信息框：
    - `Sample Rate`
    - `Modulation Type`
    - `Number of Samples`
    - `Length of Waveform`
    - `Average Power`
    - `Peak Power`
    - `PAPR`

## 2026-05-20 补充：Waveform Details 信息框不可见

### 现象

- 频谱预览窗口中 `Waveform Details` 信息框完全不可见。

### 本地假设

- 当前对话框用 `QStackedLayout::StackAll` 把 plot 和 overlay 叠放，但没有显式把 overlay 提到顶层。
- `QStackedLayout` 在 `StackAll` 模式下仍会把 current widget 置顶；若 current 仍是先加入的 plot，则 overlay 会被压在 plot 下层。

### 修复边界

- 只调整 `multitonespectrumdialog.cpp` 的 overlay 层级与可见性。
- 不改频谱数据、信息框内容来源或主题配色策略。

## 2026-05-20 补充：Waveform Details 默认右侧并支持拖动

### 现状

- 当前详情框虽然已经能显示，但仍由 overlay 内部布局固定在左上角。
- 当前没有任何拖动逻辑，用户无法在预览窗口里重新摆放信息框。

### 本地方案

- 取消由 layout 持续托管 `m_detailsFrame` 的位置，改为让 overlay 容器承载一个可手动定位的详情框。
- 默认停靠在 plot 区域右上角，并在窗口 resize 时继续保持右上角停靠。
- 当用户按下并拖动详情框时，切换为手动位置；后续 resize 只做边界夹紧，不再强制吸回默认点位。

### 修复边界

- 只改 `multitonespectrumdialog.{h,cpp}`。
- 不改 preview 数据内容与详情字段。

## 2026-05-20 补充：Multitone 当前实现知识库沉淀

### 目标

- 新增一篇 KnowledgeBase 文档，总结当前 htra multitone 的：
   - 参数模型与业务语义
   - tone lattice / notch / phase mode / sample count 生成逻辑
   - AutoScale 等价口径下的最终 int16 量化方式
   - 当前 preview 频谱的分析口径
   - 与 VSG60 的主要一致点和仍允许存在的差异
   - 当前采样率约束 `1.25 * Count * FreqSpacing <= 125 MHz`

### 输出边界

- 文档只描述当前代码路径与当前实现，不回顾已废弃的旧公式。
- 文档需要接入 `.github/KnowledgeBase/Index.md`，便于后续检索。