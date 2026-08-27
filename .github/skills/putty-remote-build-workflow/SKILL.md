---
name: putty-remote-build-workflow
description: 'Use when connecting from Windows to a Raspberry Pi, Linux host, or external device with PuTTY/plink to install dependencies, configure environment, run CMake or Qt builds, and monitor long compile progress safely. Covers stable plink invocation, anti-spoof handling, remote log monitoring, duplicate-build avoidance, and PowerShell quoting pitfalls.'
argument-hint: 'Describe the host, login method, project path, and whether you need environment setup, configure, build, or progress monitoring.'
user-invocable: true
---

# PuTTY Remote Build Workflow

## When to Use

- 在 Windows 上通过 PuTTY / `plink` 连接树莓派、Linux 主机、嵌入式板卡或其他外部设备。
- 需要远端安装依赖、补齐 Qt / CMake / 编译器环境、配置工程或启动长时间编译。
- 需要在 VS Code 终端里稳定执行 SSH 命令，并持续观察远端构建进度。
- 需要避免常见的 `plink` 调用、PowerShell 引号展开和重复构建问题。

## Core Rules

- 优先在当前终端会话里把 PuTTY 安装目录加入 `PATH`，随后直接调用 `plink`。
- 不要把 `$plink = '...'; & $plink ...` 当作默认模式；在 VS Code 持久 PowerShell + 工具包装环境里，这种写法可能不稳定。
- 自动化场景默认加 `-no-antispoof`，避免首次会话卡在 `Press Return to begin session`。
- 长时间构建只启动一份；监控进度时开第二个 SSH 会话读远端日志，不要把“重新运行构建命令”当作轮询方式。
- 需要采集远端退出码时，避免让 PowerShell 先展开远端命令里的 `$?`、`$rc` 等 shell 变量。

更具体的命令模板见 [PowerShell / plink snippets](./references/powershell-plink-snippets.md)。

## Procedure

1. 先稳定本地 `plink` 调用方式。
   - 先检查 `plink` 是否已在 `PATH` 中。
   - 如果没有，优先把 `C:\Program Files\PuTTY` 加入当前终端 `PATH`。
   - 只有在 `PATH` 注入不可行时，才回退到直接写完整路径。

2. 先做最小远端探活。
   - 用一条命令确认能登录、能执行远端 shell、主机名和系统信息可读。
   - 不要上来就跑大段安装脚本。

3. 用一条紧凑命令收集远端开发环境事实。
   - 操作系统版本。
   - `gcc` / `g++` / `cmake` / `qmake` / `qtcreator` 是否存在。
   - 当前已装的关键开发包。

4. 按真实缺口补环境。
   - 先补能解释当前失败现象的最小依赖。
   - 安装后先做最便宜的验证，例如重新 `cmake -S . -B ...`，不要盲目继续装更多包。

5. 长构建必须改成“远端日志 + 双会话”。
   - 构建会话：只负责启动构建，把输出重定向到远端日志文件。
   - 监控会话：只负责 `wc` / `tail` / `pgrep` 查看进度。
   - 如果本地构建会话看起来“没有输出”，先查远端日志和进程，不要立刻再次启动同一份构建。

6. 通过三个信号判断构建是否在推进。
   - 日志行数是否增长。
   - 尾部是否出现新的 `[x/y]`、`Building`、`Linking`、`Generating` 等进度行。
   - 远端是否还有 `cmake` / `ninja` / `make` / `c++` 进程。

7. 只有在进度信号消失后，才判断构建结束或失败。
   - 无构建进程。
   - 日志尾部出现最终错误或完成语句。
   - 如果此前把退出码写入日志，则读取 `BUILD_EXIT=` 一类标记。

8. 如果误启动了重复构建，先停掉重复任务，再继续观察唯一的一份构建。
   - 否则日志、进程和进度结论都会被污染。

## Token-Saving Defaults

- 把“探活 + OS + 工具链 + 已装包”合并成一条 SSH 命令，减少来回轮次。
- 对长构建日志默认只看尾部几十行，不读取整份日志。
- 先用 `pgrep` 判断是否仍在编译，再决定是否需要更多日志。
- 如果只是确认环境是否打通，优先做到“可配置 + 已进入正常编译”，不必每次都等整仓编译结束。

## Common Failure Modes

- `$plink` 未识别：
  - 原因：在当前工具环境里，`$plink = '...'; & $plink ...` 这种模式不够稳。
  - 处理：优先给当前终端加 `PATH`，然后直接调用 `plink`。

- `Press Return to begin session`：
  - 原因：`plink` 的 anti-spoof 提示阻塞了非交互流程。
  - 处理：默认加 `-no-antispoof`。

- 远端退出码变成 `True`、空值或其他异常内容：
  - 原因：PowerShell 先展开了远端命令里的 `$?` 或 `$rc`。
  - 处理：对整段远端命令使用 PowerShell 单引号，或显式转义远端 `$`。

- 误以为“没输出所以卡住了”：
  - 原因：`plink` 构建会话正在阻塞等待远端命令完成，而不是实时把全部进度安全回传给本地工具界面。
  - 处理：通过第二个 SSH 会话查看远端日志和进程，而不是重复提交构建命令。

- 重复启动构建：
  - 原因：把“再跑一次构建试试看”当作监控手段。
  - 处理：每次重跑前先 `pgrep`，确认远端没有同路径同目标的构建仍在运行。

## SGStudio Notes

- 本仓库日常 CMake 入口是仓库根目录的 `CMakeLists.txt`，不是 `src/` 子目录。
- 在 Linux / aarch64 环境下，当前仓库对 OpenSSL 路径有额外约束；如果系统 OpenSSL 已装但 CMake 仍找不到，需要检查仓库 CMake 是否要求 `/opt/aarch64-openssl`。

## Verified SGStudio Remote Profile

2026-06-12 已验证的树莓派远端入口：

- SSH host: `192.168.3.179`
- SSH user: `htra`
- SSH host key: `ssh-ed25519 255 SHA256:VgtR4/pnkPONoSY8NhUC8bH3SrgkAHVrVLMJI9TQYQ4`
- Project path: `~/Desktop/SGSProject`
- Git remote: `http://192.168.3.29:8089/rdd/sgstudio.git`
- Git HTTP user: `jiashilin`
- Verified command: `cd ~/Desktop/SGSProject && git pull`
- Verified result: `Already up to date.`

安全边界：

- 不要把 SSH 密码或 Git HTTP 密码写入 skill、TaskLog、脚本或 git remote URL。
- 需要密码登录时，在当前终端会话通过环境变量、Pageant/SSH key 或 Git credential helper 注入。
- 非交互 `git pull` 需要 Git HTTP 密码时，优先使用一次性 `GIT_ASKPASS` 临时脚本，命令结束后立刻删除。
- 具体模板见 [PowerShell / plink snippets](./references/powershell-plink-snippets.md) 的 SGStudio verified profile 小节。

## Expected Output

- 远端环境补齐过程可复现。
- `plink` 调用稳定，不再反复踩 `$plink` 和 anti-spoof 的坑。
- 长时间编译可以通过日志和第二会话稳定观察进度。
- 不再因为“看起来没输出”而误启动重复构建。
