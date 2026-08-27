# About 弹窗暂时隐藏端口供电信息

## Scope

- 在 About 弹窗中隐藏 `RF Port` 与 `USB Port` 两行。
- 同时隐藏每行的标题标签和数值标签，避免留下空白值控件。
- 保留实时供电状态采集、刷新与复制信息逻辑，便于后续恢复显示。

## Evidence and assumptions

- 观察：`RF Port` 对应 `RFPwrlabel` / `RFPwrValueLabel`，`USB Port` 对应 `USBPwrLabel` / `USBPwrValueLabel`。
- 假设：用户所说“暂时 invisible”表示仅改变界面可见性，不删除字段或后台刷新能力。

## Success criteria

- About 弹窗不显示 `RF Port`、`USB Port` 标题及其数值。
- 其余 About 信息保持不变。
- 恢复时只需移除四个控件的 `visible=false` 属性。

## Verification level

- `static`

## Verification checklist

- [x] 四个对应 QLabel 均设置为不可见。
- [x] 未删除或修改实时供电数据逻辑。
- [x] `.ui` XML 解析与 `git diff --check` 均通过。

## Verification result

- 静态验证通过。
- 按仓库默认规则未执行编译或运行验证。
