# AArch64 双平台单产物直接启动

## 任务目标

让 192.168.3.250 上通过 `scripts/build.sh raspberry-pi` 生成的一份 AArch64
归档，同时支持：

- Raspberry Pi（Debian 12 / Wayland / labwc）；
- RK3588（Ubuntu 18.04 / X11 / xcb）；
- 用户在目标桌面会话中直接执行 `bin/<application>`，不依赖根目录启动脚本。

## 范围

- 在 `QApplication` 创建前建立 Linux 包内 Qt plugin 隔离，并根据真实桌面会话
  自动选择 Wayland 或 xcb QPA。
- 保留 Raspberry Pi 构建中的 QtWayland 与 vendored LayerShellQt，使该 profile
  成为 Wayland + X11 的 AArch64 超集；LayerShellQt 只在 Wayland helper 路径使用。
- 把 `build.sh raspberry-pi` 的依赖闭包、ABI 审计和启动合同改为双平台直接启动。
- 更新 Linux 构建与 250/108 兼容性文档。

不在本任务中改动闭源 modulation 库、目标系统镜像、设备驱动或业务功能。

## 已观察事实

- 250 当前 Raspberry Pi 与 Ubuntu AArch64 归档的主程序字节一致；主要差异是
  Raspberry Pi profile 额外构建并部署 LayerShellQt 相关 helper/runtime。
- 108 当前归档的主程序、MiniBar、maintenance 和 updater 在清空
  `LD_LIBRARY_PATH` 后均可由 ELF `DT_RPATH` 解析，无 `not found`。
- 108 桌面会话是 Wayland，同时存在 `DISPLAY=:0`，并带有指向
  `/opt/Qt/5.15.18/gcc_aarch64/plugins` 的外部 `QT_PLUGIN_PATH`；直接启动必须在
  `QApplication` 前覆盖这类外部 Qt plugin 路径。
- RK3588 的历史现场资料表明它是 AArch64、Ubuntu 18.04/glibc 2.27、Xorg/xcb；
  本次没有可连接的当前 RK3588 地址，因此其运行结果仍需现场闭环。
- 历史 AArch64 包除 `libgenSignalWave.so` 外，ABI 需求落在 RK3588 上限内。
  `libgenSignalWave.so` 要求 `GLIBC_2.29`；用户已明确接受这一闭源库例外，本任务
  不把它作为统一产物的阻断项，也不把例外扩展到其他 ELF。

## 设计

1. Linux 主程序在创建 `QApplication` 前，从 `/proc/self/exe` 确定 `bin/`：
   - 把 `QT_PLUGIN_PATH` 固定为当前包的 `bin/`；
   - 把 `QT_QPA_PLATFORM_PLUGIN_PATH` 固定为当前包的 `bin/platforms/`；
   - 清除 `QT_WAYLAND_SHELL_INTEGRATION` 和继承的 `QT_QPA_PLATFORM`；
   - 如果命令行显式传入 `-platform`，保留该诊断覆盖；否则优先服从
     `XDG_SESSION_TYPE`，并以可访问的 Wayland socket 或 `DISPLAY` 与对应包内
     platform plugin 为实际可用性门禁，选择 `wayland` 或 `xcb`。
2. 子进程继承主进程收口后的环境，因此 MiniBar、maintenance/updater 重启链不再
   依赖 launcher 注入 Qt 路径。MiniBar 现有运行时 platform 判断继续保证
   LayerShellQt 只在 Wayland 下启用。
3. `build.sh raspberry-pi` 同时把 qwayland 与 qxcb 作为必需依赖种子，要求两套
   platform plugin 都进入可解析闭包。
4. 共享产物 ABI 门禁使用 RK3588 的已知上限：`GLIBC_2.27`、
   `GLIBCXX_3.4.25`、`CXXABI_1.3.11`。只允许包内
   `lib/libgenSignalWave.so` 的 GLIBC 需求例外到已知 `2.29`，并打印显式警告；
   其他文件仍严格失败。
5. 归档可以继续携带兼容 launcher，但正式合同与审计入口改为直接执行
   `bin/<application>`，launcher 不再是 Raspberry Pi 的必需条件。

## 成功标准

- `build.sh raspberry-pi` 开启 LayerShellQt，并强制验证 qwayland 与 qxcb 两套
  platform runtime。
- 主程序在任何 Qt GUI 初始化之前完成包内 plugin 隔离与 QPA 自动选择。
- 主程序具备包含 `$ORIGIN/../lib` 的可继承 `DT_RPATH`；构建脚本对 Raspberry Pi
  profile 也执行直接启动合同审计。
- 共享归档内所有非 modulation ELF 都不超过 RK3588 已知 ABI 上限，闭源库例外
  精确限制在 `lib/libgenSignalWave.so` 和 `GLIBC_2.29`。
- 文档不再要求 Raspberry Pi 交叉包必须使用启动脚本，并明确支持边界是普通用户
  的已登录图形桌面会话；纯 SSH、root 或 system service 不是默认直接启动场景。

## 验证级别

`static`

- `bash -n scripts/build.sh`；
- 检查启动 bootstrap 位于 `QApplication app(...)` 之前；
- 检查两套 QPA plugin 均进入必需依赖闭包和 required runtime 列表；
- 检查 ABI 例外只匹配单一包内闭源库路径；
- `git diff --check`。

本任务不主动编译，也不在远端启动 GUI。最终发布前仍需分别在 108 Wayland 与
RK3588 X11 上直接执行同一归档的 `bin/<application>`，完成 UI、MiniBar、设备及
业务回归。

## 实施结果（2026-08-26）

- `src/app/main.cpp` 已在 `QApplication` 前完成 Linux 包内 Qt plugin 隔离、
  `QT_WAYLAND_SHELL_INTEGRATION` 清理与 Wayland/xcb 自动选择。
- `scripts/build.sh raspberry-pi` 已把 qwayland、qxcb 和 LayerShellQt 同时列为
  必需 runtime，并对该 profile 启用直接启动 `DT_RPATH` 合同。
- 一般 ELF ABI 门禁已切到 RK3588 基线；闭源库例外只允许
  `${BUILD_DIR}/lib/libgenSignalWave.so` 的 GLIBC 需求不高于 2.29。
- 已更新 KnowledgeBase，将交叉包正式入口改为 `bin/<application>`，launcher
  仅作为兼容工具。
- `scripts/launch_pi.sh` 的帮助和运行日志已同步声明直接执行受支持；脚本仍保留
  原有 Wayland 兼容行为。

静态检查结果：

- Git Bash `bash -n scripts/build.sh` 与 `bash -n scripts/launch_pi.sh`：通过；
- bootstrap 与 `QApplication` 创建顺序、双 QPA 文件/闭包、ABI 上限和例外路径
  断言：通过；
- `git diff --check`：通过（仅提示当前 Windows `core.autocrlf` 的行尾转换预告）；
- 未编译、未连接 RK3588、未启动远端 GUI，符合本任务的 `static` 验证级别。
