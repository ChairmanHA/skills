# Ubuntu 18.04 VMware 虚拟机桥接直连树莓派

本文记录一套已经实机验证的网络方案：Windows 主机通过物理以太网直连树莓派，
VMware Workstation 中的 Ubuntu 18.04 虚拟机保留原 NAT 管理网卡，同时增加第二块
桥接网卡，以固定地址访问树莓派。

仓库提供自动化脚本：
[`scripts/configure_ubuntu18_vmware_raspberry_pi_bridge.ps1`](../../scripts/configure_ubuntu18_vmware_raspberry_pi_bridge.ps1)。

## 已验证拓扑

```text
Raspberry Pi                              Windows host
192.168.1.100/24  <--- physical cable ---> 192.168.1.11/24
                                                   |
                                      VMware bridged network (VMnet0)
                                                   |
Ubuntu 18.04 VM                                    |
  ens37: 192.168.1.22/24  <------------------------+
  ens33: 192.168.28.129/24 --- VMware NAT (VMnet8) ---> management/Internet
```

设计边界：

- `ens33` 保持 NAT，不修改原 SSH 管理地址和默认路由。
- `ens37` 只服务 `192.168.1.0/24`，不设置默认网关和 DNS。
- Ubuntu 到树莓派的流量从 `ens37` 发出；互联网流量仍走 `ens33`。
- Windows 上只让目标物理以太网参与 VMware 自动桥接，防止 VMware 错选 WLAN。

## 适用环境

脚本面向以下环境：

- Windows 10/11，VMware Workstation，具有管理员权限；
- Windows OpenSSH Client 已安装，`ssh.exe` 可用；
- Ubuntu 18.04 使用 NetworkManager，`nmcli` 可用；
- Ubuntu SSH 服务已启动，现有 NAT 地址能够从 Windows 访问；
- 树莓派和 Windows 物理以太网已经配置在同一 IPv4 子网；
- 虚拟机第二块网卡在 Ubuntu 中预期名为 `ens37`。如果实际名称不同，可通过脚本
  参数覆盖。

脚本默认值对应已验证机器：

| 项目 | 默认值 |
| --- | --- |
| VMX | `D:\development\Ubuntu18.04\Ubuntu18.04.vmx` |
| Windows 物理网卡 | `以太网` |
| Windows 有线地址 | `192.168.1.11/24`（脚本通过实际链路验证，不写入该地址） |
| Ubuntu NAT 地址 | `192.168.28.129` |
| Ubuntu SSH 用户 | `harogic` |
| Ubuntu 桥接接口 | `ens37` |
| Ubuntu 桥接地址 | `192.168.1.22/24` |
| 树莓派地址 | `192.168.1.100` |
| NetworkManager profile | `Raspberry Pi link` |

## 执行前检查

先确保 Windows 能通过物理网线访问树莓派。在 Windows PowerShell 中执行：

```powershell
Get-NetAdapter | Format-Table -AutoSize Name, Status, InterfaceDescription
Get-NetIPConfiguration -InterfaceAlias '以太网'
ping 192.168.1.100
```

`ping 192.168.1.100` 必须成功。若失败，先检查物理网线、Windows 有线地址和树莓派
地址；脚本会在任何 VMware 修改前执行相同的检查并在失败时停止。

在 Ubuntu 中可用以下命令确认管理条件：

```bash
systemctl is-active ssh
nmcli -t -f STATE general
ip -brief -4 address
```

预期 SSH 和 NetworkManager 均处于工作状态，原 NAT 地址仍可访问。

## 一键脚本

关闭或保存 Ubuntu 中未保存的应用。首次增加虚拟网卡时，脚本会请求一次正常关机，
并可能关闭后重新打开 VMware Workstation 界面。

以“管理员身份”打开 Windows PowerShell，在仓库根目录执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\configure_ubuntu18_vmware_raspberry_pi_bridge.ps1
```

脚本会依次要求：

1. 输入大写 `YES`，确认网络绑定、虚拟机正常关机和 VMX 修改；
2. 输入 Ubuntu SSH 登录密码；
3. 输入 Ubuntu `sudo` 密码。

密码只由 SSH/sudo 在终端中交互读取，脚本不接收密码参数，也不会把密码写入日志或
文件。配置 SSH 密钥后，SSH 登录密码提示可以省略，但 sudo 仍按 Ubuntu 策略提示。

如果环境与默认值不同，显式传参：

```powershell
.\scripts\configure_ubuntu18_vmware_raspberry_pi_bridge.ps1 `
    -VmxPath 'D:\VMs\Ubuntu18\Ubuntu18.vmx' `
    -BridgeAdapterName 'Ethernet' `
    -SshUser 'build' `
    -ManagementAddress '192.168.28.129' `
    -GuestBridgeInterface 'ens37' `
    -GuestAddress '192.168.1.22/24' `
    -RaspberryPiAddress '192.168.1.100'
```

使用 `-Force` 只会跳过 `YES` 确认，不会跳过 SSH/sudo 的安全认证：

```powershell
.\scripts\configure_ubuntu18_vmware_raspberry_pi_bridge.ps1 -Force
```

## 脚本实际执行的操作

1. 验证管理员权限、VMX、`vmrun.exe`、`ssh.exe`、目标 Windows 网卡和所有 IPv4
   参数。
2. 从 Windows ping 树莓派；物理链路失败时立即退出。
3. 在目标物理网卡启用 `VMware Bridge Protocol`，在其他物理网卡关闭该协议，
   然后重启 `VMnetBridge` 服务。
4. 检查 VMX 中指定的 `ethernet1`：
   - 已是启用的桥接网卡时保持不变，不关机；
   - 完全不存在时正常关闭目标虚拟机，确认没有其他 VMware 虚拟机运行，关闭
     Workstation UI，创建时间戳备份后追加桥接 `e1000` 网卡；
   - 已被其他用途占用时拒绝覆盖并报告错误。
5. 启动虚拟机并等待原 NAT 地址的 TCP/22 恢复。
6. 通过 SSH 创建或更新 `Raspberry Pi link`：
   - `connection.interface-name=ens37`；
   - `connection.autoconnect=yes`；
   - `connection.autoconnect-priority=100`；
   - `ipv4.method=manual`；
   - `ipv4.addresses=192.168.1.22/24`；
   - `ipv4.never-default=yes`；
   - 网关和 DNS 为空；
   - Ubuntu 18.04 的 NetworkManager 使用 `ipv6.method=ignore`。
7. 从 Ubuntu 的指定接口 ping 树莓派；失败会使脚本返回错误。
8. 从 Windows 验证 Ubuntu 新地址的 ICMP 或 TCP/22。

VMX 写入只发生在目标网卡完全不存在时。备份位于 VMX 同一目录，格式为：

```text
<virtual-machine>.vmx.backup-yyyyMMdd-HHmmss
```

## 验证命令

Ubuntu：

```bash
ip -brief -4 address
ip -4 route
nmcli -g connection.id,connection.interface-name,connection.autoconnect,ipv4.method,ipv4.addresses,ipv4.gateway,ipv4.never-default connection show 'Raspberry Pi link'
ping -I ens37 -c 4 -W 2 192.168.1.100
```

关键预期：

```text
ens33  UP  192.168.28.129/24
ens37  UP  192.168.1.22/24
default via 192.168.28.2 dev ens33
192.168.1.0/24 dev ens37 ... src 192.168.1.22
4 packets transmitted, 4 received, 0% packet loss
```

Windows：

```powershell
ping 192.168.1.22
Test-NetConnection 192.168.1.22 -Port 22
```

## 幂等性

脚本可以重复运行：

- 已存在的 VMX 桥接网卡不会重复添加；
- 已存在的 `Raspberry Pi link` 会被更新而不是重复创建；
- 已正确运行的虚拟机无需为了重复配置而关机；
- VMware 桥接绑定和 NetworkManager 参数会被重新核对并收敛到目标状态。

## 常见问题

### Windows 能 ping 树莓派，Ubuntu 不能

先检查 VMware 是否自动桥接到了 WLAN：

```powershell
Get-NetAdapterBinding -ComponentID vmware_bridge |
    Format-Table -AutoSize Name, DisplayName, Enabled
```

目标物理以太网应为 `True`，WLAN 应为 `False`。管理员 PowerShell 中可手动修正：

```powershell
Enable-NetAdapterBinding -Name '以太网' -ComponentID vmware_bridge
Disable-NetAdapterBinding -Name 'WLAN' -ComponentID vmware_bridge
Restart-Service VMnetBridge
```

不要关闭 WLAN 网卡本身；这里只关闭 WLAN 对 VMware 桥接协议的参与。

### Ubuntu 中没有 `ens37`

执行：

```bash
ip -brief link
lspci | grep -i ethernet
```

如果第二块网卡名称不是 `ens37`，把实际名称通过
`-GuestBridgeInterface '<name>'` 传入。如果根本没有第二块设备，确认脚本没有报告
“其他 VMware 虚拟机仍在运行”或“VMX adapter index 已被占用”。

### VMX 修改后新网卡仍未出现

观察：VMware Workstation UI 会缓存并回写 VMX。不能在 UI 仍持有旧配置时直接编辑
文件。脚本通过“正常关机 → 确认无其他 VM → 关闭 Workstation UI → 备份并编辑 →
启动”的顺序避免该问题。手工处理也必须遵守同一顺序。

### Ubuntu 18.04 报 `ipv6.method disabled` 不支持

Ubuntu 18.04 随附的旧版 NetworkManager 不接受该值。使用：

```bash
sudo nmcli connection modify 'Raspberry Pi link' ipv6.method ignore
```

脚本已经使用兼容值 `ignore`。

### SSH 无法恢复

确认原 NAT 网卡仍存在，并在 Windows 执行：

```powershell
Test-NetConnection 192.168.28.129 -Port 22
```

在 Ubuntu 控制台检查：

```bash
ip -brief -4 address show ens33
systemctl status ssh --no-pager
```

本方案不应修改 `ens33` 或其 NetworkManager profile。

## 回滚

### 只删除 Ubuntu 静态连接

```bash
sudo nmcli connection delete 'Raspberry Pi link'
```

### 恢复其他物理网卡参与 VMware 桥接

例如重新允许 WLAN：

```powershell
Enable-NetAdapterBinding -Name 'WLAN' -ComponentID vmware_bridge
Restart-Service VMnetBridge
```

注意：重新启用后，VMware 自动桥接可能再次选择 WLAN。更稳妥的做法是在 VMware
Virtual Network Editor 中把 VMnet0 明确绑定到目标物理以太网。

### 删除第二块 VMware 网卡

优先在虚拟机完全关机后，通过 VMware UI 的 VM Settings 删除 `Network Adapter 2`。
若必须恢复 VMX 备份，先确认目标虚拟机已经完全停止、备份路径无误，再以备份覆盖
当前 VMX。不要在虚拟机运行或 Workstation UI 仍缓存配置时覆盖。

## 已验证结果（2026-08-25）

- Ubuntu `ens37`：`192.168.1.22/24`；
- Ubuntu `ens33`：`192.168.28.129/24`；
- 默认路由仍为 `default via 192.168.28.2 dev ens33`；
- Ubuntu `ens37 -> 192.168.1.100`：4 发 4 收，0% 丢包；
- Windows `192.168.1.11 -> 192.168.1.22`：ping 成功；
- Windows 到 `192.168.1.22:22`：TCP 成功。

## VMware 参考

- [Broadcom KB 311353：自动桥接选择错误网卡时，把 VMnet0 指定到可用物理网卡](https://knowledge.broadcom.com/external/article/311353)
- [Broadcom KB 1018697：VMware Workstation 虚拟网络编辑器与多网卡桥接建议](https://knowledge.broadcom.com/external/article?legacyId=1018697)

