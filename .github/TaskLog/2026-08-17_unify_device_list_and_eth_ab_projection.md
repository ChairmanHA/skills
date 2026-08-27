# Unify Device List and ETH A/B projection

日期：2026-08-17  
状态：已完成  
验证级别：`static`

## Scope

将 USB/ETH 的正常设备选择统一到 Device List；`ETH Connect` 只负责按用户输入创建或打开 manual ETH。成功获得 UID 的 manual ETH 继续由 `DeviceManager` 注册并显示在 Device List。

同 IP 下端口 5000/5001 的产品配对规则只用于标题栏 A/B 快捷按钮：A 固定为 5000，B 固定为 5001。按当前产品假设，不处理同时存在两组完整 5000/5001 pair 的选择问题。

## Decisions

- 保留产品当前只允许手工连接端口 5000/5001 的校验。
- 删除“所有 manual ETH 必须使用同一 IP”的全局限制；不同 IP 的 ETH 可分别连接并进入 Device List。
- endpoint 仍按 `{IP, Port}` 精确复用，不重配已有设备对象。
- 设备切换 retain-open 不再依赖 ETH pair：任意已打开旧设备切到非空目标时都保留 handle。Streaming 仍由 profile coordinator 在切换前停止。
- 标题栏每次从 `DeviceManager::allDevices()` 重新查找同 IP、同时 open 的 5000/5001 pair；不再使用“前两个 open ETH”隐式配对。
- profile、ownership、scanner、manual ETH provisional/remembered 生命周期保持原有边界。

## Success criteria

- 用户可以依次连接不同 IP 的 manual ETH；成功设备都显示在 Device List。
- Device List 的 USB/ETH 继续使用同一 `selectDevice()` / runtime profile coordinator。
- 任意 open USB/ETH 切到非空目标时不关闭旧设备，普通 Playback 可继续自主输出。
- 标题栏只在同一 IP 的 5000 与 5001 均已 open 时显示 A/B；A=5000，B=5001。
- 单台 ETH、不同 IP 的两个 ETH、或只有同 IP 的单端口设备都不显示 A/B。
- 删除生命周期层的 `isCoResidentEthPair()`，5000/5001 pairing 不再影响 DeviceManager。
- `git diff --check` 和静态引用检查通过；按仓库规则不编译、不运行。

## Result

- `DeviceManager` 对任意已打开旧设备切到非空目标统一 retain，不再判断 USB/ETH 或 ETH endpoint pair。
- 手动 ETH 保留 5000/5001 端口校验和 `{IP, Port}` 精确复用，删除不同 IP 拒绝逻辑。
- Device List 继续展示全部 USB 和已成功获得 UID 的 manual ETH；UID=0 provisional ETH 仍不展示。
- 标题栏 A/B 仅从同 IP 且同时 open 的 5000/5001 pair 生成，A=5000，B=5001。
- 静态检查通过：`git diff --check` 无 whitespace error，旧 pair 生命周期函数和同 IP 错误文案均无引用。
- 按本仓库默认验证规则未编译、未运行。
