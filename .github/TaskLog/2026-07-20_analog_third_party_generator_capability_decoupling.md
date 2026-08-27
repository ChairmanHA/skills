# Digital / DSSS / OFDM 第三方 Generator 能力解耦

Date: 2026-07-20

## Scope

- 将 Digital Modulation、DSSS、OFDM 的设备采样率域、设备波形容量、设备切换回归决策迁移到各自 `*Modulation` 业务层。
- generator 只保留第三方算法参数归一化、生成调用、外部指针接管、payload 租约和结果计算；不保存或查询 `PlaybackCapabilities`。
- 允许 business 向 generator 提交设备无关的 generation plan，明确本次生成采用的采样率、算法长度以及预期 payload 布局。
- 保留 `packing.cpp` 的 legacy 配置入口供未迁移调用方使用，同时增加不含设备采样率/容量规则的算法配置入口。

## Observations

- 当前三个 generator 的头文件均直接包含 `core/devicecapabilities.h`，并保存 `m_playbackCapabilities`。
- 当前 oversample 选项、采样率回写、Digital/DSSS 生成长度、OFDM symbol count 容量收口以及设备切换复位均部分位于 generator。
- `Digital_Configuraion`、`DSSS_Configuraion`、`OFDM_Configuraion` 仍隐含 legacy 125 MHz 连续采样率规则；仅删除显式 capability 成员仍不能完成解耦。
- 第三方 API 返回连续 `int16_t` IQ 指针；generator 已通过大内存租约和 `PlaybackPayload::adoptExternal()` 接管，重构不得恢复第二份完整波形副本作为常态路径。

## Design Boundary

- Business resolver 输入：当前 UI/profile 与当前 `PlaybackCapabilities`；输出：永久回写后的安全 profile、可用选项和精确 generation plan。
- Generation plan 是算法执行合同，不表达设备型号或能力对象；generator 只校验 plan 与 profile、第三方返回长度的一致性。
- 参数编辑、profile restore、reset、面板重新生成和设备能力更新都必须先走 business resolver，再提交 plan。
- 设备切换后 Digital/DSSS 参数越界时关闭 Enabled 并恢复默认参数；OFDM 按容量收口 `symbolCount`，无解时才恢复默认参数。除既定 ARB 文件卸载场景外不新增弹窗。
- 400 MSPS 仍只作为精确离散点；自动归一化优先落在较小连续采样率档，不主动跳到 400 MSPS。

## Success Criteria

- `digitalmodulator.*`、`dsssmodulator.*`、`ofdmmodulator.*` 中不再出现 `PlaybackCapabilities`、`SampleRateDomain`、设备容量常量或 `setPlaybackCapabilities()`。
- 三个 business 在生成前计算并提交合法 plan；oversample 可用项由 business 根据设备域计算并刷新 UI。
- generator 在第三方分配前按 plan 的最终 payload 大小获取租约，并拒绝超出 plan 布局的第三方返回结果。
- `packing` 提供纯算法配置入口，generator 不再经由 legacy 125 MHz clamp 改写参数。
- 现有波形算法、第三方 API 参数、频谱/RMS 计算与 immutable payload 生命周期保持不变。
- 验证级别为 `static`：检查 CMake inclusion、调用链、generator 能力类型残留、文档一致性和 `git diff --check`；不编译、不运行。

## Planned Changes

1. 新增 Analog 内部的设备无关 generation plan 类型。
2. 拆分 packing 的算法归一化与 legacy 设备归一化入口。
3. 依次迁移 Digital、DSSS、OFDM generator，使 worker 仅消费 profile + plan。
4. 在三个 business 中实现 capability resolver、参数回写、选项刷新和设备切换策略。
5. 同步 `Waveform_Parameters_Constraints.md`、Digital 交互文档和多型号 Playback 设计文档。
6. 执行静态搜索与 `git diff --check`，记录未执行编译。

## Implemented Result

- 新增 `ExternalWaveform::GenerationPlan`，以 sample rate、可选 symbol length、预计第三方源长度和最终 payload 长度表达一次设备无关生成合同。
- 新增 business-only `GenerationResolver` 工具，统一 legacy fallback、连续低档优先归一化和最终 payload layout/容量校验。
- `packing` 新增 `Digital_AlgorithmConfiguration`、`DSSS_AlgorithmConfiguration`、`OFDM_AlgorithmConfiguration`；原有 `*_Configuraion` 保留为 legacy 125 MHz 包装入口。
- Digital、DSSS、OFDM 的 domain/capacity、离散选项、设备切换复位和 OFDM symbolCount 收口均已迁入对应 `*Modulation`。
- 三个 generator 已删除 `PlaybackCapabilities` 成员与 capability 查询接口；worker 仅快照 profile + plan，并在第三方分配前按 plan 最终字节数取得 `PlaybackPayloadReservation`。
- 第三方授权 signal object 的创建也改为由 business 注入无参数 factory；generator 不再包含 `deviceutils.h`，但仍负责合作方 object 的算法调用与释放。
- 第三方返回指针继续由 immutable `PlaybackPayload::adoptExternal()` 接管；只在不足设备最小下载点数的小波形补齐路径创建小型归一化 buffer，大波形常态路径不复制第二份完整 payload。
- 参数编辑、profile restore、reset、面板重新生成和 usable capability 更新均先经过 business resolver，再调用 `setGenerationPlan()`。
- 已同步三份 KnowledgeBase 文档，不新增 KnowledgeBase 文件，因此不修改 `Index.md`。

## Static Verification Result

- Analog CMake target 已列入 generation plan 与 resolver 新文件。
- 静态搜索确认 `digitalmodulator.*`、`dsssmodulator.*`、`ofdmmodulator.*` 中不存在 `PlaybackCapabilities`、`SampleRateDomain`、125/996 MiB 常量或 `setPlaybackCapabilities()`。
- 全仓搜索确认 Analog 业务中不存在对已删除 generator capability/estimate/options 接口的残留调用。
- Digital/DSSS 的 oversample options 均由 business 计算；精确 400 MHz 可保留，200~400 MHz 空洞优先回写连续低档。
- OFDM 在进入第三方 API 前已由 business 限低 `symbolCount`，Playback 与 Save IQ 共用同一 plan/capacity 门禁。
- `git diff --check` 无空白错误；仅报告仓库现有 CRLF 转换提示。
- 按本任务静态验证约定未编译、未运行，最终编译与硬件验证由用户执行。
