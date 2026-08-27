# Remote MiniBar MOD button/global state split

## Scope

RemoteMiniBar 当前把 `enabledBusinessId` 同时用于 MOD 菜单勾选、hosted editor 的 Enabled 和 MOD 按钮 ON/OFF。该语义无法表达 main 中启用 Playback/Streaming 的情况，因为这两个 business 明确不进入远程菜单。

本次只拆分 snapshot 与 helper 展示语义，不改变 MOD menu 点击行为、request 语义或协议版本。

验证级别：`static`

## Observation

- main 的全局 `Mod` property 是 main MOD 按钮的状态来源。
- 远程菜单只包含 main 过滤后的可见 provider business，并排除 `Playback`、`Streaming`。
- 每个列表 business 的 `Panel::isBtnEnabledChecked()` 是该 business 自身 Enabled 的状态来源；`Panel::enabledChanged` 是对应变化信号。
- 当前 snapshot 仅发送 `enabledBusinessId`，并从全局 `Mod` 与 `BusinessManager::selectedEntryBusiness()` 联合推导；helper 又从该 ID 推导 MOD 按钮，因而丢失“不在远程列表中的 business 已使能”这一状态。

## Design

1. `mod.enabled` 单独镜像 main 的全局 `Mod` property，仅驱动 RemoteMiniBar MOD 按钮 ON/OFF。
2. `mod.enabledBusinessId` 只扫描过滤后的 `businesses[]`，取其中自身 panel 处于 Enabled 的 business，仅驱动 menu 勾选和 hosted editor Enabled。
3. RemoteMinibarService 订阅列表 business 的 `Panel::enabledChanged`，保证 business Enabled 变化即使未改变全局 `Mod`，仍会发布新 snapshot。
4. menu 点击仍只打开对应 editor，不改变任何 business 的 Enabled 状态。
5. 继续使用现有 version 1 和既有 `enabled` JSON key，不升级协议版本。

## Success criteria

| main 状态 | RemoteMiniBar MOD 按钮 | Remote MOD menu |
| --- | --- | --- |
| 列表内 A Enabled，`Mod=ON` | ON | 仅 A 勾选 |
| 列表内 A Enabled，`Mod=OFF` | OFF | 仅 A 勾选 |
| Playback/Streaming Enabled，`Mod=ON` | ON | 无条目因该状态被勾选 |
| 列表内无 business Enabled，`Mod=OFF` | OFF | 无勾选 |

另外，打开任意 menu 项或 hosted editor 不得自动改变上述两类使能状态。

## Static verification checklist

- [x] snapshot 同时包含 `mod.enabled` 和 `mod.enabledBusinessId`。
- [x] `mod.enabled` 只读取 main `Mod` property。
- [x] `enabledBusinessId` 只来自过滤后列表 business 的 panel Enabled 状态，不读取 main current/selected business。
- [x] helper MOD 按钮只读取 `mod.enabled`。
- [x] menu 与 hosted editor Enabled 只读取 `enabledBusinessId`。
- [x] `Panel::enabledChanged` 会调度 snapshot。
- [x] menu trigger 不发送 MOD Enabled request。
- [x] `kProtocolVersion` 保持不变。

## Verification result

- `git diff --check` 通过。
- 静态断言确认 MOD button/menu/editor 的字段消费边界、Playback/Streaming 过滤和 menu trigger 行为。
- CMake inclusion 已确认：main service 与 helper window 均在现有 target 中。
- 按仓库默认策略未执行 build 或 runtime 测试。
