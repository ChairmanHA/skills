# Neutral 包无图标修复计划

## 目标
- 修正 `scripts/build.bat` 的 neutral 打包入口，使 neutral 包编译时显式走 neutralized branding。
- 确保 neutral 包在运行时不设置窗口图标，标题栏应用 logo 也不显示。
- 保持 standard 与 russian 打包链行为不变。

## 本地假设
- 当前 neutral 包仍出现图标的根因是：`scripts/build.bat` 只传了 `packet=neutral`，没有传 `SGS_BRANDING_PROFILE=neutralized`；同时顶层 `CMakeLists.txt` 没有把 `SGS_BRANDING_PROFILE=neutralized` 映射成代码实际使用的 `SGS_BRANDING_NEUTRALIZED` 编译宏。

## 判别检查
- 静态确认 `scripts/build.bat` 在 neutral 分支下会追加 `-DSGS_BRANDING_PROFILE=neutralized`。
- 静态确认顶层 `CMakeLists.txt` 会在 `SGS_BRANDING_PROFILE=neutralized` 时导出 `SGS_BRANDING_NEUTRALIZED`。
- 静态确认相关运行时代码仍通过 `SGS_BRANDING_NEUTRALIZED` 屏蔽窗口图标与 AppLogo。

## 执行策略
1. 在 `scripts/build.bat` 中根据 `PACKET` 推导 branding profile，并仅对 neutral 使用 neutralized。
2. 在顶层 `CMakeLists.txt` 中补齐 branding profile 到编译宏的映射。
3. 用静态检查复核脚本与 CMake 条件，避免误伤 standard/russian 流程。