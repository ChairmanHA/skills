# 192.168.3.250 -> 192.168.3.132 交叉编译兼容性文档任务

## 目标

形成一份可长期维护的 KnowledgeBase 文档，比较 192.168.3.250 交叉编译机与
192.168.3.132 Raspberry Pi 运行机，并说明当前 `scripts/build.sh aarch64`
为什么能够在 250 构建、在 132 运行。

## 范围

- 记录两台机器已经确认的系统、CPU、glibc、GCC、Qt 和桌面协议差异。
- 区分构建宿主、AArch64 目标工具链/Qt 前缀、真实运行系统三个层次。
- 把兼容性结论逐项对应到 `build.sh`、`launch_pi.sh`、toolchain 和 RPATH 配置。
- 明确随包运行库与目标系统运行库的边界、launcher-only 合同和失败排查步骤。
- 不再次连接远程机器，不编译，不执行目标程序，不修改构建逻辑。

## 已知证据

- 250：Ubuntu 18.04/x86_64、glibc 2.27、AArch64 GCC 7.5、目标 Qt 5.15.18。
- 132：Debian 12/aarch64、glibc 2.36、GCC 12.2、系统 Qt 5.15.8、labwc/Wayland。
- 250 历史产物已确认是 AArch64；主程序使用相对 RUNPATH；目标 Qt Wayland
  runtime 完整。
- 历史包已测得最高需求为 GLIBC 2.29、GLIBCXX 3.4.22、CXXABI 1.3.11；132
  提供的上限分别覆盖到 GLIBC 2.36、GLIBCXX 3.4.30、CXXABI 1.3.13。
- 当前 `build.sh aarch64` 已加入架构、ABI、Wayland runtime、禁止捆绑系统核心库、
  绝对动态路径和 launcher/`qt.conf` 合同审计。

## 成功标准

- 文档不把“可能可运行”表述成无条件、跨任意系统的二进制保证。
- 清楚解释 ABI 兼容方向是“旧需求由新运行时满足”，反向不成立。
- 清楚解释 Qt 5.15.18 随包隔离与系统 Qt 5.15.8 的关系。
- 给出在 250 构建、传输、在 132 启动与验证的可复制命令。
- KnowledgeBase `Index.md` 包含新文档入口。

## 验证级别

`static`

- 静态核对文档中的脚本行为与当前仓库一致。
- 执行 Markdown 链接/格式和 `git diff --check` 检查。

## 完成结果

- 新增 `KnowledgeBase/raspberry_pi_132_build_host_250_compatibility.md`，覆盖环境
  对比、三层构建模型、ABI 方向、Qt 隔离、系统库边界、脚本门禁、构建/部署命令、
  目标端验收和故障矩阵。
- 更新 `KnowledgeBase/Index.md` 的 Recent Additions 与“部署与工程”入口。
- 逐项对照当前 `build.sh` 中的 AArch64 profile、ABI 上限、必需 runtime、禁止
  捆绑库和 launcher 合同；文档中的成功日志与脚本实际输出一致。
- 文档内所有相对 Markdown 链接均可解析，`git diff --check` 通过。
- 按用户要求没有再次连接 132/250，没有编译或启动应用。
