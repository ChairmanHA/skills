# 192.168.3.250 AArch64 共用包与 Raspberry Pi / RK3588 兼容性说明

## 1. 文档目的与结论

本文说明为什么当前仓库的：

```bash
bash scripts/build.sh raspberry-pi standard cn SGStudio --watermark off
```

可以在 `192.168.3.250` 的 Ubuntu 18.04 x86_64 环境中交叉编译一份 AArch64
程序，并把同一归档部署到 `192.168.3.108` 的 Debian 12/Wayland Raspberry Pi 与
Ubuntu 18.04/X11 RK3588 上直接执行。账号密码不属于设计合同，不写入仓库脚本。

结论分为两层：

1. **当前方案具有明确、可审计的二进制兼容依据。** 250 使用 AArch64
   交叉编译器和 AArch64 Qt 5.15.18 目标前缀；Wayland、xcb、Qt plugins 和
   LayerShellQt 随同一归档携带。除用户已接受的闭源 modulation 库例外外，产物
   ABI 门禁按更旧的 RK3588 运行时设置，因此新一些的 108 也能覆盖。
2. **`build.sh` 能保证的是已知目标的静态兼容包络，不是全部运行行为。** 脚本
   可以在出包前拒绝错误架构、过高 ABI、缺失 Wayland/xcb runtime、构建机绝对
   路径和危险的系统库混装；真实 GPU/compositor、设备权限、硬件通信以及
   Analog/SCPI 业务功能仍必须分别在两台目标机上做运行验收。

因此，更准确的表述不是“250 与 108 环境差异不影响运行”，而是：

> 250 的宿主系统差异已经被交叉工具链隔离；归档内二进制的实际 ABI 需求落在
> 两类目标机的供给能力之内；Qt 用户态运行时随包保持同版；必须绑定目标系统的
> 底层运行库由各自目标机提供。`build.sh` 在打包前验证了这些边界。

这条路径定位为 **250 -> Raspberry Pi / RK3588 的共用交叉包路径**。正式
Raspberry Pi 发布判断仍可优先使用 108 原生 `build_pi.sh` 产物，详见
[Linux 两入口构建说明](linux_build_package_unified_entry.md)。

## 2. 机器身份与凭据边界

| 项目 | 192.168.3.250 | 192.168.3.108 |
| :--- | :--- | :--- |
| 角色 | x86_64 交叉编译机 | AArch64 真实运行机 |
| 登录用户 | `jiashilin` | `htra` |
| 典型仓库目录 | `/home/jiashilin/vsg2.0/sgstudio` | 不要求保存源码，只部署归档 |
| 权限 | 编译使用普通用户 | 图形应用必须使用桌面普通用户启动 |

密码属于部署凭据，不写入仓库文档、脚本、命令行示例或环境文件。本文所有远程
复制示例都让 SSH 客户端交互读取密码；生产环境建议改用受控 SSH key。

## 3. 已确认环境对比

以下数据来自本任务前序检查和已经完成的现场验证。本次文档整理不重新连接两台
机器。

| 项目 | 108 已知正常运行环境 | 250 当前构建环境 | 对兼容性的实际意义 |
| :--- | :--- | :--- | :--- |
| 系统 | Debian 12 / AArch64 | Ubuntu 18.04 / x86_64 | 250 是工具宿主，不是产物运行基线 |
| CPU 架构 | AArch64 | x86_64 | 由交叉编译器产生 AArch64 ELF；脚本再次扫描产物架构 |
| glibc | 2.36 | 宿主 glibc 2.27 | 不能直接比较宿主 glibc；应比较产物 `GLIBC_*` 需求与 108 的 2.36 |
| 本机 GCC | GCC 12.2 | GCC 7.5 | 108 的 GCC 不参与交叉包运行；250 使用 GCC 7 AArch64 交叉工具链生成程序 |
| 交叉目标 | 不适用 | `aarch64-linux-gnu` | 决定生成文件架构和目标 ABI |
| C++ runtime 能力 | `GLIBCXX_3.4.30`、`CXXABI_1.3.13` | 历史产物最高需求为 `GLIBCXX_3.4.22`、`CXXABI_1.3.11` | 108 的系统 `libstdc++` 能满足较旧需求 |
| Qt | 系统 Qt 5.15.8 | AArch64 目标 Qt 5.15.18；另有 x86_64 host tools | 应用运行时使用包内 5.15.18，不与系统 5.15.8 混用 |
| 桌面 | labwc / wlroots / Wayland | 构建机宿主桌面与目标无关 | 共用 profile 携带 Wayland、xcb 与 LayerShellQt |
| Qt platform | 目标机具有 Wayland 会话 | 包含 `qwayland-egl`、`qwayland-generic`、`qxcb` 和 `xdg-shell` plugin | 应用启动前固定包内 plugin 路径并自动选 QPA |
| Layer shell | labwc/wlroots 路径已经用于 SGStudio Minibar | LayerShellQt 与目标 Qt 5.15.18 同次构建、同包部署 | 避免依赖 108 的系统 LayerShellQt 或 Qt private ABI |
| 供应商库 | 需要运行 Analog/SCPI 等功能 | `gcc_aarch64/libgenSignalWave.so` | 已测库最高要求 `GLIBC_2.29`，低于 108 的 2.36 |

RK3588 的历史现场基线是 Ubuntu 18.04.5、AArch64、glibc 2.27、
`GLIBCXX_3.4.25`、`CXXABI_1.3.11` 与 LXDE/Xorg。当前没有可连接的 RK3588 地址，
因此这些数据是构建门禁和待验收基线，不是假装已经完成的本次运行结论。

共用包以 RK3588 的较旧 ABI 作为一般上限：`GLIBC_2.27`、
`GLIBCXX_3.4.25`、`CXXABI_1.3.11`。用户明确要求不把闭源 modulation 库作为硬
门槛，因此仅允许归档内 `lib/libgenSignalWave.so` 的 GLIBC 需求到已知
`GLIBC_2.29`；该例外不会匹配其他路径、其他 ABI 家族或未来更高版本。

这里最容易产生误解的是“250 的 glibc 是 2.27，为什么包里的供应商库却能要求
`GLIBC_2.29`”。原因是 `libgenSignalWave.so` 是预编译 AArch64 文件，并不是由
250 的 x86_64 glibc 构建出来的。最终判断必须读取每个目标 ELF 自己声明的版本化
符号需求，不能只查看构建宿主版本。

## 4. 必须区分的三个环境层次

### 4.1 构建宿主层：250 的 Ubuntu 18.04 x86_64

这一层负责运行 CMake、Make、moc、uic、rcc 和编译器进程。它的可执行文件必须是
x86_64，但它不决定最终 SGStudio 的 CPU 架构。

250 的 GNOME/Xorg 或其他宿主桌面也不进入目标程序。构建过程中不会把“当前桌面
会话”编译进 ELF；aarch64 profile 同时构建两套 QPA，运行程序再根据目标桌面
选择 Wayland 或 xcb。

### 4.2 目标构建层：250 上的 AArch64 工具链与 Qt 前缀

真正决定目标二进制的是：

```text
aarch64-linux-gnu-gcc
aarch64-linux-gnu-g++
/opt/Qt/5.15.18/gcc_aarch64
/opt/Qt/5.15.18/gcc_aarch64-hosttools
```

- GCC/G++ 产生 AArch64 机器码。
- `gcc_aarch64` 保存目标 AArch64 Qt 库、headers 和 plugins。
- `gcc_aarch64-hosttools` 保存可在 250 上运行的 x86_64 Qt 代码生成工具。
- 代码生成工具的架构可以是 x86_64；被链接的 Qt 库必须是 AArch64。

[AArch64 toolchain](../../scripts/aarch64.cmake) 显式设置目标系统为 Linux、处理器为
`aarch64`、编译器为 `aarch64-linux-gnu-gcc/g++`，并把目标 Qt 与 host tools
分开。当前 toolchain 没有完整发行版 sysroot，因此出包后的全量 ELF 审计是必需
的第二道防线，而不是可选优化。

### 4.3 真实运行层：108 的 Debian 12 AArch64

这一层负责：

- 用 AArch64 loader 装载程序；
- 提供 glibc、`libstdc++`、`libgcc_s`；
- 提供 Mesa/GPU、EGL/GL、DRM/GBM；
- 提供 Wayland socket、compositor 和输入设备；
- 提供用户权限、设备节点、网络与 USB 环境。

包内 Qt 5.15.18 位于这些系统能力之上。把三层分开后，可以看到“250 与 108
发行版不同”本身并不是阻断条件；阻断条件是目标 ELF 架构错误、声明了 108 不提供
的符号版本，或错误捆绑了必须与 108 内核/驱动配套的系统库。

## 5. ABI 为什么按当前方向兼容

### 5.1 兼容方向

Linux 版本化符号的基本判断是：

```text
产物要求的最高版本 <= 目标系统能够提供的最高版本
```

对当前历史包与两个目标：

| ABI 家族 | 250 历史 AArch64 包最高需求 | RK3588 供给上限 | 108 供给上限 | 结论 |
| :--- | :--- | :--- | :--- | :--- |
| glibc | 一般 ELF `2.27`；闭源库 `2.29` | `GLIBC_2.27` | `GLIBC_2.36` | 一般 ELF 双端满足；闭源库是用户接受的 RK 例外 |
| libstdc++ | `GLIBCXX_3.4.22` | `GLIBCXX_3.4.25` | `GLIBCXX_3.4.30` | 双端满足 |
| C++ ABI | `CXXABI_1.3.11` | `CXXABI_1.3.11` | `CXXABI_1.3.13` | 双端满足 |

这里的“低版本需求由高版本运行时满足”依赖 glibc/libstdc++ 的版本化符号兼容
机制。它不等于所有 Linux 库都能任意跨版本替换，也不保证反向兼容。

例如：

- 当前闭源库可以要求 `GLIBC_2.29`，108 的 2.36 可以提供它；RK3588 的 2.27
  不能提供它，这一点是用户接受的已知业务例外，不应表述为静态兼容。
- 如果在 108 上用 GCC 12 原生构建，产物可能要求 `GLIBC_2.36` 或
  `GLIBCXX_3.4.30`；这个产物不能反向部署到 glibc 2.27 的老系统。
- 如果以后替换供应商 `.so`，仅凭文件名相同不能继承当前结论，必须重新审计它
  的 `GLIBC_*`、`GLIBCXX_*` 和 `CXXABI_*`。

### 5.2 为什么不把旧 glibc 一起打包

glibc 不只是普通 `.so`。动态 loader、glibc、NSS、线程、DNS、内核接口之间有
紧密耦合。把 250 或某个 sysroot 的 `libc.so`、`ld-linux` 复制到应用目录，通常
会制造比原问题更隐蔽的崩溃。

当前方案反而要求：

- 包内程序只声明 108 能提供的 glibc 版本；
- 运行时使用 108 自己的 loader 和 glibc；
- `build.sh` 发现包内出现 `libc.so*`、`ld-linux*`、`libpthread.so*`、
  `libdl.so*`、`librt.so*`、`libm.so*` 等文件时直接失败。

这比“打包足够多编译环境”更稳健。应用级依赖可以随包，内核/驱动/基础 C runtime
应由目标系统提供。

## 6. Qt 5.15.18 与目标系统 Qt 5.15.8 为什么不冲突

当前交叉包不是依赖 108 的系统 Qt 5.15.8，而是携带一组来自同一目标前缀的 Qt
5.15.18 runtime：

```text
lib/libQt5Core.so.5
lib/libQt5Gui.so.5
lib/libQt5Widgets.so.5
lib/libQt5WaylandClient.so.5
bin/platforms/libqwayland-egl.so
bin/platforms/libqwayland-generic.so
bin/wayland-shell-integration/libxdg-shell.so
bin/wayland-shell-integration/liblayer-shell.so
lib/libLayerShellQtInterface.so.5
```

关键点不是 5.15.18 与 5.15.8 是否“差得少”，而是运行时不允许混用：

1. 主程序的可继承 `DT_RPATH` 把 ELF 依赖指向归档内 `lib` 与 `plugin`，不依赖
   `LD_LIBRARY_PATH`。
2. `bin/qt.conf` 把 `Libraries` 指向归档内 `lib`，把 `Plugins` 指向归档内
   `bin`。
3. 主程序在创建 `QApplication` 前覆盖 `QT_PLUGIN_PATH` 与
   `QT_QPA_PLATFORM_PLUGIN_PATH`，防止目标桌面继承的系统或 `/opt/Qt` plugin
   路径污染当前进程。
4. LayerShellQt interface 与 plugin 使用同一 Qt 5.15.18 目标构建和部署，避免
   Qt private ABI 跨补丁版本混合。

所以 108 安装 Qt 5.15.8 并不意味着交叉包必须改用 5.15.8。系统 Qt 可以继续供
系统应用使用，SGStudio 自己在启动早期建立独立的包内 Qt 解析环境。正式入口就是
`bin/SGStudio`（其他品牌包使用对应可执行文件名）；根 launcher 只是兼容工具。

RPATH 的详细背景见 [RPATH 机制](RPATH_MECHANISM.md)。

## 7. Wayland 与 LayerShellQt 边界

`build.sh raspberry-pi` 明确配置：

```text
DISPLAY_BACKEND=Wayland + X11 (runtime auto-select)
SGS_ENABLE_VENDORED_LAYER_SHELL_QT=ON
```

脚本在配置前要求目标 Qt 前缀中存在：

- `libQt5WaylandClient.so.5`；
- `libqwayland-egl.so`；
- `libqwayland-generic.so`；
- `libxdg-shell.so`。

脚本还要求 `libqxcb.so` 存在，并把 Wayland 与 xcb QPA plugin 都放入必需依赖
闭包。打包后要求 LayerShellQt interface/plugin、Qt Wayland/xcb runtime 和
`qt.conf` 全部存在且为 AArch64 ELF。108 的 labwc/wlroots compositor 提供
Wayland 与 layer-shell 协议端；RK3588 的 Xorg 提供 X11 display。包内
Qt/LayerShellQt 是客户端实现。

250 当前运行的是何种桌面不影响这个结论。主程序先看 `XDG_SESSION_TYPE`，再以
Wayland socket 或 `DISPLAY` 和对应包内 QPA plugin 做可用性门禁：108 优先选择
Wayland，RK3588 Xorg 选择 xcb。有关 Minibar 的目标端行为见
[Wayland LayerShellQt 集成说明](minibar_wayland_layer_shell_qt_integration.md)。

## 8. 包内携带与目标系统提供的依赖边界

### 8.1 应随包携带

- SGStudio 主程序、helper、updater 和业务 plugins；
- 与本次构建一致的 Qt 5.15.18 libraries；
- Qt image/platform/Wayland 等 plugins；
- 与该 Qt 构建一致的 LayerShellQt interface/plugin；
- 仓库第三方库和供应商 AArch64 库；
- 上述文件的应用级间接依赖，例如 ICU、`libxcb-*`、`libxkbcommon*`、
  `libpng16`、`libfreetype`、`libgomp.so.1`；
- `qt.conf`、配置和资源；根目录 launcher 可作为兼容工具保留，但不是运行依赖。

`build.sh` 用目标 `readelf` 从 `DT_NEEDED` 展开依赖闭包，因此这些间接依赖是被
自动发现并复制进 `lib/` 的，而不是靠手工维护列表。Wayland 与 xcb platform
plugin 都是共用包的必需种子。交叉编译不能使用 `ldd`，因为 `ldd` 需要执行目标
架构的动态 loader。

### 8.2 必须由 108 提供

- glibc 与动态 loader；
- `libstdc++` 与 `libgcc_s`；
- GL/EGL/GLES、Mesa、DRM、GBM 及 GPU vendor driver；
- `libwayland-*` 客户端库；
- `libudev` / `libsystemd` / `libusb-1.0`；
- 与内核、桌面和系统服务绑定的基础 runtime；
- Wayland compositor、socket、seat/input 与设备权限。

`libwayland-client` 是最典型的不可捆绑例子：250 提供 Wayland 1.16，108 提供
1.21，而 108 的 Mesa（`libEGL` / `libwayland-egl`）需要 Wayland 1.20 引入的
`wl_proxy_marshal_flags`。两者 soname 都是 `libwayland-client.so.0`，同一进程
只会装载一个实现，归档内的旧版本会让目标机 Mesa 以
`undefined symbol: wl_proxy_marshal_flags` 失败。

`build.sh` 对以下包内文件执行硬拒绝：

```text
libc / ld-linux / libpthread / libdl / librt / libm / libresolv / libnss
libstdc++ / libgcc_s
libGL / libEGL / libGLES / libdrm / libgbm
libwayland-* / libudev / libsystemd / libusb-1.0
```

其目的不是减少归档体积，而是避免用 250 的旧系统库覆盖 108 与内核、GPU 和桌面
配套的新系统库。

依赖闭包分两级：主程序、业务 plugins、updater、当前 QPA 的 platform plugin 无
法解析依赖时打包直接失败；其余 Qt plugin 组（例如 GTK platform theme、CUPS
print support）只打印警告，因为 Qt 在加载失败时会跳过它们。

## 9. `build.sh raspberry-pi` 当前提供的保护

### 9.1 配置前保护

脚本首先验证：

- `aarch64-linux-gnu-gcc/g++`、`file`、`strings`、目标 `readelf` 等工具存在；
- 编译器 `-dumpmachine` 结果确实是 AArch64；
- 目标 Qt prefix、libraries、plugins 和 x86_64 host tools 路径存在；
- 必需 Qt Wayland runtime 在配置前已经完整。

这能防止最常见的“在 x86_64 宿主上误链接 x86_64 Qt”错误。

### 9.2 构建和收集保护

aarch64 profile 固定使用：

- [AArch64 toolchain](../../scripts/aarch64.cmake)；
- `/opt/Qt/5.15.18/gcc_aarch64` 目标 Qt；
- `/opt/Qt/5.15.18/gcc_aarch64-hosttools`；
- `gcc_aarch64` 供应商库目录；
- Wayland + xcb 双 QPA runtime，以及 vendored LayerShellQt；
- Analog 与 SCPI 正常启用。

Qt runtime、Qt plugins、业务 plugins、供应商库和编译器 runtime 被集中收集到
同一个 build/package layout。QXlsx 及 LayerShellQt 的 RPATH 使用 `$ORIGIN`
相对位置，避免把 `/opt/Qt/...` 写入可移植归档。

2026-08-24 的首次完整交叉构建进一步发现：仅设置 `BUILD_RPATH` 与
`INSTALL_RPATH` 还不够，CMake 会把 build tree 中被链接 target 的目录和 imported
Qt 的 `/opt/Qt/.../lib` 自动追加到 build RUNPATH。当前 QXlsx、`layer-shell` 和
`LayerShellQtInterface` target 都同时使用 `BUILD_WITH_INSTALL_RPATH=TRUE`、
`BUILD_RPATH_USE_ORIGIN=TRUE`、`INSTALL_RPATH_USE_LINK_PATH=FALSE`。因此打包直接
使用的 build-tree ELF 从链接时就只包含约定的 `$ORIGIN` 相对目录，而不是在归档
阶段用 `patchelf` 掩盖构建配置问题。

### 9.3 打包前全量 ELF 审计

`verify_shared_aarch64_runtime` 扫描 `bin`、`lib`、`plugin`、`updater` 下所有
ELF，并执行：

1. 必需主程序、Qt Wayland、xcb、LayerShellQt runtime 和 `libgomp.so.1` 必须存在。
2. 每一个 ELF 都必须是 ARM AArch64。
3. 一般 ELF 最高版本需求不得超过 RK3588 已知上限：`GLIBC_2.27`、
   `GLIBCXX_3.4.25`、`CXXABI_1.3.11`；仅
   `lib/libgenSignalWave.so` 的 GLIBC 需求允许到用户接受的 `2.29`，并打印警告。
4. 不得在归档中捆绑前述 glibc、C++ runtime 或 GPU driver runtime。
5. ELF 的 `NEEDED`、RPATH 或 RUNPATH 中不得残留 `/home`、`/opt`、`/tmp`
   构建机绝对路径。
6. 任一错误使构建失败，不生成看似成功但已知不兼容的测试包。

如果闭源库以后要求高于 `GLIBC_2.29`、出现更高的 GLIBCXX/CXXABI，或另一个 ELF
要求高于 RK3588 上限，打包会失败。

### 9.4 staging 后直接启动合同审计

归档生成前，脚本还验证：

- `bin/<application>` 存在且可执行；
- 主程序包含可继承的 `$ORIGIN/../lib` `DT_RPATH`；
- `qt.conf` 的 `Plugins=bin`、`Libraries=lib` 使用相对路径。

Qt plugin 隔离和 QPA 选择由 `main.cpp` 在 `QApplication` 前完成，不再由 launcher
承担。交叉构建机不能运行 AArch64 `ldd`，所以最终仍要在两个目标机上清空
`LD_LIBRARY_PATH` 做直接启动与依赖验收。

## 10. `build.sh` 能保证和不能保证的内容

| 类别 | 当前脚本的保证 | 仍需两端实测 |
| :--- | :--- | :--- |
| CPU 架构 | 全包 ELF 均为 AArch64 | CPU 特定性能和负载 |
| glibc/C++ ABI | 不超过 108 已知版本上限 | 未扫描的运行期 `dlopen` 外部文件 |
| Qt | 必需 Qt 5.15.18、Wayland 与 xcb plugins 随包完整 | 两种图形栈的实际初始化、渲染和输入 |
| LayerShellQt | interface/plugin 存在、架构正确、同包部署 | compositor 协议及 Minibar 交互 |
| loader/QPA | 无构建机绝对路径；直接启动 RPATH、qt.conf、双 QPA 合同完整 | 目标桌面中直接执行时是否选到预期 QPA |
| 系统库隔离 | 拒绝 glibc/libstdc++/GPU 库混装 | 108 是否安装了所有系统依赖 |
| Analog/SCPI | plugins 与供应商库进入构建和审计 | 设备连接、波形生成、SCPI 命令功能 |
| 硬件 | 无法由交叉构建静态保证 | USB/ETH、设备节点、权限、驱动和真实业务 |

所以，“保证在 108 运行”的严谨定义应为：

> 如果目标仍符合已知 ABI 基线、对应 Wayland/X11 桌面和系统图形库存在，并在
> 普通用户的已登录图形会话中直接执行 `bin/<application>`，脚本会拒绝除已声明
> modulation 例外之外的当前已知静态不兼容产物。最终启动和业务正确性必须由
> 108 与 RK3588 分别实测闭环。

## 11. 推荐构建、传输和启动流程

### 11.1 在 250 构建

以 `jiashilin` 普通用户进入仓库：

```bash
cd /home/jiashilin/vsg2.0/sgstudio
JOBS="$(nproc)" bash scripts/build.sh \
  raspberry-pi standard cn SGStudio --watermark off
```

成功时应看到类似：

```text
== Verify shared aarch64 runtime for Raspberry Pi Wayland and RK3588 X11 ==
== Shared aarch64 package audit passed for ... ELF files ==
== Package launch policy: run bin/SGStudio directly; launcher is optional ==
```

归档位置为：

```text
/home/jiashilin/vsg2.0/sgstudio/build/linux_raspberry_pi_cross_aarch64/standard/cn/SGStudio.tar.gz
```

生成后先记录校验值：

```bash
cd /home/jiashilin/vsg2.0/sgstudio
sha256sum build/linux_raspberry_pi_cross_aarch64/standard/cn/SGStudio.tar.gz
tar -tzf build/linux_raspberry_pi_cross_aarch64/standard/cn/SGStudio.tar.gz | head -n 30
```

### 11.2 从 250 复制到 108

```bash
scp build/linux_raspberry_pi_cross_aarch64/standard/cn/SGStudio.tar.gz \
  htra@192.168.3.108:/home/htra/
```

不要把密码写进脚本或命令参数。传输后在 108 重算 SHA-256，并与 250 输出比较。

### 11.3 在 108 解包

在 108 的 `htra` 桌面用户终端中：

```bash
archive="$HOME/SGStudio.tar.gz"
deploy_root="$HOME/SGStudio-test-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$deploy_root"
sha256sum "$archive"
tar -xzf "$archive" -C "$deploy_root"
cd "$deploy_root/SGStudio"
```

使用时间戳目录可以保留旧测试版本，便于回滚和对比，不需要覆盖当前可运行目录。

### 11.4 直接启动

首次验收使用前台方式：

```bash
cd "$deploy_root/SGStudio"
./bin/SGStudio
```

不要使用 `sudo`，也不要把 Raspberry Pi 兼容 launcher 当作 RK3588 的入口：

```bash
sudo ./bin/SGStudio
./launch_sgstudio_pi.sh  # RK3588 上会强制 Wayland，不能代表共用包合同
```

使用 `sudo` 会脱离当前桌面的 Wayland/X11、DBus 与音频会话，并可能生成 root
所有的日志、配置和实例状态文件。纯 SSH 或 system service 同样不是默认支持入口。

## 12. 两个平台首次验收清单

### 12.1 静态加载检查

在归档根执行：

```bash
file bin/SGStudio

env -u LD_LIBRARY_PATH ldd bin/SGStudio | tee /tmp/sgstudio-ldd.txt

if grep -q 'not found' /tmp/sgstudio-ldd.txt; then
  echo 'ERROR: unresolved runtime dependencies'
else
  echo 'OK: direct runtime dependencies resolved'
fi
```

`file` 应显示 ARM AArch64。`ldd` 不应出现 `not found`。

### 12.2 Qt/Wayland 加载检查

```bash
QT_DEBUG_PLUGINS=1 ./bin/SGStudio 2>&1 | tee /tmp/sgstudio-qt-startup.log
```

重点检查：

- platform plugin 来自当前归档 `bin/platforms`；
- 加载的是包内 Qt 5.15.18，而不是 `/usr/lib` 的 Qt 5.15.8；
- Raspberry Pi 的 QPA 为 `wayland`；RK3588 的 QPA 为 `xcb`；
- `liblayer-shell.so` 与 `libLayerShellQtInterface.so.5` 能加载；
- 没有 `GLIBC_* not found`、`GLIBCXX_* not found` 或错误 ELF class/machine。

### 12.3 UI 与业务检查

至少覆盖：

1. 主窗口显示、缩放、菜单、输入和关闭流程；
2. Minibar 打开、拖动、键盘、Sweep/MOD/menu overlay 与 outside-click；
3. Analog plugin 加载及使用 `libgenSignalWave.so` 的实际波形路径；
4. SCPI plugin 启动、连接、命令处理和错误回报；
5. ETH/USB 设备发现、连接、配置和连续运行；
6. updater/helper 等独立进程从包内 runtime 正常启动；
7. 运行日志中没有意外加载 `/usr/lib/.../libQt5*.so`。

只有在两端分别完成这些检查后，才能把同一归档标记为该版本的双平台共用产物。

## 13. 常见错误与判断

| 现象 | 首要判断 | 处理原则 |
| :--- | :--- | :--- |
| `GLIBC_2.xx not found` | 使用了未通过当前 audit 的包、目标环境变化，或 RK3588 实际加载了用户接受的 modulation 例外 | 检查问题 ELF；不要复制另一套 glibc |
| `GLIBCXX_3.4.xx not found` | 目标 `libstdc++` 能力不足或误捆绑旧库 | 查 `ldd`/loader 路径；不要在包里塞 250 的 `libstdc++` |
| `wrong ELF class` / machine 不符 | 混入 x86_64 library/plugin | 构建应由全量架构审计拒绝；清理后重建 |
| `libQt5Core.so.5 not found` | 主程序 RPATH 不合格或归档不完整 | 用 `readelf -dW bin/SGStudio` 核对 `DT_RPATH` 和 `lib/` |
| `Could not load the Qt platform plugin "wayland"` | plugin 路径、间接依赖或 Wayland 会话错误 | 用 `QT_DEBUG_PLUGINS=1` 查看精确缺项 |
| Raspberry Pi 误选 `xcb` | `XDG_SESSION_TYPE`/Wayland socket 异常，程序回退到了 Xwayland | 核对桌面变量和 `/run/user/<uid>/<WAYLAND_DISPLAY>` |
| RK3588 误选 Wayland | 会话变量与真实 Xorg 环境不一致 | 核对 `XDG_SESSION_TYPE=x11`、`DISPLAY` 和启动用户 |
| LayerShellQt plugin 失败 | interface/plugin 缺失、Qt 混装或 compositor 协议问题 | 先查包内路径，再查 108 compositor |
| EGL/DRM/GBM 错误 | 108 的目标图形驱动/权限问题 | 修复目标系统；不要从 250 复制 Mesa/GPU 库 |
| 主程序能开，Analog/SCPI 失败 | 运行期供应商库、设备或业务路径问题 | 单独检查 plugin 日志、vendor `.so` 和真实设备 |
| SSH 启动找不到 Wayland display | SSH 会话没有继承桌面用户环境 | 优先从 108 当前桌面终端启动，不使用 root |

## 14. Raspberry Pi 原生构建与共用交叉包的关系

250 交叉路径当前是可解释、可审计并很适合快速 field test 的方案，但 108 原生
构建更贴近真实系统：

- 编译器、headers、Qt、Wayland development packages 与目标完全一致；
- 构建后立即在同一 arm64 用户空间执行无 `LD_LIBRARY_PATH` 的依赖检查；
- 可以从真实 labwc 桌面直接运行 `bin/SGStudio`；
- 不存在交叉 Qt 5.15.18 与系统 Qt 5.15.8 的差异。

这不是说 250 的包必然不可靠。两条路径的定位是：

| 路径 | 适用目的 |
| :--- | :--- |
| `build.sh raspberry-pi` on 250 | Raspberry Pi Wayland + RK3588 X11 的唯一共用归档、静态门禁和双端现场验证 |
| `build_pi.sh` on 108 | 正式 Raspberry Pi 发布判断、直接执行合同与完整回归 |

108 原生包只能代表 Raspberry Pi，不能替代“同一产物在两端运行”的要求。共用
发布必须使用 250 的 `raspberry-pi` 归档，并记录归档 SHA-256、108 与 RK3588 的
直接启动验收结果，避免只凭“编译成功”判断。

## 15. 最终判断

当前 `build.sh raspberry-pi` 产物具备跨 Raspberry Pi 与 RK3588 的静态共用基础，
依赖的是以下闭环：

1. 用明确的 AArch64 GCC/Qt 目标环境生成 AArch64 ELF；
2. 一般 ELF 按 RK3588 的旧 ABI 门禁，并把用户接受的 modulation 例外限制在单一文件；
3. Qt 5.15.18、Wayland/xcb plugins 和 LayerShellQt 同版随包，不混用目标系统 Qt；
4. glibc、C++ runtime 和 GPU/桌面系统层留给各自目标机，避免旧库污染；
5. 对全包执行架构、ABI、动态路径、双 QPA runtime 和直接启动合同硬检查；
6. 在 `QApplication` 前建立包内 Qt 隔离并自动选择 Wayland/xcb；
7. 最后由 108 与 RK3588 的真实桌面、GPU、设备和业务功能测试完成验收。

除已明确接受且尚未在 glibc 2.27 上成立的闭源库例外外，这不是依赖“版本大概没
问题”的经验猜测，而是有版本化符号、运行库所有权、双 QPA 和出包审计共同支撑的
兼容方案。能否标记为“已工作”仍以同一 SHA-256 归档在两端的实测结果为准。
