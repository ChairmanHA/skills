# RK3588 直连以太网静态 IPv4 配置脚本

## Scope

- 读取 Windows 主机当前有线网卡配置，确定 RK3588 直连网段与可用静态地址。
- 提供可复制到 RK3588、以 root 权限运行的 Bash 脚本。
- 脚本自动识别已连接的非无线以太网接口，立即配置 IPv4，并按设备现有网络管理器保存持久配置。
- 不修改 SGStudio 产品代码，不改变 Windows 主机网络配置。

## Observation

- Windows 有线接口“以太网”链路为 Up，IPv4 是手工配置的 `192.168.1.11/24`，DHCP 已禁用。
- 该接口上的 `192.168.1.1` 邻居不可达，链路上没有可供 RK3588 获取租约的 DHCP 服务。
- `192.168.1.12` 当前无 ARP 邻居且 ping 无响应，可作为 RK3588 的直连静态地址。
- Windows 同时通过 WLAN 使用 `192.168.3.186/24` 上网，因此 RK3588 直连接口不应接管默认路由。

## Inference

- RK3588 无法取得 IPv4 的直接原因是直连链路没有 DHCP 服务，而不是 Windows 有线物理链路断开。
- 给 RK3588 配置 `192.168.1.12/24` 后，Windows 可经本地直连路由 `192.168.1.0/24` 访问它，无需先调整 Windows IPv4 或启用 Internet Connection Sharing。

## Success Criteria

1. 脚本只选择 carrier 为 1 的非无线物理以太网接口；存在歧义时停止而不猜测。
2. RK3588 立即获得 `192.168.1.12/24`，并保留其他接口（尤其是 Wi-Fi）的默认路由。
3. 脚本为 NetworkManager、systemd-networkd 或 ifupdown 中实际存在的一种写入持久配置。
4. 运行结束时打印地址、到 `192.168.1.11` 的路由和 ping 诊断，便于 Windows 端继续验证。

## Verification Level

- `static`
- 使用 Bash 语法检查；最终端到端连通性需在 RK3588 运行脚本后由 Windows 主机 ping `192.168.1.12` 验证。

## Verification Evidence

- `C:\Program Files\Git\bin\bash.exe -n scripts/configure_rk3588_direct_ethernet.sh` 通过。
- `file` 确认脚本是 UTF-8 Bash 可执行文本；`git diff --check` 未发现空白错误。
- Windows 静态核对确认 `192.168.1.0/24` 已存在于有线接口，脚本无需添加 Windows 路由或更改 WLAN 默认路由。
- RK3588 端运行和 Windows 回 ping 尚待用户把脚本复制到设备后执行。

## Follow-up: 直连 SSH 救援

### Observation

- 用户反馈系统界面已显示网络连接，但 Windows 对 `192.168.1.12` 的 ARP 邻居仍为 `Unreachable`，ping 无回复，`plink` 连接超时。
- FlClash 会让 `Test-NetConnection 192.168.1.12 -Port 22` 经 `198.18.0.1` 虚拟接口返回假阳性；该结果不能证明 RK3588 可达。
- Windows 有线接口和 `192.168.1.0/24` 本地路由仍然正确，无需通过提权改动 Windows。

### Plan And Success Criteria

- 新增一个 RK3588 端临时救援脚本，仍按 carrier 自动选择非无线物理接口，存在歧义时停止。
- 强制刷新目标接口的运行时 IPv4 为 `192.168.1.12/24`，并明确写入到 Windows 的直连路由；不修改其他接口和默认路由。
- 启动设备已有的 OpenSSH server；若系统未安装则只报告准确安装提示，不擅自联网安装软件。
- 若检测到 UFW、firewalld 或 iptables，只为 `192.168.1.11` 放行 ICMP echo 和 TCP/22。
- 打印接口、地址、路由、ARP 和 SSH 监听状态，供 Windows 端立即复测。

### Follow-up Evidence

- 用户运行救援脚本后，Windows 已能 ping 通 `192.168.1.12`，并已通过固定 ED25519 指纹使用 `plink` 登录。
- 目标内核报告无法初始化 iptables `filter` 表；这不是地址或 SSH 的必要条件。
- 救援脚本已改为先探测 `iptables -t filter -S INPUT`，不可用时只警告并继续启动 SSH。
