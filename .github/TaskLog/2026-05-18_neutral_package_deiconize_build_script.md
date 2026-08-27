# Neutral 包资源最小化去图标方案

## 目标
- 用最小修改确保 neutral 版本不再把 icon/logo 资源编进中性专属主程序资源。
- 保持 `standard` / `russian` 打包行为不变。

## 当前结论
- `scripts/build.bat` 已经会在 neutral 分支上传入 `-DSGS_BRANDING_PROFILE=neutralized`。
- 根级 `CMakeLists.txt` 已经会在 `SGS_BRANDING_PROFILE=neutralized` 时导出 `SGS_BRANDING_NEUTRALIZED`。
- 运行时代码已用该宏屏蔽 `QApplication::setWindowIcon()`，标题栏 logo 也会被隐藏。
- 因此 neutral 版本真正需要修的不是打包脚本，而是 `src/app/res_neutral/app.rc` 与 `src/app/res_neutral/app.qrc` 中对 `app.ico` / `logo*.png` 的专属引用。

## 设计
- 回退前面新增的 `build.bat` / 根级 `CMakeLists.txt` / `src/app/CMakeLists.txt` no-icon 特判，恢复原有配置链。
- 仅修改 `src/app/res_neutral/app.rc`，移除 `app.ico` 的 PE icon 资源声明。
- 仅修改 `src/app/res_neutral/app.qrc`，移除 `app_icon` / `logo` / `logo_light` 资源条目。
- 保留 `src/app/res_neutral` 下现有二进制文件本体不动，避免无关二进制 churn；当前最小修复只需要断开引用。

## 判别检查
- 静态确认 neutralized 运行时代码仍会跳过 `setWindowIcon()`，标题栏 `AppLogo` 仍被 `ThemeManager` 直接返回空路径。
- 通过一次临时 neutral configure + `SGStudio` 目标编译，确认 comment-only `app.rc` 和裁剪后的 `app.qrc` 能正常参与构建。