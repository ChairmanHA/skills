# Updater 更新架构（SAStudioEx）— 含客户端与更新服务器通信

> 目的：在已有“Updater 插件 + maintenance + tools 脚本 +（可选）在线 zip”更新架构说明上，补全**客户端与更新服务器通信**（URL、证书信任、curl/wget 调用、断点续传与完整性校验）部分，方便内网落地与排障。
>
> 适配的更新服务器：本仓库提供的 ASP.NET Core(Kestrel) HTTPS 静态文件服务（默认 `8443`）。

---

## 1. 组件与职责（简述）

### 1.1 Updater 插件（GUI 入口）
- 读取 `../configuration/Settings.ini` 的 `[Update]`：
  - `url`：在线更新包地址（zip 或入口文件地址）
  - `online`：在线开关
- 注册菜单动作，展示更新对话框。

### 1.2 UpdateDialog（更新 UI + 模式选择）
- 在线 / 本地默认 / 选择本地 zip 三种模式。
- 不直接执行脚本：构造 maintenance 路径与脚本参数，以提权方式启动 maintenance 后退出主程序。

### 1.3 PacketSpec（更新包规格解析）
- 统一抽象三种来源（本地默认 / 在线 zip / 本地 zip）为：
  - 版本信息（Suite/Software/FPGA/MCU/BUS/GNSS/ReleaseNote）
  - maintenance 可执行程序路径
  - 脚本路径（install/update）
- zip 包：解压后递归寻找 `tools/`，读取 `tools/tools.json`，并定位 `tools/install.bat|sh`。

### 1.4 Loader（加载/下载 zip）
- LocalFileLoader：用户选择 zip。
- RemoteFileLoader：从 URL 下载 zip（Windows：libcurl；Linux：wget/curl）。

### 1.5 Decompressor（解压）
- Windows：libzip + Zip Slip 防护。
- Linux：外部 unzip/tar。

### 1.6 maintenance（独立更新执行器）
- 解析命令行：第 1 个参数是脚本路径，其余为脚本参数。
- Windows 通过 `cmd.exe /D /C call <script> <args...>` 执行 `.bat`，并展示输出。

---

## 2. Win32 更新流程（端到端简述）

1) 用户点击“更新”后选择模式。
- 本地默认：`../tools/update.bat`
- 在线/本地 zip：下载/解压 → 找到 `tools/install.bat` → maintenance 执行 `install.bat <targetInstallPath>`

2) Windows 下 UpdateDialog 提权启动 maintenance（RunAs），然后主程序退出避免文件占用。

3) maintenance 执行脚本并展示日志。

---

## 3. tools 目录脚本（简述）

- `update.bat`：调用设备/固件更新器，并透传 `%ERRORLEVEL%`。
- `install.bat`：通常先调用 `update.bat`，再将整包根目录递归覆盖拷贝到安装目录。

风险提示（建议后续改进）：
- 若 `install.bat` 不检查 `update.bat` 返回码，可能导致“固件失败但软件仍覆盖”。

---

## 4. 已知风险点（实现层面）

- 固件失败不阻断软件覆盖（脚本层）。
- 缺少回滚编排（maintenance 只展示输出）。
- 下载鉴权信息硬编码（若存在）：有安全与运维风险。

---

## 5. 客户端与更新服务器通信（HTTPS 静态文件分发）

这一节回答：
- 客户端应该请求哪些 URL？
- 如何保证 HTTPS 可信（不弹“证书不安全”/curl 校验通过）？
- 如何下载大文件（断点续传/重试）并做完整性校验？

### 5.1 服务器提供的静态资源与 URL 约定
推荐在服务器项目 `wwwroot/` 下组织：
- `wwwroot/updates/latest/tools.json`
- `wwwroot/updates/releases/SAStudio4.zip`

客户端访问示例：
- 健康检查：`https://<server-ip>:8443/health`
- 版本/元信息：`https://<server-ip>:8443/updates/latest/tools.json`
- 包下载：`https://<server-ip>:8443/updates/releases/<version>/SAStudio4.zip`

建议（可选但强烈推荐本期不做）：
- 引入 `manifest.json`，包含 `PackageUrl/PackageSize/PackageSha256`，让客户端先拉取 KB 级入口再决定是否下载大 zip。

### 5.3 证书文件在客户端与服务器的分工
- 服务器侧：使用 `server.pfx`（含私钥）配置到 Kestrel。
- 客户端侧：只需要 `root-ca.cer`（不含私钥），用于建立对服务器证书链的信任。

严禁：把 `server.pfx` 放进客户端包或共享目录。

### 5.4 Windows 客户端：curl 校验的两种方案

#### 方案 A（更推荐）：把 Root CA 安装到系统受信任根
- 将 `root-ca.cer` 导入到 Windows “受信任的根证书颁发机构”。
- 优点：浏览器与系统组件（Schannel、.NET）统一信任；通常无需每次 `--cacert`。

#### 方案 B：客户端随包携带 `root-ca.cer`，每次显式校验
这在 Linux 或 OpenSSL 后端 curl 上很稳；Windows 取决于 curl 后端：
- 若使用系统自带 `curl.exe`（常见是 Schannel），可能受系统吊销检查/策略影响。

实用建议：
- 如果你们追求“行为一致且无需管理员”，倾向于**随客户端自带 OpenSSL 后端的 curl.exe**，固定使用：
  - `curl.exe --cacert <appDir>\root-ca.cer https://<server-ip>:8443/...`

提示（内网常见）：
- 若 Schannel 因吊销检查失败，可用于验证的参数：`--ssl-no-revoke`。

### 5.5 Linux 客户端：wget/curl 校验
- wget：`wget --ca-certificate=/path/to/root-ca.cer https://<server-ip>:8443/... -O tools.json`
- curl：`curl --cacert /path/to/root-ca.cer https://<server-ip>:8443/... -o tools.json`

也可将 Root CA 安装到系统 CA，免每次指定 `--cacert`。

### 5.6 大文件下载：断点续传与重试

建议客户端支持 Range/Resume：
- curl：`curl -C - -o SAStudio4.zip https://<server-ip>:8443/updates/releases/<version>/SAStudio4.zip`
- wget：`wget -c -O SAStudio4.zip https://<server-ip>:8443/updates/releases/<version>/SAStudio4.zip`

说明：
- Kestrel 静态文件通常支持 Range；客户端可在网络不稳时继续下载。

### 5.7 完整性校验（强烈推荐，但是暂时不做）

仅靠 HTTPS 仍建议做文件级校验，防止：
- 误传/误缓存/半包/磁盘损坏

推荐在 `manifest.json`（或 tools.json）提供：
- `PackageSha256`（十六进制）
- `PackageSize`
- `PackageUrl`

客户端下载后校验：
- Windows：`certutil -hashfile SAStudio4.zip SHA256`
- Linux：`sha256sum SAStudio4.zip`

校验通过后再进入解压与执行脚本流程。

### 5.8 与现有 Updater 架构的对接建议

如果当前 RemoteFileLoader 下载的是 zip：
- `url` 可以直接指向 zip，也可以指向 `manifest.json` 由客户端解析再下载真正的 zip。
- 若需要认证：避免把账号密码硬编码在代码；更推荐通过配置文件/凭据管理/短期 token。

---

## 6. 最小可执行的“客户端通信”流程（参考）

1) GET `https://<server-ip>:8443/updates/latest/manifest.json`（或 tools.json）
2) 比对本地版本 → 决定是否更新
3) 下载 zip（支持断点续传）
4) 交给现有 maintenance + tools/install.bat 执行

---

## 7. 快速排障清单（通信相关）

- 浏览器提示不安全：
  - Root CA 未安装到受信任根？
  - 证书 SAN 未包含访问使用的 IP/DNS？
- curl 报 unknown CA：客户端未信任 Root CA 或 curl 后端不使用系统证书库。
- curl 报吊销检查失败：内网环境常见，可用 `--ssl-no-revoke` 验证；生产策略需按企业安全要求定。
- 下载中断：启用断点续传（curl `-C -` / wget `-c`）。

---