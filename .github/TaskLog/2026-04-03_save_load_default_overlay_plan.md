# 2026-04-03 Save Load Default Overlay Plan

## Problem
 结合新的 request/runtime 架构，最终采用更直接的语义：文件保存完整 authoring state，加载时先回到 default，再全量覆盖恢复，最后统一 orchestrate 一次。
- 当前 `loadSettingsFile()` 会在 `CommonDeviceProfile::restoreSettings()` 之后、business/sweep 还未完整恢复前，提前调用一次 `selectBusiness2Work()`，导致 pipeline 在半恢复状态下被裁决并下发。
- 当前保存格式把 sweep 仍塞在 `bussiness["Freq Level Sweep"]` 下，而且只保存了模拟 sweep profile，没有保存 ListMode authoring 数据和 sweep enabled 状态。
- 结合新的 request/runtime 架构，保存/加载更合理的语义应是：文件表示“相对 defaultprofile 的 authoring overlay”，加载时先回到 default，再覆盖恢复，最后统一 orchestrate 一次。
 为保存文件保留简洁的 schema 信息，重点用于区分当前完整保存格式。
  - `common` 直接保存完整公共配置。
  - `bussiness` 继续保存各业务自身完整 authoring profile。
  - sweep 改为独立 top-level `sweep` 对象，完整保存模拟 sweep、ListMode profile 以及 enabled 状态。
  - `bussiness` 暂时继续保存各业务自身 authoring profile；本次不扩散到所有业务类去提炼逐字段 defaultProfile。
  - sweep 改为独立 top-level `sweep` 对象，保存模拟 sweep、ListMode profile 以及 enabled 状态。
  - 再按文件中的完整内容覆盖恢复 common/business/sweep/选中页。
  - 先抑制 pipeline update，并先 `deactivate()` runtime。
  - 恢复结束后只调用一次 `selectBusiness2Work()`。
- 保持向后兼容：若旧文件没有 top-level `sweep`，仍兼容读取 legacy 的 `bussiness["Freq Level Sweep"]` 模拟 sweep 数据。
- `common` 中的 `LoMode` 也应与 Trigger/RefClock 一样统一为字符串持久化；加载时需兼容旧文件里基于 `unsigned int` 的历史格式。
- load 不再在中途状态触发 pipeline apply。
- 保存文件能够完整表达 sweep authoring state。
- 新保存格式语义明确：defaultprofile 是基线，文件只承载需要覆盖的运行 authoring 状态。
- 最终方案调整为：`save` 走全量保存，`load` 走事务式全量覆盖恢复。
- 仍保留当前已经修好的恢复顺序：先抑制 pipeline update 并 `deactivate()` runtime，再回到默认 authoring state，然后恢复文件内容，最后统一 `selectBusiness2Work()` 一次。
- 历史兼容保留到最小必要程度：旧的 legacy sweep 文件继续兼容；`LoMode` 兼容旧数值格式，但新文件统一写字符串。