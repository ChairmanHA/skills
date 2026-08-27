# Phase 2 + 3 实施计划

## 变更概览

### Phase 2: ProgrammedArb bridge request 缓存化

**arbmodulation.h**:
- 新增 `setBridgedApplyRequest()` override
- 新增 `m_bridgedCommon` (TxCommonSettings) + `m_hasBridgedRequest` 成员
- 新增 `fillDeviceProfileFromBridgedRequest()` 辅助方法

**arbmodulation.cpp**:
- `setBridgedApplyRequest()`: 缓存 `request.context.common`，标记 `m_hasBridgedRequest = true`，触发 `onDeviceProfileChanged()`
- `configurationDevice()`: 
  - ProgrammedArb 模式下使用 `m_bridgedCommon` 填充 profile 而非 `getCurrentProfile()`
  - OrdinaryWav 保持原有 `getCurrentProfile()` 路径（core-managed 不走此路径）

**mainwindow.cpp**:
- `applyResolvedPipeline()` legacy-managed 路径中：
  - 当 pipeline 是 FixedPlayback 且 provider 是 Playback 且 `!providerParticipating` 时（即 ProgrammedArb bridge 场景）
  - 像 streaming 一样调用 `setBridgedApplyRequest(request)` 并支持 requestChanged 时跳过 terminate/reactivate

### Phase 3: 多 waveform playback MSCAN

**deviceoperator.h/.cpp**:
- 新增 `startPlaybackListSweep(startSeqNum, endSeqNum, points, repeat)`

**fancydevice.cpp**:
- `startListSweepLocked()` playback 分支改为支持多 waveform：
  - 遍历 `[startSeqNum, endSeqNum]` 收集 waveformId/repeat/sampleRate 数组
  - 调用 `tx_config_playback()` 传入多 waveform

**arbmodulation.cpp**:
- `downloadDataAndStart()` 实现：
  - OrdinaryWav: 单 waveform 下载 + triggerStart
  - ProgrammedArb: 多 waveform 下载 + 构建 MScan 点表 + startPlaybackListSweep

## 关键设计决策

1. **sampleRate 统一从 UI 读取**：ProgrammedArb 的 sampleRate 暂时和 OrdinaryWav 一致，来自 `m_arbDataGeneRator->arbSampleRate()`
2. **bridge 路径判定**：MainWindow 不需要知道 ArbFileMode，只需检查 `providerParticipating == false` + `providerKind == Playback` 即可识别 ProgrammedArb bridge 场景
3. **点表来自 dataForDownload**：每个 `dataForDownload` 的 `freq/powdBm` 加上推导的 `dwellTime` 构成一个 MScan 点
4. **dwellTime 推导**：`waveformDuration = samples / sampleRate`，`dwellTime = waveformDuration * repetition`
