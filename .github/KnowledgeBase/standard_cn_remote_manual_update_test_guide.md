# Standard CN 远程手动下载更新发布与测试指南

本文面向 SGStudio 发布人员、网站运维和测试人员，说明当前活跃代码下
Standard CN 包在 Windows x86_64 与 Linux aarch64 上进行远程手动下载更新测试时：

- 客户端实际请求什么 URL；
- 网站需要部署什么文件；
- 更新包必须是什么结构；
- 如何准备版本、发布文件并完成验收。

本文依据当前 CMake 实际包含的 `src/plugins/updater`、打包脚本和
`updater_firmware_update_mechanism.md` 编写；
## 1. 直接交给网站运维的结论

网站域名和下载前缀当前硬编码为：

```text
https://www.harogic.cn/
```

Standard CN 两个平台必须提供下面两个可认证下载的文件：

| 测试目标 | 客户端实际请求 URL | 网站根目录下的相对路径 | 发布文件 |
| --- | --- | --- | --- |
| Windows x86_64 | `https://www.harogic.cn/windows-x86_64/cn/SGStudio.zip` | `/windows-x86_64/cn/SGStudio.zip` | 完整 Windows Standard CN 更新包 |
| Linux aarch64 | `https://www.harogic.cn/linux-aarch64/cn/SGStudio.tar.gz` | `/linux-aarch64/cn/SGStudio.tar.gz` | 完整 Linux aarch64 Standard CN 更新包 |

需要特别注意：

1. 测试口径中的“Win32”在当前工程里实际指 Windows x86_64 构建，不是
   32 位 x86。代码没有单独的 32 位 Windows URL 分支。
2. URL 中没有 `standard` 目录。Standard 在编译期被映射成文件名
   `SGStudio`，CN 被映射成目录 `cn`。
3. URL 平台段使用连字符：
   `windows-x86_64`、`linux-aarch64`；本地构建产物目录使用下划线：
   `windows_x86_64`、`linux_aarch64`。不要把本地目录名原样复制成 URL。
4. URL 不带版本号，也没有 `latest.json` 或 manifest。每次发布是在同一个
   稳定 URL 上替换完整归档文件。
5. 文件名和大小写必须精确：
   `SGStudio.zip`、`SGStudio.tar.gz`。

可以把下面这段直接发给网站运维：

> 请在现有 `https://www.harogic.cn/` 下载服务上发布两个完整文件：
> `/windows-x86_64/cn/SGStudio.zip` 和
> `/linux-aarch64/cn/SGStudio.tar.gz`。请保持现有 HTTPS 与 Basic
> Authentication 兼容，使用临时文件上传完成后再原子替换正式文件，避免
> 客户端下载到半个包。两个 URL 的认证 GET 都必须返回 200 和完整归档内容。
> 测试期间请关闭或刷新 CDN/代理缓存，并反馈最终文件大小、SHA-256、发布时间
> 和回滚文件信息。

认证凭据不要写进交付文档或聊天记录。当前客户端携带编译期固定认证信息，
网站侧应沿用与现有客户端一致的认证配置；若修改认证信息，现有二进制不会
自动跟随。

## 2. 测试前的影响范围与隔离要求

当前客户端不会使用 `Settings.ini` 中的 `url` 值改变下载地址。
`PacketSpec::loadUrl()` 会忽略传入 URL，重新拼接上面的硬编码生产域名和路径。

因此：

- 仅修改客户端 `[Update] url=...` 不能切换到测试站；
- 一旦替换正式 URL 上的文件，所有相同平台和语言的客户端在用户主动点击
  `Download` 后都会取得该包；启动和打开更新窗口不会自动请求；
- 测试前必须确认受影响客户端范围，保存线上旧文件、大小和 SHA-256；
- 测试包应在约定测试窗口内原子发布，测试结束后按计划原子恢复或正式保留；
- 若要长期使用独立测试域名，需要先修改代码或提供证书正确、行为明确的
  网络隔离方案，不能仅靠运行时配置实现。

更新成功后，maintenance 会删除安装过程中的临时备份目录；当前没有面向用户的
一键软件回滚功能。测试机本身也应预先保留可恢复的旧安装包、现场数据备份或
系统镜像。

## 3. 应准备的两个包

推荐从同一提交、同一软件版本和同一份 `package-info/version.json` 生成：

### 3.1 Windows x86_64 Standard CN

在 Windows 仓库根目录执行：

```bat
scripts\build.bat standard cn SGStudio --rebuild --watermark off
```

预期产物：

```text
build/windows_x86_64/standard/cn/SGStudio.zip
```

上传后对应：

```text
https://www.harogic.cn/windows-x86_64/cn/SGStudio.zip
```

### 3.2 Linux aarch64 Standard CN

使用仓库配置好的 aarch64 交叉构建环境时：

```bash
scripts/build.sh raspberry-pi standard cn SGStudio --watermark off
```

在 192.168.3.108 树莓派本机原生构建时（正式发布优先）：

```bash
scripts/build_pi.sh standard cn SGStudio --watermark off
```

两种方式使用不同的本地产物目录，避免混淆来源：

```text
250 交叉包：build/linux_raspberry_pi_cross_aarch64/standard/cn/SGStudio.tar.gz
108 原生包：build/linux_pi_native_aarch64/standard/cn/SGStudio.tar.gz
```

上传后对应：

```text
https://www.harogic.cn/linux-aarch64/cn/SGStudio.tar.gz
```

不要把 Windows ZIP 改后缀作为 aarch64 包，也不要把 tar.gz 解开后逐文件上传。
网站发布的是打包脚本生成的完整归档。

### 3.3 当前工作区已有产物的注意事项

2026-07-29 静态检查发现：

- 现有 Windows `build/windows_x86_64/standard/cn/SGStudio.zip` 包含当前要求的
  `SGStudio/version.json`、`SGStudio/releasenote.txt`、
  `SGStudio/bin/maintenance.exe` 和 `SGStudio/updater/updater.exe`；
- 现有 aarch64 `build/linux_aarch64/standard/cn/SGStudio.tar.gz` 是
  2026-05-22 的旧包，包内仍是 `Updater_Linux-2.0.19`，而当前代码精确查找
  小写文件名 `updater`。这个旧包不满足当前解析契约，不能直接上传测试。

因此，两平台正式联测前建议都从目标提交重新出包，aarch64 包必须重建。

## 4. 归档内部结构

两个归档都必须只有一个有效的一级根目录，推荐且由脚本默认生成：

```text
SGStudio/
```

不要在同一归档顶层加入 `__MACOSX/`、旧版本目录、说明目录等其他一级目录。
当前解析器按名称排序选择第一个一级目录作为包根，多余目录可能让整个包失效。

### 4.1 Windows ZIP 最小关键结构

```text
SGStudio/
  version.json
  releasenote.txt
  bin/
    SGStudio.exe
    maintenance.exe
    ...
  plugin/
    Updater.dll
    ...
  updater/
    updater.exe
    ...
  configuration/
    Settings.ini
    cacert.pem
    ...
  QuickWaveFormData/
  images/
```

当前解析器对 Windows 包内名称的关键要求：

- `version.json` 位于包根；
- `releasenote.txt` 位于包根；
- `updater/updater.exe` 精确存在；
- `bin/maintenance*.exe` 至少存在一个；
- 软件、插件、依赖和资源必须是完整可运行发布目录，不能只放 updater。

### 4.2 Linux aarch64 tar.gz 最小关键结构

```text
SGStudio/
  version.json
  releasenote.txt
  bin/
    SGStudio
    maintenance
    ...
  plugin/
    libUpdater.so
    ...
  updater/
    updater
    ...
  configuration/
    Settings.ini
    cacert.pem
    ...
  lib/
  QuickWaveFormData/
  images/
```

当前解析器和运行链路对 aarch64 包的关键要求：

- `updater/updater` 文件名必须全部小写；
- `updater/updater`、`bin/maintenance` 和 `bin/SGStudio` 应保留可执行位；
- 250 交叉包另含根 `launch_sgstudio_pi.sh` 并通过它启动；108 原生包不含根
  launcher，直接执行 `bin/SGStudio`；在线更新解析器不依赖 launcher；
- tar.gz 必须保留 Linux 符号链接及其相对目标，不能用会解引用或丢失链接的方式
  重新打包；
- 目标机需要可执行系统 `tar` 命令，客户端用它解压 `.tar.gz`。

## 5. `version.json` 与手动更新门控

当前没有启动版本检查或自动提示。远端归档内 `version.json` 的 `Software`
只用于目标信息展示，不与本地编译的 `SGS_VERSION` 做更新门控。

例如：

```text
测试机已安装：2.7.1
远端 Software：2.7.2
结果：手动下载后显示信息，允许 Update

测试机已安装：2.7.2
远端 Software：2.7.2
结果：手动下载后显示信息，允许 Update

测试机已安装：2.7.2
远端 Software：2.7.1
结果：手动下载后显示信息，允许 Update，不显示降级警告
```

`version.json` 中的 `Version` 是包版本显示值，`Software` 是目标软件版本显示值。
二者都不替代包结构、关键程序和固件 Profile 有效性检查。

为保证发布记录和更新后显示一致，发布版本时至少保持下面字段一致：

| 语义 | 编译/源文件位置 | 包内位置 |
| --- | --- | --- |
| 软件版本 | 顶层 `CMakeLists.txt` 的 `SGS_DEFAULT_PROJECT_VERSION`，或明确的 `SGS_PROJECT_VERSION` 覆盖 | `SGStudio/version.json` 的 `Software` |
| 包版本 | 顶层 `CMakeLists.txt` 的 `SGS_DEFAULT_PACKET_VERSION`，或明确覆盖 | `SGStudio/version.json` 的 `Version` |
| 中文更新说明 | `package-info/releasenote_standard_cn.txt` | `SGStudio/releasenote.txt` |
| 固件目标与 Profile | `package-info/version.json` | `SGStudio/version.json` |

最稳妥的首轮联测方案是：

1. 两台测试机安装明确较旧的 Standard CN 基线；
2. 用当前较新提交同时生成 Windows 与 aarch64 包；
3. 确认两个包内 `Software` 与实际编译版本一致；
4. 不要只在归档中手工改 `Software`，避免更新后显示值与实际二进制不一致。

当前 `package-info/version.json` 使用 `SchemaVersion: 2` 固件 Profile。
如果保留 Schema 2，顶层 `FPGA`、`MCU`、`FX3`、`EIO` 默认值，以及
`Firmware.OptionNamespace`、`SelectorOptions`、每个选件组合对应的完整
`Profiles[].Components` 都必须有效。设备选件已知但找不到精确 Profile 时，
Update 按钮会被禁用。不要在不理解固件选择规则时删减该文件；优先直接使用
仓库维护的版本元数据出包。

## 6. 网站发布要求

网站运维应按下面顺序发布每个平台文件：

1. 在非正式文件名下完整上传，例如 `SGStudio.zip.uploading`。
2. 计算服务端文件大小和 SHA-256，与发布人员提供值核对。
3. 在同一文件系统内把临时文件原子替换成正式文件名。
4. 清理或刷新反向代理/CDN 缓存。联测阶段推荐对这两个稳定 URL 使用
   `Cache-Control: no-cache` 或等效重新验证策略。
5. 使用与客户端相同认证方式执行一次完整 GET，确认：
   - HTTPS 证书链有效；
   - Basic Authentication 仍兼容现有客户端；
   - 最终响应是 200；
   - 返回大小和 SHA-256 与上传文件一致；
   - 没有返回 HTML 登录页、WAF 页面或错误页。
6. 记录旧文件和新文件的大小、SHA-256、发布时间与回滚方法。

当前客户端允许 HTTP 重定向，并把 4xx/5xx 当作下载失败；不需要 Range
断点续传，也不读取独立 manifest。客户端目前不会对下载包执行发布方 SHA-256
或数字签名校验，所以网站侧原子替换、人工 hash 核对和访问控制是本轮测试的
必要保护措施。

## 7. 客户端准备

两台测试机分别准备旧版 Standard CN：

- Windows x86_64 测试机；
- Linux aarch64 测试机。

`Settings.ini [Update]/online` 已废弃。源码模板不再包含该键，正式打包脚本也不再
写入 `online=true`。旧安装中遗留的 `online=true/false` 会被当前客户端忽略，无需在
测试前修改。

注意：

- 启动 SGStudio、打开 UpdateDialog 和切换 `Latest Online` 都不应访问更新 URL；
- `url=` 当前不能改变最终下载地址；
- 更新安装过程仍会回灌旧安装中的 `configuration/Settings.ini`，因此旧的
  `online` 键可能继续存在于升级安装，但没有行为影响；
- 启动前关闭其他 SGStudio 实例，否则点击 Update 时会被阻止；
- Linux 安装根必须具备当前部署约定要求的更新写权限；
- Windows 执行更新时需要接受 maintenance 的管理员权限提示；
- 若同时验证固件更新，应连接测试设备并确认包内固件 Profile 覆盖其选件组合。

## 8. 推荐联测步骤

### 8.1 发布前离线检查

Windows：

```powershell
Get-FileHash build/windows_x86_64/standard/cn/SGStudio.zip -Algorithm SHA256
```

Linux aarch64：

```bash
sha256sum build/linux_pi_native_aarch64/standard/cn/SGStudio.tar.gz
tar -tzf build/linux_pi_native_aarch64/standard/cn/SGStudio.tar.gz | head
```

同时人工确认：

- 归档只有一个 `SGStudio/` 一级根目录；
- 两个包内 `version.json` 的 `Software` 相同且高于测试机版本；
- Windows 有 `updater/updater.exe` 和 `bin/maintenance.exe`；
- aarch64 有可执行的 `updater/updater`、`bin/maintenance` 和
  `bin/SGStudio`；
- 两个包都有正确的中文 `releasenote.txt`。

### 8.2 网站上线检查

1. 运维原子发布两个文件。
2. 运维从服务端或受控客户端执行带认证的完整 GET。
3. 发布人员核对下载文件大小与 SHA-256。
4. 在开始客户端测试前再次确认没有缓存返回旧文件。

不要把认证口令直接写在 shell history、截图或测试报告中。

## 9. 当前机制的安全边界

本轮联测应明确记录以下现状：

- URL、域名和客户端认证信息仍在代码中固定；
- 运行时 `url` 配置没有真正接入；
- 客户端没有包 hash 或数字签名校验；
- 当前通过 HTTPS、服务端认证、受控发布、原子替换和人工 SHA-256 核对降低
  联测风险，但这些措施不能替代后续客户端签名校验。

正式扩大在线更新范围前，应继续跟踪
`updater_mechanism_gap_and_remediation.md` 中的 TLS、凭据、完整性与发布安全整改项。

## 10. 代码依据

- URL 生成：`src/plugins/updater/packetspec.cpp`
  `currentRemotePackageUrl()`
- Standard 文件名映射：`src/plugins/updater/CMakeLists.txt`
- 启动 handoff 接管与单次清扫：`src/plugins/updater/plugin.cpp`
- 手动 Download 与进度交互：`src/plugins/updater/updatedialog.cpp`
- 包解析和关键文件查找：`src/plugins/updater/packetspec.cpp`
- Windows 打包：`scripts/build.bat`
- Linux/aarch64 打包：`scripts/build.sh`、`scripts/build_pi.sh`
- 版本与 release note：`package-info/CMakeLists.txt`、
  `package-info/version.json`、`package-info/releasenote_standard_cn.txt`
- 当前更新链路：`updater_firmware_update_mechanism.md`
