# 2026-07-15 HTRA Multitone Notch Logical Center Fix

## Scope

- 修正 HTRA Multitone 在偶数 `Count` 且 `EvenCountCenterToneMask=true` 时的 `NotchWidth` 选音坐标。
- notch 继续表示“以界面逻辑 center 为中心的对称陷波宽度”。
- 不修改既有 tone lattice、FixedPlayback 设备 center offset、相位、采样率和波形合成架构。
- 更新现有 Multitone KnowledgeBase，记录 offset 模式下的 notch 坐标换算。

## Evidence

- `src/plugins/htra/CMakeLists.txt` 已将 `multitonegenerator.cpp/.h` 和 `multitonemodulation.cpp/.h` 纳入 HTRA 插件目标。
- `MultitoneModulation::buildPlaybackExecutionContext()` 在偶数 `Count` 且 mask 打开时请求 `fixedPlaybackCenterOffsetHz = FreqSpacing / 2`。
- `buildCandidateTones()` 在相同条件下把默认偶数 half-spacing lattice 左移 `FreqSpacing / 2`，生成整数倍基带频偏。
- 因此一根基带 tone 相对界面逻辑 center 的 RF 频偏为：

  `logicalOffset = requestedBasebandFrequency + FreqSpacing / 2`

- 当前 `applyNotch()` 直接使用 `abs(requestedBasebandFrequency)`，实际把 notch 围绕偏移后的设备 center，而不是界面逻辑 center。
- 例如 `Count=4` 时基带 lattice 为 `{-2Δf, -Δf, 0, +Δf}`，实际相对逻辑 center 的位置为 `{-1.5Δf, -0.5Δf, +0.5Δf, +1.5Δf}`；逻辑中心 notch 应成对移除基带 `-Δf` 与 `0`，现有算法只会优先移除 `0`。

## Assumption

- `NotchWidth` 的产品语义保持为“相对界面逻辑 center 的对称 RF notch”。依据是 offset workaround 的目标是保持最终 RF tone lattice 不变，只改变设备 LO center 与基带 lattice 的组合方式。

## Design

1. 为 notch 选择计算一个“基带 tone 相对逻辑 center 的显示/RF 频偏”：
   - 普通模式：`logicalOffset = requestedBasebandFrequency`
   - 偶数 mask 模式：`logicalOffset = requestedBasebandFrequency + FreqSpacing / 2`
2. `applyNotch()` 使用 `abs(logicalOffset) < NotchWidth / 2` 判断移除 tone。
3. `effectiveNotchWidth` 使用剩余 active tones 的最小非零 `abs(logicalOffset)` 计算，保持指标与 notch 的逻辑中心语义一致。
4. 基带 tone 自身的 `requestedFrequency` 不改，确保 IQ bin、设备 LO offset 和实际 RF tone 位置仍沿用既有链路。

## Success Criteria

- 偶数 mask 模式下，notch 围绕界面逻辑 center 对称选择 tone；例如 `Count=4`、足以覆盖中心一对 tone 的宽度会同时移除基带 `-Δf` 和 `0`。
- odd `Count`、mask 关闭和离散保留音模式的现有行为不变。
- `effectiveNotchWidth` 与新的逻辑中心坐标一致。
- 不新增跨 plugin 依赖，不把 runtime center 语义反向泄漏到 generator 之外。

## Verification Level

static

- 审阅 notch 坐标换算及调用边界。
- 使用静态枚举样例核对 odd/even、mask on/off 的 tone 选择。
- 运行 `git diff --check`；本轮按仓库默认规则不主动编译。

## Implementation Result

- `applyNotch()` 新增 `deviceCenterOffsetFromLogicalCenter` 输入，tone 删除顺序、删除判断和 `effectiveNotchWidth` 均统一使用逻辑 center 相对频偏。
- `selectActiveTones()` 只在 `EvenCountCenterToneMask=true` 且偶数 `Count` 时传入 `FreqSpacing / 2`；其余模式传入 `0`。
- 静态枚举验证了偶数 `Count=2,4,6,8,10` 在 mask 打开后的逻辑频偏与普通 half-spacing lattice 完全一致，并覆盖 notch 边界宽度与内部宽度。
- `Count=4`、`NotchWidth=1.1 * FreqSpacing` 的样例会正确移除基带 `-FreqSpacing` 和 `0` 两根 tone，保留相对逻辑 center 的 `-1.5 * FreqSpacing` 与 `+1.5 * FreqSpacing`。
- `git diff --check` 通过；未编译或运行程序。
