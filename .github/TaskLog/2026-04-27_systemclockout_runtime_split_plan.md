# SystemClockOut Runtime Split Plan

## Goal

- 将 `SystemClockOut` 从 `CommonPanelProfile` 中彻底移除，不再作为 `Q_GADGET` 持久化字段存在。
- 保留 `CommonDeviceProfile::m_sysClockOut` 作为唯一运行时状态。
- 将 `CommonDeviceProfile::saveSettings()` / `restoreSettings()` 改成显式白名单序列化，移除按属性名跳过 `SystemClockOut` 的实现。

## Hypothesis

- 当前实现的根因不是 save/load 少跳过一处，而是 `SystemClockOut` 仍留在 `CommonPanelProfile` 这个“默认可序列化公共配置”结构里。
- 只要把它从 gadget 中移除，并让 save/load 只显式处理允许持久化的字段，就能消除字符串过滤和调用顺序依赖，同时保留 RefOut 的运行时保活语义。

## Scope

- 修改 `src/plugins/core/commondeviceprofile.h`
- 修改 `src/plugins/core/commondeviceprofile.cpp`
- 不改运行时行为边界：`SystemClockOut` 仍不参与 `Profile.json` 保存/恢复，仍通过运行时字段参与普通 apply。
- 本次不做编译，只做静态错误检查与代码路径核对。