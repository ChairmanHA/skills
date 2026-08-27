# RemoteMiniBar checked 按钮闪烁修复

## Scope

- 修复 Raspberry Pi 上点击 RemoteMiniBar 的 RF、Sweep、MOD 按钮时，按钮短暂进入相反 `checked` 样式再恢复的问题。
- RF 同时覆盖折叠态与展开态两个按钮实例。
- 保留设备快照对 RF/MOD 高亮的所有权，以及 Sweep 快照对 Sweep 高亮的所有权。
- 不改变 Frequency、Level、展开/折叠、Restore 等其他按钮行为。

## Observation and inference

- 观察：RemoteMiniBar 中只有折叠 RF、展开 RF、Sweep、MOD 四个 `LabelButton` 以 `checkable=true` 创建。
- 观察：RF 点击只发送 `rfChangeRequested(!m_rfEnabled)`；Sweep 点击只打开 Sweep 面板；MOD 点击只打开 MOD 菜单。
- 观察：四个按钮的最终 `checked` 状态均由 `refreshSnapshotDisplay()` 经 `setInfoButtonState()` 从远端快照回写。
- 推断：默认 `QAbstractButton` 点击自动 toggle 会在快照状态恢复前产生可绘制的临时 checked 状态，Raspberry Pi 显示链路将该中间帧暴露为闪烁。
- 设计：RemoteMiniBar 的 checkable 信息按钮使用局部 externally-checked `LabelButton`，覆盖 `nextCheckState()` 阻止点击自动翻转；程序化 `setChecked()` 仍然有效。

## Success criteria

- RF 关闭或开启时点击都不产生临时相反高亮，仍发送基于当前快照的切换请求。
- Sweep 未使能或已使能时点击都不改变高亮，只正常打开面板。
- MOD 关闭或开启时点击都不改变高亮，只正常打开菜单。
- 快照回写仍可更新四个按钮的 checked 状态及现有 QSS 样式。
- Win32 与 Raspberry Pi 共用同一确定性状态流，不增加平台宏。

## Verification level

- `static`

## Verification checklist

- [x] 四个 checkable 信息按钮的用户点击不再执行默认 checked toggle。
- [x] `setInfoButtonState()` 仍可程序化更新 checked。
- [x] RF 请求、Sweep 面板、MOD 菜单的 clicked 连接保持不变。
- [x] 非 checkable 信息按钮及命令按钮保持原类型和行为。
- [x] `git diff --check` 通过。

## Verification result

- `createInfoButton(..., checkable=true)` 的调用点仅有折叠 RF、展开 RF、Sweep、MOD 四处，均切换到 externally-checked 类型。
- `nextCheckState()` 只阻止用户点击自动翻转；`setInfoButtonState()` 继续通过 `setChecked()` 接收快照状态并刷新复合标签样式。
- RF、Sweep、MOD 的 `clicked` 信号连接未修改，原请求和面板/菜单行为保持不变。
- Frequency、Level 仍使用普通非 checkable `LabelButton`，命令按钮仍使用 `QPushButton`。
- `git diff --check` 通过；按仓库默认静态验证规则未执行编译或运行。
