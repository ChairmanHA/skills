# 2026-04-09 ETH Manual Connect Minimal Plan

## Goal

- 在不改变现有 USB 发现链路的前提下，为 HTRA 增加一个手工 ETH 连接入口。
- 当前阶段不做 ETH 自动发现，不做树莓派配网，不做主机名解析。
- 保持现有 DeviceManager / BusinessManager / MainWindowDeviceController 主链路可复用。

## Fixed Assumptions

- 树莓派与 Windows 主机通过网线直连。
- 树莓派当前 IP 固定为 192.168.1.100。
- 默认端口固定为 5000。
- 默认读超时固定为 2000 ms。
- `device_open_eth(void** device, eth_setting settings, ...)` 的 `settings` 为按值传递，因此 `eth_errorcode` 不作为上层交互输入，也不应被当成可回写输出。

## Non-Goals

- 不改 USB scanner 语义。
- 不把 ETH 伪装成可发现设备。
- 不在本阶段实现广播发现、ARP 扫描、mDNS、DHCP 协商。
- 不在本阶段提供 IPv6、接口类型切换、高级网络设置页面。

## Product Behavior

### Device 菜单

- 当前 `Device` 菜单拆成两个入口：
  - `USB Connect`：保留现有已发现 USB 设备列表。
  - `ETH Connect`：单个 action，点击后打开手工连接弹窗。
- 不把 ETH 目标混入现有 USB 列表。
- `USB Connect` 仍由扫描结果动态填充。

### ETH Connect 弹窗

- 初版只包含两个必填字段：
  - `IP Address`
  - `Port`
- 初始值：
  - IP = `192.168.1.100`
  - Port = `5000`
- 弹窗底部按钮：
  - `Cancel`
  - `Connect`
- 弹窗内容区增加一行状态文本：
  - 空闲时为空。
  - 连接中显示 `Connecting 192.168.1.100:5000...`
  - 失败时显示设备返回错误文案。

### 输入校验

- IP 仅接受合法 IPv4 文本。
- Port 仅接受 1 到 65535。
- 任一字段非法时，`Connect` 按钮 disabled。
- 不在点击 `Connect` 后才做第一轮校验；应实时校验。

### 连接中状态

- 点击 `Connect` 后弹窗不关闭。
- 进入 `Connecting` 状态时：
  - IP / Port 输入框 disabled。
  - `Connect` 按钮 disabled。
  - `Cancel` 按钮也 disabled。
  - 状态文本切换为 connecting 提示。
- 成功后：
  - 弹窗自动关闭。
  - 状态栏更新为更明确的 ETH 连接信息。
- 失败后：
  - 弹窗保持打开。
  - 输入框恢复 editable。
  - `Connect` 按钮重新根据当前校验结果决定是否 enabled。
  - 状态文本显示错误信息，允许用户直接修改并重试。

## Keyboard Choice

### Recommendation

- 暂时只支持物理键盘输入.
### Reason

1. `TouchNumKeyboard` 当前是“数值 + 单位适配器”体系，主路径围绕 `BaseUnitAdapter` 和单位语义设计，更适合频率、时间、功率输入，而不是 IP 文本。
2. IP 地址虽然表面上只有数字和点，但语义上仍是字符串，而不是当前 numeric property / unit adapter 体系中的数值输入。
3. Port 虽然是纯数字，但为了减少第一版复杂度，建议与 IP 保持同一输入部件和同一键盘体系，只通过 validator 限制合法性。

### Follow-up Option

- 若后续确认设备主要运行在纯触摸场景，且通用文本键盘效率不足，再单独做一个 `IPv4Keyboard` 或一个“去单位化的数字点号键盘”。
- 这个优化不应阻塞第一版 ETH 接入。

## Minimal Architecture Change

### Key Principle

- ETH 是 manual-managed connection target，不是 scanner-managed discovered device。

### Why

- 当前 `DeviceManager` 的快照对账会删除“不在最新扫描结果中的注册设备”，并且会优先删除 UID 为 0 的设备。
- 手工创建的 ETH 设备如果直接按当前 scanner-managed 语义注册，下一轮 USB 扫描会把它清掉。

### Minimal Fix

- 给 `IDevice` 增加一个“是否受 scanner snapshot 管理”的虚接口，例如：
  - `virtual bool managedByScannerSnapshot() const { return true; }`
- USB `FancyDevice` 默认保持 `true`。
- 手工 ETH 设备 override 为 `false`。
- `DeviceManagerPrivate::onDevicesDiscovered()` 的 reconcile 逻辑仅对 `managedByScannerSnapshot() == true` 的设备执行：
  - `uid == 0` 删除
  - `snapshot missing` 删除
- 这样 ETH 设备可与 USB 设备共存于同一个 `registeredDevice` 列表，但生命周期不再受 USB 扫描快照支配。

## Driver Design

### Recommendation

- 不新建第二个完整驱动类，先在 `FancyDevice` 内增加传输模式和手工 ETH 打开参数。

### New Internal State

- 增加 `TransportKind`：
  - `UsbDiscovered`
  - `EthManual`
- 增加 ETH 打开参数缓存：
  - IPv4 string
  - port
  - timeoutMs
- 增加手工初始化函数，例如：
  - `initializeManualEthTarget(const QString &ip, quint16 port, int timeoutMs)`

### Open Behavior

- `TransportKind == UsbDiscovered`：沿用 `device_open_usb(...)`。
- `TransportKind == EthManual`：构造 `eth_setting` 后调用 `device_open_eth(...)`。
- ETH 路径成功后设置：
  - `m_deviceInfo.PhysicalInterface = "ETH"`
  - `m_deviceName` 建议显示为 `ETH 192.168.1.100:5000`
- ETH 路径失败时，沿用现有 `translateErrorCode()` 和 `Device Open Failed` 语义。

### Why Not a Separate HTRAEthDevice for Phase 1

- 独立类会复制大量 `FancyDevice` 里的公共设备能力逻辑：`configuration()`、GNSS、trigger、实时状态查询、close/reset、错误翻译。
- 当前差异主要只是 open 参数来源和连接入口。
- 第一版先把变化压缩到 transport 维度更稳。

## UI Structure

### Files to Touch

- `src/plugins/core/mainwindowdevicecontroller.h/.cpp`
- `src/plugins/core/deviceinfowidget.cpp`
- `src/plugins/core/devicemanager.cpp`
- `src/plugins/core/idevice.h`
- `src/plugins/htra/fancydevice.h/.cpp`
- 新增 `src/plugins/core/ethconnectdialog.h/.cpp/.ui` 或无 ui 版本

### MainWindowDeviceController Responsibilities

- 现有 `Connect` 子菜单改名或替换为 `USB Connect`。
- 新增 `ETH Connect` action。
- 新增打开弹窗的 slot。
- 接收弹窗发出的 connect 请求，构造 manual ETH `FancyDevice`，调用 `DeviceManager::setCurrentDevice(device)`。
- 监听设备打开结果，驱动弹窗成功关闭或失败回写。

## ETH Dialog State Machine

### States

- `Idle`
- `Connecting`
- `Failed`

### Idle

- 字段可编辑。
- `Connect` 由 validator 驱动。
- 可点击 `Cancel` 关闭。

### Connecting

- 字段 disabled。
- `Connect` disabled。
- `Cancel` disabled。
- 第一版不引入 request cancel 语义，避免“弹窗已关闭但后台连接成功后 currentDevice 被悄然切换”的状态歧义。

### Failed

- 字段重新 enabled。
- 状态文本显示错误。
- 用户可以改 IP / Port 后再次点击 `Connect`。

## Open Result Routing

### Problem

- 现有 `Device Open Failed` 主要走全局 MessageDialog。
- ETH Connect 第一版要求“失败尽量在弹窗内展示并允许重试”。

### Minimal Handling

- `ETHConnectDialog` 不直接调用驱动 open；它只发起连接请求。
- `MainWindowDeviceController` 记录一个 `pendingEthDevice` 与当前打开的 `ETHConnectDialog`。
- 当 `currentDeviceOpenStateChanged(true)` 且 current device == pendingEthDevice：
  - 视为 ETH 连接成功。
  - 关闭弹窗。
- 当收到 `systemMessage(Device Open Failed, message)` 且 current device == pendingEthDevice：
  - 若弹窗仍在，优先把错误文本回写到弹窗内。
  - 同时 suppress 本次全局错误框，避免双重提示。
  - 清理 pending 标记。

### Scope Control

- 只对“由 ETH Connect 弹窗发起的当前请求”做局部拦截。
- USB 失败仍沿用全局弹窗。

## Status Bar Behavior

### Current Gap

- `DeviceInfoWidget` 当前 ETH 仅显示 `ETH:`，信息量不够。

### Minimal Improvement

- `DeviceInfo` 增加一项字符串，例如 `EndpointDisplay` 或 `ConnectionDisplay`。
- ETH 成功连接后填入 `192.168.1.100:5000`。
- `DeviceInfoWidget::updateDeviceInfo()` 中：
  - USB 继续显示现有 `Connected USBx:` 文案。
  - ETH 显示 `Connected ETH 192.168.1.100:5000`。

## Validation Details

### IP

- 使用 `QHostAddress` 做 IPv4 合法性校验。
- 仅接受 `QAbstractSocket::IPv4Protocol`。
- 禁止空字符串。

### Port

- `QIntValidator(1, 65535)`。
- 空字符串视为非法。

### Default Restoration

- 弹窗可选提供 `Reset Default`，但不是首版必需。
- 第一版只需预填默认值即可。

## Minimal Implementation Steps

1. 新增 `IDevice::managedByScannerSnapshot()`，修改 `DeviceManager` reconcile 仅删除 scanner-managed 设备。
2. 在 `FancyDevice` 中增加 `TransportKind` 和手工 ETH 初始化能力，打通 `device_open_eth(...)`。
3. 新增 `ETHConnectDialog`，实现字段、validator、状态文本和 `Connecting` 状态。
4. 修改 `MainWindowDeviceController`：
   - `Connect` 菜单改为 `USB Connect`
   - 新增 `ETH Connect` action
   - 管理 `pendingEthDevice` 与弹窗回写
5. 修改 `DeviceInfo` / `DeviceInfoWidget`，让 ETH 成功后状态栏显示 endpoint。
6. 仅做静态构建验证；若需要再补一轮运行验证。

## Risk

- 若直接复用全局 `Device Open Failed` 而不做局部拦截，ETH 失败时会出现“弹窗内错误 + 全局错误框”重复提示。
- 若不做 scanner-managed 区分，ETH 设备会在下一轮 USB 扫描中被注销。
- 若第一版强行复用 `TouchNumKeyboard`，会把 IP 输入绑进 numeric unit adapter 体系，改动和风险都会明显放大。

## Verification Checklist

1. 打开 `Device` 菜单时能看到 `USB Connect` 和 `ETH Connect`。
2. `ETH Connect` 弹窗初始值正确。
3. 非法 IP / Port 时 `Connect` disabled。
4. 点击 `Connect` 后进入 connecting 状态，字段不可编辑。
5. 成功连接后弹窗自动关闭，状态栏显示明确 ETH endpoint。
6. 失败时弹窗保留并显示错误，可直接修改重试。
7. ETH 已连接时，USB scanner 定时扫描不会把当前 ETH 设备移除。