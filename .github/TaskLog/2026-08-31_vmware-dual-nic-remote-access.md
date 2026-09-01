# VMware 双网卡远程访问排障补充

## Scope

- 补充 `putty-remote-build-workflow`，覆盖 Windows 主机连接本地 VMware Linux 虚拟机时的双网卡入口发现与误路由判断。
- 记录可复用的 NAT / bridged 判别方法，不记录 SSH、Git 密码，不把 DHCP 地址写成固定事实。
- 不修改虚拟机网卡、主机永久路由、SSH 配置或 SGStudio 源码。

## Observation

- VMX 中 `ethernet0.connectionType = "nat"`，其 MAC 对应来宾 `ens33`；当前地址为 VMware NAT 网段 `192.168.28.129/24`。
- VMX 中 `ethernet1.connectionType = "bridged"`，其 MAC 对应来宾 `ens37`；来宾配置为 `192.168.1.22/24`，但 RX 包为 0。
- Windows 主机的 VMnet8 地址为 `192.168.28.1/24`，WLAN 地址为 `192.168.3.124/24`，物理以太网处于断开状态。
- 通过 NAT 地址可正常进入 SSH 密码认证；桥接地址不可通过真实物理路径到达。
- FlClash TUN 曾对不可达的桥接地址返回 TCP accept 后立即关闭，造成“22 端口开放但 SSH banner 前断开”的假象。

## Inference

- `UP/RUNNING` 只说明来宾虚拟网卡已启用，不证明桥接后的物理二层网络或 IP 子网可达。
- 当前最可靠的主机到来宾入口是 VMnet8 NAT 地址；桥接入口只有在绑定到有效物理适配器且来宾地址属于该适配器所在子网时才应使用。

## Plan

1. 在远程 workflow 中增加本地 VMware 入口预检与 VMX/MAC 映射方法。
2. 增加代理/TUN 假响应的识别规则，并要求用实际源地址、路由和 SSH banner 交叉验证。
3. 记录 NAT、桥接两种简化方案的选择边界，不自动更改网络配置。

## Success Criteria

- 后续任务不会仅凭用户给出的来宾静态 IP 或 TCP connect 成功判断 SSH 可达。
- 能优先用 `vmrun getGuestIPAddress` 发现当前 NAT 地址，并通过 VMnet8 直连。
- 能依据 VMX connection type 与 MAC 地址准确映射 Linux 接口。
- skill 不包含任何密码或带凭据的 Git URL。

## Verification Level

- `static`
- 重新读取 skill，检查 frontmatter 与新增说明。
- 运行 skill 的 `quick_validate.py`。
