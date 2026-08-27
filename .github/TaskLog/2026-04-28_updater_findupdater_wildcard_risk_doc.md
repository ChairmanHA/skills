# 2026-04-28 Updater 通配匹配误选文档补充

## 背景

- 当前活跃更新链路中，`PacketSpec::findUpdater()` 使用 `Updater_*` 作为匹配规则查找包内 updater。
- 该规则没有限制必须命中 `.exe`，也没有做可执行映像校验。
- 如果更新包的 `updater/` 目录里同时存在 `Updater_*.pdb`、`Updater_*.manifest`、配置文件或其他同名前缀工件，当前实现可能把非可执行文件记录为 `m_updaterPath`。
- maintenance 随后会把该路径直接传给 `QProcess`；在 Windows 上这会落到 `CreateProcess` 失败，典型表现为 `0x000000c1`（bad exe format）。

## 本次目标

1. 在 `.github/KnowledgeBase/updater_firmware_update_mechanism.md` 中补充当前包解析规则的真实隐患。
2. 在 `.github/KnowledgeBase/updater_mechanism_gap_and_remediation.md` 中把该问题明确记录到当前机制风险里。

## 依据

- 活跃实现代码位于 `src/plugins/updater/packetspec.cpp`。
- `findUpdater()` 当前只做 `Updater_*` 文件匹配，不保证命中项是可执行文件。
- 当前活跃 maintenance 位于 `src/app/maintenance/progressdialog.cpp`，会直接把传入路径交给 `QProcess` 启动。

## 结论方向

- 该问题应被归类为当前 package validation / package parser 的真实缺陷，而不是外部 updater 本体依赖缺失。
- 文档需要明确：`valid()` 不能只依赖“找到了某个 `Updater_*` 文件”，而必须确认它是预期的 updater 可执行文件。