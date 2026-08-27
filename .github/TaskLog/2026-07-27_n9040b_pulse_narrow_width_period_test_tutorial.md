# N9040B 脉冲窄脉宽/短周期测试教程

日期：2026-07-27

## 目标

基于当前 HTRA Pulse 文档、当前被 CMake 纳入的实现，以及 Keysight N9040B / N9067C 官方资料，编写一份聚焦 N9040B“脉冲检测 / Pulse Analysis”功能、可直接执行的实机测试教程：

- 先在 model 122 实机上复测既有 `48 ns / 96 ns` 组合。
- 再按受控阶梯逐步缩小 Width 和 Period。
- 操作入口集中在 Pulse Detection、Time/Sample Rate、Regions、Pulse Table 和脉冲时域轨迹，不展开普通频谱、IQ Analyzer 或其他解调功能。
- 明确区分 SGStudio UI 参数、DUT Playback 采样率、N9040B 采集采样率和 N9040B IF/分析带宽。
- 给出每个推荐组合对应的当前代码计划、50% 阈值预期、仪器设置、通过判据、记录模板和异常排查顺序。

## 范围

- 只新增脉冲检测测试教程和 KnowledgeBase 索引项，不修改 Pulse 算法、设备能力解析或 UI。
- 验证级别：`static`。
- 不编译、不运行 SGStudio，也不代替用户执行 N9040B 实机测试。

## 观察

- `src/plugins/htra/CMakeLists.txt` 当前包含 `pulsemodulation.cpp`、`pulsemodulator.cpp` 和 `pulsepanel.cpp`。
- HTRA Playback 能力已改为按 `OPTION_BW_320M_TX` 选件分档，不再由 model 122 型号本身决定：
  - 无选件：连续到 125 MSPS、125 MiB。
  - 有选件：连续到 200 MSPS，加精确 400 MSPS 单点、1000 MiB。
- Pulse resolver 强制 `Width >= 6` 点、`Period >= 8` 点、`Width <= Period`，并将最终 Width/Period 量化到整数样点后永久回写。
- 含选件档的 `48 ns / 96 ns` 当前精确计划为 `187.5 MSPS、9/18 点`；无选件基线档才是 `125 MSPS、6/12 点`。
- 当前 Pulse 波形保留半幅边沿：上升和下降的 50% 点相隔约 `(widthSamples - 1) / Fs`，所以 N9040B 默认 50% Width 不应直接期待等于 UI Width。
- N9040B 的 255 MHz 宽带 IF/分析路径足以用于本轮宽带观察；但在用户给出的 255 MSa/s Pulse Analysis 采样率下，`15 ns / 20 ns` 只有约 3.8/5.1 个分析样点，适合极限功能探索，不适合作为高精度参数定标。

## 假设

- 被测 model 122 实机确实返回 `OPTION_BW_320M_TX`。若没有返回，SGStudio 会按保守基线档处理，教程中的 400 MSPS 极限组合不成立。
- 用户使用 N9040B 上的 N9067C Pulse Analysis（或菜单与指标等价的 Pulse Measurement 应用）。
- RF 频点、输出 PEP、外部衰减和线缆损耗由用户按实际台架填写；教程不假设固定载频或固定功率。
- N9040B 不同固件版本的菜单名称可能略有不同，教程同时给出功能名，避免依赖单一界面字样。

## 成功标准

- 教程先在 Pulse Detection 内给出 `1 us / 2 us` 长脉冲功率基线，再进入 `48 ns / 96 ns`，避免把链路损耗误判为 Pulse 算法问题。
- `48 ns / 96 ns` 的当前 122 含选件预期写成 `187.5 MSPS、9/18 点、50% Width 约 42.67 ns`。
- 连续档边界 `30 ns / 40 ns @ 200 MSPS` 和离散档边界 `15 ns / 20 ns @ 400 MSPS` 均有独立用例。
- 缩小过程使用精确落格组合，避免测试矩阵本身触发不必要的 UI 回写。
- 对 PRI、Width、Duty、Top Level、Base Level、Top/Base、rise/fall、droop、overshoot、ripple 给出明确记录和判断口径。
- 明确指出 N9040B 的采集采样率不是 DUT 的 Playback 采样率，255 MHz IF/分析带宽也不是 400 MSPS DUT 采样率的替代值。

## 静态验证清单

- [x] 教程中的能力档与当前 `htradevicecapabilityresolver.cpp` 一致。
- [x] 所有样点数、DUT Fs 和半幅边沿 50% Width 预期与当前 Pulse resolver/generator 一致。
- [x] 推荐矩阵全部满足 `Width >= 15 ns`、`Period >= 20 ns`、`Width <= Period` 和 6/8 点约束。
- [x] KnowledgeBase 索引包含新增教程。
- [x] 已执行 `git diff --check`；由于仓库 `.gitignore` 忽略整个 `.github/`，另对新增/修改 Markdown 执行了尾随空白和本地相对链接检查，均通过。

## 实施结果

- 新增 `n9040b_pulse_detection_122_narrow_pulse_test_guide.md`，操作范围只覆盖 N9040B Pulse Analysis / Pulse Detection。
- 教程固定了 Pulse Detection、Time/Sample Rate 和 Regions 的统一测量口径，特别说明 `Ignore Dropouts = 0`、关闭 Min Pulse Width 过滤、Voltage 域50% Width阈值及高占空比阈值调整顺序。
- 以 `1 us / 2 us` 建立长脉冲Top Level基线，并为 `48/96 ns` 给出当前含选件122的 `187.5 MSPS、9/18点、42.67 ns` 预期。
- 测试矩阵分别覆盖50% UI Duty阶梯、`30/40 ns @ 200 MSPS`连续档边界、`25/50 ns`首次明确400 MSPS切换点和`15/20 ns @ 400 MSPS`极限组合。
- 已更新 KnowledgeBase `Index.md`。
