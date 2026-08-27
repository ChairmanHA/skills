# 2026-06-08 RefClock Open Cleanup And GNSS CommonProfile Doc

## Problem

- `FancyDevice::open()` 当前仍会在设备刚打开时执行一次 `device_query_clock()` + `device_config_clock()` 预热，但参考时钟本来已经属于 `CommonDeviceProfile -> runtime/device configuration` 的统一链路。
- 这段 open 期参考时钟预热与后续 common profile apply 属于重复路径，容易混淆“谁负责把参考时钟真正配到设备上”。
- GNSS 已经被收敛到 `CommonDeviceProfile` 做默认值/持久化缓存，但它的设备下发并不通过普通 `IDevice::Profile` 的 common runtime 配置链；这一设计边界需要在 KnowledgeBase 中明确说明。

## Local Analysis

- `FancyDevice` 内部已有 `source_query/refFreq_query/clockOut_query` 默认值（Internal / 100 MHz / Off），删除 open 期 query/config 之后，后续 common profile apply 仍可从稳定默认值起步。
- 参考时钟真正的设备配置仍会在 `FancyDevice::configuration()->applyCommonDeviceSettingsLocked()` 中执行；设备打开后的 runtime signal 也会自然走到这条链，不依赖 open 内的预热 query/config。
- GNSS 虽然放在 `CommonDeviceProfile` 中，但只是“默认值/保存恢复/ preset 缓存 owner”；它的设备下发是通过 `CommonDeviceProfile::applyGnssSettingsToCurrentDevice()` 直接走 `IDevice::configureGnssSettings()`，不是 `IDevice::Profile` 那条普通 common settings pipeline。

## Fix Plan

- 删除 `FancyDevice::open()` 中的 `device_query_clock()` / `device_config_clock()` 预热，仅保留风扇默认设置。
- 更新 KnowledgeBase：
  - 在 GNSS 文档中明确 `CommonDeviceProfile` 只是 GNSS 缓存 owner，而不是把 GNSS 并入 `IDevice::Profile` 的统一 runtime apply。
  - 在参考时钟文档中说明 open 期不再预热参考时钟，真正配置由 common profile/runtime 链承担。

## Expected Result

- 设备 open 阶段不再重复配置参考时钟。
- 参考时钟职责边界更清晰：open 只做必要初始化，common profile/runtime 负责配置。
- GNSS “在 CommonDeviceProfile 中持久化，但不走同一条 runtime 配置链”的设计被文档化，后续修改时不再混淆。