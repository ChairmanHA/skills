# 250 交叉构建包的运行时依赖闭包收集

## Scope

- 让 `scripts/build.sh` 的三个 target（`ubuntu-aarch64`、`ubuntu-x86_64`、
  `raspberry-pi`）在 192.168.3.250 上打包时收集完整的运行时依赖闭包，而不是
  只复制 Qt 顶层库和 `libgomp.so.1`。
- 不改变 `scripts/build_pi.sh`（108 原生入口）。
- 不改变归档布局、launcher 合同和现有 audit 的判定标准。

## Observation

- `scripts/build.sh` 当前只做两件收集动作：把 `${QT_LIB_DIR}` 顶层 `*.so`/
  `*.so.*` 全量复制到 `lib/`，再用 `-print-file-name` 复制 `libgomp.so.1`。
  Qt 与 Qt plugin 的间接依赖（`libxcb-icccm`、`libxcb-image`、
  `libxcb-keysyms`、`libxcb-render-util`、`libxcb-xinerama`、`libxcb-xkb`、
  `libxkbcommon`、`libxkbcommon-x11`、`libpng16`、`libfreetype`、
  `libfontconfig`、`libglib-2.0` 等）完全没有被收集。
- `scripts/build_pi.sh` 有基于 `ldd` 的闭包收集，但它是**原生** AArch64 构建。
  `ldd` 需要执行目标动态 loader，在 x86_64 主机上无法解析 AArch64 ELF，
  因此这套实现不能直接搬到 `build.sh` 的两个交叉 target。
- `build.sh` 已经定义了 `RUNTIME_READELF="${RUNTIME_CC%gcc}readelf"`
  （`aarch64-linux-gnu-readelf` / `readelf`），并已在
  `verify_aarch64_bookworm_runtime` 中用 `readelf -dW` 读取动态段。这是交叉
  场景下唯一可靠的依赖提取方式。
- `build.sh` 已经用 `${RUNTIME_CC} -print-multiarch` 计算 `MULTIARCH`，并把
  `/usr/lib/${MULTIARCH}/pkgconfig` 加入 `PKG_CONFIG_LIBDIR`；`raspberry-pi`
  的 `wayland-client`/`xkbcommon` pkg-config 前置检查通过，说明 250 上确实
  安装了目标架构的 multiarch 运行时/开发包，`/usr/lib/aarch64-linux-gnu`
  可以作为受控的依赖来源。
- 两个 launcher 模板都把 `LD_LIBRARY_PATH` 固定为
  `lib:bin:plugin:updater`，因此只要把依赖放进 `lib/`，间接依赖即可解析；
  不需要修改 launcher 或 RPATH。

## Design

### 1. 通用闭包收集（三个 target 共用）

新增 `collect_runtime_dependency_closure`，在 Qt library / Qt plugin 复制完成
之后、出包 audit 之前执行：

1. **种子集**：
   - `bin/` 顶层 ELF（主程序、helper、可执行文件）；
   - `plugin/`、`updater/` 下所有文件；
   - Qt blanket 复制之前 `lib/` 中已有的构建产物快照（SGStudio 自有库、
     vendor 库、LayerShellQt）；
   - `bin/<必需 Qt plugin 组>/` 下的文件。
2. **BFS**：用 `${RUNTIME_READELF} -dW` 读取 `DT_NEEDED`，逐层展开。
3. 只有被真正引用的库才进入闭包。`QT_LIB_DIR` 的全量复制保留原样，但
   `libQt5WebEngineCore` 之类无人引用的库不会成为 BFS 节点，因此不会把
   NSS/X11 扩展等无关依赖拖进来。

### 2. 明确的三分类边界

- **目标系统提供（禁止捆绑）**：
  `libc`/`ld-linux`/`libpthread`/`libdl`/`librt`/`libm`/`libresolv`/
  `libnss_*`/`libnsl`/`libutil`/`libanl`/`libcrypt`、`libstdc++`/`libgcc_s`、
  `libGL`/`libGLX`/`libGLdispatch`/`libOpenGL`/`libEGL`/`libGLESv*`/
  `libglapi`/`libgbm`/`libdrm*`、`libwayland-*`、`libudev`/`libsystemd`。
- **随包**：其余全部，只要能在受控搜索目录中找到，并且 ELF machine 与目标
  一致。
- **既找不到又不属于目标系统边界**：打印引用者并让打包失败，不生成看似成功
  但一定跑不起来的归档。

### 3. 受控搜索目录

按优先级：`${QT_LIB_DIR}` → `3rdParty/**/${VENDOR_ARCH_DIR}` → OpenSSL 前缀
（aarch64 用 `/opt/aarch64-openssl`，其余用 `/opt/openssl`）→ 交叉工具链
sysroot → `/usr/lib/${MULTIARCH}`、`/lib/${MULTIARCH}` → `/usr/lib`、`/lib`。

每个候选文件都必须通过 `${RUNTIME_READELF} -hW` 的 `Machine:` 校验。这样即使
搜索列表里包含 x86_64 主机目录，也不会把主机库混进 AArch64 归档。

### 4. 复制策略

按 `DT_NEEDED` 记录的 soname 命名复制解析后的真实文件
（`cp -L <src> lib/<soname>`）。loader 只按 soname 查找，这样避免在归档中重建
多级符号链接，也不会因为部署过程丢失符号链接而失效。

## 无法通过打包解决的部分（混用系统库隐患）

以下运行库**故意不捆绑**，必须由目标系统提供。这是本次改动能达到的上限：

1. `glibc` 家族与动态 loader：与内核接口、NSS、线程实现耦合，替换会制造比原
   问题更隐蔽的崩溃。
2. `libstdc++` / `libgcc_s`：目标机版本更高，向后兼容旧符号；反向覆盖会破坏
   目标机上其他组件。
3. `libGL`/`libEGL`/`libGLESv*`/`libglapi`/`libgbm`/`libdrm*`：与 GPU 内核
   驱动版本配套，必须与目标机 Mesa/DRM 同源。
4. `libwayland-*`：这是最典型的**不可捆绑**例子。250 的 Ubuntu 18.04 提供
   Wayland 1.16，108 的 Debian 12 提供 1.21。目标机的 Mesa
   （`libEGL`/`libwayland-egl`）需要 `wl_proxy_marshal_flags`
   （Wayland 1.20 引入）。soname 都是 `libwayland-client.so.0`，同一进程内只
   会装载一个实现；若归档内的 1.16 先被找到，目标机 Mesa 会以
   `undefined symbol: wl_proxy_marshal_flags` 失败。因此 Wayland 客户端库只能
   由目标系统提供。
5. `libudev`/`libsystemd`：与目标机 systemd/udev 实例和数据库格式耦合。

因此正确表述是：`build.sh` 现在能把**应用级依赖闭包**打全，让归档在目标机上
只依赖上述系统层能力；它不能、也不应该把系统图形栈和 C runtime 一起打包。

## Success Criteria

1. 三个 target 都执行同一套 `readelf` 闭包收集，Qt / Qt plugin / 业务 plugin /
   updater / vendor 库的间接依赖被复制进 `lib/`。
2. 收集过程绝不把非目标架构的 ELF 放进归档。
3. 收集过程绝不把上节列出的目标系统运行库放进归档，
   `verify_aarch64_bookworm_runtime` 的既有黑名单仍然通过。
4. 出现无法解析且不属于目标系统边界的 soname 时，打印 soname 与首个引用者并
   使打包失败。
5. `raspberry-pi` 与 `ubuntu-aarch64` 的现有 audit 与 launcher 合同检查不被
   削弱，归档布局与启动方式不变。
6. `build_pi.sh`、`build.bat`、launcher 模板与 CMake 均不修改。

## Verification Level

`static`

- Git for Windows Bash 执行 `bash -n scripts/build.sh`。
- 核对新函数与既有 audit 黑名单、`RUNTIME_READELF`、`MULTIARCH`、
  `VENDOR_ARCH_DIR` 的一致性。
- `git diff --check`。
- 不在本机执行 Linux 构建；三个 target 的实际构建与目标机运行验收由用户在
  250 及对应目标机完成。

## Implementation And Verification

- `scripts/build.sh` 为三个 target 增加 `EXPECTED_ELF_MACHINE`，并把
  `require_command "${RUNTIME_READELF}"` 提升为所有 target 的前置条件。
- 新增受控库搜索目录列表 `RUNTIME_LIB_SEARCH_DIRS`，含 Qt lib、vendor 目录、
  OpenSSL 前缀、工具链 sysroot 与 multiarch 目录。
- 新增 `is_target_provided_library`、`elf_machine_matches_target`、
  `read_elf_needed`、`find_packaged_library`、`find_target_library` 与
  `collect_runtime_dependency_closure`。
- Qt blanket 复制前记录 `lib/` 构建产物快照作为闭包种子。
- 闭包收集在 Qt plugin 复制之后、`verify_aarch64_bookworm_runtime` /
  `verify_ubuntu_aarch64_architecture` 之前执行。
- `bash -n scripts/build.sh` 通过；`git diff --check` 通过。
- 未执行 Linux 构建；缺失依赖的实际清单取决于 250 已安装的 multiarch 运行时
  包，由用户首次运行时按脚本输出补装。

## Follow-up: readelf 本地化导致架构判定恒为 false

### Field Evidence

用户在 250 上运行 `build.sh ubuntu-x86_64 standard cn` 后：

```text
== Resolve required runtime dependency closure into: .../build_gcc_standard_cn/lib ==
== Resolve optional Qt plugin dependency closure ==
ERROR: no Advanced Micro Devices X86-64 ELF files were found for the dependency closure.
```

两个阶段都没有输出任何 `bundled` 行，也没有 missing 报告，最后停在
`RUNTIME_CLOSURE_SCANNED_COUNT -eq 0` 的守卫上，归档没有生成。

### Root Cause

可选阶段的种子来自刚刚复制完成的 `bin/<plugin group>/` 目录，不可能为空，因此
唯一可能是 `elf_machine_matches_target` 对每个文件都返回 false。

初版实现用 `readelf -hW` 抓取 `Machine:` 行。binutils 的 ELF 头字段标签是可翻译
字符串（`printf (_("  Machine:  ... %s\n"), get_machine_name (...))`），在中文
locale 下标签被翻译，`sed` 的 `Machine:` 锚点匹配不到，解析结果恒为空串。

相对地，`readelf -dW` 的 `(NEEDED)` / `(RPATH)` / `(RUNPATH)` 标签由
`get_dynamic_type()` 返回且不参与翻译，所以既有的
`verify_aarch64_bookworm_runtime` 一直工作正常。这解释了为什么只有新增的头部
解析失效。

### Fix

1. 不再解析 readelf 的人类可读文本。`elf_machine_code` 用 `od` 读取 ELF 头前
   20 字节，校验 `7f 45 4c 46` magic，按 `EI_DATA` 选择字节序，取
   `e_machine`（偏移 18..19）。
2. 每个 target 增加 `EXPECTED_ELF_MACHINE_CODE`：AArch64 `00b7`，
   x86-64 `003e`。`EXPECTED_ELF_MACHINE` 保留为可读名称，只用于提示信息。
3. `read_elf_needed` 与 `verify_aarch64_bookworm_runtime` 的 `readelf -dW` 统一
   加 `LC_ALL=C`，消除同类本地化风险。
4. 零扫描失败时打印主程序实际解析出的 `e_machine` 与期望值，便于下次直接定位。
5. 增加 `require_command od`。

### Verification

- `bash -n scripts/build.sh` 通过。
- 用构造的 ELF 头验证 `elf_machine_code`：小端 x86-64 得 `003e`，小端与大端
  AArch64 均得 `00b7`，非 ELF 与不存在文件返回空串（被跳过）。
- 本轮同样不在本机执行 Linux 构建。`build.sh` 每次清空 build tree，因此用户重跑
  会重新编译。

## Follow-up: X11 包支持直接执行 bin/<application>

### Field Evidence

用户在 250 出的 `ubuntu-x86_64` 包，在 Ubuntu 20.04 上直接执行
`bin/SGStudio` 报找不到 ICU；用 `launch_linux_x11.sh` 正常。

### Root Cause

`libicui18n.so.*` 等文件已经在包内 `lib/`，问题不是缺文件而是 loader 找不到。
主程序的 RPATH 是 `DT_RUNPATH`（Ubuntu 工具链默认 `--enable-new-dtags`）。glibc
的规则是：只有当需要依赖的那个对象**自身没有 `DT_RUNPATH`** 时，才会沿 loader
链向上使用祖先的 `DT_RPATH`。`/opt/Qt/5.15.18` 的 `libQt5Core.so.5` 既没有
RPATH 也没有 RUNPATH，而主程序声明的是 RUNPATH（不继承），所以 ICU 只能从
`ld.so.cache` 和默认目录找 —— Ubuntu 20.04 提供的是 ICU 66，不是 Qt 需要的版本。
launcher 的 `LD_LIBRARY_PATH` 正是兜住这一层。

### Fix

1. `src/CMakeLists.txt` 的 Linux RPATH 块追加
   `-Wl,--disable-new-dtags` 到 `CMAKE_EXE_LINKER_FLAGS` 与
   `CMAKE_SHARED_LINKER_FLAGS`。主程序改为发出可继承的 `DT_RPATH`
   （`$ORIGIN/../lib:$ORIGIN/../plugin`），Qt 库的间接依赖随之从包内解析。
   没有引入 `patchelf`，也没有修改任何第三方二进制。
2. `scripts/build.sh` 新增 `verify_direct_launch_contract`，对
   `ubuntu-x86_64` 与 `ubuntu-aarch64` 两个 X11 target 生效：要求主程序带
   含 `$ORIGIN/../lib` 的 `(RPATH)` 条目；`ubuntu-x86_64` 额外在清空
   `LD_LIBRARY_PATH` 后用 `ldd` 确认没有 `not found`。
3. 构建信息的 `launch_policy` 改为按 target 显示实际策略。

### 边界

Raspberry Pi Wayland 包仍然必须用 `launch_sgstudio_pi.sh`。它需要的是
`QT_QPA_PLATFORM=wayland`、清理 `QT_WAYLAND_SHELL_INTEGRATION` 和 LayerShellQt
的会话环境，这些不属于 loader 路径问题，RPATH 无法替代。

`updater_files` 下的预编译 `Updater_Linux-*` 不由本仓库链接，不受该 flag 影响。

## Follow-up: libusb 归入目标系统边界

`raspberry-pi` 出包时闭包报 `libusb-1.0.so.0`（由 `libh2api.so.2` 引用）无法解析，
因为 250 没有安装 arm64 的 libusb 运行时包。

判断依据：`build_pi.sh` 的 ldd 闭包只收集 Qt 与 ICU 家族，libusb 从来没有进过
原生包，而 108 上的 HTRA USB 功能一直正常。libusb 打开目标机的 usbfs 节点并与
目标的 udev/hotplug 配套，和已经在名单里的 `libudev` 属于同一类。

因此把 `libusb-1.0.so*` 加入 `is_target_provided_library`，与既有现场事实一致，
不是为了绕过检查而放宽。

### Verification

- `bash -n scripts/build.sh` 通过；`git diff --check` 通过。
- 用真实 `readelf -dW` 输出样本验证判定：`(RPATH)` 行接受，`(RUNPATH)` 行拒绝，
  无 rpath 拒绝。
- 未执行 Linux 构建；`DT_RPATH` 的实际生成与 20.04 直接启动由用户在 250 重编后
  验收。


