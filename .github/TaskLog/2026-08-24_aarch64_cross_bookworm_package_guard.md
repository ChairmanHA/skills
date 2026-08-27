# 250 交叉构建 aarch64 包在 Raspberry Pi 132 / Bookworm 上运行的打包保护

## Scope

- 检查 `scripts/build.sh aarch64` 是否能从 192.168.3.250 的 Ubuntu 18.04/GCC 7 交叉环境生成可在 Raspberry Pi 132 Debian 12/Wayland 上测试的自包含应用运行包。
- 不改变正式 `scripts/build_linux.sh raspberry-pi` 仍路由 Debian 12 arm64 container 的发布决策。
- 不启动完整 configure/build；用户同步后自行在 250 编译和在 132 实测。

## Observation

- 250 的 `/opt/Qt/5.15.18/gcc_aarch64` 包含同一 Qt 构建的 Core/Gui/Widgets/WaylandClient、`libqwayland-egl.so`、`libqwayland-generic.so`、`libqxcb.so` 和 `libxdg-shell.so`；已检查 ELF 均为 aarch64。
- 该 Qt 前缀最高要求 `GLIBC_2.27`、`GLIBCXX_3.4.21`、`CXXABI_1.3.9`，没有携带 glibc、libstdc++、GPU/Mesa/DRM 或 Wayland 系统运行库。
- 250 历史 `build_aarch64_standard_cn` 包含 LayerShellQt interface/plugin、Qt Wayland plugins、package `qt.conf` 和根 launcher；全包最高要求为 `GLIBC_2.29`、`GLIBCXX_3.4.22`、`CXXABI_1.3.11`，均不超过 132 的 Debian 12/GCC 12 runtime。
- `bin/SGStudio` 已有 `$ORIGIN/../lib:$ORIGIN/../plugin` RUNPATH，但 ELF RUNPATH 不继承给 Qt 间接依赖；Wayland/QPA/session 环境也需要 launcher 设置。因此直接执行 `bin/SGStudio` 不属于支持的启动方式。
- 历史包中的 `libQXlsx.so.1.5.0` 仍带 `/opt/Qt/5.15.18/gcc_aarch64/lib` 绝对 RUNPATH；launcher 的 `LD_LIBRARY_PATH` 能覆盖它，但可移植归档不应保留构建机绝对路径。
- 当前 `build.sh` 只对 RK3588 profile 执行 ELF/ABI audit，aarch64/Wayland profile 缺少出包前硬检查。

## Success Criteria

1. `build.sh aarch64` 在打包前要求主程序、Qt Core/Gui/Widgets/WaylandClient、Wayland platform plugins、LayerShellQt interface/plugin、xdg-shell plugin、vendor modulation runtime 和 `libgomp.so.1` 完整存在。
2. 扫描 `bin/lib/plugin/updater` 中所有 ELF，要求均为 aarch64，且不超过 Bookworm 上限 `GLIBC_2.36`、`GLIBCXX_3.4.30`、`CXXABI_1.3.13`。
3. 拒绝把 glibc/loader、旧 libstdc++/libgcc_s、Mesa/GL/EGL/GLES/DRM/GBM driver runtime 放进 package `lib/`。
4. 拒绝 ELF 的 NEEDED/RPATH/RUNPATH 中保留 `/home`、`/opt` 或 `/tmp` 构建机绝对路径。
5. QXlsx 使用 package-relative `$ORIGIN` RUNPATH。
6. staging 后验证 Wayland launcher 使用 package-owned Qt plugin 路径与 `LD_LIBRARY_PATH`，并把 launcher-only runtime policy 打印给操作者。
7. `rk3588` 与 `gcc/x86_64` profile 的现有行为不变。

## Verification Level

- `static`
- Bash syntax、CMake/static diff、隔离 mock package audit 分支和 250 只读环境证据。
- 不启动完整构建；真实构建和 132 Wayland UI/runtime 验收由用户执行。

## Implementation And Verification

- `build.sh aarch64` 现在预检 QtWayland runtime，并在 Qt/plugin 收集后执行 Bookworm aarch64 package audit。
- audit 验证必需 runtime、全包 ELF 架构、Bookworm ABI 上限、构建机绝对动态路径和禁止捆绑的目标系统 runtime。
- staging 后验证 `qt.conf` 相对路径、Wayland/QPA 设置、package-owned Qt plugin 路径和 `LD_LIBRARY_PATH`；构建输出明确直接执行 `bin/<application>` 不受支持。
- `launch_pi.sh` 启动时打印相同的 launcher-only runtime policy。
- QXlsx target 固定 `BUILD/INSTALL_RPATH=$ORIGIN`，并使用 install RPATH 进行 build，避免 CMake 从 `/opt/Qt/...` 自动追加绝对 RUNPATH。
- Git for Windows Bash 对 `build.sh`、`launch_pi.sh` 执行 `bash -n` 通过；`git diff --check` 通过。
- 在 250 上对历史 build tree 运行新 audit，成功识别旧 build 缺少 `bin/qt.conf` 以及 QXlsx `/opt/Qt/...` 绝对 RUNPATH。
- 在 250 的隔离 `/tmp` 中组装 13 个真实 aarch64 ELF 的最小 Wayland runtime：正常目录通过 audit 和 launcher contract；注入旧 `libstdc++.so.6`、注入 x86_64 `/bin/true` 均被拒绝；临时目录已删除。
- 未启动完整 configure/build；QXlsx 新 RUNPATH 需由用户下一次 clean build 产物确认。

## Follow-up: LayerShellQt build-tree absolute RUNPATH

### Field Evidence

用户在 250 完成 `build.sh aarch64` 编译后，新增 audit 正确拒绝了两个 ELF：

- `bin/wayland-shell-integration/liblayer-shell.so` 的 RUNPATH 包含当前
  `build_aarch64_standard_cn/lib` 和 `/opt/Qt/5.15.18/gcc_aarch64/lib`；
- `lib/libLayerShellQtInterface.so.5.27.12` 的 RUNPATH 包含
  `/opt/Qt/5.15.18/gcc_aarch64/lib`。

### Root Cause

`3rdParty/CMakeLists.txt` 为两个 LayerShellQt target 设置了相对 `BUILD_RPATH`
和 `INSTALL_RPATH`，但没有禁止 CMake 把链接目标所在目录自动补入 build-tree
RUNPATH。plugin 直接链接 build tree 中的 `LayerShellQtInterface`，两者又都链接
imported Qt target，因此分别出现 build 绝对目录和 `/opt/Qt` 绝对目录。

### Fix And Success Criteria

1. `layer-shell` 与 `LayerShellQtInterface` 在 build tree 中直接使用各自已有的
   `$ORIGIN` `INSTALL_RPATH`。
2. 禁止把 link path 追加进 install RPATH，并启用 build-tree `$ORIGIN` 语义。
3. 不降低 `build.sh` 的绝对路径审计，不用 `patchelf` 在打包后掩盖构建配置问题。
4. 下一次 clean build 后，两项 `readelf -dW` 输出只能包含 `$ORIGIN` 相对目录；
   `build.sh` audit 应继续执行并通过。

### Verification Level

- `static`：核对 CMake target 所有权、CMake property 语义、CMake 语法和 diff。
- Linux ELF 的最终 RUNPATH 由用户在 250 下一次 clean build 验证。

### Implementation Result

- 在 `3rdParty/CMakeLists.txt` 的 `layer-shell` 与
  `LayerShellQtInterface` target 上增加 `BUILD_WITH_INSTALL_RPATH TRUE`、
  `BUILD_RPATH_USE_ORIGIN TRUE`、`INSTALL_RPATH_USE_LINK_PATH FALSE`。
- 保留两个 target 原有的 `$ORIGIN` `BUILD_RPATH/INSTALL_RPATH`，没有扩大运行时
  搜索范围，也没有降低 `build.sh` audit。
- 本机 CMake property 文档确认：`BUILD_WITH_INSTALL_RPATH` 会让 build-tree target
  直接使用 `INSTALL_RPATH`；`INSTALL_RPATH_USE_LINK_PATH=FALSE` 不追加外部 link
  path；`BUILD_RPATH_USE_ORIGIN` 保持 build-tree 内部依赖的相对语义。
- 静态断言确认两个 target 都包含上述三项属性且 RPATH 配置中没有 `/home`、
  `/opt` 或 `/tmp`；`git diff --check` 通过。
- 本次没有连接 250 或执行完整构建；最终 ELF 由用户下一次 `build.sh aarch64`
  clean build 和既有 audit 验证。
