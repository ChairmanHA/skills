# PowerShell / plink Snippets

下面这些片段是给 Windows PowerShell 5.1 + VS Code 终端准备的，优先级从上到下。

## 1. 先把 PuTTY 加进当前终端 PATH

```powershell
Get-Command plink -ErrorAction SilentlyContinue | Select-Object -First 1 Source

if (-not (Get-Command plink -ErrorAction SilentlyContinue)) {
    $env:PATH = 'C:\Program Files\PuTTY;' + $env:PATH
}

Get-Command plink | Select-Object -First 1 Source
```

优先用这个模式，而不是：

```powershell
$plink = 'C:\Program Files\PuTTY\plink.exe'
& $plink ...
```

原因：在当前工具包装环境里，变量赋值和随后的 `& $plink` 调用不如直接走 `PATH` 稳定。

## 2. 最小探活

```powershell
plink -no-antispoof -ssh user@host -P 22 -pw password "hostname; uname -a"
```

先确认连通性，再做环境收集和安装。

## 3. 一次性收集远端环境事实

```powershell
plink -no-antispoof -ssh user@host -P 22 -pw password "hostname; uname -a; command -v gcc || true; command -v g++ || true; command -v cmake || true; command -v qmake || true; command -v qtcreator || true"
```

如果还要看包管理器状态，再把 `dpkg -l | egrep 'cmake|qtbase5-dev|qtcreator|qtmultimedia5-dev|qtbase5-private-dev|libssl-dev' || true` 接在后面。

## 4. SGStudio 根目录配置示例

```powershell
plink -no-antispoof -ssh user@host -P 22 -pw password "cd ~/Desktop/sgs26/sgsproject/sgstudio; cmake -S . -B build/pi-qtcreator-check -G Ninja"
```

## 5. 启动长构建，只写远端日志

```powershell
plink -no-antispoof -ssh user@host -P 22 -pw password "cd ~/Desktop/sgs26/sgsproject/sgstudio; cmake --build build/pi-qtcreator-check -j4 > build/pi-qtcreator-check.build.log 2>&1"
```

这条命令本地可能长时间没有新输出，不等于远端没在编译。

## 6. 用第二个 SSH 会话监控构建进度

```powershell
plink -no-antispoof -ssh user@host -P 22 -pw password "cd ~/Desktop/sgs26/sgsproject/sgstudio; wc -l build/pi-qtcreator-check.build.log; tail -n 80 build/pi-qtcreator-check.build.log; pgrep -af 'cmake|ninja|make|c\+\+|g\+\+' || true"
```

观察这三类信号：

- 日志总行数是否增长。
- 尾部进度行是否前进。
- 是否还有构建相关进程。

## 7. 远端退出码采集，避免 PowerShell 抢先展开 `$?`

推荐写法一：PowerShell 外层用单引号包住整段远端命令。

```powershell
plink -no-antispoof -ssh user@host -P 22 -pw password 'cd ~/Desktop/sgs26/sgsproject/sgstudio; cmake --build build/pi-qtcreator-check -j4 > build/pi-qtcreator-check.build.log 2>&1; rc=$?; echo BUILD_EXIT=$rc >> build/pi-qtcreator-check.build.log'
```

推荐写法二：如果必须用 PowerShell 双引号，则手工转义远端 `$`。

```powershell
plink -no-antispoof -ssh user@host -P 22 -pw password "cd ~/Desktop/sgs26/sgsproject/sgstudio; cmake --build build/pi-qtcreator-check -j4 > build/pi-qtcreator-check.build.log 2>&1; rc=`$?; echo BUILD_EXIT=`$rc >> build/pi-qtcreator-check.build.log"
```

不要直接写下面这种形式：

```powershell
plink ... "...; rc=$?; echo BUILD_EXIT=$rc ..."
```

因为这里的 `$?`、`$rc` 可能被本地 PowerShell 先展开。

## 8. 先查进程，再决定是否重跑构建

```powershell
plink -no-antispoof -ssh user@host -P 22 -pw password "pgrep -af 'cmake --build build/pi-qtcreator-check|/usr/bin/ninja -j 4' || true"
```

如果已有在跑的构建，不要重复启动。

## 9. 清理误启动的重复构建

```powershell
plink -no-antispoof -ssh user@host -P 22 -pw password "pkill -f 'cmake --build build/pi-qtcreator-check|/usr/bin/ninja -j 4' || true"
```

先缩小匹配范围，再执行清理，避免误杀无关远端任务。

## 10. 本次 SGStudio 树莓派环境补齐的已验证缺口

Debian 12 / aarch64 上，本次实际补齐后能通过根目录 CMake 配置并进入正常编译的关键包是：

- `cmake`
- `qtmultimedia5-dev`
- `qtbase5-private-dev`
- `libssl-dev`

如果系统 OpenSSL 已装但仓库 CMake 仍报找不到 OpenSSL，再检查是否需要把：

- `/opt/aarch64-openssl/include` 映射到 `/usr/include`
- `/opt/aarch64-openssl/lib` 映射到 `/usr/lib/aarch64-linux-gnu`

## 11. SGStudio verified profile: 远端 `git pull`

2026-06-12 已验证远端：

- SSH host: `192.168.3.179`
- SSH user: `htra`
- SSH host key: `ssh-ed25519 255 SHA256:VgtR4/pnkPONoSY8NhUC8bH3SrgkAHVrVLMJI9TQYQ4`
- Project path: `~/Desktop/SGSProject`
- Git remote: `http://192.168.3.29:8089/rdd/sgstudio.git`
- Git HTTP user: `jiashilin`

不要把明文密码写入本文件。当前终端会话里用环境变量注入：

```powershell
$env:SGS_PI_SSH_PASSWORD = '<ssh-password>'
$env:SGS_GIT_HTTP_PASSWORD = '<git-http-password>'
```

最小探活：

```powershell
plink -batch -no-antispoof -hostkey "ssh-ed25519 255 SHA256:VgtR4/pnkPONoSY8NhUC8bH3SrgkAHVrVLMJI9TQYQ4" -ssh htra@192.168.3.179 -P 22 -pw $env:SGS_PI_SSH_PASSWORD "hostname; uname -a; test -d ~/Desktop/SGSProject && echo SGSProject_FOUND || echo SGSProject_MISSING"
```

如果远端 Git 凭据已经配置好，直接拉取：

```powershell
plink -batch -no-antispoof -hostkey "ssh-ed25519 255 SHA256:VgtR4/pnkPONoSY8NhUC8bH3SrgkAHVrVLMJI9TQYQ4" -ssh htra@192.168.3.179 -P 22 -pw $env:SGS_PI_SSH_PASSWORD 'cd ~/Desktop/SGSProject && git pull'
```

如果远端 Git 需要 HTTP 用户名/密码，用一次性 `GIT_ASKPASS`。注意：PowerShell 单引号参数里要用 `''\r''`，让远端真正收到 `tr -d '\r'`。

```powershell
$gitPasswordB64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($env:SGS_GIT_HTTP_PASSWORD))

@"
#!/bin/sh
case "`$1" in
*Username*) printf '%s\n' 'jiashilin' ;;
*Password*) printf '%s' '$gitPasswordB64' | base64 -d; printf '\n' ;;
*) printf '\n' ;;
esac
"@ | plink -batch -no-antispoof -hostkey "ssh-ed25519 255 SHA256:VgtR4/pnkPONoSY8NhUC8bH3SrgkAHVrVLMJI9TQYQ4" -ssh htra@192.168.3.179 -P 22 -pw $env:SGS_PI_SSH_PASSWORD 'cd ~/Desktop/SGSProject && raw=$(mktemp) && tmp=$(mktemp) && cat > "$raw" && tr -d ''\r'' < "$raw" > "$tmp" && rm -f "$raw" && chmod 700 "$tmp" && GIT_ASKPASS="$tmp" GIT_TERMINAL_PROMPT=0 git pull; rc=$?; rm -f "$tmp" "$raw"; exit $rc'
```

本次验证结果：

```text
Already up to date.
```
