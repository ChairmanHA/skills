# Merge API 2.0.32 and multi-device feature into dev

日期：2026-08-17  
状态：方案完成，待人工执行  
验证级别：`debug-build` + `debug-run` + Linux/aarch64 build

## Branch snapshot

- 目标主干：`dev` = `526df15d687b95115052eddbe31fc6b1aba711b8`
- 功能分支：`feature-api30-migration-2ethswitch` = `1895c9ef99a57edf045e34575c148d13ed819b3d`
- merge base：`1d4ed76df5d0fdba355668547a2b023049bd9c54`
- 独有提交：`dev` 7 个，功能分支 13 个。
- `git merge-tree --write-tree dev feature-api30-migration-2ethswitch` 预演得到 5 个文本冲突。
- 当前工作区有未跟踪文件；切换分支前必须提交应入库内容，或在 SourceTree 中 stash 并勾选包含 untracked files。

以上快照基于本地 `dev` / `origin/dev` 当前一致的状态。实际执行前先 Fetch；若远端已前进，重新做一次 merge-tree/冲突核对。

## Recommended strategy

不要直接在 `dev` 上 merge，也不要逐个 cherry-pick 13 个功能提交。

推荐从最新 `dev` 创建 `integration/api30-multidevice-into-dev`，在该分支一次性 merge `feature-api30-migration-2ethswitch`。原因：

- 功能分支首个 API 提交同时包含 API 大迁移和无关的 `updater_bak` 删除，不适合直接 cherry-pick。
- `bffd4c62` 同时包含双 ETH updater、DeviceManager 和版本文件，也不适合拆成机械 cherry-pick。
- 一次真实 merge 目前只有 5 个文本冲突，且保留双方提交历史和共同祖先，最容易审计与回滚。
- integration 分支从 `dev` 创建后，SourceTree 冲突界面中的 Mine/Ours 是 `dev`，Theirs 是功能分支；不要只看 Mine/Theirs 名称，始终按下述语义处理。

## SourceTree procedure

1. 在当前功能分支处理未跟踪文件，确保 Working Copy clean。
2. Push 当前功能分支，并为两个端点创建 tag：
   - `backup/dev-before-api30-merge-20260817`
   - `backup/api30-multidevice-before-dev-merge-20260817`
3. Fetch origin，不要直接 Pull 后开始解决冲突。
4. 确认本地 `dev` 与 `origin/dev` 一致；若 dev 落后，只允许 fast-forward 更新。
5. 从 `dev` 创建 `integration/api30-multidevice-into-dev` 并 checkout。
6. 在 SourceTree 中选择 Merge，将 `feature-api30-migration-2ethswitch` merge 到当前 integration 分支；不要 squash、不要 rebase。
7. 按冲突矩阵处理 5 个冲突，并检查 4 个关键自动合并文件。
8. 在 merge commit 前执行范围清理：保留 live updater 的双设备改造，但默认恢复 `src/plugins/updater_bak/` 为 dev 状态。
9. 完成 merge commit，push integration 分支；此时仍不要修改 `dev`。
10. 在 integration 分支完成静态检查、Debug build、联机回归和 Linux/aarch64 build。
11. 验证全部通过后，再 checkout `dev`，将已验证的 integration 分支合入；由于 integration 已以 dev 为第一父提交，通常可直接 fast-forward 到已验证 merge commit。
12. 最后单独提交正式版本号和 release notes，不在冲突解决阶段决定发布版本。

## Conflict matrix

### `CMakeLists.txt`

- 保留 dev：`SGS_DEFAULT_PROJECT_VERSION=2.7.4`、`SGS_DEFAULT_PACKET_VERSION=26.08.14`。
- 不保留功能分支的 `2.7.3.1` / `26.08.13`，避免版本倒退。
- API 2.0.32 合入后应在独立发布提交中决定是否升到 2.7.5。

### `package-info/version.json`

- 冲突解决阶段保留 dev 的 2.7.4 / 2026-08-14 metadata。
- 最终验证完成后，再和根 CMake 版本一起原子升级。

### `package-info/releasenote_standard_cn.txt`

- 先保留 dev 的 2.7.4 / SCPI 说明。
- API 2.0.32、多设备和 profile switching 内容在最终版本提交中追加，避免形成两个互相竞争的顶部版本块。

### `src/plugins/htra/fancydevice.cpp`

- 以功能分支的 API 2.0.32 实现为主体；不能选择整个 Mine/dev 文件，因为旧函数签名和旧结构体不能与新头文件配套。
- 人工移植 dev 修复：`kDefaultFanAutoThresholdCelsius` 必须为 `40.0f`，不是功能分支的 `50.0f`。
- 完成后重点检查 open/close、USB/ETH endpoint、status、capability、playback/streaming/sweep、power/fan/GNSS 全部仍使用新 API 类型和函数签名。

### `src/plugins/updater/updatedialog.cpp`

- 以功能分支的多目标 firmware updater 实现为主体，保留 `--target` 列表、批量断开和 maintenance 顺序执行。
- 同时保留 dev 的产品修复意图：BNC 打开 updater 时默认 Local Default。
- 当前功能分支已经把 `switchOnline()` 统一退化到 Local Default；冲突解决后需确认 BNC 与非 BNC 的实际产品策略，而不是机械复制旧 `#ifdef`。

## Mandatory auto-merge review

Git 当前能够自动合并这些文件，但仍需人工确认：

- `src/plugins/htra/fancydevice.h`
  - 保留 API 2.0.32 类型/成员；同时保留 dev 的 `m_fanMode = Core::FanMode::On`。
- `src/plugins/htra/plugin.cpp`
  - 保留字符串 API version 查询；同时保留 dev 的 `Pulse_DutyCycle` property。
- `src/plugins/core/mainwindow.h`
  - 保留 dev 为 SCPI 暴露的 `showSweepPage()` / panel accessors；同时保留 profile coordinator 和 ETH A/B 成员。
- `src/plugins/core/stepsweeppanel.cpp`
  - 保留 dev 的 `QSERIALIZER_DECLARE(StepSweepAnalogProfile)`；同时确认功能分支的 ListMode layout 处理符合当前 UI 目标。

## Scope ownership

### Feature branch owns

- `3rdParty/h2_api/` 的 2.0.32 header、Windows import/runtime binaries、Linux x86_64/aarch64 `.so`。
- `FancyDevice`、HTRA scanner/plugin 对新 API 的适配。
- DeviceManager retain-open、多 USB/ETH current 切换、UID profile coordinator、Device List、ETH Connect、标题栏 A/B。
- 双 ETH updater 的 live `src/plugins/updater/`、`src/maintenance/` 和批量 close 流程。
- A/B 所需主题和翻译更新，但 binary xlsx 必须进行实际 UI 验收。

### Dev owns

- SCPI 新库和插件及其后续 bugfix。
- sequence seed overflow 修复。
- updater BNC 默认入口修复。
- fan 默认 On、40℃自动阈值、Pulse DutyCycle property。
- 2.7.4 版本基线和其他 dev-only UI/controls 修复。

### Exclude by default

- `src/plugins/updater_bak/` 删除不属于 API 或多设备目标，并且当前 CMake 没有引用该目录。为降低范围，默认恢复成 dev 状态；若确实要清理，应另开独立提交评审。
- `3rdParty/h2_api/NewH2.zip` 当前未跟踪，不应随 merge 意外进入 dev。
- 与本次功能无关的本地 release note、脚本和分析文档不应混入 merge commit。

## Static verification

- `git status` 无 unresolved/untracked integration artifacts。
- 搜索并清零 `<<<<<<<`、`=======`、`>>>>>>>`。
- `git diff --check dev..integration/api30-multidevice-into-dev` 通过。
- `h2_api.h` 与 Windows DLL/LIB、Linux 2.0.32 SO 来自同一 API 包；不混用 dev 的 2.0.28 binaries。
- 新头文件删除 `h2_typedef.h` 后，源码没有残留 include 或旧 API 类型。
- Core CMake 同时包含 SCPI 所需依赖和 `deviceruntimeprofilecoordinator.cpp/.h`。
- 对比 `dev...feature` 的 87 个功能侧文件，确认每个变化都被保留或明确排除。

## Build verification

1. 先使用现有 `build/Qt_5_15_9_msvc2022_64-Debug` 完整构建，避免直接覆盖 Release runtime 后才发现 ABI/链接错误。
2. Debug 通过后再构建 Release。
3. 单独执行 Linux/aarch64 build，确认部署目录生成 `libh2api.so -> libh2api.so.2 -> libh2api.so.2.0 -> libh2api.so.2.0.32` 链路。
4. 检查最终 `bin/h2_api.dll` / Linux `.so` 与编译时 header 版本一致。

## Runtime regression matrix

- 单 USB：发现、open、状态轮询、CW、Playback、Streaming、拔插恢复。
- 两 USB：独立 UID profile、retain-open、Playback 切走继续、当前 USB 断连 fallback 先 restore 后 apply。
- 单 ETH：5000/5001 手工连接、Device List、断连/重连。
- USB + ETH：双向切换 save/load profile，目标 UID 和最终 apply UID 一致。
- 双 ETH：同 IP 5000/5001 的 A/B 映射、retain-open、普通 Playback 连续输出。
- Streaming：切走前停止，不允许 sender 串到目标设备。
- Firmware updater：单 USB、单 ETH、双 ETH 顺序更新；全部 updater 成功后才复制/重启。
- Dev 回归：SCPI server/IDN/失败回滚、sequence seed、Fan 默认值、Pulse DutyCycle、BNC updater 默认入口。
- 退出：所有 retained USB/ETH handle 关闭，临时 UID profile 按现有策略删除。

## Rollback

- 冲突解决期间发现方向错误：使用 SourceTree 的 Abort Merge，不使用 hard reset。
- merge commit 后发现问题：保留 integration 分支用于诊断，删除并从 dev 重新创建新的 integration 分支；不要在 dev 上反复 revert 未验证 merge。
- 只有 integration 全部验证通过后才移动 dev，因此主干回滚点始终明确。
