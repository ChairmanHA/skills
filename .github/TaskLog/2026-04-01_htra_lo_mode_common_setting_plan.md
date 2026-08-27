# HTRA 本振模式纳入公共发射参数计划

## 需求理解

- H2 API 新增 `channel_config_lo_mode(channel* ch, lomode mode)`。
- 该调用应先于 `tx_config_ffm()`，并作为射频发射链路的公共参数管理。
- 当前阶段只要求打通“公共设置 -> 设备配置”的通路，不做 UI、不做设备查询回写。

## 设计判断

- 从射频发射机语义看，`LO mode` 属于 RF 本振合成策略选择，影响的是载波生成特性，而不是某个具体业务页面或某种基带来源。
- 它和 `Center/Level/Trigger/RefClock` 一样，属于 pipeline-agnostic 的 TX common setting。
- 因此应进入以下公共模型：
  - `CommonDeviceProfile`
  - `TxCommonSettings`
  - `IDevice::Profile`
- 但它不是所有设备必然具备的通用能力，因此当前先以 `Utils::Id` 在 Core 层承载，不把 HTRA 的 `lomode` 数值枚举直接泄漏到 Core 接口。

## 本次落地范围

1. 在 Core 公共 profile / runtime context 中新增 `loMode` 字段，默认值为 `Auto`。
2. 在 `FancyDevice` 中建立 `Utils::Id -> lomode` 映射。
3. 在 `FancyDevice::applyCommonDeviceSettingsLocked()` 中，于 `tx_config_ffm()` 之前调用 `channel_config_lo_mode()`。
4. `writeback` 不做硬件查询，但会把输入值原样带回，避免成功配置后丢失该公共字段。

## 明确不做

- 不增加 UI 控件。
- 不增加 feature spec / enumDisplayOptions 展示。
- 不调用 `channel_query_lo_mode()` 做真实回读。
- 不扩展设备切换后的显示/回显逻辑到“真实设备状态”。

## 影响文件

- `src/plugins/core/commondeviceprofile.h`
- `src/plugins/core/commondeviceprofile.cpp`
- `src/plugins/core/coreplugin.cpp`
- `src/plugins/core/ibusiness.cpp`
- `src/plugins/core/idevice.h`
- `src/plugins/core/mainwindow.cpp`
- `src/plugins/core/txpipelinestate.h`
- `src/plugins/core/txpipelineruntime.cpp`
- `src/plugins/htra/fancydevice.h`
- `src/plugins/htra/fancydevice.cpp`