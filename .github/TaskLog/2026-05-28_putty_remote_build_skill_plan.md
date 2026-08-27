# 2026-05-28 PuTTY Remote Build Skill Plan

## 背景

- 当前仓库已经开始在真实设备上做环境补齐、依赖安装、CMake 配置与长时间编译验证，这类任务会反复出现。
- 本次对话暴露了几个可复用的操作坑：
  - 在 VS Code 持久 PowerShell 终端里使用 `$plink = '...'; & $plink ...` 不稳定，出现过 `$plink` 未被识别的问题。
  - `plink` 默认的 anti-spoof 提示会打断自动化流程。
  - 长时间编译时，如果只盯着一个 SSH 会话，容易误判“没输出”，进而重复启动同一份构建。
  - 通过 PowerShell 双引号把远端命令传给 `plink` 时，`$?` 这类符号可能先在本地被展开，污染远端退出码采集。
- 这些问题更适合沉淀成一个 workspace 级 skill，而不是继续靠单次对话临时试错。

## 目标

1. 创建一个 workspace 级 skill，专门指导通过 PuTTY / plink 连接外部 Linux 设备进行环境配置、依赖安装、CMake 配置与长时间编译。
2. 在 skill 中明确记录这次已验证有效的编译进度获取方式：远端日志文件 + 第二 SSH 会话监控，而不是重复触发构建。
3. 在 skill 中给出稳定的 Windows PowerShell 调用模式，避免再次出现 `$plink` 变量调用失败和远端命令被本地变量展开的问题。

## 设计结论

- skill 负责“如何做”：
  - 先定位 `plink.exe`。
  - 优先把 PuTTY 安装目录加入当前终端 `PATH`，随后直接调用 `plink`，避免用 `$plink` 变量二次调用。
  - 使用 `-no-antispoof` 消除首次连接提示对自动化的干扰。
  - 把长时间构建输出重定向到远端日志文件。
  - 通过第二个 SSH 会话执行 `wc` / `tail` / `pgrep` 监控进度，避免重复构建。
  - 需要远端退出码时，避免让 PowerShell 先展开远端命令中的 `$?`。
- 参考文件负责“命令模板”：
  - 放定位 `plink`、加 `PATH`、探活、安装依赖、配置 CMake、启动构建、监控日志、清理重复构建的现成命令模板。
- 不新增新的 agent 或 instruction；这类知识是按需加载的流程型能力，skill 是最合适的载体。

## 计划文件

- skill: `.github/skills/putty-remote-build-workflow/SKILL.md`
- reference: `.github/skills/putty-remote-build-workflow/references/powershell-plink-snippets.md`

## 非目标

- 不在本次新增脚本文件或自动化 hook。
- 不把所有远端开发场景都泛化；本 skill 只覆盖 Windows 上使用 PuTTY / plink 管理外部 Linux 设备的流程。
- 不替代仓库级构建说明；仓库特有的 CMake 入口和依赖要求只作为示例，不在 skill 中重复所有仓库文档。