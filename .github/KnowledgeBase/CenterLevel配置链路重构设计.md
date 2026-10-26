# Playback Center/Level 在线更新：配置链路重构设计

## 0. 当前结论与证据

本阶段只覆盖**固定载波 Playback** 的 FFM 在线更新。Center 和 Level 在 H2 API 中由同一个 `tx_config_ffm()` 配置，因此当前快捷路径将两者作为一个 FFM 参数组处理。

代码和联机测试确认：

- `configuration()` 的完整路径会配置 clock、LO、RF port、FFM、trigger out，并在 Playback 启动阶段配置 stream。
- 首次 Playback 或需要重新建立状态时，仍执行完整的波形驻留、`tx_config_stream`、`tx_config_output`、`channel_start` 和必要的 BUS trigger。
- 固定载波 Playback 已经运行、波形驻留可复用、触发次数为无限循环，且只有 Center 或 Level 改变时，可以进入 FFM-only 路径。
- FFM-only 路径执行 `tx_config_ffm()` 和 FFM 回读；保留已驻留波形、`tx_config_stream` 与 `channel_config_trigger`，然后补执行 `tx_config_output`、`channel_start` 和必要的 BUS trigger。
- 联机测试表明，只执行 `tx_config_ffm()` 时波形可能消失；补回输出 arm 路径后波形能够恢复。因此当前实现把输出重新 arm 作为 H2 的必要步骤。

`tx_config_ffm()` 运行期间的实际幅度瞬态、相位连续性和内部停顿时间仍属于设备行为，需要联机测量；软件侧只能减少主动执行的冗余配置。

## 1. RF 参数域与重配代价

| 参数域 | 典型硬件动作 | 当前 Playback 处理 |
|---|---|---|
| **FFM Center / Level** | 合成器调谐、衰减器或数字增益更新 | 固定载波 Playback 满足准入条件时走 FFM-only |
| LO Mode | 合成器架构切换或重新锁定 | 完整配置 |
| RF Port | 端口继电器或射频路径切换 | 完整配置 |
| Ref Clock / Clock Out | 参考时钟或 PLL 状态变化 | 完整配置 |
| Trigger Out | 路由、极性或触发输出配置 | 完整配置 |
| Mode / 波形 / 采样率 / Trigger 参数 | 基带数据路径或触发状态机变化 | 完整配置并重新 arm |
| Power（Low Power） | 整机上下电 | 完整配置；清除驻留和应用快照 |

VSG60 等设备也会把波形、射频参数和 Streaming 事件分层处理，但各厂商对运行中配置变化的暂停、重启或延迟应用语义并不完全相同。本设计以 H2 的实际调用结果为准。

## 2. 核心设计：已应用快照 + 差量计划

### 2.1 已应用快照

`FancyDevice` 持有固定载波 Playback 的已应用快照。快照只在完整 Playback apply 成功并完成 arm 后有效；FFM-only 成功后更新 Center、Level 和回读结果。

```cpp
// fancydevice.h（示意）
struct PlaybackApplySnapshot {
    Profile requested;
    Profile writeback;
    bool armed = false;
};
std::optional<PlaybackApplySnapshot> m_playbackApplySnapshot;
```

以下状态必须清除快照，使下一次配置回到完整路径：

- `open()`、`close()`、`closeForSwitch()`、`resetDevice()`、连接丢失；
- 进入掉电状态或清除波形；
- 任意 FScan、LScan、MScan 启动；
- 完整配置或启动过程任一步失败；
- 波形、采样率、模式、触发或其它公共参数发生变化。

### 2.2 FFM-only 准入条件

当前实现只对固定载波 Playback 开放，不对 Streaming、CW 或扫描路径改变行为。必须同时满足：

- 当前模式和输入模式都是 Playback；
- 设备已上电；
- 已有有效且已 arm 的 Playback 快照；
- 本次请求允许复用驻留波形；
- `triggerCount == -1`，即无限循环；
- Center 或 Level 至少一个改变；
- sample rate、时钟、LO、RF port、Trigger source/action/edge、Trigger out 等其它 Profile 字段全部未改变。

伪代码：

```cpp
if (!m_playbackApplySnapshot || !m_playbackApplySnapshot->armed
    || !m_fixedCarrierPlanConfigured
    || input.mode != PlaybackModeId
    || !input.preservePlaybackWaveforms
    || input.triggerCount != -1) {
    return ApplyScope::Full;
}

const Profile &applied = m_playbackApplySnapshot->requested;
const bool ffmChanged = applied.center != input.center
                     || applied.level != input.level;
return ffmChanged && allOtherPlaybackFieldsEqual(applied, input)
       ? ApplyScope::PlaybackFfmOnly
       : ApplyScope::Full;
```

### 2.3 FFM-only 执行路径

```cpp
bool FancyDevice::applyPlaybackFfmOnlyLocked(const Profile &input,
                                              Profile *writeback,
                                              QString *errorMessage)
{
    tx_config_ffm(primaryTxChannel(), input.center,
                  static_cast<float>(input.level));
    tx_query_ffm(primaryTxChannel(), &center, &level);
    armConfiguredOutputLocked(PlaybackModeId, ...);
    // 更新快照和 writeback，并标记 PlaybackFfmOnly
}
```

相对完整路径，当前快捷路径省略：

| 省略的调用 | 原因 |
|---|---|
| `device_config_clock` | 参考时钟未变 |
| `channel_config_lo_mode` / `channel_config_port` | LO 和 RF 路径未变 |
| `device_config_trigger_out` | Trigger out 未变 |
| `tx_clear_waveform` / `tx_download_waveform` | 波形仍驻留且允许复用 |
| `tx_config_stream` | Playback stream 参数未变 |
| `channel_config_trigger` | Trigger 配置未变 |

当前仍保留：

- `tx_config_ffm`；
- `tx_query_ffm`；
- `tx_config_output`；
- `channel_start`；
- BUS 触发源下的 `channel_bus_trigger`。

### 2.4 调用方行为

`Profile::ApplyScope::PlaybackFfmOnly` 是配置结果的回写语义。Core Playback executor 识别该结果后：

- 发布设备回写值；
- 不重新上传波形；
- 不调用完整的 `triggerStart()`，避免再次执行 `tx_config_stream` 和 `channel_config_trigger`。

完整配置和普通 Playback 启动仍沿用原有链路。

## 3. 行为边界与风险

1. **无限循环 Playback**：Center 或 Level 修改进入快捷路径；波形 payload 和 stream 配置保持驻留，输出路径重新 arm。
2. **有限次数 Playback**：继续走完整路径，保持原有重新触发语义。
3. **外部触发或 XPPS 等待态**：FFM-only 不改变触发配置，重新 arm 后仍等待原触发源。
4. **H2 内部停顿**：即使软件不重配 stream，`tx_config_ffm()` 内部仍可能产生短暂停顿、幅度瞬态或相位扰动，必须通过设备测量确认。
5. **失败处理**：FFM-only 或重新 arm 失败时清除快照和固定载波状态，下一次请求回到完整路径。

## 4. 后续阶段：固定 CW / Mute 的 FFM-only

当前实现只开放固定 Playback 的 FFM-only。长期目标是把 Center/Level 的快捷配置抽象成统一的 FFM-only 能力，根据当前模式选择必要的收尾动作：

```text
FfmOnly
    ├── Fixed Playback：保留驻留波形和 Playback stream，重新 arm 输出
    ├── Fixed CW：保留 TX_CW stream，重新 arm 输出
    └── Mute：RF 关闭时更新或记录 FFM，不启动输出
```

### 4.1 Fixed CW

CW 与 Playback 使用相同的 FFM 和触发参数体系，外部触发、BUS 触发、有限次数和等待态并不是模式差异。CW 后续可以在已 arm、仅 Center/Level 改变且其它 Profile 字段不变时：

- 执行 `tx_config_ffm` 和 FFM 回读；
- 跳过 clock、LO、RF port、trigger out、`tx_config_stream(TX_CW)` 和 `channel_config_trigger`；
- 按 H2 实测结果决定是否执行 `tx_config_output`、`channel_start` 和必要的 BUS trigger；
- 外部触发源继续保持等待，不主动发送 BUS trigger。

第一版仍建议只对无限运行态开放。有限触发模式需要确认修改 FFM 后是否会重新开始完整响应次数，确认前继续走完整路径。

### 4.2 Mute

Mute 需要区分“上电但 RF 关闭”和“自动低功耗导致设备掉电”：

- **上电静音**：可以研究直接更新 `tx_config_ffm`，不执行输出 arm；因为当前 RF 已关闭，Center/Level 只作为下一次输出的准备值。
- **自动低功耗掉电**：设备已没有可调用的 FFM 硬件状态，不能把请求直接当作硬件已应用。应保存待应用的 Center/Level，在下一次上电或退出 Mute 时再下发并回读。
- Mute 路径不能复用 Playback 的“波形驻留有效”判断，也不应为了更新 FFM 重新打开 RF 输出。

### 4.3 统一实现边界

后续可以把当前 `PlaybackFfmOnly` 的模式专用快照扩展为固定输出 FFM 快照，至少记录：模式、电源状态、是否已 arm、请求值和硬件回读值。动作仍按模式分开，避免把 Playback 的波形条件套到 CW 或 Mute。

这部分需要先完成 H2 联机验证，再决定是否把 `ApplyScope::PlaybackFfmOnly` 重命名为通用的 `FfmOnly`。

## 5. 后续阶段：LScan / FScan 参数优化（本阶段不实现）

当前 LScan、FScan 启动和参数更新继续走现有完整扫描流程。本阶段只记录可行方向，不改变扫描代码。

### 5.1 候选语义

- **LScan 中普通 FFM Level**：扫描实际 Level 由 start/stop/step 计划产生。普通 FFM Level 可以作为“非当前活动参数”，不立即下发 `tx_config_ffm`，而是记录为退出扫描后待应用值。
- **FScan 中普通 FFM Center**：扫描实际 Center 由 frequency 计划产生。普通 FFM Center 可以延迟到退出扫描或回到 Fixed Playback 时应用。
- **活动维度不能跳过**：LScan 中修改 Center、FScan 中修改 Level，或修改扫描 start/stop/step/dwell，都可能改变当前扫描计划，应继续走重配/重新 arm。

这里应使用“延迟应用”而不是静默忽略：UI 和回写状态需要明确区分“用户请求值”和“当前扫描硬件实际生效值”。

LScan/MScan 下 Level 与 RMS 的延迟应用方案已在 [RMS/PEP 配置方案](rms_pep_simd_cross_platform_design.md) 第 7.1 节给出：请求 base level 由扫描计划导出，回写不覆盖 Level 属性。

### 5.2 后续阶段的验证条件

实现前需要确认 H2 在扫描运行时不调用 `tx_config_ffm` 是否能保持当前扫描稳定，并验证：

- LScan 修改普通 Level 不重配扫描、不停止当前输出；
- FScan 修改普通 Center 不重配扫描、不停止当前输出；
- 退出扫描或切回 Fixed Playback 时，延迟值能正确应用；
- 修改活动扫描维度仍然触发必要的重配和重新 arm；
- UI 回写不会把未应用的普通参数误报为当前硬件值。

## 6. 当前状态

- **已实现并测试方向**：固定 Playback 的 Center/Level FFM-only。
- **当前 H2 测试变体**：`tx_config_ffm` 后重新执行输出 arm，但不重新配置 Playback stream。
- **未实现**：Fixed CW、Mute、LScan-only-level、FScan-only-center 的 FFM-only 或延迟应用优化。
- **验证级别**：代码和文档静态检查；RF 内部中断和切换时间仍需联机测量。
