# PGA 状态栏设备摘要补齐

## Scope

- 在 Linux 树莓派平板使用的 PGA 状态栏布局中，复用普通桌面布局已有的吞吐量、USB 速率标识和六位设备 ID 展示。
- 不改变设备信息或实时状态的数据链路，不调整 Win32 布局。

## Observation / Inference

- 观察：`DeviceInfoWidget::buildPgaUi()` 已创建 `m_bandwidth`，`updateDeviceRealTimeStatus()` 也会更新吞吐量；现有逻辑仅在吞吐量大于 0 时显示。
- 观察：PGA 详情区目前只创建 API/GUI 版本控件，没有创建 `m_interfaceIndicator`、`m_interfaceSeparator` 和 `m_uid`。
- 推断：复用普通布局的控件创建方式后，现有 `updateDeviceInfo()`、`updateInterfaceIndicator()` 和断连清理逻辑即可自动驱动 U2/U3 与六位 ID。

## Success Criteria

- PGA 状态栏在设备连接后显示 U2/U3（或已有的其他接口摘要）和设备六位十六进制 ID。
- 吞吐量大于 0 时显示，等于 0、断连或连接中时隐藏。
- API/GUI、温度、电池、告警和 Win32 状态栏行为保持不变。

## Verification

- Level: `static`
- 检查 PGA 布局创建了现有更新函数依赖的控件，并确认所有可见性仍由既有连接态/实时状态逻辑控制。
- 检查差异只涉及本 TaskLog 和 `src/plugins/core/deviceinfowidget.cpp`。

## Result

- PGA 布局已补充接口摘要、接口分隔线和六位设备 ID 控件。
- 吞吐率控件初始隐藏，并继续由 `updateThroughputDisplay()` 在连接且吞吐率大于 0 时显示。
- 静态差异检查通过；未执行编译或运行验证。
