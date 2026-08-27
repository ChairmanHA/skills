# Linux 三目标统一构建打包入口

## Scope

- 为 Raspberry Pi aarch64、Linux x86_64 X11、老系统 RK3588 Ubuntu 18.04 三类发布包新增一个用户入口。
- 入口只做目标路由和参数透传，不复制或重写现有底层构建逻辑。
- 新增一份长期 KnowledgeBase 文档，重点解释三者的系统/工具链/Qt/桌面后端/ABI/启动差异。

## Observation

- Raspberry Pi 的推荐 x86_64 主机构建路径是 `build_linux_bookworm_container.sh aarch64`：Debian 12 arm64 容器内执行原生 `build_pi.sh`，目标为 Wayland + LayerShellQt。
- Linux x86_64 当前发布路径是 `build.sh gcc_x86_64`：本机 GCC、`/opt/Qt/5.15.18/gcc_x86_64`、X11/xcb，关闭 LayerShellQt。
- 老 RK3588 路径必须经过 `build_rk3588.sh`：Ubuntu 18.04/GCC 7 aarch64 交叉编译、X11/xcb、严格旧 ABI 审计，并保留已实机验证的精确 modulation GLIBC 2.29 测试例外。
- Raspberry Pi 与 RK3588 虽然同为 aarch64，但 glibc、Qt 来源、桌面协议和打包依赖闭包完全不同，产物不可互换。
- `build_pi.sh` 仍是 Raspberry Pi 原生机器或容器内部实现，不应由使用者在 x86_64 主机上直接伪装调用。

## Design

- 新入口：`bash scripts/build_linux.sh <target> <packet> <language> [archive_name] [build options...]`。
- 规范 target：`raspberry-pi`、`x86_64`、`rk3588-ubuntu18`；提供少量明确别名。
- 路由：
  - `raspberry-pi` -> `build_linux_bookworm_container.sh aarch64`
  - `x86_64` -> `build.sh gcc_x86_64`
  - `rk3588-ubuntu18` -> `build_rk3588.sh`
- 新入口提供 `--list`、`--help`、`--dry-run`；dry-run 只打印最终命令，不执行下游脚本。
- 下游参数原样透传，因此 packet/language/archive/watermark/JOBS 等语义仍由原脚本负责。

## Success Criteria

1. 三个 canonical target 的 dry-run 均路由到正确底层脚本和环境参数。
2. 未知 target 和参数不足时返回非零并打印可操作用法。
3. 不改变现有底层脚本的直接调用方式、build tree 或归档目录。
4. 新文档包含命令、环境准备、输出路径、解包和启动命令、日志位置、禁止混用项及底层差异矩阵。
5. 更新 `.github/KnowledgeBase/Index.md`，并在既有构建脚本指南中链接统一入口文档。

## Verification Level

- `static`
- 对新脚本及所有被路由脚本执行 `bash -n`。
- 执行 `--list`、`--help`、三目标 `--dry-run` 和错误 target 检查。
- 执行 Markdown 链接/路径人工核对与 `git diff --check`。
- 不启动 Docker、CMake 或完整发布构建；三套底层构建已由各自现有流程负责，用户本轮要求的是入口与说明收敛。

## Implementation And Verification

- 新增 `scripts/build_linux.sh`，实现三个 canonical target、明确别名、`--list`、`--help` 和前置 `--dry-run`。
- 新增 `.github/KnowledgeBase/linux_build_package_unified_entry.md`，并更新 KnowledgeBase Index 与既有构建/水印指南入口提示。
- `bash -n` 已覆盖统一入口、三个下游 build wrapper/implementation 和两个 launcher template，全部通过。
- 三个 canonical target 的 dry-run 分别解析为预期命令：Bookworm arm64 container、`build.sh gcc_x86_64`、`build_rk3588.sh`。
- 裸 `aarch64` target 被拒绝并返回 1，防止 Raspberry Pi 与 RK3588 artifact 混淆。
- 新文档 4 个本地 Markdown 链接全部解析到现有文件；`git diff --check` 通过。
- 未启动 Docker、CMake 或发布构建，也未清理任何现有 build tree。
