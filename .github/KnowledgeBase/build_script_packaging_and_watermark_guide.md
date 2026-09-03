# 构建打包脚本与水印开关使用说明

## 1. 用户入口

当前只推荐三个构建入口：

| 平台/机器 | 入口 | 用途 |
| :--- | :--- | :--- |
| Win32 | `scripts/build.bat` | Windows 单包 configure/build/stage/zip |
| 192.168.3.250 | `scripts/build.sh` | Linux 三目标 configure/build/stage/tar.gz |
| 192.168.3.108 | `scripts/build_pi.sh` | Raspberry Pi 原生构建与直接执行归档 |

Linux target、环境差异与启动方式见
[Linux 两入口构建、打包与启动说明](linux_build_package_unified_entry.md)。

## 2. `build.bat`

用法：

```bat
scripts\build.bat <packet> <language> [archive_name] [options]
```

示例：

```bat
scripts\build.bat standard cn SGStudio --rebuild --watermark off
scripts\build.bat standard en SGStudio --watermark on
scripts\build.bat neutral en --without-watermark
scripts\build.bat standard en --startup-eth-connect-dialog on --analog-plugin off --scpi-plugin off
```

特点：

- build tree：`build/build_msvc_<packet>_<language>`；
- 默认增量构建；`--rebuild` 删除该 build tree 后重新 configure；
- 递归 staging `QuickWaveFormData/`，并预创建运行时 `images/`；
- 输出：`build/windows_x86_64/<packet>/<language>/<archive_name>.zip`。

### 2.1 Windows 单包 CMake 选项接口

| 脚本参数 | CMake 选项 | 源码默认值 |
| :--- | :--- | :--- |
| `--watermark on|off` | `SGS_ENABLE_INTERNAL_WATERMARK` | `OFF` |
| `--startup-eth-connect-dialog on|off` | `SGS_ENABLE_STARTUP_ETH_CONNECT_DIALOG` | `OFF` |
| `--analog-plugin on|off` | `SGS_ENABLE_ANALOG_PLUGIN` | `ON` |
| `--scpi-plugin on|off` | `SGS_ENABLE_SCPI_PLUGIN` | `ON` |

四个接口都不区分大小写。可以在同一次单包构建中组合使用，例如：

```bat
scripts\build.bat standard cn SGStudio --rebuild ^
  --watermark off ^
  --startup-eth-connect-dialog off ^
  --analog-plugin on ^
  --scpi-plugin on
```

未传入某个选项时，`build.bat` 不强制写入对应的 `-D` 参数：新 build tree
采用源码默认值，已有增量 build tree 可以保留 CMake cache 中的值。正式构建若要求
确定配置，应显式传入需要固定的开关，或配合 `--rebuild` 使用。

### 2.2 Windows 最终常规发布矩阵

最终常规发布使用：

```bat
scripts\build_all.bat --watermark off
```

该入口依次对 `standard cn/en/ru` 与 `neutral cn/en` 做全量重建。BNC/VectorCore
是按需构建的特殊版本，不在 `build_all.bat` 矩阵中；需要时单独调用
`scripts\build.bat BNC en --rebuild --watermark off`。

不传入全局 archive name 时，standard 三种语言生成 `SGStudio.zip`，neutral
两种语言生成 `VSG.zip`。

## 3. `build.sh`：250 Linux 三目标

用法：

```bash
bash scripts/build.sh \
  <target> <packet> <language> [archive_name] [--watermark on|off]
```

正式 target：

```text
ubuntu-aarch64
ubuntu-x86_64
raspberry-pi
```

示例：

```bash
bash scripts/build.sh ubuntu-aarch64 standard cn SGStudio --watermark off
bash scripts/build.sh ubuntu-x86_64 standard cn SGStudio --watermark off
bash scripts/build.sh raspberry-pi standard cn SGStudio --watermark off
```

特点：

- 三个 target 都由同一个脚本直接配置，不再经过 router/container/wrapper；
- 每次执行都会清空对应 build tree；
- Ubuntu 两个 target 使用 X11/xcb；Raspberry Pi target 使用 Wayland/LayerShellQt；
- Analog/SCPI 正常开启；不存在供应商哈希或临时 GLIBC 例外；
- 三个 target 都用目标 `readelf` 从 `DT_NEEDED` 展开依赖闭包，把 Qt、Qt
  plugin、业务 plugin 与供应商库的应用级间接依赖复制进 `lib/`；glibc、
  `libstdc++`、GL/EGL/DRM/GBM、`libwayland-*` 仍由目标系统提供，边界说明见
  [250 交叉构建与 108 运行兼容性](raspberry_pi_132_build_host_250_compatibility.md)；
- 三种产物都带根 launcher，并通过 launcher 建立包内 Qt/runtime 环境。

输出：

| target | build tree | package directory |
| :--- | :--- | :--- |
| `ubuntu-aarch64` | `build/build_rk3588_<packet>_<language>` | `build/linux_ubuntu_aarch64/<packet>/<language>` |
| `ubuntu-x86_64` | `build/build_gcc_<packet>_<language>` | `build/linux_ubuntu_x86_64/<packet>/<language>` |
| `raspberry-pi` | `build/build_aarch64_<packet>_<language>` | `build/linux_raspberry_pi_cross_aarch64/<packet>/<language>` |

## 4. `build_pi.sh`：108 原生防火墙

用法：

```bash
bash scripts/build_pi.sh \
  <packet> <language> [archive_name] [--watermark on|off]
```

示例：

```bash
bash scripts/build_pi.sh standard cn SGStudio --watermark off
bash scripts/build_pi.sh neutral en --watermark on
bash scripts/build_pi.sh BNC en VectorCore --without-watermark
```

旧的前导 `aarch64` 参数仍兼容，但已无必要：

```bash
bash scripts/build_pi.sh aarch64 standard cn SGStudio --watermark off
```

特点：

- 只允许在 AArch64 Raspberry Pi 运行，不再包含 x86_64/container 分支；
- 通过 `qmake -query` 使用 108 的系统 Qt 5.15.8；
- 收集实际需要的 Qt/ICU runtime 与 Qt plugins；
- 验证 Qt Wayland、LayerShellQt 和 package-owned Qt 解析；
- 不生成根 launcher；正式入口是 `bin/<application>`；
- staging 后清空 `LD_LIBRARY_PATH`，对全包 ELF 执行 `ldd`，发现任何
  `not found` 即拒绝归档；
- 检查主程序的 `$ORIGIN/../lib`、`$ORIGIN/../plugin` RUNPATH 和相对
  `bin/qt.conf`。

输出：

```text
build/build_pi_native_<packet>_<language>/
build/linux_pi_native_aarch64/<packet>/<language>/<archive_name>.tar.gz
```

在 108 的 `htra` Wayland 桌面终端直接启动：

```bash
cd /path/to/<archive_name>
./bin/SGStudio
```

不要使用 `sudo`，也不要把纯 SSH shell 当作默认图形启动环境。

## 5. packet、language 与程序名

| packet/language | 程序名 | 默认 archive root |
| :--- | :--- | :--- |
| `standard cn/en` | `SGStudio` | `SGStudio` |
| `standard ru` | `СПО ГСРВ` | `SGStudio` |
| `neutral *` | `VSG` | `VSG` |
| `BNC en` | `VectorCore` | `VectorCore` |

## 6. 水印开关

三个入口都支持：

```text
--watermark on
--watermark off
--with-watermark
--without-watermark
```

它们映射为：

```text
-DSGS_ENABLE_INTERNAL_WATERMARK=ON
-DSGS_ENABLE_INTERNAL_WATERMARK=OFF
```

建议每次发布显式写出开关，避免依赖源码默认值或旧 CMake cache。

### 6.1 无水印正式包

显式 `--watermark off` / `--without-watermark` 时，脚本仅修改最终 staging 中的
`configuration/Settings.ini`，删除：

```ini
FixedLic=...
DebugMode=...
RecordDeviceHistory=...
online=...
```

源码配置和 build tree 配置不被修改；最终 staging 缺文件或仍残留这些键时，脚本
应失败而不是生成误配置归档。

## 7. 缓存边界

- `build.bat` 默认增量，只有 `--rebuild` 清理整个 Windows build tree。
- `build.sh` 每次清理当前 Linux target 的 build tree。
- `build_pi.sh` 每次清理当前原生 Pi build tree。
- Qt Creator、VS Code 和手工 CMake build tree 不与这些脚本自动共享 cache。

## 8. 归档布局与启动差异

共同目录：

```text
<archive_name>/
├── bin/
├── lib/
├── plugin/
├── configuration/
├── updater/
├── QuickWaveFormData/
├── images/
├── version.json
└── releasenote.txt
```

差异：

- 250 的 `build.sh` 包在根目录额外生成 launcher，必须用 launcher 启动；
- 108 的 `build_pi.sh` 原生包没有根 launcher，直接运行 `bin/<application>`。

不同 target 的 `bin/lib/plugin` 不能拼装或相互覆盖。

## 9. 建议排障顺序

1. 确认使用了正确入口、target 和 package 输出目录。
2. 查看构建开头的 host architecture、compiler target、Qt prefix 与桌面后端。
3. 查看打包前 audit 是否通过。
4. 250 包从根 launcher 前台启动；108 原生包从 `bin/<application>` 启动。
5. 先看终端 loader/Qt plugin 输出，再看 `bin/debug.log`。
6. `GLIBC_* not found` 是 ABI 问题，不要用复制 glibc 的方式修复。
7. Wayland 问题先检查桌面用户、`XDG_RUNTIME_DIR` 和 `WAYLAND_DISPLAY`。

## 10. 相关文档

- [Linux 两入口构建、打包与启动说明](linux_build_package_unified_entry.md)
- [250 交叉构建与 108 运行兼容性](raspberry_pi_132_build_host_250_compatibility.md)
- [Linux RPATH 机制](RPATH_MECHANISM.md)
- [Windows build.bat 打包卫生](windows_build_bat_updater_packaging_hygiene.md)
