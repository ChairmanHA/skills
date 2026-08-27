# 2026-06-29 HTRA Multitone Even Mask FixedPlayback Center Offset

## Scope

- 针对 HTRA Multitone 的 `EvenCountCenterToneMask=true` 临时模式，补一个 FixedPlayback 设备中心频率偏移。
- 当 Multitone 的有效 `Count` 为偶数且 `EvenCountCenterToneMask` 打开时，Multitone provider 在 `TxProviderExecutionContext` 中请求 `FreqSpacing / 2` 的临时 RF center offset。
- `TxPipelineRuntime::applyFixedPlayback()` 只在 FixedPlayback base configuration 中把设备 profile center 写成 `logicalCenter + offset`。
- 波形本身仍由 Multitone generator 按 `EvenCountCenterToneMask=true` 生成，不在 runtime 中改写 IQ。

## Evidence

- `TxPipelineRuntime` 只消费 `TxApplyRequest`，不应回读 active business 或 HTRA generator。
- 当前 `TxProviderExecutionContext` 已是 Playback provider 到 runtime 的最小执行上下文，适合承载 provider 计算出的临时 offset。
- 当前 `MultitoneModulation` 已在构造时调用 `m_generator->setEvenCountCenterToneMask(true)`，本轮保留该现状。
- 若 runtime 直接把偏移后的 center 通过 `deviceConfigurationDone` 回写到 `CommonDeviceProfile`，下一次 refresh 会以偏移后 center 为输入并继续叠加 offset，因此本轮必须保持 writeback 的逻辑 center。

## Design

1. 在 `TxProviderExecutionContext` 增加 `fixedPlaybackCenterOffsetHz`，默认 `0`。
2. `MultitoneModulation::buildPlaybackExecutionContext()` 在初始化 provider context 后，如果 `evenCountCenterToneMask == true` 且 `count` 为偶数，设置：
   - `fixedPlaybackCenterOffsetHz = freqSpacing / 2`
3. `TxPipelineRuntime::applyFixedPlayback()`：
   - `fillDeviceProfile()` 后保存 logical center。
   - 若 provider offset 非零，则配置设备时使用 `logicalCenter + offset`。
   - emit `deviceConfigurationDone` 前把 writeback center 改回 logical center，避免 UI/profile 漂移。
   - 日志中同时打印 logical center、device center 和 offset。
4. 不影响 SweepPlayback；用户本轮只要求 FixedPlayback 临时规避。

## Success Criteria

- Multitone + FixedPlayback + even Count + `EvenCountCenterToneMask=true` 时，设备配置中心频率使用 `center + FreqSpacing / 2`。
- 其他 provider、odd Count、mask 关闭、SweepPlayback 均不发生中心频率偏移。
- CommonDeviceProfile / UI 仍保持用户输入的逻辑 center，不因本临时规避反复累加偏移。

## Verification Level

static

## 2026-06-29 Revision

- Later bench debugging showed the front-panel `Center` should reflect the temporary applied device center in this workaround.
- The runtime therefore no longer forces `writeback.center` back to `logicalCenter` before `deviceConfigurationDone`.
- This intentionally simplifies the workaround and removes the earlier anti-drift writeback guard; a later refresh while the same workaround is active can treat the shifted center as the next logical center and add the offset again.

- 本轮按仓库默认规则只做静态检查和 `git diff --check`。
- 如需要联机确认，再用现有 Debug build tree 编译并上机验证频谱。
