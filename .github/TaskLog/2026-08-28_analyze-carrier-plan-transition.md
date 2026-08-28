# Carrier Plan 状态迁移与 `config_ffm` 最小修复分析

## Scope

- 静态分析 `Fixed / FScan / LScan / MScan` 在 core runtime 与 HTRA device 层的真实调用链。
- 明确 `tx_config_ffm()` 应由 carrier plan 状态迁移触发，而不是由每次 pipeline / 波形变化触发。
- 给出最小修复方案；本任务不修改产品代码、不构建、不运行。

## 已知需求

- `tx_config_ffm()` 与 `tx_config_fscan/lscan/mscan()` 分别代表 fixed 与 sweep carrier plan 的设备状态配置。
- 从任意 sweep carrier plan 转入 fixed carrier plan，必须再次调用 `tx_config_ffm()`，即使 center/level 没有变化。
- Mute 只改变 RF 输出状态，不主动改变 carrier plan。
- `Fixed -> Mute -> Fixed` 且 Fixed 参数未变时，不重复调用 `tx_config_ffm()`。
- fixed carrier plan 内部从 CW 切到数字调制/其他 Playback 等变化，不重复调用 `tx_config_ffm()`；仅真实 fixed 参数变化时才需要调用。
- 硬件保证 `POWER_OFF -> POWER_ON` 保留 carrier plan 配置，因此低功耗 Mute 不使 Fixed 状态失效。

## 分析计划

1. 检查 orchestrator、session、runtime/executor 的 pipeline 与 carrier plan 建模。
2. 检查 `FancyDevice::configuration()` 及 sweep API 对设备状态缓存/查询的处理。
3. 列出关键状态迁移矩阵，定位当前跳过 `tx_config_ffm()` 的错误条件。
4. 提出仅修改设备层判定所需的最小状态与失效点，并列出静态验证边界。

## 成功标准

- 方案能解释“关闭 sweep 后频谱仪仍继续扫”的根因。
- 方案同时满足 sweep -> fixed 必调、Fixed/Mute 间切换不重复、fixed 内波形变化不重复。
- 不通过 UI 页面状态推断设备态，不扩大到无关 runtime 重构。

## Verification Level

`static`

## 静态分析结论

### 观察

- Sweep enabled 关闭后，`MainWindow::syncTxSessionState()` 已正确把 carrier plan 注入为 `Fixed`；core 会构造 `FixedCw` / `FixedPlayback` 请求并执行，不是 UI 漏发 refresh。
- Fixed pipeline 最终通过 `IDevice::configuration()` 进入 `FancyDevice::applyCommonDeviceSettingsLocked()`；设备 API 没有单独的 stop-sweep 配置，切回 Fixed 的状态函数就是 `tx_config_ffm()`。
- `CW / Playback / Streaming` 是波形/基带来源（当前代码用 `BasebandProviderKind::None / Playback / Streaming` 表达），与 `Fixed / FScan / LScan / MScan` carrier plan 正交。
- 已回退后的工作区代码会在每次 `configuration()` 中无条件调用 `tx_config_ffm()`，因此功能正确但会在 Fixed 内部波形变化时产生重复调用。
- 此前提交 `59e1426b` 只用 `tx_query_ffm()` 的 center/level 判断是否跳过；它没有记录当前 active carrier plan。

### 根因推断

现场现象表明：设备处于 sweep plan 时，`tx_query_ffm()` 仍可能返回与目标 Fixed 相同的 FFM 参数。因而“FFM 参数一致”不等价于“当前 carrier plan 已是 Fixed”。只比较 center/level 会在 sweep -> fixed 时误跳过 `tx_config_ffm()`，扫描继续运行。

## 最小修复方案

只修改 `FancyDevice` 的设备 API 状态维护：

1. 恢复 `configureFfmIfNeededLocked()` 的参数判重，但增加一个由本对象维护的 carrier plan 状态（最小实现可用 `m_forceNextFfmConfig` 表示“当前不能确认是 Fixed”；更清晰的实现是 `Unknown / Fixed / FScan / LScan / MScan`）。
2. open/reset 后状态为 `Unknown`；第一次 Fixed 配置必须调用 `tx_config_ffm()`。
3. `tx_config_fscan/lscan/mscan()` 一旦成功，立即把状态记为相应 Sweep；即使随后 stream/playback/arm 失败，也不能仍声称 Fixed。
4. `tx_config_ffm()` 一旦成功，立即记为 Fixed；随后只有 center/level 改变或 query 失败才再次调用。
5. Mute 不再作为 FFM 强制条件：从 Fixed 进入 Mute 保留 Fixed carrier plan 已配置状态，随后 Mute -> Fixed 且 center/level 未变时自然跳过。
6. 如果进入 Mute 前设备处于 FScan/LScan/MScan，则仍因“当前 carrier plan 不是 Fixed”而调用一次 FFM；这不是 Mute 特例，而是 Sweep -> Fixed 状态迁移。
7. 不按 `TxPipelineKind` 或 `m_mode` 对 Fixed 内部的 `CW / Playback / Streaming` 切换触发 FFM，不加入 `channel_stop()`。

### 期望迁移

| 前态 | 新意图 | `tx_config_ffm()` |
| --- | --- | --- |
| Unknown | Fixed | 调用 |
| FScan/LScan/MScan | Fixed | 调用 |
| Fixed(A) | Fixed(A)，仅 CW/Playback/Streaming 变化 | 跳过 |
| Fixed(A) | Fixed(B)，center/level 变化 | 调用 |
| Fixed(A) | 进入 Mute | 跳过 |
| Mute/Fixed(A) | Fixed(A) | 跳过 |
| FScan/LScan/MScan | 进入 Mute（设备配置目标为 Fixed） | 调用 |

### 预计产品代码改动范围

- `src/plugins/htra/fancydevice.h`
- `src/plugins/htra/fancydevice.cpp`

不需要修改 core UI/session/orchestrator/runtime。

## 实施确认（2026-08-28）

- 用户确认 Power Off 后 carrier plan 由硬件保留。
- 采用最小布尔状态 `m_fixedCarrierPlanConfigured`：`false` 同时表示 Unknown 或当前不能确认是 Fixed；无需区分 FScan/LScan/MScan。
- Mute 和 Power Off 不修改该状态；open/reset 置 `false`，FFM 成功置 `true`，任意 Scan 配置尝试置 `false`。

## 实施结果

- 已在 `FancyDevice` 中实现 `m_fixedCarrierPlanConfigured` 和 `configureFfmIfNeededLocked()`。
- 已覆盖普通/Playback 共用的 FScan、LScan、MScan，以及 Streaming FScan、Streaming LScan 五个 Scan API 入口。
- Mute、CW/Playback/Streaming 切换和 Power Off 不主动失效 Fixed carrier plan。
- 保留 center/level query 判重；query 失败或参数不同会重新调用 FFM。
- 未恢复 `channel_stop()`。

## 静态验证

- `rg` 核对所有 `tx_config_ffm/fscan/lscan/mscan` 调用点及状态更新：通过。
- `m_firstConfig` / `stopChannelBeforeBusinessConfigurationLocked` 残留检查：通过（无残留）。
- `git diff --check HEAD -- src/plugins/htra/fancydevice.cpp src/plugins/htra/fancydevice.h`：通过。
- 按仓库默认静态验证策略，未构建、未运行设备联调。
