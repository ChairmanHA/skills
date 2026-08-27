# Linux 构建入口收敛

## 目标

把 Linux 构建模型收敛为两个入口：

1. `scripts/build.sh`：只在 192.168.3.250 的 Ubuntu 18.04/x86_64 构建机上
   使用，直接负责 Ubuntu AArch64、Ubuntu x86_64 和 Raspberry Pi AArch64
   交叉包。
2. `scripts/build_pi.sh`：只在当前地址为 192.168.3.108 的 Debian 12/AArch64
   Raspberry Pi 上原生构建，作为与真实运行环境完全一致的最终防火墙。

Win32 `scripts/build.bat` 不变。

## 已确认事实

- 108（原 132）当前地址为 `192.168.3.108`，系统为 Debian 12/AArch64、glibc
  2.36、系统 Qt 5.15.8。
- 108 当前原生产物 `bin/SGStudio` 的 RUNPATH 为
  `$ORIGIN/../lib:$ORIGIN/../plugin`。
- 在 108 上移除 `LD_LIBRARY_PATH` 后对该文件执行 `ldd`，SGStudio 自有库、Qt 和
  QXlsx 仍从相邻 `lib/` 正确解析；`bin/qt.conf` 使用相对 `Prefix/Plugins/Libraries`
  路径。
- 因此原生包可以把 `bin/<application>` 作为正式直接启动入口；前提是从当前
  桌面用户的 Wayland 会话启动，以继承 `XDG_RUNTIME_DIR`、`WAYLAND_DISPLAY`、
  DBus 和音频环境。
- RK3588 的 Analog/SCPI 环境变量、供应商库 SHA-256 和 GLIBC 2.29 白名单是临时
  field-test 机制，用户明确要求删除，不再保留。

## 设计

### 250 统一入口

`build.sh` 暴露三个明确 target：

- `ubuntu-aarch64`：GCC 7 AArch64 交叉编译、Qt 5.15.18、X11/xcb；
- `ubuntu-x86_64`：250 本机 GCC、Qt 5.15.18、X11/xcb；
- `raspberry-pi`：GCC 7 AArch64 交叉编译、Qt 5.15.18、Wayland/LayerShellQt，
  供 108 做兼容性测试。

保留旧 `rk3588`、`gcc`、`x86_64`、`aarch64` 名称作为参数兼容别名，但不再用
额外 wrapper 路由。

Ubuntu AArch64 profile 保留全包 AArch64 架构检查，但不再宣称或强制 Ubuntu
18.04/glibc 2.27 ABI 上限。Analog/SCPI 使用普通默认 `ON`，不再存在哈希白名单
或符号版本例外。

### 108 原生入口

- `build_pi.sh` 只支持 AArch64 原生主机；去掉 x86_64/container 分支。
- 新的简化命令为 `build_pi.sh <packet> <language> ...`；暂时接受旧的前导
  `aarch64` 参数并提示兼容信息。
- 原生包仍收集经过验证的 Qt/plugin runtime，以保留可回滚归档；但不再生成或
  强制根目录 launcher。
- staging 后直接检查 `bin/<application>` 的 `$ORIGIN` RUNPATH、相对 `qt.conf`
  和清空 `LD_LIBRARY_PATH` 后的 `ldd` 解析结果。
- 原生包输出到独立的 `build/linux_pi_native_aarch64/...`，避免与 250 交叉包混淆。

## 删除清单

以下文件只服务于已取消的路由/container/wrapper 层，删除：

- `scripts/build_linux.sh`
- `scripts/build_linux_bookworm_container.sh`
- `scripts/build_rk3588.sh`
- `scripts/install_linux_container_build_deps.sh`
- `scripts/docker/linux-bookworm.Dockerfile`

以下文件不是冗余入口，保留：

- `scripts/aarch64.cmake`、`scripts/gcc.cmake`
- `scripts/launch_pi.sh`、`scripts/launch_linux_x11.sh`（250 交叉/打包模板）
- `scripts/install_pi_runtime_deps.sh`（目标机依赖准备）
- `scripts/audit_linux_package_ui_runtime.sh`（独立归档审计工具）
- 用户未跟踪的 `configure_rk3588_direct_ethernet.sh`、`build_all.bat`

## 成功标准

1. 250 的三类包均可直接从 `build.sh` 选择，usage 不再推荐 wrapper。
2. 仓库活动代码和 KnowledgeBase 不再依赖被删除脚本。
3. RK3588 临时哈希、插件环境变量和 GLIBC 例外逻辑完全删除。
4. `build_pi.sh` 拒绝非 AArch64 主机，且原生归档不包含根 launcher。
5. `build_pi.sh` 在打包前证明 `bin/<application>` 无需 `LD_LIBRARY_PATH` 即可解析
   直接/间接依赖，并检查相对 RUNPATH 与 `qt.conf`。
6. 不修改或覆盖当前无关的用户源码改动和未跟踪文件。

## 验证级别

`static`

- Git for Windows Bash 执行 `bash -n`。
- 对三个 `build.sh` target 和 `build_pi.sh` 做无构建参数/分支检查。
- 搜索活动脚本与 KnowledgeBase 中的已删除入口引用。
- 执行 `git diff --check`。
- 不执行完整编译；用户后续分别在 250 和 108 构建测试。

## 实施与静态验证结果

- `scripts/build.sh` 已删除 RK3588 临时 Analog/SCPI 环境开关、供应商库 SHA-256
  白名单和 GLIBC 2.29 例外；Analog/SCPI 作为普通构建项固定启用。
- Ubuntu AArch64 检查只验证包内 ELF 均为 AArch64，不再伪造或放宽 Ubuntu
  18.04 的 GLIBC 兼容承诺。
- `scripts/build_pi.sh` 同样固定启用 Analog/SCPI，不含任何哈希例外分支。
- `bash -n` 已通过 `build.sh`、`build_pi.sh` 和 `install_pi_runtime_deps.sh`。
- `build.sh --list` 已静态确认只公开 `ubuntu-aarch64`、`ubuntu-x86_64`、
  `raspberry-pi` 三个统一 target。
- `git diff --check` 已通过；未执行完整编译或远程写入。
