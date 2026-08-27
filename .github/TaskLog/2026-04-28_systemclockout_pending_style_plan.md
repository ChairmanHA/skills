# SystemClockOut Pending Style Plan

## Goal

- 修正 Device Settings 中 `RefOut/SystemClockOut` 按钮在延迟回读等待期内的视觉样式。
- 保留按钮的 `checked` 业务语义和当前绿色高亮，不再因为等待 5 秒回读而落入 disabled 灰态。

## Local hypothesis

- `systemClockOutBtn` 和 `triggerOutStateBtn` 在深浅主题里已经共用同一组对象名级别 QSS。
- 当前视觉差异来自 `DeviceSettingPanel` 在 pending readback 期间调用了 `setEnabled(false)`；这会命中通用 `LabelButton:disabled` / `QLabel:disabled` 规则，覆盖掉正常 enabled 的 toggle 样式。

## Design

- pending 期间不再禁用 `systemClockOutBtn`。
- 改为保持按钮 enabled + checked/un-checked 视觉，同时通过 `Qt::WA_TransparentForMouseEvents` 临时阻断点击。
- pending 结束、取消、或回到常规刷新路径时，恢复正常鼠标交互。