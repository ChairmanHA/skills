# HTRA 独立 CMake 模块改造计划

## 目标
- 将 3rdParty/htra/CMakeLists.txt 改造成可独立引入的第三方模块。
- 其他工程只需 `add_subdirectory(.../3rdParty/htra)` 即可获得可链接 target 与必要的部署逻辑。
- 模块实现不依赖 SGStudio 专用辅助脚本或命名。
- 在当前仓库中保持兼容，避免破坏现有构建与 stage 逻辑。

## 现状问题
- 当前实现依赖 ../ThirdPartySupport.cmake。
- 当前 target / 变量命名带有 SGS 前缀，复用性差。
- 当前通过 `PARENT_SCOPE` 把 `SGS_HTRA_DIR`、`SGS_HTRA_RUNTIME_DLLS` 吐给父作用域，属于工程内耦合协议，不适合独立模块。

## 改造思路
- 参考外部模板，模块内部自包含：
  - 根目录与 include 目录计算
  - Windows / Linux 架构识别
  - 导入库 target
  - Unix 运行库部署脚本生成
  - 标准可覆写输出目录变量
- 导出通用 target：
  - `htra_api_imported`
  - `htra_api`
  - `HTRA::HTRA`
- 导出兼容变量供父工程按需读取，而不是依赖 SGS 私有脚本：
  - `HTRA_API_ROOT`
  - `HTRA_API_INCLUDE_DIR`
  - `HTRA_API_LIB_DIR`
  - `HTRA_API_RUNTIME_FILES`
- 当前仓库根 CMake 改为读取这些通用变量并映射到自身 stage 逻辑所需变量。

## 兼容性要求
- Windows 继续支持当前目录下的 h2_api.lib / h2_api.dll 布局。
- Unix 继续支持 lib/gcc_x86_64、lib/gcc_x86_32、lib/gcc_aarch64 布局。
- 保持当前仓库里 `SGS::h2_api` 的使用方无需跟着大改时，优先保留兼容 alias。

## 验证
- 先做静态检查，确保根 CMake 对 htra 的接入点仍成立。
- 再执行现有 Windows CMake build 任务验证。