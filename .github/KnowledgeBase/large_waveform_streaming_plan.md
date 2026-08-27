# 大波形处理设计与实现报告

## 1. 背景与约束

当前系统仍有两条与大波形相关、但彼此独立的硬件边界：

1. 回放模式（板载内存回放）
    - 未返回 `OPTION_BW_320M_TX`：最大 `125 MiB`，采样率 `[195.3125 kHz, 125 MHz]`
    - 返回 `OPTION_BW_320M_TX`：最大 `1000 MiB`，采样率 `[195.3125 kHz, 200 MHz] U {400 MHz}`
    - 文件Playback及Digital/DSSS/OFDM、HTRA AM/FM/PM/Pulse/Multitone/Ramp/AWGN均已读取current capability并在生成前构建容量安全plan
2. Streaming 模式（USB 流式传输）
    - 限制：实时吞吐上限约 `62.5 MSps`
    - 能力：数据量基本不受 RAM 约束，但受持续吞吐、机器负载与文件格式边界影响

核心冲突仍然存在：当用户配置出的波形超过当前设备 `maxWaveformBytes` 时，设备下载路径无法直接承载；而自动切到 Streaming 又会把“下载”“切模式”“完整波形导出”三种不同意图混在一起，交互和实现都容易失真。

## 2. 当前方案总览

2026-05 之后，大波形处理已经收敛为 **trim-only / playback-first** 方案：

1. 当前大波形链路只保留“生成并按下载语义使用”的主路径。

## 3. 公共层实现

### 3.1 请求快照与取消语义

当前公共层只保留与“大波形截断请求”直接相关的最小机制：

1. `LargeWaveformRequestSnapshot`
    - 记录 `sampleRate`
    - 记录 `estimatedSize`
    - 记录触发参数名和值，供日志与回滚路径使用
2. `requestGenerateAndTrim(...)`
    - 生成新的 trim request id
    - 保存快照
    - 调用具体业务传入的 `applyParamFunc`，触发后台重生成
3. `cancelPendingLargeWaveformRequest()`
    - 由 `basicParamsChanged` 与 license 状态变化共同触发
    - 第三方生成不可中断，但旧请求意图会被丢弃，避免晚到结果在新参数上下文里继续生效

这里仍保留一个重要边界：`Adjust Params` / `Cancel` 路径不只是回滚数值，也应恢复 `currentUnit` 与 `displayText`，避免软键盘和按钮显示状态分叉。

### 3.2 下发路径与 trim 收口

公共层当前负责的是“把已有生成结果安全收口到设备下载语义”，而不是决定是否切到其他业务：

1. `TxProviderExecutionContext` 当前保存 `Core::PlaybackPayload` 不可变 handle，而不是按值保存 `QVector<int16_t>`；request/session/runtime 复制和 equality 都不复制或扫描 IQ 数据。
2. 文件 Playback（Quick Waveform、ARB Ordinary/IQS）直接写入唯一的最终 `int16_t` payload；Quick Waveform 已无完整 `QVector<float>` 缓存。
3. 已迁移的Digital/DSSS/OFDM及HTRA本地生成业务直接向execution context提供immutable `PlaybackPayload`；尚未迁移的legacy业务才保留兼容数据入口。
4. 若具体生成型业务没有提供原始 `int16` 数据，才回退到 `float -> int16` 的兼容转换路径；这条 legacy fallback 属于 Phase D 待迁移范围。
5. 文件路径和已迁移生成型业务均按current capability校验；`MAXDOWNLOADSIZE`只服务尚未迁移路径或无有效capability时的legacy fallback。
6. `normalizePlaybackPayloadForDownload(...)` 仍负责设备最小下载字数等兼容约束；新的一次物化文件路径在精确分配前先计算补齐后的最终字数。

因此，公共层现在的职责已经明确为：

- 管理待处理 trim 请求
- 取消过期请求
- 把最终 payload 收口到设备下载约束

### 3.3 1000 MiB 单驻留内存契约

Phase C 当前实现采用以下硬约束：

1. 允许第一次 `1000 MiB` 连续 `QVector<int16_t>` 分配成功，但绝不允许第二个同级大 payload 同时分配。
2. 超过 125 MiB 的 owned/external payload 都必须取得 Core 进程级独占租约；租约未释放时，新物化返回业务错误。
   - 对合作方 external generator（包括 Playback 生成和完整 Save IQ 导出），租约必须在调用生成 API、由其分配大内存之前取得；不能等指针返回后再检查，否则仍可能短暂出现两份同级大内存。
3. payload 在 builder 阶段唯一可写，发布后只暴露 `const int16_t *`，不允许触发 `QVector` detach。
4. `tx_download_waveform()` 已确认不会完整复制输入；同步调用返回后，runtime 立即释放共享 host storage。
5. request/cache 只保留 payload identity、offset、word count；同一设备/能力 revision 的参数-only reapply 复用设备端已下载波形，不要求 host 保留 1000 MiB。
6. Digital、DSSS、OFDM 合作方生成器已经通过 `PlaybackPayload::adoptExternal(pointer, wordCount, deleter)` 直接移交原始指针；其他合作方生成器后续迁移时沿用相同接口和独占租约。

### 3.4 DSSS / OFDM / Ramp 的当前容量边界

DSSS 已采用与 Digital 相同的“生成前按容量选择长度”机制：`symbolLength` 由 current `maxWaveformBytes` 反推，SPS 与采样率组合由 current domain 校验。OFDM 没有 `SymbolLength`，因此采用参数层门禁：

1. 先按 `ceil(FFTSize * (1 + GuardInterval / 100)) * symbolCount * 4` 保守估算完整 payload。
2. 若超过 current `maxWaveformBytes`，business 优先降低并永久回写 `symbolCount`，不弹出 trim 确认。
3. Playback 和 Save IQ 都在 `GenerateOFDMWaveform(...)` 前再次执行同一容量检查；超限时不调用生成 API。
4. DSSS/OFDM 均在生成前取得大内存租约，并直接接管合作方指针；下载后释放 external owner。

Ramp也已接入current capability和唯一payload：business由Span解析最低合法采样率，再以`maxWaveformBytes / bytesPerComplexSample / Fs`计算动态Period上限；低采样率允许超过1秒，高采样率自动缩短。Period与SweepTime量化为整数样点plan，生成前通过layout硬门禁并取得builder/租约，不依赖runtime trim，也不建立第二份完整IQ。

## 4. Digital 特化收敛

### 4.1 交互收敛到 trim-only

Digital 当前的大波形路径已经完全建立在 trim-only 基线上，而不是在一个仍存在的 handover 系统上做局部覆盖：

1. 面板提供 `Default Trim Download` 复选框，用于声明默认策略。
2. 复选框勾选时，`PN` / `Oversample` 超限会直接走 `requestGenerateAndTrim(...)`。
3. 复选框取消勾选时，Digital 弹出专用确认框；确认后才继续生成截断波形，取消则回滚当前编辑值。
4. 如果当前缓存已经是 trimmed 结果，而用户之后关闭默认 trim 开关：
    - 当前已使能时，立即再次确认；取消则关闭当前 Digital 使能态
    - 当前未使能、但随后尝试重新使能时，也会再次确认
5. 上述“继续沿用已有 trimmed 缓存”的路径不会重新生成波形。
6. Trim 完成后不会自动点亮 Digital，也不会自动切到 `Streaming`；若业务当前未激活，只保留生成结果缓存。

完整导出/Save IQ 仍应视为独立意图，不应复用“下载截断缓存”的产品语义。

### 4.2 `SymbolLength` 如何根据当前设备容量收口

Digital 当前不是“先完整生成，再在下载时硬裁 payload”，而是 **在调用算法库之前** 就把生成长度控制到下载上限以内。

完整序列长度：

$$
fullSymbolLength = 2^{PN}
$$

每个 symbol 占用字节数：

$$
bytesPerSymbol = effectiveSamplesPerSymbol \times 4
$$

其中乘以 $4$，是因为交织 IQ 的 `int16 + int16` 每个复采样点固定占 4 字节。

trim 生成时实际使用：

$$
cappedSymbolLength = \left\lfloor \frac{maxWaveformBytes}{bytesPerSymbol} \right\rfloor
$$

再把结果钳制到：

$$
1 \le cappedSymbolLength \le fullSymbolLength
$$

最终语义是：

1. 普通生成使用完整 `2^PN`
2. trim 生成使用 `cappedSymbolLength`

其中未返回 `OPTION_BW_320M_TX` 时 `maxWaveformBytes` 为125 MiB，返回该选件时为1000 MiB。这样可以保证提示大小、实际生成大小和最终下载大小使用同一套口径；恰好等于上限合法。

### 4.3 FSK 为什么必须单独估算

普通数字调制通常可近似按下面关系估算采样率：

$$
F_s = R_b \times sps
$$

但 FSK 还要满足频偏带来的最小采样率约束：

$$
F_s \ge \max(R_b \times sps,\ 4 \times (R_b + MaxDF))
$$

因此 FSK 的每符号真实样点数应按下面口径估算：

$$
effectiveSamplesPerSymbol = \max(sps, \frac{F_s}{R_b})
$$

这也是当前实现刻意让下面三处保持一致的原因：

1. Digital capability-aware size estimator 用 `max(sps, Fs / Rb)` 估算 FSK 大小
2. `selectWaveformSymbolLength(...)` 用同样口径反推 `SymbolLength`
3. `GenerateDigitalModWaveform(...)` 使用该裁剪后的 `SymbolLength` 生成 trimmed 数据

否则就会出现“提示不超限、真实生成后却超限”的口径漂移。

### 4.4 Digital Playback / Preview 内存管理硬约束

后续维护 Digital 大波形路径时，下面几条约束不能再被破坏：

1. 单一主缓存原则
    - full-size 主缓存收敛在 `DigitalModulator::m_payload`
    - payload 直接采用合作方 `GenerateDigitalModWaveform()` 返回的连续 `int16` 指针，并以 `GenSignalObjRelease()` 作为 release callback
    - Playback、Save IQ、Preview 不应各自长期持有第二份 full-size 副本
2. Preview 只保留小片段
    - `DigitalPanel` 和 `DigitalSpectrumDialog` 只持有分析/显示所需的小片段
    - 不能为了 UI 方便恢复整份 waveform cache
3. 主链路直接复用原始 `int16`
    - 下载与完整缓存保存直接读取同一 `PlaybackPayload` 的 `const int16_t *`
    - `AnalogPlaybackBusiness` 中的 `float -> int16` 只应作为旧业务 fallback
4. 允许的受控例外
    - 极短波形可在临时下载缓冲中按 `minimumWords` 做有限 padding / repeat
    - 但不能把这种例外扩展成 full-size waveform 的长期副本

明确禁止的回退方式包括：

1. 在 UI / Business 层长期保存整份 waveform 副本
2. 在 Digital 主路径重新引入全量 `int16 -> float -> int16` 往返转换
3. 对共享的 full-size `QVector<int16_t>` 做可写操作，导致新的大块 detach 长期驻留

设备切换时，Digital 若仍处于合法采样率组合，会按新设备125/1000 MiB容量重新生成；若当前组合在新domain中失效，则关闭使能并整组静默reset。该收口不弹窗。

## 5. 历史说明

此文档仍保留原文件名 `large_waveform_streaming_plan.md`，是因为仓库里早期确实讨论并实现过一版 Generate & Stream 方案；但从 2026-05 起，这条路径已经作为过时复杂交互被整体清理。今后维护大波形行为时，应以本文描述的 **trim-only 当前基线** 为准，而不再以 handover 设计为默认前提。
