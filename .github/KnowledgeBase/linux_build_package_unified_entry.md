# Linux 两入口构建、打包与启动说明

## 1. 最终模型

Linux 只保留两个构建入口：

```text
192.168.3.250 (Ubuntu 18.04 / x86_64)
└── scripts/build.sh
    ├── ubuntu-aarch64
    ├── ubuntu-x86_64
    └── raspberry-pi

192.168.3.108 (Debian 12 / AArch64 Raspberry Pi)
└── scripts/build_pi.sh
    └── native AArch64 / Wayland
```

Win32 继续使用 `scripts/build.bat`。

核心原则：

- 250 是日常多目标构建机，一个 `build.sh` 直接拥有三种 profile，不再经过
  router、Docker wrapper 或 RK3588 wrapper。
- `raspberry-pi` profile 是 AArch64 双桌面超集：同一归档携带 Wayland、xcb 与
  LayerShellQt，可在 Raspberry Pi Wayland 和 RK3588 X11 中直接执行。
- 108 是真实运行机，`build_pi.sh` 使用其 GCC 12、Qt 5.15.8、glibc 2.36 和
  Wayland 环境原生构建，作为最终发布防火墙。
- 250 生成的 Raspberry Pi 包用于快速交叉验证；遇到无法解释的 ABI、Qt 或 UI
  差异时，以 108 原生包结果为准。

## 2. 250：统一使用 `build.sh`

用法：

```bash
bash scripts/build.sh \
  <target> <packet> <language> [archive_name] [--watermark on|off]
```

查看目标：

```bash
bash scripts/build.sh --list
```

### 2.1 三个正式 target

| target | CPU | 目标桌面 | 编译方式 | Qt | 主要用途 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `ubuntu-aarch64` | AArch64 | X11/xcb | GCC 7 交叉编译 | `/opt/Qt/5.15.18/gcc_aarch64` | RK3588/Ubuntu AArch64 包 |
| `ubuntu-x86_64` | x86_64 | X11/xcb | 250 本机构建 | `/opt/Qt/5.15.18/gcc_x86_64` | Ubuntu x86_64 包 |
| `raspberry-pi` | AArch64 | Wayland + X11 自动选择 | GCC 7 交叉编译 | `/opt/Qt/5.15.18/gcc_aarch64` | 108 与 RK3588 共用的交叉包 |

旧参数 `rk3588`、`gcc`、`gcc_x86_64`、`x86_64`、`aarch64` 仍作为兼容别名，
新命令和文档只使用上表三个明确名称。

### 2.2 Ubuntu AArch64

```bash
cd /home/jiashilin/vsg2.0/sgstudio
JOBS="$(nproc)" bash scripts/build.sh \
  ubuntu-aarch64 standard cn SGStudio --watermark off
```

输出：

```text
build/build_rk3588_standard_cn/
build/linux_ubuntu_aarch64/standard/cn/SGStudio.tar.gz
```

该 profile：

- 使用 `aarch64-linux-gnu-gcc/g++` 7；
- 使用 AArch64 Qt 5.15.18 target prefix 与 x86_64 host tools；
- 使用 X11/xcb launcher；
- Analog/SCPI 正常开启；
- 扫描归档中所有 ELF，确保没有混入 x86_64 文件。

Ubuntu AArch64 目标的实际 ABI 与功能兼容性由目标机验收。

### 2.3 Ubuntu x86_64

```bash
cd /home/jiashilin/vsg2.0/sgstudio
JOBS="$(nproc)" bash scripts/build.sh \
  ubuntu-x86_64 standard cn SGStudio --watermark off
```

输出：

```text
build/build_gcc_standard_cn/
build/linux_ubuntu_x86_64/standard/cn/SGStudio.tar.gz
```

该 profile 使用 250 本机 GCC、Qt 5.15.18 x86_64 和 X11/xcb，不构建
LayerShellQt。目标必须在 X11 桌面普通用户会话中验收。

### 2.4 Raspberry Pi / RK3588 共用交叉包

```bash
cd /home/jiashilin/vsg2.0/sgstudio
JOBS="$(nproc)" bash scripts/build.sh \
  raspberry-pi standard cn SGStudio --watermark off
```

输出：

```text
build/build_aarch64_standard_cn/
build/linux_raspberry_pi_cross_aarch64/standard/cn/SGStudio.tar.gz
```

该 profile 使用 GCC 7 AArch64 交叉工具链、Qt 5.15.18、QtWayland、xcb 与
LayerShellQt。LayerShellQt 只在 Wayland MiniBar helper 中启用；X11 下 helper
走现有普通 Qt 窗口路径。打包前会检查：

- Qt Wayland、xcb 与 LayerShellQt runtime 全部完整，且两套 QPA plugin 的必需
  间接依赖都能解析；
- 所有 ELF 均为 AArch64；
- 除用户明确接受的闭源 modulation 库外，不超过 RK3588 已知的
  `GLIBC_2.27`、`GLIBCXX_3.4.25`、`CXXABI_1.3.11`；例外仅限归档内
  `lib/libgenSignalWave.so` 的 `GLIBC_2.29`，不会放宽其他 ELF；
- 不捆绑 glibc、loader、libstdc++、libgcc 或 GPU/Mesa/DRM runtime；
- 不保留 `/home`、`/opt`、`/tmp` 构建机动态路径；
- 主程序具有可继承的 `$ORIGIN/../lib` `DT_RPATH`，`qt.conf` 使用包内相对路径。

正式入口是归档内 `bin/<application>`。程序在创建 `QApplication` 前把 Qt plugin
路径固定到当前包，并根据 `XDG_SESSION_TYPE`、Wayland socket、`DISPLAY` 和包内
platform plugin 选择 `wayland` 或 `xcb`，因此不需要启动脚本注入
`QT_QPA_PLATFORM`、`QT_PLUGIN_PATH` 或 `LD_LIBRARY_PATH`。归档根 launcher 仅保留为
Raspberry Pi 兼容入口，不是共用产物的启动条件，也不应在 RK3588 上使用。

## 3. 108：只使用 `build_pi.sh`

108 是原 192.168.3.132 运行机，当前地址为（有可能变化，以实际地址为准）：

```text
192.168.3.108
```

推荐命令：

```bash
cd /home/htra/Desktop/SGSProject
JOBS="$(nproc)" bash scripts/build_pi.sh \
  standard cn SGStudio --watermark off
```

旧命令中的前导 `aarch64` 暂时仍接受：

```bash
bash scripts/build_pi.sh aarch64 standard cn SGStudio --watermark off
```

但它已经没有区分作用，新脚本只允许在 AArch64 主机运行。

输出：

```text
build/build_pi_native_standard_cn/
build/linux_pi_native_aarch64/standard/cn/SGStudio.tar.gz
```

### 3.1 为什么它是最终防火墙

`build_pi.sh` 在与生产运行完全相同的机器上使用：

- AArch64 CPU 与动态 loader；
- Debian 12 / glibc 2.36；
- GCC 12.2 与系统 libstdc++；
- Qt 5.15.8 与同机 QtWayland private headers/plugins；
- labwc/wlroots/Wayland 系统库；
- 真实 GPU、Mesa、EGL/DRM/GBM 用户态边界。

因此它不会引入 250 的 GCC 7、Qt 5.15.18 或 Ubuntu 18.04 宿主差异。构建成功
仍不等于全部业务验证成功，但 ABI、Qt 和系统图形栈的一致性最高。

### 3.2 原生包直接启动合同

原生包不生成根 launcher，正式入口为：

```bash
<package>/bin/SGStudio
```

neutral、BNC、俄文包使用对应可执行文件名。

`build_pi.sh` 在生成归档前硬检查：

1. 主程序 RUNPATH 包含 `$ORIGIN/../lib` 与 `$ORIGIN/../plugin`；
2. `bin/qt.conf` 使用 `Prefix=..`、`Plugins=bin`、`Libraries=lib`；
3. 清空 `LD_LIBRARY_PATH` 后，`bin/lib/plugin/updater` 中所有 ELF 的 `ldd`
   均无 `not found`；
4. 主程序直接解析的 Qt5 libraries 来自当前包的 `lib/`；
5. Qt Wayland、LayerShellQt、platform plugins 与必要应用 runtime 完整。

2026-08-25 已在 108 对现有原生产物只读取证：`bin/SGStudio` 的 RUNPATH 为
`$ORIGIN/../lib:$ORIGIN/../plugin`，移除 `LD_LIBRARY_PATH` 后 SGStudio 自有库、
Qt 与 QXlsx 仍从相邻 `lib/` 正确解析。

### 3.3 如何启动

在 108 已登录的 `htra` Wayland 桌面终端中：

```bash
cd /path/to/SGStudio
./bin/SGStudio
```

不要使用 `sudo`。直接启动依赖当前桌面会话已经提供：

- `XDG_RUNTIME_DIR`；
- `WAYLAND_DISPLAY`；
- session DBus；
- PulseAudio/PipeWire 环境；
- 当前用户对输入、GPU 和设备节点的权限。

从纯 SSH shell 直接执行不属于默认支持场景，因为它可能没有继承这些桌面变量。

## 4. 交叉包与原生包的启动区别

| 包来源 | 启动入口 | 原因 |
| :--- | :--- | :--- |
| 250 `ubuntu-aarch64` | `bin/<application>` 或根 X11 launcher | 主程序使用可继承的 `DT_RPATH`，包内依赖闭包完整 |
| 250 `ubuntu-x86_64` | `bin/<application>` 或根 X11 launcher | 主程序使用可继承的 `DT_RPATH`，包内依赖闭包完整 |
| 250 `raspberry-pi` | `bin/<application>` | 程序启动前隔离包内 Qt，并按真实会话自动选择 Wayland/xcb |
| 108 `build_pi.sh` | `bin/<application>` | 同机原生构建；RPATH、qt.conf 与无环境变量 ldd 已验证 |

Linux 包能直接执行的 loader 前提是主程序带 `DT_RPATH`（而不是 `DT_RUNPATH`）。
`DT_RUNPATH` 只对声明它的对象生效，不会沿依赖链继承，因此自身没有 RUNPATH 的
Qt 库找不到相邻的 ICU。`src/CMakeLists.txt` 用 `-Wl,--disable-new-dtags` 产生
`DT_RPATH`，`build.sh` 在出包前对三个 target 都硬检查这一点，x86_64 还会在清空
`LD_LIBRARY_PATH` 后用 `ldd` 确认没有 `not found`；交叉目标不能在 x86_64 构建机
上安全执行 `ldd`，因此由 `readelf` 依赖闭包和目标机验收补齐。

QPA 与 loader 是两个不同边界：`DT_RPATH` 解决 ELF 间接库，应用启动前 bootstrap
解决 Qt plugin 路径和 Wayland/xcb 选择。两个 launcher 模板仍保留为兼容便利入口；
它们不再是 `raspberry-pi` 归档的正确性前提。

## 5. packet、language 与 archive

两个入口共享业务参数：

```text
<packet> <language> [archive_name] [--watermark on|off]
```

| packet/language | 可执行文件 | 默认归档根 |
| :--- | :--- | :--- |
| `standard cn/en` | `SGStudio` | `SGStudio` |
| `standard ru` | `СПО ГСРВ` | `SGStudio` |
| `neutral *` | `VSG` | `VSG` |
| `BNC en` | `VectorCore` | `VectorCore` |

两个脚本每次都清空自己的 build tree。显式 `--watermark off` 会在 staging 中
删除正式包不应携带的内部 Settings.ini 选项，但不修改源码配置。

## 6. 保留与删除的脚本

### 6.1 用户入口

```text
scripts/build.bat     Win32
scripts/build.sh      250 Linux 三目标
scripts/build_pi.sh   108 Raspberry Pi 原生
```

### 6.2 保留的支持脚本

| 文件 | 原因 |
| :--- | :--- |
| `aarch64.cmake` | 250 AArch64 交叉 toolchain |
| `gcc.cmake` | 250 x86_64 toolchain |
| `launch_pi.sh` | 250 Raspberry Pi 兼容 launcher 模板；共用包正式入口仍为 `bin/<application>` |
| `launch_linux_x11.sh` | 250 Ubuntu 包 X11 launcher 模板 |
| `install_pi_runtime_deps.sh` | Raspberry Pi 运行依赖准备 |
| `audit_linux_package_ui_runtime.sh` | 独立归档/对比审计工具，不是构建入口 |

### 6.3 已删除的冗余层

```text
build_linux.sh
build_linux_bookworm_container.sh
build_rk3588.sh
install_linux_container_build_deps.sh
docker/linux-bookworm.Dockerfile
```

这些文件分别只是路由、container 或临时 field-test wrapper；其仍有价值的职责
已经进入 `build.sh` 或 `build_pi.sh`，不再维护第二套入口。
