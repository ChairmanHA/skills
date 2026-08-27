# Ubuntu 18.04 VMware 虚拟机直连树莓派桥接配置

## 目标

把本机已经实机验证的网络配置过程沉淀为可复用文档和 Windows PowerShell
脚本，使相同环境可以保留 Ubuntu 虚拟机原有 NAT 管理链路，同时增加一条
桥接到物理以太网的静态地址链路，用于访问直连树莓派。

## 已验证环境与观察

- Windows 物理以太网：`192.168.1.11/24`，树莓派：`192.168.1.100/24`。
- VMware Workstation 虚拟机：Ubuntu 18.04.6，原管理网卡 `ens33` 使用 NAT
  地址 `192.168.28.129/24`。
- 新增第二块 VMware `e1000` 网卡后，Ubuntu 将其识别为 `ens37`。
- Ubuntu 使用 NetworkManager；持久连接 `Raspberry Pi link` 配置为
  `192.168.1.22/24`、无网关、`ipv4.never-default=yes`。
- VMware 自动桥接最初选择了 WLAN；关闭 WLAN 上的 `VMware Bridge Protocol`
  并只保留目标物理以太网的桥接绑定后，虚拟机到树莓派 ping 变为 0% 丢包。
- VMware Workstation 图形进程会缓存并回写 VMX。新增虚拟硬件时必须先正常停止
  虚拟机并关闭该图形进程，再修改 VMX，否则新配置可能被旧缓存覆盖。

## 实现范围

- 新增一个 Windows 管理员 PowerShell 脚本：
  - 参数化 VMX、物理网卡、SSH 用户/地址、Ubuntu 静态地址和树莓派地址；
  - 保留原 `ethernet0` NAT 网卡；
  - 缺少时新增 `ethernet1` 桥接网卡，并为 VMX 创建时间戳备份；
  - 只让用户指定的 Windows 网卡参与 VMware 自动桥接；
  - 通过 SSH 在 Ubuntu 上创建/更新 NetworkManager 持久连接；
  - 验证 Ubuntu 到树莓派以及 Windows 到 Ubuntu 新地址的连通性；
  - 不保存 SSH 或 sudo 密码。
- 新增 KnowledgeBase 操作文档并更新 `Index.md`。
- 不修改 SGStudio 源码、CMake 或现有 Linux/RK3588 网络脚本。

## 安全与边界

- 脚本必须要求 Windows 管理员权限，并在修改前精确验证 VMX 与物理网卡。
- 修改 VMX 前必须确认虚拟机已完全停止，并先做可恢复备份。
- 桥接筛选会影响同一 Windows 主机上使用“自动桥接”的其他 VMware 虚拟机；
  文档必须明确说明影响及恢复命令。
- 第二块 Ubuntu 网卡不得配置默认网关或 DNS，不得改变原 NAT 默认路由。
- SSH 和 sudo 密码只允许交互输入，不得作为参数、日志或文件内容保存。

## 成功标准

1. 脚本可被 PowerShell 解析，并提供完整参数帮助。
2. 重复运行不会重复增加 VMX 网卡或 NetworkManager profile。
3. VMX 修改前生成备份；已有桥接网卡时跳过关机和 VMX 写入。
4. Ubuntu 持久配置为目标 `/24` 地址、开机自动连接、无默认网关。
5. 脚本末尾明确验证：Ubuntu 指定接口 ping 树莓派，Windows ping/SSH
   访问 Ubuntu 新地址。
6. 文档包含先决条件、标准命令、交互提示、验证、故障排查和回滚步骤。

## 验证级别

- `static`：PowerShell AST 语法检查、脚本帮助、参数和危险操作审阅。
- 不再次改动当前已配置成功的虚拟机网络；只读方式验证脚本默认值与当前环境匹配。

