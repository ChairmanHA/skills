# 构建脚本水印开关计划

## 需求

1. `scripts/build.bat` 可通过脚本参数控制 `SGS_ENABLE_INTERNAL_WATERMARK`。
2. `scripts/build.sh` 可通过脚本参数控制 `SGS_ENABLE_INTERNAL_WATERMARK`。
3. `scripts/build_all.bat` 需要把同一水印开关透传给所有 Windows 包构建。
4. 无需实际编译打包，只需完成脚本实现并给出调用示例。

## 本地假设

1. 最小改动是让脚本显式传 `-DSGS_ENABLE_INTERNAL_WATERMARK=ON/OFF`，而不是依赖顶层 `CMakeLists.txt` 默认值。
2. `build_all.bat` 本来就统一透传 `--rebuild`，因此追加一个统一的 `--watermark on|off` 参数即可覆盖五个 Windows 目标。
3. `build.sh` 当前每次都会清空 build 目录，因此显式传 `-D` 参数后不会受旧缓存影响。

## 计划

1. 为 `build.bat` 增加 `--watermark on|off` / `--with-watermark` / `--without-watermark` 参数解析，并透传给 CMake。
2. 为 `build.sh` 增加同样语义的参数解析，并通过 `CMAKE_EXTRA_ARGS` 数组透传。
3. 为 `build_all.bat` 增加统一的水印参数解析与透传，覆盖 dry-run 与真实执行分支。
4. 用脚本级轻量验证确认参数透传正确，不触发真实构建。

## 验证口径

1. `build_all.bat --dry-run` 能显示出包含水印参数的最终调用命令。
2. `bash -n scripts/build.sh` 通过语法检查。
3. 相关文件静态诊断无新增错误。