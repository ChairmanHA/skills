# 2026-06-29 HTRA Multitone 偶数 Count 中心音遮盖开关

## Scope

- 在 HTRA multitone 中新增一个临时开关，控制偶数 `Count` 时是否把 candidate tone lattice 整体偏移 `-(FreqSpacing / 2)`。
- 默认关闭，保持当前与 SMA / VSG60 对齐的偶数 half-spacing lattice 行为。
- 开关打开且 `Count` 为偶数时，使原本最靠近中心的正频偏 tone 落到 `0 Hz`，用于临时遮盖实机中心频率本振泄露。
- 按用户补充要求，该临时开关完全限制在 `MultitoneGenerator`，不在 panel / modulation / property system 暴露 UI 入口。
- 离散音表格展示改为按频率从低到高排序，不再按靠近 DC 的正负交替顺序显示。
- 更新 KnowledgeBase，明确该行为是临时绕过，打开后会与 SMA / VSG60 不一致。

## Evidence

- `src/plugins/htra/CMakeLists.txt` 已把 `multitonegenerator.*`、`multitonemodulation.*`、`multitonepanel.*` 纳入 HTRA 插件目标。
- 当前 `buildCandidateTones()` 对偶数 `Count` 使用半间隔错位 lattice：`(..., -0.5Δf, +0.5Δf, ...)`，因此默认不包含 DC。
- 当前 `computeTargetMean()` 依赖 `tone.multiplier == 0` 判断合法中心 tone；所以临时偏移不能只改显示频率或最终 bin，必须让 lattice 语义中真正存在 `0 Hz` tone。
- 当前 `DiscreteKeepToneMode` 的表格显示排序按 `abs(f)` 接近 DC 优先；用户补充要求离散音模式下直接按频率从低到高显示。

## Design

1. 在 `MultitoneGeneratorProfile` 中新增布尔字段 `EvenCountCenterToneMask`，默认 `false`。
2. 在 generator 上新增对应 getter / setter / notify，并纳入 `toProfile()`、`applyProfile()`、`restoreSettings()`、worker profile snapshot。
3. 修改 `buildCandidateTones()`：
   - 默认行为不变。
   - 仅当 `profile.count` 为偶数且 `profile.evenCountCenterToneMask == true` 时，使用整数 multiplier `[-halfCount, halfCount - 1]` 生成 tone，使频率为 `index * FreqSpacing`；这等价于把默认偶数 lattice 整体偏移 `-(FreqSpacing / 2)`。
4. 修改 `computeFundamentalStep()`：开关打开的偶数 lattice 使用 `FreqSpacing` 作为 base unit，避免继续按半间隔基本步长求解。
5. 修改 `Count / FreqSpacing` 联动上限：开关打开且 `Count` 为偶数时，完整 lattice 的 sample-rate 下限按 `1.25 * Count * FreqSpacing` 收敛；默认关闭时仍按既有 `1.25 * (Count - 1) * FreqSpacing`。
6. 不改 `MultitonePanel` / `MultitoneModulation` 的开关链路；只把已有 tone table 排序改为按 `requestedFrequency` 升序。
7. 更新 `.github/KnowledgeBase/htra_multitone_current_algorithm_and_vsg60_boundaries.md` 的参数、lattice、sample-rate/fundamental-step、表格排序和差异边界说明。

## Success Criteria

- 默认设置和旧 profile 未显式保存该字段时，偶数 `Count=2` 仍生成 `{-0.5Δf, +0.5Δf}`。
- 开关打开且 `Count=2` 时，candidate tones 变为 `{-1Δf, 0}`；`Count=4` 时变为 `{-2Δf, -1Δf, 0, +1Δf}`。
- 打开开关后，中心 tone 被 `computeTargetMean()` 视为 wanted signal，不会被目标感知 DC residual 消除。
- 保存/恢复 profile 后，若 profile 中显式携带 generator 开关字段，开关状态保持；旧 profile 未携带时默认关闭。
- HTRA multitone UI 不出现该临时开关按钮。
- 离散音表按频偏从低到高显示。
- 文档明确默认关闭和与 SMA / VSG60 不一致的临时性质。

## Verification Level

static

- 进行静态代码检查和差异审阅。
- 本轮不主动编译；如用户要求编译，再按现有 Debug build tree 验证 HTRA 目标。
