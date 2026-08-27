# build.bat 从 msvc.cmake 读取 Qt 前缀

## 目标
- 保留 `scripts/msvc.cmake` 作为 Windows batch 打包链的单一 Qt 路径来源。
- 让 `scripts/build.bat` 不再单独维护 `QT_PREFIX`。
- 在打包前显式校验 `windeployqt.exe` 是否存在，避免当前这种静默失配。

## 当前结论
- `build.bat` 当前仍显式维护 `QT_PREFIX`，并把它加到 `PATH`。
- `build.bat` 同时还会把 `scripts/msvc.cmake` 作为 `CMAKE_TOOLCHAIN_FILE` 传给 CMake。
- 当前 `scripts/msvc.cmake` 中的 `QT_PREFIX` 指向不存在的 `C:/Qt/5.15.18/msvc2026_64`，但 batch 自己的 `QT_PREFIX` 可能掩盖这个问题。

## 最小方案
- 在 `build.bat` 中删除硬编码的 `QT_PREFIX`。
- 从 `scripts/msvc.cmake` 解析 `set(QT_PREFIX "...")` 的值。
- 基于解析结果构造 `WINDEPLOYQT_EXE`，并在执行前检查其存在性。
- `windeployqt` 调用改为使用解析出的绝对路径，而不是依赖 PATH 搜索。

## 判别检查
- 静态确认 `build.bat` 中不再出现第二份 Qt 前缀字面量。
- 运行脚本内的路径解析检查，确认能从 `msvc.cmake` 读取前缀并在无效路径时提前失败。