# build.bat 从 msvc.cmake 读取 VS 根路径

## 目标
- 保留 scripts/msvc.cmake 作为 Windows batch 构建/打包链唯一的机器本地工具链入口。
- 让 scripts/build.bat 不再单独维护 Visual Studio 的绝对路径。
- 用户后续只需要修改 msvc.cmake 中的 VS 根目录和 QT_PREFIX，就能驱动同一条构建打包脚本。

## 当前结论
- build.bat 目前只剩下两条硬编码的 VS 路径仍然在脚本内维护。
- 这两条路径都可由同一个 VS 安装根目录稳定派生。
- 本机实际可用的 VS 安装根目录是 C:/Program Files/Microsoft Visual Studio/2022/Enterprise。

## 最小方案
- 在 scripts/msvc.cmake 中新增 VS_INSTALL_DIR。
- build.bat 从 msvc.cmake 读取 VS_INSTALL_DIR 和 QT_PREFIX。
- build.bat 基于 VS_INSTALL_DIR 组装 VsDevCmd.bat 与 vcvars64.bat 的路径，并在调用前检查存在性。
- 保留现有 msvc.cmake 名称与角色，不引入新的配置文件。

## 判别检查
- 静态确认 build.bat 中不再出现 Visual Studio 版本字面量路径。
- 运行一次路径解析检查，确认 VS_INSTALL_DIR、QT_PREFIX 以及派生出的三个可执行/批处理路径都存在。
