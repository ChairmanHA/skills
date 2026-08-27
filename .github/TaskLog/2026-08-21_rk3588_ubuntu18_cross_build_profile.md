# RK3588 Ubuntu 18.04 交叉编译 Profile

## Scope

- 通过 PuTTY/plink 只读检查 `192.168.1.12` RK3588 目标机和 `192.168.3.250` x86_64 交叉编译机。
- 对照 `scripts/build.sh`、`scripts/build_pi.sh`、交叉 Qt/OpenSSL 与目标现有程序的真实 ABI，设计 RK3588 专用构建入口。
- 新增独立的 RK3588/X11 构建 profile，不改变现有 aarch64/Wayland、x86_64/X11 和原生 `build_pi.sh` 默认行为。

## Observation

### RK3588 target (`192.168.1.12`)

- Rockchip RK3588，aarch64，Ubuntu 18.04.5，glibc 2.27，内核 5.10.160。
- 图形会话是 GDM 自动登录到 LXDE/Xorg，不是 Wayland；Xauthority 位于 `/run/user/1000/gdm/Xauthority`。
- 系统 Qt 是 5.9.5，但目标包可以通过相对 RUNPATH 和 launcher 的 `LD_LIBRARY_PATH` 使用随包 Qt。
- 系统 `libstdc++.so.6` 最高提供 `GLIBCXX_3.4.25`，`CXXABI_1.3.11`。
- X11/xcb 运行库已包含 Qt xcb QPA 所需的 X11、xcb、xkbcommon、fontconfig、freetype、glib 等依赖。
- 当前 `/home/rpdzkj/Desktop/SGStudio` 包来自较新 Debian 12/GCC 12 环境；可执行文件要求 `GLIBC_2.34`，随包 Qt 还要求 `GLIBC_2.35`、`GLIBCXX_3.4.29`，因此不能在该 Ubuntu 18.04 目标上运行。

### Build host (`192.168.3.250`)

- x86_64 Ubuntu 18.04.6，glibc 2.27，已安装 GCC/G++ 7.5 aarch64 交叉编译器和 arm64 glibc 2.27 开发包。
- `/opt/Qt/5.15.18/gcc_aarch64` 与对应 x86_64 host tools 完整存在，包含 aarch64 `libqxcb.so`。
- 交叉 Qt 核心/X11 组件最高要求 `GLIBC_2.27`、`GLIBCXX_3.4.21`；与目标 ABI 兼容。
- `/opt/aarch64-openssl` 提供 aarch64 OpenSSL 3，最高要求 `GLIBC_2.17`；与目标兼容。
- `3rdParty/modulation/lib/gcc_aarch64/libgenSignalWave.so` 存在。
- 远端仓库存在未跟踪构建日志和备份，且远端 `build.sh` 与当前本地版本不同；本任务不直接覆盖远端工作树。

## Inference

- 可行路径是复用 `scripts/aarch64.cmake`、GCC 7 和 `/opt/Qt/5.15.18/gcc_aarch64`，而不是 Debian 12 arm64 容器。
- 目标会话必须使用 `xcb`，并关闭 vendored LayerShellQt；现有 `launch_linux_x11.sh` 已提供正确的包内 Qt plugin/LD_LIBRARY_PATH 运行边界。
- 仅凭“编译成功”不能保证 Ubuntu 18.04 兼容；必须在打包前静态拒绝架构错误或符号版本超过目标上限的 ELF。

## Success Criteria

1. 新入口用法为 `bash scripts/build_rk3588.sh <packet> <language> [archive_name] [build options...]`。
2. RK3588 profile 使用独立 `build/build_rk3588_*` 和 `build/linux_rk3588_aarch64/*`，不清理或覆盖现有 aarch64/Wayland build tree。
3. 使用 aarch64 GCC 7、Qt 5.15.18 target prefix/host tools、OpenSSL 3 aarch64 路径和 `gcc_aarch64` vendor runtime。
4. CMake 显式设置 `SGS_ENABLE_VENDORED_LAYER_SHELL_QT=OFF`；包内 launcher 设置 `QT_QPA_PLATFORM=xcb`。
5. 构建后扫描 `bin/lib/plugin/updater` 中所有 ELF，要求 aarch64 且最高符号版本不超过 `GLIBC_2.27`、`GLIBCXX_3.4.25`、`CXXABI_1.3.11`；不满足则禁止生成发布包。
6. 现有 `aarch64`、`gcc/gcc_x86_64/x86_64` 与 `build_pi.sh` 行为保持不变。

## Verification Level

- `static`
- 本地执行 Bash 语法检查和脚本静态差异检查。
- 不在用户有未跟踪文件的 250 工作树中启动构建；实际交叉 configure/build 需在用户同步当前代码后显式执行。

## Verification Evidence

- Windows 本地 Git Bash 对 `scripts/build.sh`、`scripts/build_rk3588.sh` 和修正后的救援脚本执行 `bash -n` 通过。
- `scripts/build_rk3588.sh` 无参数调用能打印专用用法并以状态 1 退出。
- 将当前 `build.sh` 通过标准输入发送到 250 的 Bash 做了第二次 `bash -n`，不写入远端文件，语法检查通过。
- 从当前脚本原样提取 ABI audit 函数，在 250 上只读扫描 `/opt/Qt/5.15.18/gcc_aarch64`：70 个目标 ELF 均为 aarch64，且符号版本未超过 RK3588 上限。
- 已人工核对 `libqxcb.so`/`libQt5XcbQpa.so.5` 的 NEEDED 列表与 RK3588 的 X11/xcb 运行库；已检查的依赖均存在。
- 按仓库默认静态验证边界，本次未启动完整 configure/build，也未部署或替换 RK3588 上现有包。

## Follow-up: 暂时排除不兼容的 Modulation API 消费者

### Observation

- 250 上首次 RK3588 build 已完成编译，但 ABI audit 拒绝 `libgenSignalWave.so`：该供应商库要求 `GLIBC_2.29`，超过目标上限 `GLIBC_2.27`。
- `AnalogModulation` 明确链接 `MODULATION_API::modulation_api`。
- `SCPI` 也链接同一 target；用户确认首轮启动验证不需要 SCPI。
- `3rdParty/modulation` 的部署 target 是 `ALL`，因此只从插件目录排除 Analog/SCPI 还不够，必须同时不进入该第三方子目录。
- 250 当前产物的 ELF `NEEDED` 显示：`libHTRA.so -> libh2api.so.2`，但 `libh2api.so` 不硬依赖 `libgenSignalWave.so`；后者只以 `/libgenSignalWave.so` 字符串存在，属于设备 API 的运行期按需加载路径。
- 其他插件 metadata 不依赖 `Analog Modulation` 或 `SCPI`，移除两者不会造成插件依赖解析缺口。

### Plan And Success Criteria

1. 新增默认开启的 `SGS_ENABLE_ANALOG_PLUGIN` 和 `SGS_ENABLE_SCPI_PLUGIN` CMake 选项，保持所有现有 profile 行为不变。
2. `src/plugins` 仅在对应选项开启时进入 Analog/SCPI 子目录。
3. `3rdParty/modulation` 仅在 Analog 或 SCPI 至少一个开启时配置和部署。
4. RK3588 profile 固定把两个选项设为 `OFF`，因此不编译这两个插件，也不复制 `libgenSignalWave.so`。
5. HTRA 保留；首轮验证只承诺应用/插件可加载，依赖运行期 modulation vendor library 的设备功能暂不在本阶段验收范围。
6. ABI audit 保持严格，不能通过白名单跳过或容忍 `GLIBC_2.29`。

### Implementation And Verification

- 根 CMake 新增默认 `ON` 的 Analog/SCPI 插件开关，现有非 RK3588 profile 保持原行为。
- RK3588 profile 显式传入两个开关为 `OFF`，并跳过 modulation vendor runtime 的存在性检查。
- 插件目录按开关排除 `analog` 和 `scpi`；两者均关闭时，`3rdParty/modulation` 不再进入配置，因此其 `ALL` 部署 target 不会复制 `libgenSignalWave.so`。
- `git diff --check` 通过。
- 将本地 `scripts/build.sh` 和 `scripts/build_rk3588.sh` 以原始字节通过 PuTTY 标准输入送到 250 执行 `bash -n /dev/stdin`，两者均通过；未写入远端文件，未启动 configure/build。

## Follow-up: 启用 Analog/SCPI 生成 GLIBC 2.29 实机测试包

### Updated Evidence And User Decision

- 用户确认删除 `gcc_aarch64_old`，本轮只使用 `3rdParty/modulation/lib/gcc_aarch64/libgenSignalWave.so`。
- 当前供应商库 SHA-256 为 `E662E6CDB12FEF5C068B7FD022911AC1325CC570E555F06837F8F2F4B0A7F9F3`，要求 `GLIBC_2.29`；250 的 aarch64 sysroot 和 RK3588 目标均为 `GLIBC_2.27`。
- 用户明确决定接受该已知风险，要求重新启用 Analog/SCPI、生成产物并自行在目标机实测。

### Plan And Success Criteria

1. 仅由 `scripts/build_rk3588.sh` 测试入口开启 Analog/SCPI 和已知供应商库例外；其他 profile 不放宽。
2. `scripts/build.sh rk3588 ...` 的保守默认行为仍保持插件关闭，避免旁路入口无意生成不兼容包。
3. 例外只允许上述精确 SHA-256 的 `libgenSignalWave.so` 超出至 `GLIBC_2.29`；架构、GLIBCXX、CXXABI 和其他 ELF 的审计仍保持严格。
4. 构建信息及 ABI audit 必须输出明显警告，不能把测试包误报为 Ubuntu 18.04 ABI compatible。
5. 本轮做脚本语法和静态分支验证，不在 250 启动完整编译；实际 build/runtime 由用户执行。

### Implementation And Verification

- `build_rk3588.sh` 固定导出 Analog/SCPI `ON`、GLIBC 2.29 field-test exception `ON` 和当前 `gcc_aarch64` 供应商库的精确 SHA-256。
- `build.sh` 的直接 `rk3588` 入口仍默认关闭两个插件；只有 wrapper 明确注入时才启用测试行为。
- 供应商库在配置前执行 SHA-256 预检；ABI audit 只对同名、同哈希且最高恰为 `GLIBC_2.29` 的 ELF 放行，其他越界要求仍累计为错误。
- Git for Windows Bash 对两个脚本执行 `bash -n` 通过，`git diff --check` 通过。
- 使用临时隔离目录验证 ABI 例外分支：正确哈希产生单条显式 warning 并通过，错误哈希被拒绝；临时测试脚本和目录均已删除。
- 未启动完整 configure/build。
