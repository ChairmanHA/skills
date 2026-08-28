# RF 切换期间 FFM 去重

## Scope

- 核对 HTRA 设备 open 阶段的 Low Power / `POWERON` 初始状态。
- 保证每次设备成功 open 后，第一次公共设备配置无条件执行一次 `tx_config_ffm()`。
- 后续公共设备配置（包括 RF 开关导致的 Mute/CW/Playback/Streaming 重配）先通过 `tx_query_ffm()` 获取当前 center/level；两者与目标值一致时跳过 `tx_config_ffm()`。
- query 失败时不把未知硬件状态误判为一致，继续执行 `tx_config_ffm()`。
- 不改变 RF pipeline 裁决、Low Power UI 映射、其它公共参数配置和 FFM writeback 语义。

## Assumptions and evidence to verify

- Low Power ON 继续表示 `POWEROFF`，OFF 继续表示 `POWERON`。
- `FancyDevice` 实例可能经历 close/reopen；首次 FFM 强制标记必须按每次成功 open 重置，而不能只依赖构造默认值。
- FFM level 的设备 API 使用 `float`，一致性比较应按设备返回/下发的同一精度口径处理，避免 double 到 float 的表示差异导致无效重配。

## Success criteria

1. 设备 open 成功路径明确请求 `POWERON`；Low Power policy/UI 仍保持现有平台默认和 ON/OFF 映射，不与 open 时的临时硬件上电状态混为一体。
2. 每次 open 后第一次调用 `FancyDevice::configuration()` 都执行 `tx_config_ffm()`，不先 query 跳过。
3. 第一次成功 FFM 后，后续配置在 query 成功且 center、level 均与目标一致时不调用 `tx_config_ffm()`。
4. center 或 level 任一不同，或 query 失败时，仍调用 `tx_config_ffm()`。
5. FFM warning/error handling、`m_txLevelUnlevelActive` 与最终 writeback query 行为保持有效。
6. 修改仅限当前 CMake 已包含的 HTRA 设备实现和必要的长期架构文档。

## Plan

1. 读取 `FancyDevice` open/close、power state、configuration/FFM 当前实现及 CMake inclusion。
2. 设计最小的 per-open 首次 FFM 状态与 query-then-config 判断。
3. 修改代码，并同步更新相关 KnowledgeBase 中已经过时的 open power/FFM 行为说明。
4. 进行静态检查：调用链、状态重置、错误/警告语义、diff 与工作区冲突检查。

## Verification level

`static`

- 不编译、不运行设备。
- 通过源码调用链、CMake inclusion、`rg` 和 `git diff` 验证。

## Implementation result

- 已确认并保留 `FancyDevice::open()` 的统一 `POWERON`：设备类型只设置 Low Power 策略/UI 默认值，不改变 open 阶段的硬件上电动作。
- 将原本只在整次 configuration 结束时清除的 `m_firstConfig` 收窄为 `m_forceNextFfmConfig`；每次 open 成功重置，首次 `tx_config_ffm()` 成功后立即清除。
- 新增 `configureFfmIfNeededLocked()`：
  - 首次配置直接调用 `tx_config_ffm()`；
  - 后续先调用 `tx_query_ffm()`；
  - center 使用 API 的 `double` 值比较，level 先收敛到实际下发的 `float` 精度再比较；
  - 两者一致时跳过 config，任一不同或 query 失败时继续 config；
  - 保留 `H2_WARNING_UNLEVEL`、`handleStatus()` 和失败重试语义。
- 已同步修正设备打开流程和 Device Settings / Low Power 知识库说明；未新增 KnowledgeBase 文件，因此无需修改索引。

## Static verification

- `src/plugins/htra/CMakeLists.txt` 已确认包含 `fancydevice.cpp/.h`。
- `rg` 已确认旧 `m_firstConfig` 不再残留，首次强制标记只在声明、open 重置、FFM 判断和成功清除处出现。
- `git diff --check` 通过；仅报告仓库既有 CRLF 转换提示，无空白错误。
- 未编译、未运行设备，实机仍需核对首次 open、Low Power 默认 ON/OFF、RF OFF/ON 且 center/level 不变、query 失败回退四类日志/API 次数。
