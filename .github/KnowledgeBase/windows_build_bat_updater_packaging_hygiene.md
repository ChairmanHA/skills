# Windows build.bat updater 打包与 3rdParty 依赖收敛说明

本文档说明 2026-05 对 Windows 源码编译链与 `scripts/build.bat` 打包链做出的调整，目标是：

- 解决 `Updater.dll` 因打包遗漏可选依赖而在部分机器上无法加载的问题。
- 让 `scripts/build.bat` 默认走增量构建，不再每次都把 3rdParty 从源码重新编译。
- 明确未来若要调整 updater 打包、品牌包型或第三方依赖，应修改哪些文件。

## 背景问题

此前 `Updater.dll` 通过 `libcurl_static + libzip::zip` 间接带入了多组并非 updater 必需的可选能力：

- curl 的 Brotli / Zstd 内容解码
- curl 的 LDAP / LDAPS
- libzip 的 BZip2 / LZMA / Zstd 支持

这些能力来自上游 CMake 默认值或 `AUTO` 自动探测。一旦本机装有 vcpkg 对应库，源码编译的 `Updater.dll` 就会真的导入这些 DLL；但 `scripts/build.bat` 产物并没有同步把它们打进包内，于是会出现：

- 开发机上因为 PATH 恰好包含 vcpkg 目录，插件可正常加载。
- 目标机上缺少这些 DLL 时，`Updater.dll` 在插件加载阶段直接失败。

另一个并行问题是：根目录 CMake 的业务版本/品牌宏泄漏到 3rdParty 目标，导致 git hash、版本号或品牌 profile 变化时，3rdParty 的编译命令也变化；再叠加 `build.bat` 以前每次都清空整个 build tree，就形成了“项目代码一改，3rdParty 也经常整树重编”的体验。

## 目前已做的调整

### 1. 收紧 3rdParty 的 updater 相关可选依赖

位置：`3rdParty/CMakeLists.txt`

当前显式关闭了以下非必需能力：

- curl: `BUILD_LIBCURL_DOCS`、`BUILD_MISC_DOCS`、`BUILD_OSSFUZZ`、`ENABLE_CURL_MANUAL`
- curl: `CURL_BROTLI`、`CURL_ZSTD`
- curl: `CURL_DISABLE_LDAP`、`CURL_DISABLE_LDAPS`
- zlib: `ZLIB_BUILD_TESTING`
- libzip: `ENABLE_BZIP2`、`ENABLE_LZMA`、`ENABLE_ZSTD`

这样做后，`Updater.dll` 的直接导入项已经收敛到：

- 工程内 DLL：`Core.dll`、`ExtensionSystem.dll`、`Controls.dll`、`Utils.dll`
- Qt DLL：`Qt5Core.dll`、`Qt5Gui.dll`、`Qt5Widgets.dll`
- 压缩相关只剩：`z.dll`
- 系统与 MSVC 运行时 DLL

之前导致问题的 `bz2.dll`、`zstd.dll`、`brotlidec.dll`、`brotlicommon.dll` 不再出现在 `Updater.dll` 的导入表里。

### 2. 让业务编译宏只作用于一方代码

位置：`src/CMakeLists.txt`

以下 SGStudio 业务宏与编译标准，已经从仓库根 CMake 的全局作用域收回到 `src/` 子树：

- `CMAKE_CXX_STANDARD 17`
- `SGS_BRANDING_NEUTRALIZED`
- `SGS_GIT_HASH` / `SGS_GIT_HASH_FULL`
- `GIT_BRANCH`
- `PACKET_VERSION` / `SGS_VERSION`
- `SGS_BRANDING_PROFILE`

这样 `src/libs`、`src/app`、`src/plugins`、`src/maintenance` 仍能拿到原有品牌和版本信息，但 `3rdParty` 不再因为这些业务元数据变化而重编。

### 3. `build.bat` 默认改为增量构建

位置：`scripts/build.bat`

当前行为：

- 默认复用 `build/build_msvc_<packet>_<language>`。
- 只有显式传入 `--rebuild` 或 `/rebuild` 时，才删除整个 build tree。

这意味着日常运行：

- `scripts/build.bat standard cn SGStudio`
- `scripts/build.bat neutral en SGStudio`

都会优先走增量构建；只有需要彻底 clean build 时，才用：

- `scripts/build.bat standard cn SGStudio --rebuild`

## 当前验证结论

### Updater.dll 缺依赖导致无法加载的问题

针对最初的 updater 打包缺依赖问题，当前结论是：**原始问题已解决**。

已完成的验证：

1. 通过 `scripts/build.bat standard cn SGStudio` 重新生成包。
2. 解压 `build/windows_x86_64/standard/cn/SGStudio.zip`。
3. 检查包内 `plugin/Updater.dll` 的导入表，确认不再依赖 `bz2.dll`、`zstd.dll`、`brotlidec.dll`、`brotlicommon.dll`。
4. 直接启动解压后的包内 `bin/SGStudio.exe`。
5. 检查包内 `bin/debug.log`，确认日志中存在：
   - `Load Updater OK`
   - `Initialize Updater OK`
   - `Start Updater OK`

也就是说，在真实包环境下，Updater 插件已经能够成功加载和初始化，不再依赖开发机 PATH 中恰好存在的 vcpkg DLL。

### 仍需注意的边界

当前包内仍沿用现有 Windows MSVC runtime 策略：

- 包里有 `vc_redist.x64.exe`
- 但不额外散装复制 `MSVCP140.dll` / `VCRUNTIME140.dll` / `VCRUNTIME140_1.dll`

这不是这次 updater 专项问题的根因，也不影响“Updater 缺少 Brotli/Zstd/BZip2 依赖”的修复结论；但如果未来遇到“整包在纯净机器上完全起不来”的问题，应继续按仓库现有 MSVC runtime 打包策略排查，而不是回头怀疑 updater 的压缩依赖又漏包了。

## 以后如果还出问题，应该看哪里

### 如果是 updater 又开始缺 DLL

优先检查：

- `3rdParty/CMakeLists.txt`
  - 是否重新把 curl 的 Brotli / Zstd / LDAP 打开了
  - 是否重新把 libzip 的 BZip2 / LZMA / Zstd 打开了
- `src/plugins/updater/CMakeLists.txt`
  - updater 目前直接链接哪些三方库
- `src/plugins/updater/packetspec.cpp`
  - 是否新增了新的网络协议、认证或内容解码需求
- `src/plugins/updater/decompressor.cpp`
  - 是否开始支持新的压缩格式或新的 ZIP 特性

### 如果是 build.bat 又开始频繁重编 3rdParty

优先检查：

- `scripts/build.bat`
  - 是否又把 `BUILD_DIR` 的整树删除恢复成默认行为
  - 是否新加了会污染缓存的 configure 参数
- `src/CMakeLists.txt`
  - 是否有新的业务编译宏需要下沉到更小作用域
- 仓库根 `CMakeLists.txt`
  - 是否又把业务版本/品牌宏重新做成了全局 `add_compile_definitions(...)`

### 如果是 neutral / russian / standard 包型行为不一致

优先检查：

- `scripts/build.bat`
  - `PACKET -> BRANDING_PROFILE` 的映射
- `src/CMakeLists.txt`
  - `SGS_BRANDING_NEUTRALIZED` 与 `SGS_BRANDING_PROFILE` 的下发
- `configuration_files/`
  - 各 packet/language 对应配置目录
- `updater_files/`
  - 包内 updater 附带文件内容
- `package-info/`
  - release note 与 version.json 的配置阶段拷贝

## 推荐验证方式

当未来再次修改 updater 依赖或 Windows 打包链时，建议至少做以下三步验证：

1. 跑一次目标包型的 `scripts/build.bat`。
2. 解压生成的 zip，而不是只看 build tree。
3. 启动解压后的 `bin/SGStudio.exe`，检查包内 `debug.log` 里是否仍然有 `Load/Initialize/Start Updater OK`。

如果只是怀疑导入项变化，也应至少补一条导入表检查，确认是否重新出现 `bz2` / `zstd` / `brotli` 之类的外部依赖。
